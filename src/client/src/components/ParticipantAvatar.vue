<script setup lang="ts">
import { computed, ref, watch } from 'vue'
const props = defineProps<{ participant: { displayName?: string | null; handle?: string | null; avatar?: string | null; classification?: string | null } | null | undefined }>()
const name = computed(() => props.participant?.displayName || props.participant?.handle || 'Participant')
const initial = computed(() => name.value.slice(0, 1).toUpperCase())
const avatar = computed(() => {
  const value = props.participant?.avatar ?? ''
  return /^https:\/\//i.test(value) || /^\/api\/profile-cache\/avatar\?/.test(value) ? value : ''
})
const broken = ref(false)
watch(avatar, () => { broken.value = false })
</script>

<template>
  <span class="participant-avatar" :class="{ 'participant-avatar-agent': participant?.classification?.toLowerCase() === 'agent' }" aria-hidden="true"><img v-if="avatar && !broken" :src="avatar" alt="" loading="lazy" referrerpolicy="no-referrer" @error="broken = true"><span v-else>{{ initial }}</span></span>
</template>
