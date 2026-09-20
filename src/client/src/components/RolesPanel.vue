<script setup lang="ts">
// The Roles tab, ported from roles.js: the role directory with member counts and
// pagination, and the role editor — appearance, permissions, and direct members.
import { computed, onMounted, onScopeDispose, reactive, ref, shallowRef, watch } from 'vue'
import { useSite } from '../stores/site'
import type { Settings } from '../composables/settings'
import {
  clearRolesSession, normalizeRole, protectedOwnerRole, roleColor, roleKeyFor, rolesRequest,
  systemRole, type NormalizedRole, type RoleCapability, type RoleDescriptor,
} from '../composables/roles-api'

const props = defineProps<{ settings: Settings }>()
const site = useSite()
const { activeTab } = props.settings

const groupOrder = ['host', 'tangent', 'topic', 'post', 'other']
const groupLabels: Record<string, string> = { host: 'Server', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'Other' }
const rolePageSize = 100

const descriptor = shallowRef<RoleDescriptor | null>(null)
const descriptorError = ref(false)
const roles = ref<NormalizedRole[]>([])
const roleTotal = ref(0)
const roleListPage = ref(1)
const roleListHasPrevious = ref(false)
const roleListHasNext = ref(false)
const memberTotals = reactive(new Map<string, number>())
const selected = shallowRef<(NormalizedRole & { _new?: boolean }) | null>(null)
const editorTab = ref('appearance')
const dirty = ref(false)
const draftGrantKeys = reactive(new Set<string>())

const listStatus = ref('Loading roles…')
const listStatusError = ref(false)
const roleStatus = ref('')
const roleStatusError = ref(false)
const membersStatus = ref('')
const membersStatusError = ref(false)
const searchTerm = ref('')
const permissionSearch = ref('')
const memberSearchTerm = ref('')

const formName = ref('')
const formPurpose = ref('')
const formColor = ref('#a98df0')
const formSelfEnrollable = ref(false)
const managedExplanation = ref('')

const memberItems = ref<Array<Record<string, unknown>>>([])
const memberCount = ref(0)
const memberHeadingCount = ref(0)
const memberProtected = ref(false)
const memberSearchDisabled = ref(false)

let loadVersion = 0
let searchSeq = 0
let memberSearchTimer: ReturnType<typeof setTimeout> | undefined
let lastIdentity = ''

function setListStatus(value: string, error = false) {
  listStatus.value = value
  listStatusError.value = error
}
function setRoleStatus(value: string, error = false) {
  roleStatus.value = value
  roleStatusError.value = error
}
function setMembersStatus(value: string, error = false) {
  membersStatus.value = value
  membersStatusError.value = error
}

const identitySignature = (): string => (site.site?.participant?.participantRef || site.site?.participant?.did || '').toString()

const managed = computed(() => systemRole(selected.value))
const canManageMembers = computed(() => !!selected.value && !selected.value._new && !protectedOwnerRole(selected.value))
const editorActionsVisible = computed(() => editorTab.value !== 'members' && !systemRole(selected.value))
const saveLabel = computed(() => (selected.value?._new ? 'Create role' : 'Save changes'))
const retireVisible = computed(() => !systemRole(selected.value) && !selected.value?._new)
const resetVisible = computed(() => !systemRole(selected.value))
const kicker = computed(() => (selected.value?._new ? 'NEW ROLE' : 'HOST ROLE'))

const directoryHeading = computed(() => {
  const first = roles.value.length ? (roleListPage.value - 1) * rolePageSize + 1 : 0
  const last = first ? first + roles.value.length - 1 : 0
  return `Roles — ${first}–${last} of ${roleTotal.value}`
})

const filteredRoles = computed(() => {
  const term = searchTerm.value.trim().toLocaleLowerCase()
  return roles.value.filter(role => !term || `${role.name} ${role.purpose || ''}`.toLocaleLowerCase().includes(term))
})

function memberCountLabel(role: NormalizedRole): string {
  const total = memberTotals.get(role.id)
  return total == null ? '—' : String(total)
}

function capabilityGroup(key: string): string {
  const prefix = String(key || '').replace(/^global:/, '').split(/[._]/)[0]
  return groupOrder.includes(prefix) ? prefix : 'other'
}

