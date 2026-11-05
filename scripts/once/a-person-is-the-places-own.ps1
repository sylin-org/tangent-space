<#
.SYNOPSIS
Moves a Tangent Space's data folder to a participant being the place's own person: run once, right after the package
merges and before the merged build opens the folder, with the Tangent Space stopped.

.DESCRIPTION
Every stored participant whose id is the DID of their Bluesky account gets a fresh GUID v7 as their id, keeps that DID
as the account they are bound to, and every row that names them by id names the new one. In one transaction of the
folder's tangent.db, through the sqlite3 command line, for each of them:
- Koan's rows: their person, their logins, the roles they hold and their own permission overwrites, each composed id
  composed again; the audit events about them; the jobs that run on their participant and their Connection. Their
  sessions are deleted, so everyone signs in again.
- Tangent's rows: their participant, bound to the DID; the place's proprietor and operator; the log's actors and targets;
  every Post and every Post's earlier version — author, editor, remover, latest replier, recent authors, the participants
  it mentions and each mention's target in its body, now /p/ and their id — with the DID each was written as; their
  marks of what they have seen, their Connection, its receipts, what came back for their articles, the publication they
  put, now under the DID it is under, the images they brought, and their asks to join a Tangent.
Copies already published stay where their at:// addresses say, and Koan's atproto grants stay keyed by DID. A row of
another kind that names one of them stops the script before anything changes, saying which. It takes a backup first,
beside the database, refuses a folder a running Tangent Space holds, moves nothing a second time, and prints what it
moved.

.EXAMPLE
pwsh scripts/once/a-person-is-the-places-own.ps1 -DataFolder .local/run-real/tangent-data
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

function Read-Rows([string] $sql) {
    $out = $sql | & $sqlite.Source -json $database
    if ($LASTEXITCODE -ne 0) { throw "sqlite3 could not read: $sql" }
    if (-not $out) { return @() }
    return @(($out -join "`n") | ConvertFrom-Json)
}

function Quote([string] $text) { "'" + $text.Replace("'", "''") + "'" }

