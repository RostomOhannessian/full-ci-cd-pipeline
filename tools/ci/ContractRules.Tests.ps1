#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-CON-001, REQ-API-001, REQ-API-002, REQ-API-003, REQ-API-004, REQ-API-005

# Proves that every rule in contracts/openapi/spectral.yaml can fail. The contract is broken in one way for each rule, all the broken copies are linted
# in one container run, and each copy must trigger its rule. The unbroken contract must trigger none. It needs Docker, like the lint it tests.

BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $script:Contract = Get-Content -Path (Join-Path $script:Root 'contracts' 'openapi' 'catalog' 'v1' 'catalog.openapi.yaml') -Raw
    $script:Ruleset = Get-Content -Path (Join-Path $script:Root 'contracts' 'openapi' 'spectral.yaml') -Raw

    # Each entry breaks the contract in one place. The pattern must match, so a change to the contract that moves the target fails the test
    # instead of silently weakening it.
    $script:Mutations = @(
        @{ Rule = 'oas3-valid-media-example'; Pattern = '(?m)^(\s+)status: active\r?$'; Replacement = '${1}status: archived'; Because = 'an example with a status that is not in the enum' }
        @{ Rule = 'catalog-paths-are-versioned'; Pattern = '(?m)^  /api/v1/categories:'; Replacement = '  /categories:'; Because = 'a path without the version segment' }
        @{ Rule = 'catalog-operation-ids-are-camel-case'; Pattern = 'operationId: listCategories'; Replacement = 'operationId: list_categories'; Because = 'an operation ID in snake case' }
        @{ Rule = 'catalog-operation-has-own-security'; Pattern = '(operationId: discontinueProduct[\s\S]*?)\r?\n      security:\r?\n        - oauth2: \[catalog\.admin\]'; Replacement = '$1'; Because = 'an operation without its own security' }
        @{ Rule = 'catalog-reads-need-the-read-scope'; Pattern = '(operationId: getProduct[\s\S]*?security:\r?\n        - oauth2: \[)catalog\.read(\])'; Replacement = '${1}catalog.write${2}'; Because = 'a GET that asks for the write scope' }
        @{ Rule = 'catalog-create-requires-idempotency-key'; Pattern = '(operationId: createProduct[\s\S]*?parameters:\r?\n        - \$ref: ''#/components/parameters/)IdempotencyKey(''\r?\n)'; Replacement = '${1}IfNoneMatch${2}'; Because = 'a create without the Idempotency-Key parameter' }
        @{ Rule = 'catalog-create-answers-with-location'; Pattern = '(operationId: createProduct[\s\S]*?''201'':[\s\S]*?headers:\r?\n)            Location:\r?\n              \$ref: ''#/components/headers/Location''\r?\n'; Replacement = '$1'; Because = 'a 201 without the Location header' }
        @{ Rule = 'catalog-change-requires-if-match'; Pattern = '(operationId: updateProduct[\s\S]*?)\r?\n        ''428'':\r?\n          \$ref: ''#/components/responses/PreconditionRequired'''; Replacement = '$1'; Because = 'an update without the 428 answer' }
        @{ Rule = 'catalog-single-resource-get-is-conditional'; Pattern = '(operationId: getProduct[\s\S]*?)''304'':'; Replacement = '${1}''305'':'; Because = 'a GET of one resource without the 304 answer' }
        @{ Rule = 'catalog-single-resource-answers-carry-an-etag'; Pattern = '(operationId: getProduct[\s\S]*?''200'':[\s\S]*?headers:\r?\n)            ETag:\r?\n              \$ref: ''#/components/headers/ETag''\r?\n'; Replacement = '$1'; Because = 'a 200 for one resource without the ETag header' }
        @{ Rule = 'catalog-collections-paginate'; Pattern = '(operationId: listProducts[\s\S]*?headers:\r?\n)            Link:\r?\n              \$ref: ''#/components/headers/Link''\r?\n'; Replacement = '$1'; Because = 'a list without the Link header' }
        @{ Rule = 'catalog-requests-reject-unknown-properties'; Pattern = '(    CategoryCreate:[\s\S]*?additionalProperties: )false'; Replacement = '${1}true'; Because = 'a request body that accepts unknown properties' }
        @{ Rule = 'catalog-errors-are-problem-json'; Pattern = '(    NotFound:\r?\n      description:[\s\S]*?content:\r?\n        )application/problem\+json:'; Replacement = '${1}application/json:'; Because = 'an error answer in application/json' }
        @{ Rule = 'catalog-responses-send-api-version'; Pattern = '(    ServiceUnavailable:[\s\S]*?)        api-supported-versions:\r?\n          \$ref: ''#/components/headers/ApiSupportedVersions''\r?\n'; Replacement = '$1'; Because = 'an answer without the api-supported-versions header' }
        @{ Rule = 'catalog-responses-carry-the-deprecation-headers'; Pattern = '(    InternalServerError:[\s\S]*?)        Sunset:\r?\n          \$ref: ''#/components/headers/Sunset''\r?\n'; Replacement = '$1'; Because = 'an answer without the Sunset header' }
        @{ Rule = 'catalog-media-types-have-examples'; Pattern = '(?m)^( +)examples:\r?$'; Replacement = '${1}exampleList:'; Because = 'a body without an example' }
    )

    $script:Directory = Join-Path ([IO.Path]::GetTempPath()) "contract-rules-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $script:Directory | Out-Null
    [IO.File]::WriteAllText((Join-Path $script:Directory 'control.openapi.yaml'), $script:Contract)

    $script:Applied = @{}
    for ($index = 0; $index -lt $script:Mutations.Count; $index++) {
        $mutation = $script:Mutations[$index]
        $name = 'm{0:D2}.openapi.yaml' -f ($index + 1)
        $changed = [regex]::Replace($script:Contract, $mutation.Pattern, $mutation.Replacement, 'None', [timespan]::FromSeconds(5))
        $script:Applied[$mutation.Rule] = @{ File = $name; Changed = ($changed -cne $script:Contract) }
        [IO.File]::WriteAllText((Join-Path $script:Directory $name), $changed)
    }

    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $script:Findings = @()
    $script:RunError = $null
    if (-not $docker) {
        $script:RunError = 'Docker is not installed, and the contract lint runs in a container.'
    }
    else {
        $arguments = @(
            'compose', '-f', (Join-Path $script:Root 'tools' 'lint' 'compose.yaml'), 'run', '--rm', '-T', '-v', "$($script:Directory):/mutant:ro", 'contract-lint',
            'lint', '--ruleset', 'contracts/openapi/spectral.yaml', '--format', 'json', '--quiet', '/mutant/*.openapi.yaml')
        $output = & $docker.Source @arguments 2>$null
        $json = ($output -join "`n").Trim()
        if (-not $json.StartsWith('[')) { $script:RunError = "The lint did not return JSON. Is Docker running? Output: $json" }
        else { $script:Findings = @($json | ConvertFrom-Json) }
    }
}

