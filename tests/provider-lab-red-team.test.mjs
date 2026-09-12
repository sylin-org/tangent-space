import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// No Docker command is executed: the child PowerShell process receives a function
// named docker that returns synthetic inspection data and rejects unknown commands.
const guard = fileURLToPath(new URL('../probes/ProviderLab/check.ps1', import.meta.url));
const mockScript = String.raw`
$ErrorActionPreference = 'Stop'
$project = 'tangent-epic005-provider-lab'
$networkName = "$($project)_host-access"
$scenario = $env:EPIC005_RED_TEAM_CASE
function global:docker {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$DockerArgs)
    $global:LASTEXITCODE = 0
    if ($DockerArgs[0] -eq 'network' -and $DockerArgs[1] -eq 'inspect') {
        $members = @{ a = @{ Name = "$project-mongo-1" }; b = @{ Name = "$project-postgres-1" } }
        if ($scenario -eq 'extra-network-member') { $members.c = @{ Name = 'unrelated-app' } }
        @{ Driver = 'bridge'; Labels = @{ 'com.docker.compose.project' = $project }; Containers = $members } | ConvertTo-Json -Depth 12
        return
    }
    if ($DockerArgs[0] -eq 'inspect') {
        $mongo = $DockerArgs[1] -eq "$project-mongo-1"
        $service = if ($mongo) { 'mongo' } else { 'postgres' }
        $port = if ($mongo) { '27017/tcp' } else { '5432/tcp' }
        $hostPort = if ($mongo) { '27119' } else { '25432' }
        $data = if ($mongo) { '/data/db' } else { '/var/lib/postgresql/data' }
        $image = if ($mongo) { 'mongo:8.3.4@sha256:309d760ba3f7962e14d54ac6123c7fe72ed2d465196b4d0eaf8abcea7dd39450' } else { 'postgres:17.11-bookworm@sha256:051f7b7b3abdd564d5d1bd1e8c4b9c1b6e77087d1dd22020ede611c096a272e0' }
        $networks = @{ $networkName = @{} }
        if ($scenario -eq 'extra-container-network') { $networks.other = @{} }
        $owner = if ($scenario -eq 'wrong-container-owner') { 'other-project' } else { $project }
        $hostIp = if ($scenario -eq 'public-port') { '0.0.0.0' } else { '127.0.0.1' }
        $memory = if ($scenario -eq 'uncapped-memory') { 0 } else { 2147483648L }
        @{
            Config = @{ Labels = @{ 'com.docker.compose.project' = $owner }; Image = $image }
            State = @{ Status = 'running' }
            HostConfig = @{ NanoCpus = 1000000000L; Memory = $memory; MemorySwap = 2147483648L; PidsLimit = 256 }
            NetworkSettings = @{ Ports = @{ $port = @(@{ HostIp = $hostIp; HostPort = $hostPort }) }; Networks = $networks }
            Mounts = @(@{ Destination = $data; Type = 'volume'; Name = "$($project)_$service-data" })
        } | ConvertTo-Json -Depth 12
        return
    }
    if ($DockerArgs[0] -eq 'volume' -and $DockerArgs[1] -eq 'inspect') {
        $owner = if ($scenario -eq 'wrong-volume-owner') { 'unrelated-project' } else { $project }
        @{ Labels = @{ 'com.docker.compose.project' = $owner } } | ConvertTo-Json -Depth 12
        return
    }
    if ($DockerArgs[0] -eq 'exec' -and $DockerArgs[2] -eq 'du') {
        if ($scenario -eq 'disk-ceiling') { '6291456 /synthetic/data' } else { '100000 /synthetic/data' }
        return
    }
    throw ('Unmocked Docker command is forbidden: ' + ($DockerArgs -join ' '))
}
function global:Get-CimInstance {
    param([string]$ClassName)
    if ($ClassName -ne 'Win32_OperatingSystem') { throw 'Unexpected CIM request' }
    @{ FreePhysicalMemory = $(if ($scenario -eq 'low-memory') { 7L * 1024 * 1024 } else { 32L * 1024 * 1024 }) }
}
try { & $env:EPIC005_RED_TEAM_GUARD; exit 0 }
catch { Write-Output $_.Exception.Message; exit 1 }
`;

function inspect(caseName) {
  const result = spawnSync(process.platform === 'win32' ? 'pwsh.exe' : 'pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', mockScript], {
    encoding: 'utf8', timeout: 15000,
    env: { ...process.env, EPIC005_RED_TEAM_CASE: caseName, EPIC005_RED_TEAM_GUARD: guard },
  });
  if (result.error) throw result.error;
  return { code: result.status, output: `${result.stdout}\n${result.stderr}` };
}

test('provider guard accepts a fully matching synthetic lab', () => {
  const result = inspect('baseline');
  assert.equal(result.code, 0, result.output);
  assert.match(result.output, /Combined database state/);
});

for (const [scenario, expected] of [
  ['extra-network-member', /Unexpected container attached/],
  ['extra-container-network', /Unexpected network attachment/],
  ['wrong-container-owner', /Unexpected identity\/image\/state/],
  ['public-port', /Unexpected actual port bindings/],
  ['uncapped-memory', /Unexpected resource limits/],
  ['wrong-volume-owner', /Unexpected volume ownership/],
  ['disk-ceiling', /exceeds 10 GiB/],
  ['low-memory', /Less than 8 GiB host RAM/],
]) {
  test(`provider guard rejects ${scenario}`, () => {
    const result = inspect(scenario);
    assert.notEqual(result.code, 0, result.output);
    assert.match(result.output, expected);
  });
}
