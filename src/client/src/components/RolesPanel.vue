<script setup lang="ts">
import { registerSettingsChange, useSettingsSubmit } from '../composables/settings-changes'
// The Roles tab provides the role directory with member counts and
// pagination, and the role editor — appearance, permissions, and direct members.
import { computed, nextTick, onMounted, onScopeDispose, reactive, ref, shallowRef, watch } from 'vue'
import { useSite } from '../stores/site'
import type { Settings } from '../composables/settings'
import {
  clearRolesSession, derivedRole, normalizeRole, roleColor, roleKeyFor, rolesRequest,
  systemRole, type NormalizedRole, type RoleCapability, type RoleDescriptor,
} from '../composables/roles-api'

const submitSettings = useSettingsSubmit()
const props = defineProps<{ settings: Settings }>()
const site = useSite()
const { activeTab } = props.settings
const pathAvailable = ref(false)
watch(activeTab, async () => { await nextTick(); pathAvailable.value = !!document.getElementById('role-path-slot') }, { immediate: true, flush: 'post' })

const groupOrder = ['host', 'tangent', 'topic', 'post', 'other']
const groupLabels: Record<string, string> = { host: 'Space', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'People and roles' }
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
const pending = ref(false)
const confirmingDelete = ref(false)
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
const memberPage = ref(1)
const memberNextPage = ref<number | null>(null)
const memberSearchDisabled = ref(false)

let loadVersion = 0
let searchSeq = 0
let memberSearchTimer: ReturnType<typeof setTimeout> | undefined
let lastIdentity = ''
let editorVersion = 0
function roleDraftKey(role: NormalizedRole & { _new?: boolean }) {
  return 'tangent:host-role-draft:' + [props.settings.actorKey.value, role._new ? 'new' : role.id].map(encodeURIComponent).join(':')
}
function persistRoleDraft() {
  const role = selected.value
  if (!role) return
  try {
    if (!dirty.value) sessionStorage.removeItem(roleDraftKey(role))
    else sessionStorage.setItem(roleDraftKey(role), JSON.stringify({ name: formName.value, purpose: formPurpose.value, color: formColor.value, selfEnrollable: formSelfEnrollable.value, grants: [...draftGrantKeys] }))
  } catch { /* Browser storage may be unavailable. The live draft remains editable. */ }
}
function clearRoleDraft(role: NormalizedRole & { _new?: boolean }) {
  try { sessionStorage.removeItem(roleDraftKey(role)) } catch { /* unavailable */ }
}

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
const canManageMembers = computed(() => !!selected.value && !selected.value._new && !derivedRole(selected.value))
const memberProtected = computed(() => derivedRole(selected.value))
const editorActionsVisible = computed(() => editorTab.value !== 'members' && !systemRole(selected.value))
const saveLabel = computed(() => (selected.value?._new ? 'Create role' : 'Save changes'))
const retireVisible = computed(() => !systemRole(selected.value) && !selected.value?._new)
const resetVisible = computed(() => !systemRole(selected.value))

const filteredRoles = computed(() => {
  const term = searchTerm.value.trim().toLocaleLowerCase()
  return roles.value.filter(role => !term || `${role.name} ${role.purpose || ''}`.toLocaleLowerCase().includes(term))
})

function memberCountLabel(role: NormalizedRole): string {
  if (role.id === 'role:owner') return 'Space owner · automatic'
  if (role.id === 'role:participant') return 'Everyone signed in · automatic'
  if (derivedRole(role)) return 'Automatic membership'
  const total = memberTotals.get(role.id)
  return total == null ? 'Member count unavailable' : total === 0 ? 'No one yet' : `${total} ${total === 1 ? 'member' : 'members'}`
}

function permissionWords(value: string): string {
  return value.replace(/Enter server/g, 'Enter the Space').replace(/Manage server/g, 'Manage the Space')
    .replace(/this Tangent Space server/g, 'this Space').replace(/\bserver\b/g, 'Space')
    .replace(/\bTopics\b/g, 'topics').replace(/\bTopic\b/g, 'topic').replace(/\bPosts\b/g, 'posts').replace(/\bPost\b(?! anywhere)/g, 'post')
}

