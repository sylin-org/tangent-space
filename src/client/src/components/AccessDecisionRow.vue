<script setup lang="ts">
// One access decision row, ported from access.js renderAccess: chips for the chosen
// tokens, a combobox that offers @everyone, @authenticated and the server's roles,
// and (in a Topic) a return to the Tangent's inherited default.
import { computed, ref } from 'vue'
import type { SettingsDecision } from '../composables/settings'
import type { RoleOption } from '../composables/roles-api'

const props = defineProps<{
  decision: SettingsDecision
  modelValue: string[] | null
  choices: RoleOption[]
  parentTokens: string[]
  topicContext: boolean
}>()

const emit = defineEmits<{ (e: 'update:modelValue', value: string[] | null): void }>()

const query = ref('')
const open = ref(false)
const activeIndex = ref(0)

const isInherited = computed(() => props.modelValue == null)
const chosen = computed<string[]>(() => props.modelValue ?? props.parentTokens)

const stateLabel = computed(() => (isInherited.value ? 'Inherited from Tangent' : 'Set here'))

function tokenLabel(token: string): string {
  if (token === 'everyone') return '@everyone'
  if (token === 'authenticated') return '@authenticated'
  const role = props.choices.find(value => value.id === token)
  return '@' + (role?.name || token.replace(/^(role|group):/, ''))
}

function tokenColor(token: string): string {
  return props.choices.find(value => value.id === token)?.color || (token === 'everyone' ? '#6fd9b3' : '#d7c8ff')
}

const suggestions = computed<RoleOption[]>(() => {
  const text = query.value.trim().replace(/^@/, '').toLocaleLowerCase()
  const selected = new Set(chosen.value)
  return props.choices
    .filter(option => !selected.has(option.id) && (!text || option.name.toLocaleLowerCase().includes(text) || option.id.toLocaleLowerCase().includes(text)))
    .slice(0, 12)
})

function choose(option: RoleOption) {
  const base = props.modelValue == null ? [...chosen.value] : [...props.modelValue]
  if (!base.includes(option.id)) base.push(option.id)
  emit('update:modelValue', base)
  query.value = ''
  open.value = false
}

function remove(token: string) {
  if (props.modelValue == null) return
  emit('update:modelValue', props.modelValue.filter(value => value !== token))
}

function inherit() {
  emit('update:modelValue', null)
  query.value = ''
}

function onInput() {
  activeIndex.value = 0
  open.value = true
}

function onBlur() {
  setTimeout(() => {
    open.value = false
  }, 100)
}

function onKeydown(event: KeyboardEvent) {
  const values = suggestions.value
  if (event.key === 'Escape') {
    open.value = false
    return
  }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    activeIndex.value = (activeIndex.value + (event.key === 'ArrowDown' ? 1 : -1) + Math.max(values.length, 1)) % Math.max(values.length, 1)
  }
  if (event.key === 'Enter' && open.value && values[activeIndex.value]) {
    event.preventDefault()
    choose(values[activeIndex.value]!)
  }
}
</script>

<template>
  <section class="access-action" :data-action="decision.key">
    <div class="access-action-head">
      <h3>{{ decision.label }}</h3>
      <span class="access-action-state" :class="{ 'is-inherited': isInherited }">{{ stateLabel }}</span>
    </div>
    <p class="access-action-note">{{ decision.description }}</p>
    <div class="access-chip-row" :aria-label="decision.label + ' selections'" :class="{ 'is-inherited': isInherited }">
      <span v-if="!chosen.length" class="access-chip-empty">{{ isInherited ? 'No inherited roles' : 'Only global stewards' }}</span>
      <span v-for="token in chosen" :key="token" class="access-chip" :style="{ '--role-color': tokenColor(token) }">
        <i class="access-chip-dot" aria-hidden="true"></i>
        <strong>{{ tokenLabel(token) }}</strong>
        <button v-if="!isInherited" type="button" :aria-label="'Remove ' + tokenLabel(token)" @click="remove(token)">×</button>
      </span>
    </div>
    <div class="access-combobox">
      <input
        v-model="query"
        class="input"
        type="search"
        placeholder="Type @role to add…"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="open && suggestions.length > 0"
        :aria-label="decision.label"
        @focus="open = true"
        @input="onInput"
        @blur="onBlur"
        @keydown="onKeydown"
      >
      <div v-if="open && suggestions.length" class="access-suggestions" role="listbox">
        <button
          v-for="(option, index) in suggestions"
          :key="option.id"
          type="button"
          class="access-suggestion"
          role="option"
          :aria-selected="index === activeIndex"
          :style="{ '--role-color': option.color }"
          @mousedown.prevent
          @click="choose(option)"
        >
          <i aria-hidden="true"></i>
          <span><strong>{{ option.name }}</strong><small>{{ option.description }}</small></span>
        </button>
      </div>
    </div>
    <div class="access-action-footer">
      <p class="access-action-note">{{ decision.bypass }}</p>
      <button v-if="topicContext" type="button" class="access-inherit" @click="inherit">Use Tangent default</button>
    </div>
  </section>
</template>
