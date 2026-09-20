<script setup lang="ts">
// The settings page, ported from index.html's settings section with access.js,
// roles.js, people.js and rooms.js's server form. Same tabs, ids, classes, forms.
import { computed } from 'vue'
import type { Settings, SettingsTabName } from '../composables/settings'
import RolesPanel from './RolesPanel.vue'
import PeoplePanel from './PeoplePanel.vue'
import AccessPanel from './AccessPanel.vue'
import AtmospherePanel from './AtmospherePanel.vue'
import ArtworkPicker from './ArtworkPicker.vue'

import type { Atmosphere } from '../composables/atmosphere'

const props = defineProps<{ settings: Settings; atmosphere: Atmosphere }>()

const {
  context, resource, shellVisible, denied, activeTab, tabsVisible, heading, breadcrumbs, contextHeading,
  accessIntro, accessBadge,
  serverForm, serverCanManage, serverRoleSummary, serverPermissionsSummary, cardImageUrl, serverStatus, saveServer,
  contextForm, contextStatus, contextStatusError, saveContext, settingsTab,
} = props.settings

const signInVisible = computed(() => !shellVisible.value && !denied.value)

const tabGlyphs: Record<SettingsTabName, string> = {
  server: '\u25C7',
  context: '\u25C7',
  roles: '\u25C8',
  access: '\u25C6',
  people: '\u25C9',
}

const tabOrder: SettingsTabName[] = ['server', 'context', 'roles', 'access', 'people']
const visibleTabs = computed(() => tabOrder.filter(name => tabsVisible[name]))

