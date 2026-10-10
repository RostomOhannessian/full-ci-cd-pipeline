#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-001, REQ-DX-002, REQ-CI-005

BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $script:Container = Join-Path $script:Root '.devcontainer'
    $script:Dockerfile = Get-Content -Path (Join-Path $script:Container 'Dockerfile')
    $script:ConfigText = Get-Content -Path (Join-Path $script:Container 'devcontainer.json') -Raw
    $script:Config = $script:ConfigText | ConvertFrom-Json
    $script:Lock = Get-Content -Path (Join-Path $script:Container 'devcontainer-lock.json') -Raw | ConvertFrom-Json
    $script:Workflow = Get-Content -Path (Join-Path $script:Root '.github' 'workflows' 'devcontainer.yml')
    $script:WorkflowText = $script:Workflow -join "`n"
    $script:Features = @($script:Config.features.PSObject.Properties.Name)
}

Describe 'the Dev Container image' {
    It 'starts from one base image that is pinned by tag and digest' {
        $from = @($script:Dockerfile | Where-Object { $_ -match '^\s*FROM\s' })

        $from | Should -HaveCount 1
        $from[0] | Should -Match '^FROM\s+\S+:[A-Za-z0-9][A-Za-z0-9._-]*@sha256:[0-9a-f]{64}\s*$'
    }

    It 'uses the Microsoft .NET Dev Container image, which carries the SDK that global.json pins' {
        ($script:Dockerfile | Where-Object { $_ -match '^\s*FROM\s' }) | Should -Match '^FROM mcr\.microsoft\.com/devcontainers/dotnet:10\.0@'
    }

    It 'turns telemetry off for the tools in the container' {
        $text = $script:Dockerfile -join "`n"

        $text | Should -Match 'DOTNET_CLI_TELEMETRY_OPTOUT=1'
        $text | Should -Match 'POWERSHELL_TELEMETRY_OPTOUT=1'
    }
}

Describe 'devcontainer.json' {
    It 'builds from the Dockerfile beside it' {
        $script:Config.build.dockerfile | Should -Be 'Dockerfile'
        $script:Config.PSObject.Properties.Name | Should -Not -Contain 'image'
    }

    It 'adds docker-in-docker, so the stack runs inside the container and no host socket is shared' {
        $script:Features | Should -Contain 'ghcr.io/devcontainers/features/docker-in-docker:4'
        $script:ConfigText | Should -Not -Match 'docker\.sock'
        ($script:Config.PSObject.Properties.Name -contains 'mounts') | Should -BeFalse
    }

    It 'pins every feature to a major version and takes only features from the official namespace' {
        $script:Features.Count | Should -BeGreaterOrEqual 2
        foreach ($feature in $script:Features) {
            $feature | Should -Match '^ghcr\.io/devcontainers/features/[a-z-]+:\d+$' -Because "$feature needs a major version, and the lock file pins the rest"
        }
    }

    It 'runs the bootstrap and then dev doctor after the container is created' {
        $script:Config.postCreateCommand | Should -Match '\./dev bootstrap'
        $script:Config.postCreateCommand | Should -Match '\./dev doctor'
        $script:Config.postCreateCommand.IndexOf('bootstrap') | Should -BeLessThan $script:Config.postCreateCommand.IndexOf('doctor')
    }

    It 'starts the shim through sh, so a lost execute bit cannot break the command' {
        $script:Config.postCreateCommand | Should -Match '(^|&&\s*)sh \./dev '
    }

    It 'states the resources that the api profile needs' {
        $script:Config.hostRequirements.cpus | Should -BeGreaterOrEqual 4
        $script:Config.hostRequirements.memory | Should -Match '^\d+gb$'
    }

    It 'forwards only the two browser ports and never a database port' {
        @($script:Config.forwardPorts | Sort-Object) | Should -Be @(3000, 8180)
    }
}

Describe 'devcontainer-lock.json' {
    It 'locks every feature that devcontainer.json names, and nothing else' {
        $locked = @($script:Lock.features.PSObject.Properties.Name | Sort-Object)

        $locked | Should -Be @($script:Features | Sort-Object)
    }

    It 'pins each feature to an exact version and to the digest of its OCI artifact' {
        foreach ($property in $script:Lock.features.PSObject.Properties) {
            $entry = $property.Value
            $entry.version | Should -Match '^\d+\.\d+\.\d+$' -Because $property.Name
            $entry.integrity | Should -Match '^sha256:[0-9a-f]{64}$' -Because $property.Name
            $entry.resolved | Should -Match '@sha256:[0-9a-f]{64}$' -Because $property.Name
            $entry.resolved | Should -Match "@$([regex]::Escape($entry.integrity))$" -Because "$($property.Name) must resolve to the digest it records"
        }
    }

    It 'keeps each locked version inside the major version that devcontainer.json asks for' {
        foreach ($property in $script:Lock.features.PSObject.Properties) {
            $major = $property.Name.Split(':')[-1]
            $property.Value.version | Should -Match "^$major\." -Because $property.Name
        }
    }
}

