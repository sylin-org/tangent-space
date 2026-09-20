<script setup lang="ts">
// The edit-history viewer, ported from history.js: the closed disclosure under an
// edited post. Reads the post's recorded pre-edit snapshots and renders each era
// verbatim — the server's text, facets and timestamps; nothing is reconstructed.
import { ref } from 'vue'
import { useSite } from '../stores/site'
import { facetedSegments } from '../composables/facets'
import type { Conversation, Message } from '../composables/conversation'

const MAX_ERAS = 20
const FETCH_SIZE = 200

const props = defineProps<{ conversation: Conversation; message: Message }>()

const { resolved } = props.conversation
const site = useSite()
const open = ref(false)
const loaded = ref(false)
const loading = ref(false)
const loadError = ref('')
const restricted = ref(false)
const eras = ref<HistoryRow[]>([])
const truncated = ref(false)

interface HistoryRow {
  id: string
  previousChangeId?: string
  authorParticipantId?: string
  editedAt?: string
  acceptedAt?: string
  content?: { text?: string }
  facets?: unknown
}

function authorLabel(did?: string): string {
  if (!did) return ''
  if (did === (site.site?.participant?.participantRef ?? '')) return 'You'
  const handle = resolved.value[did]?.handle
  return resolved.value[did]?.displayName || (handle ? '@' + handle.replace(/^@/, '') : 'Participant')
}

function eraSegments(row: HistoryRow) {
  const text = row.content && typeof row.content.text === 'string' ? row.content.text : ''
  return facetedSegments(text, Array.isArray(row.facets) ? row.facets : undefined, resolved.value)
}

function eraStamp(row: HistoryRow): string {
  if (row.editedAt) return 'Edited ' + new Date(row.editedAt).toLocaleString()
  return row.acceptedAt ? 'Posted ' + new Date(row.acceptedAt).toLocaleString() : 'Posted earlier'
}

/// Newest-first order by the snapshot chain; GUIDv7 ids sort lexicographically in
/// time order, so unreferenced rows fall to the tail honestly.
function orderRows(rows: HistoryRow[]): HistoryRow[] {
  const byId = new Map(rows.filter(row => row && typeof row.id === 'string').map(row => [row.id, row]))
  const referenced = new Set(rows.map(row => row?.previousChangeId).filter(Boolean))
  const byNewest = (left: HistoryRow, right: HistoryRow) => String(right.id).localeCompare(String(left.id))
  let head: HistoryRow | undefined = typeof props.message.changeId === 'string' ? byId.get(props.message.changeId) : undefined
  if (!head) head = [...byId.values()].filter(row => !referenced.has(row.id)).sort(byNewest)[0]
  const ordered: HistoryRow[] = []
  const seen = new Set<string>()
  for (let node: HistoryRow | undefined = head; node && !seen.has(node.id); node = node.previousChangeId ? byId.get(node.previousChangeId) : undefined) {
    if (!node) break
    seen.add(node.id)
    ordered.push(node)
  }
  return [...ordered, ...[...byId.values()].filter(row => !seen.has(row.id)).sort(byNewest)]
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const response = await fetch('/api/history/messages?set=changelog&size=' + FETCH_SIZE
      + '&filter=' + encodeURIComponent(JSON.stringify({ ofPostId: props.message.id })), {
      credentials: 'same-origin',
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    })
    if (!response.ok) throw Object.assign(new Error('History is unavailable.'), { status: response.status })
    const rows = await response.json()
    if (!Array.isArray(rows)) throw new Error('History could not be read.')
    const ordered = orderRows(rows as HistoryRow[])
    eras.value = ordered.slice(0, MAX_ERAS)
    truncated.value = ordered.length > MAX_ERAS || rows.length >= FETCH_SIZE
  } catch (error) {
    restricted.value = [401, 403].includes((error as Error & { status?: number }).status ?? 0)
    loadError.value = restricted.value
      ? 'Earlier versions aren’t available to this account.'
      : 'Earlier versions couldn’t be loaded. Your post is still here.'
  } finally {
    loading.value = false
  }
}

function onToggle() {
  // Lazy: the scoped fetch runs on first open only.
  if (!open.value || loaded.value) return
  loaded.value = true
  void load()
}
</script>

<template>
  <details class="source-details history-details" @toggle="onToggle">
    <summary :title="message.editedAt ? 'Last edited ' + new Date(message.editedAt).toLocaleString() : undefined" >Edited · history</summary>
    <div class="history-body" aria-live="polite">
      <p v-if="loading" class="hint">Loading earlier versions…</p>
      <template v-else-if="loadError">
        <p class="hint">{{ loadError }}</p>
        <button v-if="!restricted" class="btn btn-quiet" type="button" @click="load">Try again</button>
      </template>
      <template v-else-if="!eras.length">
        <p class="hint">No earlier versions are available to you.</p>
      </template>
      <template v-else>
        <div v-for="row in eras" :key="row.id" class="history-era">
          <div class="message-byline">
            <strong class="message-author">{{ authorLabel(row.authorParticipantId) }}</strong>
            <time>{{ eraStamp(row) }}</time>
          </div>
          <p class="message-text">
            <template v-for="(segment, index) in eraSegments(row)" :key="index">
              <a v-if="segment.kind === 'mention'" class="mention" :href="segment.href" :title="segment.title">{{ segment.text }}</a>
              <span v-else-if="segment.kind === 'group'" class="mention-group" :title="segment.title">{{ segment.text }}</span>
              <span v-else-if="segment.kind === 'tag'" class="mention-tag">{{ segment.text }}</span>
              <template v-else>{{ segment.text }}</template>
            </template>
          </p>
        </div>
        <p v-if="truncated" class="hint">Showing the most recent available versions.</p>
      </template>
    </div>
  </details>
</template>
