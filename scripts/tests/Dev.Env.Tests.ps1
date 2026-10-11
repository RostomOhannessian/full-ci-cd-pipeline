#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003, REQ-DX-005

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Env.psm1') -Force

    function Write-Template {
        param([string] $Body)
        $path = Join-Path $TestDrive "$([guid]::NewGuid().ToString('N')).example"
        [System.IO.File]::WriteAllText($path, $Body.Replace("`r`n", "`n"))
        return $path
    }

    $script:Template = @'
# a comment
PORT=1433
SECRET_ONE=
NAME=catalog
SECRET_TWO=
'@
}

Describe 'Read-DevEnvFile' {
    It 'reads names and values, and skips comments and blank lines' {
        $path = Write-Template "# c`n`nA=1`nB=`nC=two words`n"

        $values = Read-DevEnvFile -Path $path

        $values['A'] | Should -Be '1'
        $values['B'] | Should -Be ''
        $values['C'] | Should -Be 'two words'
        $values.Keys | Should -HaveCount 3
    }

    It 'returns nothing for a file that does not exist' {
        (Read-DevEnvFile -Path (Join-Path $TestDrive 'missing.env')).Count | Should -Be 0
    }

    It 'rejects a line that is not NAME=value, and names the line' {
        $path = Write-Template "A=1`nthis is wrong`n"

        { Read-DevEnvFile -Path $path } | Should -Throw '*:2: expected NAME=value*'
    }

    It 'rejects a lower case name, so a typo cannot hide' {
        $path = Write-Template "lower=1`n"

        { Read-DevEnvFile -Path $path } | Should -Throw '*expected NAME=value*'
    }
}

Describe 'Get-DevSecret' {
    It 'returns the length that was asked for, from letters and digits only' {
        $secret = Get-DevSecret -Length 40

        $secret | Should -HaveCount 1
        $secret.Length | Should -Be 40
        $secret | Should -Match '^[A-Za-z0-9]+$'
    }

    It 'always meets the SQL Server password policy: an upper case letter, a lower case letter, and a digit' {
        1..200 | ForEach-Object {
            $secret = Get-DevSecret -Length 16
            $secret | Should -MatchExactly '[A-Z]'
            $secret | Should -MatchExactly '[a-z]'
            $secret | Should -Match '[0-9]'
        }
    }

    It 'does not repeat' {
        $secrets = 1..50 | ForEach-Object { Get-DevSecret }

        ($secrets | Select-Object -Unique).Count | Should -Be 50
    }

    It 'refuses a secret that is too short to be one' {
        { Get-DevSecret -Length 8 } | Should -Throw
    }
}

Describe 'Initialize-DevEnvironment' {
    BeforeEach {
        $script:Env = Join-Path $TestDrive "$([guid]::NewGuid().ToString('N')).env"
    }

    It 'creates .env with a default for each named value and a generated secret for each empty one' {
        $template = Write-Template $script:Template

        $result = Initialize-DevEnvironment -TemplatePath $template -Path $script:Env

        $result.Created | Should -BeTrue
        $result.Generated | Should -Be @('SECRET_ONE', 'SECRET_TWO')
        $values = Read-DevEnvFile -Path $script:Env
        $values['PORT'] | Should -Be '1433'
        $values['NAME'] | Should -Be 'catalog'
        $values['SECRET_ONE'].Length | Should -BeGreaterOrEqual 16
        $values['SECRET_ONE'] | Should -Not -Be $values['SECRET_TWO']
    }

    It 'writes LF line endings and no byte order mark, so the file reads the same on every host' {
        Initialize-DevEnvironment -TemplatePath (Write-Template $script:Template) -Path $script:Env | Out-Null

        $bytes = [System.IO.File]::ReadAllBytes($script:Env)

        $bytes | Should -Not -Contain 13
        ($bytes[0..2] -join ',') | Should -Not -Be '239,187,191'
    }

    It 'restricts the file to its owner on Linux and macOS' -Skip:$IsWindows {
        Initialize-DevEnvironment -TemplatePath (Write-Template $script:Template) -Path $script:Env | Out-Null

        $ownerOnly = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite

        [System.IO.File]::GetUnixFileMode($script:Env) | Should -Be $ownerOnly
    }

    It 'never changes a value that exists' {
        $template = Write-Template $script:Template
        Initialize-DevEnvironment -TemplatePath $template -Path $script:Env | Out-Null
        $before = Get-Content -Path $script:Env -Raw

        $result = Initialize-DevEnvironment -TemplatePath $template -Path $script:Env

        $result.Changed | Should -BeFalse
        Get-Content -Path $script:Env -Raw | Should -Be $before
    }

    It 'adds a variable that a newer template introduced, and keeps the rest' {
        $template = Write-Template $script:Template
        Initialize-DevEnvironment -TemplatePath $template -Path $script:Env | Out-Null
        $first = Read-DevEnvFile -Path $script:Env
        $newer = Write-Template ($script:Template + "`nSECRET_THREE=`nEXTRA=yes`n")

        $result = Initialize-DevEnvironment -TemplatePath $newer -Path $script:Env

        $result.Generated | Should -Be @('SECRET_THREE')
        $result.Defaulted | Should -Be @('EXTRA')
        $second = Read-DevEnvFile -Path $script:Env
        $second['SECRET_ONE'] | Should -Be $first['SECRET_ONE']
        $second['EXTRA'] | Should -Be 'yes'
    }

    It 'fills a variable that exists but is empty, without touching the others' {
        $template = Write-Template $script:Template
        Set-Content -Path $script:Env -Value "PORT=9999`nSECRET_ONE=`n"

        $result = Initialize-DevEnvironment -TemplatePath $template -Path $script:Env

        $values = Read-DevEnvFile -Path $script:Env
        $values['PORT'] | Should -Be '9999'
        $values['SECRET_ONE'].Length | Should -BeGreaterOrEqual 16
        $result.Generated | Should -Contain 'SECRET_ONE'
    }

    It 'regenerates only the secrets with -Force, and leaves the defaults' {
        $template = Write-Template $script:Template
        Initialize-DevEnvironment -TemplatePath $template -Path $script:Env | Out-Null
        $before = Read-DevEnvFile -Path $script:Env
        $edited = (Get-Content -Path $script:Env -Raw) -replace 'PORT=1433', 'PORT=4000'
        [System.IO.File]::WriteAllText($script:Env, $edited)

        Initialize-DevEnvironment -TemplatePath $template -Path $script:Env -Force | Out-Null

        $after = Read-DevEnvFile -Path $script:Env
        $after['SECRET_ONE'] | Should -Not -Be $before['SECRET_ONE']
        $after['PORT'] | Should -Be '4000'
    }

    It 'fails when the template does not exist' {
        { Initialize-DevEnvironment -TemplatePath (Join-Path $TestDrive 'nope') -Path $script:Env } | Should -Throw '*does not exist*'
    }
}

Describe 'Test-DevEnvironment' {
    It 'lists the variables that .env lacks or leaves empty' {
        $template = Write-Template $script:Template
        $path = Join-Path $TestDrive 'partial.env'
        Set-Content -Path $path -Value "PORT=1`nSECRET_ONE=`n"

        $state = Test-DevEnvironment -TemplatePath $template -Path $path

        $state.Exists | Should -BeTrue
        $state.Missing | Should -Be @('SECRET_ONE', 'NAME', 'SECRET_TWO')
    }

    It 'says that .env does not exist' {
        (Test-DevEnvironment -TemplatePath (Write-Template $script:Template) -Path (Join-Path $TestDrive 'absent.env')).Exists | Should -BeFalse
    }
}
