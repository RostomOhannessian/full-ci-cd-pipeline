#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003, REQ-DX-005, REQ-GOV-003

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Doctor.psm1') -Force

    $script:Api = Get-DevProfile -Name 'api'

    function Get-Reply {
        param([int] $ExitCode = 0, [string[]] $Output = @(), [bool] $Found = $true)
        return [pscustomobject]@{ ExitCode = $ExitCode; Output = $Output; Found = $Found }
    }

    # Answers the Docker commands that doctor asks, from a table, so each test names only what differs.
    function Use-DockerReply {
        param([string] $Server = '29.8.1', [string] $Info = 'linux 8 17179869184', [string] $Compose = '5.5.1', [int] $ServerExit = 0, [bool] $Found = $true)
        $script:Docker = @{ Server = $Server; Info = $Info; Compose = $Compose; ServerExit = $ServerExit; Found = $Found }
        Mock -ModuleName Dev.Doctor Invoke-DevNative {
            if (-not $script:Docker.Found) { return [pscustomobject]@{ ExitCode = 127; Output = @(); Found = $false } }
            switch -Wildcard ($ArgumentList -join ' ') {
                'version*' { [pscustomobject]@{ ExitCode = $script:Docker.ServerExit; Output = @($script:Docker.Server); Found = $true } }
                'info*' { [pscustomobject]@{ ExitCode = 0; Output = @($script:Docker.Info); Found = $true } }
                '--version' { [pscustomobject]@{ ExitCode = 0; Output = @('git version 2.50.0'); Found = $true } }
                'compose version*' { [pscustomobject]@{ ExitCode = 0; Output = @($script:Docker.Compose); Found = $true } }
                default { [pscustomobject]@{ ExitCode = 0; Output = @(); Found = $true } }
            }
        }
    }
}

Describe 'Test-DevSdkVersion' {
    It 'accepts <Installed> for the pin <Pinned> under <RollForward>: <Expected>' -ForEach @(
        @{ Installed = '10.0.401'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $true }
        @{ Installed = '10.0.402'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $true }
        @{ Installed = '10.0.499'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $true }
        @{ Installed = '10.0.400'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $false }
        @{ Installed = '10.0.501'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $false }
        @{ Installed = '10.1.401'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $false }
        @{ Installed = '9.0.401'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $false }
        @{ Installed = '10.0.402'; Pinned = '10.0.401'; RollForward = 'disable'; Expected = $false }
        @{ Installed = '10.0.401'; Pinned = '10.0.401'; RollForward = 'disable'; Expected = $true }
        @{ Installed = 'nonsense'; Pinned = '10.0.401'; RollForward = 'latestPatch'; Expected = $false }
    ) {
        Test-DevSdkVersion -Installed $Installed -Pinned $Pinned -RollForward $RollForward | Should -Be $Expected
    }
}

Describe 'Get-DevProfile' {
    It 'knows the four profiles of plan section 12, and only the api profile can run today' {
        'api', 'lite', 'full', 'ci' | ForEach-Object { (Get-DevProfile -Name $_).Name | Should -Be $_ }
        (Get-DevProfile -Name 'api').Available | Should -BeTrue
        (Get-DevProfile -Name 'lite').Available | Should -BeFalse
    }

    It 'carries the budgets that the plan and ADR-0019 state' {
        (Get-DevProfile -Name 'api').WarnMemoryGiB | Should -Be 4
        (Get-DevProfile -Name 'api').MinCpu | Should -Be 2
        (Get-DevProfile -Name 'lite').WarnMemoryGiB | Should -Be 10
        (Get-DevProfile -Name 'full').WarnMemoryGiB | Should -Be 16
        (Get-DevProfile -Name 'full').MinCpu | Should -Be 8
    }

    It 'rejects a profile that does not exist' {
        { Get-DevProfile -Name 'huge' } | Should -Throw "*Unknown profile 'huge'*"
    }
}

Describe 'Test-DevHostSupport' {
    It 'passes x86-64 hosts' -ForEach @('windows', 'linux', 'macos') {
        $family = $_
        Mock -ModuleName Dev.Doctor Get-DevHost { [pscustomobject]@{ Family = $family; Architecture = 'x64' } }

        (Test-DevHostSupport).Status | Should -Be 'ok'
    }

    It 'warns on arm64, where SQL Server containers are not supported' -ForEach @('macos', 'windows', 'linux') {
        $family = $_
        Mock -ModuleName Dev.Doctor Get-DevHost { [pscustomobject]@{ Family = $family; Architecture = 'arm64' } }

        $check = Test-DevHostSupport

        $check.Status | Should -Be 'warn'
        $check.Message | Should -BeLike '*SQL Server*x86-64*'
        $check.Fix | Should -BeLike '*Codespace*'
    }
}

