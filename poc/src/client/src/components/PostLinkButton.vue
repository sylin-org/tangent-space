<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{ tangentKey: string; postId: string; label?: string }>()
const copied = ref(false)
const failed = ref(false)
const path = computed(() => '/t/' + encodeURIComponent(props.tangentKey) + '/' + encodeURIComponent(props.postId))
const url = computed(() => new URL(path.value, window.location.origin).href)
let feedbackTimer: ReturnType<typeof setTimeout> | undefined
let copyGeneration = 0
function clearFeedback() {
  if (feedbackTimer) clearTimeout(feedbackTimer)
  feedbackTimer = undefined
  copied.value = false
  failed.value = false
}
watch(url, () => { copyGeneration++; clearFeedback() })
onUnmounted(() => { copyGeneration++; clearFeedback() })

async function copyLink() {
  const target = url.value
  const generation = ++copyGeneration
  clearFeedback()
  try {
    await navigator.clipboard.writeText(target)
    if (generation !== copyGeneration || target !== url.value) return
    copied.value = true
    failed.value = false
    feedbackTimer = setTimeout(() => { if (generation === copyGeneration) clearFeedback() }, 2200)
  } catch {
    if (generation !== copyGeneration || target !== url.value) return
    copied.value = false
    failed.value = true
  }
}
</script>

<template>
  <span class="post-link-control">
    <button class="post-link-copy" type="button" :aria-label="label || 'Copy link to Post'" :title="label || 'Copy link to Post'" @click="copyLink">
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10 13a5 5 0 0 0 7.1 0l2.1-2.1a5 5 0 0 0-7.1-7.1L10.9 5"/><path d="M14 11a5 5 0 0 0-7.1 0l-2.1 2.1a5 5 0 0 0 7.1 7.1l1.2-1.2"/></svg>
    </button>
    <span v-if="copied" class="post-link-feedback" role="status">Link copied</span>
    <span v-if="failed" class="post-link-fallback" role="status">Copy unavailable. <a :href="path">Open link</a> or select <input :value="url" readonly aria-label="Post link URL" @focus="($event.target as HTMLInputElement).select()"></span>
  </span>
</template>
