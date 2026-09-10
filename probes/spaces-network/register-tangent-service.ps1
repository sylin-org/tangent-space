param([ValidateRange(1024, 65535)][int]$Port = 5220)
$ErrorActionPreference = 'Stop'
$result = docker exec tangent-spaces-network node /atproto/tangent-probe/register-tangent-service.mjs $Port
if ($LASTEXITCODE -ne 0) { throw 'Tangent managing-app fixture registration failed.' }
# The container's /evidence mount persists only the public DID and endpoint at
# .local/spaces-network/tangent-service-<port>.json. No private key is retained.
$result | ConvertFrom-Json
