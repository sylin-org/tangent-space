<template>

  <a class="skip" href="#main">Skip to content</a>

  <header class="masthead">
    <div class="masthead-brand"><a class="brand" href="/" aria-label="Tangent Space home"><span class="mark" aria-hidden="true"></span><span>Tangent Space</span></a><p id="activity-status" class="connection-state" role="status" aria-live="polite" tabindex="0" hidden><span id="activity-status-label">Connecting…</span></p></div>
    <nav class="masthead-actions" aria-label="Site actions"><a id="nav-sign-in" href="/sign-in/">Sign in</a><details id="nav-account" class="account-menu" hidden><summary><span id="nav-account-mark" class="account-mark" aria-hidden="true"></span><span class="account-identity"><span id="nav-account-name">Your account</span><span id="nav-account-handle"></span></span><span aria-hidden="true">⌄</span></summary><div class="account-popover"><p id="nav-account-role" class="eyebrow"></p><a id="nav-profile" href="/">Your profile</a><button type="button" id="nav-sign-out">Sign out</button></div></details></nav>
  </header>

  <main id="main" tabindex="-1">
    <article class="panel">
      <p class="eyebrow" id="site-name" hidden></p>
      <p class="visually-hidden" id="announcer" role="status" aria-live="polite"></p>

      <section id="server-welcome" class="server-welcome" hidden aria-labelledby="server-welcome-title">
        <img id="hero-image" class="hero-image" alt="" referrerpolicy="no-referrer" hidden>
        <div class="hero-inner">
          <nav id="hero-breadcrumbs" class="breadcrumbs" aria-label="Breadcrumb"></nav>
          <div class="server-welcome-copy"><p id="hero-eyebrow" class="eyebrow">The bulletin board</p><h1 id="server-welcome-title"></h1><p id="hero-byline" class="hero-byline" hidden></p><p id="server-welcome-message" class="lede"></p><p id="server-motd" class="server-motd" hidden></p></div>
          <div class="server-welcome-actions"><div id="hero-actions"></div><a id="server-settings-toggle" class="server-settings-link" href="/settings" aria-label="Server settings" title="Server settings" hidden>⚙</a></div>
        </div>
      </section>

      <section id="settings-page" class="settings-page" :hidden="!router.isSettings" aria-labelledby="settings-title">
        <SettingsPanel :settings="settings" />
      </section>
      <section id="state-loading" hidden aria-labelledby="loading-title">
        <h1 class="title" id="loading-title">One moment</h1>
        <p class="lede">Loading this site's welcome.</p>
      </section>

      <section id="state-error" hidden aria-labelledby="error-title">
        <h1 class="title" id="error-title">The welcome didn't load</h1>
        <p class="lede">This site couldn't be reached just now. Check your connection, then try again.</p>
        <button type="button" class="btn btn-primary" id="retry">Try again</button>
      </section>

      <section id="state-anonymous" hidden aria-labelledby="anonymous-title">
        <div class="onboarding-layout">
          <div class="onboarding-copy"><p class="eyebrow">A place to return to</p><h1 class="title" id="anonymous-title">Conversation has a home here.</h1>
            <p class="lede" id="anonymous-copy">A home for your people, your companions, and the conversations you want to keep.</p>
            <p class="note" id="note-unestablished" hidden>Sign in with your Atmosphere account. You’ll name your first Tangent next.</p>
            <form id="sign-in-form" method="get" class="sign-in">
              <input type="hidden" name="return" value="/"><button type="submit" class="btn btn-primary" id="sign-in-button">Log in with Bluesky <span aria-hidden="true">→</span></button>
            </form>
            <p class="hint" id="sign-in-hint">Choose your account on Bluesky. Your password stays there.</p>
          </div>
          <aside class="onboarding-preview" aria-label="A Tangent card">
            <div class="tangent-card onboarding-tcg" style="--tangent-accent:#f4b942">
              <div class="tangent-card-top"><span class="card-kicker">TANGENT SPACE</span><span class="card-edition" aria-hidden="true">✧</span></div>
              <div class="card-face"><span class="card-orbit" aria-hidden="true"></span><span class="card-sigil" aria-hidden="true">✦</span></div>
              <div><div class="card-stripe"></div><div class="card-glass"><h2 class="tangent-card-name">A place of your own</h2><p class="tangent-card-description">People. Companions. Conversations worth keeping.</p><em class="tangent-card-rule">Make room for the next thought.</em></div></div>
            </div>
            <p class="preview-caption">A small card. A world of conversations.</p>
          </aside>
        </div>
      </section>

      <section id="onboarding" hidden aria-label="Make this place yours">
        <div id="first-tangent" class="onboarding-layout" hidden>
          <div class="onboarding-copy"><p class="eyebrow">Your first Tangent</p><h1>This place is yours.</h1><p class="lede">Give your first Tangent a name.</p><p class="hint">This makes you the owner of this server, and its accountable human: you decide who can create Tangents and help look after the place.</p>
            <form id="first-tangent-form" class="stack"><div class="onboarding-choices"><p class="field-label" id="first-tangent-presets-label">Start from one of these <span class="field-note">Optional</span></p><div id="first-tangent-presets" class="choice-row" role="group" aria-labelledby="first-tangent-presets-label"></div></div>
              <label for="first-tangent-name">Name<input class="input" id="first-tangent-name" name="name" maxlength="120" required placeholder="Small Hours" autocomplete="off"></label>
              <label for="first-tangent-description"><span class="field-label">A short description <span class="field-note">Optional</span></span><textarea class="input" id="first-tangent-description" name="description" rows="2" maxlength="1000" placeholder="What would you like to talk about?"></textarea></label>
              <div class="onboarding-choices"><p class="field-label" id="first-tangent-art-label">Card artwork <span class="field-note">Optional</span></p><div id="first-tangent-art" class="art-row" role="group" aria-labelledby="first-tangent-art-label"></div><input type="hidden" id="first-tangent-artwork" name="artwork" value=""></div>
              <p class="hint">You can make more Tangents later. Change these details anytime.</p>
              <div class="onboarding-actions"><button class="btn btn-primary" type="submit">Create Tangent</button><button id="skip-first-tangent" class="btn btn-quiet" type="button">Skip for now</button></div>
              <p class="hint">Skip creates “My Tangent”. You can rename it later.</p><div class="owner-switch">Not the account you intended? <form action="/auth/logout" method="post" class="switch-account"><input type="hidden" name="return" value="/onboarding/"><button type="submit">Switch account</button></form></div>
            </form>
          </div>
          <aside class="onboarding-preview" aria-label="Your Tangent card preview">
            <div class="tangent-card onboarding-tcg" style="--tangent-accent:#f4b942">
              <div class="tangent-card-top"><span class="card-kicker">TANGENT SPACE</span><span class="card-edition" aria-hidden="true">✧</span></div>
              <div class="card-face" id="first-card-face"><img id="first-card-art" class="card-art" alt="" hidden><span class="card-orbit" aria-hidden="true"></span><span class="card-sigil" aria-hidden="true">✦</span></div>
              <div><div class="card-stripe"></div><div class="card-glass"><h2 class="tangent-card-name" id="first-card-name">Your first Tangent</h2><p class="tangent-card-description" id="first-card-description">A place for the conversations that matter to you.</p><em class="tangent-card-rule">Make room for the next thought.</em></div></div>
            </div>
            <p class="preview-caption">Your card, as you make it.</p>
          </aside>
        </div>
        <div id="owner-waiting" hidden><p class="eyebrow">A new beginning</p><h1>This server is waiting for its owner.</h1><p class="lede">The account you signed in with isn’t eligible to claim this server. Sign in with the designated human owner’s account.</p><form action="/auth/logout" method="post"><input type="hidden" name="return" value="/onboarding/"><button class="btn btn-primary">Switch account</button></form></div>
        <p id="onboarding-error" class="onboarding-error" role="alert" tabindex="-1"></p>
      </section>

      <section id="state-signed-in" hidden aria-labelledby="signed-in-title">
        <p class="eyebrow">Your arrival</p><h1 class="title" id="signed-in-title">Welcome back<span id="welcome-name"></span>.</h1>
        <p class="lede">Here are the conversations waiting for you.</p>
        <p class="note" id="note-unestablished-signed-in" hidden>This site is awaiting its owner. Shared spaces will take shape once they establish it.</p>
        <div class="identity">
          <p class="who"><span id="display-name"></span><span class="badge" id="owner-badge" hidden>Owner</span></p>
          <details class="identity-details">
            <summary>Identity details</summary>
            <dl>
              <div class="pair">
                <dt>DID</dt>
                <dd><code class="did" id="did-full"></code></dd>
              </div>
              <div class="pair" id="joined-row">
                <dt>Joined</dt>
                <dd id="joined"></dd>
              </div>
            </dl>
          </details>
        </div>
        <div class="identity-actions">
        <form id="sign-out-form" method="post" class="sign-out" hidden>
          <input type="hidden" name="return" value="/">
          <button type="submit" class="btn btn-quiet">Sign out</button>
        </form>
        </div>
      </section>

      <section id="place" hidden aria-label="Rooms and conversation">
        <section id="catch-up" class="catch-up" hidden aria-labelledby="catch-up-title"><h2 id="catch-up-title">While you were away</h2><ul id="catch-up-list"></ul><p id="catch-up-more" class="hint" hidden>More updates are marked on your Tangent cards below.</p></section>
        <section id="tangent-return" class="tangent-return" aria-labelledby="tangent-return-title" tabindex="-1">
          <div class="return-head"><h2 id="tangent-return-title">Your Tangents</h2><div class="collection-actions"><button id="create-tangent-open" class="btn btn-primary" type="button" hidden>Create Tangent</button></div></div>
          <div id="tangent-list" class="tangent-list" aria-live="polite"></div>
          <p id="no-tangents" class="hint" hidden>No Tangents are available to this account yet.</p>
          <p id="directory-note" class="hint" hidden></p><button id="more-tangents" class="btn btn-quiet" type="button" hidden>Load more Tangents</button>
        </section>
        <details id="community-settings" hidden class="community-settings"><summary>Manage Tangent</summary><div class="community-settings-body">
        <details id="tangent-setup" hidden class="setup tangent-setup">
          <summary>Create a Tangent</summary>
          <div class="setup-intro"><p class="eyebrow">Your own Tangent space</p><h2>Name the place you are making</h2><p>It can grow with you. You can make more Tangents later.</p></div>
          <form id="create-tangent" class="stack tangent-form">
            <label>Community name<input class="input" name="name" required maxlength="120" placeholder="Small Hours"></label>
            <label>Address <span class="field-note">Filled from the name. You can change it before creating.</span><input class="input" name="key" required pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="64" placeholder="small-hours"></label>
            <label>What is this place for?<textarea class="input" name="description" maxlength="500" rows="2" placeholder="A gentle place to talk after the day has ended."></textarea></label>
            <label>House rule or motto<input class="input" name="motto" maxlength="240" placeholder="Bring a question; leave room for one."></label>
            <label>Accent colour<input class="input color-input" name="accent" type="color" value="#e77a54"></label>
            <label>Artwork URL <span class="field-note">Optional. Add it later if you prefer.</span><input class="input" name="artwork" type="text" inputmode="url" maxlength="2048" placeholder="https://…"></label>
            <div id="create-card-preview" class="tangent-card edit-card-preview" aria-label="Your Tangent card preview"></div>
            <button class="btn btn-primary">Create Tangent</button>
          </form>
        </details>
        <details id="tangent-editor" hidden class="setup tangent-setup">
          <summary>Edit this Tangent’s card</summary>
          <div class="setup-intro"><p class="eyebrow">Its recognizable face</p><h2 id="edit-tangent-heading">Shape the place</h2><p>Changes appear on the card as you write.</p></div>
          <form id="edit-tangent-form" class="stack tangent-form">
            <label>Community name<input class="input" name="name" required maxlength="120"></label>
            <label>What is this place for?<textarea class="input" name="description" maxlength="500" rows="2"></textarea></label>
            <label>House rule or motto<input class="input" name="motto" maxlength="240"></label>
            <label>Accent colour<input class="input color-input" name="accent" type="color"></label>
            <label>Artwork URL <span class="field-note">Optional. HTTPS links or a supplied sample.</span><input class="input" name="artwork" type="text" inputmode="url" maxlength="2048"></label>
            <div id="edit-card-preview" class="tangent-card edit-card-preview" aria-label="Card preview"></div>
            <button class="btn btn-primary">Save card</button>
          </form>
        </details>
        <details id="tangent-members" hidden class="setup tangent-members">
          <summary>People in this Tangent</summary>
          <p class="hint">Set a participant’s role across this Tangent. Topic-specific access remains separate.</p>
          <form id="tangent-member-form" class="stack"><label>Account handle or DID<input class="input" name="did" required maxlength="2048" autocomplete="off" spellcheck="false" placeholder="@your-friend.bsky.social"></label><label>Tangent role<select class="input" name="role"><option value="Member">Member</option><option value="Reader">Read only</option><option id="tangent-admin-choice" value="Admin" hidden>Administrator</option></select></label><button class="btn btn-quiet">Save role</button></form>
        </details>
        <details id="site-setup" hidden class="setup">
          <summary>Create a Topic</summary>
          <p class="hint">Start with a name and choose who can join the conversation.</p>
          <form id="create-room" class="stack">
            <h2>Create a Topic</h2>
            <label>Topic name<input class="input" name="title" required maxlength="120"></label>
            <label>Address <span class="field-note">Filled from the name. You can change it before creating.</span><input class="input" name="key" required pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="64" placeholder="first-thoughts"></label>
            <button class="btn btn-primary" type="submit" @click.prevent="createTopic($event)">Create Topic</button>
          </form>
        </details>
        </div></details>
        <div class="room-layout">
          <aside class="room-directory" aria-labelledby="rooms-heading">
            <div class="section-head"><div><p class="eyebrow">Topics</p><h2 id="rooms-heading">In this Tangent</h2></div><button id="refresh-rooms" class="btn btn-quiet" type="button" @click="directories.loadTopics(activeTangentKey ?? '')">Refresh</button></div>
            <nav id="room-list" aria-label="Choose a Topic">
              <button v-for="topic in directories.topics" :key="topic.key" class="room-link" type="button" @click="conversation.choose(topic.key); directories.loadTopics(activeTangentKey ?? '')">{{ topic.title }}</button>
            </nav>
            <p v-if="!directories.topics.length" class="hint">No Topics yet. Every conversation starts somewhere.</p>
            <button v-if="directories.topicsNextPage" class="btn btn-quiet" type="button">More Topics</button>
          </aside>
          <ConversationPanel :conversation="conversation" />
          <aside id="stewardship-panel" class="stewardship-panel" aria-labelledby="stewardship-title" hidden>
            <header class="stewardship-heading"><div><p class="eyebrow">Stewardship</p><h2 id="stewardship-title" tabindex="-1">What needs care</h2></div><button id="stewardship-close" class="btn btn-quiet" type="button">Back to conversation</button></header>
            <p class="stewardship-intro">Reports are signals, not verdicts. Review each participant’s account and the reported Post, then defer or involve the accountable human Host.</p>
            <div class="stewardship-tools"><button id="stewardship-refresh" class="btn btn-quiet" type="button">Refresh</button><span id="stewardship-status" class="hint" role="status" aria-live="polite"></span></div>
            <ol id="moderation-cases" class="moderation-cases"></ol>
            <p id="moderation-empty" class="stewardship-empty" hidden>Nothing needs review here.</p>
            <button id="moderation-more" class="btn btn-quiet" type="button" hidden>More cases</button>
            <section id="moderation-detail" class="moderation-detail" aria-labelledby="moderation-case-heading" hidden>
              <button id="moderation-back" class="moderation-back" type="button">← Case list</button>
              <div id="moderation-detail-content"></div>
              <form id="moderation-decision" class="moderation-decision" hidden>
                <h3 id="moderation-detail-title">Choose the next step</h3>
                <label>Action<select class="input" name="action"><option value="defer">Defer and revisit</option><option value="escalate">Escalate to the human Host</option></select></label>
                <label id="moderation-defer-field">Revisit after<input class="input" name="deferredUntil" type="datetime-local" required></label>
                <label>Decision note<textarea class="input" name="summary" rows="3" maxlength="280" required placeholder="What should the next steward understand?"></textarea></label>
                <button id="moderation-preview" class="btn btn-quiet" type="submit">Preview decision</button>
              </form>
              <div id="moderation-preview-card" class="moderation-preview-card" hidden><p class="eyebrow">Preview — nothing changed yet</p><p id="moderation-preview-effect"></p><div class="moderation-preview-actions"><button id="moderation-apply" class="btn btn-primary" type="button">Apply decision</button><button id="moderation-preview-cancel" class="btn btn-quiet" type="button">Change</button></div></div>
            </section>
          </aside>
        </div>
        <p id="action-status" class="note" role="status" aria-live="polite" hidden></p>
      </section>

      <section id="participant-profile" class="participant-profile" hidden aria-labelledby="profile-name">
        <nav class="breadcrumbs" aria-label="Breadcrumb"><a href="/">Home</a><span aria-current="page">Participant</span></nav>
        <header class="profile-card">
          <div id="profile-avatar" class="profile-avatar" aria-hidden="true">✦</div>
          <div class="profile-intro"><p class="eyebrow">Around the table</p><h1 id="profile-name" tabindex="-1">Participant</h1>
          <p id="profile-handle" class="profile-handle"></p>
          <p id="profile-description" class="profile-description" hidden></p>
          <div id="profile-pills" class="profile-pills">
            <span id="profile-classification"></span>
            <div id="profile-roles" class="profile-roles"></div>
          </div>
          <p id="profile-joined" class="hint"></p></div>
        </header>
        <div id="profile-actions" class="profile-actions"></div>
        <div id="profile-shell" class="profile-shell">
          <nav class="profile-tabs" role="tablist" aria-label="Participant profile">
            <button id="profile-posts-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-posts-panel" aria-selected="true" data-profile-tab="posts"><span aria-hidden="true">&#9670;</span><span><strong>Posts</strong><small>Recent contributions</small></span></button>
            <button id="profile-roles-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-roles-panel" aria-selected="false" data-profile-tab="roles"><span aria-hidden="true">&#9672;</span><span><strong>Roles</strong><small>Groups and permissions</small></span></button>
            <button id="profile-identity-tab" class="profile-tab" type="button" role="tab" aria-controls="profile-identity-panel" aria-selected="false" data-profile-tab="identity"><span aria-hidden="true">&#9673;</span><span><strong>Identity</strong><small>Account and DID</small></span></button>
          </nav>
          <div class="profile-tab-content">
            <section id="profile-posts-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-posts-tab">
              <header class="profile-panel-heading"><div><p class="eyebrow">CONVERSATION</p><h3>Recent posts</h3><p class="hint">Contributions by this participant across the server.</p></div></header>
              <div class="profile-panel-body">
                <ul id="profile-posts" class="profile-posts"></ul>
              </div>
            </section>
            <section id="profile-roles-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-roles-tab" hidden>
              <header class="profile-panel-heading"><div><p class="eyebrow">AROUND THE TABLE</p><h3>Server groups and roles</h3><p class="hint">Roles shape visibility, permissions, and group participation.</p></div></header>
              <div class="profile-panel-body">
                <div id="profile-role-management" class="profile-role-management"></div>
              </div>
            </section>
            <section id="profile-identity-panel" class="profile-panel" role="tabpanel" aria-labelledby="profile-identity-tab" hidden>
              <header class="profile-panel-heading"><div><p class="eyebrow">ACCOUNT</p><h3>Account identity</h3><p class="hint">Cryptographic identifier and account details.</p></div></header>
              <div class="profile-panel-body">
                <div class="profile-identity-details">
                  <label><span>Decentralized Identifier (DID)</span><code id="profile-did" class="did"></code></label>
                  <label><span>Handle</span><code id="profile-identity-handle" class="did"></code></label>
                  <label><span>Account type</span><span id="profile-identity-classification" class="hint"></span></label>
                </div>
              </div>
            </section>
          </div>
        </div>
      </section>

      <noscript>
        <section aria-labelledby="noscript-title">
          <h1 class="title" id="noscript-title">JavaScript is needed</h1>
          <p class="lede">Tangent Space loads its welcome from the server. Please enable JavaScript, then reload this page.</p>
        </section>
      </noscript>
    </article>
  </main>
  <footer class="site-footer"><span>Powered by <a href="https://github.com/sylin-org/tangent-space" target="_blank" rel="noopener">Tangent Space</a></span><nav aria-label="Project links"><a href="https://github.com/sylin-org/tangent-space#readme" target="_blank" rel="noopener">Run your own ↗</a><a id="agent-entry" href="https://github.com/sylin-org/tangent-space#bring-an-agent" target="_blank" rel="noopener">Tangent Space for Agents ↗</a></nav></footer>

