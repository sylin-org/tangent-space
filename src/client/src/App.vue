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
            <p class="note" id="note-unestablished" :hidden="site.established">Sign in with your Atmosphere account. You’ll name your first Tangent next.</p>
            <form id="sign-in-form" method="get" class="sign-in" :action="site.site?.signIn">
              <input type="hidden" name="return" :value="signInReturn"><button type="submit" class="btn btn-primary" id="sign-in-button">Log in with Bluesky <span aria-hidden="true">→</span></button>
            </form>
            <p class="hint" id="sign-in-hint">{{ onboardingSignIn ? 'Choose your account on Bluesky. That account becomes this server’s owner.' : 'Choose your account on Bluesky. Your password stays there.' }}</p>
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

      <section id="onboarding" :hidden="!onboardingVisible" aria-label="Make this place yours">
        <OnboardingPanel :site="site" :router="router" />
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

      <section id="place" :hidden="!placeVisible" aria-label="Rooms and conversation">
        <section id="catch-up" class="catch-up" hidden aria-labelledby="catch-up-title"><h2 id="catch-up-title">While you were away</h2><ul id="catch-up-list"></ul><p id="catch-up-more" class="hint" hidden>More updates are marked on your Tangent cards below.</p></section>
        <section id="tangent-return" class="tangent-return" aria-labelledby="tangent-return-title" tabindex="-1">
          <div class="return-head"><h2 id="tangent-return-title">Your Tangents</h2><div class="collection-actions"><button id="create-tangent-open" class="btn btn-primary" type="button" :hidden="!(site.site?.participant && directories.tangentsCanCreate && site.site.onboarding === 'complete')" @click="openCreateTangent">Create Tangent</button></div></div>
          <TangentDirectory :directories="directories" :signed-in="!!site.site?.participant" @settings="key => router.navigate('/t/' + encodeURIComponent(key) + '/settings')" />
        </section>
        <details id="community-settings" ref="communitySettings" :hidden="!communitySettingsVisible" class="community-settings"><summary>Manage Tangent</summary><div class="community-settings-body">
        <details id="tangent-setup" ref="tangentSetup" :hidden="!directories.tangentsCanCreate" class="setup tangent-setup">
          <summary>Create a Tangent</summary>
          <div class="setup-intro"><p class="eyebrow">Your own Tangent space</p><h2>Name the place you are making</h2><p>It can grow with you. You can make more Tangents later.</p></div>
          <form id="create-tangent" ref="createTangentForm" class="stack tangent-form" @submit.prevent="submitCreateTangent" @input="autoKey">
            <label>Community name<input class="input" name="name" required maxlength="120" placeholder="Small Hours"></label>
            <label>Address <span class="field-note">Filled from the name. You can change it before creating.</span><input class="input" name="key" required pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="64" placeholder="small-hours" data-automatic="true" @input="manualKey"></label>
            <label>What is this place for?<textarea class="input" name="description" maxlength="500" rows="2" placeholder="A gentle place to talk after the day has ended."></textarea></label>
            <label>House rule or motto<input class="input" name="motto" maxlength="240" placeholder="Bring a question; leave room for one."></label>
            <label>Accent colour<input class="input color-input" name="accent" type="color" value="#e77a54"></label>
            <label>Artwork URL <span class="field-note">Optional. Add it later if you prefer.</span><input class="input" name="artwork" type="text" inputmode="url" maxlength="2048" placeholder="https://…"></label>
            <div id="create-card-preview" class="tangent-card edit-card-preview" aria-label="Your Tangent card preview"></div>
            <button class="btn btn-primary">Create Tangent</button>
          </form>
        </details>
        <TangentEditorPanel :directories="directories" :tangent="activeTangent" @saved="key => router.navigate('/t/' + encodeURIComponent(key) + '/topics')" />
        <details id="site-setup" :hidden="!siteSetupVisible" class="setup">
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
          <ConversationPanel :conversation="conversation" :moderation="moderation" />
          <aside id="stewardship-panel" class="stewardship-panel" aria-labelledby="stewardship-title" :hidden="!moderation.panelOpen.value">
            <header class="stewardship-heading"><div><p class="eyebrow">Stewardship</p><h2 id="stewardship-title" tabindex="-1">What needs care</h2></div><button id="stewardship-close" class="btn btn-quiet" type="button" @click="moderation.closePanel()">Back to conversation</button></header>
            <StewardshipPanel :moderation="moderation" />
          </aside>
        </div>
        <ReportPanel :moderation="moderation" />
        <p id="action-status" class="note" role="status" aria-live="polite" :hidden="!moderation.announcement.value" :class="{ error: moderation.announcementError.value }">{{ moderation.announcement.value }}</p>
      </section>

      <section id="participant-profile" class="participant-profile" :hidden="router.route.kind !== 'participant'" aria-labelledby="profile-name">
        <ProfilePanel :site="site" :router="router" />
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
import { createModeration } from './composables/moderation'
import ConversationPanel from './components/ConversationPanel.vue'
import SettingsPanel from './components/SettingsPanel.vue'
import WelcomeHero from './components/WelcomeHero.vue'
import MastheadBar from './components/MastheadBar.vue'
import TangentDirectory from './components/TangentDirectory.vue'
import ProfilePanel from './components/ProfilePanel.vue'
import TangentEditorPanel from './components/TangentEditorPanel.vue'
import StewardshipPanel from './components/StewardshipPanel.vue'
import OnboardingPanel from './components/OnboardingPanel.vue'
import ReportPanel from './components/ReportPanel.vue'

