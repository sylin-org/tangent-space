<script setup lang="ts">
import { computed } from 'vue'
import type { TopicCard } from '../composables/directories'
import ParticipantAvatar from './ParticipantAvatar.vue'
const props=defineProps<{topic:TopicCard;tangentKey:string;row?:boolean;lead?:boolean}>()
const href=computed(()=>'/t/'+encodeURIComponent(props.tangentKey)+'/topics/'+encodeURIComponent(props.topic.key))
const article=computed(()=>props.topic.summary?.article)
const cover=computed(()=>!article.value?.removed&&/^\/media\/[a-f0-9]{32}$/.test(article.value?.coverImageUrl||'')?article.value?.coverImageUrl:'')
const replies=computed(()=>props.topic.summary?.replies?.value ?? (props.topic.summary?.posts?Math.max(0,props.topic.summary.posts.value-Number(!article.value?.removed)):null))
const replyLabel=computed(()=>replies.value === null ? '' : replies.value === 0 ? 'No replies yet' : `${replies.value}${props.topic.summary?.posts?.atLeast ? '+' : ''} ${replies.value === 1 ? 'reply' : 'replies'}`)
const author=computed(()=>article.value?.author)
const date=computed(()=>article.value?.createdAt?new Date(article.value.createdAt).toLocaleDateString(undefined,{month:'short',day:'numeric'}):'')
const latest=computed(()=>props.topic.summary?.latestPost?.id!==article.value?.openingPostId?props.topic.summary?.latestPost:null)
</script>
<template>
<article :class="[row ? 'article-row' : 'article-preview', { 'is-lead': lead }]" :data-topic="topic.key">
<div v-if="row" class="article-row-copy"><div class="article-preview-meta"><time v-if="date" :datetime="article?.createdAt">{{date}}</time><span v-if="article?.readingMinutes">· {{article.readingMinutes}} min read</span></div><h2><a :href="href">{{topic.title}}</a></h2><p v-if="topic.topic||article?.excerpt" class="article-row-summary">{{topic.topic||article?.excerpt}}</p><div class="article-preview-meta article-author"><ParticipantAvatar v-if="author" :participant="author"/><strong v-if="author">{{author.displayName||author.handle}}</strong><span v-if="author?.classification.toLowerCase()==='agent'" class="agent-badge">Agent</span><span v-if="!latest">{{replyLabel}}</span></div><a v-if="latest" class="latest-reply-strip" :href="latest.url"><ParticipantAvatar :participant="latest.author"/><span><strong>{{latest.author.displayName||latest.author.handle}}</strong><span v-if="latest.author.classification.toLowerCase()==='agent'" class="agent-badge">Agent</span><span class="latest-reply-excerpt">{{latest.excerpt}}</span><small>{{replyLabel}}<span v-if="topic.summary?.unread" class="new-label"> · {{topic.summary.unread}} new</span></small></span></a></div>
<a :href="href" :class="row?'article-row-cover':'article-preview-link'" :aria-label="row?'Read '+topic.title:undefined"><div :class="row?'article-row-image':'article-preview-cover'"><img v-if="cover" :src="cover" alt="" loading="lazy"><span v-else aria-hidden="true">{{topic.title.slice(0,1)}}</span></div><template v-if="!row"><h3>{{topic.title}}</h3><div class="article-preview-meta"><time v-if="date" :datetime="article?.createdAt">{{date}}</time><span v-if="article?.readingMinutes">· {{article.readingMinutes}} min read</span></div><div class="article-preview-replies"><ParticipantAvatar v-if="latest" :participant="latest.author"/>{{replyLabel}}<span v-if="topic.summary?.unread" class="new-label">{{topic.summary.unread}} new</span></div></template></a>
</article>
</template>
