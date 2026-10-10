Set-StrictMode -Version Latest

# `dev compose`: starts, stops, resets, and checks the API-only stack in compose.yaml (the `api` profile, ADR-0019 and ADR-0026). The check
# proves what the plan says about the stack: each service is healthy and answers on the loopback interface, Valkey enforces its ACL, Keycloak
# issues the tokens that the realm file describes, and the telemetry stack accepts OTLP.

Import-Module (Join-Path $PSScriptRoot 'Dev.Common.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Env.psm1')

$script:Services = @('sqlserver', 'valkey', 'keycloak', 'otel-lgtm')

# What the catalog realm must produce for each demo user when the broadest scope request is made. Keycloak 26.8 does not withhold a requested
# scope from a user who lacks the mapped role (ADR-0026), so the role claim is the proof: it holds only the roles that the user has.
$script:UserRoleExpectation = [ordered]@{
    reader = @('catalog-reader')
    editor = @('catalog-editor', 'catalog-reader')
    admin  = @('catalog-admin', 'catalog-editor', 'catalog-reader')
}

function Get-DevComposeArgument {
    <#
    .SYNOPSIS
        Returns the arguments that point Docker Compose at this repository, so a command works from any folder.
    #>
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    return @('compose', '--project-directory', $RepositoryRoot, '--file', (Join-Path $RepositoryRoot 'compose.yaml'))
}

function Invoke-DevCompose {
    <#
    .SYNOPSIS
        Runs one `dev compose` action and returns its exit code.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [ValidateSet('up', 'down', 'reset', 'ps', 'logs')] [string] $Action,
        [string[]] $Rest = @(),
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $base = Get-DevComposeArgument -RepositoryRoot $RepositoryRoot

    if ($Action -eq 'up') {
        $state = Test-DevEnvironment -TemplatePath (Join-Path $RepositoryRoot '.env.example') -Path (Join-Path $RepositoryRoot '.env')
        if (-not $state.Exists -or $state.Missing.Count -gt 0) {
            Write-DevMessage -Level fail -Message 'The environment file is missing or incomplete, so the stack has no credentials. Run: ./dev bootstrap'
            return 1
        }
    }

    switch ($Action) {
        'up' {
            Write-DevMessage -Level info -Message 'Starting SQL Server, Valkey, Keycloak, and Grafana with OpenTelemetry. The first run pulls about 3 GB of images.'
            Write-DevMessage -Level info -Message 'SQL Server runs as the Developer edition, and starting it accepts Microsoft''s license terms for development and test use.'
            $code = Invoke-DevNativeLive -FilePath 'docker' -ArgumentList ($base + @('up', '--detach', '--wait', '--wait-timeout', '300') + $Rest)
            if ($code -eq 0) { Write-DevStackEndpoint -RepositoryRoot $RepositoryRoot }
            return $code
        }
        'down' { return Invoke-DevNativeLive -FilePath 'docker' -ArgumentList ($base + @('down', '--remove-orphans') + $Rest) }
        'reset' {
            Write-DevMessage -Level warn -Message 'Deleting the stack and its data volumes. The data is disposable (plan section 6), and the next "up" starts clean.'
            return Invoke-DevNativeLive -FilePath 'docker' -ArgumentList ($base + @('down', '--volumes', '--remove-orphans') + $Rest)
        }
        'ps' { return Invoke-DevNativeLive -FilePath 'docker' -ArgumentList ($base + @('ps') + $Rest) }
        'logs' { return Invoke-DevNativeLive -FilePath 'docker' -ArgumentList ($base + @('logs', '--tail', '200') + $Rest) }
    }
}

function Write-DevStackEndpoint {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'The dev command is an interactive tool, and its endpoint list is its output.')]
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $values = Read-DevEnvFile -Path (Join-Path $RepositoryRoot '.env')
    $port = { param($name, $default) if ($values.Contains($name) -and $values[$name]) { $values[$name] } else { $default } }
    Write-DevMessage -Level ok -Message 'The stack is healthy. Everything listens on 127.0.0.1 only:'
    Write-Host "       SQL Server  localhost,$(& $port 'SQL_PORT' 1433)  (login sa, password in .env)"
    Write-Host "       Valkey      localhost:$(& $port 'VALKEY_PORT' 6379)  (ACL users; the default user is off)"
    Write-Host "       Keycloak    http://localhost:$(& $port 'KEYCLOAK_PORT' 8180)/realms/catalog  (admin console: /admin)"
    Write-Host "       Grafana     http://localhost:$(& $port 'GRAFANA_PORT' 3000)  (admin, password in .env)"
    Write-Host "       OTLP        grpc localhost:$(& $port 'OTLP_GRPC_PORT' 4317), http localhost:$(& $port 'OTLP_HTTP_PORT' 4318)"
    Write-Host '       Next: ./dev compose check'
}

