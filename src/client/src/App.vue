<template>

  <a class="skip" href="#main">Skip to content</a>

  <header class="masthead">
    <MastheadBar :site="site" :router="router" @sign-out="submitSignOut" />
  </header>

  <main id="main" tabindex="-1">
    <article class="panel">
      <p class="eyebrow" id="site-name" :hidden="!site.site">{{ site.site?.name }}</p>
      <p class="visually-hidden" id="announcer" role="status" aria-live="polite">{{ site.announcement }}</p>

      <WelcomeHero :site="site" :router="router" :tangent="routeTangent" :topic="routeTopic" />

      <section id="settings-page" class="settings-page" :hidden="!router.isSettings" aria-labelledby="settings-title">
        <SettingsPanel :settings="settings" />
      </section>

      <section id="state-loading" :hidden="site.state !== 'loading'" aria-labelledby="loading-title">
        <h1 class="title" id="loading-title">One moment</h1>
        <p class="lede">Loading this site's welcome.</p>
      </section>

      <section id="state-error" :hidden="site.state !== 'error'" aria-labelledby="error-title">
        <h1 class="title" id="error-title">The welcome didn't load</h1>
        <p class="lede">This site couldn't be reached just now. Check your connection, then try again.</p>
        <button type="button" class="btn btn-primary" id="retry" @click="site.load()">Try again</button>
      </section>

      <section id="state-anonymous" :hidden="!anonymousVisible" aria-labelledby="anonymous-title">
        <div class="onboarding-layout">
          <div class="onboarding-copy"><p class="eyebrow">A place to return to</p><h1 class="title" id="anonymous-title">{{ anonymousTitle }}</h1>
            <p class="lede" id="anonymous-copy">{{ anonymousCopy }}</p>
            <p class="note" id="note-unestablished" :hidden="router.route.kind !== 'home' || site.established">Sign in with your Atmosphere account. You’ll name your first Tangent next.</p>
            <form id="sign-in-form" method="get" class="sign-in" :action="site.site?.signIn">
              <input type="hidden" name="return" :value="signInReturn"><button type="submit" class="btn btn-primary" id="sign-in-button">Log in with Bluesky <span aria-hidden="true">→</span></button>
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

      <section id="state-signed-in" :hidden="site.state !== 'signedIn' || authenticationRoute" aria-labelledby="signed-in-title">
        <p class="eyebrow">Your arrival</p><h1 class="title" id="signed-in-title">Welcome back<span id="welcome-name">{{ welcomeName }}</span>.</h1>
        <p class="lede">Here are the conversations waiting for you.</p>
        <p class="note" id="note-unestablished-signed-in" :hidden="site.established">This site is awaiting its owner. Shared spaces will take shape once they establish it.</p>
        <div class="identity">
          <p class="who"><span id="display-name">{{ site.displayName }}</span><span class="badge" id="owner-badge" :hidden="!site.site?.participant?.isOwner">Owner</span></p>
          <details class="identity-details">
            <summary>Identity details</summary>
            <dl>
              <div class="pair">
                <dt>DID</dt>
                <dd><code class="did" id="did-full">{{ site.site?.participant?.did || site.site?.participant?.participantRef }}</code></dd>
              </div>
              <div class="pair" id="joined-row" :hidden="!site.joinedLabel">
                <dt>Joined</dt>
                <dd id="joined">{{ site.joinedLabel }}</dd>
              </div>
            </dl>
          </details>
        </div>
        <div class="identity-actions">
        <form id="sign-out-form" ref="signOutForm" method="post" class="sign-out" :hidden="!site.site?.signOut" :action="site.site?.signOut ?? ''">
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

      <section id="participant-profile" class="participant-profile" :hidden="router.route.kind !== 'participant'" aria-labelledby="profile-name">
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
import { computed, onMounted, ref, watch, watchEffect } from 'vue'
import { useSite } from './stores/site'
import { useRouter } from './stores/router'
import { createConversation } from './composables/conversation'
import { createDirectories } from './composables/directories'
import { createSettings } from './composables/settings'
import ConversationPanel from './components/ConversationPanel.vue'
import SettingsPanel from './components/SettingsPanel.vue'
import WelcomeHero from './components/WelcomeHero.vue'
import MastheadBar from './components/MastheadBar.vue'

// The boot port (app.js), the conversation loop (rooms.js core), the directories,
// the settings page, and the hero + masthead (pages.js prepare/hero).
const site = useSite()
const router = useRouter()
const conversation = createConversation()
const directories = createDirectories()
const settings = createSettings(site, router)

// body[data-page] drives the page shell in the CSS (pages.css).
watchEffect(() => {
  document.body.dataset.page = router.route.kind
  document.body.dataset.signedIn = String(!!site.site?.participant)
})

onMounted(async () => {
  router.bind()
  await site.load()
  directories.loadTangents().catch(() => {})
  site.loadServer().catch(() => {})
  if (router.isSettings) await settings.open()
})

watch(() => router.route, () => {
  if (router.isSettings) void settings.open()
})

const authenticationRoute = computed(() => ['sign-in', 'onboarding'].includes(router.route.kind))

// Anonymous visitors see the welcome copy only where they are asked to sign in;
// everywhere else the hero carries the sign-in action (pages.js prepare()).
const anonymousVisible = computed(() => authenticationRoute.value && site.state !== 'loading' && !site.site?.participant)
const anonymousTitle = computed(() => (router.route.kind === 'sign-in' ? 'Welcome back.' : 'Conversation has a home here.'))
const anonymousCopy = computed(() =>
  router.route.kind === 'sign-in' ? 'Sign in to ' + (site.site?.name ?? 'this server') + ' with your Atmosphere account.' : 'A home for your people, your companions, and the conversations you want to keep.',
)

const signInReturn = computed(() => {
  if (router.route.kind === 'onboarding') return '/onboarding/'
  const requested = new URLSearchParams(window.location.search).get('return')
  return requested && /^\/(?!\/)/.test(requested) && !/[\\\u0000-\u001f]/.test(requested) ? requested : '/'
})

const welcomeName = computed(() => {
  const handle = site.site?.participant?.handle
  return handle && handle.length > 0 ? ', ' + handle : ''
})

const signOutForm = ref<HTMLFormElement | null>(null)

function submitSignOut() {
  signOutForm.value?.requestSubmit()
}

// The hero's Tangent and Topic come from the route when the directory or the
// conversation holds them (rooms.js updateHero's lookup).
const routeTangent = computed(() => {
  const key = 'tangent' in router.route ? router.route.tangent : undefined
  return (key && directories.tangents.find(t => t.key === key)) || null
})
const routeTopic = computed(() => (['topic', 'post'].includes(router.route.kind) ? conversation.room.value : null))

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
