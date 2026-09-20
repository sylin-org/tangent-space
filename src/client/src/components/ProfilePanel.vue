<script setup lang="ts">
// The participant profile, ported from profile.js (ADR 0008): identity card,
// roles, recent posts, and the viewer's permitted actions — all served by the
// experience API. Facet-aware post text lands with the facets unit.
import { computed, onMounted, onScopeDispose, reactive, ref, watch } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { rolesSession } from '../composables/roles-api'

const props = defineProps<{ site: SiteStore; router: RouterStore }>()

interface ProfileRole {
  scope: string
  role: string
  label?: string
}

interface ProfilePost {
  createdAt: string
  url?: string
  text: string
  removed?: boolean
  facets?: unknown
}

interface ProfileData {
  participantRef?: string
  did?: string
  handle?: string
  displayName?: string
  description?: string
  avatar?: string
  classification?: string
  suspended?: boolean
  self?: boolean
  joinedAt?: string
  roles?: ProfileRole[]
  posts?: ProfilePost[]
  morePosts?: boolean
}

interface ProfileEnvelope {
  status?: string
  result?: { data?: ProfileData }
  actions?: Array<{ name: string; label: string; targetRef?: string }>
}

interface ManagedRole {
  id: string
  name: string
  color?: string
  purpose?: string
  canToggle?: boolean
  isMember?: boolean
  isProtected?: boolean
  isSelfEnrollable?: boolean
}

const data = ref<ProfileData | null>(null)
const envelope = ref<ProfileEnvelope | null>(null)
const failedMessage = ref('')
const loading = ref(false)
const activeTab = ref('posts')
let revision = 0

const state = reactive({
  roleItems: [] as ManagedRole[],
  roleStatus: '',
  roleStatusError: false,
})

const name = computed(() => (data.value && (data.value.displayName || data.value.handle)) || (failedMessage.value ? 'Profile unavailable' : 'Getting acquainted…'))
const cleanHandle = computed(() => (data.value?.handle || '').replace(/^@/, ''))
const classification = computed(() => String(data.value?.classification || 'undeclared').toLowerCase())
const identityClassification = computed(() => {
  if (!data.value) return ''
  const label = classification.value === 'agent' ? 'Agent' : classification.value === 'human' ? 'Human' : 'Not declared (hidden)'
  return label + (data.value.suspended ? ' · suspended' : '') + (data.value.self ? ' · this is you' : '')
})
const joinedLine = computed(() => {
  const meta: string[] = []
  if (data.value?.joinedAt) meta.push('Joined ' + new Date(data.value.joinedAt).toLocaleDateString())
  if (data.value?.self) meta.push('this is you')
  if (data.value?.suspended) meta.push('suspended')
  return meta.join(' · ')
})

function formatRoleLabel(role: ProfileRole): string {
  if (role.scope === 'server') {
    if (role.role === 'owner') return 'Server Owner'
    if (role.role === 'participant') return 'Participant'
    if (role.role === 'administrator') return 'Administrator'
    return role.role.charAt(0).toUpperCase() + role.role.slice(1)
  }
  const rolePart = role.role.charAt(0).toUpperCase() + role.role.slice(1)
  return role.label ? `${role.label} · ${rolePart}` : rolePart
}

const roleChips = computed(() => (data.value?.roles ?? []).map(role => ({ key: role.scope + ':' + role.role, label: formatRoleLabel(role), role: role.role })))

const visibleManagedRoles = computed(() =>
  state.roleItems.filter(role => props.site.site?.participant?.isOwner === true || role.canToggle || role.isMember),
)

const isSelfViewer = computed(() => data.value?.self === true)
const ownerViewer = computed(() => props.site.site?.participant?.isOwner === true)

const setRoleActions = computed(() =>
  (envelope.value?.actions ?? [])
    .filter(action => action.name === 'set_role' && action.targetRef && data.value?.did)
    .map(action => {
      const key = action.targetRef!.split('::')[1]
      return key
        ? { label: action.label, href: '/t/' + encodeURIComponent(key) + '/topics?participant=' + encodeURIComponent(data.value!.did!) + '#tangent-members' }
        : null
    })
    .filter((entry): entry is { label: string; href: string } => entry !== null),
)

