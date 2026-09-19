<script setup lang="ts">
import { onMounted } from 'vue'
import { useWorkspace } from './stores/workspace'
import TangentList from './components/TangentList.vue'
import TopicList from './components/TopicList.vue'
import TopicReader from './components/TopicReader.vue'
import Composer from './components/Composer.vue'

const workspace = useWorkspace()
onMounted(() => workspace.arrive())
</script>

<template>
  <div class="workspace">
    <aside class="sidebar">
      <header class="identity">
        <strong>{{ workspace.identity?.displayName ?? 'Tangent Space' }}</strong>
        <span v-if="workspace.identity?.handle" class="handle">{{ workspace.identity.handle }}</span>
      </header>
      <TangentList />
      <TopicList v-if="workspace.activeTangentKey" />
    </aside>
    <main class="content">
      <p v-if="workspace.error" class="error" role="alert">{{ workspace.error }}</p>
      <p v-else-if="workspace.loading && !workspace.posts.length" class="quiet">Loading…</p>
      <template v-else-if="workspace.activeTopicKey">
        <TopicReader />
        <Composer />
      </template>
      <p v-else class="quiet">Choose a Topic to read.</p>
    </main>
  </div>
</template>

<style scoped>
.workspace { display: flex; min-height: 100vh; }
.sidebar { width: 280px; border-right: 1px solid var(--line, #ddd); padding: 1rem; }
.identity { display: flex; flex-direction: column; margin-bottom: 1rem; }
.handle { color: var(--quiet, #777); font-size: 0.85rem; }
.content { flex: 1; padding: 1rem 1.5rem; max-width: 60rem; }
.error { color: var(--danger, #a11); }
.quiet { color: var(--quiet, #777); }
</style>
