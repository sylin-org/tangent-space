<script setup lang="ts">
import { ref, watch } from 'vue'
import { rolesRequest } from '../composables/roles-api'
import { useSite } from '../stores/site'
const props = defineProps<{ signedIn: boolean }>()
const site = useSite()
interface Item { key: string; tangentKey: string; title: string; presentation: string; url: string }
const items = ref<Item[]>([])
const next = ref<number | null>(1)
const error = ref('')
const busy = ref(false)
let generation = 0
async function load() {
  if (!props.signedIn || !next.value || busy.value) return
  const token = generation
  busy.value = true
  error.value = ''
  try {
    const page = await rolesRequest<{ items: Item[]; nextPage: number | null }>('/api/workspace/following?page=' + next.value)
    if (token !== generation) return
    items.value = [...items.value, ...page.items].slice(-100)
    next.value = page.nextPage
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
  finally { if (token === generation) busy.value = false }
}
watch([() => props.signedIn, () => site.site?.participant?.participantRef], () => {
  generation++
  items.value = []
  next.value = 1
  error.value = ''
  busy.value = false
  void load()
}, { immediate: true })
</script>
<template>
  <section aria-labelledby="following-title"><h1 id="following-title">Following</h1><p class="hint">Tangents and topics you chose to follow.</p><a v-if="!signedIn" class="btn btn-primary" href="/sign-in/?return=%2Ffollowing">Sign in to see what you follow</a><p v-if="busy" role="status">Loading…</p><p v-if="error" class="error" role="alert">{{ error }}</p><p v-if="signedIn && !busy && !error && !items.length" class="hint">Nothing followed yet. Follow a Tangent or topic to hear about new posts.</p><div class="following-list"><a v-for="item in items" :key="item.key" :href="item.url"><span>{{ item.presentation === 'tangent' ? 'Tangent' : item.presentation === 'article' ? 'Article' : 'Discussion' }}</span><h2>{{ item.title }}</h2><span>Open →</span></a></div><button v-if="next && items.length" class="btn btn-quiet" type="button" :disabled="busy" @click="load">More followed places</button></section>
</template>
