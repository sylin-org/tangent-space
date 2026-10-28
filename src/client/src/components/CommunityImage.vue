<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = defineProps<{ artwork?: string | null; name: string }>()
const source = computed(() => {
  const value = props.artwork?.trim() ?? ''
  return /^https:\/\//i.test(value) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(value) || /^\/artwork\/[a-f0-9]{64}\.(png|jpg|webp)$/i.test(value)
    ? value : ''
})
const initial = computed(() => props.name.trim().slice(0, 1).toUpperCase() || 'T')
const broken = ref(false)
watch(source, () => { broken.value = false })
</script>

<template>
  <span class="community-image" aria-hidden="true">
    <img v-if="source && !broken" :src="source" alt="" loading="lazy" referrerpolicy="no-referrer" @error="broken = true">
    <span v-else class="community-image-fallback">{{ initial }}</span>
  </span>
</template>
