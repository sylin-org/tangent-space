<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { EditorContent, useEditor } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import { Markdown } from '@tiptap/markdown'
import { httpRequest } from '../api'
import { canEditMarkdownRichly } from '../composables/markdown-roundtrip'

const props = defineProps<{
  modelValue: string
  tangentKey: string
  topicKey?: string
  identity: string
  label: string
  disabled?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string]; busy: [value: boolean] }>()

const view = ref<'rich' | 'source'>('source')
const notice = ref('')
const linkOpen = ref(false)
const imageOpen = ref(false)
const linkUrl = ref('')
const linkLabel = ref('')
const imageUrl = ref('')
const imageAlt = ref('')
const busy = ref(false)
const fileField = ref<HTMLInputElement | null>(null)
let lastEmitted = ''
let uploadSerial = 0
let uploadController: AbortController | null = null

const editor = useEditor({
  content: props.modelValue,
  contentType: 'markdown',
  extensions: [
    StarterKit.configure({ link: { openOnClick: false, autolink: false } }),
    Image.configure({ inline: true, allowBase64: false, HTMLAttributes: { referrerpolicy: 'no-referrer' } }),
    Markdown,
  ],
  editorProps: { attributes: { class: 'markdown-editor-surface', role: 'textbox', 'aria-multiline': 'true', 'aria-label': props.label } },
  onUpdate: ({ editor: current }) => {
    if (view.value !== 'rich') return
    const value = current.getMarkdown()
    lastEmitted = value
    emit('update:modelValue', value)
  },
})

watch(() => props.modelValue, value => {
  if (!editor.value || value === lastEmitted) return
  editor.value.commands.setContent(value, { contentType: 'markdown', emitUpdate: false })
})
watch(() => props.disabled, value => editor.value?.setEditable(!value), { immediate: true })
watch(editor, current => { if (current) showRich() }, { immediate: true })
function stopUpload() {
  uploadSerial++
  uploadController?.abort()
  uploadController = null
  busy.value = false
  emit('busy', false)
}
watch(() => props.identity, stopUpload)
onBeforeUnmount(stopUpload)

