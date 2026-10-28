<script setup lang="ts">
import { computed, nextTick, onMounted, onScopeDispose, ref, watch } from 'vue'
import { httpRequest } from '../api'
import type { RouterStore } from '../stores/router'
import type { DirectoryParticipant } from '../composables/directories'

const props = defineProps<{ router: RouterStore; identityKey: string }>()
type Match = { id: string; title: string; url: string; excerpt: string; author: DirectoryParticipant }
const field = ref<HTMLFormElement | null>(null), input = ref<HTMLInputElement | null>(null)
const query = ref(''), items = ref<Match[]>([]), error = ref('')
const open = ref(false), focused = ref(false), busy = ref(false), composing = ref(false)
const active = ref(-1), limited = ref(false)
const valid = computed(() => query.value.trim().length >= 2 && query.value.trim().length <= 100)
const allHref = computed(() => '/search?q=' + encodeURIComponent(query.value.trim()))
const activeId = computed(() => active.value >= 0 ? 'space-search-option-' + active.value : undefined)
let timer: ReturnType<typeof setTimeout> | undefined
let request: AbortController | undefined
let generation = 0

function cancel() {
  generation++
  clearTimeout(timer)
  request?.abort()
  request = undefined
  busy.value = false
}
function dismiss() {
  cancel()
  open.value = false
  active.value = -1
  items.value = []
  error.value = ''
  limited.value = false
}
function schedule(delay = 200) {
  cancel()
  active.value = -1
  items.value = []
  error.value = ''
  limited.value = false
  open.value = focused.value && valid.value && !composing.value
  if (!open.value) return
  busy.value = true
  const token = generation, value = query.value.trim(), actor = props.identityKey
  timer = setTimeout(async () => {
    const controller = new AbortController()
    request = controller
    try {
      const response = await httpRequest('/api/search?q=' + encodeURIComponent(value) + '&page=1', {
        cache: 'no-store', signal: controller.signal,
      })
      const data = await response.json()
      if (token !== generation || actor !== props.identityKey || !open.value) return
      if (!response.ok) throw new Error(data.message || 'Search is unavailable. Try again.')
      items.value = data.items.slice(0, 5)
      limited.value = data.scopeLimited === true
    } catch {
      if (token === generation && !controller.signal.aborted) error.value = 'Search is unavailable. Try again.'
    } finally {
      if (token === generation) busy.value = false
    }
  }, delay)
}
watch(query, () => schedule(), { flush: 'sync' })
watch(() => props.identityKey, () => { dismiss(); query.value = ''; focused.value = false }, { flush: 'sync' })
watch(() => props.router.route, () => {
  dismiss()
  focused.value = false
  if (props.router.route.kind === 'search') query.value = new URLSearchParams(location.search).get('q') || ''
}, { immediate: true, flush: 'sync' })

function show() {
  focused.value = true
  schedule()
}
function submit() {
  if (composing.value || !valid.value) return
  const href = open.value && active.value >= 0 ? items.value[active.value]?.url : allHref.value
  if (!href) return
  dismiss()
  input.value?.blur()
  props.router.navigate(href)
}
function select(event: MouseEvent, href: string) {
  dismiss()
  if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return
  event.preventDefault()
  input.value?.blur()
  props.router.navigate(href)
}
function keydown(event: KeyboardEvent) {
  if (event.isComposing || composing.value) return
  if (event.key === 'Escape') {
    if (open.value) event.preventDefault()
    dismiss()
    return
  }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    if (!open.value) { show(); return }
    if (!items.value.length) return
    active.value = event.key === 'ArrowDown'
      ? (active.value + 1) % items.value.length
      : (active.value <= 0 ? items.value.length : active.value) - 1
    void nextTick(() => document.getElementById(activeId.value!)?.scrollIntoView?.({ block: 'nearest' }))
  }
}
function focusout(event: FocusEvent) {
  if (event.relatedTarget && field.value?.contains(event.relatedTarget as Node)) return
  focused.value = false
  dismiss()
}
function outside(event: PointerEvent) {
  if (!field.value?.contains(event.target as Node)) dismiss()
}
function shortcut(event: KeyboardEvent) {
  const target = event.target as HTMLElement | null
  if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey || event.isComposing
      || target?.closest('input,textarea,select,[contenteditable=true]')) return
  if (!input.value?.getBoundingClientRect().width) return
  event.preventDefault()
  input.value.focus()
}
onMounted(() => {
  document.addEventListener('pointerdown', outside)
  document.addEventListener('keydown', shortcut)
})
onScopeDispose(() => {
  cancel()
  document.removeEventListener('pointerdown', outside)
  document.removeEventListener('keydown', shortcut)
})
</script>

