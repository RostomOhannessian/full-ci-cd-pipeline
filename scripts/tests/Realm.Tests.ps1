#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# requirements: REQ-IDN-001, REQ-IDN-005

BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $script:RealmPath = Join-Path $script:Root 'identity' 'keycloak' 'realms' 'catalog-realm.json'
    $script:RealmText = Get-Content -Path $script:RealmPath -Raw
    $script:Realm = $script:RealmText | ConvertFrom-Json -Depth 20
    $script:Compose = Get-Content -Path (Join-Path $script:Root 'compose.yaml') -Raw
    $script:EnvExample = Get-Content -Path (Join-Path $script:Root '.env.example') -Raw

    function Get-Client { param([string] $Id) $script:Realm.clients | Where-Object clientId -EQ $Id }
    function Get-User { param([string] $Name) $script:Realm.users | Where-Object username -EQ $Name }
}

Describe 'The catalog realm file' {
    It 'is the catalog realm, enabled, and requires TLS for external clients' {
        $script:Realm.realm | Should -Be 'catalog'
        $script:Realm.enabled | Should -BeTrue
        $script:Realm.sslRequired | Should -Be 'external'
    }

    It 'defines the three catalog scopes' {
        $names = @($script:Realm.clientScopes | ForEach-Object name)

        $names | Should -Contain 'catalog.read'
        $names | Should -Contain 'catalog.write'
        $names | Should -Contain 'catalog.admin'
    }

    It 'issues aud=catalog-api through an audience mapper in each catalog scope' {
        foreach ($name in 'catalog.read', 'catalog.write', 'catalog.admin') {
            $scope = $script:Realm.clientScopes | Where-Object name -EQ $name
            $mapper = @($scope.protocolMappers | Where-Object protocolMapper -EQ 'oidc-audience-mapper')

            $mapper | Should -HaveCount 1 -Because $name
            $mapper[0].config.'included.client.audience' | Should -Be 'catalog-api'
            $mapper[0].config.'access.token.claim' | Should -Be 'true'
        }
    }

    It 'defines the three realm roles, with each higher role including the one below' {
        $roles = @{}
        foreach ($role in $script:Realm.roles.realm) { $roles[$role.name] = $role }

        $roles.Keys | Should -Contain 'catalog-reader'
        $roles['catalog-editor'].composites.realm | Should -Contain 'catalog-reader'
        $roles['catalog-admin'].composites.realm | Should -Contain 'catalog-editor'
        $roles['catalog-reader'].PSObject.Properties['composites'] | Should -BeNullOrEmpty
    }

    It 'maps each scope to one realm role, so the role claim follows the scopes that were requested' {
        $mappings = @{}
        foreach ($mapping in $script:Realm.scopeMappings) { $mappings[$mapping.clientScope] = @($mapping.roles) }

        $mappings['catalog.read'] | Should -Be @('catalog-reader')
        $mappings['catalog.write'] | Should -Be @('catalog-editor')
        $mappings['catalog.admin'] | Should -Be @('catalog-admin')
    }

    It 'puts the realm roles in the token through the basic scope, so the API can check the role as well as the scope' {
        $basic = $script:Realm.clientScopes | Where-Object name -EQ 'basic'
        $mapper = $basic.protocolMappers | Where-Object protocolMapper -EQ 'oidc-usermodel-realm-role-mapper'

        $mapper.config.'claim.name' | Should -Be 'realm_access.roles'
        $mapper.config.'access.token.claim' | Should -Be 'true'
    }

    It 'allows only the algorithm family that the plan allows' {
        $script:Realm.defaultSignatureAlgorithm | Should -BeIn 'RS256', 'ES256'
    }

    It 'turns on brute-force protection and short access tokens, and turns off self-registration' {
        $script:Realm.bruteForceProtected | Should -BeTrue
        $script:Realm.accessTokenLifespan | Should -BeLessOrEqual 300
        $script:Realm.registrationAllowed | Should -BeFalse
    }
}

