<script setup lang="ts">
import { computed, defineAsyncComponent, reactive, ref, watch, nextTick, onMounted } from 'vue'
import { httpRequest, contentApi } from '../api'
import { MAX_POST_BYTES, type PostFormat } from '../composables/conversation'
import { previewMarkdown } from '../composables/draft-preview'
import type { RouterStore } from '../stores/router'
const MarkdownComposer = defineAsyncComponent(() => import('./MarkdownComposer.vue'))
const props = withDefaults(defineProps<{ tangentKey: string | null; tangentName: string; author: string; actorKey: string; actorDid?: string; audience: string; kind: 'discussions' | 'articles'; router: RouterStore; showTrigger?: boolean; dedicated?: boolean }>(), { showTrigger: true })
const emit = defineEmits<{ openState: [value: boolean]; cancel: []; title: [value: string] }>()
const open = ref(false), indexAvailable = ref(false), pathAvailable = ref(false), composerUpload = ref(false)
const richEditing = ref(props.kind === 'articles')
const preview = ref(false), publishPanel = ref(false), discardConfirm = ref(false), savedAt = ref(''), coverDescriptionOpen = ref(false)
const formElement = ref<HTMLFormElement | null>(null)
const inFlight = reactive(new Set<string>()), error = ref('')
const selectedAudience = ref<'Public' | 'Restricted'>('Restricted'), pendingAudience = ref<'Public' | 'Restricted'>('Restricted')
const blueskyConnectUrl = ref('')
const publishToBluesky = ref(false), pendingPublish = ref(false), blueskyReady = ref(false), publicationError = ref(''), createdPost = ref('')
const emptyDraft = () => ({ title: '', subtitle: '', pendingSubtitle: '', text: '', format: (props.kind === 'articles' || props.dedicated ? 'markdown' : 'plain') as PostFormat, requestId: '', pendingTitle: '', pendingText: '', pendingFormat: 'plain' as PostFormat })
const draft = reactive(emptyDraft())
const key = computed(() => props.actorKey && props.tangentKey ? 'tangent:start:' + encodeURIComponent(props.actorKey) + ':' + encodeURIComponent(props.tangentKey) : '')
const busy = computed(() => inFlight.has(key.value)), awaitingConfirmation = computed(() => !!draft.requestId && !busy.value)
const remainingBytes = computed(() => MAX_POST_BYTES - new TextEncoder().encode(draft.text).length)
const wordCount = computed(() => draft.text.trim().split(/\s+/).filter(Boolean).length)
const previewHtml = computed(() => draft.format === 'markdown' ? previewMarkdown(draft.text) : '')
const firstImage = computed(() => /!\[([^\]]*)\]\(([^)]+)\)/.exec(draft.text))
const coverDescription = computed({ get: () => firstImage.value?.[1] || '', set: (value: string) => { const image = firstImage.value; if (image) draft.text = draft.text.slice(0, image.index) + '![' + value.replace(/[\[\]\n]/g, '') + '](' + image[2] + ')' + draft.text.slice(image.index + image[0].length) } })
const checks = computed(() => { const images = [...draft.text.matchAll(/!\[([^\]]*)\]\(([^)]+)\)/g)]; return [{ label: 'Title', ok: !!draft.title.trim() }, { label: 'Cover image', ok: images.length > 0 }, { label: 'Image descriptions', ok: images.every(image => image[1]?.trim()) }, { label: 'Link addresses', ok: !/\]\((?!https?:|\/|#)[^)]+\)/i.test(draft.text) }] })
const draftOutline = computed(() => { let fenced = false; return draft.text.split('\n').flatMap(line => { if (/^\s*(```|~~~)/.test(line)) { fenced = !fenced; return [] } const match = !fenced && line.match(/^#{2,4}\s+(.+)/); return match ? [match[1]!.replace(/\s+#+$/, '')] : [] }) })
watch([() => draft.text, preview], async () => { await nextTick(); document.querySelectorAll('.start-conversation .markdown-editor-surface h2,.start-conversation .markdown-editor-surface h3,.start-conversation .markdown-editor-surface h4,.creation-article-preview .article-prose h2,.creation-article-preview .article-prose h3,.creation-article-preview .article-prose h4').forEach((heading, index) => heading.id = 'draft-heading-' + (index + 1)) }, { flush: 'post' })
function draftSection(index: number) { const heading = document.getElementById('draft-heading-' + (index + 1)); if (heading) heading.scrollIntoView({ block: 'center', behavior: 'smooth' }); else document.querySelector<HTMLTextAreaElement>('.start-conversation .markdown-source')?.focus() }
function removeCover() { const image = firstImage.value; if (image) draft.text = draft.text.slice(0, image.index) + draft.text.slice(image.index + image[0].length); coverDescriptionOpen.value = false }
async function checkBluesky() {
  const identity = key.value
  try {
    const connection = await contentApi.blueskyConnection()
    if (identity !== key.value) return
    blueskyReady.value = connection.canPublish && (!props.actorDid || connection.actorDid === props.actorDid)
    blueskyConnectUrl.value = ''
    if (connection.connectUrl) { const url = new URL(connection.connectUrl, window.location.origin); if (url.origin === window.location.origin && url.pathname === '/api/v1/content/bluesky/connect') { url.searchParams.set('return', window.location.pathname + window.location.search); blueskyConnectUrl.value = url.pathname + url.search } }
  } catch { if (identity === key.value) { blueskyReady.value = false; blueskyConnectUrl.value = '' } }
}
watch(() => draft.title, value => emit('title', value), { immediate: true })
watch(open, async value => { emit('openState', value); await nextTick(); indexAvailable.value = !!document.getElementById(props.dedicated ? 'creation-index-slot' : 'article-index-slot'); if (value) void checkBluesky() })
watch(key, value => {
  composerUpload.value = false; selectedAudience.value = props.audience === 'Public' ? 'Public' : 'Restricted'; pendingAudience.value = selectedAudience.value
  publishToBluesky.value = false; pendingPublish.value = false; createdPost.value = ''; publicationError.value = ''; preview.value = false; publishPanel.value = false; savedAt.value = ''
  Object.assign(draft, emptyDraft()); richEditing.value = props.kind === 'articles'; open.value = props.dedicated === true
  if (!value) return
  try {
    const saved = JSON.parse(sessionStorage.getItem(value) || '{}') as Record<string, unknown>
    if (typeof saved.subtitle === 'string') draft.subtitle = saved.subtitle.slice(0, 240)
    if (typeof saved.pendingSubtitle === 'string') draft.pendingSubtitle = saved.pendingSubtitle.slice(0, 240)
    if (typeof saved.title === 'string') draft.title = saved.title.slice(0, 120)
    if (typeof saved.text === 'string') draft.text = saved.text
    if (saved.audience === 'Public' || saved.audience === 'Restricted') selectedAudience.value = saved.audience
    if (saved.pendingAudience === 'Public' || saved.pendingAudience === 'Restricted') pendingAudience.value = saved.pendingAudience
    publishToBluesky.value = saved.publishToBluesky === true; pendingPublish.value = saved.pendingPublish === true
    if (saved.format === 'markdown' || saved.format === 'plain') draft.format = saved.format
    if (typeof saved.requestId === 'string' && typeof saved.pendingTitle === 'string' && typeof saved.pendingText === 'string') { draft.requestId = saved.requestId; draft.pendingTitle = saved.pendingTitle; draft.pendingText = saved.pendingText; draft.pendingFormat = saved.pendingFormat === 'markdown' ? 'markdown' : 'plain' }
    open.value = props.dedicated === true || !!(draft.title || draft.text)
  } catch { /* Storage is an optional drafting aid. */ }
}, { immediate: true })
watch([draft, selectedAudience, pendingAudience, publishToBluesky, pendingPublish], () => {
  if (!key.value) return
  try {
    if (draft.title || draft.text || draft.requestId) sessionStorage.setItem(key.value, JSON.stringify({ ...draft, audience: selectedAudience.value, pendingAudience: pendingAudience.value, publishToBluesky: publishToBluesky.value, pendingPublish: pendingPublish.value }))
    else sessionStorage.removeItem(key.value)
    savedAt.value = new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  } catch { savedAt.value = '' }
}, { deep: true })
function close() { open.value = false; error.value = ''; if (props.dedicated) emit('cancel') }
function show() { if (!draft.title && !draft.text && !draft.requestId) selectedAudience.value = props.audience === 'Public' ? 'Public' : 'Restricted'; open.value = true; error.value = '' }
function discard() { if (busy.value || awaitingConfirmation.value) return; Object.assign(draft, emptyDraft()); try { sessionStorage.removeItem(key.value) } catch { /* Draft fields are clear. */ } discardConfirm.value = false; close() }
function toggleFormatting() {
  if (props.dedicated && props.kind === 'discussions') { richEditing.value = !richEditing.value; draft.format = 'markdown' }
  else draft.format = draft.format === 'plain' ? 'markdown' : 'plain'
}
function requestPublish() { if (!draft.title.trim() || !draft.text.trim()) { error.value = 'Add a title and an opening thought before publishing.'; formElement.value?.reportValidity(); return } error.value = ''; publishPanel.value = true }
function keyboardPublish(event: KeyboardEvent) { if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) { event.preventDefault(); if (props.kind === 'articles' && !publishPanel.value) requestPublish(); else void submit() } }
defineExpose({ show })
onMounted(async () => { if (props.dedicated) show(); await nextTick(); indexAvailable.value = !!document.getElementById(props.dedicated ? 'creation-index-slot' : 'article-index-slot'); pathAvailable.value = !!document.getElementById('creation-path-actions') })
async function submit() {
  if (busy.value || composerUpload.value || !props.tangentKey || !props.actorKey) return
  const originKey = key.value, tangent = props.tangentKey, actor = props.actorKey
  const title = (draft.requestId ? draft.pendingTitle : draft.title).trim(), text = (draft.requestId ? draft.pendingText : draft.text).trim(), format = draft.requestId ? draft.pendingFormat : draft.format
  if (!title || !text) { error.value = 'Add a title and an opening thought before publishing.'; return }
  if (new TextEncoder().encode(text).length > MAX_POST_BYTES) { error.value = 'The opening thought must fit within 64 KiB of UTF-8 text.'; return }
  if (!draft.requestId) { pendingAudience.value = selectedAudience.value; pendingPublish.value = publishToBluesky.value; draft.requestId = 'thread-' + crypto.randomUUID(); draft.pendingTitle = title; draft.pendingSubtitle = draft.subtitle.trim(); draft.pendingText = text; draft.pendingFormat = format }
  const requestId = draft.requestId
  inFlight.add(originKey); error.value = ''
  try {
    const response = await httpRequest('/api/v1/experience/tangents/' + encodeURIComponent(tangent) + '/topics', { method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ requestId, title, text, format, audience: pendingAudience.value, subtitle: draft.pendingSubtitle }) })
    const result = await response.json().catch(() => ({})) as { topicRef?: string; postRef?: string; message?: string; reason?: string }
    if (!response.ok) { const failure = new Error(result.message || result.reason || 'The conversation could not be started. Your draft is still here.') as Error & { status?: number }; failure.status = response.status; throw failure }
    const topicKey = result.topicRef?.split('::').at(-1)
    if (!topicKey || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(topicKey)) throw new Error('Creation could not be confirmed. Your draft is still here; retry to reopen the same conversation.')
    if (originKey !== key.value || actor !== props.actorKey || tangent !== props.tangentKey) return
    createdPost.value = result.postRef?.split('::').at(-1) || ''
    if (pendingPublish.value && pendingAudience.value === 'Public' && createdPost.value) { try { await contentApi.publishPost(createdPost.value) } catch (cause) { publicationError.value = 'The topic was created. Bluesky: ' + (cause as Error).message; return } }
    try { sessionStorage.removeItem(originKey) } catch { /* The created topic is available. */ }
    if (originKey !== key.value || actor !== props.actorKey || tangent !== props.tangentKey || !['topics', 'new-topic'].includes(props.router.route.kind) || !('tangent' in props.router.route) || props.router.route.tangent !== tangent) return
    Object.assign(draft, emptyDraft()); open.value = false
    props.router.navigate('/t/' + encodeURIComponent(tangent) + '/topics/' + encodeURIComponent(topicKey))
  } catch (cause) {
    if ([400, 401, 403, 404, 409, 422].includes((cause as { status?: number }).status ?? 0) && originKey === key.value) { draft.requestId = ''; draft.pendingTitle = ''; draft.pendingText = ''; draft.pendingFormat = 'plain' }
    if (originKey === key.value && actor === props.actorKey) error.value = (cause as Error).message
  } finally { inFlight.delete(originKey) }
}
watch(() => props.kind, kind => { if (!draft.title && !draft.text && !draft.requestId) draft.format = kind === 'articles' || props.dedicated ? 'markdown' : 'plain' })
</script>

<template>
  <section class="start-conversation" :class="{ 'is-writing': open, 'article-writing': kind === 'articles', 'creation-writing': dedicated }" aria-labelledby="start-conversation-title" @keydown="keyboardPublish">
    <button v-if="!open && showTrigger !== false" class="btn btn-primary" type="button" @click="show">{{ kind === 'articles' ? 'Write an article' : 'Start a Topic' }}</button>
    <Teleport v-if="open && indexAvailable" :to="dedicated ? '#creation-index-slot' : '#article-index-slot'">
      <div v-if="kind === 'articles'" class="draft-index index-links"><h2 class="sidebar-heading">Contents</h2><button v-for="(heading, index) in draftOutline" :key="index" type="button" @click="draftSection(index)">{{ heading }}</button><p v-if="!draftOutline.length" class="hint">Add headings to shape your article.</p><p class="draft-word-count">{{ wordCount.toLocaleString() }} words · {{ Math.max(1, Math.ceil(wordCount / 220)) }} min read</p><section class="publishing-checks"><h3>Before publishing</h3><ul><li v-for="check in checks" :key="check.label" :class="{ 'check-ready': check.ok }"><span aria-hidden="true">{{ check.ok ? '✓' : '○' }}</span>{{ check.label }}</li></ul><template v-if="firstImage && !coverDescription.trim()"><p class="hint">The cover has no description.</p><button type="button" class="text-action" @click="coverDescriptionOpen = true; preview = false">Describe it</button></template><p v-else class="hint">A cover is optional. Review link destinations before publishing.</p></section></div>
      <div v-else class="writing-guide"><h2 class="sidebar-heading">Formatting</h2><dl><div><dt><strong>Bold</strong></dt><dd>**text**</dd></div><div><dt><em>Italic</em></dt><dd>*text*</dd></div><div><dt>Link</dt><dd>[text](url)</dd></div><div><dt>Quote</dt><dd>&gt; text</dd></div><div><dt>List</dt><dd>- item</dd></div><div><dt>Code</dt><dd>`code`</dd></div><div><dt>Mention</dt><dd>@handle</dd></div></dl><p>Mentions notify people and agents who can read here.</p><h3 class="sidebar-heading">Shortcuts</h3><dl><div><dt>Bold</dt><dd>⌘ / Ctrl B</dd></div><div><dt>Italic</dt><dd>⌘ / Ctrl I</dd></div><div><dt>Post</dt><dd>⌘ / Ctrl ↵</dd></div></dl></div>
    </Teleport>
    <Teleport v-if="open && dedicated && pathAvailable" to="#creation-path-actions"><span class="creation-saved">{{ savedAt ? 'Saved ' + savedAt : 'Draft kept in this tab' }}</span><template v-if="kind === 'articles'"><button class="btn btn-quiet" type="button" :aria-pressed="preview" @click="preview = !preview">{{ preview ? 'Edit' : 'Preview' }}</button><button class="btn btn-primary" type="button" :disabled="busy || composerUpload || remainingBytes < 0" :aria-expanded="publishPanel" @click="requestPublish">Publish <span aria-hidden="true">⌄</span></button></template></Teleport>
    <form v-if="open" ref="formElement" class="start-conversation-form" @submit.prevent="kind === 'articles' && dedicated && !publishPanel ? requestPublish() : submit()">
      <header class="creation-writing-header"><h1 id="start-conversation-title" class="visually-hidden">{{ kind === 'articles' ? 'Write an article' : 'New topic' }}</h1><p>Posting in <a :href="'/t/' + encodeURIComponent(tangentKey || '') + '/topics'">{{ tangentName }}</a></p><button type="button" class="btn btn-quiet" :disabled="busy" @click="close">Close</button></header>
      <section v-if="kind === 'articles' && publishPanel" class="creation-publish-panel" aria-labelledby="publish-title"><h2 id="publish-title">Publish to {{ tangentName }}</h2><fieldset><legend>Who can read</legend><label class="publish-audience-choice"><input v-model="selectedAudience" type="radio" value="Public" :disabled="busy || awaitingConfirmation"><span><strong>Public</strong><small>Anyone on the web can read this article.</small></span></label><label class="publish-audience-choice"><input v-model="selectedAudience" type="radio" value="Restricted" :disabled="busy || awaitingConfirmation"><span><strong>Admitted</strong><small>People admitted to this Tangent. Can’t go to Bluesky.</small></span></label></fieldset><label class="check-row"><input v-model="publishToBluesky" type="checkbox" :disabled="selectedAudience !== 'Public' || !blueskyReady || awaitingConfirmation">Also post to Bluesky</label><p v-if="selectedAudience === 'Public' && !blueskyReady" class="hint">Bluesky publishing is not connected for this account. <a v-if="blueskyConnectUrl" :href="blueskyConnectUrl">Connect Bluesky</a> <button class="text-action" type="button" @click="checkBluesky">Check connection</button></p><div v-if="publishToBluesky && selectedAudience === 'Public'" class="publish-link-preview"><img v-if="firstImage" :src="firstImage[2]" alt=""><strong>{{ draft.title }}</strong><span>{{ draft.subtitle || tangentName }}</span></div><div class="creation-actions"><button class="btn btn-quiet" type="button" :disabled="busy" @click="publishPanel = false">Cancel</button><button class="btn btn-primary" type="submit" :disabled="busy || composerUpload || remainingBytes < 0">{{ busy ? 'Publishing…' : awaitingConfirmation ? 'Confirm original request' : 'Publish' }}</button></div></section>
      <template v-if="!preview"><label class="creation-title-field"><span class="visually-hidden">Title</span><input v-model="draft.title" class="input" required maxlength="120" :disabled="busy || awaitingConfirmation" :placeholder="kind === 'articles' ? 'Your article title' : 'What’s on your mind?'"></label><label v-if="kind === 'articles'" class="creation-subtitle-field"><span class="visually-hidden">Subtitle</span><input v-model="draft.subtitle" class="input" maxlength="240" :disabled="busy || awaitingConfirmation" placeholder="Add a subtitle"></label>
      <div v-if="kind === 'articles' && firstImage" class="creation-cover-tools"><span>Cover · shown in lists and link previews</span><button type="button" class="text-action" :disabled="busy || awaitingConfirmation" @click="coverDescriptionOpen = !coverDescriptionOpen">{{ coverDescription.trim() ? 'Description' : 'Describe cover' }}</button><button type="button" class="text-action" :disabled="busy || awaitingConfirmation" @click="removeCover">Remove</button><label v-if="coverDescriptionOpen">Description<input v-model="coverDescription" class="input" :disabled="busy || awaitingConfirmation" placeholder="Describe this image for readers who cannot see it."></label></div>
      <div v-if="kind === 'discussions' || !dedicated" class="creation-format-control"><button class="btn btn-quiet composer-format-toggle" type="button" :aria-pressed="dedicated && kind === 'discussions' ? richEditing : draft.format === 'markdown'" :disabled="busy || awaitingConfirmation || composerUpload" @click="toggleFormatting">Aa <span>Formatting</span></button><span>Markdown works here</span></div>
      <MarkdownComposer v-if="draft.format === 'markdown' && (kind === 'articles' || richEditing || !dedicated)" v-model="draft.text" :tangent-key="tangentKey ?? ''" :identity="key" :label="kind === 'articles' ? 'Article body' : 'Opening thought'" :disabled="busy || awaitingConfirmation" @busy="composerUpload = $event" /><label v-else class="creation-opening"><span class="visually-hidden">Opening thought</span><textarea v-model="draft.text" class="input" required rows="8" :disabled="busy || awaitingConfirmation" aria-describedby="opening-size" placeholder="Give people somewhere to begin…"></textarea></label><p v-if="kind === 'articles' && draft.format === 'markdown' && !firstImage" class="hint">The first uploaded image becomes the article cover.</p></template>
      <article v-else class="creation-article-preview"><p class="eyebrow">Preview · {{ selectedAudience === 'Public' ? 'Public' : 'Admitted' }}</p><h1>{{ draft.title || 'Your article title' }}</h1><p v-if="draft.subtitle" class="creation-preview-subtitle">{{ draft.subtitle }}</p><p class="creation-preview-byline">{{ author }} · {{ Math.max(1, Math.ceil(wordCount / 220)) }} min read</p><div v-if="draft.format === 'markdown'" class="article-prose post-content-markdown" v-html="previewHtml"></div><div v-else class="article-prose creation-plain-preview">{{ draft.text }}</div></article>
      <p v-if="remainingBytes < 1024" id="opening-size" class="hint" :class="{ error: remainingBytes < 0 }">{{ remainingBytes < 0 ? 'Too long by ' + -remainingBytes + ' bytes' : remainingBytes + ' bytes left' }}</p><p v-if="awaitingConfirmation" class="hint">This request may have completed. Retry the original request to confirm it before editing.</p>
      <footer class="creation-writing-footer"><div class="creation-posting-context"><span>Posting as <strong>{{ author }}</strong></span><label v-if="kind === 'discussions'" class="creation-posting-audience"><span class="visually-hidden">Who can read</span><select v-model="selectedAudience" :disabled="busy || awaitingConfirmation"><option value="Public">Public · anyone can read</option><option value="Restricted">Admitted · people admitted here</option></select></label><span v-else>{{ selectedAudience === 'Public' ? 'Public · anyone can read' : 'Admitted · people admitted here' }}</span></div><div class="creation-actions"><label v-if="kind === 'discussions'" class="check-row"><input v-model="publishToBluesky" type="checkbox" :disabled="selectedAudience !== 'Public' || !blueskyReady || awaitingConfirmation">Also post to Bluesky</label><span v-else class="hint">{{ savedAt ? 'Draft saved ' + savedAt : 'Draft kept in this browser tab' }}</span><button class="btn btn-quiet" type="button" :disabled="busy || awaitingConfirmation" @click="discardConfirm = !discardConfirm">Discard</button><button v-if="kind === 'discussions'" class="btn btn-primary" :disabled="busy || composerUpload || remainingBytes < 0 || !draft.title.trim() || !draft.text.trim()" type="submit">{{ busy ? 'Posting…' : composerUpload ? 'Uploading…' : awaitingConfirmation ? 'Confirm original request' : 'Post' }} <kbd>⌘↵</kbd></button><button v-else-if="!dedicated" class="btn btn-primary" :disabled="busy || composerUpload || remainingBytes < 0" type="submit">Publish article</button></div><p v-if="kind === 'discussions' && selectedAudience !== 'Public'" class="hint">Only public posts can go to Bluesky.</p><p v-else-if="kind === 'discussions' && !blueskyReady" class="hint"><a v-if="blueskyConnectUrl" :href="blueskyConnectUrl">Connect Bluesky to publish there.</a><span v-else>Bluesky publishing is not connected for this account.</span></p><div v-if="discardConfirm" class="creation-discard-confirm"><span>Discard this draft? Your title and writing will be removed.</span><button type="button" class="btn btn-quiet" @click="discardConfirm = false">Keep writing</button><button type="button" class="btn btn-danger" @click="discard">Discard draft</button></div></footer>
      <p v-if="publicationError" class="error" role="alert">{{ publicationError }} <button class="btn" type="button" @click="submit">Retry publication</button><a v-if="createdPost" :href="'/t/' + encodeURIComponent(tangentKey || '') + '/' + encodeURIComponent(createdPost)">Open created topic</a></p><p v-if="error" class="error" role="alert">{{ error }}</p>
    </form>
  </section>
</template>