async function show() {
  const version = ++revision
  const current = props.router.route
  if (current.kind !== 'participant') return
  loading.value = true
  failedMessage.value = ''
  data.value = null
  envelope.value = null
  document.title = 'Participant · Tangent'
  activeTab.value = new URL(window.location.href).searchParams.get('tab') || 'posts'
  try {
    const response = await fetch('/api/participants/' + encodeURIComponent(current.identifier) + '/profile', {
      headers: { accept: 'application/json' },
      credentials: 'same-origin',
    })
    if (version !== revision) return
    if (!response.ok) throw Object.assign(new Error('Profile request failed'), { status: response.status })
    const payload = (await response.json()) as ProfileEnvelope
    if (payload?.status === 'blocked') {
      fail('No participant is visible under that address.')
      return
    }
    if (!payload?.result?.data) {
      fail('The profile could not be loaded.')
      return
    }
    envelope.value = payload
    data.value = payload.result.data ?? null
    const canonical = '/u/' + encodeURIComponent(data.value!.handle || data.value!.did || 'tangent:local:' + data.value!.participantRef)
    if (canonical !== window.location.pathname) props.router.canonicalize(canonical)
    document.title = name.value + ' · Tangent'
    void loadManagedRoles()
  } catch (error) {
    if (version !== revision) return
    const status = (error as Error & { status?: number }).status
    fail(status === 403 || status === 404 ? 'No participant is visible under that address.' : 'The profile could not be loaded.')
  } finally {
    if (version === revision) loading.value = false
  }
}

function fail(message: string) {
  failedMessage.value = message
  data.value = null
  envelope.value = null
  state.roleItems = []
  document.title = 'Profile unavailable · Tangent'
}

async function loadManagedRoles() {
  const participantRef = data.value?.participantRef
  if (!participantRef || !props.site.site?.participant) {
    state.roleItems = []
    return
  }
  state.roleStatus = ''
  state.roleStatusError = false
  try {
    const response = await fetch('/api/roles/ui/participant/' + encodeURIComponent(participantRef) + '/roles', {
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    })
    if (!response.ok) throw new Error('Could not load roles.')
    state.roleItems = (await response.json()) as ManagedRole[]
  } catch (error) {
    state.roleStatus = (error as Error).message
    state.roleStatusError = true
    state.roleItems = []
  }
}

async function toggleRole(role: ManagedRole) {
  const participantRef = data.value?.participantRef
  if (!participantRef) return
  const willAdd = !role.isMember
  state.roleStatus = ''
  state.roleStatusError = false
  try {
    const token = await rolesSession()
    const response = await fetch(
      '/api/roles/ui/participant/' + encodeURIComponent(participantRef) + '/roles/' + encodeURIComponent(role.id),
      {
        method: willAdd ? 'PUT' : 'DELETE',
        credentials: 'same-origin',
        headers: { Accept: 'application/json', [token.headerName]: token.requestToken },
      },
    )
    if (!response.ok) {
      const errData = await response.json().catch(() => ({}))
      throw new Error((errData as Record<string, string>).message || (errData as Record<string, string>).error || 'Failed to update role membership.')
    }
    role.isMember = willAdd
    state.roleStatus = willAdd
      ? (isSelfViewer.value && !ownerViewer.value ? `Joined ${role.name}.` : `${role.name} role granted.`)
      : (isSelfViewer.value && !ownerViewer.value ? `Left ${role.name}.` : `${role.name} role removed.`)
    // Keep the header chips in step with the membership.
    const roles = (data.value?.roles ?? []).slice()
    const roleKey = role.id.replace(/^role:/, '')
    const without = roles.filter(entry => !(entry.scope === 'server' && entry.role === roleKey))
    if (willAdd) without.push({ scope: 'server', role: roleKey, label: 'This server' })
    if (data.value) data.value.roles = without
  } catch (error) {
    state.roleStatus = (error as Error).message
    state.roleStatusError = true
  }
}

