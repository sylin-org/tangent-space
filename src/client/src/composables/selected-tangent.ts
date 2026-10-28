import { computed, ref, watch } from 'vue'
import { httpRequest } from '../api'
import type { Directories, TangentCard } from './directories'

// A direct community link can address a Tangent beyond the bounded directory page.
export function createSelectedTangent(directories: Directories, key: () => string | null, actor: () => string) {
  const described = ref<{ key: string; actor: string; tangent: TangentCard } | null>(null)
  let request = 0

  const listed = computed(() => directories.tangents.find(item => item.key === key()) ?? null)
  const current = computed(() => listed.value ?? (described.value?.key === key() && described.value.actor === actor() ? described.value.tangent : null))

  async function load(force = false) {
    const tangentKey = key()
    const viewer = actor()
    const version = ++request
    if (!tangentKey || (!force && listed.value)) {
      described.value = null
      return
    }
    if (described.value?.key !== tangentKey || described.value.actor !== viewer) described.value = null
    try {
      const response = await httpRequest('/api/v1/tangents/' + encodeURIComponent(tangentKey), {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' },
      })
      if (version !== request || key() !== tangentKey || actor() !== viewer) return
      if (!response.ok) { described.value = null; return }
      const item = await response.json() as TangentCard
      if (version !== request || key() !== tangentKey || actor() !== viewer) return
      described.value = item.key === tangentKey ? { key: tangentKey, actor: viewer, tangent: item } : null
    } catch {
      if (version === request && key() === tangentKey && actor() === viewer) described.value = null
    }
  }

  watch([key, actor, listed], () => { void load() }, { immediate: true })
  return { current, load }
}
