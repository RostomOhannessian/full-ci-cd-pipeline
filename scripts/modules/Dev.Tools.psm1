Set-StrictMode -Version Latest

# The verified installer for the developer tools in tools/versions.yaml (ADR-0024). It installs only what the manifest pins, from the one
# place the manifest names, and only after the SHA-256 of the download matches the pin. A mismatch installs nothing.

Import-Module (Join-Path $PSScriptRoot 'Dev.Yaml.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Common.psm1')

$script:SupportedKinds = @('powershell-module')
$script:GalleryPackagePattern = '^https://www\.powershellgallery\.com/api/v2/package/(?<name>[A-Za-z0-9.]+)/(?<version>\d+\.\d+\.\d+)$'
$script:MarkerFile = 'dev-tool.json'

function Get-DevToolsRoot {
    <#
    .SYNOPSIS
        Returns the folder where the installer puts tools. It is inside the repository and Git ignores it.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    return Join-Path $RepositoryRoot '.dev'
}

function Read-DevToolManifest {
    <#
    .SYNOPSIS
        Reads and validates tools/versions.yaml, and returns one object for each tool.
    .DESCRIPTION
        Every rule here protects the installer from a manifest that a pull request changed: only a known kind, an exact version, a download
        URL that is the Gallery's own address for that name and version, and a full SHA-256 are accepted.
    #>
    [CmdletBinding()]
    param([string] $Path = (Join-Path (Get-DevRepositoryRoot) 'tools' 'versions.yaml'))

    if (-not (Test-Path -Path $Path -PathType Leaf)) { throw "The tool manifest '$Path' does not exist." }

    $document = ConvertFrom-DevYaml -Text (Get-Content -Path $Path -Raw) -Source (Split-Path -Path $Path -Leaf)
    if ($null -eq $document -or $document -isnot [System.Collections.IDictionary]) { throw 'The tool manifest must be a mapping.' }
    if (-not $document.Contains('schema-version') -or $document['schema-version'] -ne '1') { throw "The tool manifest must declare 'schema-version: 1'." }
    if (-not $document.Contains('tools') -or $document['tools'] -isnot [System.Array]) { throw "The tool manifest must have a 'tools' list." }

    $seenIds = @{}
    $seenNames = @{}
    $tools = foreach ($entry in $document['tools']) {
        foreach ($field in 'id', 'kind', 'name', 'version', 'url', 'sha256') {
            if ($entry -isnot [System.Collections.IDictionary] -or -not $entry.Contains($field) -or [string]::IsNullOrWhiteSpace($entry[$field])) {
                throw "A tool entry is missing '$field'."
            }
        }

        $id = $entry['id']
        if ($id -notmatch '^[a-z][a-z0-9-]*$') { throw "The tool ID '$id' must be lower case letters, digits, and hyphens." }
        if ($seenIds.ContainsKey($id)) { throw "The tool ID '$id' appears twice." }
        $seenIds[$id] = $true

        if ($entry['kind'] -notin $script:SupportedKinds) {
            throw "${id}: the kind '$($entry['kind'])' is not supported. The installer supports: $($script:SupportedKinds -join ', ')."
        }
        if ($entry['name'] -notmatch '^[A-Za-z][A-Za-z0-9.]*$') { throw "${id}: the module name '$($entry['name'])' is not a valid name." }
        if ($seenNames.ContainsKey($entry['name'].ToLowerInvariant())) { throw "${id}: the module name '$($entry['name'])' appears twice." }
        $seenNames[$entry['name'].ToLowerInvariant()] = $true
        if ($entry['version'] -notmatch '^\d+\.\d+\.\d+$') { throw "${id}: the version '$($entry['version'])' must be exact, as 1.2.3." }
        if ($entry['sha256'] -cnotmatch '^[0-9a-f]{64}$') { throw "${id}: sha256 must be 64 lower case hexadecimal characters." }

        $match = [regex]::Match($entry['url'], $script:GalleryPackagePattern)
        if (-not $match.Success -or $match.Groups['name'].Value -ne $entry['name'] -or $match.Groups['version'].Value -ne $entry['version']) {
            throw "${id}: the url must be https://www.powershellgallery.com/api/v2/package/$($entry['name'])/$($entry['version']), and it is '$($entry['url'])'."
        }

        [pscustomobject]@{
            Id      = $id
            Kind    = $entry['kind']
            Name    = $entry['name']
            Version = $entry['version']
            Url     = $entry['url']
            Sha256  = $entry['sha256']
        }
    }

    return @($tools)
}

function Get-DevToolTarget {
    param([pscustomobject] $Tool, [string] $Root)

    return Join-Path $Root 'modules' $Tool.Name $Tool.Version
}

function Get-DevToolStatus {
    <#
    .SYNOPSIS
        Says whether each pinned tool is installed exactly as the manifest pins it.
    .DESCRIPTION
        A tool counts as installed when its folder holds the module manifest and a marker that records the pinned SHA-256, so a folder that
        a different pin created, or one that someone copied in, is reported as missing.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $Tool,
        [string] $Root = (Get-DevToolsRoot)
    )

    foreach ($item in $Tool) {
        $target = Get-DevToolTarget -Tool $item -Root $Root
        $marker = Join-Path $target $script:MarkerFile
        $installed = $false

        if ((Test-Path -Path (Join-Path $target "$($item.Name).psd1") -PathType Leaf) -and (Test-Path -Path $marker -PathType Leaf)) {
            try {
                $recorded = Get-Content -Path $marker -Raw | ConvertFrom-Json
                $installed = $recorded.sha256 -ceq $item.Sha256
            }
            catch {
                $installed = $false
            }
        }

        [pscustomobject]@{ Id = $item.Id; Name = $item.Name; Version = $item.Version; Installed = $installed; Path = $target }
    }
}

