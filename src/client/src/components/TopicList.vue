<script setup lang="ts">
import { useWorkspace } from '../stores/workspace'
import { topicKeyOf } from '../api'

const workspace = useWorkspace()
</script>

<template>
  <nav v-if="workspace.topics.length" aria-label="Topics">
    <h2>Topics</h2>
    <ul class="list">
      <li v-for="topic in workspace.topics" :key="topic.topicRef">
        <button
          type="button"
          class="item"
          :class="{ active: workspace.activeTopicKey === topicKeyOf(topic.topicRef) }"
          @click="workspace.openTopic(topicKeyOf(topic.topicRef))"
        >
          {{ topic.title }}
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
.item { width: 100%; text-align: left; padding: 0.4rem 0.6rem; border: 0; background: transparent; cursor: pointer; border-radius: 6px; }
.item.active, .item:hover { background: var(--hover, #eee); }
h2 { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.06em; color: var(--quiet, #777); }
</style>
