#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-002, REQ-DX-005, REQ-IDN-005

BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $script:Compose = Get-Content -Path (Join-Path $script:Root 'compose.yaml') -Raw
    $script:ComposeCode = (Get-Content -Path (Join-Path $script:Root 'compose.yaml') | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
    $script:Services = [regex]::Match($script:ComposeCode, '(?ms)^services:\n(.*?)(?=^configs:|^volumes:)').Groups[1].Value
    $script:ComposeLines = Get-Content -Path (Join-Path $script:Root 'compose.yaml')
    $script:EnvExample = Get-Content -Path (Join-Path $script:Root '.env.example') -Raw

    $exampleNames = [regex]::Matches($script:EnvExample, '(?m)^([A-Z][A-Z0-9_]*)=') | ForEach-Object { $_.Groups[1].Value }
    $script:ExampleNames = @($exampleNames)
}

Describe 'compose.yaml, the API-only stack' {
    It 'pins every image by tag and digest' {
        $images = @($script:ComposeLines | Where-Object { $_ -match '^\s*image:' })

        $images.Count | Should -BeGreaterOrEqual 4
        foreach ($line in $images) { $line | Should -Match '@sha256:[0-9a-f]{64}\s*$' -Because $line }
        foreach ($line in $images) { $line | Should -Match ':[A-Za-z0-9][A-Za-z0-9._-]*@sha256:' -Because "$line needs a tag beside the digest, so Dependabot can propose an update" }
    }

    It 'runs the four services the api profile names' {
        foreach ($service in 'sqlserver', 'valkey', 'keycloak', 'otel-lgtm') {
            $script:Compose | Should -Match "(?m)^  $([regex]::Escape($service)):" -Because $service
        }
    }

    It 'publishes every port on the loopback interface, on a high port that needs no administrator rights' {
        $ports = @($script:ComposeLines | Where-Object { $_ -match '^\s*-\s*"[^"]*:\d+"\s*$' -and $_ -match '\$\{|127\.0\.0\.1' })

        $ports.Count | Should -BeGreaterOrEqual 6
        foreach ($line in $ports) {
            $line | Should -Match '"127\.0\.0\.1:\$\{[A-Z_]+:-(?<port>\d+)\}:\d+"' -Because $line
            [int][regex]::Match($line, ':-(\d+)\}').Groups[1].Value | Should -BeGreaterOrEqual 1024 -Because "$line must not need a privileged port"
        }
    }

    It 'never binds a port to every interface' {
        $script:Compose | Should -Not -Match '"0\.0\.0\.0:'
        @($script:ComposeLines | Where-Object { $_ -match '^\s*-\s*"\d+:\d+"' }) | Should -HaveCount 0
    }

    It 'has no credential in the file: every password and secret is read from the environment' {
        $sensitive = @($script:ComposeLines | Where-Object { $_ -match '^\s*[A-Z_]*(PASSWORD|SECRET)\s*:' -and $_ -notmatch '^\s*#' })

        $sensitive.Count | Should -BeGreaterThan 0
        foreach ($line in $sensitive) { $line | Should -Match ':\s*\$\{[A-Z_]+:\?' -Because "$line must require a value from .env" }
    }

    It 'reads only variables that .env.example lists' {
        $used = [regex]::Matches($script:ComposeCode, '(?<!\$)\$\{([A-Z][A-Z0-9_]*)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique

        $used.Count | Should -BeGreaterThan 8
        foreach ($name in $used) { $script:ExampleNames | Should -Contain $name -Because "compose.yaml reads $name" }
    }

    It 'gives every service a health check, so "up --wait" means the stack works' {
        $services = [regex]::Matches($script:Services, '(?ms)^  ([a-z-]+):\n(.*?)(?=^  [a-z-]+:\n|\z)')

        $services.Count | Should -Be 4
        foreach ($service in $services) { $service.Groups[2].Value | Should -Match 'healthcheck:' -Because $service.Groups[1].Value }
    }

    It 'limits the memory and the privileges of every service' {
        $services = [regex]::Matches($script:Services, '(?ms)^  ([a-z-]+):\n(.*?)(?=^  [a-z-]+:\n|\z)')

        foreach ($service in $services) {
            $service.Groups[2].Value | Should -Match 'mem_limit:' -Because "$($service.Groups[1].Value) needs a memory limit"
            $service.Groups[2].Value | Should -Match 'no-new-privileges:true' -Because "$($service.Groups[1].Value) must not gain privileges"
        }
    }

    It 'never mounts the Docker socket or runs a privileged container' {
        $script:Compose | Should -Not -Match 'docker\.sock'
        $script:Compose | Should -Not -Match '(?m)^\s*privileged:\s*true'
    }

    It 'switches off the default Valkey user and gives the application user a narrow ACL' {
        $acl = [regex]::Match($script:Compose, '(?ms)^configs:\r?\n.*?content: \|\r?\n(.*?)(?=^volumes:)').Groups[1].Value

        $acl | Should -Match '(?m)^\s+user default off'
        $appLine = ($acl -split '\r?\n' | Where-Object { $_ -match 'VALKEY_APP_USER' }) -join ' '
        $appLine | Should -Match '-@all'
        $appLine | Should -Not -Match '\+@all|\+@dangerous|\+flushall|\+flushdb|\+keys|\+eval|\+acl|\+config\|set|\+debug|\+shutdown'
        $appLine | Should -Match '~v2:catalog:dev:\*'
        $appLine | Should -Not -Match '~\*'
        $appLine | Should -Not -Match '&\*'
    }

    It 'mounts the realm file from identity/keycloak/realms as the only realm source, read-only' {
        $script:Compose | Should -Match '\./identity/keycloak/realms/catalog-realm\.json:/opt/keycloak/data/import/catalog-realm\.json:ro'
        @($script:ComposeLines | Where-Object { $_ -match 'import/' }) | Should -HaveCount 1
        $script:Compose | Should -Match '--import-realm'
    }

    It 'advertises one fixed localhost issuer for Keycloak' {
        $script:Compose | Should -Match '--hostname=http://localhost:\$\{KEYCLOAK_PORT:-8180\}'
    }
}

Describe '.env.example' {
    It 'lists variable names with defaults for the ports and no value for any secret' {
        foreach ($line in ($script:EnvExample -split '\r?\n' | Where-Object { $_ -match '^[A-Z]' })) {
            $name, $value = $line -split '=', 2
            if ($name -match 'PASSWORD$|SECRET$') { $value | Should -BeNullOrEmpty -Because "$name is a secret, and dev bootstrap generates it" }
            if ($name -match '_PORT$') { $value | Should -Match '^\d{4,5}$' -Because "$name is a port" }
        }
    }

    It 'binds the ports that the platform plan reserves for the API, 8080 and 8081, to nothing' {
        $script:EnvExample | Should -Not -Match '=(8080|8081)\s*$'
    }

    It 'is ignored when it is called .env, and tracked when it is called .env.example' {
        $ignore = Get-Content -Path (Join-Path $script:Root '.gitignore') -Raw

        $ignore | Should -Match '(?m)^\.env$'
        $ignore | Should -Match '(?m)^!\.env\.example$'
    }
}

Describe 'The dev shims' {
    It 'exist for Linux, macOS, and Windows' {
        Join-Path $script:Root 'dev' | Should -Exist
        Join-Path $script:Root 'dev.cmd' | Should -Exist
    }

    It 'start PowerShell as a local .NET tool, so the only prerequisites are Git, Docker, and the .NET SDK' {
        foreach ($shim in 'dev', 'dev.cmd') {
            $text = Get-Content -Path (Join-Path $script:Root $shim) -Raw
            $text | Should -Match 'dotnet tool restore' -Because $shim
            $text | Should -Match 'dotnet pwsh' -Because $shim
            $text | Should -Match 'scripts[/\\]dev\.ps1' -Because $shim
        }
    }

    It 'keep thin: they hold no logic beyond finding the SDK and starting the script' {
        (Get-Content -Path (Join-Path $script:Root 'dev')).Count | Should -BeLessThan 20
        (Get-Content -Path (Join-Path $script:Root 'dev.cmd')).Count | Should -BeLessThan 20
    }

    It 'use the line endings that .gitattributes asks for: LF for the shell shim and CRLF for the Windows one' {
        [System.IO.File]::ReadAllBytes((Join-Path $script:Root 'dev')) | Should -Not -Contain 13
        [System.IO.File]::ReadAllBytes((Join-Path $script:Root 'dev.cmd')) | Should -Contain 13
    }

    It 'start with a shebang, and the shell shim is executable in Git' {
        (Get-Content -Path (Join-Path $script:Root 'dev') -TotalCount 1) | Should -Be '#!/usr/bin/env sh'

        $mode = git -C $script:Root ls-files --stage -- dev 2>$null
        $mode | Should -Not -BeNullOrEmpty -Because 'the shim must be tracked'
        $mode | Should -BeLike '100755 *'
    }

    It 'pin PowerShell exactly as a local tool' {
        $manifest = Get-Content -Path (Join-Path $script:Root '.config' 'dotnet-tools.json') -Raw | ConvertFrom-Json

        $manifest.tools.powershell.version | Should -Match '^7\.\d+\.\d+$'
        $manifest.tools.powershell.commands | Should -Contain 'pwsh'
        $manifest.tools.powershell.rollForward | Should -BeFalse
    }
}
