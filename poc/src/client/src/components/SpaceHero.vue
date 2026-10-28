<script setup lang="ts">
import { computed } from 'vue'
import type { Directories, DirectoryCount } from '../composables/directories'
import { directoryCount } from '../composables/directories'

const props = defineProps<{
  name: string
  mark?: string | null
  image?: string | null
  byline?: string | null
  welcome?: string | null
  message?: string | null
  canManage?: boolean
  canCreate?: boolean
  preview?: boolean
  directories?: Directories
}>()
const emit = defineEmits<{ manage: []; create: [] }>()
const imageUrl = computed(() => {
  const url = props.image?.trim() || ''
  return (/^https:\/\//i.test(url) || /^\/(?!\/)/.test(url)) && !/[\u0000-\u001f\\]/.test(url) ? url : ''
})
const totals = computed(() => {
  const directory = props.directories
  if (!directory?.tangentsLoaded) return null
  const tangents = directory.tangents
  const incomplete = !!directory.tangentsNextPage || directory.tangentsIncomplete
  function sum(key: 'topics' | 'posts'): DirectoryCount | null {
    if (!tangents.every(item => item.summary?.[key])) return null
    return { value: tangents.reduce((total, item) => total + item.summary![key]!.value, 0), atLeast: incomplete || tangents.some(item => item.summary![key]!.atLeast) }
  }
  return { tangents: { value: tangents.length, atLeast: incomplete }, topics: sum('topics'), posts: sum('posts') }
})
</script>

<template>
  <header class="space-hero" :class="{ 'space-hero-preview': preview, 'space-hero-has-cover': !!imageUrl }">
    <img v-if="imageUrl" class="space-hero-cover" :src="imageUrl" alt="" referrerpolicy="no-referrer">
    <div class="space-hero-inner">
    <div class="space-hero-image" aria-hidden="true">
      <img v-if="mark" :src="mark" alt="" referrerpolicy="no-referrer"><span v-else>{{ (name || 'S').slice(0, 1).toUpperCase() }}</span>
    </div>
    <div class="space-hero-copy">
      <component :is="preview ? 'h3' : 'h1'" :id="preview ? undefined : 'tangent-return-title'">{{ name || 'Your Space' }}</component>
      <p v-if="byline" class="space-hero-byline">{{ byline }}</p>
      <p v-if="welcome" class="space-hero-welcome">{{ welcome }}</p>
      <p v-else-if="!byline && canManage" class="space-hero-welcome"><button class="space-welcome-edit" type="button" @click="emit('manage')">Add a byline and a welcome</button> · only stewards see this</p>
      <p v-if="totals && !preview" class="space-hero-stats" aria-label="Visible content"><span><strong>{{ directoryCount(totals.tangents) }}</strong> {{ totals.tangents.value === 1 ? 'Tangent' : 'Tangents' }}</span><span v-if="totals.topics"><strong>{{ directoryCount(totals.topics) }}</strong> {{ totals.topics.value === 1 ? 'topic' : 'topics' }}</span><span v-if="totals.posts"><strong>{{ directoryCount(totals.posts) }}</strong> {{ totals.posts.value === 1 ? 'post' : 'posts' }}</span></p>
      <p v-if="message" class="space-hero-message"><span>Today in this Space</span>{{ message }}</p>
    </div>
    </div>
  </header>
</template>
<style src="../assets/directories.css"></style>