const permissionDescriptions: Record<string, string> = {
  'Enter the Space': 'See this Space even when its access list leaves them out.',
  'Manage the Space': "Change the Space’s identity, how it runs, and its access defaults.",
  'Create Tangents': 'Create a Tangent anywhere in the Space.',
  'See every Tangent': 'See Tangents whatever their access lists say.',
  'Manage every Tangent': 'Change details and access for every Tangent.',
  'Create topics anywhere': 'Start a topic in every Tangent they can see.',
  'See every topic': 'See topics whatever their access lists say.',
  'Manage every topic': 'Change details and access for every topic.',
  'Post anywhere': 'Reply in every topic they can see.',
  'Remove any post': 'Moderate posts in every topic they can see.',
  'Manage roles': 'Create roles, change their permissions and manage their members.',
  'Manage participants': 'Approve, suspend and restore participants.',
  'Warn participants': 'Send an official warning that stays on their record.',
  'Time out participants': 'Restrict someone in a Tangent for a set time. Reversible.',
  'Ban participants': 'Ban participants from a Tangent.',
}
function permissionDescription(capability: RoleCapability): string {
  return permissionDescriptions[permissionWords(capability.label || '')] || permissionWords(capability.description || capability.key)
}

function grantChanged(key: string): boolean {
  return draftGrantKeys.has(key) !== !!selected.value?.grants.some(grant => grant.capability === key)
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
    .filter(item => !term || permissionWords(`${item.key} ${item.label || ''} ${item.description || ''}`).toLocaleLowerCase().includes(term))
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
    void searchMembers('', 1)
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
  if (pending.value) return
  searchSeq++
  editorVersion++
  selected.value = null
  confirmingDelete.value = false
  dirty.value = false
  if (updateUrl) roleUrl(null)
}

function showEditor(role: NormalizedRole & { _new?: boolean }, urlMode: 'push' | 'replace' | null = 'push', tab = 'appearance') {
  if (pending.value) return
  searchSeq++
  editorVersion++
  selected.value = role
  confirmingDelete.value = false
  memberCount.value = memberTotals.get(role.id) ?? 0
  dirty.value = false
  if (urlMode) roleUrl(role, urlMode)
  renderRole()
  roleEditorTab(tab)
}

function renderRole() {
  const role = selected.value
  if (!role) return
  draftGrantKeys.clear()
  for (const grant of role.grants ?? []) draftGrantKeys.add(grant.capability)
  formName.value = role.name || ''
  formPurpose.value = role.purpose || ''
  formColor.value = roleColor(role.presentation?.color)
  formSelfEnrollable.value = role.presentation?.selfEnrollable === 'true' || role.metadata?.selfEnrollable === 'true'
  try {
    const raw = sessionStorage.getItem(roleDraftKey(role))
    if (raw) {
      const draft = JSON.parse(raw) as Record<string, unknown>
      if (typeof draft.name === 'string') formName.value = draft.name.slice(0, 160)
      if (typeof draft.purpose === 'string') formPurpose.value = draft.purpose.slice(0, 1024)
      if (typeof draft.color === 'string') formColor.value = roleColor(draft.color)
      if (typeof draft.selfEnrollable === 'boolean') formSelfEnrollable.value = draft.selfEnrollable
      if (Array.isArray(draft.grants)) {
        draftGrantKeys.clear()
        for (const grant of draft.grants) if (typeof grant === 'string') draftGrantKeys.add(grant)
      }
      dirty.value = true
    }
  } catch { /* Ignore a malformed or unavailable browser draft. */ }
  managedExplanation.value = derivedRole(role)
    ? 'Appearance and permissions are managed by Tangent. Membership follows identity and cannot be changed here.'
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

function markDirty() {
  if (pending.value) return
  const role = selected.value
  dirty.value = !!role && (formName.value !== role.name || formPurpose.value !== role.purpose || formColor.value !== roleColor(role.presentation?.color)
    || formSelfEnrollable.value !== (role.presentation?.selfEnrollable === 'true' || role.metadata?.selfEnrollable === 'true')
    || draftGrantKeys.size !== new Set(role.grants.map(grant => grant.capability)).size || role.grants.some(grant => !draftGrantKeys.has(grant.capability)))
  setRoleStatus('')
  persistRoleDraft()
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
      if (version !== loadVersion || identity !== identitySignature()) return
    }
    if (version !== loadVersion || identity !== identitySignature() || activeTab.value !== 'roles') return
    if (target) showEditor(target, null, selected.value?._new ? editorTab.value : 'appearance')
    else {
      if (requested) roleUrl(null)
      showOverview(false)
    }
    setListStatus(
      descriptorError.value
        ? 'Roles loaded. Permission details are temporarily unavailable.'
        : roles.value.length
          ? ''
          : 'No roles yet.',
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
    for (const role of roles.value) memberTotals.set(role.id, role.memberCount ?? 0)
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
  if (!role || systemRole(role) || pending.value || !dirty.value) return
  const at = editorVersion
  const identity = identitySignature()
  const originPath = window.location.pathname
  const originRole = new URL(window.location.href).searchParams.get('role')
  const name = formName.value.trim()
  const purpose = formPurpose.value.trim()
  if (!name) {
    setRoleStatus('Give this role a name.', true)
    return
  }
  setRoleStatus(role._new ? 'Creating…' : 'Saving…')
  pending.value = true
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
    if (at !== editorVersion || identity !== identitySignature() || selected.value !== role) return
    const result = await rolesRequest<Record<string, unknown>>('/api/identity/roles/' + encodeURIComponent(key), {
      method: role._new ? 'PUT' : 'PATCH',
      body,
    })
    if (at !== editorVersion || identity !== identitySignature() || selected.value !== role) return
    clearRoleDraft(role)
    selected.value = normalizeRole(result)
    dirty.value = false
    if (activeTab.value === 'roles' && window.location.pathname === originPath && new URL(window.location.href).searchParams.get('role') === originRole) roleUrl(selected.value, 'replace')
    pending.value = false
    await loadScope()
    setRoleStatus('Role saved.')
  } catch (error) {
    if (at !== editorVersion || identity !== identitySignature()) return
    const status = (error as Error & { status?: number }).status
    if (status === 400 && /antiforgery/i.test((error as Error).message)) clearRolesSession()
    setRoleStatus((error as Error).message, true)
  } finally {
    if (at === editorVersion) pending.value = false
  }
}