// The boot port (app.js), the conversation loop (rooms.js core), the directories,
// the settings page, the hero + masthead (pages.js prepare/hero), and the place
// section's route reactions (rooms.js enterRoute).
const site = useSite()
const router = useRouter()
const conversation = createConversation(router)
const directories = createDirectories()
const settings = createSettings(site, router)
const moderation = createModeration(conversation)

// body[data-page] drives the page shell in the CSS (pages.css).
watchEffect(() => {
  document.body.dataset.page = router.route.kind
  document.body.dataset.signedIn = String(!!site.site?.participant)
})

onMounted(async () => {
  router.bind()
  moderation.wireViewer(() => site.site?.participant?.participantRef ?? '')
  await site.load()
  await directories.loadTangents().catch(() => {})
  site.loadServer().catch(() => {})
  if (router.isSettings) await settings.open()
  await dispatchRoute()
})

const authenticationRoute = computed(() => ['sign-in', 'onboarding'].includes(router.route.kind))

// The place section owns conversation; the other routes own their sections
// (rooms.js enterRoute's visibility half).
const placeVisible = computed(() =>
  !['settings', 'tangent-settings', 'topic-settings', 'participant', 'sign-in', 'onboarding'].includes(router.route.kind),
)

// enterRoute's dispatch: a Tangent route loads its Topics, a Topic route opens
// the conversation, a Post route opens its window. The route key guards against
// re-entering for the same place.
let dispatchedRoute = ''
async function dispatchRoute() {
  if (site.state === 'loading') return
  const route = router.route
  const key = [route.kind, 'tangent' in route ? route.tangent : '', 'topic' in route ? route.topic : '', 'post' in route ? route.post : ''].filter(Boolean).join(':')
  if (!placeVisible.value || key === dispatchedRoute) return
  dispatchedRoute = key
  if ('tangent' in route && route.tangent) {
    await directories.loadTopics(route.tangent)
    if (route.kind === 'topic') await conversation.choose(route.topic)
    else if (route.kind === 'post') await conversation.openPost(route.tangent, route.post)
  }
}

watch(() => router.route, () => {
  if (router.isSettings) void settings.open()
  void dispatchRoute()
})

// Anonymous visitors see the welcome copy only where they are asked to sign in;
// everywhere else the hero carries the sign-in action (pages.js prepare()).
const anonymousVisible = computed(() => authenticationRoute.value && site.state !== 'loading' && !site.site?.participant)
// Onboarding's sign-in step rewrites the welcome copy for the owner-to-be
// (onboarding.js show(), state sign_in).
const onboardingState = computed(() => site.site?.onboarding || 'complete')
const onboardingSignIn = computed(() => router.route.kind === 'onboarding' && onboardingState.value === 'sign_in')
const onboardingVisible = computed(() => router.route.kind === 'onboarding' && site.state !== 'loading' && !['complete', 'sign_in'].includes(onboardingState.value))

