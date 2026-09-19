<script setup lang="ts">
import { useWorkspace } from '../stores/workspace'

const workspace = useWorkspace()
</script>

<template>
  <section aria-label="Conversation">
    <h1>{{ workspace.topicTitle }}</h1>
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
h1 { font-size: 1.3rem; }
</style>