function resetRole() {
  const role = selected.value
  if (!role || systemRole(role) || pending.value) return
  clearRoleDraft(role)
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
  if (!role || role._new || systemRole(role) || pending.value || !confirmingDelete.value) return
  const at = editorVersion
  const identity = identitySignature()
  const originPath = window.location.pathname
  const originRole = new URL(window.location.href).searchParams.get('role')
  setRoleStatus('Deleting…')
  pending.value = true
  try {
    await rolesRequest('/api/identity/roles/' + encodeURIComponent(role.id), { method: 'DELETE' })
    if (at !== editorVersion || identity !== identitySignature() || selected.value !== role) return
    clearRoleDraft(role)
    selected.value = null
    dirty.value = false
    if (activeTab.value === 'roles' && window.location.pathname === originPath && new URL(window.location.href).searchParams.get('role') === originRole) roleUrl(null, 'replace')
    pending.value = false
    await loadScope()
    setListStatus('Role deleted.')
  } catch (error) {
    if (at === editorVersion && identity === identitySignature()) setRoleStatus((error as Error).message, true)
  } finally {
    if (at === editorVersion) pending.value = false
  }
}

async function searchMembers(query = '', page = 1): Promise<boolean> {
  const seq = ++searchSeq
  const role = selected.value
  const identity = identitySignature()
  if (!role || role._new) {
    memberItems.value = []
    memberCount.value = 0
    memberHeadingCount.value = 0
    memberNextPage.value = null
    return false
  }
  if (derivedRole(role)) {
    memberItems.value = []
    memberCount.value = 0
    memberHeadingCount.value = 0
    memberNextPage.value = null
    setMembersStatus('')
    return true
  }
  const manageable = canManageMembers.value
  memberSearchDisabled.value = !manageable
  setMembersStatus('Loading members…')
  try {
    const text = (query || '').trim()
    const params = new URLSearchParams({ role: role.id, page: String(page) })
    if (text) params.set('query', text)
    const result = await rolesRequest<{ items: Array<Record<string, unknown>>; page: number; nextPage?: number | null; totalMembers: number }>('/api/roles/ui/search?' + params)
    if (seq !== searchSeq || selected.value?.id !== role.id || identity !== identitySignature()) return false
    memberItems.value = result.items
    memberPage.value = result.page
    memberNextPage.value = result.nextPage ?? null
    memberCount.value = result.totalMembers
    memberHeadingCount.value = result.totalMembers
    memberTotals.set(role.id, result.totalMembers)
    setMembersStatus('')
    return true
  } catch (error) {
    if (seq !== searchSeq || selected.value?.id !== role.id || identity !== identitySignature()) return false
    setMembersStatus((error as Error).message, true)
    return false
  }
}

