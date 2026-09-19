<script setup lang="ts">
import { useWorkspace } from '../stores/workspace'

const workspace = useWorkspace()
</script>

<template>
  <nav aria-label="Tangents">
    <h2>Tangents</h2>
    <ul class="list">
      <li v-for="tangent in workspace.tangents" :key="tangent.tangentRef">
        <button
          type="button"
          class="item"
          :class="{ active: workspace.activeTangentKey && tangent.tangentRef.endsWith(workspace.activeTangentKey) }"
          @click="workspace.openTangent(tangent.tangentRef.split('::')[1] ?? tangent.tangentRef)"
        >
          {{ tangent.name }}
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
