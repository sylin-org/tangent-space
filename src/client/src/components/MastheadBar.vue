<script setup lang="ts">
// The masthead's account surface provides the sign-in
// link carrying the return path, and the account menu with profile and sign-out.
import { computed, onMounted, onScopeDispose, ref } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'

import type { Activity } from '../composables/activity'
import type { Profiles } from '../composables/profiles'
import type { WorkspaceAttention } from '../composables/workspace-attention'
import AppearanceSwitch from './AppearanceSwitch.vue'
import SearchBox from './SearchBox.vue'

const props = defineProps<{ site: SiteStore; router: RouterStore; activity: Activity; profiles: Profiles; attention?: WorkspaceAttention; canCreate?: boolean; canStart?: boolean; writingLabel?: string; tangentName?: string; topicTitle?: string }>()

const emit = defineEmits<{ (e: 'sign-out'): void; (e: 'create'): void; (e: 'start'): void }>()

const accountMenu = ref<HTMLDetailsElement | null>(null)

const authenticationRoute = computed(() => ['sign-in', 'onboarding'].includes(props.router.route.kind))
const readingRoute = computed(() => ['topic', 'post'].includes(props.router.route.kind))
const readingBackHref = computed(() => 'tangent' in props.router.route ? '/t/' + encodeURIComponent(props.router.route.tangent) + '/topics' : '/')
const participant = computed(() => props.site.site?.participant ?? null)

const signInHref = computed(() => {
  void props.router.route
  return '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
})

// Fresh identity from the local profile cache; the arrival payload is the fallback.
const ownProfile = computed(() => props.profiles.snapshotOf(participant.value?.did))
const accountName = computed(() =>
  ownProfile.value?.displayName || participant.value?.displayName || participant.value?.handle || 'Your account')
const accountHandle = computed(() => (participant.value?.handle ? '@' + participant.value.handle.replace(/^@/, '') : ''))
const accountAvatar = computed(() => {
  const avatar = ownProfile.value?.avatar
  return typeof avatar === 'string' && avatar.startsWith('/api/profile-cache/avatar?') ? avatar : ''
})
const accountMark = computed(() =>
  (ownProfile.value?.displayName || participant.value?.handle || 'You').slice(0, 1).toUpperCase())
const accountRole = computed(() => (participant.value?.isOwner ? 'Space owner' : 'Signed in'))
const profileHref = computed(() =>
  '/u/' + encodeURIComponent(participant.value?.did || 'tangent:local:' + (participant.value?.participantRef ?? '')),
)
const summaryLabel = computed(() => 'Account menu for ' + (participant.value?.handle || 'you'))

function onDocumentClick(event: MouseEvent) {
  const menu = accountMenu.value
  if (menu && !menu.contains(event.target as Node)) menu.open = false
}

function onMenuKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && accountMenu.value?.open) {
    accountMenu.value.open = false
    accountMenu.value.querySelector('summary')?.focus()
  }
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
})

onScopeDispose(() => {
  document.removeEventListener('click', onDocumentClick)
})
</script>

<template>
  <div v-if="readingRoute" class="mobile-reading-header"><a class="mobile-reading-back" :href="readingBackHref" :aria-label="'Back to ' + (tangentName || 'Tangent')"><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="m14 6-6 6 6 6"/></svg></a><div class="mobile-reading-place"><span>{{ tangentName || ('tangent' in router.route ? router.route.tangent : 'Tangent') }}</span><strong>{{ topicTitle || 'Conversation' }}</strong></div></div>
  <div class="masthead-brand">
    <a class="brand" href="/" :aria-label="(site.server?.name || site.site?.name || 'Tangent Space') + ' home'"><span class="mark" aria-hidden="true"><img v-if="site.server?.markImageUrl" :src="site.server.markImageUrl" alt=""><template v-else>{{ (site.server?.name || site.site?.name || 'T').slice(0, 1) }}</template></span><span>{{ site.server?.name || site.site?.name || 'Tangent Space' }}</span></a>
    <p
      id="activity-status"
      class="connection-state"
      role="status"
      aria-live="polite"
      tabindex="0"
      :hidden="!participant || authenticationRoute"
      :data-state="activity.connection.state"
      :title="activity.connection.title"
      :aria-label="activity.connection.title"
    >
      <span id="activity-status-label">{{ activity.connection.label }}</span>
    </p>
  </div>
  <SearchBox :router="router" :identity-key="site.state + ':' + (participant?.participantRef || '')" />
  <nav class="masthead-actions" aria-label="Site actions">
    <a class="header-search" href="/search" aria-label="Search"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/></svg></a>
    <details v-if="participant && (canCreate || canStart)" class="new-menu"><summary class="btn btn-primary">+ New</summary><div class="new-menu-panel"><button v-if="canStart" type="button" @click="emit('start'); ($event.currentTarget as HTMLElement).closest('details')!.open = false">{{writingLabel || 'New topic'}}</button><button v-if="canCreate" type="button" @click="emit('create'); ($event.currentTarget as HTMLElement).closest('details')!.open = false">New Tangent</button></div></details>
    <a v-if="participant" class="header-following" href="/following">Following</a><a v-if="participant" class="header-notifications" href="/notifications" :aria-label="'Notifications, ' + (attention?.notifications.value.filter(item => item.unread).length || 0) + ' unread'"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9Z"/><path d="M9 21h6"/></svg><span v-if="attention?.notifications.value.some(item => item.unread)" class="notification-dot"></span></a>
    <a id="nav-sign-in" :href="signInHref" :hidden="!!participant">Sign in</a>
    <details id="nav-account" ref="accountMenu" class="account-menu" :hidden="!participant" @keydown="onMenuKeydown">
      <summary :aria-label="summaryLabel">
        <span id="nav-account-mark" class="account-mark" aria-hidden="true">
          <img v-if="accountAvatar" :src="accountAvatar" alt="">
          <template v-else>{{ accountMark }}</template>
        </span>
        <span class="account-identity"><span id="nav-account-name">{{ accountName }}</span><span id="nav-account-handle">{{ accountHandle }}</span></span>
        <span aria-hidden="true">⌄</span>
      </summary>
      <div class="account-popover">
        <div class="account-menu-identity"><strong>{{ accountName }}</strong><span>{{ accountHandle }} · {{ accountRole }}</span></div>
        <a id="nav-profile" :href="profileHref">Your profile</a>
        <a v-if="participant?.isOwner || site.server?.canManage" href="/settings">Space settings</a>
        <a href="/notifications">Notifications</a><p class="menu-label">Appearance</p><AppearanceSwitch />
        <button type="button" id="nav-sign-out" :hidden="!site.site?.signOut" @click="emit('sign-out')">Sign out</button>
      </div>
    </details>
  </nav>
</template>

<style src="../assets/mobile-reader.css"></style>
