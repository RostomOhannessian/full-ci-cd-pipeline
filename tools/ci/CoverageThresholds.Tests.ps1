#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'CoverageThresholds.psm1') -Force

    function Initialize-FakeRepository {
        param([string[]] $Assemblies = @(), [string[]] $ExtraFiles = @())
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $root 'src') -Force | Out-Null
        foreach ($name in $Assemblies) {
            New-Item -ItemType Directory -Path (Join-Path $root "src/$name") -Force | Out-Null
            Set-Content -Path (Join-Path $root "src/$name/Code.cs") -Value 'class C {}'
        }
        foreach ($file in $ExtraFiles) {
            $path = Join-Path $root $file
            New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
            Set-Content -Path $path -Value 'class G {}'
        }
        return $root
    }

    # A Cobertura file. Each class is @{ Package; File; Lines = @(@{ N; Hits; Branch = '1/2' }) }.
    function Initialize-CoberturaFile {
        param([hashtable[]] $Classes)
        $xml = '<?xml version="1.0" encoding="utf-8"?><coverage><packages>'
        foreach ($package in ($Classes | Group-Object { $_.Package })) {
            $xml += "<package name=""$($package.Name)""><classes>"
            foreach ($class in $package.Group) {
                $xml += "<class name=""X"" filename=""$($class.File)""><lines>"
                foreach ($line in $class.Lines) {
                    $branch = if ($line.Branch) { " branch=""True"" condition-coverage=""50% ($($line.Branch))""" } else { ' branch="False"' }
                    $xml += "<line number=""$($line.N)"" hits=""$($line.Hits)""$branch />"
                }
                $xml += '</lines></class>'
            }
            $xml += '</classes></package>'
        }
        $xml += '</packages></coverage>'
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.cobertura.xml')
        Set-Content -Path $path -Value $xml
        return $path
    }

    # Ten lines in one class: $Covered of them have hits. Branch counts are given as 'covered/total' on the first line.
    function Get-CoveredLine {
        param([int] $Covered, [int] $Total = 10, [string] $Branch)
        1..$Total | ForEach-Object { @{ N = $_; Hits = [int]($_ -le $Covered); Branch = $(if ($_ -eq 1) { $Branch }) } }
    }

    $script:policy = Get-Content -Raw (Join-Path $PSScriptRoot '../../governance/policies/coverage-thresholds.json') | ConvertFrom-Json

    function Invoke-CoverageCheck {
        param([string] $Root, [string[]] $Reports)
        $report = Read-CoverageReport -Path $Reports -ExcludePath @($script:policy.excludePaths)
        return Get-CoverageResult -Report $report -Policy $script:policy -RepositoryRoot $Root
    }
}

Describe 'The coverage policy' {
    It 'carries the thresholds from plan section 10.4' {
        $domain = $script:policy.assemblies | Where-Object name -EQ 'Catalog.Domain'
        $application = $script:policy.assemblies | Where-Object name -EQ 'Catalog.Application'
        $domain.line | Should -Be 90
        $domain.branch | Should -Be 80
        $application.line | Should -Be 85
        $application.branch | Should -Be 80
        $script:policy.solution.line | Should -Be 80
    }
}

