<script setup lang="ts">
// The masthead's account surface, ported from pages.js prepare(): the sign-in
// link carrying the return path, and the account menu with profile and sign-out.
import { computed, onMounted, onScopeDispose, ref } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'

import type { Activity } from '../composables/activity'

const props = defineProps<{ site: SiteStore; router: RouterStore; activity: Activity }>()

const emit = defineEmits<{ (e: 'sign-out'): void }>()

const accountMenu = ref<HTMLDetailsElement | null>(null)

const authenticationRoute = computed(() => ['sign-in', 'onboarding'].includes(props.router.route.kind))
const participant = computed(() => props.site.site?.participant ?? null)

const signInHref = computed(() => {
  void props.router.route
  return '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
})

const accountName = computed(() => participant.value?.displayName || 'Your account')
const accountHandle = computed(() => (participant.value?.handle ? '@' + participant.value.handle.replace(/^@/, '') : ''))
const accountMark = computed(() => (participant.value?.handle || 'You').slice(0, 1).toUpperCase())
const accountRole = computed(() => (participant.value?.isOwner ? 'Server owner' : 'Signed in'))
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
  <div class="masthead-brand">
    <a class="brand" href="/" aria-label="Tangent Space home"><span class="mark" aria-hidden="true"></span><span>Tangent Space</span></a>
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
  <nav class="masthead-actions" aria-label="Site actions">
    <a id="nav-sign-in" :href="signInHref" :hidden="!!participant">Sign in</a>
    <details id="nav-account" ref="accountMenu" class="account-menu" :hidden="!participant" @keydown="onMenuKeydown">
      <summary :aria-label="summaryLabel">
        <span id="nav-account-mark" class="account-mark" aria-hidden="true">{{ accountMark }}</span>
        <span class="account-identity"><span id="nav-account-name">{{ accountName }}</span><span id="nav-account-handle">{{ accountHandle }}</span></span>
        <span aria-hidden="true">⌄</span>
      </summary>
      <div class="account-popover">
        <p id="nav-account-role" class="eyebrow">{{ accountRole }}</p>
        <a id="nav-profile" :href="profileHref">Your profile</a>
        <button type="button" id="nav-sign-out" :hidden="!site.site?.signOut" @click="emit('sign-out')">Sign out</button>
      </div>
    </details>
  </nav>
</template>
