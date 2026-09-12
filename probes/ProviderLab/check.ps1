param()
$ErrorActionPreference = 'Stop'
$labProject = 'tangent-epic005-provider-lab'
$labNetwork = "$($labProject)_host-access"
$networkJson = docker network inspect $labNetwork
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect lab network' }
$network = @($networkJson | ConvertFrom-Json)[0]
if ($network.Driver -ne 'bridge' -or $network.Labels.'com.docker.compose.project' -ne $labProject) {
    throw 'Unexpected lab network identity'
}
$members = @($network.Containers.PSObject.Properties | ForEach-Object { $_.Value.Name } | Sort-Object)
$expectedMembers = @("$labProject-mongo-1", "$labProject-postgres-1" | Sort-Object)
if (($members -join ',') -ne ($expectedMembers -join ',')) { throw 'Unexpected container attached to lab network' }
$labExpectations = @(
    @{ Service = 'mongo'; Port = '27017/tcp'; HostPort = '27119'; Data = '/data/db'; Volume = 'mongo-data'; Image = 'mongo:8.3.4@sha256:309d760ba3f7962e14d54ac6123c7fe72ed2d465196b4d0eaf8abcea7dd39450' },
    @{ Service = 'postgres'; Port = '5432/tcp'; HostPort = '25432'; Data = '/var/lib/postgresql/data'; Volume = 'postgres-data'; Image = 'postgres:17.11-bookworm@sha256:051f7b7b3abdd564d5d1bd1e8c4b9c1b6e77087d1dd22020ede611c096a272e0' }
)
$labTotalKiB = 0L
$labRows = foreach ($expected in $labExpectations) {
    $containerName = "$labProject-$($expected.Service)-1"
    $inspection = docker inspect $containerName
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $containerName" }
    $container = @($inspection | ConvertFrom-Json)[0]
    if ($container.Config.Labels.'com.docker.compose.project' -ne $labProject -or
        $container.Config.Image -ne $expected.Image -or $container.State.Status -ne 'running') {
        throw "Unexpected identity/image/state for $containerName"
    }
    $networks = @($container.NetworkSettings.Networks.PSObject.Properties.Name)
    if ($networks.Count -ne 1 -or $networks[0] -ne $labNetwork) { throw "Unexpected network attachment for $containerName" }
    if ($container.HostConfig.NanoCpus -ne 1000000000 -or
        $container.HostConfig.Memory -ne 2147483648 -or
        $container.HostConfig.MemorySwap -ne 2147483648 -or
        $container.HostConfig.PidsLimit -ne 256) { throw "Unexpected resource limits for $containerName" }
    $bindings = @($container.NetworkSettings.Ports.($expected.Port))
    if ($bindings.Count -ne 1 -or $bindings[0].HostIp -ne '127.0.0.1' -or
        $bindings[0].HostPort -ne $expected.HostPort) { throw "Unexpected actual port bindings for $containerName" }
    $mount = @($container.Mounts | Where-Object Destination -eq $expected.Data)
    if ($mount.Count -ne 1 -or $mount[0].Type -ne 'volume' -or
        $mount[0].Name -ne "$($labProject)_$($expected.Volume)") { throw "Unexpected data mount for $containerName" }
    if (@($container.Mounts | Where-Object Type -eq 'bind').Count -ne 0) { throw "Unexpected bind mount for $containerName" }
    $volumeJson = docker volume inspect $mount[0].Name
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect lab data volume' }
    $volume = @($volumeJson | ConvertFrom-Json)[0]
    if ($volume.Labels.'com.docker.compose.project' -ne $labProject) { throw 'Unexpected volume ownership' }
    $size = docker exec $containerName du -sk $expected.Data
    if ($LASTEXITCODE -ne 0 -or $size -notmatch '^([0-9]+)\s') { throw "Cannot measure storage for $containerName" }
    $sizeKiB = [long]$Matches[1]
    $labTotalKiB += $sizeKiB
    [pscustomobject]@{ Service = $expected.Service; State = $container.State.Status; DataMiB = [math]::Round($sizeKiB / 1024, 2); CPUs = 1; MemoryGiB = 2; HostPort = $expected.HostPort }
}
if ($labTotalKiB -gt 10L * 1024 * 1024) { throw 'Lab generated database data exceeds 10 GiB; stop the lab and investigate. No automatic deletion is performed.' }
$freeMiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
if ($freeMiB -lt 8192) { throw 'Less than 8 GiB host RAM free; do not start or continue a load phase.' }
$labRows
Write-Output ("Combined database state: {0:N2} MiB; free host RAM: {1:N0} MiB" -f ($labTotalKiB / 1024), $freeMiB)