const capabilityGroups = computed<Array<{ group: string; label: string; values: RoleCapability[] }>>(() => {
  if (!selected.value) return []
  const term = permissionSearch.value.trim().toLocaleLowerCase()
  const available = (descriptor.value?.capabilities ?? [])
    .filter(item => !item.scopeTypes || item.scopeTypes.includes('host'))
    .filter(item => !term || `${item.key} ${item.label || ''} ${item.description || ''}`.toLocaleLowerCase().includes(term))
  return groupOrder
    .map(group => ({ group, label: groupLabels[group], values: available.filter(item => capabilityGroup(item.key) === group) }))
    .filter(section => section.values.length > 0)
})

const memberByline = (person: Record<string, unknown>): string => {
  const handle = person.handle as string | undefined
  return handle ? (handle.startsWith('@') ? handle : '@' + handle) : 'Tangent Space participant'
}

const personLabel = (person: Record<string, unknown>): string => ((person.displayName || person.handle || 'Participant') as string)

function hasAvatar(person: Record<string, unknown>): boolean {
  return typeof person.avatar === 'string' && person.avatar.startsWith('/api/profile-cache/avatar?')
}

function editorTabFrom(name: string): string {
  if (!['appearance', 'permissions', 'members'].includes(name)) name = 'appearance'
  return name
}

function roleEditorTab(name: string) {
  editorTab.value = editorTabFrom(name)
  if (editorTab.value === 'members' && selected.value && !selected.value._new) {
    memberSearchTerm.value = ''
    void searchMembers('')
  }
}

function roleUrl(role: (NormalizedRole & { _new?: boolean }) | null, mode: 'push' | 'replace' = 'replace') {
  const url = new URL(window.location.href)
  url.searchParams.set('tab', 'roles')
  if (role) url.searchParams.set('role', role._new ? 'new' : role.id)
  else url.searchParams.delete('role')
  const state = { ...((window.history.state as Record<string, unknown>) || {}), tangentRoleEditor: !!role }
  if (mode === 'push') window.history.pushState(state, '', url.pathname + url.search + url.hash)
  else window.history.replaceState(state, '', url.pathname + url.search + url.hash)
}

function showOverview(updateUrl = true) {
  selected.value = null
  dirty.value = false
  if (updateUrl) roleUrl(null)
}

function showEditor(role: NormalizedRole & { _new?: boolean }, urlMode: 'push' | 'replace' | null = 'push', tab = 'appearance') {
  selected.value = role
  dirty.value = false
  if (urlMode) roleUrl(role, urlMode)
  renderRole()
  roleEditorTab(tab)
}

function renderRole() {
  const role = selected.value
  if (!role) return
  Object.keys(draftGrantKeys).forEach(key => draftGrantKeys.delete(key))
  for (const grant of role.grants ?? []) draftGrantKeys.add(grant.capability)
  formName.value = role.name || ''
  formPurpose.value = role.purpose || ''
  formColor.value = roleColor(role.presentation?.color)
  formSelfEnrollable.value = role.presentation?.selfEnrollable === 'true' || role.metadata?.selfEnrollable === 'true'
  managedExplanation.value = protectedOwnerRole(role)
    ? 'Appearance and permissions are managed by Tangent. The server owner is set by server administration and this setting is not changeable here.'
    : 'Appearance and permissions are managed by Tangent. Authorized owners can still change direct membership.'
}

const preview = computed(() => {
  const name = formName.value.trim() || 'Untitled role'
  const purpose = formPurpose.value.trim() || 'What this role is for'
  const color = roleColor(formColor.value)
  return { name, purpose, color, colorValue: color.toLocaleLowerCase() }
})

function createRole() {
  const role: NormalizedRole & { _new?: boolean } = {
    _new: true, id: '', name: 'New role', purpose: '', grants: [],
    presentation: { color: '#a98df0', system: 'false', selfEnrollable: 'false' },
  }
  showEditor(role)
}

function leaveEditor() {
  if (dirty.value) {
    setRoleStatus('Save or reset your changes before returning to all roles.', true)
    return
  }
  if ((window.history.state as Record<string, unknown> | null)?.tangentRoleEditor) {
    window.history.back()
    showOverview(false)
  } else showOverview()
}

function markDirty() {
  dirty.value = true
  setRoleStatus('Unsaved changes')
}

function toggleGrant(key: string, checked: boolean) {
  if (checked) draftGrantKeys.add(key)
  else draftGrantKeys.delete(key)
  markDirty()
}

