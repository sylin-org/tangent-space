<script setup lang="ts">
import { httpRequest } from '../api'

// The cover picker, ported from artwork.js: an upload with bounded size and type,
// a URL fallback, and the curated library filtered by mood. The picker blocks the
// form's save handlers while an image is in flight.
import { computed, ref, watch } from 'vue'
import { artworkPresets } from '../composables/artwork-presets'

const props = defineProps<{
  scope: 'server' | 'tangent'
  tangentKey?: string | null
  modelValue: string
}>()

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void; (e: 'uploading', value: boolean): void }>()

const moods = ['All moods', ...new Set(artworkPresets.map(entry => entry.group))]
const mood = ref('All moods')
const feedback = ref('')
const uploading = ref(false)
const picker = ref<HTMLInputElement | null>(null)

const url = computed(() => props.modelValue)

function choose(value: string, message: string): void {
  emit('update:modelValue', value)
  feedback.value = message
}

const visiblePresets = computed(() =>
  artworkPresets.filter(entry => mood.value === 'All moods' || entry.group === mood.value),
)

const selectionLabel = computed(() => {
  const selected = artworkPresets.find(entry => url.value === '/tangent-art/' + entry.slug + '.png')
  return 'Selected: ' + (selected?.name || (url.value ? 'Custom image' : 'No artwork'))
})

watch(uploading, value => emit('uploading', value))

async function onPick(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 5 * 1024 * 1024 || !file.size) {
    feedback.value = 'Choose a PNG, JPEG, or WebP image no larger than 5 MB.'
    input.value = ''
    return
  }
  const before = url.value
  uploading.value = true
  feedback.value = 'Uploading your image…'
  try {
    const content = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader()
      reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '')
      reader.onerror = () => reject(new Error('This file could not be read. Try choosing it again.'))
      reader.readAsDataURL(file)
    })
    const response = await httpRequest('/api/artwork', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scope: props.scope, tangentKey: props.tangentKey || null, content }),
    })
    const result = await response.json().catch(() => ({})) as { url?: string; reason?: string; title?: string }
    if (!response.ok || typeof result.url !== 'string') throw new Error(result.reason || result.title || 'The upload did not finish. Please try again.')
    if (props.tangentKey !== undefined && url.value !== before) {
      feedback.value = 'The card changed during upload. Choose the image again to use it here.'
      return
    }
    choose(result.url, 'Image ready. Save your changes to use it on the card.')
  } catch (error) {
    feedback.value = (error as Error).message
  } finally {
    uploading.value = false
    if (picker.value) picker.value.value = ''
  }
}
</script>

<template>
  <div class="artwork-upload">
    <p class="artwork-legend">Choose a cover, or bring your own.</p>
    <p class="artwork-current">{{ selectionLabel }}</p>
    <label>Card artwork<input ref="picker" type="file" accept="image/png,image/jpeg,image/webp" class="input" @change="onPick"></label>
    <p class="hint">PNG, JPEG, or WebP · up to 5 MB. Card images are public.</p>
    <p class="hint" role="status">{{ feedback }}</p>
    <details class="artwork-library">
      <summary>Browse {{ artworkPresets.length }} covers</summary>
      <label class="artwork-filter">Mood
        <select v-model="mood" class="input">
          <option v-for="group in moods" :key="group" :value="group">{{ group }}</option>
        </select>
      </label>
      <p class="hint" role="status">{{ visiblePresets.length }} covers · Select one to preview it.</p>
      <div class="artwork-choices" aria-label="Curated artwork">
        <button
          v-for="entry in visiblePresets"
          :key="entry.slug"
          type="button"
          class="artwork-choice"
          :data-mood="entry.group"
          :aria-pressed="url === '/tangent-art/' + entry.slug + '.png'"
          @click="choose('/tangent-art/' + entry.slug + '.png', 'Preview updated. Save your changes to keep it.')"
        >
          <img :src="'/tangent-art/' + entry.slug + '.png'" alt="" loading="lazy">
          <span>{{ entry.name }}</span>
        </button>
      </div>
    </details>
    <button type="button" class="btn btn-quiet artwork-clear" :aria-pressed="url === ''" @click="choose('', 'Preview updated.')">No artwork</button>
  </div>
</template>
