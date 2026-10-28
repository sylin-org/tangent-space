<script setup lang="ts">
import { computed } from 'vue'
import type { TopicCard } from '../composables/directories'
import { directoryCount,directoryRelativeDate } from '../composables/directories'
import ParticipantAvatar from './ParticipantAvatar.vue'
const props = defineProps<{topic:TopicCard;tangentKey:string;unread?:number}>()
const replies = computed(() => props.topic.summary?.replies ?? (props.topic.summary?.posts ? { value: Math.max(0, props.topic.summary.posts.value - 1), atLeast: props.topic.summary.posts.atLeast } : null))
const latest = computed(() => props.topic.summary?.latestPost)
const firstAuthor = computed(() => props.topic.summary?.openingPost?.author || props.topic.summary?.starter)
const participants = computed(() => [...new Map([firstAuthor.value, latest.value?.author].filter((person):person is NonNullable<typeof person> => !!person).map(person => [person.id, person])).values()])
const firstPost = computed(() => props.topic.summary?.openingPost?.excerpt || props.topic.topic || (props.topic.summary?.posts?.value === 1 ? latest.value?.excerpt : ''))
const latestTime = computed(() => {
  const value = latest.value?.createdAt || props.topic.summary?.createdAt
  if (!value) return ''
  const date = new Date(value)
  return date.toDateString() === new Date().toDateString() ? date.toLocaleTimeString(undefined, {hour:'numeric',minute:'2-digit'}) : date.toLocaleDateString(undefined,{month:'short',day:'numeric'})
})
</script>
<template>
<article class="topic-row">
  <div class="topic-row-main"><span v-if="unread" class="topic-unread-dot" role="img" aria-label="Unread"></span><h3><a :href="'/t/'+encodeURIComponent(tangentKey)+'/topics/'+encodeURIComponent(topic.key)">{{topic.title}}</a></h3><p class="topic-row-meta"><strong>{{firstAuthor?.displayName||firstAuthor?.handle||'Participant'}}</strong><span v-if="firstAuthor?.classification?.toLowerCase()==='agent'" class="agent-badge">Agent</span><template v-if="firstPost">: {{firstPost}}</template><span v-else-if="topic.summary?.createdAt"> · {{directoryRelativeDate(topic.summary.createdAt)}}</span></p><a v-if="latest && replies?.value" class="topic-row-latest" :href="latest.url"><ParticipantAvatar :participant="latest.author"/><span><strong>{{latest.author.displayName||latest.author.handle}}</strong><span v-if="latest.author.classification?.toLowerCase()==='agent'" class="agent-badge">Agent</span> {{latest.excerpt}}</span><span v-if="unread" class="new-label">New</span></a></div>
  <div class="topic-row-people" :aria-label="directoryCount(topic.summary?.participants)+' participants'"><ParticipantAvatar v-for="person in participants" :key="person.id" :participant="person"/><span v-if="topic.summary?.participants && topic.summary.participants.value > participants.length" class="topic-more-people">+{{topic.summary.participants.value - participants.length}}</span></div>
  <div class="topic-row-counts"><span>{{directoryCount(replies)}}<span class="topic-count-word">{{ ' ' + (replies?.value===1?'reply':'replies') }}</span></span><small v-if="unread" class="new-label">{{unread}} new</small></div>
  <a v-if="latest" class="topic-row-activity" :href="latest.url"><time :datetime="latest.createdAt" :title="new Date(latest.createdAt).toLocaleString()">{{latestTime}}</time><span>{{latest.author.displayName || latest.author.handle}}<span v-if="latest.author.classification.toLowerCase()==='agent'" class="agent-badge">Agent</span></span></a>
</article>
</template>