# Koan's composed id (RoleContract.ComposeId): the SHA-256 of the kind and each part prefixed with its length.
function Compose([string] $kind, [string[]] $parts) {
    $text = (@($kind) + @($parts | ForEach-Object { "$($_.Length):$_" })) -join [char]0x1f
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

$people = Read-Rows 'SELECT Id FROM "Tangent.Participants.Participant" WHERE Id LIKE ''did:%'';'
if ($people.Count -eq 0) {
    Write-Output "Nothing to move: every participant in $database already has an id of the place's own."
    return
}

# What this script moves, by table; what it leaves keyed by DID on purpose, since it names the account and not the
# person. Any other row that names a participant's DID stops the script.
$moved = @(
    'Koan.Identity.Identity', 'Koan.Identity.ExternalIdentityLink', 'Koan.Identity.Session', 'Koan.Identity.Roles.RoleMember',
    'Koan.Identity.Roles.PermissionOverwrite', 'Koan.Identity.AuditEvent', 'Koan.Jobs.JobRecord',
    'Tangent.Participants.Participant', 'Tangent.Space.TangentSpace', 'Tangent.Events.Event', 'Tangent.Posts.Post',
    'Tangent.Posts.Post#audit', 'Tangent.Events.Checkpoint', 'Tangent.Connections.Connection', 'Tangent.Connections.Receipt',
    'Tangent.Connections.Elsewhere', 'Tangent.Connections.Publication', 'Tangent.Posts.MediaUse', 'Tangent.Tangents.Ask')
$accounts = @('Koan.Web.Auth.Connector.Atproto.Storage.AtprotoSession', 'Koan.Web.Auth.Connector.Atproto.Storage.AtprotoAuthorizationState')
$tables = (Read-Rows "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE '%koan_lexical%' AND name NOT LIKE 'sqlite_%';").name
foreach ($table in $tables | Where-Object { $_ -notin $moved -and $_ -notin $accounts }) {
    foreach ($person in $people) {
        $found = Read-Rows "SELECT COUNT(*) AS n FROM `"$table`" WHERE Id LIKE $(Quote ('%' + $person.Id + '%')) OR Json LIKE $(Quote ('%' + $person.Id + '%'));"
        if ($found[0].n -gt 0) { throw "$table names $($person.Id) in $($found[0].n) rows, which this script does not move; nothing changed." }
    }
}

$statements = [Collections.Generic.List[string]]::new()
function Step([string] $label, [string] $sql) { $statements.Add($sql.TrimEnd().TrimEnd(';') + ';'); $statements.Add("SELECT $(Quote ($label + ': ')) || changes();") }

foreach ($person in $people) {
    $old = $person.Id
    $new = [guid]::CreateVersion7().ToString()
    $o = Quote $old
    $n = Quote $new
    $statements.Add("SELECT $(Quote "$old becomes $new");")

    # Koan's rows.
    Step 'person' "UPDATE `"Koan.Identity.Identity`" SET Id = $n WHERE Id = $o"
    Step 'logins' "UPDATE `"Koan.Identity.ExternalIdentityLink`" SET Json = json_set(Json, '$.IdentityId', $n) WHERE json_extract(Json, '$.IdentityId') = $o"
    Step 'sessions ended' "DELETE FROM `"Koan.Identity.Session`" WHERE json_extract(Json, '$.IdentityId') = $o"
    foreach ($member in Read-Rows "SELECT Id, json_extract(Json, '$.RoleKey') AS RoleKey FROM `"Koan.Identity.Roles.RoleMember`" WHERE json_extract(Json, '$.PersonId') = $o;") {
        Step "role $($member.RoleKey)" "UPDATE `"Koan.Identity.Roles.RoleMember`" SET Id = $(Quote (Compose 'RoleMember' @($member.RoleKey, $new))), Json = json_set(Json, '$.PersonId', $n) WHERE Id = $(Quote $member.Id)"
    }
    Step 'roles they added' "UPDATE `"Koan.Identity.Roles.RoleMember`" SET Json = json_set(Json, '$.AddedBy', $n) WHERE json_extract(Json, '$.AddedBy') = $o"
    foreach ($overwrite in Read-Rows "SELECT Id, json_extract(Json, '$.ResourceId') AS ResourceId FROM `"Koan.Identity.Roles.PermissionOverwrite`" WHERE json_extract(Json, '$.SubjectKind') = 'Person' AND json_extract(Json, '$.SubjectId') = $o;") {
        Step "overwrite on $($overwrite.ResourceId)" "UPDATE `"Koan.Identity.Roles.PermissionOverwrite`" SET Id = $(Quote (Compose 'PermissionOverwrite' @($overwrite.ResourceId, 'Person', $new))), Json = json_set(Json, '$.SubjectId', $n) WHERE Id = $(Quote $overwrite.Id)"
    }
    Step 'overwrites they changed' "UPDATE `"Koan.Identity.Roles.PermissionOverwrite`" SET Json = json_set(Json, '$.UpdatedBy', $n) WHERE json_extract(Json, '$.UpdatedBy') = $o"
    Step 'audit events' @"
UPDATE "Koan.Identity.AuditEvent" SET Json = json_set(Json,
    '$.Subject', CASE json_extract(Json, '$.Subject') WHEN $o THEN $n ELSE json_extract(Json, '$.Subject') END,
    '$.Actor', CASE json_extract(Json, '$.Actor') WHEN $o THEN $n ELSE json_extract(Json, '$.Actor') END,
    '$.Target', replace(json_extract(Json, '$.Target'), $(Quote ('Identity/' + $old)), $(Quote ('Identity/' + $new))))
WHERE json_extract(Json, '$.Subject') = $o OR json_extract(Json, '$.Actor') = $o OR json_extract(Json, '$.Target') = $(Quote ('Identity/' + $old))
"@
    Step 'jobs' "UPDATE `"Koan.Jobs.JobRecord`" SET Json = json_set(Json, '$.WorkId', $n, '$.CoalesceKey', replace(json_extract(Json, '$.CoalesceKey'), $o, $n)) WHERE json_extract(Json, '$.WorkId') = $o AND json_extract(Json, '$.WorkType') IN ('Tangent.Participants.Participant', 'Tangent.Connections.Connection')"

    # Tangent's rows.
    Step 'participant' "UPDATE `"Tangent.Participants.Participant`" SET Id = $n, Json = json_set(Json, '$.Did', $o) WHERE Id = $o"
    Step 'proprietor' "UPDATE `"Tangent.Space.TangentSpace`" SET Json = json_set(Json, '$.ProprietorId', $n) WHERE json_extract(Json, '$.ProprietorId') = $o"
    Step 'operator' "UPDATE `"Tangent.Space.TangentSpace`" SET Json = json_set(Json, '$.OperatorId', $n) WHERE json_extract(Json, '$.OperatorId') = $o"
    Step 'events by them' "UPDATE `"Tangent.Events.Event`" SET Json = json_set(Json, '$.ActorId', $n) WHERE json_extract(Json, '$.ActorId') = $o"
    Step 'events for them' "UPDATE `"Tangent.Events.Event`" SET Json = json_set(Json, '$.TargetId', $n) WHERE json_extract(Json, '$.TargetId') = $o"
    foreach ($posts in 'Tangent.Posts.Post', 'Tangent.Posts.Post#audit') {
        $which = if ($posts -eq 'Tangent.Posts.Post') { 'posts' } else { 'earlier versions' }
        Step "$which they wrote, keeping the account they wrote them as" "UPDATE `"$posts`" SET Json = json_set(Json, '$.AuthorId', $n, '$.AuthorDid', $o) WHERE json_extract(Json, '$.AuthorId') = $o"
        foreach ($field in 'EditedBy', 'RemovedBy', 'LatestReplyAuthorId') {
            Step "$which, $field" "UPDATE `"$posts`" SET Json = json_set(Json, '$.$field', $n) WHERE json_extract(Json, '$.$field') = $o"
        }
        foreach ($list in 'RecentAuthorIds', 'MentionedIds') {
            Step "$which, $list" @"
UPDATE "$posts" SET Json = json_set(Json, '$.$list',
    (SELECT json_group_array(CASE each.value WHEN $o THEN $n ELSE each.value END) FROM json_each("$posts".Json, '$.$list') AS each))
WHERE EXISTS (SELECT 1 FROM json_each("$posts".Json, '$.$list') AS each WHERE each.value = $o)
"@
        }
        Step "$which, mentions in the body" "UPDATE `"$posts`" SET Json = json_set(Json, '$.Body', replace(json_extract(Json, '$.Body'), $(Quote ('](at://' + $old + ')')), $(Quote ('](/p/' + $new + ')')))) WHERE instr(json_extract(Json, '$.Body'), $(Quote ('](at://' + $old + ')'))) > 0"
    }
    Step 'seen marks' "UPDATE `"Tangent.Events.Checkpoint`" SET Id = $n || substr(Id, $($old.Length + 1)), Json = json_set(Json, '$.ParticipantId', $n) WHERE json_extract(Json, '$.ParticipantId') = $o"
    Step 'connection' "UPDATE `"Tangent.Connections.Connection`" SET Id = $n WHERE Id = $o"
    Step 'receipts' "UPDATE `"Tangent.Connections.Receipt`" SET Id = $n || substr(Id, $($old.Length + 1)), Json = json_set(Json, '$.ConnectionId', $n) WHERE json_extract(Json, '$.ConnectionId') = $o"
    Step 'what came back for their articles' "UPDATE `"Tangent.Connections.Elsewhere`" SET Json = json_set(Json, '$.AuthorId', $n) WHERE json_extract(Json, '$.AuthorId') = $o"
    Step 'publication' "UPDATE `"Tangent.Connections.Publication`" SET Json = json_set(Json, '$.Proprietor', $n, '$.Did', $o) WHERE json_extract(Json, '$.Proprietor') = $o"
    Step 'images they brought' "UPDATE `"Tangent.Posts.MediaUse`" SET Id = substr(Id, 1, length(Id) - $($old.Length)) || $n WHERE Id LIKE $(Quote ('%/' + $old))"
    Step 'asks to join' "UPDATE `"Tangent.Tangents.Ask`" SET Id = substr(Id, 1, length(Id) - $($old.Length)) || $n, Json = json_set(Json, '$.PersonId', $n) WHERE json_extract(Json, '$.PersonId') = $o"
}

$backup = Join-Path $DataFolder ('tangent.db.before-a-person-is-the-places-own-' + (Get-Date -Format 'yyyyMMddHHmmss'))
"VACUUM INTO $(Quote $backup);" | & $sqlite.Source $database
if ($LASTEXITCODE -ne 0) { throw "The backup to $backup failed; nothing changed." }
Write-Output "Backed up to $backup."

$sql = (@('.bail on', 'BEGIN IMMEDIATE;') + $statements + @('COMMIT;')) -join "`n"
$sql | & $sqlite.Source $database
if ($LASTEXITCODE -ne 0) { throw "sqlite3 stopped with exit code $LASTEXITCODE; the transaction did not commit." }
Write-Output "Moved ${database}: $($people.Count) participants have ids of the place's own."
