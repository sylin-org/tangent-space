<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { contentApi, type ContentSourcePage, type ContentSourceState } from '../api'

const props = defineProps<{ actorKey: string }>()
const sources = ref<ContentSourceState[]>([])
const author = ref('')
const actorDid = ref('')
const loadedAuthor = ref('')
const page = ref<ContentSourcePage | null>(null)
const busy = ref(false)
const message = ref('')
const failed = ref(false)
const backfillAvailable = ref(false)
const lastRefresh = ref('')
const bluesky = computed(() => sources.value.find(source => source.id === 'bluesky'))
const local = computed(() => sources.value.find(source => source.id === 'local'))
let generation = 0
const draftKey = (actor: string) => `tangent:host-settings:${encodeURIComponent(actor)}:source-account`
watch(() => props.actorKey, actor => {
  generation++
  sources.value = []
  page.value = null
  actorDid.value = ''
  loadedAuthor.value = ''
  lastRefresh.value = ''
  message.value = ''
  busy.value = false
  try { author.value = actor ? sessionStorage.getItem(draftKey(actor)) || '' : '' }
  catch { author.value = '' }
  if (actor) void loadSources()
}, { immediate: true })
watch(author, value => {
  if (!props.actorKey) return
  try {
    if (value.trim()) sessionStorage.setItem(draftKey(props.actorKey), value)
    else sessionStorage.removeItem(draftKey(props.actorKey))
  } catch { /* The account can still be entered for this session. */ }
})

async function loadSources() {
  const token = generation
  busy.value = true
  failed.value = false
  try { const result = await contentApi.sources(); if (token === generation) sources.value = result.sources }
  catch (error) { if (token === generation) { failed.value = true; message.value = (error as Error).message } }
  finally { if (token === generation) busy.value = false }
}

async function refresh() {
  const selected = author.value.trim().replace(/^@/, '')
  if (!selected || busy.value) return
  const token = generation
  busy.value = true
  failed.value = false
  message.value = ''
  try {
    const result = await contentApi.refreshAuthor(selected)
    const resultPage = await contentApi.readAuthor(result.actorDid)
    if (token !== generation) return
    actorDid.value = result.actorDid
    loadedAuthor.value = selected
    page.value = resultPage
    lastRefresh.value = result.refreshedAt
    backfillAvailable.value = result.backfill !== 'complete'
    message.value = result.imported + (result.imported === 1 ? ' Post added.' : ' Posts added.')
  } catch (error) { if (token === generation) { failed.value = true; message.value = (error as Error).message } }
  finally { if (token === generation) busy.value = false }
}

async function readPage(direction: 'before' | 'after', postId: string) {
  if (!actorDid.value || busy.value) return
  const token = generation
  busy.value = true
  failed.value = false
  try { const result = await contentApi.readAuthor(actorDid.value, direction, postId); if (token === generation) page.value = result }
  catch (error) { if (token === generation) { failed.value = true; message.value = (error as Error).message } }
  finally { if (token === generation) busy.value = false }
}

function postLink(url: string): string | undefined {
  try { const link = new URL(url, window.location.origin); return ['http:', 'https:'].includes(link.protocol) ? link.href : undefined }
  catch { return undefined }
}

</script>

<template>
  <section class="sources-panel stack" aria-labelledby="sources-heading" :aria-busy="busy">
    <header><h2 id="sources-heading">Content sources</h2><p class="hint">Read local conversations and selected public Bluesky posts in Tangent.</p></header>
    <component :is="local.read ? 'a' : 'div'" v-if="local" class="source-summary" :class="{ 'source-summary-link': local.read }" :href="local.read ? '/tangents/' : undefined">
      <h3>Local</h3><p>Your Tangents, Topics and Posts.</p>
      <span v-if="local.read" class="source-post-action">Browse Tangents →</span>
    </component>
    <div v-if="bluesky" class="stack">
      <header><h3>Bluesky <span class="hint">Public reading</span></h3><p class="hint">Import public Posts from an account. To publish one of your own public Posts, open its action menu and choose Publish to Bluesky.</p></header>
      <form class="stack" @submit.prevent="refresh">
        <label>Bluesky account<input v-model="author" class="input" name="bluesky-author" required :disabled="busy" placeholder="alice.bsky.social" autocomplete="off" spellcheck="false"></label>
        <div><button class="btn btn-quiet" type="submit" :disabled="busy || !author.trim()">{{ busy ? 'Loading…' : page ? 'Refresh public Posts' : 'Load public Posts' }}</button></div>
      </form>
      <p v-if="lastRefresh" class="hint">Last refreshed {{ new Date(lastRefresh).toLocaleString() }}.</p>
      <p v-if="backfillAvailable" class="hint">More history is available. Each refresh adds another bounded page.</p>
      <template v-if="page">
        <h4>Stored Posts from {{ loadedAuthor }}</h4>
        <p v-if="!page.posts.length" class="hint">No public Posts are stored for this account yet.</p>
        <ol v-else class="source-posts" aria-label="Stored Bluesky Posts">
          <li v-for="post in page.posts" :key="post.id">
            <a class="source-post-link" :href="postLink(post.url)" target="_blank" rel="noopener noreferrer">
              <article>
                <header><strong>@{{ post.author.replace(/^@/, '') }}</strong> <time :datetime="post.createdAt">{{ new Date(post.createdAt).toLocaleString() }}</time></header>
                <p class="post-text">{{ post.text }}</p>
                <span class="source-post-action">Original on Bluesky ↗</span>
              </article>
            </a>
          </li>
        </ol>
        <nav class="source-pages" aria-label="Bluesky history">
          <button v-if="page.olderBefore" type="button" class="btn btn-quiet" :disabled="busy" @click="readPage('before', page.olderBefore)">Older Posts</button>
          <button v-if="page.newerAfter" type="button" class="btn btn-quiet" :disabled="busy" @click="readPage('after', page.newerAfter)">Newer Posts</button>
        </nav>
      </template>
    </div>
    <p v-if="message" class="action-status" :class="{ error: failed }" role="status">{{ message }}</p>
    <button v-if="failed && !sources.length" type="button" class="btn btn-quiet" :disabled="busy" @click="loadSources">Retry</button>
  </section>
</template>

<style scoped>
.source-summary { padding-bottom: 1rem; }
.source-summary-link { display:block; padding:1rem .75rem; color:inherit; text-decoration:none; border-radius:6px; }
.source-summary-link:hover { background:var(--surface-soft); }
.source-summary-link:focus-visible { outline:2px solid var(--accent); outline-offset:-2px; }
.source-posts { list-style: none; margin: 0; padding: 0; }
.source-posts li { border-bottom: 1px solid var(--line); }
.source-post-link { display:block; padding:1rem .75rem; color:inherit; text-decoration:none; border-radius:6px; }
.source-post-link:hover { background:var(--surface-soft); }
.source-post-link:focus-visible { outline:2px solid var(--accent); outline-offset:-2px; }
.source-post-action { color:var(--accent); }
.source-posts header { display: flex; flex-wrap: wrap; gap: .5rem; }
.source-posts time { color: var(--ink-soft); font-size: .85rem; }
.post-text { white-space: pre-wrap; overflow-wrap: anywhere; }
.source-pages { display: flex; flex-wrap: wrap; gap: .75rem; }
</style>
