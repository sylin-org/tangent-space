import { computed, reactive, ref, shallowRef, watch } from 'vue'
import type { RouterStore, WorkspaceRoute } from '../stores/router'
import type { ServerView } from '../stores/site'
import type { useSite } from '../stores/site'
import { clearRolesSession, rolesRequest } from './roles-api'

export type SiteStore = ReturnType<typeof useSite>

// The settings page's coordination, ported from access.js (context, resource, shell),
// roles.js (tab state and the tab/role URL contract) and rooms.js (the server form).

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
  name?: string
  description?: string
  motto?: string
  accent?: string
  artwork?: string
  canManage?: boolean
  isOwner?: boolean
}

export interface TopicView {
  tangentKey: string
  key: string
  title?: string
  topic?: string
  allowPostEditing?: boolean
  isLocked?: boolean
  canManage?: boolean
}

export interface SettingsResource {
  name: string
  description: string
  open: string
  canManage: boolean
  tangent?: TangentView
  topic?: TopicView
}

export type SettingsTabName = 'server' | 'context' | 'roles' | 'access' | 'people'

export function createSettings(site: SiteStore, router: RouterStore) {
  const context = shallowRef<SettingsContext | null>(null)
  const resource = shallowRef<SettingsResource | null>(null)
  const shellVisible = ref(false)
  const denied = ref('')
  const activeTab = ref<SettingsTabName>('server')
  const tabsVisible = reactive({ server: true, context: false, roles: true, access: true, people: true })
  const heading = reactive({
    title: 'Server settings',
    eyebrow: 'MAKE YOURSELF AT HOME',
    intro: 'Name your place, set the mood, and decide who can help.',
  })
  const breadcrumbs = ref<Array<{ label: string; href: string | null }>>([
    { label: 'Home', href: '/' },
    { label: 'Server settings', href: null },
  ])
  const contextHeading = reactive({ label: 'Tangent', kicker: 'TANGENT', name: 'Loading…', copy: '' })
  const accessBadge = ref('')
  const accessIntro = ref('Choose who can discover this place and what they may do here.')
  const serverStatus = ref('')
  const contextStatus = ref('')
  const contextStatusError = ref(false)
  let openToken = 0

  const contextSignature = (c: SettingsContext | null): string =>
    c ? [c.kind, c.tangent, c.topic].filter(Boolean).join(':') : ''

  // Which tab the URL asks for, or the one this context opens on (roles.js requestedTab).
  function requestedTab(contextKind: SettingsContext['kind']): SettingsTabName {
    const requested = new URL(window.location.href).searchParams.get('tab')
    const fallback: SettingsTabName = contextKind === 'server' ? 'server' : 'context'
    if (!requested) return fallback
    if (requested === 'access') return 'access'
    // Roles and People describe the server itself, so they mean nothing in a Tangent or Topic.
    return contextKind === 'server' && ['roles', 'people'].includes(requested) ? (requested as SettingsTabName) : fallback
  }

  function contextFor(route: WorkspaceRoute): SettingsContext | null {
    if (route.kind === 'settings') {
      return {
        kind: 'server',
        title: 'Server settings',
        eyebrow: 'MAKE YOURSELF AT HOME',
        intro: 'Name your place, shape its roles, and decide who can enter.',
        accessPath: '/api/server/access',
        decisions: [
          { key: 'see', label: 'Who can enter', description: 'Choose who can discover and open this server.', bypass: 'Owner and roles with See every server can always enter.' },
          { key: 'createTangents', label: 'Who can create Tangents', description: 'Choose who may start a new Tangent here.', bypass: 'Owner and roles with Create Tangents anywhere can always create one.' },
          { key: 'manage', label: 'Who can manage', description: 'Choose who may change server settings and access.', bypass: 'The Owner can always manage this server.' },
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
        canManage: site.site?.participant?.isOwner === true,
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
    contextHeading.label = c.kind === 'topic' ? 'Topic' : 'Tangent'
    contextHeading.kicker = c.kind.toUpperCase()
    contextHeading.name = r.name
    contextHeading.copy = r.description || (c.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.')
    tabsVisible.server = c.kind === 'server'
    tabsVisible.roles = c.kind === 'server'
    tabsVisible.context = c.kind !== 'server'
    tabsVisible.access = true
    accessBadge.value = c.kind === 'server' ? 'Server' : r.name + ' · ' + (c.kind === 'topic' ? 'Topic' : 'Tangent')
    accessIntro.value = c.kind === 'topic'
      ? 'Choose local roles, or keep a field inherited from this Tangent.'
      : 'Use the server roles people already understand. Global stewards keep their built-in authority.'
    fillContextForm(c, r)
  }

  /** Everything a route change or identity change does to the settings page (access.js openFor). */
  async function open() {
    const token = ++openToken
    const c = contextFor(router.route)
    if (!c) return
    denied.value = ''
    clearRolesSession()
    if (!site.site?.participant) {
      shellVisible.value = false
      context.value = null
      resource.value = null
      return
    }
    // The owner check lands before anything loads, so the shell never flashes.
    if (c.kind === 'server') shellVisible.value = site.site.participant.isOwner === true
    try {
      if (c.kind === 'server') await site.loadServer()
      if (token !== openToken) return
      const r = await resourceFor(c)
      if (token !== openToken) return
      if (!r.canManage) throw new Error('Your current role cannot change settings here.')
      configureShell(c, r)
      shellVisible.value = true
      settingsTab(requestedTab(c.kind), false)
      document.title = c.title + ' · ' + r.name + ' · Tangent Space'
    } catch (error) {
      if (token !== openToken) return
      shellVisible.value = false
      context.value = null
      denied.value = (error as Error).message || 'These settings are not available.'
    }
  }

  function settingsTab(name: string, updateUrl = true) {
    if (!['server', 'context', 'roles', 'access', 'people'].includes(name)) name = 'server'
    activeTab.value = name as SettingsTabName
    if (updateUrl) {
      const url = new URL(window.location.href)
      if (name !== 'server' && name !== 'context') url.searchParams.set('tab', name)
      else {
        url.searchParams.delete('tab')
        url.searchParams.delete('role')
      }
      window.history.replaceState({ ...(window.history.state || {}), tangentRoleEditor: false }, '', url.pathname + url.search + url.hash)
    }
  }

  // --- The server settings form (rooms.js renderServerSettings + preview + submit) ---

  const serverForm = reactive({
    name: '',
    byline: '',
    coverImageUrl: '',
    welcomeMessage: '',
    motd: '',
    allowAgentTangentOwnership: false,
  })

  function fillServerForm() {
    const s: ServerView | null = site.server
    if (!s) return
    serverForm.name = s.name || ''
    serverForm.byline = s.byline || ''
    serverForm.coverImageUrl = s.coverImageUrl || ''
    serverForm.welcomeMessage = s.welcomeMessage || ''
    serverForm.motd = s.motd || ''
    serverForm.allowAgentTangentOwnership = s.allowAgentTangentOwnership === true
  }

  watch(() => site.server, fillServerForm, { immediate: true })

  const serverAllowedActions = computed<string[]>(() => {
    const permissions = site.server?.permissions
    return permissions && !Array.isArray(permissions) ? (permissions.allowedActions ?? []) : []
  })

  const serverCanManage = computed(
    () => site.site?.participant?.isOwner === true || serverAllowedActions.value.includes('manageServer') || serverAllowedActions.value.includes('editServer'),
  )

  const serverRoleSummary = computed(() => {
    const permissions = site.server?.permissions
    if (!permissions || Array.isArray(permissions)) return ''
    const labels: Record<string, string> = {
      read: 'read', reply: 'reply', manageServer: 'manage this server', createTangent: 'create Tangents',
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
    serverStatus.value = ''
    try {
      await rolesRequest('/api/server', { method: 'PATCH', body: { ...serverForm }, scope: 'access' })
    } catch (error) {
      if ((error as Error & { status?: number }).status === 403) {
        serverStatus.value = 'Your account can no longer change these settings.'
        window.location.assign('/')
        return
      }
      serverStatus.value = 'Settings could not be saved. Please try again.'
      return
    }
    try {
      await site.loadServer()
    } catch {
      serverStatus.value = 'Server settings could not be read.'
      return
    }
    serverStatus.value = 'Server settings saved.'
  }

  // --- The context editor (access.js renderContextEditor + submit) ---

  const contextForm = reactive({
    name: '', description: '', motto: '', accent: '#f4b942', artwork: '',
    title: '', topic: '', allowPostEditing: false, isLocked: false,
  })

  function fillContextForm(c: SettingsContext, r: SettingsResource) {
    if (c.kind === 'tangent' && r.tangent) {
      contextForm.name = r.tangent.name || ''
      contextForm.description = r.tangent.description || ''
      contextForm.motto = r.tangent.motto || ''
      contextForm.accent = r.tangent.accent || '#f4b942'
      contextForm.artwork = r.tangent.artwork || ''
    } else if (c.kind === 'topic' && r.topic) {
      contextForm.title = r.topic.title || ''
      contextForm.topic = r.topic.topic || ''
      contextForm.allowPostEditing = r.topic.allowPostEditing === true
      contextForm.isLocked = r.topic.isLocked === true
    }
  }

  async function saveContext() {
    const c = context.value
    if (!c || c.kind === 'server') return
    contextStatus.value = 'Saving…'
    contextStatusError.value = false
    try {
      const body =
        c.kind === 'tangent'
          ? {
              name: contextForm.name.trim(),
              description: contextForm.description.trim(),
              motto: contextForm.motto.trim(),
              accent: contextForm.accent,
              artwork: contextForm.artwork.trim(),
            }
          : {
              title: contextForm.title.trim(),
              topic: contextForm.topic.trim(),
              allowPostEditing: contextForm.allowPostEditing,
              isLocked: contextForm.isLocked,
            }
      const path =
        c.kind === 'tangent'
          ? '/api/v1/tangents/' + encodeURIComponent(c.tangent!)
          : '/api/v1/tangents/' + encodeURIComponent(c.tangent!) + '/topics/' + encodeURIComponent(c.topic!) + '/settings'
      await rolesRequest(path, { method: 'PATCH', body, scope: 'access' })
      if (contextSignature(c) !== contextSignature(contextFor(router.route))) return
      const r = resource.value
      if (r) {
        r.name = (body as { name?: string; title?: string }).name || (body as { title?: string }).title || r.name
        r.description = (body as { description?: string; topic?: string }).description ?? (body as { topic?: string }).topic ?? r.description
        if (c.kind === 'tangent' && r.tangent) Object.assign(r.tangent, body)
        if (c.kind === 'topic' && r.topic) Object.assign(r.topic, body)
        contextHeading.name = r.name
        contextHeading.copy = r.description || (c.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.')
      }
      contextStatus.value = (c.kind === 'topic' ? 'Topic' : 'Tangent') + ' details saved.'
    } catch (error) {
      contextStatus.value = (error as Error).message
      contextStatusError.value = true
    }
  }

  return {
    context, resource, shellVisible, denied, activeTab, tabsVisible, heading, breadcrumbs,
    contextHeading, accessBadge, accessIntro,
    serverForm, serverCanManage, serverRoleSummary, serverPermissionsSummary, cardImageUrl, serverStatus, saveServer,
    contextForm, contextStatus, contextStatusError, saveContext,
    requestedTab, contextFor, settingsTab,
    open,
  }
}

export type Settings = ReturnType<typeof createSettings>