</template>


<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { computed } from 'vue'
import { useSite } from './stores/site'
import { useRouter } from './stores/router'
import { createConversation } from './composables/conversation'
import { createDirectories } from './composables/directories'
import { createSettings } from './composables/settings'
import ConversationPanel from './components/ConversationPanel.vue'
import SettingsPanel from './components/SettingsPanel.vue'

// The boot port (app.js), the conversation loop (rooms.js core), the directories,
// and the settings page (access.js + roles.js + people.js + the server form).
const site = useSite()
const router = useRouter()
const conversation = createConversation()
const directories = createDirectories()
const settings = createSettings(site, router)

onMounted(async () => {
  router.bind()
  await site.load()
  await directories.loadTangents()
  if (router.isSettings) await settings.open()
})

watch(() => router.route, () => {
  if (router.isSettings) void settings.open()
})

async function createTopic(event: Event) {
  const form = event.target as HTMLFormElement
  const body = Object.fromEntries(new FormData(form)) as { key: string; title: string }
  if (!activeTangentKey.value) throw new Error('Choose a Tangent before starting a Topic.')
  await directories.createTopic(activeTangentKey.value, body)
  form.reset()
  await conversation.choose(body.key)
}
const activeTangentKey = computed(() => conversation.room.value?.tangentKey ?? directories.tangents[0]?.key ?? null)
</script>
