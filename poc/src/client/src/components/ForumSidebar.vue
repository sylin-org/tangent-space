<script setup lang="ts">

import { computed } from 'vue'

import type { ArticleFilter, Directories, TangentCard } from '../composables/directories'

import type { RouterStore } from '../stores/router'

import type { Settings } from '../composables/settings'

import type { Conversation } from '../composables/conversation'

import CommunityImage from './CommunityImage.vue'

import ParticipantAvatar from './ParticipantAvatar.vue'

const props = defineProps<{ directories: Directories; router: RouterStore; settings: Settings; tangent: TangentCard | null; conversation: Conversation; expanded: boolean; canCreate: boolean; canManageSpace?: boolean; workspaceAvailable?: boolean; signedIn: boolean; topicView: string; articleFilter: ArticleFilter; writing: boolean }>()

const emit = defineEmits<{ create: []; view: [value:string]; articleFilter:[value:ArticleFilter] }>()

const home = computed(() => ['home', 'tangents'].includes(props.router.route.kind))

const currentTangent = computed(() => 'tangent' in props.router.route ? props.router.route.tangent : '')

const sections = computed(() => props.router.route.kind === 'settings'

  ? props.settings.peopleOnly?.value ? [{ id: 'people', label: 'People' }] : [{ id: 'server', label: 'General' }, { id: 'appearance', label: 'Appearance' }, { id: 'people', label: 'People' }, { id: 'roles', label: 'Roles' }, { id: 'access', label: 'Access' }, { id: 'sources', label: 'Sources' }, { id: 'storage', label: 'Storage' }]

  : props.router.route.kind === 'tangent-settings' ? [{ id: 'context', label: 'About' }, { id: 'people', label: 'People' }, { id: 'access', label: 'Access' }]

  : [{ id: 'context', label: 'Details' }, { id: 'people', label: 'People' }, { id: 'access', label: 'Access' }])

const articleMonths=computed(()=>[...new Set(props.directories.topics.map(topic=>topic.summary?.article?.createdAt.slice(0,7)).filter((value):value is string=>!!value))])

const articleAuthors=computed(()=>[...new Map(props.directories.topics.flatMap(topic=>topic.summary?.article?.author ? [[topic.summary.article.author.id,topic.summary.article.author] as const] : [])).values()])

function monthName(value:string){return new Date(value+'-15T12:00:00Z').toLocaleDateString(undefined,{month:'long',year:'numeric'})}

const article = computed(() => props.conversation.selectedTopic.value?.presentation === 'article' && ['topic', 'post'].includes(props.router.route.kind))

const replies = computed(() => props.conversation.messages.value.filter(item => item.id !== props.conversation.openingPost.value?.id && item.sequence !== 1))

function unread(tangent: TangentCard) { return tangent.summary?.recentTopics.reduce((total, topic) => total + (topic.summary?.unread || 0), 0) || 0 }

function postTime(value: string) { return new Date(value).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }) }

</script>

