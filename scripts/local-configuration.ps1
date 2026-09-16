# A fresh configuration for the Docker state directory: SQLite under /state, public atproto
# sign-in, and ownership open for the first verified sign-in unless an explicit DID is given.
function New-TangentLocalConfiguration {
    param([Parameter(Mandatory)][string]$Origin, [Parameter(Mandatory)][string]$StateDirectory, [string]$OwnerDid = '')
    if ($OwnerDid -and $OwnerDid -notmatch '^did:[a-z]+:[A-Za-z0-9._:%-]+$') { throw 'OwnerDid must be blank or a valid AT DID.' }
    $scopes = @('atproto')
    $clientId = 'http://localhost?redirect_uri=' + [Uri]::EscapeDataString("$Origin/auth/atproto/callback") + '&scope=' + [Uri]::EscapeDataString(($scopes -join ' '))
    return [ordered]@{
        Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
        Tangent = @{ Space = @{ Name = 'Tangent Space'; OwnerDid = $OwnerDid } }
        Koan = @{
            Identity = @{ Posture = 'Closed'; SeedDevUsers = $false }
            Security = @{ Trust = @{ DevIdentity = @{ Enabled = $false } } }
            Data = @{ Sources = @{ Default = @{ Adapter = 'sqlite'; ConnectionString = "Data Source=$($StateDirectory.TrimEnd('/') + '/tangent.sqlite')" } } }
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
}

# Launch-time configuration placement for the Docker state directory. An existing
# appsettings.json is retained byte-for-byte; a fresh one is generated only when missing,
# so repeat launches never rewrite a working configuration.
function Save-TangentDockerConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Origin, [Parameter(Mandatory)][string]$HostStateDirectory, [string]$OwnerDid = '')
    if (-not (Test-Path -LiteralPath $HostStateDirectory -PathType Container)) { throw "The Docker state directory does not exist: $HostStateDirectory" }
    $settingsPath = Join-Path $HostStateDirectory 'appsettings.json'
    if (Test-Path -LiteralPath $settingsPath) {
        Write-Verbose "Retained existing configuration byte-for-byte: $settingsPath"
        return [pscustomobject]@{ status = 'retained'; path = $settingsPath }
    }
    New-TangentLocalConfiguration -Origin $Origin -StateDirectory '/state' -OwnerDid $OwnerDid |
        ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8
    return [pscustomobject]@{ status = 'created'; path = $settingsPath }
}
