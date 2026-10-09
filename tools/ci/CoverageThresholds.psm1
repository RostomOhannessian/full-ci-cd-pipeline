Set-StrictMode -Version Latest

function Read-CoverageReport {
    <#
    .SYNOPSIS
        Reads Cobertura files and returns the line and branch counts for each package.
    .DESCRIPTION
        Several reports may cover the same package, so counts are merged per source line: a line is covered when any report covers it.
        Classes whose file matches an exclusion pattern are ignored, which is how generated code and migrations stay out of the numbers.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]] $Path,
        [string[]] $ExcludePath = @()
    )

    $lines = @{}
    foreach ($file in $Path) {
        $document = [System.Xml.XmlDocument]::new()
        $document.Load($file)

        foreach ($package in $document.SelectNodes('//package')) {
            foreach ($class in $package.SelectNodes('classes/class')) {
                $fileName = $class.GetAttribute('filename') -replace '\\', '/'
                if ($ExcludePath | Where-Object { $fileName -match $_ }) { continue }

                foreach ($line in $class.SelectNodes('lines/line')) {
                    $branchCovered = 0
                    $branchTotal = 0
                    if ($line.GetAttribute('condition-coverage') -match '\((\d+)/(\d+)\)') {
                        $branchCovered = [int]$Matches[1]
                        $branchTotal = [int]$Matches[2]
                    }

                    $key = '{0}|{1}|{2}' -f $package.GetAttribute('name'), $fileName, $line.GetAttribute('number')
                    $hits = [int]$line.GetAttribute('hits')
                    if ($lines.ContainsKey($key)) {
                        $existing = $lines[$key]
                        $existing.Hits = [Math]::Max($existing.Hits, $hits)
                        $existing.BranchCovered = [Math]::Max($existing.BranchCovered, $branchCovered)
                        $existing.BranchTotal = [Math]::Max($existing.BranchTotal, $branchTotal)
                    }
                    else {
                        $lines[$key] = [pscustomobject]@{
                            Package       = $package.GetAttribute('name')
                            Hits          = $hits
                            BranchCovered = $branchCovered
                            BranchTotal   = $branchTotal
                        }
                    }
                }
            }
        }
    }

    $packages = @{}
    foreach ($group in $lines.Values | Group-Object -Property Package) {
        $packages[$group.Name] = [pscustomobject]@{
            Name            = $group.Name
            LinesValid      = $group.Count
            LinesCovered    = @($group.Group | Where-Object { $_.Hits -gt 0 }).Count
            BranchesValid   = ($group.Group | Measure-Object -Property BranchTotal -Sum).Sum
            BranchesCovered = ($group.Group | Measure-Object -Property BranchCovered -Sum).Sum
        }
    }

    return $packages
}

function Get-CoverageResult {
    <#
    .SYNOPSIS
        Compares coverage with the policy and returns one result per check.
    .DESCRIPTION
        An assembly that contains code but has no coverage data fails, because "not measured" must never read as "covered".
        An assembly with no code yet is not applicable. An exemption in the policy names its reason and the work package that ends it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [hashtable] $Report,
        [Parameter(Mandatory)] [object] $Policy,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $results = [System.Collections.Generic.List[object]]::new()
    $sourceRoot = Join-Path $RepositoryRoot 'src'
    $excluded = @($Policy.excludePaths)

    if (Test-Path $sourceRoot) {
        foreach ($directory in Get-ChildItem -Path $sourceRoot -Directory | Sort-Object Name) {
            $name = $directory.Name
            $package = $Report[$name]
            $threshold = @($Policy.assemblies | Where-Object { $_.name -eq $name }) | Select-Object -First 1
            $exemption = @($Policy.exemptions | Where-Object { $_.assembly -eq $name }) | Select-Object -First 1

            if (-not (Test-HasCode -Directory $directory.FullName -ExcludePath $excluded)) {
                $results.Add((New-CoverageResult $name 'lines' $null $null 'N/A' 'No code yet.'))
                continue
            }

            if ($null -eq $package) {
                if ($exemption) {
                    $results.Add((New-CoverageResult $name 'lines' $null $null 'Exempt' "$($exemption.reason) Ends in $($exemption.endsIn)."))
                }
                else {
                    $results.Add((New-CoverageResult $name 'lines' $null $null 'Fail' 'The assembly has code but no coverage data. No test loaded it, so it is not measured. Add tests or a documented exemption.'))
                }
                continue
            }

            if ($threshold) {
                $results.Add((New-RateResult $name 'lines' $threshold.line $package.LinesCovered $package.LinesValid))
                $results.Add((New-RateResult $name 'branches' $threshold.branch $package.BranchesCovered $package.BranchesValid))
            }
        }
    }

    $included = @($Report.Values | Where-Object { $_.Name -match $Policy.solution.includePackages -and $_.Name -notmatch $Policy.solution.excludePackages })
    $covered = 0
    $valid = 0
    foreach ($package in $included) {
        $covered += $package.LinesCovered
        $valid += $package.LinesValid
    }

    $results.Add((New-RateResult 'Solution' 'lines' $Policy.solution.line $covered $valid))

    return $results
}

function Format-CoverageSummary {
    <#
    .SYNOPSIS
        Renders the results as a Markdown table for a job summary.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]] $Result)

    $rows = foreach ($item in $Result) {
        $required = if ($null -eq $item.Required) { '' } else { "$($item.Required)%" }
        $actual = if ($null -eq $item.Actual) { '' } else { "$($item.Actual)%" }
        '| {0} | {1} | {2} | {3} | {4} | {5} |' -f $item.Scope, $item.Metric, $required, $actual, $item.Status, $item.Note
    }

    return @('| Scope | Metric | Required | Actual | Status | Note |', '| --- | --- | --- | --- | --- | --- |') + $rows
}

function Test-HasCode {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [string[]] $ExcludePath = @()
    )

    $files = Get-ChildItem -Path $Directory -Recurse -File -Filter '*.cs' |
        ForEach-Object { $_.FullName -replace '\\', '/' } |
        Where-Object { $path = $_; -not ($ExcludePath | Where-Object { $path -match $_ }) }

    return @($files).Count -gt 0
}

function New-CoverageResult {
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Creates an in-memory object only.')]
    param($Scope, $Metric, $Required, $Actual, $Status, $Note)

    return [pscustomobject]@{ Scope = $Scope; Metric = $Metric; Required = $Required; Actual = $Actual; Status = $Status; Note = $Note }
}

function New-RateResult {
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Creates an in-memory object only.')]
    param($Scope, $Metric, $Required, $Covered, $Valid)

    if (-not $Valid) {
        return New-CoverageResult $Scope $Metric $Required $null 'N/A' "No coverable $Metric."
    }

    $actual = [Math]::Round(100 * $Covered / $Valid, 2)
    $status = if ($actual -ge $Required) { 'Pass' } else { 'Fail' }
    return New-CoverageResult $Scope $Metric $Required $actual $status "$Covered of $Valid."
}

Export-ModuleMember -Function Read-CoverageReport, Get-CoverageResult, Format-CoverageSummary
