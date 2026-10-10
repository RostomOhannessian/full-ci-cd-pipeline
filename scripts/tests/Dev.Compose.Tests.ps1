#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003, REQ-IDN-001

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Compose.psm1') -Force

    function ConvertTo-Jwt {
        param([hashtable] $Claims)
        $encode = { param($text) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text)).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
        return "$(& $encode '{"alg":"none"}').$(& $encode ($Claims | ConvertTo-Json -Compress)).signature"
    }

    $script:Environment = [ordered]@{
        KEYCLOAK_PORT = '8180'; KEYCLOAK_ADMIN_USER = 'admin'; KEYCLOAK_ADMIN_PASSWORD = 'pw'
        CATALOG_SMOKE_TESTS_SECRET = 's1'; CATALOG_SYNTHETIC_SHOPPER_SECRET = 's2'
    }
}

Describe 'ConvertFrom-DevJwt' {
    It 'reads the claims of a token, including a payload that needs base64url padding' {
        $token = ConvertTo-Jwt @{ iss = 'http://localhost:8180/realms/catalog'; aud = 'catalog-api'; scope = 'catalog.read'; note = 'padding?' }

        $claims = ConvertFrom-DevJwt -Token $token

        $claims.iss | Should -Be 'http://localhost:8180/realms/catalog'
        $claims.scope | Should -Be 'catalog.read'
    }

    It 'rejects a value that is not a JWT' {
        { ConvertFrom-DevJwt -Token 'not-a-token' } | Should -Throw '*three parts*'
    }
}

Describe 'Get-DevComposeArgument' {
    It 'points Compose at the repository, so the command works from any folder' {
        $arguments = Get-DevComposeArgument -RepositoryRoot (Join-Path $TestDrive 'repo')

        $arguments[0] | Should -Be 'compose'
        $arguments | Should -Contain '--project-directory'
        $arguments[-1] | Should -BeLike '*compose.yaml'
    }
}

Describe 'Invoke-DevCompose' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "stack-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $script:Root | Out-Null
        Set-Content -Path (Join-Path $script:Root '.env.example') -Value "PORT=1`nSECRET="
        Mock -ModuleName Dev.Compose Invoke-DevNativeLive { 0 }
        Mock -ModuleName Dev.Compose Write-DevStackEndpoint { }
    }

    It 'refuses to start the stack without credentials, and starts nothing' {
        $code = Invoke-DevCompose -Action up -RepositoryRoot $script:Root 6>$null

        $code | Should -Be 1
        Should -Invoke -ModuleName Dev.Compose Invoke-DevNativeLive -Times 0 -Exactly
    }

    It 'starts the stack and waits for every service to be healthy' {
        Set-Content -Path (Join-Path $script:Root '.env') -Value "PORT=1`nSECRET=x"

        $code = Invoke-DevCompose -Action up -RepositoryRoot $script:Root 6>$null

        $code | Should -Be 0
        Should -Invoke -ModuleName Dev.Compose Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter {
            $ArgumentList -contains 'up' -and $ArgumentList -contains '--wait' -and $ArgumentList -contains '--detach'
        }
    }

    It 'deletes the volumes only on reset' {
        Invoke-DevCompose -Action down -RepositoryRoot $script:Root 6>$null | Out-Null
        Invoke-DevCompose -Action reset -RepositoryRoot $script:Root 6>$null | Out-Null

        Should -Invoke -ModuleName Dev.Compose Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'down' -and $ArgumentList -notcontains '--volumes' }
        Should -Invoke -ModuleName Dev.Compose Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'down' -and $ArgumentList -contains '--volumes' }
    }

    It 'returns the exit code of Compose' {
        Mock -ModuleName Dev.Compose Invoke-DevNativeLive { 17 }

        Invoke-DevCompose -Action ps -RepositoryRoot $script:Root 6>$null | Should -Be 17
    }

    It 'passes extra words to Compose, such as a service name for the logs' {
        Invoke-DevCompose -Action logs -Rest @('keycloak') -RepositoryRoot $script:Root 6>$null | Out-Null

        Should -Invoke -ModuleName Dev.Compose Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'logs' -and $ArgumentList[-1] -eq 'keycloak' }
    }
}

Describe 'Test-DevStackHealth' {
    It 'passes when the four services are running and healthy' {
        Mock -ModuleName Dev.Compose Invoke-DevNative {
            [pscustomobject]@{ ExitCode = 0; Found = $true; Output = @(
                    '{"Service":"sqlserver","State":"running","Health":"healthy"}'
                    '{"Service":"valkey","State":"running","Health":"healthy"}'
                    '{"Service":"keycloak","State":"running","Health":"healthy"}'
                    '{"Service":"otel-lgtm","State":"running","Health":"healthy"}') }
        }

        (Test-DevStackHealth).Status | Should -Be 'ok'
    }

    It 'fails and names a service that is not healthy, one that exited, and one that was never created' {
        Mock -ModuleName Dev.Compose Invoke-DevNative {
            [pscustomobject]@{ ExitCode = 0; Found = $true; Output = @(
                    '{"Service":"sqlserver","State":"running","Health":"starting"}'
                    '{"Service":"valkey","State":"exited","Health":""}'
                    '{"Service":"keycloak","State":"running","Health":"healthy"}') }
        }

        $check = Test-DevStackHealth

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*sqlserver is starting*'
        $check.Message | Should -BeLike '*valkey is exited*'
        $check.Message | Should -BeLike '*otel-lgtm is not created*'
    }

    It 'fails when Compose cannot list the stack' {
        Mock -ModuleName Dev.Compose Invoke-DevNative { [pscustomobject]@{ ExitCode = 1; Found = $true; Output = @('error') } }

        (Test-DevStackHealth).Status | Should -Be 'fail'
    }
}