const anonymousTitle = computed(() =>
  onboardingSignIn.value ? 'Welcome to your own Tangents.' : router.route.kind === 'sign-in' ? 'Welcome back.' : 'Conversation has a home here.')
const anonymousCopy = computed(() =>
  router.route.kind === 'sign-in' || onboardingSignIn.value
    ? 'Sign in to ' + (site.site?.name ?? 'this server') + ' with your Atmosphere account.'
    : 'A home for your people, your companions, and the conversations you want to keep.',
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

// The room sidebar follows the route's Tangent, then the open room, then the
// first card (rooms.js activeTangentKey).
const activeTangentKey = computed(() => {
  if ('tangent' in router.route && router.route.tangent) return router.route.tangent
  return conversation.room.value?.tangentKey ?? directories.tangents[0]?.key ?? null
})

// The active Tangent as the manage forms see it (rooms.js selectTangent's flags).
const activeTangent = computed(() => directories.tangents.find(t => t.key === activeTangentKey.value) ?? null)

const communitySettingsVisible = computed(() =>
  !!(directories.tangentsCanCreate || site.site?.participant?.isOwner || activeTangent.value?.canManage || activeTangent.value?.isOwner || activeTangent.value?.canCreateTopic === true))

const siteSetupVisible = computed(() =>
  site.site?.participant?.isOwner === true || activeTangent.value?.canCreateTopic === true || (activeTangent.value?.permissions?.allowedActions?.includes('createTopic') ?? false))

const communitySettings = ref<HTMLDetailsElement | null>(null)
const tangentSetup = ref<HTMLDetailsElement | null>(null)
const createTangentForm = ref<HTMLFormElement | null>(null)

function openCreateTangent() {
  if (communitySettings.value) communitySettings.value.open = true
  if (tangentSetup.value) tangentSetup.value.open = true
  const name = createTangentForm.value?.elements.namedItem('name')
  if (name instanceof HTMLInputElement) name.focus()
}

// pages.js derives the address from the name until the operator edits it.
function slugFromName(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    .slice(0, 64)
    .replace(/-$/, '')
}

function autoKey(event: Event) {
  const form = event.target as HTMLFormElement
  const title = form.elements.namedItem('name') as HTMLInputElement | null
  const key = form.elements.namedItem('key') as HTMLInputElement | null
  if (!title || !key || key.dataset.automatic === 'false') return
  key.value = slugFromName(title.value)
}

function manualKey() {
  const form = tangentSetup.value?.querySelector<HTMLFormElement>('#create-tangent')
  const key = form?.elements.namedItem('key') as HTMLInputElement | null
  if (key) key.dataset.automatic = key.value ? 'false' : 'true'
}

async function submitCreateTangent(event: Event) {
  const form = event.target as HTMLFormElement
  const body = Object.fromEntries(new FormData(form)) as Record<string, string>
  body.name = body.name.trim()
  body.key = body.key.trim()
  body.description = (body.description ?? '').trim()
  body.motto = (body.motto ?? '').trim()
  if (!body.name || !body.key) throw new Error('Give this Tangent a name and stable address.')
  await directories.createTangent(body as never)
  form.reset()
  if (communitySettings.value) communitySettings.value.open = false
  router.navigate('/t/' + encodeURIComponent(body.key) + '/topics')
}

async function createTopic(event: Event) {
  const form = event.target as HTMLFormElement
  const body = Object.fromEntries(new FormData(form)) as { key: string; title: string }
  if (!activeTangentKey.value) throw new Error('Choose a Tangent before starting a Topic.')
  await directories.createTopic(activeTangentKey.value, body)
  form.reset()
  router.navigate('/t/' + encodeURIComponent(activeTangentKey.value) + '/topics/' + encodeURIComponent(body.key))
}
</script>
