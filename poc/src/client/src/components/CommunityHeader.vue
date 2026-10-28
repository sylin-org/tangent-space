<script setup lang="ts">
import {directoryCount} from '../composables/directories'
import type {TangentCard} from '../composables/directories'
import CommunityImage from './CommunityImage.vue'

defineProps<{
  name: string
  description?: string
  motto?: string
  artwork?: string
  accent?: string
  compact?: boolean
  preview?: boolean
  canManage?: boolean
  canStartTopic?: boolean
  summary?: TangentCard['summary']
  readAudience?: string
  kind?: 'discussions' | 'articles'
}>()
const emit = defineEmits<{ manage: []; startTopic: [] }>()
</script>

<template>
  <div class="active-tangent-head" :class="{ 'community-header-preview': preview, 'community-header-compact': compact, 'community-header-discussions': kind !== 'articles' }" :style="{ '--community-accent': accent || '#fbbf24' }">
    <CommunityImage :artwork="artwork" :name="name" />
    <div class="active-tangent-copy">
      <h1>{{ name }}</h1>
      <p v-if="motto" class="tangent-motto">{{ motto }}</p>
      <p v-if="description">{{ description }}</p><p v-if="summary || readAudience" class="tangent-directory-meta"><span v-if="summary?.topics">{{directoryCount(summary.topics)}} {{kind === 'articles' ? summary.topics.value === 1 ? 'article' : 'articles' : summary.topics.value === 1 ? 'topic' : 'topics'}}</span><span v-if="summary?.posts">{{directoryCount(summary.posts)}} {{summary.posts.value === 1 ? 'post' : 'posts'}}</span><span v-if="readAudience">{{readAudience === 'Public' ? 'Public doorstep · topics show who can read' : 'Admitted · only participants with access can read'}}</span></p>
    </div>
    <div v-if="!preview && (canManage || canStartTopic)" class="active-tangent-actions">
      <button v-if="canManage" class="btn btn-quiet tangent-manage-action" type="button" @click="emit('manage')">Tangent settings</button>
      <button v-if="canStartTopic" class="btn btn-primary new-writing-action" type="button" @click="emit('startTopic')">{{ kind === 'articles' ? 'New article' : 'New topic' }}</button>
    </div>
  </div>
</template>
