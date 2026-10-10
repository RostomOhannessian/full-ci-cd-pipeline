#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Project.psm1') -Force

    # A repository with the test projects that the suites select by name.
    function Build-ProjectTree {
        $root = Join-Path $TestDrive "tree-$([guid]::NewGuid().ToString('N'))"
        foreach ($name in 'Catalog.Domain.Tests', 'Catalog.Application.Tests', 'Catalog.Architecture.Tests', 'Catalog.Infrastructure.IntegrationTests', 'Catalog.Identity.ContractTests', 'Catalog.Client.PactTests', 'Governance.Auditor.Tests') {
            New-Item -ItemType Directory -Path (Join-Path $root 'tests' $name) -Force | Out-Null
            Set-Content -Path (Join-Path $root 'tests' $name "$name.csproj") -Value '<Project />'
        }
        New-Item -ItemType Directory -Path (Join-Path $root 'tests' 'Catalog.Domain.Tests' 'obj') -Force | Out-Null
        Set-Content -Path (Join-Path $root 'tests' 'Catalog.Domain.Tests' 'obj' 'Generated.csproj') -Value '<Project />'
        return $root
    }
}

Describe 'Get-DevTestProject' {
    BeforeAll { $script:Tree = Build-ProjectTree }

    It 'selects the unit projects by name, and leaves out the architecture, integration, contract, and Pact projects' {
        $unit = Get-DevTestProject -Suite unit -RepositoryRoot $script:Tree

        $unit | Should -Be @('tests/Catalog.Application.Tests/Catalog.Application.Tests.csproj', 'tests/Catalog.Domain.Tests/Catalog.Domain.Tests.csproj', 'tests/Governance.Auditor.Tests/Governance.Auditor.Tests.csproj')
    }

    It 'selects the architecture project alone' {
        Get-DevTestProject -Suite architecture -RepositoryRoot $script:Tree | Should -Be @('tests/Catalog.Architecture.Tests/Catalog.Architecture.Tests.csproj')
    }

    It 'ignores a project under obj or bin' {
        (Get-DevTestProject -Suite unit -RepositoryRoot $script:Tree) -join ' ' | Should -Not -BeLike '*obj*'
    }

    It 'finds the real test projects of this repository' {
        (Get-DevTestProject -Suite architecture) | Should -Contain 'tests/Catalog.Architecture.Tests/Catalog.Architecture.Tests.csproj'
        (Get-DevTestProject -Suite unit) | Should -Contain 'tests/Governance.Auditor.Tests/Governance.Auditor.Tests.csproj'
    }
}

Describe 'Invoke-DevTest' {
    BeforeEach {
        Mock -ModuleName Dev.Project Invoke-DevDotnetTest { 0 }
        Mock -ModuleName Dev.Project Invoke-DevScriptTest { 0 }
        Mock -ModuleName Dev.Project Invoke-DevDocumentationGate { 0 }
        Mock -ModuleName Dev.Project Get-DevTestProject { @('tests/X/X.csproj') }
        Mock -ModuleName Dev.Project Write-DevMessage { }
    }

    It 'runs only the suite that was asked for' {
        Invoke-DevTest -Suite scripts | Should -Be 0

        Should -Invoke -ModuleName Dev.Project Invoke-DevScriptTest -Times 1 -Exactly
        Should -Invoke -ModuleName Dev.Project Invoke-DevDocumentationGate -Times 0 -Exactly
        Should -Invoke -ModuleName Dev.Project Invoke-DevDotnetTest -Times 0 -Exactly
    }

    It 'runs every suite for all' {
        Invoke-DevTest -Suite all | Should -Be 0

        Should -Invoke -ModuleName Dev.Project Invoke-DevDotnetTest -Times 2 -Exactly
        Should -Invoke -ModuleName Dev.Project Invoke-DevScriptTest -Times 1 -Exactly
        Should -Invoke -ModuleName Dev.Project Invoke-DevDocumentationGate -Times 1 -Exactly
    }

    It 'keeps going after a failed suite and returns the failure at the end' {
        Mock -ModuleName Dev.Project Invoke-DevScriptTest { 1 }

        Invoke-DevTest -Suite all | Should -Be 1

        Should -Invoke -ModuleName Dev.Project Invoke-DevDocumentationGate -Times 1 -Exactly
    }

    It 'rejects a suite that does not exist' {
        { Invoke-DevTest -Suite 'mutation' } | Should -Throw
    }
}

Describe 'Invoke-DevDotnetTest' {
    It 'runs each project with the Microsoft.Testing.Platform syntax, and returns the worst exit code' {
        Mock -ModuleName Dev.Project Write-DevMessage { }
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { if ($ArgumentList -contains 'b.csproj') { 3 } else { 0 } }

        Invoke-DevDotnetTest -Project @('a.csproj', 'b.csproj', 'c.csproj') | Should -Be 3

        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 3 -Exactly -ParameterFilter { $FilePath -eq 'dotnet' -and $ArgumentList[0] -eq 'test' -and $ArgumentList[1] -eq '--project' }
    }
}

Describe 'Invoke-DevDocumentationGate' {
    It 'runs the five lint services that CI runs, then the documentation auditor' {
        Mock -ModuleName Dev.Project Write-DevMessage { }
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { 0 }

        Invoke-DevDocumentationGate | Should -Be 0

        foreach ($service in 'markdownlint', 'links', 'spelling', 'diagrams', 'secrets-worktree') {
            Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains $service -and $ArgumentList -contains 'tools/lint/compose.yaml' }
        }
        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $FilePath -eq 'dotnet' -and $ArgumentList -contains 'tools/Documentation.Auditor' }
    }

    It 'runs every check even when one fails, so one run shows every problem' {
        Mock -ModuleName Dev.Project Write-DevMessage { }
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { if ($ArgumentList -contains 'links') { 2 } else { 0 } }

        Invoke-DevDocumentationGate | Should -Be 2

        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 6 -Exactly
    }
}

Describe 'Invoke-DevDocumentation' {
    BeforeEach {
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { 0 }
    }

    It 'restores the tools, then runs the metadata and build steps through the script that accepts exactly one warning' {
        Invoke-DevDocumentation | Should -Be 0

        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $FilePath -eq 'dotnet' -and $ArgumentList -contains 'restore' }
        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'metadata' -and ($ArgumentList -join ' ') -like '*Invoke-DocFx.ps1*' }
        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'build' -and ($ArgumentList -join ' ') -like '*Invoke-DocFx.ps1*' }
    }

    It 'serves the site only when asked' {
        Invoke-DevDocumentation | Out-Null
        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 0 -Exactly -ParameterFilter { $ArgumentList -contains 'serve' }

        Invoke-DevDocumentation -Serve | Out-Null
        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'serve' }
    }

    It 'stops at the first failing step' {
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { if ($ArgumentList -contains 'metadata') { 4 } else { 0 } }

        Invoke-DevDocumentation | Should -Be 4

        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 0 -Exactly -ParameterFilter { $ArgumentList -contains 'build' }
    }
}

Describe 'Invoke-DevBuild' {
    It 'builds the solution in Release' {
        Mock -ModuleName Dev.Project Invoke-DevNativeLive { 0 }

        Invoke-DevBuild | Should -Be 0

        Should -Invoke -ModuleName Dev.Project Invoke-DevNativeLive -Times 1 -Exactly -ParameterFilter { $ArgumentList -contains 'ProductCatalog.slnx' -and $ArgumentList -contains 'Release' }
    }
}
