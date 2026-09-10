$ErrorActionPreference = 'Stop'
$container = 'tangent-spaces-network'
$label = docker inspect --format '{{index .Config.Labels "tangent.fixture"}}' $container 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output 'No Tangent Spaces fixture container is running.'; exit 0 }
if ($label -ne 'spaces-network') { throw 'Refusing to stop a container without the expected Tangent fixture label.' }
docker stop --timeout 15 $container
if ($LASTEXITCODE -ne 0) { throw 'Fixture stop failed' }
docker rm $container
if ($LASTEXITCODE -ne 0) { throw 'Fixture container removal failed' }
Write-Output 'Stopped the disposable network. Saved evidence remains in .local/spaces-network; its account identities no longer exist.'