Describe 'The clients in the catalog realm' {
    It 'defines the four clients of plan section 8.7' {
        @($script:Realm.clients | ForEach-Object clientId) | Should -Be @('catalog-api', 'catalog-cli', 'synthetic-shopper', 'smoke-tests')
    }

    It 'makes catalog-api a resource server that never asks for a token' {
        $api = Get-Client 'catalog-api'

        $api.bearerOnly | Should -BeTrue
        $api.standardFlowEnabled | Should -BeFalse
        $api.serviceAccountsEnabled | Should -BeFalse
    }

    It 'makes catalog-cli a public client with PKCE (S256) and the device flow, and no secret' {
        $cli = Get-Client 'catalog-cli'

        $cli.publicClient | Should -BeTrue
        $cli.attributes.'pkce.code.challenge.method' | Should -Be 'S256'
        $cli.attributes.'oauth2.device.authorization.grant.enabled' | Should -Be 'true'
        $cli.PSObject.Properties['secret'] | Should -BeNullOrEmpty
    }

    It 'makes the synthetic shopper and the smoke tests confidential clients that use client credentials' {
        foreach ($id in 'synthetic-shopper', 'smoke-tests') {
            $client = Get-Client $id

            $client.publicClient | Should -BeFalse -Because $id
            $client.serviceAccountsEnabled | Should -BeTrue -Because $id
            $client.standardFlowEnabled | Should -BeFalse -Because $id
        }
    }

    It 'enables no implicit flow and no password grant on any client' {
        foreach ($client in $script:Realm.clients) {
            $client.implicitFlowEnabled | Should -BeFalse -Because $client.clientId
            $client.directAccessGrantsEnabled | Should -BeFalse -Because $client.clientId
        }
    }

    It 'turns off full scope on every client, so a token carries only the roles of the scopes that were requested' {
        foreach ($client in $script:Realm.clients) { $client.fullScopeAllowed | Should -BeFalse -Because $client.clientId }
    }

    It 'gives the synthetic shopper read access only, and the smoke tests every scope' {
        (Get-Client 'synthetic-shopper').defaultClientScopes | Should -Contain 'catalog.read'
        (Get-Client 'synthetic-shopper').defaultClientScopes | Should -Not -Contain 'catalog.write'
        (Get-Client 'synthetic-shopper').defaultClientScopes | Should -Not -Contain 'catalog.admin'
        @((Get-Client 'synthetic-shopper').optionalClientScopes) | Should -HaveCount 0
        (Get-Client 'smoke-tests').defaultClientScopes | Should -Contain 'catalog.admin'
    }

    It 'lets only loopback addresses receive the authorization code' {
        foreach ($uri in (Get-Client 'catalog-cli').redirectUris) { $uri | Should -Match '^http://(127\.0\.0\.1|localhost)/' }
    }
}

Describe 'The users in the catalog realm' {
    It 'gives each demo user exactly one role, and each service account the least role its client needs' {
        (Get-User 'reader').realmRoles | Should -Be @('catalog-reader')
        (Get-User 'editor').realmRoles | Should -Be @('catalog-editor')
        (Get-User 'admin').realmRoles | Should -Be @('catalog-admin')
        (Get-User 'service-account-synthetic-shopper').realmRoles | Should -Be @('catalog-reader')
        (Get-User 'service-account-smoke-tests').realmRoles | Should -Be @('catalog-admin')
    }

    It 'ties each service account to its client' {
        (Get-User 'service-account-synthetic-shopper').serviceAccountClientId | Should -Be 'synthetic-shopper'
        (Get-User 'service-account-smoke-tests').serviceAccountClientId | Should -Be 'smoke-tests'
    }

    It 'gives each demo user the profile that Keycloak requires, on the reserved example domain' {
        foreach ($name in 'reader', 'editor', 'admin') {
            $user = Get-User $name
            $user.email | Should -Match '@catalog\.example$' -Because $name
            $user.firstName | Should -Not -BeNullOrEmpty
            $user.lastName | Should -Not -BeNullOrEmpty
            $user.emailVerified | Should -BeTrue
        }
    }
}

Describe 'The secrets in the catalog realm file' {
    It 'are all placeholders that the environment fills, never values' {
        $values = [regex]::Matches($script:RealmText, '"(?:secret|value)"\s*:\s*"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }

        $values.Count | Should -Be 5
        foreach ($value in $values) { $value | Should -Match '^\$\{[A-Z][A-Z0-9_]*\}$' }
    }

    It 'use only variables that .env.example lists and compose.yaml passes to Keycloak' {
        $names = [regex]::Matches($script:RealmText, '\$\{([A-Z][A-Z0-9_]*)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique

        $names.Count | Should -Be 5
        foreach ($name in $names) {
            $script:EnvExample | Should -Match "(?m)^${name}=" -Because "$name must be in .env.example"
            $script:Compose | Should -Match "(?m)^\s+${name}:\s*\`$\{${name}:\?" -Because "compose.yaml must pass $name to Keycloak"
        }
    }

    It 'leave no realm file with a hostname or a port, so the one file serves every environment' {
        $script:RealmText | Should -Not -Match 'https?://(?!(127\.0\.0\.1|localhost)/)'
        $script:RealmText | Should -Not -Match ':\d{4,5}'
    }
}

Describe 'The realm folder' {
    It 'holds the catalog realm as the single source, and nothing else imports a copy' {
        @(Get-ChildItem -Path (Join-Path $script:Root 'identity' 'keycloak' 'realms') -Filter '*.json' | ForEach-Object Name) | Should -Contain 'catalog-realm.json'

        $copies = @(Get-ChildItem -Path $script:Root -Recurse -Filter '*realm*.json' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[\\/](\.git|node_modules|bin|obj|\.dev|\.docfx|_site)[\\/]' -and $_.FullName -notmatch '[\\/]identity[\\/]keycloak[\\/]realms[\\/]' })
        $copies | Should -HaveCount 0
    }
}