function onTabKeydown(event: KeyboardEvent, name: SettingsTabName) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return
  event.preventDefault()
  const tabs = visibleTabs.value
  const index = tabs.indexOf(name)
  const next = tabs[(index + (['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1) + tabs.length) % tabs.length]!
  settingsTab(next)
  document.getElementById('settings-' + next + '-tab')?.focus()
}
</script>

<template>
  <nav id="settings-breadcrumbs" class="breadcrumbs" aria-label="Breadcrumb">
    <template v-for="(crumb, index) in breadcrumbs" :key="index">
      <a v-if="crumb.href" :href="crumb.href">{{ crumb.label }}</a>
      <span v-else :aria-current="index === breadcrumbs.length - 1 ? 'page' : undefined">{{ crumb.label }}</span>
    </template>
  </nav>
  <header class="settings-page-heading">
    <p id="settings-eyebrow" class="eyebrow">{{ heading.eyebrow }}</p>
    <h1 id="settings-title">{{ heading.title }}</h1>
    <p id="settings-intro">{{ heading.intro }}</p>
  </header>
  <p id="settings-denied" class="hint" :hidden="!denied">{{ denied || 'Server settings are available to this server’s owner and authorized administrators.' }}</p>
  <a id="settings-sign-in" class="btn btn-primary" href="/sign-in/?return=%2Fsettings" :hidden="!signInVisible">Sign in</a>
  <div id="settings-shell" class="settings-shell" :hidden="!shellVisible">
    <nav class="settings-tabs" role="tablist" aria-label="Server settings">
      <button
        v-for="name in visibleTabs"
        :id="'settings-' + name + '-tab'"
        :key="name"
        class="settings-tab"
        type="button"
        role="tab"
        :aria-controls="'settings-' + name + '-panel'"
        :aria-selected="activeTab === name"
        :data-settings-tab="name"
        @click="settingsTab(name)"
        @keydown="onTabKeydown($event, name)"
      >
        <span aria-hidden="true" v-html="tabGlyphs[name]"></span>
        <span>
          <strong v-if="name === 'context'" id="settings-context-label">{{ contextHeading.label }}</strong>
          <strong v-else-if="name === 'server'">Server</strong>
          <strong v-else-if="name === 'roles'">Roles</strong>
          <strong v-else-if="name === 'access'">Access</strong>
          <strong v-else>People</strong>
          <small v-if="name === 'server'">Identity and atmosphere</small>
          <small v-else-if="name === 'context'">Identity and details</small>
          <small v-else-if="name === 'roles'">Permissions and people</small>
          <small v-else-if="name === 'access'">Who can see and act</small>
          <small v-else>Everyone who has arrived</small>
        </span>
      </button>
    </nav>
    <div class="settings-tab-content">
      <section id="settings-server-panel" role="tabpanel" aria-labelledby="settings-server-tab" :hidden="activeTab !== 'server'">
        <details id="server-settings" class="setup" :open="serverCanManage">
          <summary class="visually-hidden">Server settings</summary>
          <form id="server-settings-form" class="stack" @submit.prevent="saveServer">
            <div><p class="eyebrow">Your server’s identity</p><h2>A place people remember.</h2><p class="hint">These details welcome visitors here and travel with your server’s card in their companion collection.</p></div>
            <div class="server-identity-editor">
              <div class="stack">
                <label>Server name<input v-model="serverForm.name" class="input" name="name" maxlength="120" required placeholder="Leo’s Garage"></label>
                <label>Byline<input v-model="serverForm.byline" class="input" name="byline" maxlength="240" placeholder="A few words that feel like you"></label>
                <div class="artwork-upload">
                  <label>Card and cover image URL<input v-model="serverForm.coverImageUrl" class="input" name="coverImageUrl" maxlength="2048" placeholder="https://… or /local-image.png"></label>
                  <ArtworkPicker scope="server" v-model="serverForm.coverImageUrl" />
                </div>
                <label>Description &amp; welcome<textarea v-model="serverForm.welcomeMessage" class="input" name="welcomeMessage" rows="3" maxlength="1000" placeholder="What happens here? Who is this place for?"></textarea></label>
                <label>Message of the day<input v-model="serverForm.motd" class="input" name="motd" maxlength="500" placeholder="A little news for anyone dropping by"></label>
                <div><button type="button" id="server-atmosphere-open" class="btn btn-quiet" aria-controls="atmosphere-panel" :aria-expanded="atmosphere.panelOpen" @click="atmosphere.open()">Choose an atmosphere</button></div>
              </div>
              <aside class="server-card-preview" aria-label="Your server card preview">
                <div class="server-card-art">
                  <img v-if="cardImageUrl" id="server-card-image" :src="cardImageUrl" alt="" referrerpolicy="no-referrer">
                  <span class="eyebrow">Tangent server · ✧</span>
                  <div><span class="server-card-sigil" aria-hidden="true">✦</span><h3 id="server-card-name">{{ serverForm.name.trim() || 'Your place' }}</h3><p id="server-card-byline" :hidden="!serverForm.byline.trim()">{{ serverForm.byline }}</p></div>
                </div>
                <div class="server-card-copy">
                  <p id="server-card-description">{{ serverForm.welcomeMessage.trim() || 'A place for your people and the conversations worth keeping.' }}</p>
                  <div id="server-card-board" :hidden="!serverForm.motd.trim()"><span class="eyebrow">On the board</span><p id="server-card-motd">{{ serverForm.motd }}</p></div>
                  <p class="hint">A small card. Your corner of the world.</p>
                </div>
              </aside>
            </div>
            <AtmospherePanel :atmosphere="atmosphere" />
            <details class="server-policy-editor">
              <summary>Tangent stewardship</summary>
              <div class="stack">
                <label class="check-row"><input v-model="serverForm.allowAgentTangentOwnership" type="checkbox" name="allowAgentTangentOwnership"> Allow agents to own Tangents</label>
                <p class="hint">Who may create Tangents is configured in Access.</p>
                <p id="server-role" class="server-role" :hidden="!serverRoleSummary">{{ serverRoleSummary }}</p>
                <p id="server-permissions-summary" class="hint">{{ serverPermissionsSummary }}</p>
              </div>
            </details>
            <div class="settings-save"><button class="btn btn-primary">Save server settings</button><p id="settings-status" role="status">{{ serverStatus }}</p></div>
          </form>
        </details>
      </section>
      <section id="settings-context-panel" class="settings-context-panel" role="tabpanel" aria-labelledby="settings-context-tab" :hidden="activeTab !== 'context'">
        <div class="settings-context-card">
          <p id="settings-context-kicker" class="eyebrow">{{ contextHeading.kicker }}</p>
          <h2 id="settings-context-name">{{ contextHeading.name }}</h2>
          <p id="settings-context-copy" class="lede">{{ contextHeading.copy }}</p>
          <div id="settings-context-editor">
            <form v-if="context?.kind === 'tangent'" class="settings-context-form stack" @submit.prevent="saveContext">
              <div class="settings-context-fields">
                <label class="settings-field"><span>Tangent name</span><input v-model="contextForm.name" class="input settings-control" name="name" maxlength="120" required></label>
                <label class="settings-field"><span>Description</span><textarea v-model="contextForm.description" class="input settings-control" name="description" rows="4" maxlength="1000"></textarea></label>
                <label class="settings-field"><span>Motto</span><input v-model="contextForm.motto" class="input settings-control" name="motto" maxlength="240"></label>
                <label class="settings-field"><span>Accent color</span><input v-model="contextForm.accent" class="input settings-control" name="accent" type="color"></label>
                <label class="settings-field"><span>Artwork URL</span><input v-model="contextForm.artwork" class="input settings-control" name="artwork" maxlength="2048"></label>
              </div>
              <div class="settings-context-actions">
                <button class="btn btn-primary" type="submit">Save details</button>
                <a class="btn btn-quiet" :href="resource?.open">Open</a>
                <p class="hint" role="status" :class="{ error: contextStatusError }">{{ contextStatus }}</p>
              </div>
            </form>
            <form v-else-if="context?.kind === 'topic'" class="settings-context-form stack" @submit.prevent="saveContext">
              <div class="settings-context-fields">
                <label class="settings-field"><span>Topic title</span><input v-model="contextForm.title" class="input settings-control" name="title" maxlength="120" required></label>
                <label class="settings-field"><span>Topic description</span><textarea v-model="contextForm.topic" class="input settings-control" name="topic" rows="5" maxlength="2000"></textarea></label>
                <label class="check-row settings-check"><input v-model="contextForm.allowPostEditing" type="checkbox" name="allowPostEditing"> Allow authors to edit their Posts</label>
                <label class="check-row settings-check"><input v-model="contextForm.isLocked" type="checkbox" name="isLocked"> Lock this Topic</label>
              </div>
              <div class="settings-context-actions">
                <button class="btn btn-primary" type="submit">Save details</button>
                <a class="btn btn-quiet" :href="resource?.open">Open</a>
                <p class="hint" role="status" :class="{ error: contextStatusError }">{{ contextStatus }}</p>
              </div>
            </form>
          </div>
        </div>
      </section>
      <section id="settings-roles-panel" class="roles-panel" role="tabpanel" aria-labelledby="settings-roles-tab" :hidden="activeTab !== 'roles'">
        <header class="roles-heading"><div><p class="eyebrow">ACCESS, WITHOUT MYSTERY</p><h2>Roles</h2><p>Group people by responsibility, then give each role only what it needs.</p></div></header>
        <p id="role-scope-note" class="role-scope-note hint">These are server roles. They can be used by Access rules throughout this Tangent Space.</p>
        <RolesPanel :settings="settings" />
      </section>
      <section id="settings-people-panel" role="tabpanel" aria-labelledby="settings-people-tab" :hidden="activeTab !== 'people'">
        <header class="roles-heading"><div><p class="eyebrow">WHO IS HERE</p><h2>People</h2><p>Everyone who has arrived on this server, newest last. Roles are granted on the Roles tab.</p></div></header>
        <PeoplePanel :settings="settings" />
      </section>
      <section id="settings-access-panel" class="access-panel" role="tabpanel" aria-labelledby="settings-access-tab" :hidden="activeTab !== 'access'">
        <header class="access-heading">
          <div>
            <p class="eyebrow">A CLEAR DOOR</p>
            <h2>Access</h2>
            <p id="access-intro">{{ accessIntro }}</p>
          </div>
          <span id="access-scope-badge" class="access-scope-badge">{{ accessBadge }}</span>
        </header>
        <AccessPanel :settings="settings" />
      </section>
    </div>
  </div>
</template>
