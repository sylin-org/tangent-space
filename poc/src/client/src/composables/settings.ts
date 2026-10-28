import { computed, reactive, ref, shallowRef, watch } from 'vue'
import type { RouterStore, WorkspaceRoute } from '../stores/router'
import type { ServerView } from '../stores/site'
import type { useSite } from '../stores/site'
import { clearRolesSession, rolesRequest } from './roles-api'

export type SiteStore = ReturnType<typeof useSite>

// The settings page coordinates context, resource, tabs, roles, and server identity.

export interface SettingsDecision {
  key: string
  label: string
  description: string
  bypass: string
}

export interface SettingsContext {
  kind: 'server' | 'tangent' | 'topic'
  tangent?: string
  topic?: string
  title: string
  eyebrow: string
  intro: string
  accessPath: string
  decisions: SettingsDecision[]
}

export interface TangentView {
  key: string
  kind?: 'discussions' | 'articles'
  name?: string
  description?: string
  motto?: string
  accent?: string
  artwork?: string
  admission?: 'Open' | 'Approval'
  canManageAdmission?: boolean
  canManage?: boolean
  isOwner?: boolean
}

export interface TopicView {
  tangentKey: string
  key: string
  title?: string
  presentation?: 'conversation' | 'article'
  topic?: string
  allowPostEditing?: boolean
  isLocked?: boolean
  canManage?: boolean
  readAudience?: string
  publiclyReadable?: boolean
}

export interface SettingsResource {
  name: string
  description: string
  open: string
  canManage: boolean
  tangent?: TangentView
  topic?: TopicView
}

export type SettingsTabName = 'server' | 'appearance' | 'context' | 'roles' | 'access' | 'people' | 'sources' | 'storage'