Describe 'Test-DevDocker' {
    It 'fails when Docker is not installed' {
        Use-DockerReply -Found $false

        $checks = @(Test-DevDocker -ResourceProfile $script:Api)

        $checks | Should -HaveCount 1
        $checks[0].Status | Should -Be 'fail'
        $checks[0].Message | Should -BeLike '*not installed*'
    }

    It 'fails when the daemon does not answer, and says how to start it' {
        Use-DockerReply -ServerExit 1

        $check = @(Test-DevDocker -ResourceProfile $script:Api)[0]

        $check.Status | Should -Be 'fail'
        $check.Fix | Should -BeLike '*Start Docker*'
    }

    It 'fails on Windows containers' {
        Use-DockerReply -Info 'windows 8 17179869184'

        $check = @(Test-DevDocker -ResourceProfile $script:Api)[0]

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*windows containers*'
    }

    It 'passes a machine that meets the api budget' {
        Use-DockerReply -Info 'linux 4 8589934592'

        $checks = @(Test-DevDocker -ResourceProfile $script:Api)

        @($checks | Where-Object Status -NE 'ok') | Should -HaveCount 0
    }

    It 'warns below the api memory budget and fails below what SQL Server needs: <Gigabytes> GiB gives <Status>' -ForEach @(
        @{ Gigabytes = 2; Status = 'fail' }
        @{ Gigabytes = 3.5; Status = 'warn' }
        @{ Gigabytes = 4; Status = 'ok' }
        @{ Gigabytes = 16; Status = 'ok' }
    ) {
        Use-DockerReply -Info "linux 8 $([int64]($Gigabytes * 1GB))"

        $memory = @(Test-DevDocker -ResourceProfile $script:Api) | Where-Object Name -EQ 'Docker memory'

        $memory.Status | Should -Be $Status
    }

    It 'warns when Docker has fewer CPUs than the profile asks for' {
        Use-DockerReply -Info "linux 1 $(8GB)"

        (@(Test-DevDocker -ResourceProfile $script:Api) | Where-Object Name -EQ 'Docker CPUs').Status | Should -Be 'warn'
    }

    It 'checks the budget of the profile that was asked for' {
        Use-DockerReply -Info "linux 4 $(8GB)"

        (@(Test-DevDocker -ResourceProfile (Get-DevProfile -Name 'lite')) | Where-Object Name -EQ 'Docker memory').Status | Should -Be 'warn'
    }

    It 'fails a Compose plugin older than 2.23, which compose.yaml needs for inline configs' {
        Use-DockerReply -Compose '2.20.0'

        (@(Test-DevDocker -ResourceProfile $script:Api) | Where-Object Name -EQ 'Docker Compose').Status | Should -Be 'fail'
    }

    It 'accepts a Compose version with a leading v and a suffix' {
        Use-DockerReply -Compose 'v2.40.3-desktop.1'

        (@(Test-DevDocker -ResourceProfile $script:Api) | Where-Object Name -EQ 'Docker Compose').Status | Should -Be 'ok'
    }
}

Describe 'Test-DevPort' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "root-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $script:Root | Out-Null
        Set-Content -Path (Join-Path $script:Root '.env.example') -Value "SQL_PORT=1433`nVALKEY_PORT=6379`nKEYCLOAK_PORT=8180`nGRAFANA_PORT=3000`nOTLP_GRPC_PORT=4317`nOTLP_HTTP_PORT=4318"
    }

    It 'reads the ports from .env.example, and .env overrides them' {
        Set-Content -Path (Join-Path $script:Root '.env') -Value 'KEYCLOAK_PORT=9999'

        $ports = @(Get-DevComposePort -RepositoryRoot $script:Root)

        ($ports | Where-Object Variable -EQ 'KEYCLOAK_PORT').Port | Should -Be 9999
        ($ports | Where-Object Variable -EQ 'SQL_PORT').Port | Should -Be 1433
    }

    It 'fails and names each port that is taken, with the variable to change' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 0; Output = @(); Found = $true } }
        Mock -ModuleName Dev.Doctor Test-DevPortFree { $Port -ne 6379 -and $Port -ne 3000 }

        $check = Test-DevPort -RepositoryRoot $script:Root

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*Valkey (6379)*'
        $check.Message | Should -BeLike '*Grafana (3000)*'
        $check.Fix | Should -BeLike '*VALKEY_PORT*'
    }

    It 'passes when every port is free' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 0; Output = @(); Found = $true } }
        Mock -ModuleName Dev.Doctor Test-DevPortFree { $true }

        (Test-DevPort -RepositoryRoot $script:Root).Status | Should -Be 'ok'
    }

    It 'does not call a port taken when the stack itself holds it' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 0; Output = @('abc123'); Found = $true } }
        Mock -ModuleName Dev.Doctor Test-DevPortFree { $false }

        (Test-DevPort -RepositoryRoot $script:Root).Status | Should -Be 'ok'
    }
}

