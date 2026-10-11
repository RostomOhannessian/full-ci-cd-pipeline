#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-CON-002

# Proves that the conformance check passes for wording changes, fails for each kind of structural change, and fails closed when it cannot run.
# It needs Docker, because oasdiff runs in a container.

BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $script:Script = Join-Path $PSScriptRoot 'Test-ContractConformance.ps1'
    $script:AuthoredPath = Join-Path $script:Root 'contracts' 'openapi' 'catalog' 'v1' 'catalog.openapi.yaml'
    $script:Authored = Get-Content -Path $script:AuthoredPath -Raw
    $script:Work = Join-Path ([IO.Path]::GetTempPath()) "conformance-tests-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $script:Work | Out-Null

    function script:Invoke-Conformance {
        param([string] $Text, [string] $Extension = '.yaml')

        $served = Join-Path $script:Work "served-$([guid]::NewGuid().ToString('N'))$Extension"
        [IO.File]::WriteAllText($served, $Text)
        $output = @(& $script:Script -Authored $script:AuthoredPath -Served $served 2>&1 | ForEach-Object { "$_" })
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }
}

AfterAll {
    if ($script:Work -and (Test-Path -Path $script:Work)) { Remove-Item -Path $script:Work -Recurse -Force -ErrorAction SilentlyContinue }
}

Describe 'Test-ContractConformance' {
    It 'passes for the same document' {
        $result = Invoke-Conformance -Text $script:Authored

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -BeLike '*structurally equal*'
    }

    It 'passes when only descriptions, summaries, and examples differ' {
        $text = $script:Authored
        $text = $text -replace '(?m)^(\s+)description: The identifier of the category\.', '${1}description: Another wording.'
        $text = $text -replace '(?m)^(\s+)summary: List categories', '${1}summary: Another summary'
        $text = $text -replace '(?m)^(\s+)example: eyJ2IjoxLCJrIjoiR2FyZGVuIn0', '${1}example: abc'
        $text = $text -replace '(?m)^(\s+)name: Electronics\r?$', '${1}name: Gadgets'
        $text | Should -Not -BeExactly $script:Authored

        $result = Invoke-Conformance -Text $text

        $result.ExitCode | Should -Be 0 -Because $result.Output
    }

    It 'fails when <Name>' -ForEach @(
        @{ Name = 'a constraint changes'; Pattern = 'maxLength: 200'; Replacement = 'maxLength: 201' }
        @{ Name = 'a required property is dropped'; Pattern = 'required: \[sku, name, price, categoryId\]'; Replacement = 'required: [name, price, categoryId]' }
        @{ Name = 'a response is renamed away'; Pattern = '(?s)(operationId: getProduct.*?)        ''304'':'; Replacement = '${1}        ''305'':' }
        @{ Name = 'a header parameter is renamed'; Pattern = 'name: Idempotency-Key'; Replacement = 'name: Idempotency-Id' }
        @{ Name = 'a security scope changes'; Pattern = '(operationId: createCategory[\s\S]*?security:\r?\n        - oauth2: \[)catalog\.admin(\])'; Replacement = '${1}catalog.write${2}' }
        @{ Name = 'an endpoint is renamed'; Pattern = '(?m)^  /api/v1/products:'; Replacement = '  /api/v1/items:' }
        @{ Name = 'the version in the info block changes'; Pattern = '(?m)^  version: 1\.0\.0'; Replacement = '  version: 1.0.1' }
    ) {
        $text = [regex]::Replace($script:Authored, $Pattern, $Replacement)
        $text | Should -Not -BeExactly $script:Authored -Because 'the change must apply'

        $result = Invoke-Conformance -Text $text

        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -BeLike '*differs from the authored contract*'
    }

    It 'compares a JSON document with the YAML contract, because the served document is JSON' {
        $result = Invoke-Conformance -Text '{"openapi":"3.1.0","info":{"title":"Product Catalog API","version":"1.0.0"},"paths":{}}' -Extension '.json'

        $result.ExitCode | Should -Be 1 -Because 'an empty document is not the contract'
        $result.Output | Should -BeLike '*differs from the authored contract*'
    }

    It 'exits 2 when the served file does not exist' {
        & $script:Script -Authored $script:AuthoredPath -Served (Join-Path $script:Work 'missing.json') 2>&1 | Out-Null

        $LASTEXITCODE | Should -Be 2
    }

    It 'exits 2 for a file that is not YAML or JSON' {
        $other = Join-Path $script:Work 'served.txt'
        [IO.File]::WriteAllText($other, 'x')

        & $script:Script -Authored $script:AuthoredPath -Served $other 2>&1 | Out-Null

        $LASTEXITCODE | Should -Be 2
    }

    It 'exits 2 when oasdiff cannot read the document, and never reports equality' {
        $result = Invoke-Conformance -Text "this: is: not: an: openapi document`n  - [broken"

        $result.ExitCode | Should -Be 2 -Because $result.Output
        $result.Output | Should -Not -BeLike '*structurally equal*'
    }
}
