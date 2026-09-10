// Disposable restore proof. Reuses existing credentials and state; it neither
// enrolls nor starts an OAuth flow, and labels its one new message as scripted.
import { spawn } from 'node:child_process'
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { ParticipantClient } from './client.mjs'

const options = Object.fromEntries(Array.from({ length: (process.argv.length - 2) / 2 }, (_, index) =>
  [process.argv[2 + index * 2].slice(2), process.argv[3 + index * 2]]))
for (const key of ['input', 'output', 'restore-info', 'model-directory', 'owner-cookie', 'command-file', 'baseline'])
  if (!options[key]) throw new Error(`Missing --${key}`)
const input = resolve(options.input); const output = resolve(options.output)
await mkdir(output, { recursive: true })
const context = JSON.parse(await readFile(join(input, 'context.json'), 'utf8'))
const restored = JSON.parse(await readFile(options['restore-info'], 'utf8'))
if (restored.origin !== context.site) throw new Error('Restore and participant site origins differ')
const credential = JSON.parse(await readFile(join(input, 'agent-credential.json'), 'utf8'))
const client = new ParticipantClient(context.site, credential.token)
const baselineBytes = await readFile(options.baseline, 'utf8')
const baseline = JSON.parse(baselineBytes)
const modelDirectory = resolve(options['model-directory'])
const modelBefore = await readFile(join(modelDirectory, 'last-model-execution.json'), 'utf8')
const invocationCount = async () => (await readdir(modelDirectory, { withFileTypes: true })).filter(entry => entry.isDirectory()).length
const invocationsBefore = await invocationCount()
const checks = []
function check(name, condition, details = {}) {
  checks.push({ name, passed: !!condition, ...details })
  if (!condition) throw new Error(`Restore proof failed: ${name}`)
}
async function runner(command, extra = []) {
  const args = ['clients/participant/client.mjs', command, '--site', context.site,
    '--credential-file', join(input, 'agent-credential.json'), '--room', context.room,
    '--state', join(input, 'agent-state.json'), ...extra]
  return new Promise((resolveResult, reject) => {
    const child = spawn(process.execPath, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] })
    let stdout = ''
    child.stdout.on('data', chunk => { stdout += chunk })
    child.on('error', reject)
    child.on('close', code => {
      if (code !== 0) return reject(new Error(`Participant process failed with exit ${code}`))
      try { resolveResult(JSON.parse(stdout)) } catch { reject(new Error('Runner result was not one JSON object')) }
    })
  })
}
const watchOptions = ['--command-file', resolve(options['command-file']), '--once']
const cookie = JSON.parse(await readFile(options['owner-cookie'], 'utf8'))
if (cookie.origin !== context.site || cookie.did !== context.ownerDid) throw new Error('Owner fixture context differs')
let continuation
try {
  const welcome = await client.welcome()
  check('existing-bearer-restored-with-same-DID', welcome.participant?.did === context.agentDid, { did: welcome.participant?.did })
  const restoredRead = await client.read(context.room)
  check('restored-server-read-position-without-client-cursor', restoredRead.messages.length === 0, { count: restoredRead.messages.length })
  const historyResponse = await fetch(`${context.site}/api/rooms/${encodeURIComponent(context.room)}/messages`, {
    headers: { cookie: cookie.cookies.map(([name, value]) => `${name}=${value}`).join('; ') },
    redirect: 'error', signal: AbortSignal.timeout(30000)
  })
  check('restored-browser-session-reads-history', historyResponse.status === 200, { status: historyResponse.status })
  const history = await historyResponse.json()
  const original = history.messages.filter(message => message.sourceUri === baseline.agentReply.sourceUri)
  check('original-model-source-retained', original.length === 1 && original[0].sourceCid === baseline.agentReply.sourceCid
    && original[0].authorDid === context.agentDid && original[0].content.text === baseline.agentReply.text,
  { sourceUri: baseline.agentReply.sourceUri, sourceCid: baseline.agentReply.sourceCid })
  const idle = await runner('watch', watchOptions)
  check('fresh-runner-resumes-idle-after-restore', idle.kind === 'idle' && idle.count === 0, { result: idle })
  const text = 'Scripted restore proof: this continuation exercises a fresh Spaces write using the restored agent connection and existing Tangent credential.'
  const textPath = join(output, 'scripted-continuation.txt')
  await writeFile(textPath, text + '\n')
  continuation = await runner('post', ['--text-file', textPath])
  check('new-scripted-source-write-accepted-after-restore', continuation.kind === 'posted'
    && typeof continuation.sourceCid === 'string' && continuation.sourceUri !== baseline.agentReply.sourceUri,
  { operationId: continuation.operationId, sourceUri: continuation.sourceUri, sourceCid: continuation.sourceCid })
  const updated = await client.read(context.room)
  const accepted = updated.messages.filter(message => message.sourceUri === continuation.sourceUri)
  check('new-source-projects-with-agent-authorship', accepted.length === 1 && accepted[0].sourceCid === continuation.sourceCid
    && accepted[0].authorDid === context.agentDid && accepted[0].content.text.trim() === text,
  { authorDid: accepted[0]?.authorDid, sourceCid: accepted[0]?.sourceCid })
  const own = await runner('watch', watchOptions)
  check('own-scripted-continuation-needs-no-model', own.kind === 'read' && own.count === 1, { result: own })
  const finalIdle = await runner('watch', watchOptions)
  check('runner-remains-idle-after-acknowledgement', finalIdle.kind === 'idle' && finalIdle.count === 0, { result: finalIdle })
  const serverResume = await client.read(context.room)
  const state = JSON.parse(await readFile(join(input, 'agent-state.json'), 'utf8'))
  check('restored-server-and-client-acknowledge-new-source', serverResume.messages.length === 0 && !state.pending && !state.ackPending
    && state.did === context.agentDid, { count: serverResume.messages.length, sameDid: state.did === context.agentDid, pending: false, ackPending: false })
  check('no-additional-model-invocation', await invocationCount() === invocationsBefore
    && modelBefore === await readFile(join(modelDirectory, 'last-model-execution.json'), 'utf8'),
  { invocationDirectoriesBefore: invocationsBefore, invocationDirectoriesAfter: await invocationCount() })
  check('original-model-evidence-unchanged', baselineBytes === await readFile(options.baseline, 'utf8'))
} catch (error) {
  const evidence = { status: 'failed', completedAt: new Date().toISOString(), site: context.site, room: context.room,
    instance: restored.instance, pid: restored.pid, error: error.message, checks, continuation }
  await writeFile(join(output, 'participation-restore.json'), JSON.stringify(evidence, null, 2) + '\n')
  console.log(JSON.stringify(evidence)); process.exitCode = 1
}
if (!process.exitCode) {
  const evidence = { status: 'passed', completedAt: new Date().toISOString(), site: context.site, room: context.room,
    restore: { instance: restored.instance, pid: restored.pid, sameOrigin: true, newStateDirectory: true },
    baseline: 'agent-demo.json', existingCredentialReused: true, newEnrollment: false, newOAuthAuthorization: false,
    continuation: { ...continuation, origin: 'scripted restoration fixture; not a new model reply' }, checks,
    interpretation: 'A fresh operation received an accepted verified Spaces source reference after backup restoration, exercising the restored upstream OAuth connection rather than only a cached browser identity.' }
  await writeFile(join(output, 'participation-restore.json'), JSON.stringify(evidence, null, 2) + '\n')
  console.log(JSON.stringify({ status: evidence.status, checks: checks.length, instance: restored.instance, pid: restored.pid, continuation }))
}