Describe 'Test-DevValkey' {
    BeforeEach {
        # A server that behaves as the ACL in compose.yaml says: the default user is off, and the application user has the narrow rights.
        Mock -ModuleName Dev.Compose Get-DevValkeyReply {
            $line = $Command -join ' '
            if ((Get-Variable -Name As -ValueOnly -ErrorAction SilentlyContinue) -eq 'anonymous') { return 'NOAUTH Authentication required.' }
            switch -Wildcard ($line) {
                'HMSET v2:catalog:dev:*' { 'OK' }
                'HMGET v2:catalog:dev:*' { 'ok' }
                'UNLINK *' { '1' }
                'PUBLISH catalog:dev.Backplane:v2 *' { '0' }
                default { "NOPERM User catalog-dev has no permissions to run '$($Command[0])'" }
            }
        }
    }

    It 'passes a server that enforces the ACL' {
        (Test-DevValkey).Status | Should -Be 'ok'
    }

    It 'fails when the default user is not switched off' {
        Mock -ModuleName Dev.Compose Get-DevValkeyReply { if ((Get-Variable -Name As -ValueOnly -ErrorAction SilentlyContinue) -eq 'anonymous') { 'PONG' } else { 'OK' } }

        $check = Test-DevValkey

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*default user is not switched off*'
    }

    It 'fails when the application user can reach a key or a channel outside the catalog prefix, or run a command it should not' {
        Mock -ModuleName Dev.Compose Get-DevValkeyReply {
            if ((Get-Variable -Name As -ValueOnly -ErrorAction SilentlyContinue) -eq 'anonymous') { return 'NOAUTH' }
            if ($Command[0] -eq 'HMGET') { return 'ok' }
            if ($Command[0] -eq 'PUBLISH' -and $Command[1] -like 'catalog:*') { return '0' }
            if ($Command[0] -in 'UNLINK') { return '1' }
            return 'OK'
        }

        $check = Test-DevValkey

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*write a key outside the catalog prefix*'
        $check.Message | Should -BeLike '*flush the database*'
        $check.Message | Should -BeLike '*list the ACL*'
        $check.Message | Should -BeLike '*publish to a channel outside the catalog prefix*'
    }

    It 'fails when the application user cannot use its own keys' {
        Mock -ModuleName Dev.Compose Get-DevValkeyReply { if ((Get-Variable -Name As -ValueOnly -ErrorAction SilentlyContinue) -eq 'anonymous') { 'NOAUTH' } else { 'NOPERM no' } }

        $check = Test-DevValkey

        $check.Message | Should -BeLike '*cannot write its own key*'
        $check.Message | Should -BeLike '*cannot publish to the backplane channel*'
    }

    It 'refuses a probe word that could carry a second command' {
        { Invoke-DevValkey -Command @('PING; FLUSHALL') } | Should -Throw '*not allowed in a probe*'
    }
}