function Invoke-DevDownload {
    <#
    .SYNOPSIS
        Downloads a file over HTTPS. Tests replace this function, so nothing in the test suite touches the network.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Url, [Parameter(Mandatory)] [string] $Path)

    if (-not $Url.StartsWith('https://', [StringComparison]::Ordinal)) { throw "Refusing to download '$Url', because it is not an https address." }
    Invoke-WebRequest -Uri $Url -OutFile $Path -MaximumRedirection 3 -TimeoutSec 180 -UseBasicParsing
}

function Expand-DevPackage {
    <#
    .SYNOPSIS
        Extracts a .nupkg (a zip file) into a folder after checking that no entry would land outside it.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Archive, [Parameter(Mandatory)] [string] $Destination)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [System.IO.Path]::GetFullPath($Destination).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $target = [System.IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($root, [StringComparison]::Ordinal)) {
                throw "The package entry '$($entry.FullName)' would be written outside the install folder, so the package is refused."
            }

            if ($entry.FullName.EndsWith('/') -or [string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
            }

            New-Item -ItemType Directory -Path (Split-Path -Path $target -Parent) -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Install-DevTool {
    <#
    .SYNOPSIS
        Installs one pinned tool, unless it is already installed exactly as pinned.
    .DESCRIPTION
        Downloads to a temporary folder, compares the SHA-256 with the manifest, extracts with a path check, confirms that the module
        manifest names the pinned version, and only then moves the result into place. Any failure removes the temporary folder and leaves
        the install folder as it was.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [pscustomobject] $Tool,
        [string] $Root = (Get-DevToolsRoot),
        [switch] $Force
    )

    $target = Get-DevToolTarget -Tool $Tool -Root $Root
    $status = @(Get-DevToolStatus -Tool @($Tool) -Root $Root)[0]
    if ($status.Installed -and -not $Force) {
        return [pscustomobject]@{ Id = $Tool.Id; Version = $Tool.Version; Action = 'present'; Path = $target }
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) "dev-tool-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        $archive = Join-Path $work "$($Tool.Name).$($Tool.Version).nupkg"
        Invoke-DevDownload -Url $Tool.Url -Path $archive

        $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Tool.Sha256) {
            throw "Checksum mismatch for $($Tool.Name) $($Tool.Version): the manifest pins $($Tool.Sha256) and the download is $actual. Nothing was installed."
        }

        $staging = Join-Path $work 'staging'
        Expand-DevPackage -Archive $archive -Destination $staging

        $moduleManifest = Join-Path $staging "$($Tool.Name).psd1"
        if (-not (Test-Path -Path $moduleManifest -PathType Leaf)) { throw "The package for $($Tool.Name) has no $($Tool.Name).psd1, so it is not a module. Nothing was installed." }
        $declared = (Import-PowerShellDataFile -Path $moduleManifest).ModuleVersion
        if ($declared -ne $Tool.Version) { throw "The package for $($Tool.Name) declares version $declared, and the manifest pins $($Tool.Version). Nothing was installed." }

        foreach ($leftover in '_rels', 'package', '[Content_Types].xml', "$($Tool.Name).nuspec") {
            Remove-Item -LiteralPath (Join-Path $staging $leftover) -Recurse -Force -ErrorAction SilentlyContinue
        }

        [pscustomobject]@{ id = $Tool.Id; name = $Tool.Name; version = $Tool.Version; sha256 = $Tool.Sha256 } |
            ConvertTo-Json | Set-Content -Path (Join-Path $staging $script:MarkerFile) -Encoding utf8NoBOM

        if (Test-Path -Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        New-Item -ItemType Directory -Path (Split-Path -Path $target -Parent) -Force | Out-Null
        Move-Item -LiteralPath $staging -Destination $target

        return [pscustomobject]@{ Id = $Tool.Id; Version = $Tool.Version; Action = 'installed'; Path = $target }
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-DevToolSet {
    <#
    .SYNOPSIS
        Installs every tool in the manifest, or the ones named, and returns what happened to each.
    #>
    [CmdletBinding()]
    param(
        [string] $ManifestPath = (Join-Path (Get-DevRepositoryRoot) 'tools' 'versions.yaml'),
        [string] $Root = (Get-DevToolsRoot),
        [string[]] $Id = @(),
        [switch] $Force
    )

    $tools = @(Read-DevToolManifest -Path $ManifestPath)
    if ($Id.Count -gt 0) {
        $unknown = @($Id | Where-Object { $_ -notin $tools.Id })
        if ($unknown.Count -gt 0) { throw "The manifest has no tool named: $($unknown -join ', ')." }
        $tools = @($tools | Where-Object { $_.Id -in $Id })
    }

    foreach ($tool in $tools) { Install-DevTool -Tool $tool -Root $Root -Force:$Force }
}

function Enable-DevToolPath {
    <#
    .SYNOPSIS
        Puts the install folder first on PSModulePath for this session, so the pinned modules win over any other copy.
    #>
    [CmdletBinding()]
    param([string] $Root = (Get-DevToolsRoot))

    $modules = Join-Path $Root 'modules'
    $separator = [System.IO.Path]::PathSeparator
    $parts = @($env:PSModulePath -split [regex]::Escape($separator) | Where-Object { $_ -and $_ -ne $modules })
    $env:PSModulePath = (@($modules) + $parts) -join $separator
}

Export-ModuleMember -Function Get-DevToolsRoot, Read-DevToolManifest, Get-DevToolStatus, Install-DevTool, Install-DevToolSet, Enable-DevToolPath
