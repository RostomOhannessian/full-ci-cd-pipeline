Set-StrictMode -Version Latest

# Helpers that every dev command shares. Native programs run through Invoke-DevNative, so a test can replace one function and never start a
# real process, and nothing here reads or writes outside the repository except through the paths a caller names.

function Get-DevRepositoryRoot {
    <#
    .SYNOPSIS
        Returns the repository root, which is two folders above this module.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    return (Resolve-Path -Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
}

function Invoke-DevNative {
    <#
    .SYNOPSIS
        Runs a native program and returns its exit code and output without throwing.
    .DESCRIPTION
        A program that is missing is reported as exit code 127 with an empty output, so a check can say "not installed" and go on. The
        arguments are passed as an array, never through a shell, so a value cannot add a second command.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $ArgumentList = @(),
        [string] $WorkingDirectory
    )

    $command = Get-Command -Name $FilePath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        return [pscustomobject]@{ ExitCode = 127; Output = @(); Found = $false }
    }

    $previous = Get-Location
    try {
        if ($WorkingDirectory) { Set-Location -Path $WorkingDirectory }
        $output = @(& $command.Source @ArgumentList 2>&1 | ForEach-Object { "$_" })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Found = $true }
    }
    finally {
        Set-Location -Path $previous
    }
}

function Invoke-DevNativeLive {
    <#
    .SYNOPSIS
        Runs a native program with its output going straight to the console, and returns its exit code.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $ArgumentList = @(),
        [string] $WorkingDirectory
    )

    $command = Get-Command -Name $FilePath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        Write-DevMessage -Level fail -Message "'$FilePath' is not installed or is not on the PATH."
        return 127
    }

    $previous = Get-Location
    try {
        if ($WorkingDirectory) { Set-Location -Path $WorkingDirectory }
        # The program writes to the console, and only its exit code comes back, so a caller can return the result as a number.
        & $command.Source @ArgumentList | Out-Host
        return [int]$LASTEXITCODE
    }
    finally {
        Set-Location -Path $previous
    }
}

function Write-DevMessage {
    <#
    .SYNOPSIS
        Writes one status line in the form that every dev command uses: [ok], [warn], [fail], or [info].
    #>
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'The dev command is an interactive tool, and coloured status lines are its output.')]
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateSet('ok', 'warn', 'fail', 'info')] [string] $Level,
        [Parameter(Mandatory)] [string] $Message
    )

    $color = switch ($Level) { 'ok' { 'Green' } 'warn' { 'Yellow' } 'fail' { 'Red' } default { 'Cyan' } }
    Write-Host ('[{0}]' -f $Level.PadRight(4)) -ForegroundColor $color -NoNewline
    Write-Host " $Message"
}

function Build-DevCheck {
    <#
    .SYNOPSIS
        Creates the result of one check, which doctor and the stack check both return.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('ok', 'warn', 'fail')] [string] $Status,
        [Parameter(Mandatory)] [string] $Message,
        [string] $Fix = ''
    )

    return [pscustomobject]@{ Name = $Name; Status = $Status; Message = $Message; Fix = $Fix }
}

function Write-DevCheck {
    <#
    .SYNOPSIS
        Prints check results and a summary, and returns 1 when any check failed and 0 otherwise.
    #>
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'The dev command is an interactive tool, and its status lines are its output.')]
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Check,
        [string] $Title = 'checks'
    )

    foreach ($item in $Check) {
        Write-DevMessage -Level $item.Status -Message "$($item.Name): $($item.Message)"
        if ($item.Status -ne 'ok' -and $item.Fix) {
            Write-Host "       fix: $($item.Fix)"
        }
    }

    $ok = @($Check | Where-Object Status -EQ 'ok').Count
    $warn = @($Check | Where-Object Status -EQ 'warn').Count
    $fail = @($Check | Where-Object Status -EQ 'fail').Count
    Write-Host ''
    Write-Host "$Title`: $ok ok, $warn warning(s), $fail failure(s)"
    return [int]($fail -gt 0)
}

function Get-DevHost {
    <#
    .SYNOPSIS
        Describes the machine: the operating system family and the CPU architecture.
    #>
    [CmdletBinding()]
    param()

    $family = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    return [pscustomobject]@{ Family = $family; Architecture = $architecture }
}

function Test-DevPortFree {
    <#
    .SYNOPSIS
        Returns $true when nothing listens on the TCP port of the loopback interface.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)] [ValidateRange(1, 65535)] [int] $Port)

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
        return $true
    }
    catch [System.Net.Sockets.SocketException] {
        return $false
    }
    finally {
        $listener.Stop()
    }
}

Export-ModuleMember -Function Get-DevRepositoryRoot, Invoke-DevNative, Invoke-DevNativeLive, Write-DevMessage, Build-DevCheck, Write-DevCheck, Get-DevHost, Test-DevPortFree
