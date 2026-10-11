#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Tools.psm1') -Force

    # Builds a package like the ones the PowerShell Gallery serves: a zip with the module at its root and the NuGet metadata beside it.
    function Build-FakePackage {
        param([string] $Name = 'Fake', [string] $Version = '1.2.3', [string] $DeclaredVersion = $Version, [string] $ExtraEntry, [switch] $NoModuleManifest)

        $source = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $source '_rels'), (Join-Path $source 'package') -Force | Out-Null
        if (-not $NoModuleManifest) { Set-Content -Path (Join-Path $source "$Name.psd1") -Value "@{ ModuleVersion = '$DeclaredVersion'; RootModule = '$Name.psm1' }" }
        Set-Content -Path (Join-Path $source "$Name.psm1") -Value 'function Get-Fake { 1 }'
        Set-Content -Path (Join-Path $source "$Name.nuspec") -Value '<package />'
        Set-Content -LiteralPath (Join-Path $source '[Content_Types].xml') -Value '<Types />'
        Set-Content -Path (Join-Path $source '_rels' '.rels') -Value '<Relationships />'

        $archive = Join-Path $TestDrive "$Name.$Version.$([guid]::NewGuid().ToString('N')).nupkg"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)

        if ($ExtraEntry) {
            $zip = [System.IO.Compression.ZipFile]::Open($archive, 'Update')
            try {
                $entry = $zip.CreateEntry($ExtraEntry)
                $writer = [System.IO.StreamWriter]::new($entry.Open())
                $writer.Write('payload')
                $writer.Dispose()
            }
            finally { $zip.Dispose() }
        }

        return $archive
    }

    function Build-FakeTool {
        param([string] $Archive, [string] $Name = 'Fake', [string] $Version = '1.2.3', [string] $Sha256)
        if (-not $Sha256) { $Sha256 = (Get-FileHash -Path $Archive -Algorithm SHA256).Hash.ToLowerInvariant() }
        return [pscustomobject]@{
            Id = $Name.ToLowerInvariant(); Kind = 'powershell-module'; Name = $Name; Version = $Version
            Url = "https://www.powershellgallery.com/api/v2/package/$Name/$Version"; Sha256 = $Sha256
        }
    }

    function Write-Manifest {
        param([string] $Body)
        $path = Join-Path $TestDrive "$([guid]::NewGuid().ToString('N')).yaml"
        Set-Content -Path $path -Value $Body
        return $path
    }

    $script:ValidTool = @'
  - id: fake
    kind: powershell-module
    name: Fake
    version: 1.2.3
    url: https://www.powershellgallery.com/api/v2/package/Fake/1.2.3
    sha256: 0000000000000000000000000000000000000000000000000000000000000000
'@
}