function ConvertFrom-DevJwt {
    <#
    .SYNOPSIS
        Decodes the payload of a JWT so a check can read its claims. It does not validate the signature, and the check is not a validator.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Token)

    $parts = $Token.Split('.')
    if ($parts.Count -ne 3) { throw 'The value is not a JWT: it does not have three parts.' }

    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    switch ($payload.Length % 4) { 2 { $payload += '==' } 3 { $payload += '=' } 1 { throw 'The JWT payload is not valid base64url.' } }
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
}

function Test-DevTcpPort {
    <#
    .SYNOPSIS
        Returns $true when a TCP connection to the loopback address succeeds within the timeout.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)] [int] $Port, [int] $TimeoutMilliseconds = 3000)

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync([System.Net.IPAddress]::Loopback, $Port)
        return $connect.Wait($TimeoutMilliseconds) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-DevStackEnvironment {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $template = Read-DevEnvFile -Path (Join-Path $RepositoryRoot '.env.example')
    $actual = Read-DevEnvFile -Path (Join-Path $RepositoryRoot '.env')
    $merged = [ordered]@{}
    foreach ($key in $template.Keys) { $merged[$key] = if ($actual.Contains($key) -and $actual[$key]) { $actual[$key] } else { $template[$key] } }
    return $merged
}

function Test-DevStackHealth {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $result = Invoke-DevNative -FilePath 'docker' -ArgumentList ((Get-DevComposeArgument -RepositoryRoot $RepositoryRoot) + @('ps', '--all', '--format', 'json'))
    if ($result.ExitCode -ne 0) {
        return Build-DevCheck -Name 'Containers' -Status fail -Message 'Docker Compose could not list the stack.' -Fix 'Run: ./dev doctor'
    }

    $rows = @($result.Output | Where-Object { $_ -match '^\s*\{' } | ForEach-Object { $_ | ConvertFrom-Json })
    $problems = foreach ($service in $script:Services) {
        $row = $rows | Where-Object { $_.Service -eq $service } | Select-Object -First 1
        if (-not $row) { "$service is not created" }
        elseif ($row.State -ne 'running') { "$service is $($row.State)" }
        elseif ($row.Health -ne 'healthy') { "$service is $($row.Health)" }
    }

    if ($problems) {
        return Build-DevCheck -Name 'Containers' -Status fail -Message ($problems -join '; ') -Fix 'Run: ./dev compose up, then ./dev compose logs <service>'
    }

    return Build-DevCheck -Name 'Containers' -Status ok -Message "$($script:Services -join ', ') are running and healthy."
}

function Invoke-DevComposeExec {
    <#
    .SYNOPSIS
        Runs a shell command inside a service container, and returns its exit code and output.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Service,
        [Parameter(Mandatory)] [string] $Script,
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    return Invoke-DevNative -FilePath 'docker' -ArgumentList ((Get-DevComposeArgument -RepositoryRoot $RepositoryRoot) + @('exec', '-T', $Service, 'sh', '-c', $Script))
}

