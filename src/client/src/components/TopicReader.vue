<script setup lang="ts">
import { useWorkspace, type WatchMode } from '../stores/workspace'

const workspace = useWorkspace()
const modes: WatchMode[] = ['all', 'replies', 'none']
</script>

<template>
  <section aria-label="Conversation">
    <div class="toolbar">
      <h1>{{ workspace.topicTitle }}</h1>
      <label class="watch">Watch
        <select :value="workspace.watchMode" @change="workspace.watchTangent(($event.target as HTMLSelectElement).value as WatchMode)">
          <option v-for="mode in modes" :key="mode" :value="mode">{{ mode }}</option>
        </select>
      </label>
      <button v-if="workspace.readCursor" class="mark-read" type="button" @click="workspace.markRead()">Mark read</button>
    </div>
    <article v-for="post in workspace.posts" :key="post.ref" class="post" :class="{ removed: post.removed }">
      <header>
        <strong>{{ post.authorName }}</strong>
        <time>{{ new Date(post.createdAt).toLocaleString() }}</time>
        <span v-if="post.editedAt" class="edited">(edited)</span>
      </header>
      <p class="text">{{ post.removed ? '[removed]' : post.text }}</p>
    </article>
  </section>
</template>

<style scoped>
.post { border-bottom: 1px solid var(--line, #ddd); padding: 0.6rem 0; }
.post.removed .text { color: var(--quiet, #777); font-style: italic; }
header { display: flex; gap: 0.5rem; align-items: baseline; }
time, .edited { color: var(--quiet, #777); font-size: 0.8rem; }
.text { white-space: pre-wrap; margin: 0.3rem 0 0; }
.toolbar { display: flex; align-items: baseline; gap: 1rem; justify-content: space-between; }
h1 { font-size: 1.3rem; margin: 0; }
.watch { color: var(--quiet, #777); font-size: 0.85rem; display: flex; gap: 0.35rem; align-items: center; }
.mark-read { border: 1px solid var(--line, #ddd); background: transparent; padding: 0.25rem 0.6rem; cursor: pointer; border-radius: 6px; }
</style>
