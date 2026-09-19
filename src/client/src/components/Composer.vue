<script setup lang="ts">
import { ref } from 'vue'
import { useWorkspace } from '../stores/workspace'

const workspace = useWorkspace()
const text = ref('')

async function submit() {
  if (!text.value.trim() || workspace.posting) return
  await workspace.post(text.value)
  text.value = ''
}
</script>

<template>
  <form v-if="workspace.activeTopicKey" class="composer" @submit.prevent="submit">
    <textarea
      v-model="text"
      rows="3"
      placeholder="Write a post…"
      aria-label="Write a post"
      :disabled="workspace.posting"
    ></textarea>
    <div class="row">
      <span v-if="workspace.error" class="error" role="alert">{{ workspace.error }}</span>
      <button class="btn" type="submit" :disabled="workspace.posting || !text.trim()">
        {{ workspace.posting ? 'Saving…' : 'Post' }}
      </button>
    </div>
  </form>
</template>

<style scoped>
.composer { margin-top: 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
textarea { width: 100%; resize: vertical; }
.row { display: flex; justify-content: flex-end; align-items: center; gap: 0.75rem; }
.error { color: var(--danger, #a11); margin-right: auto; }
.btn { padding: 0.4rem 1rem; cursor: pointer; }
.btn:disabled { opacity: 0.5; cursor: default; }
</style>
