<script setup lang="ts">
import CommunityImage from './CommunityImage.vue'

defineProps<{
  eyebrow: string
  name: string
  backLabel: string
  artwork?: string | null
  description?: string
  accent?: string
}>()
const emit = defineEmits<{ back: [] }>()
</script>

<template>
  <header class="settings-workspace-heading">
    <button class="settings-workspace-back" type="button" @click="emit('back')">← Back to {{ backLabel }}</button>
    <div class="settings-workspace-identity" :style="{ '--community-accent': accent || '#fbbf24' }">
      <CommunityImage v-if="artwork !== undefined" :artwork="artwork" :name="name" />
      <div><p class="eyebrow">{{ eyebrow }}</p><h1 id="settings-title">{{ name }}</h1><p v-if="description">{{ description }}</p></div>
    </div>
  </header>
</template>
