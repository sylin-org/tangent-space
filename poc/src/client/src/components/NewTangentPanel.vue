<script setup lang="ts">
import { computed, ref, reactive, watch, onMounted } from 'vue'
import CreationCoverPicker from './CreationCoverPicker.vue'
import TangentAccentPalette from './TangentAccentPalette.vue'
import CommunityImage from './CommunityImage.vue'
const props = defineProps<{ actorKey: string; error: string; busy: boolean }>()
const emit = defineEmits<{ submit: [event: Event]; cancel: []; ready: [form: HTMLFormElement] }>()
const formElement = ref<HTMLFormElement | null>(null), uploading = ref(false), addressEdited = ref(false), railReady = ref(false)
const audienceOpen = ref(false), addressOpen = ref(false)
const initial = () => ({ name: '', key: '', description: '', motto: '', readAudience: 'Public', kind: 'articles' as 'discussions' | 'articles', accent: '#c8931f', artwork: '' })
const form = reactive(initial())
const draftKey = computed(() => 'tangent:new-tangent:' + encodeURIComponent(props.actorKey))
watch(() => form.name, name => { if (!addressEdited.value) form.key = name.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 64).replace(/-$/, '') })
watch(() => props.actorKey, () => {
  Object.assign(form, initial()); addressEdited.value = false
  try {
    const saved = JSON.parse(sessionStorage.getItem(draftKey.value) || 'null')
    if (saved && typeof saved === 'object') {
      for (const field of ['name', 'key', 'description', 'motto', 'artwork', 'accent'] as const) if (typeof saved[field] === 'string') form[field] = saved[field]
      form.kind = saved.kind === 'discussions' ? 'discussions' : 'articles'
      form.readAudience = saved.readAudience === 'Admitted' ? 'Admitted' : 'Public'
      addressEdited.value = saved.addressEdited === true
    }
  } catch { /* A new draft remains available. */ }
}, { immediate: true })
watch([form, addressEdited], () => { if (!props.actorKey) return; try { sessionStorage.setItem(draftKey.value, JSON.stringify({ ...form, addressEdited: addressEdited.value })) } catch { /* Editing remains available without storage. */ } }, { deep: true })
onMounted(() => { railReady.value = !!document.getElementById('creation-index-slot'); if (formElement.value) { emit('ready', formElement.value); formElement.value.querySelector<HTMLInputElement>('input[name=name]')?.focus({ preventScroll: true }) } })
</script>
<template>
  <div class="new-tangent-workspace creation-tangent" :style="{ '--creation-accent': form.accent }">
    <Teleport v-if="railReady" to="#creation-index-slot"><aside class="creation-tangent-preview" :style="{ '--creation-accent': form.accent }"><h2 class="sidebar-heading">Preview</h2>
      <p>In the Space</p><div class="creation-preview-row"><CommunityImage :name="form.name || 'Your Tangent'" :artwork="form.artwork" /><span><strong>{{ form.name || 'Your Tangent' }}</strong><small>{{ form.kind === 'articles' ? 'Articles' : 'Discussions' }}</small></span></div>
      <p>In the path</p><div class="creation-preview-path"><span class="preview-elbow" aria-hidden="true"></span><CommunityImage :name="form.name || 'Your Tangent'" :artwork="form.artwork" /><span>{{ form.name || 'Your Tangent' }}</span><span aria-hidden="true">⌄</span></div>
      <p>On the home page</p><div class="creation-preview-row creation-preview-home"><CommunityImage :name="form.name || 'Your Tangent'" :artwork="form.artwork" /><span><strong>{{ form.name || 'Your Tangent' }}</strong><small>{{ form.motto || 'A place for the next thought.' }}</small></span></div>
    </aside></Teleport>
    <form id="create-tangent" ref="formElement" class="tangent-form" @submit.prevent="emit('submit', $event)">
      <header class="new-tangent-heading"><h1 id="create-tangent-title">New Tangent</h1><p>A Tangent holds one kind of topic: articles or discussions.</p></header>
      <div class="creation-name-row"><label>Name<input v-model="form.name" class="input" name="name" required maxlength="120" placeholder="Field Notes" :disabled="busy"></label><label>Motto <small>Optional</small><input v-model="form.motto" class="input creation-motto" name="motto" maxlength="240" placeholder="Small things, noticed at work." :disabled="busy"></label></div>
      <div class="creation-address"><span>/t/{{ form.key || 'your-tangent' }}</span><button class="text-action" type="button" @click="addressOpen = !addressOpen">{{ addressOpen ? 'Done' : 'Edit address' }}</button><label v-if="addressOpen">Address<input v-model="form.key" class="input" required pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="64" :disabled="busy" @input="addressEdited = !!form.key"><small>Links keep this address when you rename the Tangent.</small></label><input type="hidden" name="key" :value="form.key"></div>
      <fieldset class="creation-kind"><legend>Kind</legend><div class="creation-kind-choices"><label :class="{ selected: form.kind === 'articles' }"><input v-model="form.kind" type="radio" name="kind" value="articles" :disabled="busy"><span class="kind-glyph kind-article" aria-hidden="true"><i></i><b></b><b></b><b></b></span><span><strong>Articles</strong><small>Long-form writing with images. Each article keeps its own replies.</small></span></label><label :class="{ selected: form.kind === 'discussions' }"><input v-model="form.kind" type="radio" name="kind" value="discussions" :disabled="busy"><span class="kind-glyph kind-discussion" aria-hidden="true"><b></b><b></b><b></b></span><span><strong>Discussions</strong><small>Topics and replies. Quick to start, easy to follow.</small></span></label></div></fieldset>
      <CreationCoverPicker v-model="form.artwork" :name="form.name" :disabled="busy" @uploading="uploading = $event" /><input type="hidden" name="artwork" :value="form.artwork">
      <div class="creation-accent"><TangentAccentPalette v-model="form.accent" /><p class="hint">Marks the tile edge, the header and topic labels on this Tangent’s pages.</p></div><input type="hidden" name="accent" :value="form.accent">
      <fieldset class="creation-audience"><legend>Who can read</legend><div class="creation-audience-summary"><span aria-hidden="true">{{ form.readAudience === 'Public' ? '◎' : '♙' }}</span><span><strong>{{ form.readAudience }}</strong><small>{{ form.readAudience === 'Public' ? 'Anyone on the web can read it.' : 'Only people admitted to this Tangent can read it.' }}</small></span><button type="button" class="btn btn-quiet" @click="audienceOpen = !audienceOpen">{{ audienceOpen ? 'Done' : 'Set here' }}</button></div><div v-if="audienceOpen" class="creation-audience-options"><label><input v-model="form.readAudience" type="radio" value="Public" :disabled="busy">Public <small>Anyone on the web</small></label><label><input v-model="form.readAudience" type="radio" value="Admitted" :disabled="busy">Admitted <small>People admitted to this Tangent</small></label></div><input type="hidden" name="readAudience" :value="form.readAudience"></fieldset>
      <details class="creation-description"><summary>Add a description <span>Optional</span></summary><label class="visually-hidden" for="tangent-description">Description</label><textarea id="tangent-description" v-model="form.description" class="input" name="description" maxlength="500" rows="3" placeholder="What belongs here?" :disabled="busy"></textarea></details>
      <div class="creation-actions"><span class="hint">Draft kept in this browser tab</span><button class="btn btn-quiet" type="button" :disabled="busy" @click="emit('cancel')">Cancel</button><button class="btn btn-primary" :disabled="busy || uploading || !form.name.trim() || !form.key">{{ busy ? 'Creating…' : uploading ? 'Uploading…' : 'Create Tangent' }}</button></div><p v-if="error" class="error" role="alert">{{ error }}</p>
    </form>
  </div>
</template>
