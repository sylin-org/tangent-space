<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { registerSettingsChange, useSettingsSubmit } from '../composables/settings-changes'
import { useSite } from '../stores/site'
import type { Settings } from '../composables/settings'
import type { Atmosphere } from '../composables/atmosphere'
import SettingsWorkspaceHeader from './SettingsWorkspaceHeader.vue'
import SpaceHero from './SpaceHero.vue'
import SettingsSectionTabs from './SettingsSectionTabs.vue'
import ArtworkPicker from './ArtworkPicker.vue'
import AtmospherePanel from './AtmospherePanel.vue'
import PeoplePanel from './PeoplePanel.vue'
import RolesPanel from './RolesPanel.vue'
import AccessPanel from './AccessPanel.vue'
import ContentSourcesPanel from './ContentSourcesPanel.vue'
import BlueskyBridgePanel from './BlueskyBridgePanel.vue'
import DatabasePanel from './DatabasePanel.vue'
import MediaStoragePanel from './MediaStoragePanel.vue'
import SpaceJoining from './SpaceJoining.vue'

const submitSettings = useSettingsSubmit()
const props = defineProps<{ settings: Settings; atmosphere: Atmosphere; expectedDid: string }>()
const emit = defineEmits<{ return: [] }>()
const site = useSite()
const {
  activeTab, settingsTab, serverForm, serverSaved, serverDirty, serverSaving, serverUploading,
  serverStorageAvailable, serverStatus, saveServer, discardServer, cardImageUrl, actorKey,
} = props.settings
const imagePickerOpen = ref(false)
const markPickerOpen = ref(false)
const imageUrlOpen = ref(false)
const signInHref = computed(() => '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search))
const tabs = [
  { id: 'server', label: 'General' },
  { id: 'appearance', label: 'Appearance' },
  { id: 'people', label: 'People' },
  { id: 'roles', label: 'Roles' },
  { id: 'access', label: 'Access' },
  { id: 'sources', label: 'Sources' },
  { id: 'storage', label: 'Storage' },
]

const visibleTabs = computed(() => props.settings.peopleOnly?.value ? tabs.filter(tab => tab.id === 'people') : tabs)

watch([activeTab, () => props.settings.shellVisible.value, () => props.atmosphere.canManage], ([tab, visible, authorized]) => {
  if ((tab === 'server' || tab === 'appearance') && visible && authorized) props.atmosphere.open()
  else props.atmosphere.close()
}, { immediate: true })
onUnmounted(() => props.atmosphere.close())
const coordinated = registerSettingsChange({ label: 'Space details', count: () => props.settings.serverCanManage.value ? Object.keys(serverForm).filter(field => serverForm[field as keyof typeof serverForm] !== serverSaved.value[field as keyof typeof serverForm]).length : 0, busy: () => serverSaving.value || serverUploading.value, save: saveServer, discard: discardServer, status: () => serverStatus.value })
registerSettingsChange({ label: 'Appearance', count: () => props.atmosphere.canManage && props.atmosphere.dirty ? 1 : 0, busy: () => props.atmosphere.saving, save: props.atmosphere.save, discard: props.atmosphere.resetPreview, status: () => props.atmosphere.statusMessage })
</script>

<template>
  <div class="host-settings-workspace">
    <SettingsWorkspaceHeader eyebrow="SPACE SETTINGS" :name="serverSaved.name || site.site?.name || 'Space settings'" back-label="Space" @back="emit('return')" />
    <p v-if="settings.loading.value || site.state === 'loading' || (!settings.shellVisible.value && site.site?.participant && !settings.denied.value)" role="status" class="hint">Loading Space settings…</p>
    <p v-else-if="settings.denied.value" role="status" class="hint error">{{ settings.denied.value }}</p>
    <a v-else-if="!settings.shellVisible.value" class="btn btn-primary" :href="signInHref">Sign in to manage this Space</a>
    <template v-else>
      <SettingsSectionTabs :tabs="visibleTabs" :active="activeTab" tab-prefix="settings" label="Space settings" @select="settingsTab" />
      <section v-if="!settings.peopleOnly?.value" id="settings-server-panel" class="settings-workspace-section" role="tabpanel" aria-labelledby="settings-server-tab" v-show="activeTab === 'server'">
        <header><h2>General</h2><p>The name and welcome visitors see.</p></header>
        <div class="host-identity-layout">
          <form id="server-settings-form" class="host-identity-form" @submit.prevent="submitSettings(saveServer)">
            <div class="host-image-setting"><span class="host-image-preview"><img v-if="serverForm.markImageUrl" :src="serverForm.markImageUrl" alt=""></span><div><strong>Mark</strong><p>Beside the name in the header and on the landing page.</p><button type="button" class="btn btn-quiet" @click="markPickerOpen = !markPickerOpen">Replace</button><button v-if="serverForm.markImageUrl" class="btn btn-quiet" type="button" @click="serverForm.markImageUrl = ''">Remove</button></div></div><ArtworkPicker v-if="markPickerOpen" scope="server" v-model="serverForm.markImageUrl" @uploading="serverUploading = $event" />
            <div class="host-image-setting"><span class="host-image-preview"><img v-if="cardImageUrl" :src="cardImageUrl" alt="" referrerpolicy="no-referrer"><span v-else aria-hidden="true">{{ (serverForm.name || 'S').slice(0, 1).toUpperCase() }}</span></span><div><strong>Landing image</strong><p>Behind the welcome on the landing page.</p><div class="host-image-actions"><button type="button" class="btn btn-quiet" :aria-expanded="imagePickerOpen" @click="imagePickerOpen = !imagePickerOpen">{{ serverForm.coverImageUrl ? 'Replace' : 'Choose image' }}</button><button v-if="serverForm.coverImageUrl" type="button" class="btn btn-quiet" @click="serverForm.coverImageUrl = ''">Remove</button></div></div></div>
            <div v-show="imagePickerOpen" class="host-image-picker"><ArtworkPicker :key="actorKey" scope="server" v-model="serverForm.coverImageUrl" :disabled="serverSaving" @uploading="serverUploading = $event" /><button type="button" class="host-url-toggle" :aria-expanded="imageUrlOpen" @click="imageUrlOpen = !imageUrlOpen">Use an image URL</button><label v-if="imageUrlOpen">HTTPS image URL<input v-model="serverForm.coverImageUrl" class="input" type="url" maxlength="2048" placeholder="https://…" :disabled="serverSaving || serverUploading"><small>Changes show in the preview until you save.</small></label></div>
            <label>Name<input v-model="serverForm.name" class="input" maxlength="120" required placeholder="Your place"></label>
            <label>Byline<input v-model="serverForm.byline" class="input" maxlength="240" placeholder="A few words that feel like you"></label>
            <label>Description and welcome<textarea v-model="serverForm.welcomeMessage" class="input" rows="3" maxlength="1000" placeholder="What happens here? Who is this place for?"></textarea></label>
            <label>Message of the day<input v-model="serverForm.motd" class="input" maxlength="500" placeholder="A little news for anyone dropping by"></label>
            <div class="host-stewardship"><h3>Tangent stewardship</h3><label class="check-row"><input v-model="serverForm.allowAgentTangentOwnership" type="checkbox"> Agents can own Tangents</label><p class="hint">Who can create Tangents is set in Access.</p></div>
          </form>
          <aside class="host-preview-column" aria-label="Space identity preview"><p class="eyebrow">Landing page</p><SpaceHero :name="serverForm.name || 'Your Space'" :image="cardImageUrl" :mark="serverForm.markImageUrl" :byline="serverForm.byline" :welcome="serverForm.welcomeMessage" :message="serverForm.motd" preview /><p class="hint">How the Space greets visitors. Add a byline to put a line under the name.</p></aside>
          <div v-if="!coordinated" class="settings-workspace-savebar host-identity-savebar"><button class="btn btn-primary" type="submit" form="server-settings-form" :disabled="!serverDirty || serverSaving || serverUploading">{{ serverSaving ? 'Saving…' : 'Save changes' }}</button><button class="btn btn-quiet" type="button" :disabled="!serverDirty || serverSaving || serverUploading" @click="discardServer">Discard</button><p role="status">{{ serverStatus || (serverDirty ? serverStorageAvailable ? 'Unsaved edits are kept in this browser tab.' : 'Unsaved edits' : '') }}</p></div>
        </div>
        
      </section>
      <section v-if="!settings.peopleOnly?.value" id="settings-appearance-panel" class="settings-workspace-section" v-show="activeTab === 'appearance'" aria-label="Appearance"><header><h2>Appearance</h2><p>The atmosphere and accent of this Space.</p></header><AtmospherePanel :atmosphere="atmosphere" :active="activeTab === 'appearance'" coordinated /><fieldset class="space-accent-picker"><legend>Space accent</legend><label v-for="tone in ['gold', 'jade', 'iris', 'copper']" :key="tone" :data-accent="tone"><input v-model="serverForm.accent" type="radio" :value="tone"><strong>{{ tone[0]!.toUpperCase() + tone.slice(1) }}</strong><span class="accent-samples"><span class="accent-paper">Reply</span><span class="accent-ink">Reply</span></span></label></fieldset></section>
      <section id="settings-people-panel" class="settings-workspace-section" role="tabpanel" aria-labelledby="settings-people-tab" v-show="activeTab === 'people'"><header><h2>People</h2><p>Everyone who has arrived here. Open someone to see their profile and roles.</p></header><SpaceJoining v-if="site.site?.participant?.isOwner || site.server?.canManageParticipants" :actor-key="actorKey" :owner="!!site.site?.participant?.isOwner" :can-review="!!site.server?.canManageParticipants" requests-only :active="activeTab === 'people'" /><PeoplePanel :settings="settings" /></section>
      <section v-if="!settings.peopleOnly?.value" id="settings-roles-panel" class="settings-workspace-section host-roles-section" role="tabpanel" aria-labelledby="settings-roles-tab" v-show="activeTab === 'roles'"><header><h2>Roles</h2><p>Group people by responsibility. Access rules anywhere in the Space can use these roles.</p></header><RolesPanel :settings="settings" /></section>
      <section v-if="!settings.peopleOnly?.value" id="settings-access-panel" class="settings-workspace-section host-access-section" role="tabpanel" aria-labelledby="settings-access-tab" v-show="activeTab === 'access'"><header><h2>Access</h2><p>Choose who can enter this Space and what they may do here.</p></header><SpaceJoining v-if="site.site?.participant?.isOwner" :actor-key="actorKey" owner /><AccessPanel :settings="settings" /></section>
      <section v-if="!settings.peopleOnly?.value" id="settings-sources-panel" class="settings-workspace-section" role="tabpanel" aria-labelledby="settings-sources-tab" v-show="activeTab === 'sources'"><BlueskyBridgePanel :actor-key="actorKey" :active="activeTab === 'sources'" /><details class="storage-database"><summary>Browse a public account</summary><ContentSourcesPanel :actor-key="actorKey" /></details></section>
      <section v-if="!settings.peopleOnly?.value" id="settings-storage-panel" class="settings-workspace-section" role="tabpanel" aria-labelledby="settings-storage-tab" v-show="activeTab === 'storage'"><MediaStoragePanel :actor-key="actorKey" :active="activeTab === 'storage'" /><details class="storage-database"><summary>Content database</summary><DatabasePanel :key="actorKey" :expected-did="expectedDid" /></details></section>
    </template>
  </div>
</template>