<template>

  <aside class="forum-sidebar" :class="{ 'is-expanded': expanded }" aria-label="Page index">

    <nav v-if="router.isSettings && settings.shellVisible.value" class="index-links" aria-label="Settings sections">

      <p class="sidebar-heading">{{ router.route.kind === 'settings' ? 'Space settings' : tangent?.name || 'Settings' }}</p>

      <button v-for="section in sections" :key="section.id" type="button" :aria-current="settings.activeTab.value === section.id ? 'page' : undefined" @click="settings.settingsTab(section.id)">{{ section.label }}</button>

      <template v-if="router.route.kind === 'settings'"><p class="sidebar-heading">Tangents</p><a v-for="item in directories.tangents.filter(item => item.canManage || item.isOwner)" :key="item.key" :href="'/t/' + encodeURIComponent(item.key) + '/settings'">{{ item.name }}</a></template>

      <a v-else href="/settings">Space settings</a>

    </nav>

    <nav v-else-if="home && workspaceAvailable !== false" class="index-links" aria-label="Tangents">

      <p class="sidebar-heading">Tangents</p>

      <a v-for="item in directories.tangents" :key="item.key" class="sidebar-tangent" :style="{'--community-accent':item.accent}" :href="'/t/' + encodeURIComponent(item.key) + '/topics'"><CommunityImage :artwork="item.artwork" :name="item.name" /><span class="sidebar-tangent-copy"><strong>{{ item.name }}</strong><small>{{item.kind === 'articles' ? 'Articles' : 'Discussions'}}<template v-if="item.readAudience !== 'Public'"> · Admitted</template></small></span><span v-if="unread(item)" class="sidebar-new">{{unread(item)}} new</span></a>

      <button v-if="directories.tangentsNextPage" type="button" @click="directories.loadTangents(directories.tangentsNextPage ?? undefined)">More Tangents</button>

      <button v-if="canCreate" type="button" @click="emit('create')">+ New Tangent</button>

      <template v-if="canManageSpace"><p class="sidebar-heading">People</p><a href="/settings?tab=people">People in this Space</a></template>

    </nav>

    <nav v-else-if="!article && !writing && currentTangent" class="index-links" aria-label="Topics in this Tangent">

      <p class="sidebar-heading">{{ tangent?.kind === 'articles' ? 'Articles' : 'Topics' }}</p>

      <a @click="emit('view','all');emit('articleFilter',{month:'',author:''})" :href="'/t/' + encodeURIComponent(currentTangent) + '/topics'">All {{ tangent?.kind === 'articles' ? 'articles' : 'topics' }}</a>

      <template v-if="tangent?.kind === 'articles' && router.route.kind === 'topics'">

        <section v-for="month in articleMonths" :key="month" class="sidebar-article-month"><button class="sidebar-month-filter" type="button" :aria-pressed="articleFilter.month===month" @click="emit('articleFilter',{month:articleFilter.month===month?'':month,author:articleFilter.author})">{{monthName(month)}}</button><div class="index-contents"><a v-for="topic in directories.topics.filter(item => item.summary?.article?.createdAt.startsWith(month))" :key="topic.key" :href="'/t/'+encodeURIComponent(currentTangent)+'/topics/'+encodeURIComponent(topic.key)"><span>{{topic.title}}</span><small v-if="topic.summary?.unread" class="sidebar-new">{{topic.summary.unread}} new {{topic.summary.unread === 1 ? 'reply' : 'replies'}}</small></a></div></section>

        <p class="sidebar-heading">Authors</p><button v-for="author in articleAuthors" :key="author.id" type="button" :aria-pressed="articleFilter.author===author.id" @click="emit('articleFilter',{month:articleFilter.month,author:articleFilter.author===author.id?'':author.id})"><ParticipantAvatar :participant="author"/><span>{{author.displayName || author.handle}}<span v-if="author.classification.toLowerCase() === 'agent'" class="agent-badge">Agent</span></span><span class="sidebar-count">{{directories.topics.filter(item => item.summary?.article?.author.id === author.id).length}}</span></button><p v-if="directories.topicsNextPage || directories.topicsIncomplete" class="hint">Articles on this page</p>

      </template><template v-if="signedIn && router.route.kind === 'topics' && tangent?.kind !== 'articles'"><button v-for="view in [{key:'unread',label:'Unread'},{key:'following',label:'Following'},{key:'started',label:'Started by you'}]" :key="view.key" type="button" :aria-current="topicView===view.key?'page':undefined" @click="emit('view',view.key)">{{view.label}}</button></template><div v-if="router.route.kind !== 'topics'" class="index-contents"><a v-for="topic in directories.topics" :key="topic.key" :href="'/t/' + encodeURIComponent(currentTangent) + '/topics/' + encodeURIComponent(topic.key)" :aria-current="'topic' in router.route && router.route.topic === topic.key ? 'page' : undefined">{{ topic.title }}</a></div>

      <a v-if="signedIn && tangent?.canCreateTopic && router.route.kind !== 'topics'" class="new-writing-action" :href="'/t/'+encodeURIComponent(currentTangent)+'/new'">+ New {{ tangent.kind === 'articles' ? 'article' : 'topic' }}</a>

      <button v-if="directories.topicsNextPage" type="button" @click="directories.loadTopics(currentTangent, directories.topicsNextPage ?? undefined, true)">More topics</button>

    </nav>

    <div v-else-if="router.route.kind === 'notifications'" id="notification-index-slot"></div>

    <nav v-else-if="!article && !writing" class="index-links" aria-label="Your workspace"><p class="sidebar-heading">Your workspace</p><a href="/notifications">Notifications</a><a href="/following" :aria-current="router.route.kind === 'following' ? 'page' : undefined">Following</a><a href="/search" :aria-current="router.route.kind === 'search' ? 'page' : undefined">Search</a><a href="/">All Tangents</a></nav>

    <section v-if="currentTangent && !router.isSettings && !writing" class="sidebar-reading"><h3 class="sidebar-heading">Who can read</h3><p>{{['topic','post'].includes(router.route.kind) ? conversation.selectedTopic.value?.publiclyReadable ? 'Public · anyone on the web' : 'Admitted · participants with access' : tangent?.readAudience === 'Public' ? 'Public doorstep' : tangent?.readAudience === 'Signed in' ? 'Signed in · participants admitted to this Space' : 'Admitted · participants with access'}}</p><p v-if="router.route.kind === 'topics'">Each topic shows its own audience.</p><a v-if="!signedIn" :href="'/sign-in?return='+encodeURIComponent('/t/'+currentTangent+'/topics')">Sign in to participate</a></section>

    <div id="article-index-slot"></div>

    <nav v-if="['topic', 'post'].includes(router.route.kind) && replies.length" class="index-links replies-map" aria-label="Replies map">

      <p class="sidebar-heading">Replies map</p>

      <a v-for="message in replies" :key="message.id" :href="'#' + message.id"><ParticipantAvatar :participant="conversation.resolved.value[message.authorParticipantId]"/><span class="reply-map-person">{{ conversation.resolved.value[message.authorParticipantId]?.displayName || conversation.resolved.value[message.authorParticipantId]?.handle || 'Participant' }}<span v-if="conversation.resolved.value[message.authorParticipantId]?.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span><time :datetime="message.content.createdAt" :title="new Date(message.content.createdAt).toLocaleString()">{{ postTime(message.content.createdAt) }}</time></span></a>

    </nav>

  </aside>

</template>

