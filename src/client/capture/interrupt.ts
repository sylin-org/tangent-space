// What an interrupted run undoes before it exits: the browsers, the started Tangent Spaces with their temp
// folders, and the unfinished round. Each step is registered while it is needed and undone newest first.

const pending = new Set<() => Promise<unknown>>()
let stopping: Promise<never> | undefined

/** Registers a step to undo on SIGINT or SIGTERM; the returned function forgets it once it is undone normally. */
export function onInterrupt(undo: () => Promise<unknown>): () => void {
  pending.add(undo)
  return () => pending.delete(undo)
}

/** While an interrupt is being handled, a promise that ends only by exiting the process; otherwise undefined. */
export function interrupted(): Promise<never> | undefined {
  return stopping
}

for (const signal of ['SIGINT', 'SIGTERM'] as const)
  process.on(signal, () => {
    if (stopping) return
    console.error(`${signal}: stopping and removing the unfinished round.`)
    stopping = (async () => {
      for (const undo of [...pending].reverse()) await undo().catch(() => undefined)
      process.exit(130)
    })()
  })
