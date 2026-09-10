function New-TangentLocalConfiguration {
    param($Fixture, [string]$ManagingApp, [string]$Origin, [string]$StateDirectory)
    $owner = @($Fixture.accounts | Where-Object role -eq 'owner')[0]
    $authority = @($Fixture.accounts | Where-Object role -eq 'authority')[0]
    $participantScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read&action=create"
    $authorityScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read_self&action=read&manage=create"
    $scopes = @('atproto', $participantScope, $authorityScope)
    $clientId = 'http://localhost?redirect_uri=' + [Uri]::EscapeDataString("$Origin/auth/atproto/callback") + '&scope=' + [Uri]::EscapeDataString(($scopes -join ' '))
$settings = [ordered]@{
    Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
    Tangent = @{ Site = @{ Name = 'Tangent Space'; OwnerDid = $owner.did }; Spaces = @{ AuthorityDid = $authority.did; ManagingApp = $ManagingApp
        # This origin was actually tested and rejected experimental Spaces consent. The compatible fixtures do not use it.
        UnsupportedProviderOrigins = @('https://cortinarius.us-west.host.bsky.network') } }
    Koan = @{
        Identity = @{ Posture = 'Closed'; SeedDevUsers = $false }
        Security = @{ Trust = @{ DevIdentity = @{ Enabled = $false } } }
        Data = @{ Sources = @{ Default = @{ Adapter = 'sqlite'; ConnectionString = "Data Source=$($StateDirectory.TrimEnd('/') + '/tangent.sqlite')" } } }
        Web = @{ Auth = @{
            PreferredProviderId = 'atproto'
            Providers = @{ atproto = @{ Type = 'atproto'; ClientId = $clientId; Scopes = $scopes } }
            Atproto = @{
                PlcDirectory = 'https://plc.directory'; DevelopmentPlcDirectory = $fixture.plc
                SessionDirectory = ($StateDirectory.TrimEnd('/') + '/oauth')
                MaximumResponseBytes = 8388608
                DevelopmentAllowedOrigins = @($fixture.plc, $fixture.pds1, $fixture.pds2)
                DevelopmentHandles = @{}
            }
        } }
    }
}
foreach ($account in $fixture.accounts) { $settings.Koan.Web.Auth.Atproto.DevelopmentHandles[$account.handle] = $account.did }

return $settings
}
