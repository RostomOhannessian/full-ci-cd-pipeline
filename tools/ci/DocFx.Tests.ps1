#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DOC-005

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'DocFx.psm1') -Force

    # DocFX writes one JSON object for each message and wraps a long message with a real line break, as the first sample shows.
    function Initialize-DocFxLog {
        param([string[]] $Entry)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        Set-Content -Path $path -Value ($Entry -join "`n")
        return $path
    }

    $script:Info = '{"severity":"info","message":"Processing Catalog.Api","date_time":"2026-10-09T01:51:28Z"}'
    $script:NoApi = "{`"severity`":`"warning`",`"message`":`"No .NET API detected for `nCatalog.Api,Catalog.Domain. Check that the input contains APIs.`",`"date_time`":`"2026-10-09T01:51:28Z`"}"
    $script:BadLink = '{"severity":"warning","message":"Invalid file link:(~/docs/missing.md).","file":"docs/index.md","line":"12","code":"InvalidFileLink","date_time":"2026-10-09T01:51:28Z"}'
    $script:Failure = '{"severity":"error","message":"Build failed.","date_time":"2026-10-09T01:51:28Z"}'
}

Describe 'The accepted DocFX warnings' {
    It 'name exactly one warning, so a new exception is a reviewed change to this test' {
        # Remove this warning and this test in WP1.6, when the domain model gives DocFX a public API to document.
        Get-DocFxAllowance | Should -Be @('^No \.NET API detected')
    }
}

Describe 'Get-DocFxProblem' {
    It 'ignores informational messages' {
        $log = Initialize-DocFxLog -Entry @($script:Info)

        @(Get-DocFxProblem -LogPath $log) | Should -HaveCount 0
    }

    It 'reads a message that DocFX wrapped across lines and accepts the allowed warning' {
        $log = Initialize-DocFxLog -Entry @($script:Info, $script:NoApi, $script:Info)

        $problems = @(Get-DocFxProblem -LogPath $log -Allowed (Get-DocFxAllowance))

        $problems | Should -HaveCount 1
        $problems[0].Allowed | Should -BeTrue
        $problems[0].Message | Should -BeLike 'No .NET API detected for Catalog.Api,Catalog.Domain.*'
    }

    It 'does not accept the same warning when nothing allows it' {
        $log = Initialize-DocFxLog -Entry @($script:NoApi)

        (Get-DocFxProblem -LogPath $log).Allowed | Should -BeFalse
    }

    It 'fails a warning that is not allowed, with its code and location' {
        $log = Initialize-DocFxLog -Entry @($script:NoApi, $script:BadLink)

        $failing = @(Get-DocFxProblem -LogPath $log -Allowed (Get-DocFxAllowance) | Where-Object { -not $_.Allowed })

        $failing | Should -HaveCount 1
        $failing[0].Code | Should -Be 'InvalidFileLink'
        $failing[0].File | Should -Be 'docs/index.md'
    }

    It 'never accepts an error, even when its text matches an allowance' {
        $log = Initialize-DocFxLog -Entry @('{"severity":"error","message":"No .NET API detected"}')

        (Get-DocFxProblem -LogPath $log -Allowed (Get-DocFxAllowance)).Allowed | Should -BeFalse
    }

    It 'fails when DocFX wrote no log, because silence must not read as success' {
        { Get-DocFxProblem -LogPath (Join-Path $TestDrive 'missing.json') } | Should -Throw '*wrote no log*'
    }

    It 'fails when the log is empty' {
        $log = Initialize-DocFxLog -Entry @('')

        { Get-DocFxProblem -LogPath $log } | Should -Throw '*is empty*'
    }
}

Describe 'Format-DocFxSummary' {
    It 'reports a clean run' {
        (Format-DocFxSummary -Command 'build' -Problem @()) -join "`n" | Should -BeLike '### DocFX build: passed*No warnings and no errors.'
    }

    It 'reports a failure and marks the accepted warning' {
        $log = Initialize-DocFxLog -Entry @($script:NoApi, $script:BadLink, $script:Failure)

        $summary = (Format-DocFxSummary -Command 'metadata' -Problem @(Get-DocFxProblem -LogPath $log -Allowed (Get-DocFxAllowance))) -join "`n"

        $summary | Should -BeLike '### DocFX metadata: failed*'
        $summary | Should -Match 'InvalidFileLink'
        $summary | Should -Match '\| yes \|'
        $summary | Should -Match '\| no \|'
    }
}