function Test-DevSqlServer {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Environment, [string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $port = [int]$Environment['SQL_PORT']
    $login = Invoke-DevComposeExec -Service 'sqlserver' -RepositoryRoot $RepositoryRoot `
        -Script '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY(''ProductVersion'') AS varchar(32)) + '' '' + CAST(SERVERPROPERTY(''Edition'') AS varchar(64))"'
    if ($login.ExitCode -ne 0) {
        return Build-DevCheck -Name 'SQL Server' -Status fail -Message 'The sa login does not work inside the container.' -Fix 'If .env changed after the first start, run: ./dev compose reset'
    }
    if (-not (Test-DevTcpPort -Port $port)) {
        return Build-DevCheck -Name 'SQL Server' -Status fail -Message "Nothing accepts connections on 127.0.0.1:$port." -Fix 'Check the port in .env and run: ./dev compose up'
    }

    return Build-DevCheck -Name 'SQL Server' -Status ok -Message "Login works and 127.0.0.1:$port accepts connections ($((($login.Output | Where-Object { $_ }) -join ' ').Trim()))."
}

function Invoke-DevValkey {
    <#
    .SYNOPSIS
        Runs a Valkey command as the application user, or as nobody, inside the Valkey container. The password never leaves the container.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]] $Command,
        [ValidateSet('app', 'anonymous')] [string] $As = 'app',
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    foreach ($word in $Command) {
        if ($word -notmatch '^[A-Za-z0-9:._-]+$') { throw "The Valkey probe word '$word' has characters that are not allowed in a probe." }
    }

    $auth = if ($As -eq 'app') { '--user "$VALKEY_APP_USER" --pass "$VALKEY_APP_PASSWORD"' } else { '' }
    return Invoke-DevComposeExec -Service 'valkey' -RepositoryRoot $RepositoryRoot -Script "valkey-cli --no-auth-warning $auth $($Command -join ' ')"
}

function Get-DevValkeyReply {
    <#
    .SYNOPSIS
        Runs a Valkey command inside the container and returns the reply as one trimmed line of text.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string[]] $Command,
        [ValidateSet('app', 'anonymous')] [string] $As = 'app',
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $result = Invoke-DevValkey -Command $Command -As $As -RepositoryRoot $RepositoryRoot
    return (($result.Output | Where-Object { $_ }) -join ' ').Trim()
}

function Test-DevValkey {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $failures = [System.Collections.Generic.List[string]]::new()

    if ((Get-DevValkeyReply -Command @('PING') -As anonymous -RepositoryRoot $RepositoryRoot) -notmatch 'NOAUTH') { $failures.Add('the default user is not switched off') }

    # IDistributedCache stores a value as a hash, so the probe writes one the same way, to a key that the application user may use.
    $key = 'v2:catalog:dev:doctor-probe'
    if ((Get-DevValkeyReply -Command @('HMSET', $key, 'data', 'ok') -RepositoryRoot $RepositoryRoot) -ne 'OK') { $failures.Add('the application user cannot write its own key') }
    if ((Get-DevValkeyReply -Command @('HMGET', $key, 'data') -RepositoryRoot $RepositoryRoot) -ne 'ok') { $failures.Add('the application user cannot read its own key') }
    Get-DevValkeyReply -Command @('UNLINK', $key) -RepositoryRoot $RepositoryRoot | Out-Null

    foreach ($denied in @(
            @{ Why = 'write a key outside the catalog prefix'; Command = @('HMSET', 'other:key', 'data', 'x') },
            @{ Why = 'flush the database'; Command = @('FLUSHALL') },
            @{ Why = 'list the ACL'; Command = @('ACL', 'LIST') },
            @{ Why = 'publish to a channel outside the catalog prefix'; Command = @('PUBLISH', 'other', 'x') }
        )) {
        if ((Get-DevValkeyReply -Command $denied.Command -RepositoryRoot $RepositoryRoot) -notmatch 'NOPERM') { $failures.Add("the application user can $($denied.Why)") }
    }

    if ((Get-DevValkeyReply -Command @('PUBLISH', 'catalog:dev.Backplane:v2', 'doctor') -RepositoryRoot $RepositoryRoot) -notmatch '^\d+$') { $failures.Add('the application user cannot publish to the backplane channel') }

    if ($failures.Count -gt 0) {
        return Build-DevCheck -Name 'Valkey ACL' -Status fail -Message ($failures -join '; ') -Fix 'Compare the valkey-acl config in compose.yaml with spike 1.b, then run: ./dev compose reset'
    }

    return Build-DevCheck -Name 'Valkey ACL' -Status ok -Message 'The default user is off, and the application user reaches only its own keys and channels with the measured command set.'
}
function Get-DevToken {
    <#
    .SYNOPSIS
        Gets an access token with the client credentials grant and returns its claims. A refusal is returned as an error, not thrown.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Issuer,
        [Parameter(Mandatory)] [string] $ClientId,
        [Parameter(Mandatory)] [string] $Secret,
        [string] $Scope
    )

    $body = @{ grant_type = 'client_credentials'; client_id = $ClientId; client_secret = $Secret }
    if ($Scope) { $body['scope'] = $Scope }

    try {
        $response = Invoke-RestMethod -Method Post -Uri "$Issuer/protocol/openid-connect/token" -Body $body -TimeoutSec 15
        return [pscustomobject]@{ Ok = $true; Claims = (ConvertFrom-DevJwt -Token $response.access_token); Error = $null }
    }
    catch {
        $detail = if ($_.ErrorDetails) { ($_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue).error } else { $null }
        return [pscustomobject]@{ Ok = $false; Claims = $null; Error = if ($detail) { $detail } else { $_.Exception.Message } }
    }
}

function Get-DevUserRole {
    <#
    .SYNOPSIS
        Asks Keycloak for the access token it would issue a demo user for the broadest scope request, and returns the roles in it.
    .DESCRIPTION
        The realm has no flow that a script can run for a person, because the password grant is off. The administrator API can generate the
        example token, and it applies the same client scopes, mappers, and role scope mappings as a real login.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $KeycloakUrl,
        [Parameter(Mandatory)] [System.Net.NetworkCredential] $AdminCredential,
        [Parameter(Mandatory)] [string[]] $UserName,
        [string] $Realm = 'catalog',
        [string] $ClientId = 'catalog-cli'
    )

    $admin = Invoke-RestMethod -Method Post -Uri "$KeycloakUrl/realms/master/protocol/openid-connect/token" -TimeoutSec 15 `
        -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = $AdminCredential.UserName; password = $AdminCredential.Password }
    $headers = @{ Authorization = "Bearer $($admin.access_token)" }
    $client = @(Invoke-RestMethod -Headers $headers -TimeoutSec 15 -Uri "$KeycloakUrl/admin/realms/$Realm/clients?clientId=$ClientId")[0]
    $scope = [uri]::EscapeDataString('catalog.read catalog.write catalog.admin')

    foreach ($name in $UserName) {
        $user = @(Invoke-RestMethod -Headers $headers -TimeoutSec 15 -Uri "$KeycloakUrl/admin/realms/$Realm/users?username=$([uri]::EscapeDataString($name))&exact=true")[0]
        $token = Invoke-RestMethod -Headers $headers -TimeoutSec 15 -Uri "$KeycloakUrl/admin/realms/$Realm/clients/$($client.id)/evaluate-scopes/generate-example-access-token?userId=$($user.id)&scope=$scope"
        $roles = if ($token.PSObject.Properties['realm_access']) { @($token.realm_access.roles) } else { @() }
        [pscustomobject]@{ User = $name; Roles = @($roles | Sort-Object); Audience = $token.aud; Scope = "$($token.scope)" }
    }
}