const rich = computed(() => view.value === 'rich')
function showRich() {
  if (!editor.value) return
  editor.value.commands.setContent(props.modelValue, { contentType: 'markdown', emitUpdate: false })
  if (!canEditMarkdownRichly(props.modelValue, editor.value.getMarkdown())) {
    notice.value = 'This article uses formatting the visual editor cannot preserve. Edit its Markdown source to keep every part intact.'
    view.value = 'source'
    return
  }
  notice.value = ''
  view.value = 'rich'
}
function sourceInput(event: Event) {
  notice.value = ''
  emit('update:modelValue', (event.target as HTMLTextAreaElement).value)
}
function validUrl(value: string, image = false): string | null {
  const candidate = value.trim()
  if (!candidate) return null
  if (image && candidate.startsWith('/media/')) return candidate
  try {
    const parsed = new URL(candidate, window.location.origin)
    if (!['http:', 'https:'].includes(parsed.protocol)) return null
    return candidate
  } catch { return null }
}
function addLink() {
  const href = validUrl(linkUrl.value)
  if (!href || !editor.value) { notice.value = 'Enter a valid http or https link.'; return }
  const label = linkLabel.value.trim()
  const chain = editor.value.chain().focus()
  if (editor.value.state.selection.empty) chain.insertContent({ type: 'text', text: label || href, marks: [{ type: 'link', attrs: { href } }] }).run()
  else chain.setLink({ href }).run()
  linkOpen.value = false
  linkUrl.value = linkLabel.value = ''
  notice.value = ''
}
function imageFields(){imageOpen.value=!imageOpen.value;linkOpen.value=false;if(editor.value?.isActive('image')){const attributes=editor.value.getAttributes('image');imageUrl.value=String(attributes.src||'');imageAlt.value=String(attributes.alt||'')}}
function removeImage(){if(editor.value?.isActive('image'))editor.value.chain().focus().deleteSelection().run();imageOpen.value=false}
function addImage(src: string, alt = '') {
  if (!editor.value) return
  editor.value.chain().focus().setImage({ src, alt }).run()
  imageOpen.value = false
  imageUrl.value = imageAlt.value = ''
  notice.value = ''
}
function addImageUrl() {
  const src = validUrl(imageUrl.value, true)
  if (!src) { notice.value = 'Enter a valid http or https image URL.'; return }
  addImage(src, imageAlt.value.trim())
}
function fileBase64(file: File): Promise<string> {
  return file.arrayBuffer().then(buffer => {
    const bytes = new Uint8Array(buffer)
    let binary = ''
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
    return btoa(binary)
  })
}
async function uploadImage(event: Event) {
  const field = event.target as HTMLInputElement
  const file = field.files?.[0]
  field.value = ''
  if (file) await uploadFile(file)
}
async function uploadFile(file: File) {
  if (!file || busy.value) return
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) { notice.value = 'Choose a PNG, JPEG or WebP image.'; return }
  if (file.size > 20 * 1024 * 1024) { notice.value = 'Choose an image smaller than 20 MiB.'; return }
  const serial = ++uploadSerial
  const identity = props.identity
  const tangentKey = props.tangentKey
  const topicKey = props.topicKey
  const controller = new AbortController()
  uploadController = controller
  busy.value = true
  emit('busy', true)
  notice.value = 'Uploading image…'
  try {
    const content = await fileBase64(file)
    if (serial !== uploadSerial || identity !== props.identity) return
    const response = await httpRequest('/api/media', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ tangentKey, ...(topicKey ? { topicKey } : {}), fileName: file.name, content }),
      signal: controller.signal,
    })
    if (serial !== uploadSerial || identity !== props.identity || tangentKey !== props.tangentKey || topicKey !== props.topicKey) return
    const result = await response.json().catch(() => ({})) as { url?: string; message?: string; reason?: string }
    if (serial !== uploadSerial || identity !== props.identity || tangentKey !== props.tangentKey || topicKey !== props.topicKey) return
    if (!response.ok || !result.url) throw new Error(result.message || result.reason || 'The image could not be uploaded.')
    addImage(result.url, file.name.replace(/\.[^.]+$/, ''))
  } catch (error) { if (serial === uploadSerial && !controller.signal.aborted) notice.value = (error as Error).message }
  finally {
    if (serial === uploadSerial) {
      uploadController = null
      busy.value = false
      emit('busy', false)
    }
  }
}
function onPaste(event: ClipboardEvent) {
  if (!rich.value || props.disabled) return
  const file = Array.from(event.clipboardData?.files ?? []).find(candidate => candidate.type.startsWith('image/'))
  if (!file) return
  event.preventDefault()
  void uploadFile(file)
}
function onDrop(event: DragEvent) {
  if (!rich.value || props.disabled) return
  const file = Array.from(event.dataTransfer?.files ?? []).find(candidate => candidate.type.startsWith('image/'))
  if (!file) return
  event.preventDefault()
  void uploadFile(file)
}
</script>

