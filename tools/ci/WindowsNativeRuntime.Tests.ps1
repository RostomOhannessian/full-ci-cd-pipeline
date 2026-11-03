#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-SUP-003

# Proves that the check for the native Windows runtime of SqlClient passes for the output of a publish for Linux and fails, with the path, for
# each way that the Windows assets can reach an output folder. A check that cannot fail would not keep the license exception honest (ADR-0004).

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'WindowsNativeRuntime.psm1') -Force
    $script:Script = Join-Path $PSScriptRoot 'Test-NoWindowsNativeRuntime.ps1'

    # A folder with the given files, each given as a path relative to the folder.
    function script:New-Output {
        param([string[]] $File)

        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        foreach ($name in $File) {
            $path = Join-Path $root $name
            New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
            Set-Content -Path $path -Value 'x'
        }
        $root
    }

    function script:Invoke-Check {
        param([string[]] $Path)

        $output = @(& $script:Script -Path $Path 2>&1 | ForEach-Object { "$_" })
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }

    $script:LinuxPublish = @('Catalog.Api.dll', 'Catalog.Api.deps.json', 'Catalog.Infrastructure.dll', 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.Internal.Logging.dll', 'appsettings.json')
}

Describe 'Find-WindowsNativeRuntimeAsset' {
    It 'finds nothing in the output of a publish for Linux' {
        $folder = New-Output -File $script:LinuxPublish

        @(Find-WindowsNativeRuntimeAsset -Path $folder).Count | Should -Be 0
    }

    It 'finds nothing in an empty folder' {
        $folder = New-Output -File @()

        @(Find-WindowsNativeRuntimeAsset -Path $folder).Count | Should -Be 0
    }

    It 'finds <Name>' -ForEach @(
        @{ Name = 'the library of one Windows runtime'; File = @('runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll'); Expected = @('runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll') }
        @{ Name = 'the libraries of all three Windows runtimes'; File = @('runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll', 'runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll', 'runtimes/win-arm64/native/Microsoft.Data.SqlClient.SNI.dll'); Expected = @('runtimes/win-arm64/native/Microsoft.Data.SqlClient.SNI.dll', 'runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll', 'runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll') }
        @{ Name = 'a library beside the application'; File = @('Microsoft.Data.SqlClient.SNI.x64.dll'); Expected = @('Microsoft.Data.SqlClient.SNI.x64.dll') }
        @{ Name = 'the symbols of the library'; File = @('runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.pdb'); Expected = @('runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.pdb') }
        @{ Name = 'a name in another case'; File = @('runtimes/win-x64/native/microsoft.data.sqlclient.sni.dll'); Expected = @('runtimes/win-x64/native/microsoft.data.sqlclient.sni.dll') }
        @{ Name = 'a copy of the package folder from a NuGet cache'; File = @('microsoft.data.sqlclient.sni.runtime/7.1.0/LICENSE.txt'); Expected = @('microsoft.data.sqlclient.sni.runtime/7.1.0/LICENSE.txt') }
    ) {
        $folder = New-Output -File ($script:LinuxPublish + $File)

        @(Find-WindowsNativeRuntimeAsset -Path $folder) | Should -Be $Expected
    }
}

Describe 'Test-NoWindowsNativeRuntime' {
    It 'passes for the output of a publish for Linux' {
        $result = Invoke-Check -Path (New-Output -File $script:LinuxPublish)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -BeLike '*holds no native Windows runtime*'
    }

    It 'fails for a publish with no runtime identifier, which copies the Windows assets, and names the file' {
        $folder = New-Output -File ($script:LinuxPublish + 'runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll')

        $result = Invoke-Check -Path $folder

        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -BeLike '*runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll*'
        $result.Output | Should -BeLike '*ADR-0004*'
    }

    It 'fails when any one of several folders holds the assets' {
        $clean = New-Output -File $script:LinuxPublish
        $dirty = New-Output -File ($script:LinuxPublish + 'Microsoft.Data.SqlClient.SNI.x86.dll')

        $result = Invoke-Check -Path @($clean, $dirty)

        $result.ExitCode | Should -Be 1 -Because $result.Output
    }

    It 'fails closed with exit code 2 when a folder does not exist' {
        $result = Invoke-Check -Path (Join-Path $TestDrive 'missing')

        $result.ExitCode | Should -Be 2
    }
}