function Test-DevKeycloak {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Environment)

    $port = $Environment['KEYCLOAK_PORT']
    $url = "http://localhost:$port"
    $issuer = "$url/realms/catalog"
    $failures = [System.Collections.Generic.List[string]]::new()

    try {
        $discovery = Invoke-RestMethod -Uri "$issuer/.well-known/openid-configuration" -TimeoutSec 15
    }
    catch {
        return Build-DevCheck -Name 'Keycloak realm' -Status fail -Message "The catalog realm does not answer at $issuer." -Fix 'Run: ./dev compose logs keycloak'
    }
    if ($discovery.issuer -ne $issuer) { $failures.Add("the issuer is '$($discovery.issuer)', and the fixed issuer is '$issuer'") }

    $smoke = Get-DevToken -Issuer $issuer -ClientId 'smoke-tests' -Secret $Environment['CATALOG_SMOKE_TESTS_SECRET']
    if (-not $smoke.Ok) { $failures.Add("smoke-tests cannot get a token ($($smoke.Error))") }
    else {
        if ("catalog-api" -notin @($smoke.Claims.aud)) { $failures.Add('the smoke-tests token lacks aud=catalog-api') }
        if ((@($smoke.Claims.scope -split ' ' | Sort-Object) -join ' ') -ne 'catalog.admin catalog.read catalog.write') { $failures.Add("the smoke-tests scopes are '$($smoke.Claims.scope)'") }
    }

    $shopper = Get-DevToken -Issuer $issuer -ClientId 'synthetic-shopper' -Secret $Environment['CATALOG_SYNTHETIC_SHOPPER_SECRET']
    if (-not $shopper.Ok) { $failures.Add("synthetic-shopper cannot get a token ($($shopper.Error))") }
    elseif ($shopper.Claims.scope -ne 'catalog.read') { $failures.Add("the synthetic-shopper scope is '$($shopper.Claims.scope)', and it must be catalog.read only") }

    $broad = Get-DevToken -Issuer $issuer -ClientId 'synthetic-shopper' -Secret $Environment['CATALOG_SYNTHETIC_SHOPPER_SECRET'] -Scope 'catalog.read catalog.write catalog.admin'
    if ($broad.Ok) { $failures.Add('synthetic-shopper was granted scopes beyond catalog.read') }

    try {
        $users = @(Get-DevUserRole -KeycloakUrl $url -AdminCredential ([System.Net.NetworkCredential]::new($Environment['KEYCLOAK_ADMIN_USER'], $Environment['KEYCLOAK_ADMIN_PASSWORD'])) -UserName @($script:UserRoleExpectation.Keys))
        foreach ($user in $users) {
            $expected = $script:UserRoleExpectation[$user.User]
            if (($user.Roles -join ',') -ne ($expected -join ',')) { $failures.Add("$($user.User) gets the roles '$($user.Roles -join ', ')', and must get '$($expected -join ', ')'") }
            if ('catalog-api' -notin @($user.Audience)) { $failures.Add("$($user.User) gets no aud=catalog-api") }
        }
    }
    catch {
        $failures.Add("the demo users could not be checked ($($_.Exception.Message))")
    }

    if ($failures.Count -gt 0) {
        return Build-DevCheck -Name 'Keycloak realm' -Status fail -Message ($failures -join '; ') -Fix 'Compare identity/keycloak/realms/catalog-realm.json with the expectations in scripts/modules/Dev.Compose.psm1, then run: ./dev compose reset'
    }

    return Build-DevCheck -Name 'Keycloak realm' -Status ok -Message "Issuer $issuer, aud=catalog-api, client scopes follow each client, and the demo users get only their roles."
}