<template>
  <div class="markdown-composer" @paste.capture="onPaste" @drop.capture="onDrop">
    <div class="markdown-composer-topline">
      <span class="markdown-composer-label">{{ label }}</span>
      <div class="markdown-view-switch" role="group" aria-label="Editor view">
        <button type="button" :aria-pressed="rich" :disabled="disabled" @click="showRich">Visual</button>
        <button type="button" :aria-pressed="!rich" :disabled="disabled" @click="view = 'source'; notice = ''">Source</button>
      </div>
    </div>
    <template v-if="rich">
      <div class="markdown-toolbar" role="toolbar" aria-label="Text formatting">
        <button type="button" title="Bold" aria-label="Bold" :aria-pressed="editor?.isActive('bold')" :disabled="disabled" @click="editor?.chain().focus().toggleBold().run()"><strong>B</strong></button>
        <button type="button" title="Italic" aria-label="Italic" :aria-pressed="editor?.isActive('italic')" :disabled="disabled" @click="editor?.chain().focus().toggleItalic().run()"><em>I</em></button>
        <button type="button" title="Heading 2" aria-label="Heading 2" :aria-pressed="editor?.isActive('heading', { level: 2 })" :disabled="disabled" @click="editor?.chain().focus().toggleHeading({ level: 2 }).run()">H2</button>
        <button type="button" title="Heading 3" aria-label="Heading 3" :aria-pressed="editor?.isActive('heading', { level: 3 })" :disabled="disabled" @click="editor?.chain().focus().toggleHeading({ level: 3 }).run()">H3</button>
        <button type="button" title="Bullet list" aria-label="Bullet list" :aria-pressed="editor?.isActive('bulletList')" :disabled="disabled" @click="editor?.chain().focus().toggleBulletList().run()">• List</button>
        <button type="button" title="Numbered list" aria-label="Numbered list" :aria-pressed="editor?.isActive('orderedList')" :disabled="disabled" @click="editor?.chain().focus().toggleOrderedList().run()">1. List</button>
        <button type="button" title="Quote" aria-label="Quote" :aria-pressed="editor?.isActive('blockquote')" :disabled="disabled" @click="editor?.chain().focus().toggleBlockquote().run()">❝</button>
        <button type="button" title="Link" aria-label="Insert link" :disabled="disabled" @click="linkOpen = !linkOpen; imageOpen = false">Link</button>
        <button type="button" title="Image" aria-label="Insert image" :disabled="disabled" @click="imageFields()">Image</button>
        <button type="button" title="Divider" aria-label="Insert divider" :disabled="disabled" @click="editor?.chain().focus().setHorizontalRule().run()">—</button>
        <button type="button" title="Undo" aria-label="Undo" :disabled="disabled || !editor?.can().undo()" @click="editor?.chain().focus().undo().run()">↶</button>
        <button type="button" title="Redo" aria-label="Redo" :disabled="disabled || !editor?.can().redo()" @click="editor?.chain().focus().redo().run()">↷</button>
      </div>
      <div v-if="linkOpen" class="markdown-inline-fields">
        <label>Link URL<input v-model="linkUrl" class="input" type="url" placeholder="https://example.com"></label>
        <label>Text (optional)<input v-model="linkLabel" class="input" type="text" placeholder="Link text"></label>
        <button type="button" class="btn btn-quiet" @click="addLink">Insert link</button>
      </div>
      <div v-if="imageOpen" class="markdown-inline-fields">
        <button type="button" class="btn btn-quiet" :disabled="busy" @click="fileField?.click()">Upload image</button>
        <label>Image URL<input v-model="imageUrl" class="input" type="url" placeholder="https://example.com/image.jpg"></label>
        <label>Description<input v-model="imageAlt" class="input" type="text" placeholder="Describe the image"></label>
        <button type="button" class="btn btn-quiet" @click="addImageUrl">{{editor?.isActive('image') ? 'Update image' : 'Insert URL'}}</button><button v-if="editor?.isActive('image')" type="button" class="btn btn-quiet" @click="removeImage">Remove image</button>
      </div>
      <EditorContent :editor="editor" class="markdown-editor" />
    </template>
    <label v-else class="markdown-source-label">Markdown source
      <textarea class="input markdown-source" :value="modelValue" rows="12" :disabled="disabled" spellcheck="true" @input="sourceInput"></textarea>
    </label>
    <input ref="fileField" type="file" accept="image/png,image/jpeg,image/webp" aria-label="Choose an image to upload" @change="uploadImage">
    <p v-if="notice" class="hint" role="status">{{ notice }}</p>
  </div>
</template>
