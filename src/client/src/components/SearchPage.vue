<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { httpRequest } from '../api'
import { useRouter, type WorkspaceRoute } from '../stores/router'
import { useSite } from '../stores/site'
import ParticipantAvatar from './ParticipantAvatar.vue'
import type { DirectoryParticipant } from '../composables/directories'

const props = defineProps<{ route: WorkspaceRoute }>()
const router = useRouter()
const site = useSite()
const query = ref(''), submittedQuery = ref('')
const loading = ref(false), error = ref(''), more = ref(false), page = ref(1)
const scopeLimited = ref(false), searched = ref(false)
const items = ref<{ id: string; title: string; url: string; excerpt: string; author: DirectoryParticipant }[]>([])
const valid = computed(() => query.value.trim().length >= 2)
let generation = 0
let debounce: ReturnType<typeof setTimeout> | undefined
let requestController: AbortController | undefined
let internalHref: string | undefined
let editingHistory = false
let disposed = false
const actor = () => site.state + ':' + (site.site?.participant?.participantRef ?? '')
const ready = () => site.state === 'anonymous' || site.state === 'signedIn'

function clearResults() {
  generation++
  clearTimeout(debounce)
  debounce = undefined
  requestController?.abort()
  requestController = undefined
  items.value = []
  error.value = ''
  more.value = false
  page.value = 1
  scopeLimited.value = false
  searched.value = false
  loading.value = false
}

async function search(next = 1, value = query.value.trim()) {
  clearResults()
  if (disposed || !ready() || value.length < 2) return
  const token = generation, identity = actor()
  const controller = new AbortController()
  requestController = controller
  const current = () => !disposed && token === generation && identity === actor()
  loading.value = true
  submittedQuery.value = value
  page.value = next
  try {
    const response = await httpRequest('/api/search?q=' + encodeURIComponent(value) + '&page=' + next, { cache: 'no-store', signal: controller.signal })
    const data = await response.json().catch(() => ({}))
    if (!current()) return
    if (!response.ok) throw new Error(data.message || 'Search could not be completed. Try again.')
    items.value = data.items
    more.value = data.more
    scopeLimited.value = data.scopeLimited
    searched.value = true
  } catch (cause) {
    if (current() && !controller.signal.aborted) error.value = (cause as Error).message
  } finally {
    if (current()) { loading.value = false; requestController = undefined }
  }
}

function visit(value: string, next = 1, mode: 'live' | 'submit' | 'page' = 'live') {
  const href = '/search' + (value ? '?q=' + encodeURIComponent(value) + (next > 1 ? '&page=' + next : '') : '')
  if (href !== location.pathname + location.search) {
    internalHref = href
    router.navigate(href, { replace: mode !== 'page' && editingHistory })
    editingHistory = mode === 'live'
  } else if (mode !== 'live') editingHistory = false
  void search(next, value)
}
function edit(event: Event) {
  query.value = (event.target as HTMLInputElement).value
  clearResults()
  loading.value = valid.value && ready()
  debounce = setTimeout(() => visit(query.value.trim()), 200)
}
function submit() {
  if (!valid.value) return
  visit(query.value.trim(), 1, 'submit')
}
function fromLocation() {
  editingHistory = false
  clearResults()
  const params = new URLSearchParams(location.search)
  query.value = params.get('q') || ''
  const requestedPage = Number(params.get('page'))
  if (valid.value) void search(Number.isSafeInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1)
}
watch(() => props.route, () => {
  if (internalHref === location.pathname + location.search) { internalHref = undefined; return }
  internalHref = undefined
  fromLocation()
}, { immediate: true })
watch(actor, fromLocation, { flush: 'sync' })
onBeforeUnmount(() => { disposed = true; clearResults() })
</script>

<template>
  <section class="search-page" aria-labelledby="search-title" :aria-busy="loading">
    <header class="page-heading"><div><h1 id="search-title">Search this Space</h1><p>Find topics, articles and replies you can read.</p></div></header>
    <form class="search-form" role="search" @submit.prevent="submit">
      <input :value="query" class="input" type="search" aria-label="Search words" placeholder="A title, phrase or a few words…" minlength="2" maxlength="100" required @input="edit">
      <button class="btn btn-primary" type="submit" :disabled="!valid">Search</button>
    </form>
    <p v-if="loading" role="status" class="hint">Searching…</p>
    <p v-else-if="error" class="error" role="alert">{{ error }} <button class="text-action" type="button" @click="search(page)">Try again</button></p>
    <p v-else-if="searched" class="search-summary" role="status">{{ items.length ? `${items.length} result${items.length === 1 ? '' : 's'} on this page` : 'No results' }} for “{{ submittedQuery }}”</p>
    <p v-else-if="!ready()" class="hint" role="status">{{ site.state === 'error' ? 'Search is unavailable until the Space reconnects.' : 'Checking your access…' }}</p>
    <p v-else class="hint" role="status">{{ query.trim() ? 'Type at least 2 characters to search.' : 'Search by a title, phrase or a few words.' }}</p>
    <p v-if="scopeLimited" class="hint">Search covers the first 100 accessible topics.</p>
    <article v-for="item in items" :key="item.id" class="search-result">
      <ParticipantAvatar :participant="item.author" />
      <div><h2><a :href="item.url">{{ item.title }}</a></h2><p class="search-author">{{ item.author.displayName }} <span v-if="item.author.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span></p><p>{{ item.excerpt }}</p><a class="search-open" :href="item.url">Read in conversation →</a></div>
    </article>
    <p v-if="searched && !loading && !error && !items.length" class="hint">Try a shorter phrase or another word from the conversation.</p>
    <nav v-if="page > 1 || more" class="directory-pagination" aria-label="Search result pages"><button v-if="page > 1" class="btn" type="button" :disabled="loading" @click="visit(submittedQuery, page - 1, 'page')">← Previous</button><button v-if="more" class="btn" type="button" :disabled="loading" @click="visit(submittedQuery, page + 1, 'page')">Next →</button></nav>
  </section>
</template>
