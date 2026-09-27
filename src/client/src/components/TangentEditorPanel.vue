<script setup lang="ts">
// The Manage Tangent forms, edit the Tangent's card with a
// live preview, and set a participant's role across the Tangent.
import { computed, reactive, ref, watch } from 'vue'
import type { Directories } from '../composables/directories'
import ArtworkPicker from './ArtworkPicker.vue'
import type { DirectoryTangent } from './TangentDirectory.vue'

const props = defineProps<{
  directories: Directories
  tangent: DirectoryTangent | null
}>()

const emit = defineEmits<{ (e: 'saved', key: string): void }>()

const ownerViewer = computed(() => props.tangent?.isOwner === true)

const canManage = computed(() => !!props.tangent && (props.tangent.canManage === true || props.tangent.isOwner === true))

const editorVisible = computed(() => canManage.value)
const membersVisible = computed(() => canManage.value)
const heading = computed(() => props.tangent?.name || props.tangent?.key || 'Shape the place')

const card = reactive({ name: '', description: '', motto: '', accent: '#d88957', artwork: '' })
const memberDid = ref('')
const memberRole = ref('Member')
const cardStatus = ref('')
const memberStatus = ref('')
const cardError = ref('')
const memberError = ref('')

watch(() => props.tangent?.key, () => {
  const t = props.tangent
  card.name = t?.name || ''
  card.description = t?.description || ''
  card.motto = t?.motto || ''
  card.accent = t?.accent || '#d88957'
  card.artwork = t?.artwork || ''
  memberDid.value = ''
  memberRole.value = 'Member'
  cardStatus.value = ''
  memberStatus.value = ''
  cardError.value = ''
  memberError.value = ''
}, { immediate: true })

function safeAccent(value?: string): string {
  return /^#[0-9a-f]{6}$/i.test(value || '') ? value! : '#d88957'
}

function validArtwork(value?: string): boolean {
  return !!value && (/^https:\/\//i.test(value) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(value) || /^\/artwork\/[a-f0-9]{64}\.(png|jpg|webp)$/.test(value))
}

const preview = computed(() => ({
  name: card.name || 'Untitled Tangent',
  description: card.description || 'A shared place for conversation.',
  motto: card.motto,
  accent: safeAccent(card.accent),
  hasArtwork: validArtwork(card.artwork),
}))

async function submitCard() {
  cardError.value = ''
  card.name = card.name.trim()
  card.description = card.description.trim()
  card.motto = card.motto.trim()
  card.artwork = card.artwork.trim()
  if (!props.tangent) {
    cardError.value = 'Choose a Tangent before editing its card.'
    return
  }
  if (!card.name || !validArtwork(card.artwork)) {
    cardError.value = 'Give this Tangent a name. Artwork must use HTTPS or a supplied sample.'
    return
  }
  try {
    await props.directories.updateTangent(props.tangent.key, { ...card })
    cardStatus.value = 'Tangent card saved.'
    emit('saved', props.tangent.key)
  } catch (error) {
    cardError.value = (error as Error).message
  }
}

async function submitMember() {
  memberError.value = ''
  const did = memberDid.value.trim()
  if (!props.tangent || !did) {
    memberError.value = 'Choose a Tangent and enter an account handle or DID.'
    return
  }
  try {
    await props.directories.setMemberRole(props.tangent.key, did, memberRole.value)
    memberDid.value = ''
    memberStatus.value = 'Tangent role saved.'
  } catch (error) {
    memberError.value = (error as Error).message
  }
}
</script>

<template>
  <details id="tangent-editor" class="setup tangent-setup" :hidden="!editorVisible">
    <summary>Edit this Tangent’s card</summary>
    <div class="setup-intro"><p class="eyebrow">Its recognizable face</p><h2 id="edit-tangent-heading">{{ heading }}</h2><p>Changes appear on the card as you write.</p></div>
    <form id="edit-tangent-form" class="stack tangent-form" @submit.prevent="submitCard">
      <label>Community name<input v-model="card.name" class="input" name="name" required maxlength="120"></label>
      <label>What is this place for?<textarea v-model="card.description" class="input" name="description" maxlength="500" rows="2"></textarea></label>
      <label>House rule or motto<input v-model="card.motto" class="input" name="motto" maxlength="240"></label>
      <label>Accent colour<input v-model="card.accent" class="input color-input" name="accent" type="color"></label>
      <label>Artwork URL <span class="field-note">Optional. HTTPS links or a supplied sample.</span><input v-model="card.artwork" class="input" name="artwork" type="text" inputmode="url" maxlength="2048"></label>
      <ArtworkPicker scope="tangent" :tangent-key="tangent?.key ?? null" v-model="card.artwork" />
      <div class="tangent-card edit-card-preview sylin-card" :class="{ 'has-artwork': preview.hasArtwork }" :style="{ '--tangent-accent': preview.accent }" aria-label="Card preview">
        <span class="tcg-art">
          <img v-if="preview.hasArtwork" :src="card.artwork" alt="" loading="lazy">
          <span v-else class="tcg-emblem">✦</span>
          <span class="tcg-edition">TANGENT</span>
        </span>
        <span class="tcg-divider"></span>
        <span class="tcg-disc activity-disc" title="All caught up">✦</span>
        <span class="tcg-notch" aria-hidden="true"></span>
        <span class="tcg-pane">
          <strong class="tcg-name">{{ preview.name }}</strong>
          <span class="tcg-description">{{ preview.description }}</span>
          <em v-if="preview.motto" class="tcg-motto">“{{ preview.motto }}”</em>
          <span class="tcg-foot tangent-card-footer">No unread messages</span>
        </span>
        <span class="tcg-holo" aria-hidden="true"></span>
        <span class="tcg-holo2" aria-hidden="true"></span>
      </div>
      <button class="btn btn-primary">Save card</button>
      <p class="hint" role="status" :class="{ error: !!cardError }">{{ cardError || cardStatus }}</p>
    </form>
  </details>
  <details id="tangent-members" class="setup tangent-members" :hidden="!membersVisible">
    <summary>People in this Tangent</summary>
    <p class="hint">Set a participant’s role across this Tangent. Topic-specific access remains separate.</p>
    <form id="tangent-member-form" class="stack" @submit.prevent="submitMember">
      <label>Account handle or DID<input v-model="memberDid" class="input" name="did" required maxlength="2048" autocomplete="off" spellcheck="false" placeholder="@your-friend.bsky.social"></label>
      <label>Tangent role
        <select v-model="memberRole" class="input" name="role">
          <option value="Member">Member</option>
          <option value="Reader">Read only</option>
          <option id="tangent-admin-choice" value="Admin" :hidden="!ownerViewer" :disabled="!ownerViewer">Administrator</option>
        </select>
      </label>
      <button class="btn btn-quiet">Save role</button>
      <p class="hint" role="status" :class="{ error: !!memberError }">{{ memberError || memberStatus }}</p>
    </form>
  </details>
</template>
