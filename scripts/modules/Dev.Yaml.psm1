Set-StrictMode -Version Latest

# A strict subset of YAML, enough for tools/versions.yaml and nothing more. PowerShell 7 has no YAML parser, and a parser module would be
# one more dependency to pin before the installer that pins it can run. The reader refuses everything outside the subset, with the
# line number, so a file that it cannot read exactly is never read approximately. The documentation auditor reads the same file with a
# full YAML library and refuses anchors too, so the two readers agree on what is allowed.

function ConvertFrom-DevYaml {
    <#
    .SYNOPSIS
        Reads block mappings and block sequences with scalar values.
    .DESCRIPTION
        Supported: comments, plain and quoted scalars, nested block mappings and block sequences with space indentation, and [] for an
        empty list. Refused: tabs, flow collections, anchors, aliases, tags, block scalars, document markers, merge keys, quoted keys, and
        duplicate keys. Every scalar is returned as a string, and an empty value or null is returned as $null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [string] $Source = 'YAML'
    )

    $lines = [System.Collections.Generic.List[object]]::new()
    $number = 0
    foreach ($raw in ($Text -split '\r?\n')) {
        $number++
        if ($raw -match '^\s*#' -or $raw -match '^\s*$') { continue }
        if ($raw -match '^\s*\t') { throw "${Source}:${number}: indentation must use spaces, not tabs." }
        if ($raw -match '^(---|\.\.\.)\s*$') { throw "${Source}:${number}: document markers are not supported." }

        $indent = $raw.Length - $raw.TrimStart(' ').Length
        $lines.Add([pscustomobject]@{ Indent = $indent; Text = $raw.TrimStart(' ').TrimEnd(); Number = $number })
    }

    if ($lines.Count -eq 0) { return $null }

    $state = [pscustomobject]@{ Lines = $lines; Index = 0; Source = $Source }
    $first = $lines[0]
    if ($first.Indent -ne 0) { throw "${Source}:$($first.Number): the document must start at column 1." }

    $result = Read-DevYamlBlock -State $state -Indent 0
    if ($state.Index -lt $lines.Count) {
        $line = $lines[$state.Index]
        throw "${Source}:$($line.Number): unexpected content '$($line.Text)'."
    }

    return $result
}

function Read-DevYamlBlock {
    param([pscustomobject] $State, [int] $Indent)

    $line = $State.Lines[$State.Index]
    if ($line.Text -eq '-' -or $line.Text.StartsWith('- ', [StringComparison]::Ordinal)) {
        return , (Read-DevYamlSequence -State $State -Indent $Indent)
    }

    return Read-DevYamlMapping -State $State -Indent $Indent
}

function Read-DevYamlMapping {
    param([pscustomobject] $State, [int] $Indent)

    $map = [ordered]@{}
    while ($State.Index -lt $State.Lines.Count) {
        $line = $State.Lines[$State.Index]
        if ($line.Indent -lt $Indent) { break }
        if ($line.Indent -gt $Indent) { throw "$($State.Source):$($line.Number): unexpected indentation." }
        if ($line.Text -eq '-' -or $line.Text.StartsWith('- ', [StringComparison]::Ordinal)) {
            throw "$($State.Source):$($line.Number): a list item must be indented under its key."
        }

        $match = [regex]::Match($line.Text, '^(?<key>[A-Za-z0-9_][A-Za-z0-9_.-]*):(?:\s+(?<value>.*))?$')
        if (-not $match.Success) { throw "$($State.Source):$($line.Number): expected 'key: value', and found '$($line.Text)'." }

        $key = $match.Groups['key'].Value
        if ($map.Contains($key)) { throw "$($State.Source):$($line.Number): the key '$key' appears twice." }

        $State.Index++
        $rawValue = if ($match.Groups['value'].Success) { $match.Groups['value'].Value.Trim() } else { '' }

        if ($rawValue -eq '' -or $rawValue.StartsWith('#', [StringComparison]::Ordinal)) {
            $hasChild = $State.Index -lt $State.Lines.Count -and $State.Lines[$State.Index].Indent -gt $Indent
            if ($hasChild) {
                $map[$key] = Read-DevYamlBlock -State $State -Indent $State.Lines[$State.Index].Indent
            }
            else {
                $map[$key] = $null
            }
        }
        else {
            $map[$key] = ConvertTo-DevYamlScalar -Value $rawValue -Source $State.Source -Number $line.Number
        }
    }

    return $map
}

function Read-DevYamlSequence {
    param([pscustomobject] $State, [int] $Indent)

    $items = [System.Collections.Generic.List[object]]::new()
    while ($State.Index -lt $State.Lines.Count) {
        $line = $State.Lines[$State.Index]
        if ($line.Indent -lt $Indent) { break }
        if ($line.Indent -gt $Indent) { throw "$($State.Source):$($line.Number): unexpected indentation." }
        if (-not ($line.Text -eq '-' -or $line.Text.StartsWith('- ', [StringComparison]::Ordinal))) { break }

        $content = if ($line.Text.Length -gt 1) { $line.Text.Substring(2).TrimStart(' ') } else { '' }
        $offset = $line.Text.Length - $content.Length

        if ($content -eq '' -or $content.StartsWith('#', [StringComparison]::Ordinal)) {
            $State.Index++
            $hasChild = $State.Index -lt $State.Lines.Count -and $State.Lines[$State.Index].Indent -gt $Indent
            $items.Add($(if ($hasChild) { Read-DevYamlBlock -State $State -Indent $State.Lines[$State.Index].Indent } else { $null }))
        }
        elseif ($content -match '^[A-Za-z0-9_][A-Za-z0-9_.-]*:(\s|$)') {
            # The text after the dash is the first key of a mapping whose other keys line up with it.
            $line.Indent = $Indent + $offset
            $line.Text = $content
            $items.Add((Read-DevYamlMapping -State $State -Indent ($Indent + $offset)))
        }
        else {
            $State.Index++
            $items.Add((ConvertTo-DevYamlScalar -Value $content -Source $State.Source -Number $line.Number))
        }
    }

    return , $items.ToArray()
}

function ConvertTo-DevYamlScalar {
    param([string] $Value, [string] $Source, [int] $Number)

    if ($Value -eq '[]') { return , @() }

    if ($Value.StartsWith('"', [StringComparison]::Ordinal)) {
        $match = [regex]::Match($Value, '^"(?<body>(?:[^"\\]|\\.)*)"\s*(#.*)?$')
        if (-not $match.Success) { throw "${Source}:${Number}: the double-quoted value is not closed." }
        return [regex]::Replace($match.Groups['body'].Value, '\\(.)', {
                param($m)
                switch ($m.Groups[1].Value) { 'n' { "`n" } 't' { "`t" } default { $m.Groups[1].Value } }
            })
    }

    if ($Value.StartsWith("'", [StringComparison]::Ordinal)) {
        $match = [regex]::Match($Value, "^'(?<body>(?:[^']|'')*)'\s*(#.*)?$")
        if (-not $match.Success) { throw "${Source}:${Number}: the single-quoted value is not closed." }
        return $match.Groups['body'].Value.Replace("''", "'")
    }

    if ($Value -match '^[&*!|>{\[%@`]') {
        throw "${Source}:${Number}: '$($Value.Substring(0, 1))' starts a YAML feature that this reader does not support (anchors, aliases, tags, block scalars, flow collections)."
    }

    $plain = ($Value -replace '\s+#.*$', '').Trim()
    if ($plain -in '', '~', 'null') { return $null }
    return $plain
}

Export-ModuleMember -Function ConvertFrom-DevYaml
