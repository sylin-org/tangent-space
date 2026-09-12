function New-TangentLocalConfiguration {
    param($Fixture, [string]$ManagingApp, [string]$Origin, [string]$StateDirectory, [string]$OwnerDid = '')
    # Standalone starts unclaimed; preserve the native fixture launcher's explicit mode.
    if ($Fixture -and -not $PSBoundParameters.ContainsKey('OwnerDid')) { $OwnerDid = @($Fixture.accounts | Where-Object role -eq 'owner')[0].did }
    if ($OwnerDid -and $OwnerDid -notmatch '^did:[a-z]+:[A-Za-z0-9._:%-]+$') { throw 'OwnerDid must be blank or a valid AT DID.' }
    $scopes = @('atproto')
    if ($Fixture) {
        $authority = @($Fixture.accounts | Where-Object role -eq 'authority')[0]
        $participantScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read&action=create&action=update&action=delete"
        $authorityScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read_self&action=read&manage=create"
        $scopes += @($participantScope, $authorityScope)
    }
    $clientId = 'http://localhost?redirect_uri=' + [Uri]::EscapeDataString("$Origin/auth/atproto/callback") + '&scope=' + [Uri]::EscapeDataString(($scopes -join ' '))
$settings = [ordered]@{
    Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
    Tangent = @{ Site = @{ Name = 'Tangent Space'; OwnerDid = $OwnerDid }; Conversation = @{ Storage = 'Local' } }
    Koan = @{
        Identity = @{ Posture = 'Closed'; SeedDevUsers = $false }
        Security = @{ Trust = @{ DevIdentity = @{ Enabled = $false } } }
        Data = @{ Sources = @{ Default = @{ Adapter = 'sqlite'; ConnectionString = "Data Source=$($StateDirectory.TrimEnd('/') + '/tangent.sqlite')" } } }
        # The in-process embedding model ships in the image under /app/models; relative paths
        # resolve against the app base directory. Keep in step with src/server/web/appsettings.json.
        Ai = @{ Onnx = @{ ModelPath = 'models/all-MiniLM-L6-v2/model_quantized.onnx'; VocabPath = 'models/all-MiniLM-L6-v2/vocab.txt'; ModelName = 'all-MiniLM-L6-v2' } }
        Web = @{ Auth = @{
            PreferredProviderId = 'atproto'
            Providers = @{ atproto = @{ Type = 'atproto'; ClientId = $clientId; Scopes = $scopes } }
            Atproto = @{
                PlcDirectory = 'https://plc.directory'
                SessionDirectory = ($StateDirectory.TrimEnd('/') + '/oauth')
                MaximumResponseBytes = 8388608
            }
        } }
    }
}
if ($Fixture) {
    $settings.Tangent.Spaces = @{ AuthorityDid = $authority.did; ManagingApp = $ManagingApp
        UnsupportedProviderOrigins = @('https://cortinarius.us-west.host.bsky.network') }
    $settings.Koan.Web.Auth.Atproto.DevelopmentPlcDirectory = $Fixture.plc
    $settings.Koan.Web.Auth.Atproto.DevelopmentAllowedOrigins = @($Fixture.plc, $Fixture.pds1, $Fixture.pds2)
    $settings.Koan.Web.Auth.Atproto.DevelopmentHandles = @{}
    foreach ($account in $Fixture.accounts) { $settings.Koan.Web.Auth.Atproto.DevelopmentHandles[$account.handle] = $account.did }
}

return $settings
}

# Launch-time configuration placement for the Docker state directory. An existing
# appsettings.json is retained byte-for-byte; a fresh one is generated only when missing,
# so repeat launches never rewrite a working configuration.
function Save-TangentDockerConfiguration {
    [CmdletBinding()]
    param($Fixture, [string]$ManagingApp, [Parameter(Mandatory)][string]$Origin,
        [Parameter(Mandatory)][string]$HostStateDirectory, [string]$OwnerDid = '')
    if (-not (Test-Path -LiteralPath $HostStateDirectory -PathType Container)) { throw "The Docker state directory does not exist: $HostStateDirectory" }
    $settingsPath = Join-Path $HostStateDirectory 'appsettings.json'
    if (Test-Path -LiteralPath $settingsPath) {
        Write-Verbose "Retained existing configuration byte-for-byte: $settingsPath"
        return [pscustomobject]@{ status = 'retained'; path = $settingsPath }
    }
    if ($Fixture -and -not $ManagingApp) { throw 'A managing-app service registration is required to generate a fresh fixture configuration.' }
    $settings = New-TangentLocalConfiguration -Fixture $Fixture -ManagingApp $ManagingApp -Origin $Origin -StateDirectory '/state' -OwnerDid $OwnerDid
    # In Docker the fixture origins are reached through the host gateway while their URLs stay unchanged.
    if ($Fixture) { $settings.Koan.Web.Auth.Atproto.DevelopmentConnectHost = 'host.docker.internal' }
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8
    return [pscustomobject]@{ status = 'created'; path = $settingsPath }
}

# Validates the fixture-network marker inside the Docker state directory. An existing marker
# that names a different disposable network is rejected; a missing marker is created.
function Set-TangentNetworkMarker {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$HostStateDirectory, [Parameter(Mandatory)][long]$NetworkId)
    if (-not (Test-Path -LiteralPath $HostStateDirectory -PathType Container)) { throw "The Docker state directory does not exist: $HostStateDirectory" }
    $markerPath = Join-Path $HostStateDirectory 'network.json'
    if (Test-Path -LiteralPath $markerPath) {
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ([long]$marker.networkId -ne $NetworkId) {
            throw "This Docker database belongs to fixture network $($marker.networkId), not $NetworkId. Preserve it and choose a fresh state directory before creating a new demonstration."
        }
        return [pscustomobject]@{ status = 'matched'; networkId = $NetworkId; path = $markerPath }
    }
    @{ networkId = $NetworkId } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding utf8
    return [pscustomobject]@{ status = 'created'; networkId = $NetworkId; path = $markerPath }
}