Describe 'Read-DevToolManifest' {
    It 'reads the real manifest, and every tool in it is valid' {
        $tools = @(Read-DevToolManifest -Path (Join-Path $PSScriptRoot '..' '..' 'tools' 'versions.yaml'))

        $tools.Id | Should -Contain 'pester'
        $tools.Id | Should -Contain 'psscriptanalyzer'
    }

    It 'returns each tool with its pinned values' {
        $path = Write-Manifest "schema-version: 1`ntools:`n$script:ValidTool"

        $tool = @(Read-DevToolManifest -Path $path)[0]

        $tool.Name | Should -Be 'Fake'
        $tool.Version | Should -Be '1.2.3'
        $tool.Sha256 | Should -Be ('0' * 64)
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'an unknown kind'; Edit = { $args[0] -replace 'powershell-module', 'shell-script' }; Pattern = '*not supported*' }
        @{ Name = 'a version range'; Edit = { $args[0] -replace 'version: 1\.2\.3', 'version: ^1.2.3' }; Pattern = '*must be exact*' }
        @{ Name = 'a short checksum'; Edit = { $args[0] -replace 'sha256: 0+', 'sha256: abc' }; Pattern = '*64 lower case*' }
        @{ Name = 'an upper case checksum'; Edit = { $args[0] -replace 'sha256: 0+', ('sha256: ' + ('A' * 64)) }; Pattern = '*64 lower case*' }
        @{ Name = 'a url on another host'; Edit = { $args[0] -replace 'www\.powershellgallery\.com', 'example.org' }; Pattern = '*the url must be*' }
        @{ Name = 'a url for another module'; Edit = { $args[0] -replace 'package/Fake/', 'package/Other/' }; Pattern = '*the url must be*' }
        @{ Name = 'a plain http url'; Edit = { $args[0] -replace 'https://', 'http://' }; Pattern = '*the url must be*' }
        @{ Name = 'a bad ID'; Edit = { $args[0] -replace 'id: fake', 'id: Fake_1' }; Pattern = '*lower case letters*' }
    ) {
        $path = Write-Manifest ("schema-version: 1`ntools:`n" + (& $Edit $script:ValidTool))

        { Read-DevToolManifest -Path $path } | Should -Throw $Pattern
    }

    It 'rejects a missing field' {
        $path = Write-Manifest ("schema-version: 1`ntools:`n" + ($script:ValidTool -replace '(?m)^    sha256:.*\r?\n?', ''))

        { Read-DevToolManifest -Path $path } | Should -Throw "*missing 'sha256'*"
    }

    It 'rejects a duplicate ID' {
        $path = Write-Manifest ("schema-version: 1`ntools:`n$script:ValidTool`n$script:ValidTool")

        { Read-DevToolManifest -Path $path } | Should -Throw '*appears twice*'
    }

    It 'rejects a manifest without the schema version' {
        $path = Write-Manifest "tools:`n$script:ValidTool"

        { Read-DevToolManifest -Path $path } | Should -Throw '*schema-version*'
    }

    It 'rejects a manifest that does not exist' {
        { Read-DevToolManifest -Path (Join-Path $TestDrive 'missing.yaml') } | Should -Throw '*does not exist*'
    }
}

Describe 'Install-DevTool' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive "root-$([guid]::NewGuid().ToString('N'))"
        $script:Downloads = 0
    }

    It 'downloads, verifies, and installs a module with the Gallery layout' {
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path } -Verifiable

        $result = Install-DevTool -Tool $tool -Root $script:Root

        $result.Action | Should -Be 'installed'
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'Fake.psd1' | Should -Exist
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'Fake.psm1' | Should -Exist
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'dev-tool.json' | Should -Exist
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' '_rels' | Should -Not -Exist
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'Fake.nuspec' | Should -Not -Exist
        Should -InvokeVerifiable
    }

    It 'makes the installed module importable by name and version' {
        $archive = Build-FakePackage
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }
        Install-DevTool -Tool (Build-FakeTool -Archive $archive) -Root $script:Root | Out-Null
        $before = $env:PSModulePath

        try {
            Enable-DevToolPath -Root $script:Root
            $module = Import-Module -Name Fake -RequiredVersion 1.2.3 -PassThru -Force

            $module.Version.ToString() | Should -Be '1.2.3'
            (Get-Command Get-Fake).Source | Should -Be 'Fake'
        }
        finally {
            Remove-Module Fake -Force -ErrorAction SilentlyContinue
            $env:PSModulePath = $before
        }
    }

    It 'installs nothing when the checksum does not match the pin' {
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive -Sha256 ('f' * 64)
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }

        { Install-DevTool -Tool $tool -Root $script:Root } | Should -Throw '*Checksum mismatch*Nothing was installed*'

        Join-Path $script:Root 'modules' | Should -Not -Exist
    }

    It 'leaves a working install alone when a later install fails its checksum' {
        $good = Build-FakePackage
        $tool = Build-FakeTool -Archive $good
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $good -Destination $Path }
        Install-DevTool -Tool $tool -Root $script:Root | Out-Null
        $tampered = Build-FakePackage -ExtraEntry 'extra.txt'
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $tampered -Destination $Path }

        { Install-DevTool -Tool $tool -Root $script:Root -Force } | Should -Throw '*Checksum mismatch*'

        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'Fake.psd1' | Should -Exist
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'extra.txt' | Should -Not -Exist
    }

    It 'refuses a package entry that would escape the install folder' {
        $archive = Build-FakePackage -ExtraEntry '../escaped.txt'
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }

        { Install-DevTool -Tool $tool -Root $script:Root } | Should -Throw '*outside the install folder*'

        Join-Path $script:Root 'modules' | Should -Not -Exist
    }

    It 'refuses a package that is not a module' {
        $archive = Build-FakePackage -NoModuleManifest
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }

        { Install-DevTool -Tool $tool -Root $script:Root } | Should -Throw '*has no Fake.psd1*'
    }

    It 'refuses a package whose module declares another version' {
        $archive = Build-FakePackage -DeclaredVersion '9.9.9'
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }

        { Install-DevTool -Tool $tool -Root $script:Root } | Should -Throw '*declares version 9.9.9*'
    }

    It 'does not download again when the tool is installed as pinned' {
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }
        Install-DevTool -Tool $tool -Root $script:Root | Out-Null

        $second = Install-DevTool -Tool $tool -Root $script:Root

        $second.Action | Should -Be 'present'
        Should -Invoke -ModuleName Dev.Tools Invoke-DevDownload -Times 1 -Exactly
    }

    It 'installs again when the pin changed, because the recorded checksum no longer matches' {
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }
        Install-DevTool -Tool $tool -Root $script:Root | Out-Null
        $other = Build-FakePackage -ExtraEntry 'new.txt'
        $repinned = Build-FakeTool -Archive $other
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $other -Destination $Path }

        $result = Install-DevTool -Tool $repinned -Root $script:Root

        $result.Action | Should -Be 'installed'
        Join-Path $script:Root 'modules' 'Fake' '1.2.3' 'new.txt' | Should -Exist
    }

    It 'leaves no temporary folder behind after a failure' {
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive -Sha256 ('f' * 64)
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }
        $before = @(Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Filter 'dev-tool-*' -Directory -ErrorAction SilentlyContinue).Count

        { Install-DevTool -Tool $tool -Root $script:Root } | Should -Throw

        @(Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Filter 'dev-tool-*' -Directory -ErrorAction SilentlyContinue).Count | Should -Be $before
    }
}

