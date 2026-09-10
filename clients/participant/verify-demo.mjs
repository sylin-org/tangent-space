// Verify an already completed real model turn. This does not invent a reply or
// call a model for an empty room; it checks accepted source records and runs the
// existing participant process once more from its durable state.
import { spawn } from 'node:child_process'
import { readFile, writeFile, readdir } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { ParticipantClient } from './client.mjs'

const options = Object.fromEntries(Array.from({ length: (process.argv.length - 2) / 2 }, (_, index) =>
  [process.argv[2 + index * 2].slice(2), process.argv[3 + index * 2]]))
for (const key of ['output', 'model-directory', 'owner-cookie', 'command-file'])
  if (!options[key]) throw new Error(`Missing --${key}`)
const output = resolve(options.output)
const modelDirectory = resolve(options['model-directory'])
const context = JSON.parse(await readFile(join(output, 'context.json'), 'utf8'))
const model = JSON.parse(await readFile(join(modelDirectory, 'last-model-execution.json'), 'utf8'))
if (model.exitCode !== 0 || model.textEvents !== 1 || model.toolEvents !== 0) throw new Error('Expected one successful conversational model turn with no tools')
const cookie = JSON.parse(await readFile(options['owner-cookie'], 'utf8'))
if (cookie.origin !== context.site || cookie.did !== context.ownerDid) throw new Error('Owner fixture context differs')
const response = await fetch(`${context.site}/api/rooms/${encodeURIComponent(context.room)}/messages`, {
  headers: { cookie: cookie.cookies.map(([name, value]) => `${name}=${value}`).join('; ') },
  redirect: 'error', signal: AbortSignal.timeout(30000)
})
if (!response.ok) throw new Error(`History verification returned HTTP ${response.status}`)
const history = await response.json()
const human = history.messages.find(message => message.sourceUri === context.human.sourceUri)
const replies = history.messages.filter(message => message.authorDid === context.agentDid && message.content?.text === model.replyText)
if (!human || replies.length !== 1) throw new Error('Expected one accepted human fixture and one actual model reply')
const reply = replies[0]
if (reply.content.replyTo?.uri !== human.sourceUri || reply.content.replyTo?.cid !== human.sourceCid)
  throw new Error('Actual model reply did not retain the human source reference')
const before = await readFile(join(modelDirectory, 'last-model-execution.json'), 'utf8')
const beforeDirectories = (await readdir(modelDirectory, { withFileTypes: true })).filter(entry => entry.isDirectory()).length
const args = ['clients/participant/client.mjs', 'watch', '--site', context.site,
  '--credential-file', join(output, 'agent-credential.json'), '--room', context.room,
  '--state', join(output, 'agent-state.json'), '--command-file', resolve(options['command-file']), '--once']
const idle = await new Promise((resolveResult, reject) => {
  const child = spawn(process.execPath, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  let stdout = ''; let stderr = ''
  child.stdout.on('data', chunk => { stdout += chunk })
  child.stderr.on('data', chunk => { stderr += chunk })
  child.on('error', reject)
  child.on('close', code => {
    if (code !== 0) return reject(new Error(`Restarted runner failed with exit ${code}`))
    try { resolveResult(JSON.parse(stdout)) } catch { reject(new Error('Runner result was not one JSON object')) }
  })
})
const afterDirectories = (await readdir(modelDirectory, { withFileTypes: true })).filter(entry => entry.isDirectory()).length
const state = JSON.parse(await readFile(join(output, 'agent-state.json'), 'utf8'))
const noModelCall = before === await readFile(join(modelDirectory, 'last-model-execution.json'), 'utf8') && beforeDirectories === afterDirectories
if (!noModelCall || idle.kind !== 'idle' || state.did !== context.agentDid || state.pending || state.ackPending)
  throw new Error('Restart did not preserve identity, durable continuation, idle model suppression, and acknowledgement')
const credential = JSON.parse(await readFile(join(output, 'agent-credential.json'), 'utf8'))
const client = new ParticipantClient(context.site, credential.token)
const arrival = await client.welcome()
const serverResume = await client.read(context.room)
if (arrival.participant?.did !== context.agentDid || serverResume.messages.length !== 0)
  throw new Error('Existing bearer identity or server-stored read position did not survive')
const evidence = {
  completedAt: new Date().toISOString(), status: 'passed', site: context.site, room: context.room,
  modelExecution: model,
  humanInput: { kind: 'scripted human fixture, submitted through the real owner browser session',
    authorDid: human.authorDid, text: human.content.text, sourceUri: human.sourceUri, sourceCid: human.sourceCid },
  agentReply: { kind: 'actual model output, posted by the unattended participant runner with a Tangent bearer credential',
    authorDid: reply.authorDid, text: reply.content.text, sourceUri: reply.sourceUri, sourceCid: reply.sourceCid,
    replyTo: reply.content.replyTo, acceptedAt: reply.acceptedAt },
  restartAndIdle: { processExitCode: 0, result: idle, sameDid: state.did === context.agentDid,
    pendingOperation: false, pendingAcknowledgement: false, savedCursorPresent: !!state.cursor,
    modelExecutionUnchanged: noModelCall, modelInvocationDirectoriesBefore: beforeDirectories, modelInvocationDirectoriesAfter: afterDirectories,
    existingBearerAccepted: true, serverReadWithoutCursor: { count: serverResume.messages.length, freshness: serverResume.freshness },
    ...(options['previous-host-pid'] && options['current-host-pid'] ? { applicationRestart: {
      previousPid: Number(options['previous-host-pid']), currentPid: Number(options['current-host-pid']),
      reason: 'Coordinated monolith restart retained the SQLite store and protected state.' } } : {}) },
  limits: ['One bounded live model response; this is not an autonomous multi-turn endurance test.',
    'The human question was a scripted fixture. The reply was generated by the configured real model process.',
    'The suggested revocation test in the model text was not executed by the model.',
    'Backup restoration is covered by separate application recovery evidence.']
}
await writeFile(join(output, 'agent-demo.json'), JSON.stringify(evidence, null, 2) + '\n')
console.log(JSON.stringify({ status: evidence.status, authorDid: reply.authorDid, sourceUri: reply.sourceUri,
  sourceCid: reply.sourceCid, restartAndIdle: evidence.restartAndIdle }))
