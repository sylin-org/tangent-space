<script setup lang="ts">
import CommunityImage from './CommunityImage.vue'

defineProps<{ name: string; description?: string; artwork?: string; accent?: string; href?: string; preview?: boolean; canManage?: boolean; topicCount?: string; postCount?: string; kind?: 'discussions' | 'articles' }>()
const emit = defineEmits<{ manage: [] }>()
</script>

<template>
  <div class="tangent-row-identity" :class="{ 'community-directory-preview': preview }" :style="{ '--community-accent': accent || '#fbbf24' }">
    <component :is="href ? 'a' : 'div'" class="tangent-row-community" :href="href || undefined">
      <span class="tangent-row-image"><CommunityImage :artwork="artwork" :name="name" /></span>
      <span class="tangent-row-copy">
        <strong class="tangent-row-name">{{ name }}</strong>
        <span class="tangent-row-purpose">{{ description || 'No description yet.' }}</span>
        <span v-if="topicCount !== undefined || postCount !== undefined" class="tangent-row-counts" aria-label="Community activity">
          <span>{{ topicCount ?? '—' }} {{ kind === 'articles' ? 'Articles' : 'Topics' }}</span><span aria-hidden="true">·</span><span>{{ postCount ?? '—' }} Posts</span>
        </span>
      </span>
    </component>
    <button v-if="canManage" class="tangent-row-manage" type="button" :aria-label="'Manage ' + name" @click="emit('manage')">Manage</button>
  </div>
</template>