export function createSettings(site: SiteStore, router: RouterStore) {
  const context = shallowRef<SettingsContext | null>(null)
  const resource = shallowRef<SettingsResource | null>(null)
  const shellVisible = ref(false)
  const peopleOnly = computed(() => context.value?.kind === 'server' && site.server?.canManage !== true && site.server?.canManageParticipants === true)
  const actorKey = computed(() => site.site?.participant?.participantRef || '')
  const loadedActor = ref('')
  const loading = ref(false)
  const pageKind = ref<SettingsContext['kind']>('server')
  const denied = ref('')
  const activeTab = ref<SettingsTabName>('server')
  const tabsVisible = reactive({ server: true, context: false, roles: true, access: true, people: true })
  const heading = reactive({
    title: 'Space settings',
    eyebrow: 'MAKE YOURSELF AT HOME',
    intro: 'Name your place, set the mood, and decide who can help.',
  })
  const breadcrumbs = ref<Array<{ label: string; href: string | null }>>([
    { label: 'Home', href: '/' },
    { label: 'Space settings', href: null },
  ])
  const accessBadge = ref('')
  const accessIntro = ref('Choose who can discover this place and what they may do here.')
  const serverStatus = ref('')
  let openToken = 0

  // The URL selects a tab when that tab belongs to the current settings scope.
  function requestedTab(contextKind: SettingsContext['kind']): SettingsTabName {
    const requested = new URL(window.location.href).searchParams.get('tab')
    const fallback: SettingsTabName = contextKind === 'server' ? 'server' : 'context'
    if (!requested) return fallback
    if (requested === 'access') return 'access'
    if (requested === 'people' && contextKind === 'tangent') return 'people'
    return contextKind === 'server' && ['appearance', 'roles', 'people', 'sources', 'storage'].includes(requested) ? (requested as SettingsTabName) : fallback
  }

  function contextFor(route: WorkspaceRoute): SettingsContext | null {
    if (route.kind === 'settings') {
      return {
        kind: 'server',
        title: 'Space settings',
        eyebrow: 'MAKE YOURSELF AT HOME',
        intro: 'Name your place, shape its roles, and decide who can enter.',
        accessPath: '/api/server/access',
        decisions: [
          { key: 'see', label: 'Who can enter', description: 'Choose who can discover and open this Space.', bypass: 'The Owner and roles with permission to read this Space can always enter.' },
          { key: 'createTangents', label: 'Who can create Tangents', description: 'Choose who may start a new Tangent here.', bypass: 'Owner and roles with Create Tangents anywhere can always create one.' },
          { key: 'manage', label: 'Who can manage', description: 'Choose who may change Space settings and access.', bypass: 'The Owner can always manage this Space.' },
        ],
      }
    }
    if (route.kind === 'tangent-settings') {
      return {
        kind: 'tangent',
        tangent: route.tangent,
        title: 'Tangent settings',
        eyebrow: 'SHAPE THIS PLACE',
        intro: 'Tune this Tangent and choose who finds a place here.',
        accessPath: '/api/v1/tangents/' + encodeURIComponent(route.tangent) + '/access',
        decisions: [
          { key: 'see', label: 'Who can see', description: 'Choose who can discover and open this Tangent.', bypass: 'Owner, Administrators, and roles with See every Tangent can always see it.' },
          { key: 'post', label: 'Who can post', description: 'Set the default for posting in Topics here.', bypass: 'Owner, Administrators, and roles with Post anywhere can always post.' },
          { key: 'createTopics', label: 'Who can create Topics', description: 'Choose who can begin a new conversation here.', bypass: 'Owner, Administrators, and roles with Create Topics anywhere can always create one.' },
          { key: 'manage', label: 'Who can manage', description: 'Choose who can change this Tangent’s details and access.', bypass: 'Owner, Administrators, and roles with Manage every Tangent can always manage it.' },
        ],
      }
    }
    if (route.kind === 'topic-settings') {
      return {
        kind: 'topic',
        tangent: route.tangent,
        topic: route.topic,
        title: 'Topic settings',
        eyebrow: 'TEND THIS CONVERSATION',
        intro: 'Keep the conversation clear, welcoming, and appropriately private.',
        accessPath: '/api/v1/tangents/' + encodeURIComponent(route.tangent) + '/topics/' + encodeURIComponent(route.topic) + '/access',
        decisions: [
          { key: 'see', label: 'Who can see', description: 'Choose who can discover and read this Topic.', bypass: 'Owner, Administrators, and roles with See every Topic can always see it.' },
          { key: 'post', label: 'Who can post', description: 'Choose who can add Posts to this Topic.', bypass: 'Owner, Administrators, and roles with Post anywhere can always post.' },
          { key: 'manage', label: 'Who can manage', description: 'Choose who can change this Topic’s details and access.', bypass: 'Owner, Administrators, and roles with Manage every Topic can always manage it.' },
        ],
      }
    }
    return null
  }

  async function resourceFor(c: SettingsContext): Promise<SettingsResource> {
    if (c.kind === 'server') {
      return {
        name: site.server?.name || site.site?.name || 'Tangent Space',
        description: site.server?.welcomeMessage || '',
        open: '/',
        canManage: site.site?.participant != null && (site.server?.canManage === true || site.server?.canManageParticipants === true),
      }
    }
    const tangent = await rolesRequest<TangentView>('/api/v1/tangents/' + encodeURIComponent(c.tangent!), { scope: 'access' })
    if (c.kind === 'tangent') {
      return {
        tangent,
        name: tangent.name || tangent.key,
        description: tangent.description ?? '',
        open: '/t/' + encodeURIComponent(tangent.key) + '/topics',
        canManage: tangent.canManage === true || tangent.isOwner === true,
      }
    }
    const topic = await rolesRequest<TopicView>(
      '/api/v1/tangents/' + encodeURIComponent(c.tangent!) + '/topics/' + encodeURIComponent(c.topic!),
      { scope: 'access' },
    )
    if (topic.tangentKey !== c.tangent) throw new Error('This Topic does not belong to this Tangent.')
    return {
      tangent,
      topic,
      name: topic.title || topic.key,
      description: topic.topic ?? '',
      open: '/t/' + encodeURIComponent(topic.tangentKey) + '/topics/' + encodeURIComponent(topic.key),
      canManage: topic.canManage === true,
    }
  }

  function configureShell(c: SettingsContext, r: SettingsResource) {
    context.value = c
    resource.value = r
    heading.title = c.title
    heading.eyebrow = c.eyebrow
    heading.intro = c.intro
    const crumbs: Array<{ label: string; href: string | null }> = [
      { label: site.server?.name || site.site?.name || 'Home', href: '/' },
    ]
    if (c.kind !== 'server') {
      crumbs.push({
        label: r.tangent?.name || r.name || c.tangent!,
        href: c.kind === 'topic' ? '/t/' + encodeURIComponent(c.tangent!) + '/topics' : null,
      })
    }
    crumbs.push({ label: c.title, href: null })
    breadcrumbs.value = crumbs
    tabsVisible.server = c.kind === 'server'
    tabsVisible.roles = c.kind === 'server'
    tabsVisible.context = c.kind !== 'server'
    tabsVisible.access = true
    accessBadge.value = c.kind === 'server' ? 'Space' : r.name + ' · ' + (c.kind === 'topic' ? 'Topic' : 'Tangent')
    accessIntro.value = c.kind === 'topic'
      ? 'Choose local roles, or keep a field inherited from this Tangent.'
      : 'Use the Space roles people already understand. Global stewards keep their built-in authority.'
  }

  /** Load the authorized settings scope for the current route and participant. */
  async function open() {
    const token = ++openToken
    const c = contextFor(router.route)
    if (!c) return
    const openingActor = actorKey.value
    loadedActor.value = ''
    context.value = null
    resource.value = null
    pageKind.value = c.kind
    heading.title = c.title
    heading.eyebrow = c.eyebrow
    heading.intro = c.intro
    shellVisible.value = false
    loading.value = true
    denied.value = ''
    clearRolesSession()
    if (!site.site?.participant) {
      shellVisible.value = false
      context.value = null
      resource.value = null
      loading.value = false
      return
    }
    try {
      if (c.kind === 'server') await site.loadServer()
      if (token !== openToken) return
      const r = await resourceFor(c)
      if (token !== openToken) return
      if (!r.canManage) throw new Error('Your current role cannot change settings here.')
      configureShell(c, r)
      loadedActor.value = openingActor
      shellVisible.value = true
      loading.value = false
      settingsTab(requestedTab(c.kind), false)
      document.title = (c.kind === 'topic' && r.topic?.presentation === 'article' ? 'Article Settings' : c.title)
        + (r.name === 'Tangent Space' ? '' : ' · ' + r.name) + ' · Tangent Space'
    } catch (error) {
      if (token !== openToken) return
      shellVisible.value = false
      context.value = null
      denied.value = (error as Error).message || 'These settings are not available.'
      loading.value = false
    }
  }

  function settingsTab(name: string, updateUrl = true) {
    if (!['server', 'appearance', 'context', 'roles', 'access', 'people', 'sources', 'storage'].includes(name)) name = 'server'
    if (peopleOnly.value) name = 'people'
    activeTab.value = name as SettingsTabName
    if (updateUrl) {
      const url = new URL(window.location.href)
      if (name !== 'server' && name !== 'context') url.searchParams.set('tab', name)
      else {
        url.searchParams.delete('tab')
        url.searchParams.delete('role')
      }
      if (name !== 'people') url.searchParams.delete('participant')
      window.history.replaceState({ ...(window.history.state || {}), tangentRoleEditor: false }, '', url.pathname + url.search + url.hash)
    }
  }

  watch([() => site.server?.canManage, () => site.server?.canManageParticipants], () => {
    if (context.value?.kind !== 'server' || !shellVisible.value) return
    if (site.server?.canManage !== true && site.server?.canManageParticipants !== true) {
      shellVisible.value = false
      denied.value = 'Your current access does not allow Space settings.'
    } else if (peopleOnly.value) settingsTab('people')
  })

  // --- The server settings form ---

  const serverForm = reactive({
    name: '',
    byline: '',
    coverImageUrl: '',
    markImageUrl: '',
    accent: 'gold',
    welcomeMessage: '',
    motd: '',
    allowAgentTangentOwnership: false,
  })
  type ServerIdentity = typeof serverForm
  const serverSaved = ref<ServerIdentity>({ ...serverForm })
  const serverSaving = ref(false)
  const serverUploading = ref(false)
  const serverStorageAvailable = ref(false)
  const serverDirty = computed(() => (Object.keys(serverSaved.value) as (keyof ServerIdentity)[])
    .some(field => serverForm[field] !== serverSaved.value[field]))
  let serverFormActor = ''
  let serverSaveToken = 0
  let serverHydrating = false
  const serverDraftKey = (actor: string) => `tangent:host-settings:${encodeURIComponent(actor)}:identity`
  const snapshotServer = (s: ServerView): ServerIdentity => ({
    name: s.name || '', byline: s.byline || '', coverImageUrl: s.coverImageUrl || '',
    markImageUrl: s.markImageUrl || '', accent: s.accent || 'gold',
    welcomeMessage: s.welcomeMessage || '', motd: s.motd || '',
    allowAgentTangentOwnership: s.allowAgentTangentOwnership === true,
  })
  function changedServerFields(): Partial<ServerIdentity> {
    return Object.fromEntries((Object.keys(serverSaved.value) as (keyof ServerIdentity)[])
      .filter(field => serverForm[field] !== serverSaved.value[field])
      .map(field => [field, serverForm[field]]))
  }
  function persistServerDraft() {
    if (serverHydrating || !actorKey.value) return
    try {
      const changed = changedServerFields()
      if (Object.keys(changed).length) sessionStorage.setItem(serverDraftKey(actorKey.value), JSON.stringify(changed))
      else sessionStorage.removeItem(serverDraftKey(actorKey.value))
      serverStorageAvailable.value = true
    } catch { serverStorageAvailable.value = false }
  }

  function fillServerForm() {
    const s: ServerView | null = site.server
    const actor = actorKey.value
    if (!s || !actor) return
    serverHydrating = true
    const changed = actor === serverFormActor ? changedServerFields() : {} as Partial<ServerIdentity>
    serverFormActor = actor
    serverSaved.value = snapshotServer(s)
    Object.assign(serverForm, serverSaved.value)
    try {
      const stored = JSON.parse(sessionStorage.getItem(serverDraftKey(actor)) || 'null') as Partial<ServerIdentity> | null
      if (stored && typeof stored === 'object') {
        for (const field of Object.keys(serverSaved.value) as (keyof ServerIdentity)[]) {
          if (typeof stored[field] === typeof serverSaved.value[field]) (changed as Record<string, unknown>)[field] = stored[field]
        }
      }
      serverStorageAvailable.value = true
    } catch { serverStorageAvailable.value = false }
    Object.assign(serverForm, changed)
    serverHydrating = false
    persistServerDraft()
  }

  watch([() => site.server, actorKey], () => {
    serverSaveToken++
    serverSaving.value = false
    fillServerForm()
  }, { immediate: true })
  watch(serverForm, () => {
    if (serverHydrating) return
    serverStatus.value = ''
    persistServerDraft()
  }, { deep: true, flush: 'sync' })

  const serverCanManage = computed(() => site.site?.participant != null && site.server?.canManage === true)

  const serverRoleSummary = computed(() => {
    const permissions = site.server?.permissions
    if (!permissions || Array.isArray(permissions)) return ''
    const labels: Record<string, string> = {
      read: 'read', reply: 'reply', manageServer: 'manage this Space', createTangent: 'create Tangents',
      manageTangent: 'manage this Tangent', createTopic: 'create Topics', manageTopic: 'manage this Topic',
      manageParticipants: 'manage participants', editOwnPost: 'edit your posts', deleteOwnPost: 'delete your posts',
      removePost: 'remove posts', reportPost: 'report posts',
    }
    return 'Your role: ' + (permissions.role ?? '') + '. You can ' + (permissions.allowedActions ?? []).map(a => labels[a] || a).join(', ') + '.'
  })

  const serverPermissionsSummary = computed(() => {
    const permissions = site.server?.permissions
    if (!permissions || !Array.isArray(permissions)) return ''
    return permissions
      .map(p => [p.role, p.scope, (p.allowedActions ?? []).join(', ')].filter(Boolean).join(': '))
      .join(' · ')
  })

  const cardImageUrl = computed(() => {
    const url = serverForm.coverImageUrl.trim()
    return (/^https:\/\//i.test(url) || /^\/(?!\/)/.test(url)) && !/[\u0000-\u001f\\]/.test(url) ? url : ''
  })

  async function saveServer() {
    if (!serverCanManage.value || !serverDirty.value || serverSaving.value || serverUploading.value || !actorKey.value) return
    if (!serverForm.name.trim()) { serverStatus.value = 'Give this Space a name.'; return }
    const submitted = changedServerFields()
    const actor = actorKey.value
    const token = ++serverSaveToken
    serverSaving.value = true
    serverStatus.value = 'Saving…'
    try {
      await rolesRequest('/api/server', { method: 'PATCH', body: submitted, scope: 'access' })
      if (token !== serverSaveToken || actor !== actorKey.value) return
      serverSaved.value = { ...serverSaved.value, ...submitted }
      persistServerDraft()
      serverStatus.value = serverDirty.value ? 'Earlier changes saved. New edits remain unsaved.' : 'Space details saved.'
      await site.loadServer()
    } catch (error) {
      if (token === serverSaveToken && actor === actorKey.value) serverStatus.value = (error as Error).message || 'Space details could not be saved.'
    } finally {
      if (token === serverSaveToken) serverSaving.value = false
    }
  }
  function discardServer() {
    if (serverSaving.value || serverUploading.value) return
    Object.assign(serverForm, serverSaved.value)
    persistServerDraft()
    serverStatus.value = ''
  }

  return {
    context, resource, peopleOnly, actorKey, loadedActor, shellVisible, loading, pageKind, denied, activeTab, tabsVisible, heading, breadcrumbs,
    accessBadge, accessIntro,
    serverForm, serverSaved, serverDirty, serverSaving, serverUploading, serverStorageAvailable,
    serverCanManage, serverRoleSummary, serverPermissionsSummary, cardImageUrl, serverStatus, saveServer, discardServer,
    requestedTab, contextFor, settingsTab,
    open,
  }
}

export type Settings = ReturnType<typeof createSettings>