Describe 'Test-DevEnvironmentFile' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "env-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $script:Root | Out-Null
        Set-Content -Path (Join-Path $script:Root '.env.example') -Value "A=1`nSECRET="
    }

    It 'warns when .env does not exist, and points at bootstrap' {
        $check = Test-DevEnvironmentFile -RepositoryRoot $script:Root

        $check.Status | Should -Be 'warn'
        $check.Fix | Should -BeLike '*bootstrap*'
    }

    It 'warns and names a variable that .env lacks' {
        Set-Content -Path (Join-Path $script:Root '.env') -Value 'A=1'

        (Test-DevEnvironmentFile -RepositoryRoot $script:Root).Message | Should -BeLike '*SECRET*'
    }

    It 'passes a complete .env' {
        Set-Content -Path (Join-Path $script:Root '.env') -Value "A=1`nSECRET=x"

        (Test-DevEnvironmentFile -RepositoryRoot $script:Root).Status | Should -Be 'ok'
    }
}

Describe 'Test-DevGitIdentity' {
    BeforeEach {
        Mock -ModuleName Dev.Doctor Invoke-DevNative {
            if ($ArgumentList -contains '--version') { return [pscustomobject]@{ ExitCode = 0; Output = @('git version 2.50.0'); Found = $true } }
            [pscustomobject]@{ ExitCode = $script:EmailExit; Output = @($script:Email); Found = $true }
        }
    }

    It 'passes a GitHub noreply address' {
        $script:EmailExit = 0; $script:Email = '12345+someone@users.noreply.github.com'

        (Test-DevGitIdentity | Where-Object Name -EQ 'Git identity').Status | Should -Be 'ok'
    }

    It 'warns on a personal address, without printing it' {
        $script:EmailExit = 0; $script:Email = 'someone@example.org'

        $check = Test-DevGitIdentity | Where-Object Name -EQ 'Git identity'

        $check.Status | Should -Be 'warn'
        ($check.Message + $check.Fix) | Should -Not -BeLike '*someone@example.org*'
    }

    It 'warns when the address is not set' {
        $script:EmailExit = 1; $script:Email = ''

        (Test-DevGitIdentity | Where-Object Name -EQ 'Git identity').Status | Should -Be 'warn'
    }
}

Describe 'Test-DevDotnet' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "dotnet-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $script:Root | Out-Null
        Set-Content -Path (Join-Path $script:Root 'global.json') -Value '{ "sdk": { "version": "10.0.401", "rollForward": "latestPatch" } }'
    }

    It 'passes an SDK that satisfies the pin' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 0; Output = @('10.0.402'); Found = $true } }

        (Test-DevDotnet -RepositoryRoot $script:Root).Status | Should -Be 'ok'
    }

    It 'fails an SDK that does not, and names the pin' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 0; Output = @('10.0.300'); Found = $true } }

        $check = Test-DevDotnet -RepositoryRoot $script:Root

        $check.Status | Should -Be 'fail'
        $check.Message | Should -BeLike '*10.0.401*'
    }

    It 'fails when dotnet cannot find the pinned SDK' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 145; Output = @('A compatible .NET SDK was not found.'); Found = $true } }

        (Test-DevDotnet -RepositoryRoot $script:Root).Status | Should -Be 'fail'
    }

    It 'fails when dotnet is not installed, and says which SDK to install' {
        Mock -ModuleName Dev.Doctor Invoke-DevNative { [pscustomobject]@{ ExitCode = 127; Output = @(); Found = $false } }

        $check = Test-DevDotnet -RepositoryRoot $script:Root

        $check.Status | Should -Be 'fail'
        $check.Fix | Should -BeLike '*10.0.401*'
    }
}

Describe 'Invoke-DevDoctor' {
    BeforeEach {
        Use-DockerReply
        Mock -ModuleName Dev.Doctor Test-DevPortFree { $true }
    }

    It 'checks the host, Git, .NET, PowerShell, the script tools, Docker, ports, and the environment file for the api profile' {
        $names = (Invoke-DevDoctor -ProfileName 'api').Name

        $names | Should -Contain 'Host'
        $names | Should -Contain 'Git'
        $names | Should -Contain '.NET SDK'
        $names | Should -Contain 'PowerShell'
        $names | Should -Contain 'Script tools'
        $names | Should -Contain 'Docker'
        $names | Should -Contain 'Docker memory'
        $names | Should -Contain 'Ports'
        $names | Should -Contain 'Environment file'
    }

    It 'checks only the Docker budget for a profile that arrives in Phase 3, and says so' {
        $checks = Invoke-DevDoctor -ProfileName 'lite'

        $checks.Name | Should -Not -Contain 'Ports'
        ($checks | Where-Object Name -EQ 'Profile').Message | Should -BeLike '*Phase 3*'
    }

    It 'changes nothing on the machine: it never starts, stops, or writes anything' {
        Invoke-DevDoctor -ProfileName 'api' | Out-Null

        Should -Invoke -ModuleName Dev.Doctor Invoke-DevNative -ParameterFilter { $ArgumentList -contains 'up' -or $ArgumentList -contains 'down' -or $ArgumentList -contains 'run' -or $ArgumentList -contains 'rm' } -Times 0 -Exactly
    }
}
