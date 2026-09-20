<script setup lang="ts">
// The Tangent collection, ported from rooms.js renderTangents + cardContents:
// one card per Tangent with its accent and artwork, and the settings gear for
// those who manage it. Unread marks arrive with the activity transport.
import { computed } from 'vue'
import type { Directories } from '../composables/directories'
import type { Activity } from '../composables/activity'

export interface DirectoryTangent {
  key: string
  name?: string
  description?: string
  motto?: string
  accent?: string
  artwork?: string
  canManage?: boolean
  isOwner?: boolean
  canCreateTopic?: boolean
  permissions?: { role?: string; allowedActions?: string[] }
  topics?: Array<{ key: string }>
}

const props = defineProps<{ directories: Directories; signedIn: boolean; activity?: Activity }>()

const emit = defineEmits<{ (e: 'settings', key: string): void }>()

function safeAccent(value?: string): string {
  return /^#[0-9a-f]{6}$/i.test(value || '') ? value! : '#d88957'
}

function validArtwork(value?: string): boolean {
  return !!value && (/^https:\/\//i.test(value) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(value) || /^\/artwork\/[a-f0-9]{64}\.(png|jpg|webp)$/.test(value))
}

const cards = computed(() => props.directories.tangents as DirectoryTangent[])

// The card's unread mark: the Topic activity the transport reports (rooms.js cardContents).
function unread(tangent: DirectoryTangent): { count: number; capped: boolean; replies: boolean } {
  const topics = tangent.topics ?? []
  let count = 0
  let capped = false
  let replies = false
  for (const topic of topics) {
    const activity = props.activity?.activityFor(topic.key)
    if (!activity) continue
    count += activity.unreadCount
    capped = capped || activity.unreadCountCapped
    replies = replies || activity.directReplies > 0
  }
  return { count, capped, replies }
}

function unreadLabel(tangent: DirectoryTangent): string {
  const mark = unread(tangent)
  if (mark.replies) return 'replies to you'
  if (!mark.count) return 'All caught up'
  return (mark.capped ? '50+' : String(mark.count)) + ' unread'
}

function unreadMark(tangent: DirectoryTangent): string {
  const mark = unread(tangent)
  if (mark.replies) return '◆'
  if (!mark.count) return '✦'
  return mark.capped ? '50+' : String(mark.count)
}
</script>

<template>
  <div id="tangent-list" class="tangent-list" aria-live="polite">
    <div v-for="tangent in cards" :key="tangent.key" class="tangent-card-wrap">
      <a
        class="tangent-card sylin-card"
        :href="'/t/' + encodeURIComponent(tangent.key) + '/topics'"
        :style="{ '--tangent-accent': safeAccent(tangent.accent) }"
        :class="{ 'has-artwork': validArtwork(tangent.artwork) }"
      >
        <span class="tcg-art">
          <img v-if="validArtwork(tangent.artwork)" :src="tangent.artwork" alt="" loading="lazy">
          <span v-else class="tcg-emblem">✦</span>
          <span class="tcg-edition">TANGENT</span>
        </span>
        <span class="tcg-divider"></span>
        <span class="tcg-disc activity-disc" :title="unreadLabel(tangent)">{{ unreadMark(tangent) }}</span>
        <span class="tcg-notch" aria-hidden="true"></span>
        <span class="tcg-pane">
          <strong class="tcg-name">{{ tangent.name || tangent.key }}</strong>
          <span class="tcg-description">{{ tangent.description || 'A shared place for conversation.' }}</span>
          <em v-if="tangent.motto" class="tcg-motto">“{{ tangent.motto }}”</em>
          <span class="tcg-foot tangent-card-footer">{{ signedIn ? 'No unread messages' : 'Explore this Tangent' }}</span>
        </span>
        <span class="tcg-holo" aria-hidden="true"></span>
        <span class="tcg-holo2" aria-hidden="true"></span>
      </a>
      <button
        v-if="tangent.canManage || tangent.isOwner"
        class="card-settings btn btn-quiet"
        type="button"
        title="Tangent settings"
        :aria-label="'Open settings for ' + (tangent.name || tangent.key)"
        @click.prevent="emit('settings', tangent.key)"
      >⚙</button>
    </div>
  </div>
  <p id="no-tangents" class="hint" :hidden="cards.length > 0">{{ signedIn ? 'No Tangents are available to this account yet.' : 'Sign in to discover the Tangents available to you.' }}</p>
  <button id="more-tangents" class="btn btn-quiet" type="button" :hidden="!directories.tangentsNextPage" @click="directories.loadTangents(directories.tangentsNextPage ?? undefined)">Load more Tangents</button>
</template>
