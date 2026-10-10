#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003, REQ-DX-002

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Commands.psm1') -Force
    $script:Table = Get-DevCommandTable
}

Describe 'ConvertFrom-DevArgument' {
    It 'reads switches, values in both forms, and positional words' {
        $parsed = ConvertFrom-DevArgument -Words @('one', '--force', '--profile', 'api', '--name=x', 'two') -Switch @('force') -Value @('profile', 'name')

        $parsed.Error | Should -BeNullOrEmpty
        $parsed.Options['force'] | Should -BeTrue
        $parsed.Options['profile'] | Should -Be 'api'
        $parsed.Options['name'] | Should -Be 'x'
        $parsed.Positional | Should -Be @('one', 'two')
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'an unknown option'; Words = @('--nope'); Pattern = 'Unknown option --nope.' }
        @{ Name = 'a value for a switch'; Words = @('--force=1'); Pattern = 'does not take a value' }
        @{ Name = 'a value option with no value'; Words = @('--profile'); Pattern = 'needs a value' }
    ) {
        (ConvertFrom-DevArgument -Words $Words -Switch @('force') -Value @('profile')).Error | Should -BeLike "*$Pattern*"
    }

    It 'accepts no words' {
        (ConvertFrom-DevArgument -Words @()).Error | Should -BeNullOrEmpty
    }
}

Describe 'The command table' {
    It 'names every subcommand that plan section 12 lists' {
        $script:Table.Keys | Should -Contain 'doctor'
        $script:Table.Keys | Should -Contain 'bootstrap'
        $script:Table.Keys | Should -Contain 'build'
        $script:Table.Keys | Should -Contain 'test'
        $script:Table.Keys | Should -Contain 'docs'
        $script:Table.Keys | Should -Contain 'compose'
        $script:Table.Keys | Should -Contain 'platform'
        $script:Table.Keys | Should -Contain 'tf'
        $script:Table.Keys | Should -Contain 'seed'
        $script:Table.Keys | Should -Contain 'demo'
        $script:Table.Keys | Should -Contain 'lab'
        $script:Table.Keys | Should -Contain 'clean'
    }

    It 'makes available what WP1.4 delivers, and marks the rest as planned with the work package that delivers it' {
        foreach ($name in 'doctor', 'bootstrap', 'build', 'test', 'docs', 'compose', 'lab') { $script:Table[$name].Available | Should -BeTrue -Because $name }
        foreach ($name in 'platform', 'tf', 'seed', 'demo', 'clean') {
            $script:Table[$name].Available | Should -BeFalse -Because $name
            $script:Table[$name].Arrives | Should -Not -BeNullOrEmpty -Because $name
        }
        $script:Table['platform'].Arrives | Should -BeLike '*Phase 3*'
    }

    It 'gives every available command a usage line and a summary' {
        foreach ($name in $script:Table.Keys) {
            $script:Table[$name].Summary | Should -Not -BeNullOrEmpty -Because $name
            if ($script:Table[$name].Available) { $script:Table[$name].Usage | Should -BeLike "dev $name*" }
        }
    }

    It 'matches the commands that AGENTS.md lists as running today' {
        $agents = Get-Content -Path (Join-Path $PSScriptRoot '..' '..' 'AGENTS.md') -Raw
        foreach ($name in 'doctor', 'bootstrap', 'build', 'test', 'docs', 'compose') {
            $agents | Should -BeLike "*``dev $name*" -Because "AGENTS.md must list dev $name"
        }
    }
}

