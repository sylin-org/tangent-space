<script setup lang="ts">
import { computed, ref } from 'vue'
import { httpRequest } from '../api'
import { artworkPresets } from '../composables/artwork-presets'
const props = defineProps<{ modelValue: string; name: string; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string]; uploading: [value: boolean] }>()
const expanded = ref(false), uploading = ref(false), error = ref('')
const field = ref<HTMLInputElement | null>(null)
const featured = ['blueprint-exchange', 'greenhouse', 'moss-and-signal', 'coastal-hours', 'orbital-dawn', 'neural-loom']
const covers = computed(() => expanded.value ? artworkPresets : featured.map(slug => artworkPresets.find(item => item.slug === slug)!))
async function upload(event: Event) {
  const input = event.target as HTMLInputElement, file = input.files?.[0]
  input.value = ''
  if (!file || props.disabled || uploading.value) return
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 5 * 1024 * 1024 || !file.size) { error.value = 'Choose a PNG, JPEG or WebP image no larger than 5 MB.'; return }
  uploading.value = true; emit('uploading', true); error.value = ''
  try {
    const content = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(',')[1] || ''); reader.onerror = () => reject(new Error('This file could not be read.')); reader.readAsDataURL(file) })
    const response = await httpRequest('/api/artwork', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ scope: 'tangent', tangentKey: null, content }) })
    const result = await response.json() as { url?: string; reason?: string; title?: string }
    if (!response.ok || !result.url) throw new Error(result.reason || result.title || 'The cover could not be uploaded.')
    emit('update:modelValue', result.url)
  } catch (cause) { error.value = (cause as Error).message }
  finally { uploading.value = false; emit('uploading', false) }
}
</script>
<template>
  <fieldset class="creation-cover-picker"><legend>Cover <button type="button" class="text-action" @click="expanded = !expanded">{{ expanded ? 'Show featured' : 'See all ' + artworkPresets.length }}</button></legend>
    <div class="creation-cover-grid">
      <button v-for="cover in covers" :key="cover.slug" type="button" :title="cover.name" :aria-label="cover.name" :aria-pressed="modelValue === '/tangent-art/' + cover.slug + '.png'" :disabled="disabled || uploading" @click="emit('update:modelValue', '/tangent-art/' + cover.slug + '.png')"><img :src="'/tangent-art/' + cover.slug + '.png'" alt=""><span class="cover-choice-check" v-if="modelValue === '/tangent-art/' + cover.slug + '.png'">✓</span></button>
      <button type="button" class="creation-letter-cover" title="Letter tile" aria-label="Letter tile" :aria-pressed="!modelValue" :disabled="disabled || uploading" @click="emit('update:modelValue', '')">{{ name.trim()[0]?.toUpperCase() || 'T' }}</button>
      <button type="button" class="creation-upload-cover" :disabled="disabled || uploading" @click="field?.click()"><span aria-hidden="true">＋</span>{{ uploading ? 'Uploading…' : 'Upload' }}</button>
    </div>
    <input ref="field" class="visually-hidden" type="file" accept="image/png,image/jpeg,image/webp" aria-label="Upload a Tangent cover" @change="upload">
    <p v-if="error" class="error" role="alert">{{ error }}</p>
  </fieldset>
</template>