function changeMemberPage(delta: number) {
  if (memberSearchTerm.value.trim()) return
  const page = delta < 0 ? memberPage.value - 1 : memberNextPage.value
  if (page && page >= 1) void searchMembers('', page)
}

async function toggleMember(person: Record<string, unknown>) {
  const role = selected.value
  if (!role) return
  const identity = identitySignature()
  const query = memberSearchTerm.value
  const page = memberPage.value
  const label = personLabel(person)
  const willAdd = !person.inRole
  setMembersStatus(willAdd ? `Adding ${label}…` : `Removing ${label}…`)
  try {
    await rolesRequest('/api/identity/roles/' + encodeURIComponent(role.id) + '/members/' + encodeURIComponent(person.id as string), {
      method: willAdd ? 'PUT' : 'DELETE',
    })
    if (selected.value?.id !== role.id || identity !== identitySignature() || query !== memberSearchTerm.value) return
    const fresh = await searchMembers(query, page)
    if (fresh && selected.value?.id === role.id && identity === identitySignature())
      setMembersStatus(`${label} ${willAdd ? 'added to' : 'removed from'} ${role.name}.`)
  } catch (error) {
    if (selected.value?.id === role.id && identity === identitySignature()) setMembersStatus((error as Error).message, true)
  }
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
watch(() => props.settings.actorKey.value, () => {
  editorVersion++
  loadVersion++
  pending.value = false
  selected.value = null
  dirty.value = false
  descriptor.value = null
  if (activeTab.value === 'roles') void loadScope()
})

function onMemberSearchInput() {
  searchSeq++
  clearTimeout(memberSearchTimer)
  memberSearchTimer = setTimeout(() => void searchMembers(memberSearchTerm.value, 1), 200)
}

onMounted(() => {
  window.addEventListener('popstate', syncRoleHistory)
  if (activeTab.value === 'roles') void loadScope()
})

onScopeDispose(() => {
  editorVersion++
  loadVersion++
  clearTimeout(memberSearchTimer)
  window.removeEventListener('popstate', syncRoleHistory)
})
const coordinated = registerSettingsChange({ label: 'Role', count: () => dirty.value ? 1 : 0, busy: () => pending.value, save: saveRole, discard: resetRole, status: () => roleStatus.value })
</script>

<template>
  <Teleport v-if="activeTab === 'roles' && selected && pathAvailable" to="#role-path-slot"><span class="path-separator" aria-hidden="true">/</span><span class="path-current" aria-current="page">{{ preview.name }}</span></Teleport>
  <div class="roles-workspace">
  <section id="role-overview" class="role-overview" aria-label="Roles">
    <div class="role-overview-toolbar">
      <label class="role-search"><span class="visually-hidden">Find a role</span><input v-model="searchTerm" id="role-search" class="input" type="search" placeholder="Find a role"></label>
      <button id="role-create" class="btn" type="button" :disabled="pending" @click="createRole">New role</button>
    </div>
    <nav id="role-directory-list" class="role-directory-list" aria-label="Choose a role">
      <button
        v-for="role in filteredRoles"
        :key="role.id"
        type="button"
        class="role-directory-row"
        :aria-current="selected?.id === role.id && !selected?._new ? 'true' : undefined"
        :style="{ '--role-color': roleColor(role.presentation?.color) }"
        :disabled="pending"
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
            <small class="role-directory-members">{{ memberCountLabel(role) }}</small>
          </span>
        </span>
        <span class="role-directory-chevron">›</span>
      </button>
      <p v-if="!filteredRoles.length && roles.length" class="role-directory-empty">No roles match that search.</p>
    </nav>
    <div v-if="roleListHasPrevious || roleListHasNext" class="role-directory-actions">
      <button id="role-page-previous" class="btn btn-quiet" type="button" :hidden="!roleListHasPrevious" @click="changeRolePage(-1)">Previous</button>
      <button id="role-page-next" class="btn btn-quiet" type="button" :hidden="!roleListHasNext" @click="changeRolePage(1)">Next</button>
    </div>
    <p v-if="roles.length" class="hint role-count-note">Counts show direct assignments.</p>
    <p v-if="listStatus" id="role-list-status" class="hint" role="status" :class="{ error: listStatusError }">{{ listStatus }}</p>
    <button v-if="listStatusError" class="btn btn-quiet" type="button" @click="loadScope">Retry</button>
  </section>
  <p v-if="!selected && roles.length" class="role-selection-hint hint">Choose a role to see its details, permissions and members.</p>
  <section id="role-editor-view" class="role-editor-view" :hidden="!selected">
    <header class="role-editor-heading">
      <div class="role-title-line">
        <span id="role-color-dot" class="role-color-dot" aria-hidden="true" :style="{ '--role-color': preview.color }"></span>
        <div>
          <h3 id="role-editor-title">{{ preview.name }}</h3>
          <p v-if="selected?.purpose" class="hint">{{ permissionWords(selected.purpose) }}</p>
        </div>
      </div>
      <span id="role-managed" class="role-managed" :hidden="!managed">Built-in role</span>
    </header>
    <div class="role-editor-workspace">
      <form id="role-form" @submit.prevent="submitSettings(saveRole)">
        <nav class="role-editor-tabs" role="tablist" aria-label="Role editor">
          <button id="role-tab-appearance" type="button" role="tab" aria-controls="role-appearance-panel" :tabindex="editorTab === 'appearance' ? 0 : -1" :aria-selected="editorTab === 'appearance'" data-role-editor-tab="appearance" @click="roleEditorTab('appearance')" @keydown="onEditorTabKeydown($event, 'appearance')">Details</button>
          <button id="role-tab-permissions" type="button" role="tab" aria-controls="role-permissions-panel" :tabindex="editorTab === 'permissions' ? 0 : -1" :aria-selected="editorTab === 'permissions'" data-role-editor-tab="permissions" @click="roleEditorTab('permissions')" @keydown="onEditorTabKeydown($event, 'permissions')">Permissions</button>
          <button id="role-tab-members" type="button" role="tab" aria-controls="role-members-panel" :tabindex="editorTab === 'members' ? 0 : -1" :aria-selected="editorTab === 'members'" data-role-editor-tab="members" @click="roleEditorTab('members')" @keydown="onEditorTabKeydown($event, 'members')">Members <span v-if="!memberProtected" id="role-member-count">{{ memberCount }}</span></button>
        </nav>
        <section id="role-appearance-panel" role="tabpanel" aria-labelledby="role-tab-appearance" :hidden="editorTab !== 'appearance'">
          <div class="role-appearance-layout">
            <div class="role-fields">
              <label>Role name<input v-model="formName" id="role-name" class="input" maxlength="160" required :disabled="managed || pending" @input="markDirty"></label>
              <label>Purpose<textarea v-model="formPurpose" id="role-purpose" class="input" maxlength="1024" rows="3" placeholder="What is this role for?" :disabled="managed || pending" @input="markDirty"></textarea></label>
              <label class="role-color-field"><span>Role color</span><span class="role-color-control"><input v-model="formColor" id="role-color" type="color" value="#a98df0" :disabled="managed || pending" @input="markDirty"><code id="role-color-value">{{ preview.colorValue }}</code></span></label>
              <label class="check-row"><input v-model="formSelfEnrollable" id="role-self-enrollable" type="checkbox" :disabled="managed || pending" @change="markDirty"> Allow participants to self-enroll</label>
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
          <div v-if="retireVisible" class="role-retire">
            <button v-if="!confirmingDelete" id="role-retire" class="btn btn-quiet role-danger" type="button" :disabled="pending" @click="confirmingDelete = true">Delete role</button>
            <section v-else class="role-delete-confirmation" aria-label="Confirm role deletion"><h4>Delete {{ selected?.name }}?</h4><p>Its members will lose the permissions granted by this role.</p><div><button class="btn btn-quiet" type="button" :disabled="pending" @click="confirmingDelete = false">Cancel</button><button class="btn role-danger" type="button" :disabled="pending" @click="retireRole">{{ pending ? 'Deleting…' : 'Delete role' }}</button></div></section>
          </div>
        </section>
        <section id="role-permissions-panel" role="tabpanel" aria-labelledby="role-tab-permissions" :hidden="editorTab !== 'permissions'">
          <label class="role-permission-search"><span class="visually-hidden">Find a permission</span><input v-model="permissionSearch" id="role-permission-search" class="input" type="search" placeholder="Find a permission"></label>
          <div id="role-capabilities" class="role-capabilities">
            <p v-if="descriptorError" class="hint error">Permission details could not be loaded. The role and its members remain available; reload to retry.</p>
            <template v-else>
              <section v-for="section in capabilityGroups" :key="section.group" class="role-capability-group">
                <h4>{{ section.label }} <span>{{ section.values.filter(capability => draftGrantKeys.has(capability.key)).length }} of {{ section.values.length }} on</span></h4>
                <div class="role-capability-list">
                  <label v-for="capability in section.values" :key="capability.key" class="role-capability" :class="{ 'is-changed': grantChanged(capability.key) }">
                    <span><strong>{{ permissionWords(capability.label || capability.key) }} <span v-if="grantChanged(capability.key)" class="role-kind">Changed</span></strong><small>{{ permissionDescription(capability) }}</small></span>
                    <input type="checkbox" role="switch" :aria-label="permissionWords(capability.label || capability.key)" :aria-checked="draftGrantKeys.has(capability.key)" :value="capability.key" :checked="draftGrantKeys.has(capability.key)" :disabled="managed || pending" @change="toggleGrant(capability.key, ($event.target as HTMLInputElement).checked)">
                  </label>
                </div>
              </section>
              <p v-if="!capabilityGroups.length" class="hint">No permissions match that search.</p>
            </template>
          </div>
        </section>
        <section id="role-members-panel" role="tabpanel" aria-labelledby="role-tab-members" :hidden="editorTab !== 'members'">
          <div class="role-members-heading">
            <div>
              <h4>Members <span v-if="!memberProtected" id="role-members-heading-count">{{ memberHeadingCount }}</span></h4>
              <p id="role-members-scope" class="hint">{{ memberProtected ? 'Membership follows identity.' : 'Direct assignments in this scope.' }}</p>
            </div>
          </div>
          <label v-if="!memberProtected" class="role-member-search">
            <span class="visually-hidden">Search members</span>
            <input v-model="memberSearchTerm" id="role-member-search" class="input" type="search" placeholder="Find someone by exact handle or DID" :disabled="memberSearchDisabled" @input="onMemberSearchInput">
          </label>
          <div id="role-member-protected" class="role-member-protected" :hidden="!memberProtected">
            <strong>Membership is automatic</strong>
            <p>{{ selected?.id === 'role:owner' ? 'The Space owner is designated by Space administration.' : selected?.id === 'role:participant' ? 'Every signed-in participant holds this role.' : 'Everyone holds this role.' }} It cannot be granted or removed here.</p>
          </div>
          <div v-if="!memberProtected" id="role-members" class="role-members">
            <p v-if="selected?._new" class="role-member-empty">Create this role before managing members.</p>
            <p v-else-if="!memberItems.length" class="role-member-empty">{{ memberSearchTerm.trim() ? 'No participant matches that exact handle or DID.' : memberCount ? 'No members on this page.' : 'No direct members in this role yet.' }}</p>
            <div v-for="person in memberItems" v-else :key="(person.id as string)" class="role-member" :style="{ '--role-color': roleColor(selected?.presentation?.color) }">
              <span class="role-member-avatar" :class="{ 'is-agent': String(person.classification).toLowerCase() === 'agent' }">
                <img v-if="hasAvatar(person)" :src="(person.avatar as string)" alt="" loading="lazy" referrerpolicy="no-referrer">
                <template v-else>{{ personLabel(person).slice(0, 1).toUpperCase() }}</template>
              </span>
              <span>
                <strong>{{ personLabel(person) }} <span v-if="String(person.classification).toLowerCase() === 'agent'" class="agent-badge">Agent</span></strong>
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
          <div v-if="!memberSearchTerm.trim() && (memberPage > 1 || memberNextPage)" class="role-member-pages">
            <button class="btn btn-quiet" type="button" :disabled="memberPage <= 1" @click="changeMemberPage(-1)">Previous members</button>
            <span>Page {{ memberPage }}</span>
            <button class="btn btn-quiet" type="button" :disabled="!memberNextPage" @click="changeMemberPage(1)">More members</button>
          </div>
          <p id="role-members-status" class="hint" role="status" :class="{ error: membersStatusError }">{{ membersStatus }}</p>
        </section>
        <div id="role-editor-actions" class="role-editor-actions" :hidden="!editorActionsVisible">
          <button v-if="!coordinated" id="role-save" class="btn btn-primary" type="submit" :disabled="!dirty || pending">{{ pending ? 'Saving…' : saveLabel }}</button>
          <button v-if="!coordinated" id="role-reset" class="btn btn-quiet" type="button" :hidden="!resetVisible" :disabled="!dirty || pending" @click="resetRole">Discard</button>
          <p id="role-status" role="status" :class="{ error: roleStatusError }">{{ roleStatus }}</p>
        </div>
      </form>
    </div>
  </section>
  </div>
</template>
<style src="../assets/roles.css"></style>
