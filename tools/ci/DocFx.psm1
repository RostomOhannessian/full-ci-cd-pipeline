Set-StrictMode -Version Latest

function Get-DocFxAllowance {
    <#
    .SYNOPSIS
        Returns the DocFX warnings that the documentation build accepts, as regular expressions.
    .DESCRIPTION
        DocFX warns that it found no .NET API when no project under src has a public type, and it gives the warning no code, so the
        rules setting in docfx.json cannot silence it. That is true until WP1.6 adds the domain model. The metadata step accepts exactly
        this warning, and every other warning and error fails the build. Remove the entry in WP1.6, and the test that names it.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param()

    return @('^No \.NET API detected')
}

function Get-DocFxProblem {
    <#
    .SYNOPSIS
        Reads a DocFX log and returns every warning and error in it.
    .DESCRIPTION
        DocFX writes one JSON object for each message, and it wraps a long message with real line breaks, so the file is split at the start
        of each object and the breaks inside a message become spaces. A warning that matches an allowance is returned with Allowed set.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LogPath,
        [string[]] $Allowed = @()
    )

    if (-not (Test-Path -Path $LogPath -PathType Leaf)) {
        throw "DocFX wrote no log at '$LogPath', so its result cannot be checked."
    }

    $text = Get-Content -Path $LogPath -Raw
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "The DocFX log at '$LogPath' is empty, so its result cannot be checked."
    }

    foreach ($chunk in ($text -split '(?m)\r?\n(?=\{"severity")')) {
        if ([string]::IsNullOrWhiteSpace($chunk)) { continue }

        $entry = ($chunk -replace '\s*\r?\n\s*', ' ') | ConvertFrom-Json
        if ($entry.severity -notin 'warning', 'error') { continue }

        $matchesAllowance = $entry.severity -eq 'warning' -and @($Allowed | Where-Object { $entry.message -match $_ }).Count -gt 0
        [pscustomobject]@{
            Severity = $entry.severity
            Code     = if ($entry.PSObject.Properties['code']) { $entry.code } else { '' }
            File     = if ($entry.PSObject.Properties['file']) { $entry.file } else { '' }
            Line     = if ($entry.PSObject.Properties['line']) { $entry.line } else { '' }
            Message  = $entry.message
            Allowed  = $matchesAllowance
        }
    }
}

function Format-DocFxSummary {
    <#
    .SYNOPSIS
        Formats the problems of one DocFX command as Markdown for a job summary.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Command,
        [AllowEmptyCollection()] [object[]] $Problem = @()
    )

    $failing = @($Problem | Where-Object { -not $_.Allowed })
    $lines = @("### DocFX $Command`: $(if ($failing.Count -eq 0) { 'passed' } else { 'failed' })", '')

    if ($Problem.Count -eq 0) {
        $lines += 'No warnings and no errors.'
        return $lines
    }

    $lines += '| Severity | Code | Location | Message | Accepted |'
    $lines += '| --- | --- | --- | --- | --- |'
    foreach ($item in ($Problem | Select-Object -First 100)) {
        $location = if ($item.File) { "$($item.File):$($item.Line)" } else { '' }
        $message = ($item.Message -replace '\|', '\|')
        $lines += "| $($item.Severity) | $($item.Code) | $location | $message | $(if ($item.Allowed) { 'yes' } else { 'no' }) |"
    }

    return $lines
}

Export-ModuleMember -Function Get-DocFxAllowance, Get-DocFxProblem, Format-DocFxSummary