Describe 'Test-DevKeycloak' {
    BeforeEach {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { [pscustomobject]@{ issuer = 'http://localhost:8180/realms/catalog' } }
        Mock -ModuleName Dev.Compose Get-DevToken {
            switch ($ClientId) {
                'smoke-tests' { [pscustomobject]@{ Ok = $true; Error = $null; Claims = [pscustomobject]@{ aud = 'catalog-api'; scope = 'catalog.read catalog.write catalog.admin' } } }
                'synthetic-shopper' {
                    if ((Get-Variable -Name Scope -ValueOnly -ErrorAction SilentlyContinue)) { [pscustomobject]@{ Ok = $false; Error = 'invalid_scope'; Claims = $null } }
                    else { [pscustomobject]@{ Ok = $true; Error = $null; Claims = [pscustomobject]@{ aud = 'catalog-api'; scope = 'catalog.read' } } }
                }
            }
        }
        Mock -ModuleName Dev.Compose Get-DevUserRole {
            [pscustomobject]@{ User = 'reader'; Roles = @('catalog-reader'); Audience = 'catalog-api'; Scope = '' }
            [pscustomobject]@{ User = 'editor'; Roles = @('catalog-editor', 'catalog-reader'); Audience = 'catalog-api'; Scope = '' }
            [pscustomobject]@{ User = 'admin'; Roles = @('catalog-admin', 'catalog-editor', 'catalog-reader'); Audience = 'catalog-api'; Scope = '' }
        }
    }

    It 'passes a realm that issues the documented tokens' {
        (Test-DevKeycloak -Environment $script:Environment).Status | Should -Be 'ok'
    }

    It 'fails when the issuer is not the fixed localhost issuer' {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { [pscustomobject]@{ issuer = 'http://keycloak:8080/realms/catalog' } }

        (Test-DevKeycloak -Environment $script:Environment).Message | Should -BeLike '*fixed issuer*'
    }

    It 'fails when the realm does not answer' {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { throw 'connection refused' }

        (Test-DevKeycloak -Environment $script:Environment).Status | Should -Be 'fail'
    }

    It 'fails when the audience is missing from a token' {
        Mock -ModuleName Dev.Compose Get-DevToken { [pscustomobject]@{ Ok = $true; Error = $null; Claims = [pscustomobject]@{ aud = 'account'; scope = 'catalog.read' } } }

        (Test-DevKeycloak -Environment $script:Environment).Message | Should -BeLike '*lacks aud=catalog-api*'
    }

    It 'fails when the read-only client is granted a broader scope' {
        Mock -ModuleName Dev.Compose Get-DevToken {
            if ($ClientId -eq 'smoke-tests') { return [pscustomobject]@{ Ok = $true; Error = $null; Claims = [pscustomobject]@{ aud = 'catalog-api'; scope = 'catalog.read catalog.write catalog.admin' } } }
            [pscustomobject]@{ Ok = $true; Error = $null; Claims = [pscustomobject]@{ aud = 'catalog-api'; scope = 'catalog.read catalog.admin' } }
        }

        (Test-DevKeycloak -Environment $script:Environment).Message | Should -BeLike '*beyond catalog.read*'
    }

    It 'fails when a user holds a role that the realm file does not give them' {
        Mock -ModuleName Dev.Compose Get-DevUserRole {
            [pscustomobject]@{ User = 'reader'; Roles = @('catalog-admin', 'catalog-editor', 'catalog-reader'); Audience = 'catalog-api'; Scope = '' }
            [pscustomobject]@{ User = 'editor'; Roles = @('catalog-editor', 'catalog-reader'); Audience = 'catalog-api'; Scope = '' }
            [pscustomobject]@{ User = 'admin'; Roles = @('catalog-admin', 'catalog-editor', 'catalog-reader'); Audience = 'catalog-api'; Scope = '' }
        }

        (Test-DevKeycloak -Environment $script:Environment).Message | Should -BeLike "*reader gets the roles 'catalog-admin, catalog-editor, catalog-reader'*"
    }

    It 'fails, without throwing, when the administrator API cannot be reached' {
        Mock -ModuleName Dev.Compose Get-DevUserRole { throw 'Unauthorized' }

        (Test-DevKeycloak -Environment $script:Environment).Message | Should -BeLike '*demo users could not be checked*'
    }
}

Describe 'Get-DevToken' {
    It 'returns the claims of an issued token' {
        $text = ConvertTo-Jwt @{ scope = 'catalog.read'; aud = 'catalog-api' }
        Mock -ModuleName Dev.Compose Invoke-RestMethod ([scriptblock]::Create("[pscustomobject]@{ access_token = '$text' }"))

        $result = Get-DevToken -Issuer 'http://localhost:8180/realms/catalog' -ClientId 'c' -Secret 's'

        $result.Ok | Should -BeTrue
        $result.Claims.scope | Should -Be 'catalog.read'
    }

    It 'returns a refusal as an error value and does not throw' {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { throw 'HTTP 400' }

        $result = Get-DevToken -Issuer 'http://localhost:8180/realms/catalog' -ClientId 'c' -Secret 's'

        $result.Ok | Should -BeFalse
        $result.Error | Should -BeLike '*400*'
    }
}

Describe 'Test-DevTelemetry' {
    It 'passes when Grafana is healthy and the OTLP endpoint accepts an export' {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { [pscustomobject]@{ database = 'ok' } }
        Mock -ModuleName Dev.Compose Invoke-WebRequest { [pscustomobject]@{ StatusCode = 200 } }

        (Test-DevTelemetry -Environment @{ GRAFANA_PORT = '3000'; OTLP_HTTP_PORT = '4318' }).Status | Should -Be 'ok'
    }

    It 'fails when Grafana does not answer' {
        Mock -ModuleName Dev.Compose Invoke-RestMethod { throw 'refused' }

        (Test-DevTelemetry -Environment @{ GRAFANA_PORT = '3000'; OTLP_HTTP_PORT = '4318' }).Status | Should -Be 'fail'
    }
}

Describe 'Invoke-DevComposeCheck' {
    It 'stops at an unhealthy stack and reports only that, because the other checks would only repeat it' {
        Mock -ModuleName Dev.Compose Test-DevStackHealth { [pscustomobject]@{ Name = 'Containers'; Status = 'fail'; Message = 'keycloak is starting'; Fix = '' } }
        Mock -ModuleName Dev.Compose Test-DevValkey { throw 'must not run' }

        $checks = @(Invoke-DevComposeCheck -RepositoryRoot $TestDrive)

        $checks | Should -HaveCount 1
        $checks[0].Status | Should -Be 'fail'
    }
}
