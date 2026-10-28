<script setup lang="ts">
const props = defineProps<{
  tabs: Array<{ id: string; label: string }>
  active: string
  tabPrefix: string
  label: string
}>()
const emit = defineEmits<{ select: [name: string] }>()

function onKeydown(event: KeyboardEvent, name: string) {
  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
  event.preventDefault()
  const index = props.tabs.findIndex(tab => tab.id === name)
  const next = props.tabs[(index + (event.key === 'ArrowRight' ? 1 : props.tabs.length - 1)) % props.tabs.length]
  if (!next) return
  emit('select', next.id)
  document.getElementById(`${props.tabPrefix}-${next.id}-tab`)?.focus()
}
</script>

<template>
  <nav class="workspace-section-tabs" role="tablist" :aria-label="label">
    <button v-for="tab in tabs" :id="`${tabPrefix}-${tab.id}-tab`" :key="tab.id" type="button" role="tab"
      :aria-controls="`${tabPrefix}-${tab.id}-panel`" :aria-selected="active === tab.id" :data-settings-tab="tab.id"
      @click="emit('select', tab.id)" @keydown="onKeydown($event, tab.id)">{{ tab.label }}</button>
  </nav>
</template>