Describe 'Invoke-DevCommand' {
    BeforeEach {
        Mock -ModuleName Dev.Commands Write-DevMessage { }
        Mock -ModuleName Dev.Commands Write-Host { }
        Mock -ModuleName Dev.Common Write-Host { }
    }

    It 'prints help for no command, help, and a flag, and exits 0' -ForEach @('help', '--help', '-h') {
        Invoke-DevCommand -Name $_ | Should -Be 0
    }

    It 'prints help for one command' {
        Invoke-DevCommand -Name 'help' -Rest @('doctor') | Should -Be 0

        Should -Invoke -ModuleName Dev.Commands Write-Host -ParameterFilter { $Object -like '*dev doctor*' }
    }

    It 'exits 2 for an unknown command' {
        Invoke-DevCommand -Name 'frobnicate' | Should -Be 2
    }

    It 'exits 2 for a planned command and names the work package that delivers it' -ForEach @('platform', 'tf', 'seed', 'demo', 'clean') {
        Invoke-DevCommand -Name $_ -Rest @('status') | Should -Be 2

        Should -Invoke -ModuleName Dev.Commands Write-DevMessage -ParameterFilter { $Message -like '*is planned*arrives with*' }
    }

    It 'accepts the command name in any case' {
        Invoke-DevCommand -Name 'HELP' | Should -Be 0
    }

    Context 'doctor' {
        It 'passes the profile through and returns 1 when a check failed' {
            Mock -ModuleName Dev.Commands Invoke-DevDoctor { @([pscustomobject]@{ Name = 'x'; Status = 'fail'; Message = 'm'; Fix = '' }) }

            Invoke-DevCommand -Name 'doctor' -Rest @('--profile', 'lite') | Should -Be 1

            Should -Invoke -ModuleName Dev.Commands Invoke-DevDoctor -Times 1 -Exactly -ParameterFilter { $ProfileName -eq 'lite' }
        }

        It 'returns 0 when nothing failed' {
            Mock -ModuleName Dev.Commands Invoke-DevDoctor { @([pscustomobject]@{ Name = 'x'; Status = 'warn'; Message = 'm'; Fix = '' }) }

            Invoke-DevCommand -Name 'doctor' | Should -Be 0

            Should -Invoke -ModuleName Dev.Commands Invoke-DevDoctor -Times 1 -Exactly -ParameterFilter { $ProfileName -eq 'api' }
        }

        It 'exits 2 for an unknown profile, an unknown option, and a positional word' -ForEach @(
            @{ Words = @('--profile', 'huge') }
            @{ Words = @('--verbose') }
            @{ Words = @('now') }
        ) {
            Invoke-DevCommand -Name 'doctor' -Rest $Words | Should -Be 2
        }
    }

    Context 'test' {
        It 'runs all suites by default, and the suite that was named' {
            Mock -ModuleName Dev.Commands Invoke-DevTest { 0 }

            Invoke-DevCommand -Name 'test' | Should -Be 0
            Invoke-DevCommand -Name 'test' -Rest @('scripts') | Should -Be 0

            Should -Invoke -ModuleName Dev.Commands Invoke-DevTest -Times 1 -Exactly -ParameterFilter { $Suite -eq 'all' }
            Should -Invoke -ModuleName Dev.Commands Invoke-DevTest -Times 1 -Exactly -ParameterFilter { $Suite -eq 'scripts' }
        }

        It 'exits 2 for a suite that does not exist, without running anything' {
            Mock -ModuleName Dev.Commands Invoke-DevTest { 0 }

            Invoke-DevCommand -Name 'test' -Rest @('mutation') | Should -Be 2

            Should -Invoke -ModuleName Dev.Commands Invoke-DevTest -Times 0 -Exactly
        }
    }

    Context 'compose' {
        It 'passes an action, with its extra words, to Compose' {
            Mock -ModuleName Dev.Commands Invoke-DevCompose { 0 }

            Invoke-DevCommand -Name 'compose' -Rest @('logs', 'keycloak') | Should -Be 0

            Should -Invoke -ModuleName Dev.Commands Invoke-DevCompose -Times 1 -Exactly -ParameterFilter { $Action -eq 'logs' -and $Rest -contains 'keycloak' }
        }

        It 'runs the stack check and returns 1 when it fails' {
            Mock -ModuleName Dev.Commands Invoke-DevComposeCheck { @([pscustomobject]@{ Name = 'x'; Status = 'fail'; Message = 'm'; Fix = '' }) }

            Invoke-DevCommand -Name 'compose' -Rest @('check') | Should -Be 1
        }

        It 'exits 2 without an action or with an unknown one' -ForEach @(@(), @('explode')) {
            Invoke-DevCommand -Name 'compose' -Rest $_ | Should -Be 2
        }
    }

    Context 'docs, build, and lab' {
        It 'serves the site only for the word serve' {
            Mock -ModuleName Dev.Commands Invoke-DevDocumentation { 0 }

            Invoke-DevCommand -Name 'docs' -Rest @('serve') | Should -Be 0
            Invoke-DevCommand -Name 'docs' -Rest @('publish') | Should -Be 2

            Should -Invoke -ModuleName Dev.Commands Invoke-DevDocumentation -Times 1 -Exactly -ParameterFilter { $Serve }
        }

        It 'refuses an argument for build' {
            Invoke-DevCommand -Name 'build' -Rest @('--fast') | Should -Be 2
        }

        It 'exits 2 for a lab that has no script, and names the work package' {
            Invoke-DevCommand -Name 'lab' -Rest @('7', 'run') | Should -Be 2

            Should -Invoke -ModuleName Dev.Commands Write-DevMessage -ParameterFilter { $Message -like '*WP1.13*' }
        }

        It 'exits 2 for a lab without an action' {
            Invoke-DevCommand -Name 'lab' -Rest @('1') | Should -Be 2
        }
    }
}

Describe 'Invoke-DevBootstrap' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "boot-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $script:Root | Out-Null
        Set-Content -Path (Join-Path $script:Root '.env.example') -Value "PORT=1`nSECRET="
        Mock -ModuleName Dev.Commands Write-DevMessage { }
    }

    It 'installs the tools and creates .env' {
        Mock -ModuleName Dev.Commands Install-DevToolSet { [pscustomobject]@{ Id = 'pester'; Version = '5.9.0'; Action = 'installed' } }

        Invoke-DevBootstrap -RepositoryRoot $script:Root | Should -Be 0

        Should -Invoke -ModuleName Dev.Commands Install-DevToolSet -Times 1 -Exactly
        Join-Path $script:Root '.env' | Should -Exist
    }

    It 'skips the tools with -SkipTools, and the environment with -SkipEnvironment' {
        Mock -ModuleName Dev.Commands Install-DevToolSet { }

        Invoke-DevBootstrap -SkipTools -RepositoryRoot $script:Root | Should -Be 0
        Should -Invoke -ModuleName Dev.Commands Install-DevToolSet -Times 0 -Exactly

        Remove-Item -Path (Join-Path $script:Root '.env')
        Invoke-DevBootstrap -SkipTools -SkipEnvironment -RepositoryRoot $script:Root | Should -Be 0
        Join-Path $script:Root '.env' | Should -Not -Exist
    }

    It 'returns 1 and installs nothing more when a download fails its checksum' {
        Mock -ModuleName Dev.Commands Install-DevToolSet { throw 'Checksum mismatch for Pester 5.9.0' }

        Invoke-DevBootstrap -RepositoryRoot $script:Root | Should -Be 1

        Join-Path $script:Root '.env' | Should -Not -Exist
    }
}
