<script setup lang="ts">
import { computed, ref, watch, onMounted, nextTick } from 'vue'
import type { WorkspaceAttention } from '../composables/workspace-attention'
import { rolesRequest } from '../composables/roles-api'
import { useSite } from '../stores/site'
import ParticipantAvatar from './ParticipantAvatar.vue'
const props = defineProps<{ attention: WorkspaceAttention; signedIn: boolean }>()
const site = useSite(), filter = ref('all'), settingsOpen = ref(false), railReady = ref(false), pathReady = ref(false)
interface NotificationPreferences { notifyReplies: boolean; notifyMentions: boolean; notifyFollowing: boolean; notifyNewPeople: boolean }
const preferences = ref<NotificationPreferences | null>(null), baseline = ref(''), preferenceStatus = ref(''), preferenceError = ref(''), loadingPreferences = ref(false), saving = ref(false)
let preferenceVersion = 0
const dirty = computed(() => !!preferences.value && JSON.stringify(preferences.value) !== baseline.value)
const pageIsPartial = computed(() => props.attention.incomplete.value || props.attention.more.value || !!props.attention.previousCursor.value)
const unread = computed(() => props.attention.notifications.value.filter(item => item.unread).length)
const filters = computed(() => [{ id: 'all', label: 'All' }, { id: 'direct_reply', label: 'Replies' }, { id: 'direct_mention', label: 'Mentions' }, { id: 'watched_activity', label: 'Follows' }, ...(site.site?.participant?.isOwner ? [{ id: 'new_people', label: 'New people' }] : [])])
function filterCount(id: string) { return props.attention.notifications.value.filter(item => item.unread && (id === 'all' || item.kind === id)).length }
async function loadPreferences() {
  const actor = site.site?.participant?.participantRef, version = ++preferenceVersion
  preferences.value = null; baseline.value = ''; preferenceStatus.value = ''; preferenceError.value = ''; saving.value = false; loadingPreferences.value = false
  if (!actor) return
  loadingPreferences.value = true
  try {
    const current = await rolesRequest<NotificationPreferences>('/api/workspace/preferences')
    if (version !== preferenceVersion || actor !== site.site?.participant?.participantRef) return
    preferences.value = { notifyReplies: current.notifyReplies, notifyMentions: current.notifyMentions, notifyFollowing: current.notifyFollowing, notifyNewPeople: current.notifyNewPeople }
    baseline.value = JSON.stringify(preferences.value)
  } catch (cause) { if (version === preferenceVersion) preferenceError.value = (cause as Error).message }
  finally { if (version === preferenceVersion) loadingPreferences.value = false }
}
watch(() => site.site?.participant?.participantRef, () => { filter.value = 'all'; settingsOpen.value = false; void loadPreferences() }, { immediate: true })
async function savePreferences() {
  if (!preferences.value || saving.value || !dirty.value || !site.site?.participant) return
  const version = preferenceVersion, actor = site.site.participant.participantRef, submitted = JSON.stringify(preferences.value)
  saving.value = true; preferenceStatus.value = ''; preferenceError.value = ''
  try {
    await rolesRequest('/api/workspace/preferences', { method: 'PATCH', body: JSON.parse(submitted) })
    if (version !== preferenceVersion || actor !== site.site?.participant?.participantRef) return
    baseline.value = submitted; preferenceStatus.value = 'Notification settings saved.'
    await props.attention.load(null)
  } catch (cause) { if (version === preferenceVersion) preferenceError.value = (cause as Error).message }
  finally { if (version === preferenceVersion) saving.value = false }
}
function discardPreferences() { if (baseline.value && !saving.value) { preferences.value = JSON.parse(baseline.value); preferenceStatus.value = ''; preferenceError.value = '' } }
function chooseFilter(value: string) { filter.value = value; settingsOpen.value = false }
const shown = computed(() => props.attention.notifications.value.filter(item => filter.value === 'all' || item.kind === filter.value).slice().sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()))
function dayLabel(value: string) {
  const date = new Date(value), today = new Date(), yesterday = new Date(); yesterday.setDate(today.getDate() - 1)
  if (date.toDateString() === today.toDateString()) return 'Today'
  if (date.toDateString() === yesterday.toDateString()) return 'Yesterday'
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', ...(date.getFullYear() !== today.getFullYear() ? { year: 'numeric' } : {}) })
}
const groups = computed(() => { const grouped = new Map<string, typeof shown.value>(); for (const item of shown.value) { const key = dayLabel(item.createdAt); grouped.set(key, [...(grouped.get(key) || []), item]) } return [...grouped].map(([label, items]) => ({ label, items })) })
async function locateSlots() { await nextTick(); railReady.value = !!document.getElementById('notification-index-slot'); pathReady.value = !!document.getElementById('notification-path-actions') }
onMounted(locateSlots)
watch([() => site.established, () => props.signedIn], locateSlots, { flush: 'post' })
</script>
<style src="../assets/notifications.css"></style>
<template>
  <section class="notifications-page" aria-labelledby="notifications-title">
    <Teleport v-if="signedIn && railReady" to="#notification-index-slot"><nav class="notification-index index-links" aria-label="Notification filters"><h2 class="sidebar-heading">Notifications</h2><button v-for="item in filters" :key="item.id" type="button" :aria-current="filter === item.id && !settingsOpen ? 'page' : undefined" @click="chooseFilter(item.id)"><span>{{ item.label }}</span><span v-if="filterCount(item.id)" class="notification-filter-count">{{ filterCount(item.id) }} new</span></button><button type="button" class="notification-settings-link" :aria-current="settingsOpen ? 'page' : undefined" @click="settingsOpen = !settingsOpen">Notification settings</button></nav></Teleport>
    <Teleport v-if="signedIn && pathReady" to="#notification-path-actions"><button class="btn btn-quiet notification-mark-read" type="button" :disabled="attention.markingRead.value || attention.loading.value || !unread" @click="attention.markRead">{{ attention.markingRead.value ? 'Marking as read...' : 'Mark all as read' }}</button></Teleport>
    <header class="notifications-heading"><h1 id="notifications-title">{{ settingsOpen ? 'Notification settings' : 'Notifications' }}</h1><p v-if="!settingsOpen && signedIn">{{ attention.loading.value ? 'Loading notifications.' : attention.error.value ? 'Notifications could not be loaded.' : unread ? unread + ' new ' + (unread === 1 ? 'notification' : 'notifications') : pageIsPartial ? 'No unread notifications on this page.' : 'You are all caught up.' }}</p><p v-else-if="settingsOpen">Choose what reaches you here.</p></header>
    <a v-if="!signedIn" class="btn btn-primary" href="/sign-in/?return=%2Fnotifications">Sign in to see notifications</a>
    <template v-else-if="settingsOpen"><p v-if="loadingPreferences" role="status">Loading notification settings...</p><p v-if="preferenceError" class="error" role="alert">{{ preferenceError }} <button v-if="!preferences" class="btn btn-quiet" type="button" @click="loadPreferences">Retry</button></p><form v-if="preferences" class="notification-settings-form" @submit.prevent="savePreferences"><fieldset :disabled="saving"><label><span><strong>Replies to you</strong><small>Someone replies to one of your posts.</small></span><input v-model="preferences.notifyReplies" type="checkbox"></label><label><span><strong>Mentions</strong><small>Someone mentions your handle in a conversation.</small></span><input v-model="preferences.notifyMentions" type="checkbox"></label><label><span><strong>Places you follow</strong><small>New activity in the Tangents and topics you follow.</small></span><input v-model="preferences.notifyFollowing" type="checkbox"></label><label v-if="site.site?.participant?.isOwner"><span><strong>New people</strong><small>Someone asks to join your Space.</small></span><input v-model="preferences.notifyNewPeople" type="checkbox"></label></fieldset><footer><span class="hint" role="status">{{ preferenceStatus || (dirty ? 'Unsaved changes' : '') }}</span><button class="btn btn-quiet" type="button" :disabled="saving || !dirty" @click="discardPreferences">Discard</button><button class="btn btn-primary" type="submit" :disabled="saving || !dirty">{{ saving ? 'Saving...' : 'Save settings' }}</button></footer></form></template>
    <template v-else><div class="notification-mobile-filters" role="group" aria-label="Notification views"><button v-for="item in filters" :key="item.id" type="button" :aria-pressed="filter === item.id" @click="chooseFilter(item.id)">{{ item.label }}</button><button type="button" @click="settingsOpen = true">Settings</button></div><p v-if="attention.loading.value" role="status">Loading notifications...</p><p v-else-if="attention.error.value" class="error" role="alert">{{ attention.error.value }} <button class="btn btn-quiet" type="button" @click="attention.load()">Retry</button></p><div v-else-if="!shown.length" class="notifications-empty"><span aria-hidden="true">&#10003;</span><h2>{{ filter === 'all' ? (pageIsPartial ? 'No notifications on this page.' : 'Nothing needs your attention.') : 'No ' + (filters.find(item => item.id === filter)?.label.toLowerCase() || 'notifications') + (pageIsPartial ? ' on this page.' : ' yet.') }}</h2><p>Replies, mentions and activity from places you follow will appear here.</p><a href="/">Explore your Space &#8594;</a></div><template v-else><section v-for="group in groups" :key="group.label" class="notification-day" :aria-label="group.label"><h2>{{ group.label }}</h2><article v-for="item in group.items" :key="item.kind + ':' + item.id" class="notification-card" :class="{ 'is-unread': item.unread }"><ParticipantAvatar :participant="item.author" /><div><header><p><strong>{{ item.author.displayName || item.author.handle || 'Participant' }}</strong> <span v-if="item.author.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span> {{ item.kind === 'new_people' ? 'asked to join' : item.kind === 'direct_reply' ? 'replied to you in' : item.kind === 'direct_mention' ? 'mentioned you in' : 'posted in' }} <a :href="item.url">{{ item.title }}</a></p><time :datetime="item.createdAt" :title="new Date(item.createdAt).toLocaleString()">{{ new Date(item.createdAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) }}</time></header><p v-if="item.excerpt" class="notification-excerpt">{{ item.excerpt }}</p><footer><a :href="item.url">{{ item.kind === 'new_people' ? 'Review request' : 'Open conversation' }} <span aria-hidden="true">&#8594;</span></a><span v-if="item.unread" class="notification-unread-dot" aria-label="Unread"></span></footer></div></article></section></template><nav v-if="attention.previousCursor.value || attention.nextCursor.value" class="notification-history-hint" aria-label="Notification pages"><button v-if="attention.previousCursor.value" class="btn btn-quiet" type="button" :disabled="attention.loading.value" @click="attention.load(attention.previousCursor.value)">First page</button><button v-if="attention.nextCursor.value" class="btn btn-quiet" type="button" :disabled="attention.loading.value" @click="attention.load(attention.nextCursor.value)">Next page</button></nav><p v-if="attention.incomplete.value" class="hint notification-history-hint">This is a limited view of recent activity. <a href="/following">Open places you follow</a> to catch up in a specific conversation.</p><button v-if="attention.error.value" class="btn btn-quiet" type="button" @click="attention.load(null)">Return to first page</button></template>
  </section>
</template>
