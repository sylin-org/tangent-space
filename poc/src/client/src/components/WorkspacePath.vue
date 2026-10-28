<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Directories, TangentCard } from '../composables/directories'
import type { RouterStore } from '../stores/router'
import type { Topic } from '../composables/conversation'
import type { Settings } from '../composables/settings'
import FollowButton from './FollowButton.vue'
import { useSite } from '../stores/site'
import CommunityImage from './CommunityImage.vue'
const props = defineProps<{ router: RouterStore; directories: Directories; tangent: TangentCard | null; topic: Topic | null; settings: Settings; canCreate: boolean; canManageSpace: boolean; writingTitle?: string }>()
const site = useSite()
const emit = defineEmits<{ create: []; manage: []; start: []; index: [] }>()
const switcher = ref<HTMLDetailsElement | null>(null)
watch(() => props.router.route, () => { if (switcher.value) switcher.value.open = false })
const sectionLabels: Record<string, string> = { server: 'General', appearance: 'Appearance', context: 'About', people: 'People', roles: 'Roles', access: 'Access', sources: 'Sources', storage: 'Storage', details: 'Details' }
const section = computed(() => sectionLabels[props.settings.activeTab.value] || 'Settings')
const isCreation = computed(() => ['new-tangent', 'new-topic'].includes(props.router.route.kind))
const isTopic = computed(() => ['topic', 'post', 'topic-settings'].includes(props.router.route.kind))
const tangentHref = computed(() => '/t/' + encodeURIComponent(props.tangent?.key || ('tangent' in props.router.route ? props.router.route.tangent : '')) + '/topics')
const signInHref = computed(() => {
  void props.router.route
  return '/sign-in?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
})
</script>
<template>
  <nav class="workspace-path" :class="{ 'reader-path': router.route.kind === 'topic' || router.route.kind === 'post' }" aria-label="Location">
    <span class="path-elbow" aria-hidden="true"></span>
    <div class="path-crumbs">
      <details v-if="'tangent' in router.route" ref="switcher" class="tangent-switcher" @keydown.esc="switcher && (switcher.open = false)">
        <summary><CommunityImage :name="tangent?.name || router.route.tangent" :artwork="tangent?.artwork" /><span class="path-tangent-label">{{ tangent?.name || router.route.tangent }}</span><span aria-hidden="true">⌄</span></summary>
        <div class="path-switcher-menu">
          <a v-for="item in directories.tangents" :key="item.key" :href="'/t/' + encodeURIComponent(item.key) + '/topics'"><CommunityImage :name="item.name" :artwork="item.artwork" /><span>{{ item.name }}<small>{{ item.kind === 'articles' ? 'Articles' : 'Discussions' }}</small></span></a>
          <button v-if="directories.tangentsNextPage" type="button" @click="directories.loadTangents(directories.tangentsNextPage ?? undefined)">More Tangents</button>
          <button v-if="canCreate" type="button" @click="emit('create')">New Tangent</button><a href="/">All Tangents</a>
        </div>
      </details>
      <template v-if="router.route.kind === 'new-tangent'"><a href="/">Home</a><span class="path-separator">/</span><span aria-current="page">New Tangent</span></template>
      <template v-else-if="router.route.kind === 'new-topic'"><span class="path-separator">/</span><span id="creation-path-title" class="path-current" aria-current="page">{{ writingTitle || (tangent?.kind === 'articles' ? 'New article' : 'New topic') }}</span><span v-if="tangent?.kind === 'articles'" class="creation-draft-tag">Draft</span></template>
      <template v-else-if="router.isSettings"><span v-if="'tangent' in router.route" class="path-separator">/</span><a v-if="router.route.kind !== 'settings'" :href="tangentHref">Settings</a><a v-else href="/settings">Space settings</a><span class="path-separator">/</span><span aria-current="page">{{ section }}</span><span v-if="router.route.kind === 'settings' && settings.activeTab.value === 'roles'" id="role-path-slot"></span></template>
      <template v-else-if="isTopic"><span class="path-separator">/</span><span class="path-current" aria-current="page">{{ topic?.title || 'topic' }}</span></template>
      <span v-else-if="router.route.kind === 'home' || router.route.kind === 'tangents'" aria-current="page">Home</span>
      <span v-else-if="router.route.kind === 'notifications' || router.route.kind === 'following' || router.route.kind === 'search'" aria-current="page">{{ router.route.kind[0]!.toUpperCase() + router.route.kind.slice(1) }}</span>
      <span v-else-if="router.route.kind === 'participant'" aria-current="page">Profile</span>
      <span v-else-if="router.route.kind === 'sign-in'" aria-current="page">Sign in</span>
    </div>
    <div class="path-actions"><div v-show="router.route.kind === 'notifications'" id="notification-path-actions"></div><div v-show="router.route.kind === 'topic' || router.route.kind === 'post'" id="topic-path-actions"></div><div v-show="isCreation" id="creation-path-actions"></div><FollowButton v-if="!isCreation && tangent && 'tangent' in router.route && !router.isSettings" :tangent="tangent.key" :topic="isTopic ? topic?.key : undefined" :signed-in="!!site.site?.participant" />
      <a v-if="isTopic && !site.site?.participant" class="btn btn-primary mobile-reader-sign-in" :href="signInHref">Sign in</a>
      <button v-if="(router.route.kind === 'home' || router.route.kind === 'tangents') && canManageSpace" type="button" class="btn btn-quiet" @click="emit('manage')">Space settings</button>
      <a v-if="router.route.kind === 'topics' && (tangent?.canManage || tangent?.isOwner)" class="btn btn-quiet" :href="tangentHref.replace(/\/topics$/, '/settings')">Tangent settings</a>
      <button class="btn btn-quiet path-index-toggle" type="button" @click="emit('index')">{{ router.isSettings ? 'Settings' : isTopic ? 'Contents' : 'Index' }}</button>
    </div>
  </nav>
</template>