AfterAll {
    if ($script:Directory -and (Test-Path -Path $script:Directory)) { Remove-Item -Path $script:Directory -Recurse -Force -ErrorAction SilentlyContinue }
}

Describe 'the contract lint rules' {
    It 'ran the lint in a container' {
        $script:RunError | Should -BeNullOrEmpty
    }

    It 'finds nothing in the unbroken contract, so a finding below is caused by the break' {
        $script:RunError | Should -BeNullOrEmpty
        $found = @($script:Findings | Where-Object { $_.source -like '*control.openapi.yaml' })

        $found | Should -HaveCount 0 -Because (($found | ForEach-Object { "$($_.code): $($_.message)" }) -join '; ')
    }

    It 'runs the rule <_> when the contract breaks it' -ForEach @(
        'oas3-valid-media-example', 'catalog-paths-are-versioned', 'catalog-operation-ids-are-camel-case', 'catalog-operation-has-own-security',
        'catalog-reads-need-the-read-scope', 'catalog-create-requires-idempotency-key', 'catalog-create-answers-with-location', 'catalog-change-requires-if-match',
        'catalog-single-resource-get-is-conditional', 'catalog-single-resource-answers-carry-an-etag', 'catalog-collections-paginate',
        'catalog-requests-reject-unknown-properties', 'catalog-errors-are-problem-json', 'catalog-responses-send-api-version',
        'catalog-responses-carry-the-deprecation-headers', 'catalog-media-types-have-examples') {
        $rule = $_
        $script:RunError | Should -BeNullOrEmpty
        $script:Applied[$rule].Changed | Should -BeTrue -Because "the break for $rule must change the contract, so the pattern still finds its target"

        $file = $script:Applied[$rule].File
        $codes = @($script:Findings | Where-Object { $_.source -like "*$file" } | ForEach-Object { $_.code })

        $codes | Should -Contain $rule
    }

    It 'has a break for every catalog rule in the ruleset, so a new rule cannot arrive untested' {
        $declared = [regex]::Matches($script:Ruleset, '(?m)^  (catalog-[a-z0-9-]+):') | ForEach-Object { $_.Groups[1].Value }
        $tested = $script:Mutations | ForEach-Object { $_.Rule }

        $declared.Count | Should -BeGreaterThan 10
        foreach ($rule in $declared) { $tested | Should -Contain $rule -Because "$rule has no break in ContractRules.Tests.ps1" }
    }
}
