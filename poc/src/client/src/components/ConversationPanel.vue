<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useSite } from '../stores/site'
import { httpRequest, contentApi } from '../api'
import TopicDetailsPanel from './TopicDetailsPanel.vue'
import ReplyFilters from './ReplyFilters.vue'
import HistoryDisclosure from './HistoryDisclosure.vue'
import PostContent from './PostContent.vue'
import ParticipantAvatar from './ParticipantAvatar.vue'
import PostLinkButton from './PostLinkButton.vue'
import SourceAttribution from './SourceAttribution.vue'
import TopicSettingsPanel from './TopicSettingsPanel.vue'
const MarkdownComposer = defineAsyncComponent(() => import('./MarkdownComposer.vue'))
import BlueskyPublishPanel from './BlueskyPublishPanel.vue'
import { MAX_POST_BYTES, type Conversation, type Message } from '../composables/conversation'
import type { Moderation } from '../composables/moderation'
import type { Settings } from '../composables/settings'
import { directoryDate, directoryRelativeDate } from '../composables/directories'
import {
  byteLength as utf8ByteLength, sizeFacets, recordFacet, replyMention,
  mentionables, type MentionTarget,
} from '../composables/facets'

const props = defineProps<{ conversation: Conversation; moderation: Moderation; tangentName?: string; anchorPost?: string; articleSettingsOpen?: boolean; settings?: Settings }>()
const emit = defineEmits<{ closeArticleSettings: [] }>()
const site = useSite()

const {
  selectedTopic, messages, openingPost: liveOpeningPost, draft, draftFormat, error, loadingTopic, loadingHistory, moreMessages, newMessages, resolved, replyTo,
  send, beginReply, cancelReply, saveEdit, cancelEdit, removeMessage, readMore,
} = props.conversation

const editing = ref<Message | null>(null)
const editText = ref('')
const composerUpload = ref(false)
const posting = ref(false)
const alsoPublish = ref(false)
const blueskyReady = ref(false)
const blueskyConnect = ref('')
const publicationStatus = ref('')
const retryPublication = ref('')
watch(() => [site.site?.participant?.participantRef, selectedTopic.value?.key], async () => {
  const actor = site.site?.participant?.participantRef
  alsoPublish.value = false
  blueskyReady.value = false
  blueskyConnect.value = ''
  publicationStatus.value = ''
  retryPublication.value = ''
  if (!actor || !site.site?.participant?.did || !selectedTopic.value?.canWrite) return
  try {
    const connection = await contentApi.blueskyConnection()
    if (site.site?.participant?.participantRef !== actor) return
    blueskyReady.value = connection.canPublish && connection.actorDid === site.site?.participant?.did
    if (connection.connectUrl) {
      const url = new URL(connection.connectUrl, location.origin)
      if (url.origin === location.origin && url.pathname === '/api/v1/content/bluesky/connect') {
        url.searchParams.set('return', location.pathname + location.search)
        blueskyConnect.value = url.pathname + url.search
      }
    }
  } catch { /* The reply remains available when the external connection cannot be checked. */ }
}, { immediate: true })
const editUpload = ref(false)
const savingEdit = ref(false)
const replyTarget = replyTo
const deleting = ref<string | null>(null)
const publishingPost = ref<string | null>(null)
const repliesBelow = ref(false)
const placementKey = computed(() => 'tangent:replies-placement:' + (site.site?.participant?.participantRef || 'visitor'))
watch(placementKey, key => { try { repliesBelow.value = localStorage.getItem(key) === 'below' } catch {} }, {immediate:true})
let loadingPlacement=false
watch(placementKey,async ()=>{const actor=site.site?.participant?.participantRef;if(!actor)return;try{const r=await httpRequest('/api/workspace/preferences',{cache:'no-store'});const prefs=await r.json();if(r.ok&&actor===site.site?.participant?.participantRef){loadingPlacement=true;repliesBelow.value=prefs.repliesPlacement==='below';await nextTick();loadingPlacement=false}}catch{}},{immediate:true})
watch(repliesBelow, value => {try {localStorage.setItem(placementKey.value,value?'below':'beside')}catch{};if(!loadingPlacement&&site.site?.participant)void httpRequest('/api/workspace/preferences',{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({repliesPlacement:value?'below':'beside'})}).catch(()=>{})})
let readObserver:IntersectionObserver|null=null
let acknowledgedPage=''
watch(()=>messages.value.map(item=>item.id).join('|'),async()=>{
  readObserver?.disconnect();if(typeof IntersectionObserver==='undefined'||!props.conversation.acknowledgeVisiblePage)return;await nextTick();
  const last=messages.value.at(-1);const target=last?document.getElementById(last.id):null;if(!target)return;
  const page=selectedTopic.value?.key+':'+last?.id;if(acknowledgedPage===page)return;
  readObserver=new IntersectionObserver(entries=>{if(!entries.some(entry=>entry.isIntersecting)||document.visibilityState==='hidden')return;readObserver?.disconnect();acknowledgedPage=page;void props.conversation.acknowledgeVisiblePage().catch(()=>{acknowledgedPage=''})},{threshold:.6});readObserver.observe(target)
},{flush:'post'})
onUnmounted(()=>readObserver?.disconnect())
const lastVisitSequence = ref<number | null>(null)
const firstUnread = computed(() => lastVisitSequence.value === null ? '' : messages.value.find(message => message.sequence > lastVisitSequence.value! && message.authorParticipantId !== site.site?.participant?.participantRef)?.id || '')
watch(() => [selectedTopic.value?.key, site.site?.participant?.participantRef], async ([key, actor]) => {lastVisitSequence.value=null;if(!key||!actor)return;try {const response=await httpRequest('/api/workspace/reading?topic='+encodeURIComponent(key),{cache:'no-store'});const state=await response.json();if(selectedTopic.value?.key===key && site.site?.participant?.participantRef===actor && response.ok)lastVisitSequence.value=state.sequence}catch{}}, {immediate:true})
const isArticle = computed(() => selectedTopic.value?.presentation === 'article')
const openingPost = computed(() => liveOpeningPost.value || (!isArticle.value ? messages.value.find(message => message.sequence === 1) ?? null : null))
const articleIndexAvailable = ref(false)
const pathActionsAvailable = ref(false)
watch(() => [selectedTopic.value?.key, props.anchorPost, site.established], async () => {
  await nextTick()
  articleIndexAvailable.value = !!document.getElementById('article-index-slot')
  pathActionsAvailable.value = !!document.getElementById('topic-path-actions')
}, { immediate: true, flush: 'post' })
const replyFilter = ref<'all' | 'people' | 'agents'>('all')
const discussionMessages = computed(() => isArticle.value && openingPost.value
  ? messages.value.filter(message => message.id !== openingPost.value?.id)
  : messages.value)
