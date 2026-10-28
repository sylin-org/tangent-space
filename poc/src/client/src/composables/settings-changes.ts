import { computed, inject, onScopeDispose, provide, ref, shallowReactive, watch, type InjectionKey } from 'vue'

export interface SettingsChange {
  label: string
  count: () => number
  busy: () => boolean
  save: () => Promise<void>
  discard: () => void
  status: () => string
}
export function createSettingsChanges() {
  const entries = shallowReactive(new Map<symbol, SettingsChange>())
  const saving = ref(false)
  const feedback = ref('')
  const count = computed(() => [...entries.values()].reduce((total, item) => total + item.count(), 0))
  const busy = computed(() => saving.value || [...entries.values()].some(item => item.busy()))
  watch(count, value => { if (value && !saving.value) feedback.value = '' })
  async function save() {
    if (busy.value) return
    saving.value = true
    feedback.value = ''
    try {
      for (const item of entries.values()) {
        if (!item.count()) continue
        await item.save()
        if (item.count()) { feedback.value = item.status() || item.label + ' could not be saved.'; return }
      }
      feedback.value = 'Changes saved.'
    } catch (error) { feedback.value = (error as Error).message || 'Changes could not be saved.' } finally { saving.value = false }
  }
  function discard() {
    if (busy.value) return
    for (const item of entries.values()) if (item.count()) item.discard()
    feedback.value = ''
  }
  return { entries, count, busy, feedback, save, discard }
}
export type SettingsChanges = ReturnType<typeof createSettingsChanges>
const key: InjectionKey<SettingsChanges> = Symbol('settings changes')
export function provideSettingsChanges() { const changes = createSettingsChanges(); provide(key, changes); return changes }
export function registerSettingsChange(change: SettingsChange) {
  const changes = inject(key, null)
  if (changes) {
    const id = Symbol(change.label)
    changes.entries.set(id, change)
    onScopeDispose(() => changes.entries.delete(id))
  }
  return !!changes
}

export function useSettingsSubmit() { const changes = inject(key, null); return (fallback: () => Promise<void>) => changes ? changes.save() : fallback() }

