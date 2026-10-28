<script setup lang="ts">
import { computed } from 'vue'
import { facetedSegments } from '../composables/facets'
import type { PostFormat } from '../composables/conversation'

const props = defineProps<{
  text: string
  format?: PostFormat
  html?: string | null
  facets?: unknown
  resolved?: Record<string, { handle?: string; displayName?: string; value?: string; profileUrl?: string }>
}>()

const markdown = computed(() => props.format === 'markdown' && typeof props.html === 'string')
const segments = computed(() => facetedSegments(props.text, props.facets, props.resolved ?? {}))
</script>

<template>
  <!-- html is derived and sanitized by the server; never render author source as HTML. -->
  <div v-if="markdown" class="post-content post-content-markdown" v-html="html"></div>
  <div v-else class="post-content post-content-plain">
    <template v-for="(segment, index) in segments" :key="index">
      <a v-if="segment.kind === 'mention'" class="mention" :href="segment.href" :title="segment.title">{{ segment.text }}</a>
      <span v-else-if="segment.kind === 'group'" class="mention-group" :title="segment.title">{{ segment.text }}</span>
      <span v-else-if="segment.kind === 'tag'" class="mention-tag">{{ segment.text }}</span>
      <template v-else>{{ segment.text }}</template>
    </template>
  </div>
</template>