<template>
  <form ref="field" class="header-search-field" role="search" @submit.prevent="submit" @focusout="focusout">
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/></svg>
    <input ref="input" v-model="query" type="search" role="combobox" aria-label="Search this Space" placeholder="Search this Space…"
      aria-autocomplete="list" :aria-expanded="open" :aria-controls="open ? 'space-search-suggestions' : undefined" :aria-activedescendant="activeId"
      autocomplete="off" minlength="2" maxlength="100" required @focus="show" @keydown="keydown"
      @compositionstart="composing = true; dismiss()" @compositionend="composing = false; schedule()">
    <button type="submit" aria-label="Search"><kbd aria-hidden="true">/</kbd></button>
    <div v-if="open" class="search-suggestions">
      <p v-if="busy" class="search-suggestion-status" role="status">Searching…</p>
      <div v-else-if="error" class="search-suggestion-status" role="alert">{{ error }} <button type="button" @click="schedule(0)">Retry</button></div>
      <p v-else-if="!items.length" class="search-suggestion-status" role="status">No results for “{{ query.trim() }}”</p>
      <p v-else class="visually-hidden" role="status">{{ items.length }} suggestions. Use the arrow keys to choose a result.</p>
      <div id="space-search-suggestions" role="listbox" aria-label="Search suggestions" :aria-busy="busy">
        <a v-for="(item, index) in items" :id="'space-search-option-' + index" :key="item.id" :href="item.url"
          class="search-suggestion" role="option" :aria-selected="active === index" tabindex="-1"
          @mousedown.left.prevent @click="select($event, item.url)">
          <strong>{{ item.title }}</strong><span class="search-suggestion-excerpt">{{ item.excerpt }}</span>
          <small>{{ item.author.displayName }}<template v-if="item.author.classification?.toLowerCase() === 'agent'"> · Agent</template></small>
        </a>
      </div>
      <p v-if="limited" class="search-suggestion-status">Search covers the first 100 accessible topics.</p>
      <a class="search-suggestion-all" :href="allHref" @click="select($event, allHref)">View all results for “{{ query.trim() }}” <span aria-hidden="true">→</span></a>
    </div>
  </form>
</template>

<style scoped>
.header-search-field{position:relative}
.header-search-field:focus-within{border-color:var(--ts-accent);box-shadow:0 0 0 2px var(--ts-accent-soft)}
.header-search-field>svg{flex-shrink:0}
.search-suggestions{position:absolute;top:calc(100% + 8px);left:0;width:min(480px,calc(100vw - 40px));min-width:100%;max-height:min(560px,calc(100dvh - 90px));overflow:auto;overscroll-behavior:contain;background:var(--ts-surface);color:var(--ts-text);border:1px solid var(--ts-line);border-radius:12px;box-shadow:0 12px 36px #0003;z-index:80;text-align:left}
.search-suggestion{display:grid;gap:5px;padding:14px 16px;color:var(--ts-text);text-decoration:none;border-bottom:1px solid var(--ts-line);overflow-wrap:anywhere}
.search-suggestion strong{font:600 15px/1.35 var(--ts-font-ui)}
.search-suggestion small{font:var(--ts-type-meta);color:var(--ts-text-3)}
.search-suggestion-excerpt{font:13px/1.45 var(--ts-font-ui);color:var(--ts-text-2);display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
.search-suggestion:hover,.search-suggestion[aria-selected=true]{background:var(--ts-accent-soft)}
.search-suggestion[aria-selected=true]{box-shadow:inset 3px 0 var(--ts-accent)}
.search-suggestion-status{margin:0;padding:16px;font:var(--ts-type-ui-small);color:var(--ts-text-2);overflow-wrap:anywhere}
.search-suggestion-status button{color:var(--ts-accent);text-decoration:underline;padding:4px}
.search-suggestion-all{display:flex;justify-content:space-between;gap:12px;padding:14px 16px;font:var(--ts-type-ui-small);color:var(--ts-accent);text-decoration:none;overflow-wrap:anywhere}
.search-suggestion-all:hover{background:var(--ts-accent-soft)}
</style>
