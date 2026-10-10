#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Common.psm1') -Force
    $script:Pwsh = Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')
}

Describe 'Invoke-DevNative' {
    It 'returns the exit code and the output of a program' {
        $result = Invoke-DevNative -FilePath $script:Pwsh -ArgumentList @('-NoProfile', '-Command', "'hello'; exit 3")

        $result.Found | Should -BeTrue
        $result.ExitCode | Should -Be 3
        $result.Output | Should -Contain 'hello'
    }

    It 'reports a missing program as exit code 127, without throwing' {
        $result = Invoke-DevNative -FilePath 'a-program-that-does-not-exist'

        $result.Found | Should -BeFalse
        $result.ExitCode | Should -Be 127
    }

    It 'passes an argument as one value, so a shell metacharacter does nothing' {
        $result = Invoke-DevNative -FilePath $script:Pwsh -ArgumentList @('-NoProfile', '-Command', 'param($x) $x', 'a; echo injected')

        $result.Output | Should -Not -Contain 'injected'
    }

    It 'runs in the working directory that the caller names' {
        $result = Invoke-DevNative -FilePath $script:Pwsh -ArgumentList @('-NoProfile', '-Command', '(Get-Location).Path') -WorkingDirectory $TestDrive

        ($result.Output -join '') | Should -Be $TestDrive
    }
}

Describe 'Invoke-DevNativeLive' {
    It 'returns only the exit code, so a caller can return it as a number' {
        $result = @(Invoke-DevNativeLive -FilePath $script:Pwsh -ArgumentList @('-NoProfile', '-Command', "'output of the child process'; exit 5") 6>$null)

        $result | Should -HaveCount 1
        $result[0] | Should -BeOfType [int]
        $result[0] | Should -Be 5
    }

    It 'returns 127 for a program that is not installed' {
        Invoke-DevNativeLive -FilePath 'a-program-that-does-not-exist' 6>$null | Should -Be 127
    }
}

Describe 'Write-DevCheck' {
    It 'returns 0 when nothing failed, even with warnings' {
        $checks = @(
            Build-DevCheck -Name 'A' -Status ok -Message 'fine'
            Build-DevCheck -Name 'B' -Status warn -Message 'careful' -Fix 'do this'
        )

        Write-DevCheck -Check $checks 6>$null | Should -Be 0
    }

    It 'returns 1 when any check failed' {
        $checks = @((Build-DevCheck -Name 'A' -Status ok -Message 'fine'), (Build-DevCheck -Name 'B' -Status fail -Message 'broken'))

        Write-DevCheck -Check $checks 6>$null | Should -Be 1
    }

    It 'prints the fix of a check that did not pass, and the counts' {
        $checks = @((Build-DevCheck -Name 'B' -Status warn -Message 'careful' -Fix 'run the thing'))

        $text = (Write-DevCheck -Check $checks -Title 'sample' 6>&1 | ForEach-Object { "$_" }) -join "`n"

        $text | Should -BeLike '*fix: run the thing*'
        $text | Should -BeLike '*sample: 0 ok, 1 warning(s), 0 failure(s)*'
    }
}

Describe 'Test-DevPortFree' {
    It 'is false for a port that something listens on, and true once it stops' {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        try { Test-DevPortFree -Port $port | Should -BeFalse }
        finally { $listener.Stop() }

        Test-DevPortFree -Port $port | Should -BeTrue
    }
}

Describe 'Get-DevHost' {
    It 'names the operating system family and the architecture' {
        $machine = Get-DevHost

        $machine.Family | Should -BeIn 'windows', 'linux', 'macos'
        $machine.Architecture | Should -Match '^(x64|x86|arm64|arm)$'
    }
}

Describe 'Get-DevRepositoryRoot' {
    It 'finds the repository, which holds the global.json that pins the SDK' {
        Join-Path (Get-DevRepositoryRoot) 'global.json' | Should -Exist
    }
}
