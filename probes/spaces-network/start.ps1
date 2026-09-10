param([switch]$Build)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$upstream = Join-Path $repoRoot '.local/upstream/atproto'
$outputDir = Join-Path $repoRoot '.local/spaces-network'
$revision = 'c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae'
$image = 'tangent-spaces-network:c1d97bb'
$container = 'tangent-spaces-network'
New-Item -ItemType Directory -Force (Split-Path $upstream), $outputDir | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $upstream '.git'))) {
  git clone --no-checkout https://github.com/bluesky-social/atproto.git $upstream
  if ($LASTEXITCODE -ne 0) { throw 'Upstream clone failed' }
  git -C $upstream checkout --detach $revision
  if ($LASTEXITCODE -ne 0) { throw 'Pinned checkout failed' }
}
$actualRevision = git -C $upstream rev-parse HEAD
if ($actualRevision -ne $revision) { throw "Expected upstream revision $revision; found $actualRevision. Use a dedicated pinned checkout." }
$dirty = git -C $upstream status --porcelain
if ($dirty) { throw 'Upstream checkout must be clean before building the protocol fixture.' }
docker image inspect $image *> $null
if ($Build -or $LASTEXITCODE -ne 0) {
  docker build -f (Join-Path $PSScriptRoot 'Dockerfile') -t $image $upstream
  if ($LASTEXITCODE -ne 0) { throw 'Protocol fixture image build failed' }
}
$existing = docker ps -a --filter "name=^/$container$" --format '{{.Names}}'
if ($existing) { throw "Fixture container already exists. Run probes/spaces-network/stop.ps1 before starting a fresh network." }
docker run -d --name $container --label tangent.fixture=spaces-network `
  -p 127.0.0.1:2582:2582 -p 127.0.0.1:2583:2583 -p 127.0.0.1:2584:2584 -p 127.0.0.1:2585:2585 `
  --mount "type=bind,source=$PSScriptRoot,target=/atproto/tangent-probe,readonly" `
  --mount "type=bind,source=$outputDir,target=/evidence" $image `
  node --import /atproto/tangent-probe/patch-varint.mjs /atproto/tangent-probe/network.mjs
if ($LASTEXITCODE -ne 0) { throw 'Protocol fixture container failed to start' }
Write-Output "Started disposable test network. Inspect http://localhost:2585/health and $outputDir/baseline-evidence.json."
Write-Output "Credentials are only in $outputDir/fixtures.json; do not commit or log that file."