async function loadScope() {
  if (activeTab.value !== 'roles') return
  const identity = identitySignature()
  if (identity !== lastIdentity) {
    memberTotals.clear()
    lastIdentity = identity
  }
  const version = ++loadVersion
  setListStatus('Loading roles…')
  setRoleStatus('')
  setMembersStatus('')
  try {
    const results = await Promise.allSettled([
      descriptor.value ? Promise.resolve(descriptor.value) : rolesRequest<RoleDescriptor>('/api/roles/ui/descriptor'),
      rolesRequest<{ items?: Record<string, unknown>[]; totalCount?: number }>('/api/identity/roles?pageSize=' + rolePageSize + '&page=1'),
    ])
    if (version !== loadVersion) return
    if (results[1]!.status === 'rejected') throw results[1]!.reason
    if (results[0]!.status === 'fulfilled') {
      descriptor.value = results[0]!.value
      descriptorError.value = false
    } else {
      descriptor.value ??= { capabilities: [] }
      descriptorError.value = true
    }
    const rolePage = results[1]!.value as { items?: Record<string, unknown>[]; totalCount?: number }
    roles.value = (rolePage.items ?? []).map(normalizeRole)
    roleTotal.value = rolePage.totalCount ?? roles.value.length
    roleListPage.value = 1
    roleListHasPrevious.value = false
    roleListHasNext.value = roles.value.length < roleTotal.value
    memberTotals.clear()
    for (const role of roles.value) {
      const total = role.memberCount ?? 0
      memberTotals.set(role.id, total)
    }
    const requested = new URL(window.location.href).searchParams.get('role')
    let target = requested === 'new' && selected.value?._new ? selected.value : roles.value.find(role => role.id === requested)
    if (requested && requested !== 'new' && !target) {
      try {
        const direct = normalizeRole(
          (await rolesRequest<Record<string, unknown>>('/api/identity/roles/' + encodeURIComponent(requested))) as Record<string, unknown>,
        )
        if (direct) target = direct
      } catch {
        // The normal unavailable route below keeps the deep link honest.
      }
    }
    if (target) showEditor(target, null, selected.value?._new ? editorTab.value : 'appearance')
    else {
      if (requested) roleUrl(null)
      showOverview(false)
    }
    setListStatus(
      descriptorError.value
        ? 'Roles loaded. Permission details are temporarily unavailable.'
        : roles.value.length
          ? `Showing ${roles.value.length} of ${roleTotal.value}`
          : 'No roles have been created at this scope.',
      descriptorError.value,
    )
  } catch (error) {
    if (version !== loadVersion) return
    roles.value = []
    roleTotal.value = 0
    roleListPage.value = 1
    roleListHasPrevious.value = false
    roleListHasNext.value = false
    memberTotals.clear()
    selected.value = null
    showOverview(false)
    setListStatus((error as Error).message, true)
  }
}

async function changeRolePage(delta: number) {
  const page = roleListPage.value + delta
  if (page < 1 || (delta < 0 && !roleListHasPrevious.value) || (delta > 0 && !roleListHasNext.value)) return
  setListStatus('Loading roles…')
  try {
    const rolePage = await rolesRequest<{ items?: Record<string, unknown>[]; totalCount?: number }>(
      '/api/identity/roles?pageSize=' + rolePageSize + '&page=' + encodeURIComponent(page),
    )
    roles.value = (rolePage.items ?? []).map(normalizeRole)
    roleListPage.value = page
    roleTotal.value = rolePage.totalCount ?? roleTotal.value
    roleListHasPrevious.value = page > 1
    roleListHasNext.value = page * rolePageSize < roleTotal.value
    memberTotals.clear()
    const first = roles.value.length ? (page - 1) * rolePageSize + 1 : 0
    const last = first ? first + roles.value.length - 1 : 0
    setListStatus(`Showing ${first}–${last} of ${roleTotal.value}`)
  } catch (error) {
    setListStatus((error as Error).message, true)
  }
}

function roleGrants(): string[] {
  if (descriptorError.value) return [...new Set((selected.value?.grants ?? []).map(grant => grant.capability))]
  const known = new Set((descriptor.value?.capabilities ?? []).map(capability => capability.key))
  const grants = (selected.value?.grants ?? []).filter(grant => !known.has(grant.capability)).map(grant => grant.capability)
  for (const capability of draftGrantKeys) grants.push(capability)
  return [...new Set(grants)]
}

