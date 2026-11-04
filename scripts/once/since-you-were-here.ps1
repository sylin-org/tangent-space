<#
.SYNOPSIS
Moves a Tangent Space's data folder to what "since you were here" reads: run once, right after the package merges and
before the merged build opens the folder, with the Tangent Space stopped.

.DESCRIPTION
In one transaction of the folder's tangent.db, through the sqlite3 command line:
- each participant's ArrivedSeq becomes the sequence of their own arrival in the log, the first standing.changed they
  did to themselves, or 0 where the log holds none;
- each Post's LogSeq becomes the sequence of the post.created that brought it, or 0 where the log holds none, and each
  root's LastLogSeq the highest LogSeq of its Topic;
- the Locked and Pinned fields leave every Post, and the Watch field every checkpoint;
- the notifications and Tangent checkpoints are deleted, so only the place and Topic marks remain.
Koan rebuilds the Topic list's index, which no longer holds Pinned, at the next start. The script prints what it
changed and refuses a folder a running Tangent Space holds.

.EXAMPLE
pwsh scripts/once/since-you-were-here.ps1 -DataFolder .local/run-real/tangent-data
#>
param(
    [Parameter(Mandatory)]
    [string] $DataFolder
)

$ErrorActionPreference = 'Stop'

$database = Join-Path $DataFolder 'tangent.db'
if (-not (Test-Path -LiteralPath $database)) { throw "No tangent.db is in $DataFolder." }

$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqlite) { throw 'This script needs the sqlite3 command line on the PATH.' }

# A running Tangent Space holds its mark locked; a mark that opens alone was left by one that stopped.
$mark = Join-Path $DataFolder 'tangent.running'
if (Test-Path -LiteralPath $mark) {
    try { [IO.File]::Open($mark, 'Open', 'ReadWrite', 'None').Dispose() }
    catch [IO.IOException] { throw "A running Tangent Space holds $DataFolder; stop it first." }
}

$sql = @'
.bail on
BEGIN IMMEDIATE;
UPDATE "Tangent.Participants.Participant"
SET Json = json_set(Json, '$.ArrivedSeq', COALESCE((
    SELECT MIN(json_extract(e.Json, '$.Seq')) FROM "Tangent.Events.Event" e
    WHERE json_extract(e.Json, '$.Kind') = 'standing.changed'
      AND json_extract(e.Json, '$.ActorId') = "Tangent.Participants.Participant".Id
      AND json_extract(e.Json, '$.TargetId') = "Tangent.Participants.Participant".Id), 0));
SELECT 'participants given their arrival: ' || changes();
UPDATE "Tangent.Posts.Post"
SET Json = json_remove(json_set(Json, '$.LogSeq', COALESCE((
    SELECT MIN(json_extract(e.Json, '$.Seq')) FROM "Tangent.Events.Event" e
    WHERE json_extract(e.Json, '$.Kind') = 'post.created'
      AND json_extract(e.Json, '$.PostId') = "Tangent.Posts.Post".Id), 0)), '$.Locked', '$.Pinned');
SELECT 'posts given their log sequence: ' || changes();
UPDATE "Tangent.Posts.Post"
SET Json = json_set(Json, '$.LastLogSeq', COALESCE((
    SELECT MAX(json_extract(p.Json, '$.LogSeq')) FROM "Tangent.Posts.Post" p
    WHERE json_extract(p.Json, '$.TopicId') = "Tangent.Posts.Post".Id), 0))
WHERE json_extract(Json, '$.ParentId') IS NULL;
SELECT 'topics given their newest post: ' || changes();
DELETE FROM "Tangent.Events.Checkpoint" WHERE json_extract(Json, '$.Scope.Kind') IN ('Notifications', 'Tangent');
SELECT 'notifications and Tangent checkpoints deleted: ' || changes();
UPDATE "Tangent.Events.Checkpoint" SET Json = json_remove(Json, '$.Watch');
SELECT 'checkpoints kept, each without its watch field: ' || changes();
COMMIT;
'@

$sql | & $sqlite.Source $database
if ($LASTEXITCODE -ne 0) { throw "sqlite3 stopped with exit code $LASTEXITCODE; the transaction did not commit." }
Write-Output "Moved $database."