Describe 'the dev shims' {
    It 'starts PowerShell through the local tool and never through whatever is installed' {
        $shim = Get-Content -Path (Join-Path $script:Root 'dev') -Raw

        $shim | Should -Match 'dotnet tool restore'
        $shim | Should -Match 'dotnet pwsh|dotnet tool run pwsh'
        $shim | Should -Match 'scripts/dev\.ps1'
    }

    It 'has a Windows shim that does the same' {
        $shim = Get-Content -Path (Join-Path $script:Root 'dev.cmd') -Raw

        $shim | Should -Match 'dotnet tool restore'
        $shim | Should -Match 'scripts\\dev\.ps1'
    }

    It 'keeps the Linux shim with LF line endings and the execute bit in the index' {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $script:Root 'dev'))
        $bytes | Should -Not -Contain 13

        $git = Get-Command git -ErrorAction SilentlyContinue
        if (-not $git) { Set-ItResult -Skipped -Because 'git is not available' ; return }
        $entry = & git -C $script:Root ls-files -s -- dev 2>$null
        if (-not $entry) { Set-ItResult -Skipped -Because 'the file is not in a Git index' ; return }
        $entry | Should -Match '^100755 '
    }
}

Describe 'the devcontainer workflow' {
    It 'runs when the Dev Container changes, every week, and by hand (REQ-CI-005)' {
        $script:WorkflowText | Should -Match '(?m)^on:'
        $script:WorkflowText | Should -Match '(?m)^  pull_request:'
        $script:WorkflowText | Should -Match '(?m)^  schedule:'
        $script:WorkflowText | Should -Match '(?m)^  workflow_dispatch:'
        $script:WorkflowText | Should -Match '- cron: "\d+ \d+ \* \* \d"'
        $script:WorkflowText | Should -Match '- "\.devcontainer/\*\*"'
    }

    It 'triggers on every path that the Dev Container onboarding reads' {
        foreach ($path in 'scripts/**', 'compose.yaml', '.env.example', 'identity/**', 'tools/versions.yaml', '.config/dotnet-tools.json', 'global.json', 'dev', 'dev.cmd') {
            $script:WorkflowText | Should -Match ([regex]::Escape("- `"$path`"")) -Because $path
        }
    }

    It 'never runs on pull_request_target and never uses a secret' {
        $script:WorkflowText | Should -Not -Match 'pull_request_target'
        $script:WorkflowText | Should -Not -Match '\$\{\{\s*secrets\.'
    }

    It 'starts with no permissions and gives the job read access to the contents only' {
        $script:WorkflowText | Should -Match '(?m)^permissions: \{\}'
        $script:WorkflowText | Should -Match '(?ms)^    permissions:\s*\n      contents: read\s*\n    steps:'
        $script:WorkflowText | Should -Not -Match '(?m)^\s+(contents|packages|id-token|pull-requests): write'
    }

    It 'pins every action by commit SHA and keeps no credential after the checkout' {
        $uses = @($script:Workflow | Where-Object { $_ -match '^\s*(-\s+)?uses:' })

        $uses.Count | Should -BeGreaterOrEqual 2
        foreach ($line in $uses) { $line | Should -Match '@[0-9a-f]{40}\s+#\s*v' -Because $line }
        $script:WorkflowText | Should -Match 'persist-credentials: false'
    }

    It 'never pushes the image and has a timeout' {
        $script:WorkflowText | Should -Match '(?m)^\s+push: never'
        $script:WorkflowText | Should -Match '(?m)^\s+timeout-minutes: \d+'
    }

    It 'runs doctor, the build, the unit tests, and the whole of Lab 1 in the container (REQ-DX-001)' {
        $region = [regex]::Match($script:WorkflowText, '(?s)# <onboarding>\n(.*?)\n\s*# </onboarding>').Groups[1].Value

        $region | Should -Not -BeNullOrEmpty
        foreach ($command in 'dev doctor', 'dev build', 'dev test unit', 'dev lab 1 setup', 'dev lab 1 run', 'dev lab 1 cleanup') {
            $region | Should -Match ([regex]::Escape("sh ./$command")) -Because $command
        }
    }

    It 'keeps the job name that the blast-radius map and the documentation expect' {
        $script:WorkflowText | Should -Match '(?m)^  devcontainer:\s*\n    name: Dev Container onboarding'
    }
}