Describe 'Get-DevToolStatus' {
    It 'reports a tool as missing until it is installed as pinned' {
        $root = Join-Path $TestDrive 'status-root'
        $archive = Build-FakePackage
        $tool = Build-FakeTool -Archive $archive
        Mock -ModuleName Dev.Tools Invoke-DevDownload { Copy-Item -Path $archive -Destination $Path }

        (Get-DevToolStatus -Tool @($tool) -Root $root).Installed | Should -BeFalse
        Install-DevTool -Tool $tool -Root $root | Out-Null
        (Get-DevToolStatus -Tool @($tool) -Root $root).Installed | Should -BeTrue
    }

    It 'does not trust a folder that was copied in without the recorded checksum' {
        $root = Join-Path $TestDrive 'copied-root'
        $folder = Join-Path $root 'modules' 'Fake' '1.2.3'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Set-Content -Path (Join-Path $folder 'Fake.psd1') -Value "@{ ModuleVersion = '1.2.3' }"
        $tool = [pscustomobject]@{ Id = 'fake'; Name = 'Fake'; Version = '1.2.3'; Sha256 = ('a' * 64) }

        (Get-DevToolStatus -Tool @($tool) -Root $root).Installed | Should -BeFalse
    }
}

Describe 'Install-DevToolSet' {
    It 'rejects an ID that the manifest does not name' {
        $path = Write-Manifest "schema-version: 1`ntools:`n$script:ValidTool"

        { Install-DevToolSet -ManifestPath $path -Root (Join-Path $TestDrive 'r') -Id 'nope' } | Should -Throw '*has no tool named: nope*'
    }
}

Describe 'Enable-DevToolPath' {
    It 'puts the install folder first, once' {
        $before = $env:PSModulePath
        try {
            Enable-DevToolPath -Root (Join-Path $TestDrive 'path-root')
            Enable-DevToolPath -Root (Join-Path $TestDrive 'path-root')
            $parts = $env:PSModulePath -split [System.IO.Path]::PathSeparator

            $parts[0] | Should -Be (Join-Path $TestDrive 'path-root' 'modules')
            @($parts | Where-Object { $_ -eq $parts[0] }) | Should -HaveCount 1
        }
        finally { $env:PSModulePath = $before }
    }
}