function roleToggleLabel(role: ManagedRole): string {
  if (isSelfViewer.value && !ownerViewer.value) return role.isMember ? 'Leave role' : 'Join role'
  return role.isMember ? 'Remove' : 'Add'
}

function roleToggleTitle(role: ManagedRole): string {
  if (isSelfViewer.value && !ownerViewer.value) return role.isMember ? `Leave ${role.name}` : `Join ${role.name}`
  return role.isMember ? `Remove ${role.name} from this participant` : `Add ${role.name} to this participant`
}

async function setClassification(value: 'Agent' | 'Human' | 'Undeclared') {
  classificationMenu.value!.open = false
  try {
    const response = await fetch('/api/participants/me', {
      method: 'PATCH',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ classification: value }),
    })
    if (!response.ok) throw new Error('Could not update classification')
    await show()
  } catch {
    // The badge stays as it was; a reload shows the stored value.
  }
}

const classificationMenu = ref<HTMLDetailsElement | null>(null)

function onDocumentClick(event: MouseEvent) {
  const menu = classificationMenu.value
  if (menu?.open && !menu.contains(event.target as Node)) menu.open = false
}

function profileTab(name: string, updateUrl = true) {
  if (!['posts', 'roles', 'identity'].includes(name)) name = 'posts'
  activeTab.value = name
  if (updateUrl) {
    const url = new URL(window.location.href)
    if (name !== 'posts') url.searchParams.set('tab', name)
    else url.searchParams.delete('tab')
    window.history.replaceState({ ...(window.history.state || {}) }, '', url.pathname + url.search + url.hash)
  }
}

function onTabKeydown(event: KeyboardEvent, tab: string) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const tabs = ['posts', 'roles', 'identity']
  const index = tabs.indexOf(tab)
  let target: string
  if (event.key === 'Home') target = tabs[0]!
  else if (event.key === 'End') target = tabs[tabs.length - 1]!
  else {
    const direction = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1
    target = tabs[(index + direction + tabs.length) % tabs.length]!
  }
  profileTab(target)
  document.querySelector<HTMLButtonElement>(`[data-profile-tab="${target}"]`)?.focus()
}

function postHref(post: ProfilePost): string {
  try {
    const target = new URL(post.url || '/', window.location.origin)
    return target.origin === window.location.origin ? target.pathname : '/tangents/'
  } catch {
    return '/tangents/'
  }
}

function avatarFallback(event: Event) {
  // A broken avatar falls back to the letter.
  (event.target as HTMLImageElement).remove()
}

function onPopstate() {
  if (props.router.route.kind === 'participant') {
    profileTab(new URL(window.location.href).searchParams.get('tab') || 'posts', false)
  }
}

watch(() => props.router.route, () => {
  void show()
})

watch(() => props.site.state, stateNow => {
  if (stateNow !== 'loading') void show()
})

onMounted(() => {
  document.addEventListener('click', onDocumentClick, { passive: true })
  window.addEventListener('popstate', onPopstate)
  if (props.site.state !== 'loading' && props.router.route.kind === 'participant') void show()
})

onScopeDispose(() => {
  document.removeEventListener('click', onDocumentClick)
  window.removeEventListener('popstate', onPopstate)
})
</script>

