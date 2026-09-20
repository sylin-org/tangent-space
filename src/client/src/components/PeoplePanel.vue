<script setup lang="ts">
// The People tab, ported from people.js: everyone who has arrived on this server,
// newest last, so an arrival holding no role is still visible.
import { onMounted, ref, watch } from 'vue'
import type { Settings } from '../composables/settings'

const props = defineProps<{ settings: Settings }>()
const { activeTab } = props.settings

interface Person {
  id?: string
  handle?: string | null
  did?: string | null
  label?: string
  avatar?: string
  roles?: string[]
  classification?: string
  isSuspended?: boolean
  joinedAt?: string
}

const people = ref<Person[]>([])
const status = ref('Loading people…')
const moreVisible = ref(false)
const page = ref(1)
let loading = false
let loaded = false

function when(value?: string): string {
  if (!value) return ''
  const at = new Date(value)
  return Number.isNaN(at.getTime()) ? '' : at.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

function target(person: Person): string {
  return person.handle || person.did || person.id || ''
}

function mark(person: Person): string {
  return (person.label || '?').slice(0, 1).toUpperCase()
}

function byline(person: Person): string {
  return person.handle ? '@' + person.handle : person.did || ''
}

async function load(next = false) {
  if (loading || (loaded && !next)) return
  loading = true
  if (!next) {
    page.value = 1
    people.value = []
  }
  status.value = 'Loading people…'
  try {
    const response = await fetch('/api/people?page=' + page.value, { credentials: 'same-origin' })
    if (response.status === 403) {
      status.value = 'Only the server owner can see this list.'
      return
    }
    if (!response.ok) throw new Error('That list could not be loaded.')
    const result = (await response.json()) as { people?: Person[]; total?: number; nextPage?: number | null }
    people.value = next ? [...people.value, ...(result.people ?? [])] : (result.people ?? [])
    const total = typeof result.total === 'number' ? result.total : people.value.length
    status.value = total === 1 ? '1 person has arrived here.' : total + ' people have arrived here.'
    moreVisible.value = !!result.nextPage
    if (result.nextPage) page.value = result.nextPage
    loaded = true
  } catch (error) {
    status.value = (error as Error).message
  } finally {
    loading = false
  }
}

watch(activeTab, name => {
  if (name === 'people') void load(false)
})

onMounted(() => {
  if (activeTab.value === 'people') void load(false)
})
</script>

<template>
  <div id="people-list" class="people-list">
    <a v-for="person in people" :key="(person.id || person.did || person.handle) ?? 'unknown'" class="person-row" :href="'/u/' + encodeURIComponent(target(person))">
      <span class="person-mark"><img v-if="person.avatar" :src="person.avatar" alt="" loading="lazy"><template v-else>{{ mark(person) }}</template></span>
      <div class="person-body">
        <strong>{{ person.label || 'Participant' }}</strong>
        <span class="person-handle">{{ byline(person) }}</span>
      </div>
      <div class="person-marks">
        <template v-if="person.roles?.length">
          <span v-for="role in person.roles" :key="role" class="person-chip">{{ role }}</span>
        </template>
        <span v-else class="person-chip person-chip-empty">no roles</span>
        <span v-if="person.classification === 'Agent'" class="person-chip person-chip-agent">🤖 Agent</span>
        <span v-else-if="person.classification === 'Human'" class="person-chip person-chip-human">👤 Human</span>
        <span v-if="person.isSuspended" class="person-chip person-chip-danger">suspended</span>
      </div>
      <span class="person-arrived" :title="'Arrived ' + (person.joinedAt || '')">{{ when(person.joinedAt) }}</span>
    </a>
  </div>
  <p id="people-status" class="hint" role="status">{{ status }}</p>
  <button id="people-more" class="btn btn-quiet" type="button" :hidden="!moreVisible" @click="load(true)">Show more</button>
</template>
