<script setup lang="ts">
import { computed } from 'vue'
import type { Conversation } from '../composables/conversation'

const props = defineProps<{ conversation: Conversation }>()
const selectedTopic = props.conversation.selectedTopic
const status = props.conversation.status
const canManage = computed(() => selectedTopic.value?.canManage === true ||
  selectedTopic.value?.permissions?.allowedActions?.includes('manageTopic') === true)
const settingsHref = computed(() => selectedTopic.value
  ? '/t/' + encodeURIComponent(selectedTopic.value.tangentKey) + '/topics/' + encodeURIComponent(selectedTopic.value.key) + '/settings'
  : '#')
const accessNote = computed(() => {
  const notes: Record<string, string> = {
    'sign-in-required': 'Sign in to enter this Topic.',
    'not-admitted': 'The current rules do not admit you.',
    removed: 'Your access to this Topic has been removed.',
    suspended: 'Your participation at this site is suspended.',
  }
  const topic = selectedTopic.value
  return topic ? notes[topic.accessState ?? ''] || (topic.canWrite ? 'You can read and take part.' : topic.canRead ? 'You can read this Topic.' : 'Content is not available under your current access.') : ''
})
const permissionsNote = computed(() => {
  const topic = selectedTopic.value
  if (!topic) return ''
  const tail = topic.isLocked ? 'This Topic is locked.' : topic.allowPostEditing ? 'Authors may edit their Posts.' : 'Posts are write-once; corrections are new replies.'
  return (topic.permissions?.role ? 'Your role: ' + topic.permissions.role + '. ' : '') + tail
})
</script>

<template>
  <details id="topic-details" class="topic-details" :open="!!selectedTopic && !selectedTopic.canRead">
    <summary>Topic details</summary>
    <div class="topic-details-body">
      <p id="topic-access" class="note">{{ accessNote }}</p>
      <p id="topic-permissions" class="hint">{{ permissionsNote }}</p>
      <p v-if="selectedTopic?.topic" class="hint">{{ selectedTopic.topic }}</p>
      <p v-if="canManage"><a class="btn btn-quiet" :href="settingsHref">Topic settings</a></p>
      <p id="history-note" class="hint" aria-live="polite">{{ status }}</p>
    </div>
  </details>
</template>
