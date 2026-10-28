<script setup lang="ts">
import ParticipantActions from './ParticipantActions.vue'
import { httpRequest } from '../api'

// The arrival directory includes participants with no granted roles.
import { computed, onUnmounted, ref, watch } from 'vue'
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
const search=ref(''),filter=ref('all'),lookupError=ref('')
const shown=computed(()=>people.value.filter(person=>(filter.value==='all'||person.classification?.toLowerCase()===(filter.value==='people'?'human':'agent')) && (!search.value || (person.label+' '+person.handle+' '+person.did).toLowerCase().includes(search.value.toLowerCase()))))
async function findPerson(){if(!search.value.trim())return;loading.value=true;lookupError.value='';try{const response=await httpRequest('/api/people/lookup?identifier='+encodeURIComponent(search.value.trim()));if(!response.ok)throw new Error('No participant was found under that handle or DID.');const person=await response.json() as Person;people.value=[person];nextPage.value=null;previous.value=[];filter.value='all';search.value='';status.value='Matching participant.'}catch(cause){lookupError.value=(cause as Error).message}finally{loading.value=false}}
const status = ref('Loading people…')
const page = ref(1)
const previous = ref<number[]>([])
const loading = ref(false)
let generation = 0

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

async function load() {
  const at = ++generation
  loading.value = true
  people.value = []
  status.value = 'Loading people…'
  try {
    const response = await httpRequest('/api/people?page=' + page.value, { credentials: 'same-origin' })
    if (at !== generation) return
    if (response.status === 403) {
      status.value = 'Only the server owner can see this list.'
      return
    }
    if (!response.ok) throw new Error('That list could not be loaded.')
    const result = (await response.json()) as { people?: Person[]; total?: number; nextPage?: number | null }
    people.value = result.people ?? []
    const total = typeof result.total === 'number' ? result.total : people.value.length
    status.value = total === 1 ? '1 participant has arrived here.' : total + ' participants have arrived here.'
    nextPage.value = result.nextPage || null
  } catch (error) {
    if (at === generation) status.value = (error as Error).message
  } finally {
    if (at === generation) loading.value = false
  }
}
const nextPage = ref<number | null>(null)
function next() {
  if (!nextPage.value || loading.value) return
  previous.value.push(page.value)
  page.value = nextPage.value
  void load()
}
function back() {
  if (!previous.value.length || loading.value) return
  page.value = previous.value.pop() || 1
  void load()
}

watch([activeTab, props.settings.actorKey], ([name]) => {
  generation++
  people.value = []
  page.value = 1
  previous.value = []
  nextPage.value = null
  if (name === 'people') void load()
}, { immediate: true })
onUnmounted(() => { generation++ })
</script>

<template>
  <form class="people-search" @submit.prevent="findPerson"><label class="visually-hidden" for="people-search">Find participants</label><input id="people-search" v-model="search" class="input" placeholder="Filter this page, or find by handle or DID"><button class="btn btn-quiet" type="submit" :disabled="loading||!search.trim()">Find account</button><button v-if="search||status==='Matching participant.'" class="btn btn-quiet" type="button" @click="search='';load()">Show all</button></form><div class="view-filters"><button v-for="item in ['all','people','agents']" :key="item" type="button" :aria-pressed="filter===item" @click="filter=item">{{item[0]!.toUpperCase()+item.slice(1)}}</button></div><p v-if="lookupError" class="error" role="alert">{{lookupError}}</p><p v-if="!loading&&people.length&&!shown.length" class="hint">No matching participants on this page. Use Find account for an exact handle or DID.</p>
  <div id="people-list" class="people-list">
    <div v-for="person in shown" :key="(person.id || person.did || person.handle) ?? 'unknown'" class="person-row">
      <span class="person-mark" :class="{'agent-avatar':person.classification === 'Agent'}"><img v-if="person.avatar" :src="person.avatar" alt="" loading="lazy"><template v-else>{{ mark(person) }}</template></span>
      <div class="person-body">
        <a :href="'/u/' + encodeURIComponent(target(person))"><strong>{{ person.label || 'Participant' }}</strong></a>
        <span class="person-handle">{{ byline(person) }}</span>
      </div>
      <div class="person-marks">
        <template v-if="person.roles?.length">
          <span v-for="role in person.roles" :key="role" class="person-chip">{{ role }}</span>
        </template>
        <span v-else class="person-chip person-chip-empty">no roles</span>
        <span v-if="person.classification === 'Agent'" class="person-chip person-chip-agent">Agent</span>
        <span v-else-if="person.classification === 'Human'" class="person-chip person-chip-human">Person</span>
        <span v-if="person.isSuspended" class="person-chip person-chip-danger">suspended</span>
      </div>
      <span class="person-arrived" :title="'Arrived ' + (person.joinedAt || '')">{{ when(person.joinedAt) }}</span>
      <ParticipantActions :identifier="target(person)" :label="person.label || 'Participant'" :actor-key="settings.actorKey.value"/>
    </div>
  </div>
  <p id="people-status" class="hint" role="status">{{ status }}</p>
  <div v-if="previous.length || nextPage" class="people-pagination"><button class="btn btn-quiet" type="button" :disabled="!previous.length || loading" @click="back">Previous</button><span>Page {{ page }}</span><button id="people-more" class="btn btn-quiet" type="button" :disabled="!nextPage || loading" @click="next">Next</button></div>
</template>
