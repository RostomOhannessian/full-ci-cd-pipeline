Set-StrictMode -Version Latest

function Find-WindowsNativeRuntimeAsset {
    <#
    .SYNOPSIS
        Lists the files in a folder that belong to the native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime.
    .DESCRIPTION
        A file counts when its name starts with Microsoft.Data.SqlClient.SNI (the native library and its symbols, under runtimes/win-x64, win-x86, and
        win-arm64, or beside the application), or when it sits in a folder named after the package, as it does in a copy of a NuGet cache. The paths come
        back relative to the folder and with forward slashes, in order.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $root = (Resolve-Path -Path $Path).ProviderPath

    @(Get-ChildItem -Path $root -Recurse -File -Force | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/'
            if ($_.Name -like 'Microsoft.Data.SqlClient.SNI*' -or $relative -match '(?i)(^|/)microsoft\.data\.sqlclient\.sni\.runtime/') { $relative }
        } | Sort-Object)
}

Export-ModuleMember -Function Find-WindowsNativeRuntimeAsset
