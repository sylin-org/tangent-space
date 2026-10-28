<script setup lang="ts">
import type { TangentCard } from '../composables/directories'
import { directoryCount } from '../composables/directories'
import CommunityImage from './CommunityImage.vue'
import ArticlePreview from './ArticlePreview.vue'
import TopicRow from './TopicRow.vue'
import FollowButton from './FollowButton.vue'
defineProps<{tangent:TangentCard;signedIn:boolean}>()
</script>
<template>
<section class="tangent-shelf" :aria-labelledby="'shelf-'+tangent.key" :style="{'--community-accent':tangent.accent}"><header class="shelf-heading"><CommunityImage :name="tangent.name" :artwork="tangent.artwork"/><div><h2 :id="'shelf-'+tangent.key"><a :href="'/t/'+encodeURIComponent(tangent.key)+'/topics'">{{tangent.name}}</a></h2><p v-if="tangent.motto">{{tangent.motto}}</p><p v-else-if="tangent.description">{{tangent.description}}</p><small v-if="tangent.readAudience && tangent.readAudience !== 'Public'" class="shelf-audience">{{tangent.readAudience === 'Signed in' ? 'Signed in · participants admitted to this Space' : 'Admitted · only participants with access can read'}}</small></div><FollowButton :tangent="tangent.key" :signed-in="signedIn"/><a :href="'/t/'+encodeURIComponent(tangent.key)+'/topics'">All {{tangent.kind==='articles' && tangent.summary?.topics ? directoryCount(tangent.summary.topics) + ' ' : ''}}{{tangent.kind==='articles'?'articles':'topics'}}</a></header><div v-if="tangent.summary?.recentTopics.length" :class="tangent.kind==='articles'?'article-shelf':'topic-rows'"><template v-for="topic in tangent.summary.recentTopics" :key="topic.key"><ArticlePreview v-if="tangent.kind==='articles'" :topic="topic" :tangent-key="tangent.key"/><TopicRow v-else :topic="topic" :tangent-key="tangent.key" :unread="topic.summary?.unread"/></template></div><p v-else class="hint">{{tangent.kind==='articles'?'No articles yet.':'No topics yet.'}}</p><a v-if="signedIn && tangent.canCreateTopic && tangent.kind !== 'articles'" class="shelf-new-topic new-writing-action" :href="'/t/'+encodeURIComponent(tangent.key)+'/new'">+ New topic in {{tangent.name}}</a><p v-if="tangent.summary?.incomplete" class="hint">More conversations are available in this Tangent.</p></section>
</template>
