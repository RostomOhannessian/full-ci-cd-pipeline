#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-DX-003

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'Dev.Yaml.psm1') -Force
}

Describe 'ConvertFrom-DevYaml' {
    It 'reads a mapping with scalar values, comments, and quotes' {
        $yaml = @'
# a comment
schema-version: 1
name: "Quoted: value"   # trailing comment
other: 'it''s fine'
plain: five # not part of the value
empty:
'@
        $result = ConvertFrom-DevYaml -Text $yaml

        $result['schema-version'] | Should -Be '1'
        $result['name'] | Should -Be 'Quoted: value'
        $result['other'] | Should -Be "it's fine"
        $result['plain'] | Should -Be 'five'
        $result['empty'] | Should -BeNullOrEmpty
    }

    It 'reads a list of mappings, as tools/versions.yaml uses' {
        $yaml = @'
tools:
  - id: pester
    version: 5.9.0
  - id: other
    version: 1.0.0
'@
        $result = ConvertFrom-DevYaml -Text $yaml

        $result['tools'] | Should -HaveCount 2
        $result['tools'][0]['id'] | Should -Be 'pester'
        $result['tools'][1]['version'] | Should -Be '1.0.0'
    }

    It 'reads nested mappings and lists of scalars' {
        $yaml = @'
outer:
  inner:
    key: value
  list:
    - one
    - two
  none: []
'@
        $result = ConvertFrom-DevYaml -Text $yaml

        $result['outer']['inner']['key'] | Should -Be 'value'
        $result['outer']['list'] | Should -Be @('one', 'two')
        @($result['outer']['none']) | Should -HaveCount 0
    }

    It 'returns nothing for a document with only comments' {
        ConvertFrom-DevYaml -Text "# nothing here`n" | Should -BeNullOrEmpty
    }

    It 'reads the real tool manifest' {
        $path = Join-Path $PSScriptRoot '..' '..' 'tools' 'versions.yaml'

        $result = ConvertFrom-DevYaml -Text (Get-Content -Path $path -Raw)

        $result['schema-version'] | Should -Be '1'
        $result['tools'].Count | Should -BeGreaterThan 0
    }

    Context 'refuses what it cannot read exactly' {
        It 'rejects <Name>' -ForEach @(
            @{ Name = 'a tab in the indentation'; Yaml = "tools:`n`t- id: x"; Pattern = '*tabs*' }
            @{ Name = 'an anchor'; Yaml = "a: &anchor value"; Pattern = '*anchors*' }
            @{ Name = 'an alias'; Yaml = "a: *anchor"; Pattern = '*aliases*' }
            @{ Name = 'a tag'; Yaml = "a: !!str value"; Pattern = '*tags*' }
            @{ Name = 'a block scalar'; Yaml = "a: |`n  text"; Pattern = '*block scalars*' }
            @{ Name = 'a flow mapping'; Yaml = "a: { b: c }"; Pattern = '*flow collections*' }
            @{ Name = 'a flow sequence'; Yaml = "a: [b, c]"; Pattern = '*flow collections*' }
            @{ Name = 'a document marker'; Yaml = "---`na: b"; Pattern = '*document markers*' }
            @{ Name = 'a merge key'; Yaml = "<<: *base"; Pattern = '*expected*' }
            @{ Name = 'a duplicate key'; Yaml = "a: 1`na: 2"; Pattern = '*appears twice*' }
            @{ Name = 'a quoted key'; Yaml = "'a': 1"; Pattern = '*expected*' }
            @{ Name = 'an unclosed quote'; Yaml = 'a: "open'; Pattern = '*not closed*' }
            @{ Name = 'a list at the indentation of its key'; Yaml = "a:`n- b"; Pattern = '*indented under its key*' }
            @{ Name = 'indentation that jumps'; Yaml = "a: 1`n    b: 2"; Pattern = '*indentation*' }
        ) {
            { ConvertFrom-DevYaml -Text $Yaml -Source 'sample.yaml' } | Should -Throw $Pattern
        }

        It 'names the file and the line of the problem' {
            { ConvertFrom-DevYaml -Text "a: 1`nb: &x 2" -Source 'sample.yaml' } | Should -Throw 'sample.yaml:2:*'
        }
    }
}
