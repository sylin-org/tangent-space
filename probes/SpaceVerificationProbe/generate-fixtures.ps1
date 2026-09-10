$ErrorActionPreference = 'Stop'
$container = 'tangent-spaces-network'
$label = docker inspect --format '{{index .Config.Labels "tangent.fixture"}}' $container
if ($LASTEXITCODE -ne 0 -or $label -ne 'spaces-network') { throw 'Start the pinned Tangent Spaces test network first.' }
docker exec $container mkdir -p /atproto/tangent-verification
if ($LASTEXITCODE -ne 0) { throw 'Could not create generator directory.' }
docker cp (Join-Path $PSScriptRoot 'generate-fixtures.mjs') "${container}:/atproto/tangent-verification/generate-fixtures.mjs"
if ($LASTEXITCODE -ne 0) { throw 'Could not copy generator.' }
docker exec $container node /atproto/tangent-verification/generate-fixtures.mjs
if ($LASTEXITCODE -ne 0) { throw 'Official fixture oracle failed.' }
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'fixtures') | Out-Null
docker cp "${container}:/atproto/tangent-verification/out/." (Join-Path $PSScriptRoot 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Could not copy generated fixtures.' }