const replyCount = computed(() => messages.value.filter(message => message.id !== openingPost.value?.id && message.sequence !== 1).length)
const filteredReplies = computed(() => discussionMessages.value.filter(message => message.id === openingPost.value?.id || replyFilter.value === 'all' || resolved.value[message.authorParticipantId]?.classification?.toLowerCase() === (replyFilter.value === 'people' ? 'human' : 'agent')))
const articleBody = ref<HTMLElement | null>(null)
const articleOutline = ref<Array<{ id: string; label: string; level: number }>>([])
const activeArticleHeading = ref('')
const readingPosition = ref<number | null>(null)
const articleAuthor = computed(() => {
  const id = openingPost.value?.authorParticipantId ?? ''
  const person = resolved.value[id]
  return person?.displayName || (person?.handle ? '@' + person.handle.replace(/^@/, '') : '') || 'Participant'
})
const articleDate = computed(() => openingPost.value?.content.createdAt
  ? new Date(openingPost.value.content.createdAt).toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' }) : '')
const articleMinutes = computed(() => Math.max(1, Math.ceil((openingPost.value?.content.text.trim().split(/\s+/).filter(Boolean).length ?? 0) / 220)))
const canManageTopic = computed(() => !!(selectedTopic.value?.canManage || selectedTopic.value?.permissions?.allowedActions?.includes('manageTopic')))
const articleSettingsHref = computed(() => selectedTopic.value
  ? '/t/' + encodeURIComponent(selectedTopic.value.tangentKey) + '/topics/' + encodeURIComponent(selectedTopic.value.key) + '/settings' : '')
watch(() => props.articleSettingsOpen, open => { if (open && props.moderation.panelOpen.value) props.moderation.closePanel() })
let restoredArticleHeading = ''
watch(() => [isArticle.value, openingPost.value?.id, openingPost.value?.content.html, openingPost.value?.content.text], async () => {
  articleOutline.value = []
  activeArticleHeading.value = ''
  if (!isArticle.value || !openingPost.value || openingPost.value.removed) return
  await nextTick()
  articleIndexAvailable.value=!!document.getElementById('article-index-slot')
  const topic = selectedTopic.value
  const opener = openingPost.value
  if (!topic || topic.presentation !== 'article') return
  const headings = articleBody.value?.querySelectorAll<HTMLElement>('h1, h2, h3, h4, h5, h6') ?? []
  articleOutline.value = [...headings].map((heading, index) => {
    const id = 'article-heading-' + (index + 1)
    heading.id = id
    return { id, label: heading.textContent?.trim() || 'Section ' + (index + 1), level: Number(heading.tagName.slice(1)) }
  }).filter(item => item.level >= 2 && item.level <= 4)
  updateActiveArticleHeading()
  paintQuoteTouchpoints()
  const fragment = window.location.hash
  if (!/^#article-heading-\d+$/.test(fragment)) return
  const path = window.location.pathname
  const tangentPath = '/t/' + encodeURIComponent(topic.tangentKey) + '/'
  if (path !== tangentPath + 'topics/' + encodeURIComponent(topic.key)
    && path !== tangentPath + encodeURIComponent(opener.id)
    && path !== tangentPath + encodeURIComponent(props.anchorPost ?? '')) return
  const restoreKey = path + fragment + ':' + topic.key + ':' + opener.id
  if (restoredArticleHeading === restoreKey) return
  restoredArticleHeading = restoreKey
  requestAnimationFrame(() => {
    if (selectedTopic.value?.key !== topic.key || openingPost.value?.id !== opener.id
      || window.location.pathname !== path || window.location.hash !== fragment) return
    document.getElementById(fragment.slice(1))?.scrollIntoView({ block: 'start' })
  })
}, { flush: 'post', immediate: true })
function paintQuoteTouchpoints() {
  articleBody.value?.querySelectorAll('.quote-touchpoint').forEach(item => item.remove())
  articleBody.value?.querySelectorAll('.quote-source').forEach(item=>item.replaceWith(...item.childNodes))
  const topic = selectedTopic.value
  if (!topic) return
  const path = '/t/' + encodeURIComponent(topic.tangentKey) + '/topics/' + encodeURIComponent(topic.key)
  for (const section of articleOutline.value) {
    const quoting = discussionMessages.value.filter(message => !message.removed && message.content.format === 'markdown' && /^> /m.test(message.content.text) && message.content.text.includes('](' + path + '#' + section.id + ')'))
    if (!quoting.length) continue
    const link = document.createElement('a')
    link.className = 'quote-touchpoint'; link.href = '#' + quoting[0]!.id
    link.textContent = quoting.length + (quoting.length === 1 ? ' quoted reply ↗' : ' quoted replies ↗')
    link.setAttribute('aria-label',link.textContent + ' to ' + section.label)
    const author=resolved.value[quoting[0]!.authorParticipantId];const mark=document.createElement('span');mark.className='quote-replier';mark.classList.toggle('is-agent',author?.classification?.toLowerCase()==='agent');mark.setAttribute('aria-hidden','true');const avatar=author?.avatar;if(avatar && (avatar.startsWith('/')&&!avatar.startsWith('//') || /^https:\/\//.test(avatar))){const img=document.createElement('img');img.src=avatar;img.alt='';img.referrerPolicy='no-referrer';mark.append(img)}else mark.textContent=(author?.displayName||author?.handle||'P').slice(0,1).toUpperCase();link.prepend(mark)

    const heading=document.getElementById(section.id)
    let paragraph:Element|null=null
    const quote=quoting[0]!.content.text.match(/^> (.+)$/m)?.[1]?.trim()
    if(quote&&heading)for(let sibling=heading.nextElementSibling;sibling&&!/^H[1-6]$/.test(sibling.tagName);sibling=sibling.nextElementSibling){if(sibling.textContent?.includes(quote)){paragraph=sibling;break}}
    if(paragraph&&quote){
      const walker=document.createTreeWalker(paragraph,NodeFilter.SHOW_TEXT);const nodes:Text[]=[];let node:Node|null;while((node=walker.nextNode()))nodes.push(node as Text)
      const text=nodes.map(node=>node.data).join(''),start=text.indexOf(quote);let offset=0,startNode:Text|undefined,endNode:Text|undefined,startOffset=0,endOffset=0
      for(const node of nodes){if(!startNode&&start>=offset&&start<offset+node.length){startNode=node;startOffset=start-offset}if(start+quote.length>offset&&start+quote.length<=offset+node.length){endNode=node;endOffset=start+quote.length-offset;break}offset+=node.length}
      if(startNode&&endNode){const range=document.createRange();range.setStart(startNode,startOffset);range.setEnd(endNode,endOffset);const mark=document.createElement('mark');mark.className='quote-source';mark.append(range.extractContents());range.insertNode(mark)}
      paragraph.append(link)
    }else heading?.append(link)
  }
}
watch(() => discussionMessages.value.map(message => message.id + message.content.text).join('|'), () => nextTick(paintQuoteTouchpoints))
function updateActiveArticleHeading() {
  if (!isArticle.value || !articleOutline.value.length) { activeArticleHeading.value = ''; return }
  if (window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 2) {
    activeArticleHeading.value = articleOutline.value[articleOutline.value.length - 1]!.id
    return
  }
  let current = articleOutline.value[0]!.id
  for (const item of articleOutline.value) {
    const heading = document.getElementById(item.id)
    if (heading && heading.getBoundingClientRect().top <= 145) current = item.id
  }
  activeArticleHeading.value = current
}
onMounted(() => window.addEventListener('scroll', updateActiveArticleHeading, { passive: true }))
onUnmounted(() => window.removeEventListener('scroll', updateActiveArticleHeading))
function rememberReadingPosition() { readingPosition.value = window.scrollY }
function backToReading() {
  if (readingPosition.value !== null) window.scrollTo({ top: readingPosition.value, behavior: 'smooth' })
  else document.getElementById('article-reading')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
watch(() => selectedTopic.value?.key, () => {
  restoredArticleHeading = ''
  readingPosition.value = null
  editing.value = null
  composerUpload.value = false
  editUpload.value = false
  deleting.value = null
  publishingPost.value = null
})
let scrolledPost = ''
let anchorFrame: number | undefined
onUnmounted(() => { if (anchorFrame !== undefined) cancelAnimationFrame(anchorFrame) })
watch(() => [props.anchorPost, messages.value.map(message => message.id).join('|'), openingPost.value?.id], () => {
  if (isArticle.value && /^#article-heading-\d+$/.test(window.location.hash)) return
  if (!props.anchorPost || scrolledPost === props.anchorPost || ![...messages.value, ...(openingPost.value ? [openingPost.value] : [])].some(message => message.id === props.anchorPost)) return
  scrolledPost = props.anchorPost
  const post = props.anchorPost
  if (anchorFrame !== undefined) cancelAnimationFrame(anchorFrame)
  anchorFrame = requestAnimationFrame(() => {
    if (props.anchorPost !== post || !readerElement.value) return
    const opener = post === openingPost.value?.id
    const target = opener ? readerElement.value.querySelector(isArticle.value ? '.article-head' : '.conversation-head') : document.getElementById(post)
    if (target && readerElement.value.contains(target)) target.scrollIntoView({ block: opener ? 'start' : 'center' })
  })
})

// --- The @-mention composer (facets.js) ---
const mentionOpen = ref(false)
const mentionTargets = ref<MentionTarget[]>([])
const mentionActive = ref(0)
const composerText = computed({
  get: () => draft.value,
  set: (value: string) => {
    draft.value = value
  },
})
let tokenStart = -1
let lookupVersion = 0
const composerField = ref<HTMLTextAreaElement | null>(null)

function onComposerInput() {
  if (draftFormat.value === 'markdown') { closeMention(); return }
  const textarea = composerField.value
  if (!textarea || !selectedTopic.value) return
  sizeFacets(selectedTopic.value.key, textarea.value)
  const before = textarea.value.slice(0, textarea.selectionStart ?? 0)
  const match = /(^|[\s(>])@([\w.-]*)$/.exec(before)
  if (!match) {
    closeMention()
    return
  }
  tokenStart = (textarea.selectionStart ?? 0) - match[2]!.length - 1
  void openMention(textarea, match[2]!)
}

async function openMention(textarea: HTMLTextAreaElement, prefix: string) {
  const topicKey = selectedTopic.value?.key
  if (!topicKey) {
    closeMention()
    return
  }
  const version = ++lookupVersion
  let list: MentionTarget[]
  try {
    list = await mentionables(topicKey, prefix)
  } catch {
    if (version === lookupVersion) closeMention()
    return
  }
  if (version !== lookupVersion || topicKey !== selectedTopic.value?.key || document.activeElement !== textarea) return
  mentionTargets.value = list ?? []
  if (!mentionTargets.value.length) {
    closeMention()
    return
  }
  mentionOpen.value = true
  mentionActive.value = 0
  positionMention(textarea)
}

function positionMention(textarea: HTMLTextAreaElement): void {
  requestAnimationFrame(() => {
    const node = document.getElementById('mention-popup')
    const form = textarea.closest('form')?.getBoundingClientRect()
    if (!node || !form) return
    const caret = textarea.selectionStart ?? 0
    const mirror = document.createElement('div')
    const style = getComputedStyle(textarea)
    for (const key of ['fontFamily', 'fontSize', 'lineHeight', 'padding', 'width', 'whiteSpace', 'wordBreak'] as const) {
      (mirror.style as unknown as Record<string, string>)[key] = style[key]
    }
    mirror.style.position = 'absolute'
    mirror.style.visibility = 'hidden'
    mirror.textContent = textarea.value.slice(0, caret)
    const marker = document.createElement('span')
    marker.textContent = '​'
    mirror.appendChild(marker)
    document.body.appendChild(mirror)
    const left = Math.min(marker.offsetLeft, form.width - node.offsetWidth - 8)
    document.body.removeChild(mirror)
    node.style.left = Math.max(0, left) + 'px'
    node.style.top = Math.min(textarea.offsetTop + textarea.offsetHeight + 4, form.height - node.offsetHeight - 4) + 'px'
  })
}

function onComposerKeydown(event: KeyboardEvent) {
  if (!mentionOpen.value) return
  const node = document.getElementById('mention-popup')
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    mentionActive.value = (mentionActive.value + (event.key === 'ArrowDown' ? 1 : -1) + mentionTargets.value.length) % mentionTargets.value.length
    node?.querySelectorAll('.mention-row').forEach((row, index) => {
      row.classList.toggle('active', index === mentionActive.value)
      row.setAttribute('aria-selected', String(index === mentionActive.value))
    })
    composerField.value?.setAttribute('aria-activedescendant', 'mention-option-' + mentionActive.value)
    node?.querySelectorAll('.mention-row')[mentionActive.value]?.scrollIntoView({ block: 'nearest' })
  } else if (event.key === 'Enter' || event.key === 'Tab') {
    event.preventDefault()
    selectMention(mentionActive.value)
  } else if (event.key === 'Escape') {
    closeMention()
  }
}

function selectMention(index: number) {
  const textarea = composerField.value
  const entry = mentionTargets.value[index]
  if (!textarea || !selectedTopic.value || !entry) {
    closeMention()
    return
  }
  const insert = '@' + entry.insert
  const after = textarea.value.slice(textarea.selectionStart ?? 0)
  const beforeKeep = textarea.value.slice(0, tokenStart)
  const start = (beforeKeep + insert).length
  const next = beforeKeep + insert + ' ' + after
  draft.value = next
  const caret = start + 1
  recordFacet(selectedTopic.value.key, next, tokenStart, entry, insert)
  closeMention()
  void textarea
  requestAnimationFrame(() => {
    composerField.value?.focus()
    composerField.value?.setSelectionRange(caret, caret)
  })
}

function closeMention() {
  lookupVersion++
  mentionOpen.value = false
  mentionActive.value = 0
  mentionTargets.value = []
  composerField.value?.setAttribute('aria-expanded', 'false')
  composerField.value?.removeAttribute('aria-activedescendant')
}

function onMentionBlur() {
  setTimeout(() => {
    if (mentionOpen.value && document.activeElement?.closest?.('#mention-popup') == null) closeMention()
  }, 120)
}

const canWrite = computed(() => selectedTopic.value?.canWrite ?? false)
const replyUnavailable = computed(() => selectedTopic.value?.isLocked
  ? 'Replies are paused while this conversation is locked.'
  : !site.site?.participant
  ? 'Sign in to see whether your account can reply. Reading stays open.'
  : 'Your current access does not allow replies here. A Tangent steward can help you understand access.')
function replySignInHref() {
  return '/sign-in?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
}
function prepareReplySignIn(event: MouseEvent) {
  (event.currentTarget as HTMLAnchorElement).href = replySignInHref()
}
const readerElement = ref<HTMLElement | null>(null)
const composerElement = ref<HTMLElement | null>(null)
const composerGeometry = ref<Record<string, string>>({})
let composerObserver: ResizeObserver | null = null
function measureComposer() {
  const reader = readerElement.value?.getBoundingClientRect()
  const composer = composerElement.value?.getBoundingClientRect()
  if (!reader?.width || !composer) return
  composerGeometry.value = { '--composer-left': reader.left + 'px', '--composer-width': reader.width + 'px', '--composer-height': composer.height + 'px' }
}
watch([readerElement, composerElement], () => {
  composerObserver?.disconnect()
  if (typeof ResizeObserver === 'undefined') return
  composerObserver = new ResizeObserver(measureComposer)
  composerObserver.observe(document.documentElement)
  if (readerElement.value) composerObserver.observe(readerElement.value)
  if (composerElement.value) composerObserver.observe(composerElement.value)
  measureComposer()
}, { flush: 'post' })
onMounted(() => window.addEventListener('resize', measureComposer))
onUnmounted(() => { composerObserver?.disconnect(); window.removeEventListener('resize', measureComposer) })


function isOwnPost(message: Message) {
  const viewer = site.site?.participant?.participantRef ?? ''
  return !!viewer && !message.removed && message.authorParticipantId === viewer
}
function mayEdit(message: Message) {
  return isOwnPost(message) && selectedTopic.value?.canRead === true && selectedTopic.value?.canWrite === true
    && selectedTopic.value?.isLocked !== true && selectedTopic.value?.allowPostEditing !== false
    && !!message.permissions?.allowedActions.includes('editOwnPost')
}
function currentMessage(id: string) {
  return openingPost.value?.id === id ? openingPost.value : messages.value.find(message => message.id === id)
}
function editUnavailableReason() {
  return selectedTopic.value?.isLocked
    ? (isArticle.value ? 'This article is locked.' : 'This conversation is locked.')
    : selectedTopic.value?.allowPostEditing === false
    ? (isArticle.value ? 'Editing is turned off for this article.' : 'Editing is turned off for this conversation.')
    : 'Your current access does not allow editing this Post.'
}
function mayDelete(message: Message) {
  return !message.removed && !!message.permissions?.allowedActions.some(action => ['deleteOwnPost', 'removePost'].includes(action))
}
function mayPublish(message: Message) {
  return !message.removed && selectedTopic.value?.readAudience === 'Public'
    && !!site.site?.participant?.did && message.authorParticipantId === site.site.participant.participantRef
}
function startEdit(message: Message) {
  if (editing.value?.id === message.id) return
  editing.value = message
  editText.value = message.content.text
}
async function submitEdit() {
  if (!editing.value || editUpload.value || savingEdit.value) return
  const requested = editing.value
  const currentTopic = selectedTopic.value
  const actor = site.site?.participant?.participantRef
  if (!currentTopic || !actor) return
  const submittedText = editText.value
  savingEdit.value = true
  try {
    const response = await httpRequest('/api/v1/tangents/' + encodeURIComponent(currentTopic.tangentKey) + '/posts/' + encodeURIComponent(requested.id), {
      credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' },
    })
    if (!response.ok) throw new Error('This Post can no longer be edited. Your changes are still here.')
    const result = await response.json() as { topic?: { key?: string; tangentKey?: string; canRead?: boolean; canWrite?: boolean; isLocked?: boolean; allowPostEditing?: boolean }; window?: { messages?: Message[]; openingPost?: Message | null } }
    if (editing.value?.id !== requested.id || selectedTopic.value?.key !== currentTopic.key || actor !== site.site?.participant?.participantRef) return
    const target = result.window?.openingPost?.id === requested.id ? result.window.openingPost : result.window?.messages?.find(message => message.id === requested.id)
    if (result.topic?.key !== currentTopic.key || result.topic?.tangentKey !== currentTopic.tangentKey || !target || target.removed) throw new Error('This Post can no longer be edited. Your changes are still here.')
    selectedTopic.value = { ...selectedTopic.value!, ...result.topic }
    if (!mayEdit(target)) throw new Error(editUnavailableReason())
    if (!await saveEdit(target, submittedText)) return
    if (editing.value?.id === target.id && editText.value === submittedText) {
      editing.value = null
      editText.value = ''
    }
  } catch (cause) { error.value = (cause as Error).message }
  finally {
    savingEdit.value = false
  }
}
function closeEdit() {
  if (editing.value) cancelEdit(editing.value)
  editing.value = null
  editText.value = ''
}
async function deleteMessage(message: Message) {
  await removeMessage(message)
  deleting.value = null
}
function startReply(message: Message) {
  beginReply(message)
  if (draftFormat.value === 'plain' && selectedTopic.value && message.authorParticipantId !== (site.site?.participant?.participantRef ?? '')) {
    const inserted = replyMention(selectedTopic.value.key, draft.value, message.authorParticipantId, authorHandle(message))
    if (inserted) {
      draft.value = inserted.text
      requestAnimationFrame(() => composerField.value?.setSelectionRange(inserted.caret, inserted.caret))
    }
  }
}
function authorHandle(message: Message): string {
  const entry = resolved.value[message.authorParticipantId]
  return entry?.handle ?? ''
}

function authorLabel(message: Message): string {
  const entry = resolved.value[message.authorParticipantId]
  const handle = entry?.handle ? '@' + entry.handle.replace(/^@/, '') : ''
  return entry?.displayName || handle || message.authorParticipantId
}
function closeReply() {
  replyTarget.value = null
  cancelReply()
}
async function confirmSubmittedPost() {
  if (posting.value) return
  posting.value = true
  try { await props.conversation.retryPendingPost() }
  finally { posting.value = false }
}
async function submitPost() {
  if (!draft.value.trim() || composerUpload.value || posting.value) return
  const actor = site.site?.participant?.participantRef
  const publish = alsoPublish.value && blueskyReady.value && selectedTopic.value?.publiclyReadable
  posting.value = true
  try {
    const postId = await send()
    if (postId && publish && site.site?.participant?.participantRef === actor) await publishReply(postId)
  } finally { posting.value = false }
}
function authorHref(message: Message): string | undefined {
  const source = /^at:\/\/(did:[^/]+)\/app\.bsky\.feed\.post\/[a-z0-9]+$/.exec(message.sourceUri || '')
  if (source) return 'https://bsky.app/profile/' + encodeURIComponent(source[1]!)
  const profile = resolved.value[message.authorParticipantId]?.profileUrl
  return profile?.startsWith('/u/') ? profile : undefined
}
async function publishReply(postId: string) {
  const actor = site.site?.participant?.participantRef
  publicationStatus.value = 'Reply posted. Publishing to Bluesky…'
  retryPublication.value = ''
  try {
    await contentApi.publishPost(postId)
    if (site.site?.participant?.participantRef === actor) publicationStatus.value = 'Reply posted and published to Bluesky.'
  } catch (cause) {
    if (site.site?.participant?.participantRef !== actor) return
    publicationStatus.value = 'Your reply is posted here. Bluesky: ' + (cause as Error).message
    retryPublication.value = postId
  }
}
const remainingBytes = computed(() => MAX_POST_BYTES - utf8ByteLength(draft.value))
const editRemainingBytes = computed(() => MAX_POST_BYTES - utf8ByteLength(editText.value))
watch(draftFormat, () => closeMention())
const audienceLabel = computed(() => selectedTopic.value?.publiclyReadable === true ? 'Public' : 'Admitted')
const articleAudience = audienceLabel
const composerHandle = computed(() => site.site?.participant?.handle ? '@' + site.site.participant.handle.replace(/^@/, '') : site.displayName)
const quoteSelection = ref('')
const quoteSection = ref('')
function captureQuote() {
  const selection = window.getSelection()
  const node = selection?.anchorNode
  if (!node || !articleBody.value?.contains(node) || !canWrite.value) { quoteSelection.value = ''; return }
  quoteSelection.value = selection?.toString().trim().slice(0, 1200) || ''
  const heading = [...(articleBody.value?.querySelectorAll('h2,h3,h4') || [])].filter(item => !!(item.compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING)).at(-1)
  quoteSection.value = heading?.id || ''
}
function quoteReply() {
  if (!openingPost.value || !quoteSelection.value) return
  startReply(openingPost.value)
  const label = articleOutline.value.find(item => item.id === quoteSection.value)?.label || 'article'
  const href = '/t/' + encodeURIComponent(selectedTopic.value!.tangentKey) + '/topics/' + encodeURIComponent(selectedTopic.value!.key) + (quoteSection.value ? '#' + quoteSection.value : '')
  draftFormat.value = 'markdown'
  draft.value = quoteSelection.value.split('\n').map(line => '> ' + line).join('\n') + '\n\n[From ' + label.replace(/[\[\]]/g, '') + ' ↑](' + href + ')\n\n' + draft.value
  quoteSelection.value = ''
  document.querySelector('.compose')?.scrollIntoView({ block: 'center' })
}
onMounted(() => document.addEventListener('selectionchange', captureQuote))
onUnmounted(() => document.removeEventListener('selectionchange', captureQuote))
</script>

<template>
  <p v-if="!selectedTopic" class="lede" role="status">{{ loadingTopic || !error ? 'Loading conversation…' : 'This conversation is unavailable.' }}</p>
  <div v-else id="topic-content" ref="readerElement" class="conversation" :class="{ 'article-topic': isArticle, 'replies-below': repliesBelow, 'composer-ready': canWrite && !!composerGeometry['--composer-left'] }" :style="composerGeometry">
    <div v-if="!isArticle && !pathActionsAvailable" class="conversation-toolbar">
      <a class="conversation-back" :href="'/t/' + encodeURIComponent(selectedTopic.tangentKey) + '/topics'">← {{ tangentName || selectedTopic.tangentKey }}</a>
      <div class="conversation-toolbar-actions">
        <button
          id="stewardship-open"
          class="btn btn-quiet"
          type="button"
          aria-controls="stewardship-panel"
          :aria-expanded="moderation.panelOpen.value"
          :hidden="!moderation.stewardshipAvailable.value || articleSettingsOpen"
          @click="moderation.panelOpen.value ? moderation.closePanel() : moderation.openPanel()"
        >Moderation</button>
      </div>
    </div>
    <header v-if="!isArticle" class="conversation-head">
      <h2 id="topic-title">{{ selectedTopic.title }}</h2>
      <p class="conversation-audience"><span class="tag">{{ audienceLabel }}</span><span v-if="openingPost"> · Started {{directoryRelativeDate(openingPost.content.createdAt)}}</span><span> · {{replyCount}} replies on this page</span></p>
    </header>
    <p v-if="error" class="action-status error" role="alert">{{ error }}</p>
    <p v-if="loadingHistory" class="hint">Loading history…</p>
    <form v-if="editing && !currentMessage(editing.id)" class="message-edit-form article-detached-edit" @submit.prevent="submitEdit">
      <p class="hint">Editing a Post outside this history page. Your draft is kept while the view updates.</p>
      <MarkdownComposer v-if="editing.content.format === 'markdown'" v-model="editText" :tangent-key="selectedTopic.tangentKey" :topic-key="selectedTopic.key" :identity="(site.site?.participant?.participantRef ?? '') + ':' + selectedTopic.key + ':' + editing.id" label="Edit Post" @busy="editUpload = $event" />
      <label v-else>Edit Post<textarea v-model="editText" class="input" rows="10"></textarea></label>
      <button class="btn btn-primary" type="submit" :disabled="editUpload || savingEdit || editRemainingBytes < 0">{{ savingEdit ? 'Saving…' : 'Save changes' }}</button>
      <button class="btn btn-quiet" type="button" @click="closeEdit">Cancel</button>
    </form>
    <Teleport v-if="pathActionsAvailable" to="#topic-path-actions">
      <div class="topic-context-actions">
        <PostLinkButton v-if="openingPost" :tangent-key="selectedTopic.tangentKey" :post-id="openingPost.id" :label="isArticle ? 'Copy link to article' : 'Copy link to topic'" />
        <button v-if="openingPost && mayEdit(openingPost)" class="btn btn-quiet topic-edit-action" type="button" @click="startEdit(openingPost)">Edit</button>
        <details class="topic-more-menu"><summary class="btn btn-quiet" aria-label="More topic actions">More <span aria-hidden="true">⌄</span></summary><div class="topic-more-panel">
          <a v-if="canManageTopic" class="btn btn-quiet" :href="articleSettingsHref">{{ isArticle ? 'Article settings' : 'Topic settings' }}</a>
          <button v-if="moderation.stewardshipAvailable.value" class="btn btn-quiet" type="button" @click="moderation.panelOpen.value ? moderation.closePanel() : moderation.openPanel()">Reports and moderation</button>
          <TopicDetailsPanel :conversation="conversation" />
        </div></details>
      </div>
    </Teleport>
    <div v-if="isArticle || articleSettingsOpen" class="article-layout">
      <div v-if="isArticle" class="article-main">
        <div v-if="!pathActionsAvailable" class="article-context-bar">
          <a class="conversation-back" :href="'/t/' + encodeURIComponent(selectedTopic.tangentKey) + '/topics'">← {{ tangentName || selectedTopic.tangentKey }}</a>
          <span class="article-context-label">Article</span>
          <div class="article-context-actions">
            <button v-if="openingPost && isOwnPost(openingPost)" class="btn btn-quiet article-edit-trigger" type="button" :disabled="!mayEdit(openingPost)" :aria-describedby="!mayEdit(openingPost) ? 'article-edit-reason' : undefined" :title="mayEdit(openingPost) ? 'Edit article' : editUnavailableReason()" @click="startEdit(openingPost)">Edit</button>
            <a v-if="canManageTopic && !articleSettingsOpen" id="article-settings-trigger" class="btn btn-quiet" :href="articleSettingsHref">Article Settings</a>
            <button id="stewardship-open" class="btn btn-quiet article-moderation-trigger" type="button" aria-controls="stewardship-panel" :aria-expanded="moderation.panelOpen.value" :hidden="!moderation.stewardshipAvailable.value || articleSettingsOpen" @click="moderation.panelOpen.value ? moderation.closePanel() : moderation.openPanel()">Moderation</button>
          </div>
        </div>
        <p v-if="openingPost && isOwnPost(openingPost) && !mayEdit(openingPost)" id="article-edit-reason" class="article-edit-unavailable" role="status">{{ editUnavailableReason() }} <a v-if="canManageTopic" :href="articleSettingsHref + '?section=access'">Change in Article Settings</a></p>
        <header class="article-head">
          <h1 id="topic-title">{{ selectedTopic.title }}</h1>
          <p v-if="selectedTopic.topic" class="article-deck">{{ selectedTopic.topic }}</p>
          <div class="article-byline">
            <ParticipantAvatar v-if="openingPost && !openingPost.removed" class="article-author-mark" :participant="resolved[openingPost.authorParticipantId] ?? { displayName: articleAuthor }" />
            <span v-if="openingPost && !openingPost.removed" class="article-author-copy"><strong><a v-if="authorHref(openingPost)" class="participant-name" :href="authorHref(openingPost)">{{ articleAuthor }}</a><template v-else>{{ articleAuthor }}</template></strong><span v-if="resolved[openingPost.authorParticipantId]?.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span><small><time :datetime="openingPost.content.createdAt">{{ articleDate }}</time> <span aria-hidden="true">·</span> {{ articleMinutes }} min read</small></span>
            <span class="article-audience-badge" :title="audienceLabel" :aria-label="audienceLabel">{{ articleAudience }}</span>
            <PostLinkButton v-if="openingPost && !openingPost.removed" :tangent-key="selectedTopic.tangentKey" :post-id="openingPost.id" label="Copy link to article" />
            <a class="article-discussion-jump" href="#discussion" @click="rememberReadingPosition">{{ discussionMessages.length }} replies ↓</a>
          </div>
        </header>
        <details v-if="articleOutline.length" class="article-outline-mobile"><summary>Contents</summary><nav><a v-for="item in articleOutline" :key="item.id" :href="'#' + item.id">{{ item.label }}</a></nav></details>
        <article v-if="openingPost" :id="openingPost.id" class="article-reading" :class="{ 'message-anchor': anchorPost === openingPost.id }">
          <form v-if="editing?.id === openingPost.id" class="message-edit-form" @submit.prevent="submitEdit">
            <MarkdownComposer v-if="openingPost.content.format === 'markdown'" v-model="editText" :tangent-key="selectedTopic.tangentKey" :topic-key="selectedTopic.key" :identity="(site.site?.participant?.participantRef ?? '') + ':' + selectedTopic.key + ':' + openingPost.id" label="Article body" @busy="editUpload = $event" />
            <template v-else><label class="visually-hidden" for="article-edit">Edit opening Post</label><textarea id="article-edit" v-model="editText" class="input" rows="12"></textarea></template>
            <span class="hint">{{ openingPost.content.format === 'markdown' ? 'Formatted text' : 'Plain text' }} · {{ editRemainingBytes < 0 ? 'Too long by ' + -editRemainingBytes + ' bytes' : editRemainingBytes + ' bytes left' }}</span>
            <button class="btn btn-primary" type="submit" :disabled="editUpload || savingEdit || editRemainingBytes < 0 || !mayEdit(openingPost)">{{ editUpload ? 'Uploading image…' : savingEdit ? 'Saving…' : 'Save changes' }}</button>
            <button class="btn btn-quiet" type="button" @click="closeEdit">Cancel</button>
          </form>
          <div v-else-if="openingPost.removed" class="article-unavailable">The opening Post was removed.</div>
           <div v-else id="article-reading" ref="articleBody" class="article-prose article-media"><PostContent :text="openingPost.content.text" :format="openingPost.content.format" :html="openingPost.content.html" :facets="openingPost.facets" :resolved="resolved" /></div>
          <button v-if="quoteSelection" class="quote-reply-action btn btn-primary" type="button" @click="quoteReply">Reply to this passage</button>
          <SourceAttribution :post-id="openingPost.id" :source-uri="openingPost.sourceUri"/><footer class="article-post-actions">
            <HistoryDisclosure v-if="!openingPost.removed && (openingPost.editedAt || openingPost.changeId)" :conversation="conversation" :message="openingPost" />
            <details class="message-menu article-post-menu">
              <summary aria-label="Actions for the opening Post">···</summary>
              <div class="message-menu-panel">
                <button v-if="!openingPost.removed && moderation.allowed(openingPost, 'reportPost')" class="btn btn-quiet" type="button" @click="moderation.openReport(openingPost.id, authorLabel(openingPost))">Report post</button>
                <button v-if="mayPublish(openingPost)" class="btn btn-quiet" type="button" @click="publishingPost = openingPost.id">Publish to Bluesky</button>
                <button v-if="mayDelete(openingPost) && deleting !== openingPost.id" class="btn btn-quiet message-danger" type="button" @click="deleting = openingPost.id">Delete post</button>
                <div v-if="deleting === openingPost.id" class="stack"><p>Remove this Post from the Topic?</p><button class="btn btn-quiet message-danger" type="button" @click="deleteMessage(openingPost)">Remove Post</button><button class="btn btn-quiet" type="button" @click="deleting = null">Keep Post</button></div>
              </div>
            </details>
          </footer>
          <BlueskyPublishPanel v-if="publishingPost === openingPost.id && mayPublish(openingPost)" :key="openingPost.id + ':' + site.site?.participant?.did" :post-id="openingPost.id" :account-did="site.site!.participant!.did!" :account-label="site.site?.participant?.handle ? '@' + site.site.participant.handle : site.displayName" @close="publishingPost = null" />
        </article>
        <p v-else class="article-unavailable" role="status">{{loadingTopic || loadingHistory ? 'Loading the article…' : 'The opening Post is unavailable. The discussion remains below.'}}</p>
      </div>
      <aside v-if="articleSettingsOpen && settings" id="article-settings-rail" key="settings-rail" class="article-settings-rail" aria-labelledby="article-settings-title">
        <TopicSettingsPanel :key="(site.site?.participant?.participantRef || '') + ':' + selectedTopic.tangentKey + ':' + selectedTopic.key" :settings="settings" compact @return="emit('closeArticleSettings')" />
      </aside>
      <Teleport v-else-if="articleOutline.length" :to="articleIndexAvailable ? '#article-index-slot' : 'body'" :disabled="!articleIndexAvailable"><aside class="article-outline" aria-label="Contents">
        <p>Contents</p>
        <nav><a v-for="item in articleOutline" :key="item.id" :href="'#' + item.id" :class="{ 'article-outline-sub': item.level > 2, 'is-active': activeArticleHeading === item.id }" :aria-current="activeArticleHeading === item.id ? 'location' : undefined" @click="activeArticleHeading = item.id">{{ item.label }}</a></nav>
      </aside></Teleport>
    </div>
    <div class="reply-region">
    <section v-if="isArticle" id="discussion" class="article-discussion-head" aria-labelledby="discussion-title">
      <div><h2 id="discussion-title">Replies <span>{{ discussionMessages.length }}</span></h2></div>
      <div><button class="btn btn-quiet reply-placement" type="button" :aria-pressed="repliesBelow" @click="repliesBelow = !repliesBelow">{{ repliesBelow ? 'Replies beside' : 'Replies below' }}</button><button class="btn btn-quiet" type="button" @click="backToReading">Back to article ↑</button></div>
    </section>
    <template v-if="isArticle || !openingPost"><h2 v-if="!isArticle" class="discussion-replies-heading">Replies <span>{{ replyCount }}</span></h2><ReplyFilters v-model="replyFilter" /></template>
    <nav v-if="conversation.publicOlderBefore.value || conversation.publicNewerAfter.value" class="history-pages" aria-label="Conversation history pages">
      <a v-if="conversation.publicOlderBefore.value" class="btn btn-quiet" rel="prev" :href="'/t/' + encodeURIComponent(selectedTopic.tangentKey) + '/topics/' + encodeURIComponent(selectedTopic.key) + '?before=' + encodeURIComponent(conversation.publicOlderBefore.value)">← Older posts</a>
      <a v-if="conversation.publicNewerAfter.value" class="btn btn-quiet" rel="next" :href="'/t/' + encodeURIComponent(selectedTopic.tangentKey) + '/topics/' + encodeURIComponent(selectedTopic.key) + '?after=' + encodeURIComponent(conversation.publicNewerAfter.value)">Newer posts →</a>
    </nav>
    <ol class="messages" :aria-label="isArticle ? 'Discussion Posts' : 'Messages'">
      <template v-for="message in filteredReplies" :key="message.id"><li v-if="message.id === firstUnread" class="new-since-divider">New since your last visit</li><li :id="message.id" class="message" :class="{ 'discussion-opening': !isArticle && message.id === openingPost?.id, removed: message.removed, 'is-nested': !!message.content.replyTo && message.content.replyTo !== openingPost?.id, 'message-anchor': anchorPost === message.id }">
        <ParticipantAvatar class="message-avatar" :participant="resolved[message.authorParticipantId]" />
        <header class="message-head">
          <strong><a v-if="authorHref(message)" class="participant-name" :href="authorHref(message)">{{ authorLabel(message) }}</a><template v-else>{{ authorLabel(message) }}</template></strong><span v-if="resolved[message.authorParticipantId]?.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span>
          <time :datetime="message.content.createdAt" :title="directoryDate(message.content.createdAt)">{{ directoryRelativeDate(message.content.createdAt) }}</time>
          <span v-if="message.editedAt" class="message-edited">Edited</span>
        </header>
        <form v-if="editing?.id === message.id" class="message-edit-form" @submit.prevent="submitEdit">
          <MarkdownComposer v-if="message.content.format === 'markdown'" v-model="editText" :tangent-key="selectedTopic.tangentKey" :topic-key="selectedTopic.key" :identity="(site.site?.participant?.participantRef ?? '') + ':' + selectedTopic.key + ':' + message.id" label="Edit Post" @busy="editUpload = $event" />
          <template v-else>
            <label class="visually-hidden" :for="'edit-' + message.id">Edit Post</label>
            <textarea :id="'edit-' + message.id" v-model="editText" class="input" rows="5" aria-describedby="edit-format-hint"></textarea>
          </template>
          <span id="edit-format-hint" class="hint">{{ message.content.format === 'markdown' ? 'Markdown formatting is preserved.' : 'Plain text formatting is preserved.' }} <span v-if="editRemainingBytes < 1024" :class="{ error: editRemainingBytes < 0 }">{{ editRemainingBytes < 0 ? 'Too long by ' + -editRemainingBytes : editRemainingBytes + ' bytes left' }}</span></span>
          <button class="btn btn-primary" type="submit" :disabled="editUpload || savingEdit || editRemainingBytes < 0 || !mayEdit(message)">{{ editUpload ? 'Uploading image…' : savingEdit ? 'Saving…' : 'Save' }}</button>
          <button class="btn btn-quiet" type="button" @click="closeEdit">Cancel</button>
        </form>
        <p v-else-if="message.removed" class="message-text">[removed]</p>
        <PostContent v-else class="message-text" :text="message.content.text" :format="message.content.format" :html="message.content.html" :facets="message.facets" :resolved="resolved" />
        <SourceAttribution :post-id="message.id" :source-uri="message.sourceUri"/><footer class="message-footer">
          <button v-if="canWrite && !message.removed" class="btn btn-quiet message-reply" type="button" @click="startReply(message)">Reply</button>
          <button v-if="isOwnPost(message)" class="btn btn-quiet message-edit-trigger" type="button" :disabled="!mayEdit(message)" :aria-describedby="!mayEdit(message) ? 'message-edit-reason-' + message.id : undefined" :title="mayEdit(message) ? 'Edit Post' : editUnavailableReason()" @click="startEdit(message)">Edit</button>
          <span v-if="isOwnPost(message) && !mayEdit(message)" :id="'message-edit-reason-' + message.id" class="message-edit-unavailable">{{ editUnavailableReason() }}</span>
          <PostLinkButton :tangent-key="selectedTopic.tangentKey" :post-id="message.id" />
          <HistoryDisclosure v-if="!message.removed && (message.editedAt || message.changeId)" :conversation="conversation" :message="message" />
          <span v-else-if="message.editedAt" class="message-edited">Edited</span>
        </footer>
        <details class="message-menu">
          <summary aria-label="Actions for this post">···</summary>
          <div class="message-menu-panel">
            <button v-if="!message.removed && moderation.allowed(message, 'reportPost')" class="btn btn-quiet" type="button" @click="moderation.openReport(message.id, authorLabel(message))">Report post</button>
            <button v-if="mayPublish(message)" class="btn btn-quiet" type="button" @click="publishingPost = message.id">Publish to Bluesky</button>
            <button v-if="mayDelete(message) && deleting !== message.id" class="btn btn-quiet message-danger" type="button" @click="deleting = message.id">Delete post</button>
            <div v-if="deleting === message.id" class="stack">
              <p>Remove this Post from the conversation?</p>
              <button class="btn btn-quiet message-danger" type="button" @click="deleteMessage(message)">Remove Post</button>
              <button class="btn btn-quiet" type="button" @click="deleting = null">Keep Post</button>
            </div>
          </div>
        </details>
        <BlueskyPublishPanel v-if="publishingPost === message.id && mayPublish(message)" :key="message.id + ':' + site.site?.participant?.did"
          :post-id="message.id" :account-did="site.site!.participant!.did!" :account-label="site.site?.participant?.handle ? '@' + site.site.participant.handle : site.displayName"
          @close="publishingPost = null" />
      </li><li v-if="!isArticle && message.id === openingPost?.id" class="discussion-replies-heading"><h2>Replies <span>{{ replyCount }}</span></h2><ReplyFilters v-model="replyFilter" /></li></template>
    </ol>
    <p v-if="!filteredReplies.some(message => message.id !== openingPost?.id && message.sequence !== 1)" class="hint replies-empty">{{ replyCount ? 'No replies in this view.' : 'No replies yet.' }}</p>
    <button v-if="moreMessages || newMessages" class="btn btn-quiet" type="button" :disabled="loadingHistory" @click="readMore">{{ loadingHistory ? 'Loading…' : newMessages ? 'Read new Posts' : 'Read next page' }}</button>
    <p v-if="!canWrite && selectedTopic.canRead && !articleSettingsOpen" class="hint reply-unavailable">
      {{ replyUnavailable }} <a v-if="!site.site?.participant && !selectedTopic.isLocked" :href="replySignInHref()" @click="prepareReplySignIn">Sign in</a>
    </p>
    <form v-if="canWrite" ref="composerElement" class="compose" :class="{ 'article-compose': isArticle, 'quick-compose': draftFormat === 'plain' && !replyTarget }" @submit.prevent="submitPost" @keydown.ctrl.enter.prevent="submitPost" @keydown.meta.enter.prevent="submitPost">
      <p v-if="conversation.pendingPost.value" class="hint" role="status">A submitted Post is awaiting confirmation. Confirm it with the same author and destination; any new draft edits stay here. <button class="btn btn-quiet" type="button" :disabled="posting || composerUpload" @click="confirmSubmittedPost">Confirm submitted Post</button></p>
      <div class="composer-who"><ParticipantAvatar :participant="{ displayName: site.displayName }" /><span>Replying as <strong>{{ composerHandle }}</strong> · {{ audienceLabel }}</span></div>
      <div v-if="replyTarget" class="reply-strip"><strong>Reply to {{ authorLabel(replyTarget) }}</strong><blockquote>{{ replyTarget.content.text.slice(0, 180) }}{{ replyTarget.content.text.length > 180 ? '…' : '' }}</blockquote><button class="btn btn-quiet" type="button" @click="closeReply">Cancel reply</button></div>
      <MarkdownComposer v-if="draftFormat === 'markdown'" v-model="draft" :tangent-key="selectedTopic.tangentKey" :topic-key="selectedTopic.key" :identity="(site.site?.participant?.participantRef ?? '') + ':' + selectedTopic.key + ':' + (replyTarget?.id ?? 'new')" :label="replyTarget ? 'Your reply' : 'Write a reply…'" @busy="composerUpload = $event" />
      <template v-else>
      <label for="message-text" :class="{ 'visually-hidden': isArticle }">{{ replyTarget ? 'Your reply' : 'Write a reply…' }}</label>
      <textarea
        id="message-text"
        ref="composerField"
        v-model="composerText"
        class="input"
        :rows="isArticle ? 2 : 4"
        required
        :placeholder="isArticle ? 'Write a reply…' : 'Write a reply…'"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="mentionOpen"
        aria-controls="mention-popup"
        @input="onComposerInput"
        @keydown="onComposerKeydown"
        @blur="onMentionBlur"
      ></textarea>
      <div id="mention-popup" class="mention-popup" role="listbox" aria-label="Mention someone" :hidden="!mentionOpen">
        <div
          v-for="(entry, index) in mentionTargets"
          :id="'mention-option-' + index"
          :key="entry.insert + index"
          class="mention-row"
          :class="{ active: index === mentionActive }"
          role="option"
          :aria-selected="index === mentionActive"
          @mousedown.prevent
          @click="selectMention(index)"
        >
          <span class="mention-label">@{{ entry.label }}</span>
          <span class="mention-meta">{{ entry.kind === 'group' ? (entry.count != null ? entry.count + ' member' + (entry.count === 1 ? '' : 's') : 'role group') : [entry.badge, entry.classification].filter(Boolean).join(' · ') || 'participant' }}</span>
        </div>
      </div>
      </template>
      <div class="compose-footer">
        <button class="btn btn-quiet format-toggle" type="button" :aria-pressed="draftFormat === 'markdown'" aria-label="Formatting" :disabled="composerUpload" @click="draftFormat = draftFormat === 'markdown' ? 'plain' : 'markdown'">Aa</button>
        <label class="composer-publish"><input v-model="alsoPublish" type="checkbox" :disabled="!selectedTopic.publiclyReadable || !blueskyReady || posting" aria-describedby="composer-publish-note">Also post to Bluesky</label>
        <span v-if="remainingBytes < 400" class="hint" :class="{ error: remainingBytes < 0 }">{{ remainingBytes < 0 ? 'Too long by ' + -remainingBytes + ' bytes' : remainingBytes + ' bytes left' }}</span>
        <button class="btn btn-primary" type="submit" :aria-label="composerUpload ? 'Uploading image' : posting ? 'Posting reply' : 'Reply'" :disabled="composerUpload || posting || remainingBytes < 0 || !draft.trim()"><span>{{ composerUpload ? 'Uploading image…' : posting ? 'Posting…' : 'Reply' }}</span><svg class="quick-send-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M10 16V4.5M5 9.5l5-5 5 5"/></svg></button>
      </div>
      <p id="composer-publish-note" class="hint composer-publish-note">{{ !selectedTopic.publiclyReadable ? 'Only public posts can go to Bluesky.' : !blueskyReady ? 'Connect Bluesky to publish there.' : 'An optional public copy, in your Bluesky account.' }} <a v-if="selectedTopic.publiclyReadable && !blueskyReady && blueskyConnect" :href="blueskyConnect">Connect Bluesky</a></p>
      <p v-if="publicationStatus" class="hint" role="status">{{ publicationStatus }} <button v-if="retryPublication" class="btn btn-quiet" type="button" @click="publishReply(retryPublication)">Retry Bluesky publication</button></p>
    </form>
    </div>
    <div v-if="isArticle" class="article-tail"><p v-if="conversation.status.value" class="hint" role="status">{{ conversation.status.value }}</p><a :href="'/t/' + encodeURIComponent(selectedTopic.tangentKey) + '/topics'">More from {{ tangentName || selectedTopic.tangentKey }} →</a></div>
  </div>
</template>
<style>
.quick-send-icon{display:none}
.participant-name{color:inherit;text-decoration:none}.participant-name:hover{text-decoration:underline;text-underline-offset:3px}
body:has(.conversation.composer-ready) .site-footer{display:none}
@media(max-width:719px){
  .compose.quick-compose{display:grid;grid-template-columns:30px minmax(0,1fr) 44px;column-gap:10px;row-gap:6px;padding-top:8px}
  .quick-compose .composer-who,.quick-compose .compose-footer{display:contents}
  .quick-compose .composer-who>span{grid-row:1;grid-column:1/-1;font:11.5px/1.5 var(--ts-font-ui);color:var(--ts-text-3)}
  .quick-compose .composer-who>.participant-avatar{grid-row:2;grid-column:1;align-self:center;width:30px;height:30px;font-size:14px}
  .conversation .compose.quick-compose>textarea{grid-row:2;grid-column:2;margin:0;min-height:44px;height:44px;padding:9px 14px;border-radius:22px;resize:none;overflow:hidden}
  .quick-compose .compose-footer>.btn-primary{grid-row:2;grid-column:3;align-self:end;width:44px;height:44px;min-height:44px;padding:0;border-radius:50%;display:grid;place-items:center;margin:0}
  .quick-compose .compose-footer>.btn-primary>span{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}
  .quick-compose .quick-send-icon{display:block;width:18px;height:18px}
  .quick-compose .format-toggle{display:none;grid-column:1;grid-row:3;padding:0;min-width:30px}
  .quick-compose .composer-publish{grid-row:3;grid-column:2/-1;align-self:center;margin:0}
  .quick-compose:focus-within .format-toggle{display:block}
  .conversation .compose.quick-compose:focus-within>textarea{height:88px;min-height:88px;border-radius:12px;overflow:auto}
  .quick-compose>.hint,.quick-compose .compose-footer>.hint,.quick-compose>.mention-popup{grid-column:1/-1;margin:0}
}
</style>