function Test-DevTelemetry {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Environment)

    try {
        $health = Invoke-RestMethod -Uri "http://localhost:$($Environment['GRAFANA_PORT'])/api/health" -TimeoutSec 15
        if ($health.database -ne 'ok') { return Build-DevCheck -Name 'Telemetry' -Status fail -Message "Grafana reports its database as '$($health.database)'." }

        $otlp = Invoke-WebRequest -Method Post -Uri "http://localhost:$($Environment['OTLP_HTTP_PORT'])/v1/traces" -ContentType 'application/json' -Body '{"resourceSpans":[]}' -TimeoutSec 15 -UseBasicParsing
        if ($otlp.StatusCode -ne 200) { return Build-DevCheck -Name 'Telemetry' -Status fail -Message "The OTLP endpoint answered $($otlp.StatusCode)." }
    }
    catch {
        return Build-DevCheck -Name 'Telemetry' -Status fail -Message "Grafana or the OTLP endpoint does not answer ($($_.Exception.Message))." -Fix 'Run: ./dev compose logs otel-lgtm'
    }

    return Build-DevCheck -Name 'Telemetry' -Status ok -Message 'Grafana is healthy and the OTLP HTTP endpoint accepts a trace export.'
}

function Invoke-DevComposeCheck {
    <#
    .SYNOPSIS
        Runs every stack check and returns the results.
    #>
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $environment = Get-DevStackEnvironment -RepositoryRoot $RepositoryRoot
    $health = Test-DevStackHealth -RepositoryRoot $RepositoryRoot
    if ($health.Status -ne 'ok') { return @($health) }

    return @(
        $health
        Test-DevSqlServer -Environment $environment -RepositoryRoot $RepositoryRoot
        Test-DevValkey -RepositoryRoot $RepositoryRoot
        Test-DevKeycloak -Environment $environment
        Test-DevTelemetry -Environment $environment
    )
}

Export-ModuleMember -Function Get-DevComposeArgument, Invoke-DevCompose, ConvertFrom-DevJwt, Test-DevTcpPort, Get-DevStackEnvironment, Test-DevStackHealth, Invoke-DevComposeExec, Test-DevSqlServer, Invoke-DevValkey, Get-DevValkeyReply, Test-DevValkey, Get-DevToken, Get-DevUserRole, Test-DevKeycloak, Test-DevTelemetry, Invoke-DevComposeCheck