Describe 'Get-CoverageResult' {
    Context 'before any code exists' {
        It 'reports no code and does not fail' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
            Remove-Item (Join-Path $root 'src/Catalog.Domain/Code.cs')
            $report = Initialize-CoberturaFile -Classes @()

            $results = Invoke-CoverageCheck -Root $root -Reports $report

            ($results | Where-Object Status -EQ 'Fail') | Should -BeNullOrEmpty
            ($results | Where-Object Scope -EQ 'Catalog.Domain').Status | Should -Be 'N/A'
            ($results | Where-Object Scope -EQ 'Solution').Status | Should -Be 'N/A'
        }
    }

    Context 'an assembly with thresholds' {
        It 'passes at or above the line and branch thresholds' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
            $report = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Domain'; File = '/r/src/Catalog.Domain/A.cs'; Lines = (Get-CoveredLine -Covered 9 -Branch '4/5') })

            $results = Invoke-CoverageCheck -Root $root -Reports $report

            ($results | Where-Object { $_.Scope -eq 'Catalog.Domain' -and $_.Metric -eq 'lines' }).Status | Should -Be 'Pass'
            ($results | Where-Object { $_.Scope -eq 'Catalog.Domain' -and $_.Metric -eq 'branches' }).Status | Should -Be 'Pass'
        }

        It 'fails below the line threshold' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
            $report = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Domain'; File = '/r/src/Catalog.Domain/A.cs'; Lines = (Get-CoveredLine -Covered 8 -Branch '4/5') })

            $result = Invoke-CoverageCheck -Root $root -Reports $report | Where-Object { $_.Scope -eq 'Catalog.Domain' -and $_.Metric -eq 'lines' }

            $result.Status | Should -Be 'Fail'
            $result.Actual | Should -Be 80
        }

        It 'fails below the branch threshold' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
            $report = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Domain'; File = '/r/src/Catalog.Domain/A.cs'; Lines = (Get-CoveredLine -Covered 10 -Branch '3/5') })

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object { $_.Scope -eq 'Catalog.Domain' -and $_.Metric -eq 'branches' }).Status | Should -Be 'Fail'
        }

        It 'uses the Application thresholds for Application' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Application'
            $report = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Application'; File = '/r/src/Catalog.Application/A.cs'; Lines = (Get-CoveredLine -Covered 85 -Total 100 -Branch '4/5') })

            ((Invoke-CoverageCheck -Root $root -Reports $report) | Where-Object { $_.Scope -eq 'Catalog.Application' -and $_.Metric -eq 'lines' }).Status | Should -Be 'Pass'
        }
    }

    Context 'an assembly that has code but no coverage data' {
        It 'fails, because not measured must not read as covered' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
            $report = Initialize-CoberturaFile -Classes @()

            $result = Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Catalog.Domain'

            $result.Status | Should -Be 'Fail'
            $result.Note | Should -BeLike '*no coverage data*'
        }

        It 'is exempt when the policy says so, and names the work package that ends the exemption' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Api'
            $report = Initialize-CoberturaFile -Classes @()

            $result = Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Catalog.Api'

            $result.Status | Should -Be 'Exempt'
            $result.Note | Should -BeLike '*WP1.10*'
        }

        It 'fails for an assembly that is not exempt, such as a new layer' {
            $root = Initialize-FakeRepository -Assemblies 'Catalog.Infrastructure'
            $report = Initialize-CoberturaFile -Classes @()

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Catalog.Infrastructure').Status | Should -Be 'Fail'
        }
    }

    Context 'the solution threshold' {
        It 'fails below 80 percent across the solution' {
            $root = Initialize-FakeRepository
            $report = Initialize-CoberturaFile -Classes @(
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 7) })

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Solution').Status | Should -Be 'Fail'
        }

        It 'passes at 80 percent across the solution' {
            $root = Initialize-FakeRepository
            $report = Initialize-CoberturaFile -Classes @(
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 8) })

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Solution').Status | Should -Be 'Pass'
        }

        It 'ignores test assemblies' {
            $root = Initialize-FakeRepository
            $report = Initialize-CoberturaFile -Classes @(
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 10) },
                @{ Package = 'Catalog.Domain.Tests'; File = '/r/tests/Catalog.Domain.Tests/T.cs'; Lines = (Get-CoveredLine -Covered 0) })

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Solution').Status | Should -Be 'Pass'
        }
    }

    Context 'generated code and migrations' {
        It 'does not count migrations or generated files' {
            $root = Initialize-FakeRepository
            $report = Initialize-CoberturaFile -Classes @(
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 10) },
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/Migrations/M1.cs'; Lines = (Get-CoveredLine -Covered 0) },
                @{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/Client.g.cs'; Lines = (Get-CoveredLine -Covered 0) })

            $result = Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Solution'

            $result.Status | Should -Be 'Pass'
            $result.Actual | Should -Be 100
        }

        It 'treats an assembly that holds only migrations as having no code' {
            $root = Initialize-FakeRepository -ExtraFiles 'src/Catalog.Infrastructure/Migrations/M1.cs'
            $report = Initialize-CoberturaFile -Classes @()

            (Invoke-CoverageCheck -Root $root -Reports $report | Where-Object Scope -EQ 'Catalog.Infrastructure').Status | Should -Be 'N/A'
        }
    }

    Context 'several reports' {
        It 'counts a line once, and a line is covered when any report covers it' {
            $root = Initialize-FakeRepository
            $first = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 5) })
            $second = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Infrastructure'; File = '/r/src/Catalog.Infrastructure/A.cs'; Lines = (Get-CoveredLine -Covered 9) })

            $result = Invoke-CoverageCheck -Root $root -Reports @($first, $second) | Where-Object Scope -EQ 'Solution'

            $result.Actual | Should -Be 90
        }
    }
}

Describe 'Format-CoverageSummary' {
    It 'renders a Markdown table with one row per result' {
        $results = @(
            [pscustomobject]@{ Scope = 'Solution'; Metric = 'lines'; Required = 80; Actual = 91.5; Status = 'Pass'; Note = '1 of 1.' },
            [pscustomobject]@{ Scope = 'Catalog.Api'; Metric = 'lines'; Required = $null; Actual = $null; Status = 'Exempt'; Note = 'Reason.' })

        $lines = Format-CoverageSummary -Result $results

        $lines.Count | Should -Be 4
        $lines[2] | Should -Be '| Solution | lines | 80% | 91.5% | Pass | 1 of 1. |'
        $lines[3] | Should -Be '| Catalog.Api | lines |  |  | Exempt | Reason. |'
    }
}

Describe 'Test-CoverageThresholds.ps1' {
    It 'exits with code 1 when no coverage file matches, because the tests must collect coverage' {
        $null = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-CoverageThresholds.ps1') -CoverageFile (Join-Path $TestDrive 'missing/*.cobertura.xml') 2>&1
        $LASTEXITCODE | Should -Be 1
    }

    It 'exits with code 1 when a threshold fails and 0 when all pass' {
        $root = Initialize-FakeRepository -Assemblies 'Catalog.Domain'
        $bad = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Domain'; File = '/r/src/Catalog.Domain/A.cs'; Lines = (Get-CoveredLine -Covered 5 -Branch '1/5') })
        $good = Initialize-CoberturaFile -Classes @(@{ Package = 'Catalog.Domain'; File = '/r/src/Catalog.Domain/A.cs'; Lines = (Get-CoveredLine -Covered 10 -Branch '5/5') })
        $script = Join-Path $PSScriptRoot 'Test-CoverageThresholds.ps1'

        $null = & pwsh -NoProfile -File $script -CoverageFile $bad -RepositoryRoot $root
        $failed = $LASTEXITCODE
        $null = & pwsh -NoProfile -File $script -CoverageFile $good -RepositoryRoot $root
        $passed = $LASTEXITCODE

        $failed | Should -Be 1
        $passed | Should -Be 0
    }
}