async function saveRole() {
  const role = selected.value
  if (!role || systemRole(role)) return
  const name = formName.value.trim()
  const purpose = formPurpose.value.trim()
  if (!name) {
    setRoleStatus('Give this role a name.', true)
    return
  }
  setRoleStatus(role._new ? 'Creating…' : 'Saving…')
  try {
    const body = {
      name,
      permissions: roleGrants(),
      metadata: {
        purpose: purpose || '',
        color: formColor.value,
        selfEnrollable: formSelfEnrollable.value ? 'true' : 'false',
      },
    }
    let key = role.id
    if (role._new) {
      key = roleKeyFor(name)
      if (key === 'role:') throw new Error('Use at least one letter or number in the role name.')
      try {
        await rolesRequest('/api/identity/roles/' + encodeURIComponent(key))
        throw new Error('A role with that stable key already exists. Choose a more distinct name.')
      } catch (error) {
        if ((error as Error & { status?: number }).status !== 404) throw error
      }
    }
    const result = await rolesRequest<Record<string, unknown>>('/api/identity/roles/' + encodeURIComponent(key), {
      method: role._new ? 'PUT' : 'PATCH',
      body,
    })
    selected.value = normalizeRole(result)
    dirty.value = false
    roleUrl(selected.value, 'replace')
    await loadScope()
    setRoleStatus('Role saved.')
  } catch (error) {
    const status = (error as Error & { status?: number }).status
    if (status === 400 && /antiforgery/i.test((error as Error).message)) clearRolesSession()
    setRoleStatus((error as Error).message, true)
  }
}

function resetRole() {
  const role = selected.value
  if (!role || systemRole(role)) return
  const tab = editorTab.value
  selected.value = role._new
    ? { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0', system: 'false', selfEnrollable: 'false' } }
    : roles.value.find(entry => entry.id === role.id) || role
  dirty.value = false
  renderRole()
  roleEditorTab(tab)
  setRoleStatus('Changes reset.')
}

async function retireRole() {
  const role = selected.value
  if (!role || role._new || systemRole(role)) return
  setRoleStatus('Deleting…')
  try {
    await rolesRequest('/api/identity/roles/' + encodeURIComponent(role.id), { method: 'DELETE' })
    selected.value = null
    dirty.value = false
    roleUrl(null, 'replace')
    await loadScope()
    setListStatus('Role deleted.')
  } catch (error) {
    setRoleStatus((error as Error).message, true)
  }
}

async function searchMembers(query = '') {
  const seq = ++searchSeq
  const role = selected.value
  if (!role || role._new) {
    memberItems.value = []
    memberCount.value = 0
    memberHeadingCount.value = 0
    return
  }
  const protectedOwner = protectedOwnerRole(role)
  const manageable = canManageMembers.value
  memberProtected.value = protectedOwner
  memberSearchDisabled.value = !manageable && !protectedOwner
  setMembersStatus('Loading members…')
  try {
    const text = (query || '').trim()
    const params = new URLSearchParams({ role: role.id })
    if (text) params.set('query', text)
    const items = await rolesRequest<Array<Record<string, unknown>>>('/api/roles/ui/search?' + params)
    if (seq !== searchSeq) return
    memberItems.value = items
    setMembersStatus('')
    if (!text) {
      memberCount.value = items.length
      memberHeadingCount.value = items.length
      memberTotals.set(role.id, items.length)
    }
    if (!items.length) setMembersStatus('')
  } catch (error) {
    if (seq !== searchSeq) return
    setMembersStatus((error as Error).message, true)
  }
}

async function toggleMember(person: Record<string, unknown>) {
  const role = selected.value
  if (!role) return
  const label = personLabel(person)
  const willAdd = !person.inRole
  setMembersStatus(willAdd ? `Adding ${label}…` : `Removing ${label}…`)
  try {
    await rolesRequest('/api/identity/roles/' + encodeURIComponent(role.id) + '/members/' + encodeURIComponent(person.id as string), {
      method: willAdd ? 'PUT' : 'DELETE',
    })
    person.inRole = willAdd
    if (willAdd) {
      memberCount.value += 1
      memberHeadingCount.value = memberCount.value
      memberTotals.set(role.id, (memberTotals.get(role.id) ?? 0) + 1)
      setMembersStatus(`${label} added to ${role.name}.`)
    } else {
      // A searched row stays listed with its toggled state; only the full list shrinks.
      if (!memberSearchTerm.value.trim()) {
        memberItems.value = memberItems.value.filter(entry => entry !== person)
        memberCount.value = memberItems.value.length
        memberHeadingCount.value = memberCount.value
        memberTotals.set(role.id, memberCount.value)
      }
      setMembersStatus(`${label} removed from ${role.name}.`)
    }
  } catch (error) {
    setMembersStatus((error as Error).message, true)
  }
}

function pickRole(role: NormalizedRole) {
  if (dirty.value) {
    setRoleStatus('Save or reset your changes before choosing another role.', true)
    return
  }
  showEditor(role, 'replace')
}

function onEditorTabKeydown(event: KeyboardEvent, name: string) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const tabs = ['appearance', 'permissions', 'members']
  const index = tabs.indexOf(name)
  let target = name
  if (event.key === 'Home') target = tabs[0]!
  else if (event.key === 'End') target = tabs[tabs.length - 1]!
  else {
    const direction = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1
    target = tabs[(index + direction + tabs.length) % tabs.length]!
  }
  roleEditorTab(target)
  document.querySelector<HTMLButtonElement>(`[data-role-editor-tab="${target}"]`)?.focus()
}

