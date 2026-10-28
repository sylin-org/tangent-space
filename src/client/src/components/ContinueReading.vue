<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useSite } from '../stores/site'
import type { Topic } from '../composables/conversation'

const props = defineProps<{ topic: Topic | null; active: boolean }>()
const site = useSite()
const actor = computed(() => site.state === 'signedIn' ? site.site?.participant?.participantRef : undefined)
const continuation = ref<Pick<Topic, 'tangentKey' | 'key' | 'title'> | null>(null)
const readingKey = (participant: string) => 'tangent:reading:' + encodeURIComponent(participant)
let generation = 0

watch(() => props.topic, topic => {
  if (actor.value && topic?.canRead) {
    try {
      localStorage.setItem(readingKey(actor.value), JSON.stringify({ tangentKey: topic.tangentKey, topicKey: topic.key }))
    } catch { /* Optional recall never blocks reading. */ }
  }
  if (props.active) void load()
}, { flush: 'sync' })
watch([actor, () => props.active], load, { immediate: true, flush: 'sync' })
onUnmounted(() => { generation++ })

async function load() {
  const token = ++generation, participant = actor.value
  continuation.value = null
  if (!participant || !props.active) return
  try {
    const saved = JSON.parse(localStorage.getItem(readingKey(participant)) || 'null')
    if (typeof saved?.tangentKey !== 'string' || !saved.tangentKey || typeof saved.topicKey !== 'string' || !saved.topicKey) return
    const response = await fetch('/api/v1/tangents/' + encodeURIComponent(saved.tangentKey) + '/topics/' + encodeURIComponent(saved.topicKey), { credentials: 'same-origin', cache: 'no-store' })
    if (!response.ok) return
    const topic = await response.json() as Topic
    if (token !== generation || actor.value !== participant || !props.active || !topic.canRead
      || topic.key !== saved.topicKey || topic.tangentKey !== saved.tangentKey || typeof topic.title !== 'string') return
    continuation.value = { tangentKey: topic.tangentKey, key: topic.key, title: topic.title }
  } catch { /* A missing recall never blocks home. */ }
}
</script>

<template>
  <a v-if="active && continuation" class="since-line reading-continuation" :href="'/t/' + encodeURIComponent(continuation.tangentKey) + '/topics/' + encodeURIComponent(continuation.key)">
    <span class="new-label">Continue reading</span><span>{{ continuation.title }}</span><strong aria-hidden="true">&rarr;</strong>
  </a>
</template>