<template>
  <nav class="breadcrumbs" aria-label="Breadcrumb"><a href="/">Home</a><span aria-current="page">Participant</span></nav>
  <header class="profile-card">
    <div id="profile-avatar" class="profile-avatar" aria-hidden="true">
      <img v-if="data?.avatar && data.avatar.startsWith('/api/profile-cache/avatar?')" :src="data.avatar" alt="" referrerpolicy="no-referrer" @error="avatarFallback">
      <template v-else>{{ name.slice(0, 1).toUpperCase() }}</template>
    </div>
    <div class="profile-intro">
      <p class="eyebrow">Around the table</p>
      <h1 id="profile-name" tabindex="-1">{{ name }}</h1>
      <p id="profile-handle" class="profile-handle">
        <a v-if="cleanHandle" class="profile-handle-link" :href="'https://bsky.app/profile/' + encodeURIComponent(cleanHandle)" target="_blank" rel="noopener noreferrer" title="View on Bluesky"><span class="profile-handle-butterfly">🦋</span>@{{ cleanHandle }}</a>
        <template v-else-if="data">An account in this community</template>
      </p>
      <p id="profile-description" class="profile-description" :hidden="!data?.description">{{ data?.description }}</p>
      <div id="profile-pills" class="profile-pills">
        <span id="profile-classification">
          <details v-if="data?.self" ref="classificationMenu" class="profile-classification-menu">
            <summary class="profile-classification-badge" :class="classification === 'agent' ? 'agent' : classification === 'human' ? 'human' : 'undeclared'" title="Change how your account is identified">
              <span class="badge-text">{{ classification === 'agent' ? '🤖 Agent' : classification === 'human' ? '👤 Human' : '+ Add badge' }}</span>
              <span class="badge-caret"> ▾</span>
            </summary>
            <div class="profile-classification-popover">
              <button v-for="opt in (['Agent', 'Human', 'Undeclared'] as const)" :key="opt" type="button" class="profile-classification-option" :class="{ active: opt.toLowerCase() === classification }" @click="setClassification(opt)">
                {{ opt === 'Agent' ? '🤖 Agent' : opt === 'Human' ? '👤 Human' : 'Hide badge' }}
              </button>
            </div>
          </details>
          <template v-else-if="data">
            <span v-if="classification === 'agent'" class="profile-classification-badge agent">🤖 Agent</span>
            <span v-else-if="classification === 'human'" class="profile-classification-badge human">👤 Human</span>
          </template>
        </span>
        <div id="profile-roles" class="profile-roles">
          <template v-if="failedMessage">{{ failedMessage }}</template>
          <template v-else>
            <span v-for="chip in roleChips" :key="chip.key" class="role-chip" :class="'role-' + chip.role">{{ chip.label }}</span>
          </template>
        </div>
      </div>
      <p id="profile-joined" class="hint">{{ joinedLine }}</p>
    </div>
  </header>
  <div id="profile-actions" class="profile-actions">
    <button v-if="failedMessage" class="btn btn-quiet" type="button" @click="show">Try again</button>
    <template v-else>
      <a v-for="action in setRoleActions" :key="action.href" class="btn btn-quiet" :href="action.href">{{ action.label }}</a>
    </template>
  </div>
  <div id="profile-shell" class="profile-shell">
    <nav class="profile-tabs" role="tablist" aria-label="Participant profile">
      <button id="profile-posts-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-posts-panel" :aria-selected="activeTab === 'posts'" data-profile-tab="posts" @click="profileTab('posts')" @keydown="onTabKeydown($event, 'posts')"><span aria-hidden="true">&#9670;</span><span><strong>Posts</strong><small>Recent contributions</small></span></button>
      <button id="profile-roles-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-roles-panel" :aria-selected="activeTab === 'roles'" data-profile-tab="roles" @click="profileTab('roles')" @keydown="onTabKeydown($event, 'roles')"><span aria-hidden="true">&#9672;</span><span><strong>Roles</strong><small>Groups and permissions</small></span></button>
      <button id="profile-identity-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-identity-panel" :aria-selected="activeTab === 'identity'" data-profile-tab="identity" @click="profileTab('identity')" @keydown="onTabKeydown($event, 'identity')"><span aria-hidden="true">&#9673;</span><span><strong>Identity</strong><small>Account and DID</small></span></button>
    </nav>
    <div class="profile-tab-content">
      <section id="profile-posts-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-posts-tab" :hidden="activeTab !== 'posts'">
        <header class="profile-panel-heading"><div><p class="eyebrow">CONVERSATION</p><h3>Recent posts</h3><p class="hint">Contributions by this participant across the server.</p></div></header>
        <div class="profile-panel-body">
          <ul id="profile-posts" class="profile-posts">
            <li v-if="!data" class="hint"></li>
            <template v-else>
              <li v-for="(post, index) in data.posts ?? []" :key="index" class="profile-post">
                <div class="profile-post-byline">
                  {{ new Date(post.createdAt).toLocaleString() }}
                  <a :href="postHref(post)">Open in Topic</a>
                </div>
                <p v-if="post.removed" class="profile-post-text">This post was removed.</p>
                <p v-else class="profile-post-text">{{ post.text }}</p>
              </li>
              <li v-if="!(data.posts ?? []).length" class="hint">No visible posts yet.</li>
              <li v-if="data.morePosts" class="hint">Showing recent posts only.</li>
            </template>
          </ul>
        </div>
      </section>
      <section id="profile-roles-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-roles-tab" :hidden="activeTab !== 'roles'">
        <header class="profile-panel-heading"><div><p class="eyebrow">AROUND THE TABLE</p><h3>Server groups and roles</h3><p class="hint">Roles shape visibility, permissions, and group participation.</p></div></header>
        <div class="profile-panel-body">
          <div id="profile-role-management" class="profile-role-management">
            <p v-if="!site.site?.participant" class="hint">Server roles assigned to this participant are shown in their profile header. Sign in to view and manage roles.</p>
            <template v-else-if="data?.participantRef">
              <div class="profile-role-list">
                <p v-if="!visibleManagedRoles.length" class="hint">No visible server roles.</p>
                <div v-for="role in visibleManagedRoles" :key="role.id" class="profile-role-row" :style="{ '--role-color': role.color || '#a98df0' }">
                  <span class="role-list-dot"></span>
                  <div class="profile-role-info">
                    <div class="profile-role-name-row">
                      <strong>{{ role.name }}</strong>
                      <span v-if="role.isProtected" class="profile-role-badge system">System</span>
                      <span v-else-if="role.isSelfEnrollable" class="profile-role-badge self-enrollable">Self-enrollable</span>
                    </div>
                    <small>{{ role.purpose || (role.isProtected ? 'System-managed role' : '') }}</small>
                  </div>
                  <div class="profile-role-actions">
                    <button
                      v-if="role.canToggle"
                      type="button"
                      :class="role.isMember ? 'btn btn-quiet profile-role-toggle in-role' : 'btn btn-primary profile-role-toggle'"
                      :title="roleToggleTitle(role)"
                      :aria-label="roleToggleTitle(role)"
                      @click="toggleRole(role)"
                    >{{ roleToggleLabel(role) }}</button>
                    <span v-else-if="role.isMember" class="profile-role-badge">Assigned</span>
                  </div>
                </div>
              </div>
              <p id="profile-role-status" class="hint" role="status" :class="{ error: state.roleStatusError }">{{ state.roleStatus }}</p>
            </template>
          </div>
        </div>
      </section>
      <section id="profile-identity-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-identity-tab" :hidden="activeTab !== 'identity'">
        <header class="profile-panel-heading"><div><p class="eyebrow">ACCOUNT</p><h3>Account identity</h3><p class="hint">Cryptographic identifier and account details.</p></div></header>
        <div class="profile-panel-body">
          <div class="profile-identity-details">
            <label><span>Decentralized Identifier (DID)</span><code id="profile-did" class="did">{{ data?.did || '—' }}</code></label>
            <label><span>Handle</span><code id="profile-identity-handle" class="did">
              <a v-if="cleanHandle" class="profile-handle-link" :href="'https://bsky.app/profile/' + encodeURIComponent(cleanHandle)" target="_blank" rel="noopener noreferrer" title="View on Bluesky"><span class="profile-handle-butterfly">🦋</span>@{{ cleanHandle }}</a>
              <template v-else>—</template>
            </code></label>
            <label><span>Account type</span><span id="profile-identity-classification" class="hint">{{ identityClassification }}</span></label>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
