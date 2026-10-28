<script setup lang="ts">
import { computed } from 'vue'
import type { Directories } from '../composables/directories'
import TopicRow from './TopicRow.vue'
const props = defineProps<{ directories: Directories; tangentKey: string; signedIn: boolean; actorKey?: string }>()
const view = defineModel<string>('view', {default:'all'})
const views = [{key:'all',label:'All topics'},{key:'unread',label:'Unread'},{key:'following',label:'Following'},{key:'started',label:'Started by you'}]
const visible = computed(() => props.directories.topics.filter(topic => view.value === 'all' || view.value === 'unread' && (topic.summary?.unread || 0) > 0 || view.value === 'following' && topic.summary?.following || view.value === 'started' && topic.summary?.starter?.id === props.actorKey))
</script>
<template>
<section class="topic-list-section" aria-label="Topics">
<nav v-if="signedIn" class="view-filters topic-mobile-views" aria-label="Topic views"><button v-for="item in views" :key="item.key" type="button" :aria-pressed="view === item.key" @click="view = item.key">{{item.label}}</button></nav>
<div v-if="visible.length" class="directory-list-toolbar"><span>{{visible.length}} {{visible.length === 1 ? 'topic' : 'topics'}}<template v-if="directories.topicsNextPage || directories.topicsIncomplete"> on this page</template></span><span>Latest activity</span></div>
<div v-if="visible.length" id="topic-list" class="topic-rows"><div class="topic-table-heading" aria-hidden="true"><span>Topic</span><span>Participants</span><span>Replies</span><span>Latest</span></div><TopicRow v-for="topic in visible" :key="topic.key" :topic="topic" :tangent-key="tangentKey" :unread="topic.summary?.unread" /></div>
<p v-if="directories.topicsLoading" class="hint" role="status">Loading topics…</p>
<p v-else-if="directories.topicsError" class="error" role="alert">{{directories.topicsError}}</p>
<p v-else-if="!visible.length && directories.topicsLoaded" class="hint">{{view === 'all' ? 'No readable topics here yet.' : 'No matching topics on this page.'}}</p>
<p v-if="directories.topicsIncomplete" class="hint">This page is a bounded selection of readable topics.</p>
<button v-if="directories.topicsNextPage" class="btn" type="button" :disabled="directories.topicsLoading" @click="directories.loadTopics(tangentKey, directories.topicsNextPage ?? undefined)">Next page →</button>
</section>
</template>
