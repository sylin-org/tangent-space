<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { contentApi, type BlueskyConnection } from '../api'

const props = defineProps<{ postId: string; accountDid: string; accountLabel: string }>()
const emit = defineEmits<{ close: [] }>()
const connection = ref<BlueskyConnection | null>(null)
const busy = ref(false)
const message = ref('')
const failed = ref(false)
const publishedUrl = ref('')
const correctAccount = computed(() => !!connection.value?.actorDid && connection.value.actorDid === props.accountDid)
const canPublish = computed(() => correctAccount.value && connection.value?.canPublish === true)
const connectHref = computed(() => {
  const supplied = connection.value?.connectUrl
  if (!supplied) return undefined
  try {
    const url = new URL(supplied, window.location.origin)
    if (url.origin !== window.location.origin || url.pathname !== '/api/v1/content/bluesky/connect') return undefined
    url.searchParams.set('return', window.location.pathname + window.location.search)
    return url.pathname + url.search
  } catch { return undefined }
})

async function load() {
  busy.value = true
  failed.value = false
  message.value = ''
  try { connection.value = await contentApi.blueskyConnection() }
  catch (error) { failed.value = true; message.value = (error as Error).message }
  finally { busy.value = false }
}

async function publish() {
  if (!canPublish.value || busy.value) return
  busy.value = true
  failed.value = false
  message.value = ''
  try {
    const result = await contentApi.publishPost(props.postId)
    const match = /^at:\/\/([^/]+)\/app\.bsky\.feed\.post\/([^/]+)$/.exec(result.uri)
    if (match) publishedUrl.value = 'https://bsky.app/profile/' + encodeURIComponent(match[1]!) + '/post/' + encodeURIComponent(match[2]!)
    message.value = result.replayed ? 'This Post is already published on Bluesky.' : 'Published on Bluesky.'
  } catch (error) { failed.value = true; message.value = (error as Error).message }
  finally { busy.value = false }
}

onMounted(load)
</script>

<template>
  <section class="bluesky-publish stack" :aria-labelledby="'bluesky-title-' + postId" :aria-busy="busy">
    <header><h4 :id="'bluesky-title-' + postId">Publish this Post to Bluesky</h4><button class="btn btn-quiet" type="button" :disabled="busy" @click="emit('close')">Close</button></header>
    <p>This publishes a public copy of your Post as <strong>{{ accountLabel }}</strong>.</p>
    <p v-if="busy && !connection" class="hint" role="status">Checking your Bluesky connection…</p>
    <template v-if="connection">
      <p v-if="canPublish" class="hint">Your Bluesky account is connected.</p>
      <p v-else-if="connection.status === 'unavailable'" class="hint">Bluesky publication is unavailable for this account.</p>
      <p v-else-if="connection.status === 'reauthorize'" class="hint">Authorize this account to publish Posts to Bluesky.</p>
      <p v-else class="hint">Connect your Bluesky account before publishing.</p>
      <p v-if="connection.actorDid && !correctAccount" class="error">The connection belongs to a different account. Sign in with the intended account and reopen this Post.</p>
      <div class="publish-actions">
        <a v-if="!canPublish && connectHref" class="btn btn-quiet" :href="connectHref">{{ connection.status === 'reauthorize' ? 'Authorize Bluesky publication' : 'Connect Bluesky' }}</a>
        <button v-if="!canPublish" type="button" class="btn btn-quiet" :disabled="busy" @click="load">Check connection</button>
        <button v-if="canPublish && !publishedUrl" type="button" class="btn btn-primary" :disabled="busy" @click="publish">{{ busy ? 'Publishing…' : failed ? 'Retry publication' : 'Publish publicly to Bluesky' }}</button>
      </div>
      <p v-if="!canPublish && connectHref" class="hint">After connecting, return to this Post to choose whether to publish it.</p>
    </template>
    <button v-if="failed && !connection" type="button" class="btn btn-quiet" :disabled="busy" @click="load">Retry connection check</button>
    <p v-if="message" class="action-status" :class="{ error: failed }" role="status">{{ message }}</p>
    <a v-if="publishedUrl" :href="publishedUrl" target="_blank" rel="noopener noreferrer">View on Bluesky ↗</a>
  </section>
</template>

<style scoped>
.bluesky-publish { margin-top: 1rem; padding: 1rem; border: 1px solid var(--line); border-radius: .75rem; }
header, .publish-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; }
header { justify-content: space-between; }
h4 { margin: 0; }
</style>