function syncRoleHistory() {
  const pathname = window.location.pathname
  if (!/\/settings$/.test(pathname.replace(/\/$/, '')) || activeTab.value !== 'roles') return
  const requested = new URL(window.location.href).searchParams.get('role')
  if (!requested) {
    showOverview(false)
    return
  }
  const target = requested === 'new' && selected.value?._new ? selected.value : roles.value.find(role => role.id === requested)
  if (target) showEditor(target, null, 'appearance')
  else showOverview(false)
}

watch(activeTab, name => {
  if (name === 'roles' && !(dirty.value && selected.value)) void loadScope()
})

function onMemberSearchInput() {
  clearTimeout(memberSearchTimer)
  memberSearchTimer = setTimeout(() => void searchMembers(memberSearchTerm.value), 200)
}

onMounted(() => {
  window.addEventListener('popstate', syncRoleHistory)
  if (activeTab.value === 'roles') void loadScope()
})

onScopeDispose(() => {
  clearTimeout(memberSearchTimer)
  window.removeEventListener('popstate', syncRoleHistory)
})
</script>

<template>
  <section id="role-overview" class="role-overview" aria-labelledby="role-overview-heading" :hidden="!!selected">
    <div class="role-overview-toolbar">
      <label class="role-search"><span class="visually-hidden">Search roles</span><input v-model="searchTerm" id="role-search" class="input" type="search" placeholder="Search roles"></label>
      <button id="role-create" class="btn btn-primary" type="button" @click="createRole">Create role</button>
    </div>
    <div class="role-directory-heading"><strong id="role-overview-heading">{{ directoryHeading }}</strong><span>Direct members</span></div>
    <div id="role-directory-list" class="role-directory-list" role="listbox" aria-label="Roles">
      <button
        v-for="role in filteredRoles"
        :key="role.id"
        type="button"
        class="role-directory-row"
        role="option"
        :style="{ '--role-color': roleColor(role.presentation?.color) }"
        @click="showEditor(role)"
      >
        <span class="role-directory-identity">
          <span class="role-list-dot"></span>
          <span class="role-directory-copy">
            <span class="role-directory-name">
              <strong>{{ role.name }}</strong>
              <small v-if="systemRole(role)" class="role-kind">Built in</small>
              <small v-else-if="role._new" class="role-kind">Unsaved</small>
              <small v-if="role.presentation?.selfEnrollable === 'true'" class="role-kind">Self-enrollable</small>
            </span>
            <small class="role-directory-purpose">{{ role.purpose || (systemRole(role) ? 'Managed by Tangent Space' : 'Custom role') }}</small>
          </span>
        </span>
        <span class="role-directory-members" :aria-label="memberCountLabel(role) + ' direct members'">{{ memberCountLabel(role) }}</span>
        <span class="role-directory-chevron">›</span>
      </button>
      <p v-if="!filteredRoles.length && roles.length" class="role-directory-empty">No roles match that search.</p>
    </div>
    <div class="role-directory-actions">
      <button id="role-page-previous" class="btn btn-quiet" type="button" :hidden="!roleListHasPrevious" @click="changeRolePage(-1)">Previous</button>
      <button id="role-page-next" class="btn btn-quiet" type="button" :hidden="!roleListHasNext" @click="changeRolePage(1)">Next</button>
    </div>
    <p id="role-list-status" class="hint" role="status" :class="{ error: listStatusError }">{{ listStatus }}</p>
    <p class="role-count-note">Member counts show direct assignments in this scope. Inherited access remains visible in the role’s permissions.</p>
  </section>

  <section id="role-editor-view" class="role-editor-view" :hidden="!selected">
    <header class="role-editor-heading">
      <button id="role-back" class="role-back" type="button" @click="leaveEditor">← All roles</button>
      <div class="role-title-line">
        <span id="role-color-dot" class="role-color-dot" aria-hidden="true" :style="{ '--role-color': preview.color }"></span>
        <div>
          <p id="role-editor-kicker" class="eyebrow">{{ kicker }}</p>
          <h3 id="role-editor-title">{{ preview.name }}</h3>
        </div>
      </div>
      <span id="role-managed" class="role-managed" :hidden="!managed">Built-in role</span>
    </header>
    <div class="role-editor-workspace">
      <aside class="role-browser" aria-label="Other roles in this scope">
        <div id="role-list" class="role-list" role="listbox" aria-label="Other roles">
          <button
            v-for="role in (selected && !roles.some(entry => entry.id === selected?.id) ? [selected, ...roles] : selected?._new ? [selected, ...roles] : roles)"
            :key="role.id || 'new'"
            type="button"
            class="role-list-item"
            role="option"
            :aria-selected="role === selected"
            :style="{ '--role-color': roleColor(role.presentation?.color) }"
            @click="pickRole(role)"
          >
            <span class="role-list-dot"></span>
            <strong>{{ role.name }}</strong>
          </button>
        </div>
      </aside>
      <form id="role-form" @submit.prevent="saveRole">
        <nav class="role-editor-tabs" role="tablist" aria-label="Role editor">
          <button type="button" role="tab" :aria-selected="editorTab === 'appearance'" data-role-editor-tab="appearance" @click="roleEditorTab('appearance')" @keydown="onEditorTabKeydown($event, 'appearance')">Appearance</button>
          <button type="button" role="tab" :aria-selected="editorTab === 'permissions'" data-role-editor-tab="permissions" @click="roleEditorTab('permissions')" @keydown="onEditorTabKeydown($event, 'permissions')">Permissions</button>
          <button type="button" role="tab" :aria-selected="editorTab === 'members'" data-role-editor-tab="members" @click="roleEditorTab('members')" @keydown="onEditorTabKeydown($event, 'members')">Members <span id="role-member-count">{{ memberCount }}</span></button>
        </nav>
        <section id="role-appearance-panel" role="tabpanel" :hidden="editorTab !== 'appearance'">
          <div class="role-appearance-layout">
            <div class="role-fields">
              <label>Role name<input v-model="formName" id="role-name" class="input" maxlength="160" required :disabled="managed" @input="markDirty"></label>
              <label>Purpose<textarea v-model="formPurpose" id="role-purpose" class="input" maxlength="1024" rows="3" placeholder="What is this role for?" :disabled="managed" @input="markDirty"></textarea></label>
              <label class="role-color-field"><span>Role color</span><span class="role-color-control"><input v-model="formColor" id="role-color" type="color" value="#a98df0" :disabled="managed" @input="markDirty"><code id="role-color-value">{{ preview.colorValue }}</code></span></label>
              <label class="check-row"><input v-model="formSelfEnrollable" id="role-self-enrollable" type="checkbox" :disabled="managed" @change="markDirty"> Allow participants to self-enroll</label>
              <p id="role-managed-explanation" class="role-managed-explanation" :hidden="!managed">{{ managedExplanation }}</p>
            </div>
            <aside id="role-preview" class="role-preview" aria-label="Role preview" :style="{ '--role-color': preview.color }">
              <p class="eyebrow">ROLE PREVIEW</p>
              <div class="role-preview-identity">
                <span id="role-preview-dot" class="role-color-dot" aria-hidden="true" :style="{ '--role-color': preview.color }"></span>
                <div><strong id="role-preview-name">{{ preview.name }}</strong><small id="role-preview-purpose">{{ preview.purpose }}</small></div>
              </div>
              <div class="role-preview-context">
                <span>How it appears in a role list</span>
                <span id="role-preview-chip" class="role-preview-chip" :style="{ '--role-color': preview.color }"><i aria-hidden="true"></i><b>{{ preview.name }}</b></span>
              </div>
            </aside>
          </div>
          <button id="role-retire" class="btn btn-quiet role-danger" type="button" :hidden="!retireVisible" @click="retireRole">Delete role</button>
        </section>
        <section id="role-permissions-panel" role="tabpanel" :hidden="editorTab !== 'permissions'">
          <label class="role-permission-search"><span class="visually-hidden">Search permissions</span><input v-model="permissionSearch" id="role-permission-search" class="input" type="search" placeholder="Search permissions"></label>
          <div id="role-capabilities" class="role-capabilities">
            <p v-if="descriptorError" class="hint error">Permission details could not be loaded. The role and its members remain available; reload to retry.</p>
            <template v-else>
              <section v-for="section in capabilityGroups" :key="section.group" class="role-capability-group">
                <h4>{{ section.label }}</h4>
                <div class="role-capability-list">
                  <label v-for="capability in section.values" :key="capability.key" class="role-capability">
                    <span><strong>{{ capability.label || capability.key }}</strong><small>{{ capability.description || capability.key }}</small></span>
                    <input type="checkbox" :value="capability.key" :checked="draftGrantKeys.has(capability.key)" :disabled="managed" @change="toggleGrant(capability.key, ($event.target as HTMLInputElement).checked)">
                  </label>
                </div>
              </section>
              <p v-if="!capabilityGroups.length" class="hint">No permissions match that search.</p>
            </template>
          </div>
        </section>
        <section id="role-members-panel" role="tabpanel" :hidden="editorTab !== 'members'">
          <div class="role-members-heading">
            <div>
              <h4>Members <span id="role-members-heading-count">{{ memberHeadingCount }}</span></h4>
              <p id="role-members-scope" class="hint">Direct assignments in this scope.</p>
            </div>
          </div>
          <label class="role-member-search">
            <span class="visually-hidden">Search members</span>
            <input v-model="memberSearchTerm" id="role-member-search" class="input" type="search" placeholder="Search members by name, handle, or DID…" :disabled="memberSearchDisabled" @input="onMemberSearchInput">
          </label>
          <div id="role-member-protected" class="role-member-protected" :hidden="!memberProtected">
            <strong>Ownership is protected</strong>
            <p>The server owner is the human account designated by server administration. This role assignment cannot replace that ownership.</p>
          </div>
          <div id="role-members" class="role-members">
            <p v-if="selected?._new" class="role-member-empty">Create this role before managing members.</p>
            <p v-else-if="!memberItems.length" class="role-member-empty">{{ memberSearchTerm.trim() ? 'No matching participants found.' : 'No members in this role yet.' }}</p>
            <div v-for="person in memberItems" v-else :key="(person.id as string)" class="role-member" :style="{ '--role-color': roleColor(selected?.presentation?.color) }">
              <span class="role-member-avatar">
                <img v-if="hasAvatar(person)" :src="(person.avatar as string)" alt="" loading="lazy" referrerpolicy="no-referrer">
                <template v-else>{{ personLabel(person).slice(0, 1).toUpperCase() }}</template>
              </span>
              <span>
                <strong>{{ personLabel(person) }}</strong>
                <small>{{ memberByline(person) }}</small>
              </span>
              <button
                v-if="canManageMembers"
                type="button"
                :class="person.inRole ? 'btn btn-quiet role-member-toggle in-role' : 'btn btn-primary role-member-toggle'"
                :title="person.inRole ? `Remove ${personLabel(person)} from ${selected?.name}` : `Add ${personLabel(person)} to ${selected?.name}`"
                :aria-label="person.inRole ? `Remove ${personLabel(person)} from ${selected?.name}` : `Add ${personLabel(person)} to ${selected?.name}`"
                @click="toggleMember(person)"
              >
                {{ person.inRole ? 'Remove' : 'Add' }}
              </button>
            </div>
          </div>
          <p id="role-members-status" class="hint" role="status" :class="{ error: membersStatusError }">{{ membersStatus }}</p>
        </section>
        <div id="role-editor-actions" class="role-editor-actions" :hidden="!editorActionsVisible">
          <button id="role-save" class="btn btn-primary" type="submit">{{ saveLabel }}</button>
          <button id="role-reset" class="btn btn-quiet" type="button" :hidden="!resetVisible" @click="resetRole">Reset</button>
          <p id="role-status" role="status" :class="{ error: roleStatusError }">{{ roleStatus }}</p>
        </div>
      </form>
    </div>
  </section>
</template>
