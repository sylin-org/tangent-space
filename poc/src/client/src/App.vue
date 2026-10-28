<template>

  <a class="skip" href="#main">Skip to content</a>

  <header class="masthead" :class="{ 'reader-masthead': router.route.kind === 'topic' || router.route.kind === 'post' }">
    <MastheadBar :tangent-name="activeTangent?.name" :topic-title="conversation.selectedTopic.value?.title" :site="site" :router="router" :activity="activity" :profiles="profiles" :attention="workspaceAttention" :can-create="canCreateTangent" :can-start="router.route.kind === 'topics' && siteSetupVisible" :writing-label="activeTangent?.kind === 'articles' ? 'New article' : 'New topic'" @create="openCreateTangent" @start="openFirstTopic" @sign-out="submitSignOut" />
  </header>
  <WorkspacePath v-if="!site.authorizationFailure" :router="router" :directories="directories" :tangent="activeTangent" :topic="conversation.selectedTopic.value" :settings="settings" :can-create="canCreateTangent" :can-manage-space="canManageSpace" :writing-title="writingTitle" @create="openCreateTangent" @manage="openSpaceSettings" @start="openFirstTopic" @index="indexOpen = !indexOpen" />
  <SpaceHero :directories="directories" v-if="placeVisible && (router.route.kind === 'home' || router.route.kind === 'tangents')" :name="site.server?.name || site.site?.name || 'Tangent Space'" :image="site.server?.coverImageUrl" :mark="site.server?.markImageUrl" :byline="site.server?.byline" :welcome="site.server?.welcomeMessage" :message="site.server?.motd" :can-manage="canManageSpace" :can-create="canCreateTangent" @manage="openSpaceSettings" @create="openCreateTangent" />
  <div class="workspace-shell" :class="{ 'has-sidebar': site.established && !authenticationRoute, 'creation-shell': creationRoute }">
  <aside v-if="creationRoute && site.established" class="forum-sidebar creation-sidebar" :class="{ 'is-expanded': indexOpen }" aria-label="Writing tools"><div id="creation-index-slot"></div></aside>
  <ForumSidebar :workspace-available="site.site?.workspaceAvailable" :can-manage-space="canManageSpace" v-else-if="site.established && !authenticationRoute" :directories="directories" :router="router" :settings="settings" :tangent="activeTangent" :conversation="conversation" :expanded="indexOpen" :signed-in="!!site.site?.participant" :topic-view="topicView" :writing="writingOpen" :article-filter="articleFilter" @article-filter="articleFilter=$event" @view="topicView=$event" :can-create="canCreateTangent" @create="openCreateTangent" />
  <main id="main" tabindex="-1">
    <article class="panel">
      <p class="eyebrow" id="site-name" :hidden="!site.site">{{ site.site?.name }}</p>
      <p class="visually-hidden" id="announcer" role="status" aria-live="polite">{{ site.announcement }}</p>

      <section v-if="!site.authorizationFailure" id="settings-page" class="settings-page" :hidden="!router.isSettings || articleSettingsRoute" aria-labelledby="settings-title">
        <TangentSettingsPanel v-if="router.route.kind === 'tangent-settings'" :key="`${site.site?.participant?.participantRef || ''}:${router.route.tangent}`" :settings="settings" :directories="directories" @return="returnFromSettings" />
        <TopicSettingsPanel v-else-if="router.route.kind === 'topic-settings' && !articleSettingsRoute" :key="`${site.site?.participant?.participantRef || ''}:${router.route.tangent}:${router.route.topic}`" :settings="settings" @return="returnFromSettings" />
        <HostSettingsPanel v-else-if="router.route.kind === 'settings'" :key="site.site?.participant?.participantRef || ''" :settings="settings" :atmosphere="atmosphere" :expected-did="site.site?.participant?.did || ''" @return="returnFromSettings" />
      </section>

      <section id="state-loading" :hidden="site.state !== 'loading'" aria-labelledby="loading-title">
        <h1 class="title" id="loading-title">One moment</h1>
        <p class="lede">Loading this site's welcome.</p>
      </section>

      <section id="state-error" :hidden="site.state !== 'error'" aria-labelledby="error-title">
        <h1 class="title" id="error-title">{{ site.authorizationFailure ? 'This account cannot open the Space.' : "The welcome didn't load" }}</h1>
        <p v-if="site.authorizationFailure" class="lede">Your current account or session is no longer allowed here. Your saved drafts stay with the account that wrote them.</p>
        <p v-else class="lede">This site couldn't be reached just now. Check your connection, then try again.</p>
        <div v-if="site.authorizationFailure" class="onboarding-actions">
          <a class="btn btn-primary" :href="recoverySignIn">Sign in again</a>
          <form action="/auth/logout" method="post"><input type="hidden" name="return" :value="recoverySignIn"><button class="btn btn-quiet" type="submit">Use another account</button></form>
        </div>
        <button type="button" class="btn btn-quiet" id="retry" @click="site.load()">{{ site.authorizationFailure ? 'Check access again' : 'Try again' }}</button>
      </section>

      <section id="state-anonymous" :hidden="!anonymousVisible" aria-labelledby="anonymous-title">
        <SetupProgress v-if="onboardingSignIn" current="account" />
        <div class="onboarding-layout">
          <div class="onboarding-copy"><p class="eyebrow">A place to return to</p><h1 class="title" id="anonymous-title">{{ anonymousTitle }}</h1>
            <p class="lede" id="anonymous-copy">{{ anonymousCopy }}</p>
            <p class="note" id="note-unestablished" :hidden="site.established">Use the account you want to manage this Space. You’ll choose your database and name your first Tangent next.</p>
            <form id="sign-in-form" method="get" class="sign-in" :action="site.site?.signIn">
              <input type="hidden" name="return" :value="signInReturn"><button type="submit" class="btn btn-primary" id="sign-in-button">Sign in with Bluesky <span aria-hidden="true">→</span></button>
            </form>
            <p class="hint" id="sign-in-hint">{{ onboardingSignIn ? 'You’ll become this Space’s owner when you finish setup. Signing in does not publish anything to Bluesky.' : 'Choose your account on Bluesky. Your password stays there.' }}</p>
            <p v-if="onboardingSignIn" class="hint sign-in-compatible">Using another AT Protocol account? Enter its handle at sign-in.</p>
          </div>
          <aside class="onboarding-preview" aria-label="A place for conversations"><div class="community-preview"><span class="community-preview-mark">T</span><strong>A place of your own</strong><p>People. Companions. Conversations worth keeping.</p></div></aside>
        </div>
      </section>

      <section id="onboarding" :hidden="!onboardingVisible" aria-label="Make this place yours">
        <OnboardingPanel :site="site" :router="router" />
      </section>

      <form id="sign-out-form" ref="signOutForm" method="post" :hidden="true" :action="site.site?.signOut ?? ''"><input type="hidden" name="return" value="/"></form>

      <NotificationsPage v-if="!site.authorizationFailure && router.route.kind === 'notifications'" :attention="workspaceAttention" :signed-in="!!site.site?.participant" />
      <SearchPage v-if="!site.authorizationFailure && router.route.kind === 'search'" :route="router.route" />
      <FollowingPage v-if="!site.authorizationFailure && router.route.kind === 'following'" :key="site.site?.participant?.participantRef" :signed-in="!!site.site?.participant" />
      <section v-if="!site.authorizationFailure" id="place" :class="{ 'space-landing': router.route.kind === 'home' || router.route.kind === 'tangents' }" :hidden="!placeVisible" aria-label="Topics and conversation">
        <SpaceJoining v-if="!creationRoute && site.site?.participant && !site.site.participant.isOwner" :key="site.site.participant.participantRef" :actor-key="site.site.participant.participantRef" />
        <section v-if="router.route.kind === 'new-tangent'" class="creation-page" aria-labelledby="create-tangent-title">
          <NewTangentPanel v-if="canCreateTangent" :key="(site.site?.participant?.participantRef || '') + ':' + newTangentGeneration" :actor-key="site.site?.participant?.participantRef || ''" :error="createTangentError" :busy="creatingTangent" @ready="createTangentForm=$event" @submit="submitCreateTangent" @cancel="returnFromCreation('/')" />
          <p v-else-if="site.state !== 'loading'">{{ site.site?.participant ? 'Your account cannot create Tangents in this Space.' : 'Sign in to create a Tangent.' }} <a :href="site.site?.participant ? '/' : '/sign-in?return=%2Ftangents%2Fnew'">{{ site.site?.participant ? 'Return home' : 'Sign in' }}</a></p>
        </section>
        <ContinueReading :topic="conversation.selectedTopic.value" :active="site.site?.workspaceAvailable !== false && (router.route.kind === 'home' || router.route.kind === 'tangents')" />
        <FirstArrival :site="site" :router="router" :kind="activeTangent?.kind" @start="openFirstTopic" />
        <section v-if="router.route.kind === 'home' || router.route.kind === 'tangents'" id="tangent-return" class="tangent-return" aria-labelledby="tangent-return-title" tabindex="-1">
          <div class="space-landing-content">
          <a v-if="workspaceAttention.notifications.value.some(item => item.unread)" class="since-line" href="/notifications"><span class="new-label">{{ workspaceAttention.notifications.value.filter(item => item.unread).length }} new</span><span>Since you were last here, there are new replies and posts in your conversations.</span><strong>Show →</strong></a>
          <p v-if="site.site?.workspaceAvailable === false" class="hint host-doorstep">{{ site.site?.participant ? 'Your account is signed in. This Space is not available with your current access.' : 'Sign in to see whether your account can enter this Space.' }} <a v-if="!site.site?.participant" href="/sign-in?return=%2F">Sign in</a></p>
          <TangentDirectory v-else :directories="directories" :signed-in="!!site.site?.participant" @settings="openDirectorySettings" />
          </div>
        </section>
        <BlueskyDirectory v-if="router.route.kind === 'home' && site.site?.workspaceAvailable !== false && ['anonymous', 'signedIn'].includes(site.state)" />
        <CommunityHeader v-if="router.route.kind === 'topics' && !writingOpen" :name="activeTangent?.name || activeTangentKey || 'Tangent'" :description="activeTangent?.description" :motto="activeTangent?.motto" :artwork="activeTangent?.artwork" :accent="activeTangent?.accent" :kind="activeTangent?.kind" :summary="activeTangent?.summary" :read-audience="activeTangent?.readAudience" :can-start-topic="!!site.site?.participant && siteSetupVisible" @start-topic="openFirstTopic" />
        <TangentJoining v-if="activeTangent && site.site?.participant && ['topics', 'topic', 'post'].includes(router.route.kind)" :tangent="activeTangent.key" :actor-key="site.site.participant.participantRef" @changed="refreshAdmission" />
        <StartConversationPanel v-if="router.route.kind === 'new-topic' && site.site?.participant && siteSetupVisible" ref="startConversation" dedicated @title="writingTitle=$event" @cancel="returnFromCreation('/t/' + encodeURIComponent(activeTangentKey || '') + '/topics')"
          :tangent-key="activeTangentKey || ''" :tangent-name="activeTangent?.name || activeTangentKey || 'this Tangent'"
          :author="site.displayName" :actor-did="site.site.participant.did || undefined" :actor-key="site.site.participant.participantRef"
          :kind="activeTangent?.kind || 'discussions'"
          :audience="activeTangent?.readAudience === 'Public' ? 'Public' : 'Admitted'"
          :router="router" :show-trigger="false" />
        <p v-if="router.route.kind === 'new-topic' && site.state !== 'loading' && (!site.site?.participant || activeTangent && !siteSetupVisible)" class="creation-unavailable">{{ site.site?.participant ? 'Your account cannot start a topic in this Tangent.' : 'Sign in to start a topic.' }} <a :href="site.site?.participant ? '/t/' + encodeURIComponent(activeTangentKey || '') + '/topics' : '/sign-in?return=' + encodeURIComponent(windowPath)">{{ site.site?.participant ? 'Return to the Tangent' : 'Sign in' }}</a></p>
        <ArticleCollection :filter="articleFilter" v-if="!writingOpen && router.route.kind === 'topics' && activeTangent?.kind === 'articles'" :directories="directories" :tangent-key="activeTangentKey || ''" :signed-in="!!site.site?.participant" :page="articlePage" />
        <TopicList v-model:view="topicView" v-else-if="!writingOpen && router.route.kind === 'topics'" :directories="directories" :tangent-key="activeTangentKey || ''" :signed-in="!!site.site?.participant" :actor-key="site.site?.participant?.participantRef" />
        <div v-if="router.route.kind === 'topic' || router.route.kind === 'post' || articleSettingsRoute" class="topic-layout" :class="{ 'article-settings-layout': articleSettingsRoute }">
          <ConversationPanel :conversation="conversation" :moderation="moderation"
            :tangent-name="activeTangent?.name" :anchor-post="router.route.kind === 'post' ? router.route.post : undefined"
            :article-settings-open="articleSettingsRoute" :settings="settings" @close-article-settings="returnFromSettings" />
          <aside id="stewardship-panel" class="stewardship-panel" aria-labelledby="stewardship-title" :hidden="!moderation.panelOpen.value">
            <header class="stewardship-heading"><div><p class="eyebrow">Moderation</p><h2 id="stewardship-title" tabindex="-1">Reports &amp; moderation</h2></div><button id="stewardship-close" class="btn btn-quiet" type="button" @click="moderation.closePanel()">Back to conversation</button></header>
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
  <SettingsSaveBar v-if="router.isSettings && !site.authorizationFailure" :changes="settingsChanges" />
  </div>
  <footer class="site-footer"><span>Powered by <a href="https://github.com/sylin-org/tangent-space" target="_blank" rel="noopener">Tangent Space</a></span><nav aria-label="Project links"><a href="https://github.com/sylin-org/tangent-space#readme" target="_blank" rel="noopener">Run your own ↗</a><a id="agent-entry" href="https://github.com/sylin-org/tangent-space#bring-an-agent" target="_blank" rel="noopener">Tangent Space for Agents ↗</a></nav><AppearanceSwitch /></footer>

</template>


<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch, watchEffect } from 'vue'
import { useSite } from './stores/site'
import { useRouter, parseWorkspaceRoute } from './stores/router'
import { createConversation } from './composables/conversation'
import { createDirectories } from './composables/directories'
import { createSelectedTangent } from './composables/selected-tangent'
import { createSettings } from './composables/settings'
import { createModeration } from './composables/moderation'
import { createActivity } from './composables/activity'
import { createAtmosphere } from './composables/atmosphere'
import { createProfiles } from './composables/profiles'
import { qualifyAgentEntry } from './agent-entry'
import { rememberSettingsOrigin, settingsOrigin, settingsScope, type SettingsOrigin } from './composables/settings-return'
import ConversationPanel from './components/ConversationPanel.vue'
import ContinueReading from './components/ContinueReading.vue'
import HostSettingsPanel from './components/HostSettingsPanel.vue'
import TopicSettingsPanel from './components/TopicSettingsPanel.vue'
import TangentSettingsPanel from './components/TangentSettingsPanel.vue'
import NotificationsPage from './components/NotificationsPage.vue'
import FollowingPage from './components/FollowingPage.vue'
import SearchPage from './components/SearchPage.vue'
import SpaceJoining from './components/SpaceJoining.vue'
import TangentJoining from './components/TangentJoining.vue'
import NewTangentPanel from './components/NewTangentPanel.vue'
import { createWorkspaceAttention } from './composables/workspace-attention'
import ForumSidebar from './components/ForumSidebar.vue'
import WorkspacePath from './components/WorkspacePath.vue'
import AppearanceSwitch from './components/AppearanceSwitch.vue'
import SettingsSaveBar from './components/SettingsSaveBar.vue'
import { provideSettingsChanges } from './composables/settings-changes'
import { useAppearance } from './composables/appearance'
import SpaceHero from './components/SpaceHero.vue'
import CommunityHeader from './components/CommunityHeader.vue'
import TopicList from './components/TopicList.vue'
import ArticleCollection from './components/ArticleCollection.vue'
import MastheadBar from './components/MastheadBar.vue'
import TangentDirectory from './components/TangentDirectory.vue'
import ProfilePanel from './components/ProfilePanel.vue'
import StewardshipPanel from './components/StewardshipPanel.vue'
import OnboardingPanel from './components/OnboardingPanel.vue'
import SetupProgress from './components/SetupProgress.vue'
import FirstArrival from './components/FirstArrival.vue'
import BlueskyDirectory from './components/BlueskyDirectory.vue'
import ReportPanel from './components/ReportPanel.vue'
import StartConversationPanel from './components/StartConversationPanel.vue'

// The workspace's site state, conversation, directories, settings, and route reactions.
const site = useSite()
const router = useRouter()
const appearance = useAppearance()
const indexOpen = ref(false)
watch(() => router.route, () => { indexOpen.value = false })
watch(() => site.site?.participant?.participantRef, actor => appearance.identify(actor || ''), { immediate: true })
const conversation = createConversation(router)
const directories = createDirectories()
const canManageSpace = computed(() => !!site.site?.participant &&
  (site.site.participant.isOwner || site.server?.canManage === true || site.server?.canManageParticipants === true))
const canCreateTangent = computed(() => !!site.site?.participant &&
  directories.tangentsCanCreate && site.site.onboarding === 'complete')
const activeTangentKey = computed(() => {
  if ('tangent' in router.route && router.route.tangent) return router.route.tangent
  return conversation.selectedTopic.value?.tangentKey ?? directories.tangents[0]?.key ?? null
})
const selectedTangent = createSelectedTangent(directories, () => activeTangentKey.value, () => site.site?.participant?.participantRef ?? 'visitor')
const settings = createSettings(site, router)
const settingsChanges = provideSettingsChanges()
const moderation = createModeration(conversation)
const activity = createActivity({ conversation, directories, site, router, onIdentityRecovered: async () => {
  directories.resetForIdentity()
  if (router.isSettings) void settings.open()
  await Promise.all([directories.loadTangents(), dispatchRoute(true)])
}, onTangentChanged: async tangentKey => {
  if (activeTangentKey.value === tangentKey) await selectedTangent.load(true)
} })
const workspaceAttention = createWorkspaceAttention()
watch([() => site.site?.workspaceAvailable, () => site.authorizationFailure], ([available, denied]) => {
  if (available === false || denied) { directories.resetForIdentity(); workspaceAttention.reset() }
  if (denied) { conversation.reset(); writingTitle.value = '' }
}, { flush: 'sync' })
watch(() => site.site?.participant?.participantRef, actor => { workspaceAttention.reset(); if (actor) void workspaceAttention.load() })
watch(() => router.route.kind, kind => { if (kind === 'notifications' && site.site?.participant) void workspaceAttention.load() })
const atmosphere = createAtmosphere()
const profiles = createProfiles(activity)
let bootstrapping = true

// The atmosphere follows the server's own settings as they load and change.
watch([() => site.server, () => site.site?.participant], ([server]) => {
  atmosphere.configure(server ?? null, site.site?.participant ?? null)
})

// The signed-in viewer's own profile decorates immediately (profile-live.js), and the
// page keeps the server announcing for every identity it renders: the viewer plus the
// authors resolved on the open conversation.
watch(() => site.site?.participant?.did, did => {
  if (did) profiles.loadOwn(did)
})

const watchedDids = computed<string[]>(() => {
  const dids = new Set<string>()
  const own = site.site?.participant?.did
  if (own) dids.add(own)
  for (const key of Object.keys(conversation.resolved.value)) dids.add(key)
  for (const message of conversation.messages.value) {
    const author = message.authorParticipantId
    if (author?.startsWith('did:')) dids.add(author)
  }
  return [...dids]
})

watch([watchedDids, () => site.state, () => site.site?.participant?.participantRef], ([dids, state]) => {
  profiles.sync(state === 'signedIn' ? dids : [])
}, { immediate: true })

// The route and active appearance drive the workspace shell.
watchEffect(() => {
  document.documentElement.dataset.accent = site.server?.accent || 'gold'
  document.body.dataset.page = router.route.kind
  document.body.dataset.signedIn = String(!!site.site?.participant)
})
watchEffect(() => {
  const route = router.route
  const labels: Record<string, string> = { search: 'Search', following: 'Following', notifications: 'Notifications', 'new-tangent': 'New Tangent', 'sign-in': 'Sign in', onboarding: 'Set up your Space' }
  if (route.kind === 'home' || route.kind === 'tangents') {
    const name = site.server?.name || site.site?.name
    document.title = name && name !== 'Tangent Space' ? name + ' · Tangent Space' : 'Tangent Space'
  } else if ((route.kind === 'topic' || route.kind === 'post') && conversation.selectedTopic.value?.title) document.title = conversation.selectedTopic.value.title + ' · Tangent Space'
  else if (route.kind === 'topics') document.title = (selectedTangent.current.value?.name || 'Tangent') + ' · Tangent Space'
  else if (route.kind === 'new-topic') document.title = (selectedTangent.current.value?.kind === 'articles' ? 'New article' : 'New topic') + ' · Tangent Space'
  else if (labels[route.kind]) document.title = labels[route.kind] + ' · Tangent Space'
})

onMounted(async () => {
  router.bind(() => true)
  document.addEventListener('tangent:workspace-revealed', restoreRouteFragment)
  document.addEventListener('click', captureSettingsOrigin, true)
  document.addEventListener('click', captureCreationOrigin, true)
  moderation.wireViewer(() => site.site?.participant?.participantRef ?? '')
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      activity.needsRefresh.value = true
      activity.setVisible(false)
    } else if (site.site?.workspaceAvailable !== false) {
      activity.start()
    }
  })
  window.addEventListener('pagehide', () => activity.stop())
  window.addEventListener('pageshow', () => { if (site.site?.workspaceAvailable !== false) activity.start() })
  atmosphere.install()
  profiles.wire()
  void qualifyAgentEntry(document.getElementById('agent-entry') as HTMLAnchorElement | null)
  await site.load()
  bootstrapping = false
  if (site.site?.workspaceAvailable !== false) await directories.loadTangents().catch(() => {})
  if (site.state === 'signedIn' && site.site?.workspaceAvailable !== false) activity.start()
  if (router.isSettings) await settings.open()
  await dispatchRoute()
  await nextTick()
  const route = router.route
  const usable = ['topic', 'post'].includes(route.kind)
    ? !!conversation.selectedTopic.value?.canRead && !conversation.error.value
    : route.kind === 'topics' ? directories.topicsLoaded && !directories.topicsError : true
  if (site.state !== 'error' && site.state !== 'loading' && usable) document.dispatchEvent(new Event('tangent:workspace-ready'))
})

// A completed welcome (re)starts activity; authentication routes never start it.
watch([() => site.state, () => router.route.kind, () => site.site?.workspaceAvailable], ([state, kind]) => {
  if (state === 'signedIn' && site.site?.workspaceAvailable !== false && !['sign-in', 'onboarding'].includes(kind)) activity.start()
  else activity.stop()
})

const authenticationRoute = computed(() => ['sign-in', 'onboarding'].includes(router.route.kind))
const articleRailScope = ref('')
const articleSettingsRoute = computed(() => router.route.kind === 'topic-settings'
  && conversation.selectedTopic.value?.tangentKey === router.route.tangent
  && conversation.selectedTopic.value?.key === router.route.topic
  && (conversation.selectedTopic.value?.presentation === 'article'
    || articleRailScope.value === (site.site?.participant?.participantRef ?? '') + ':' + router.route.tangent + ':' + router.route.topic))
watch(articleSettingsRoute, open => {
  if (open && router.route.kind === 'topic-settings')
    articleRailScope.value = (site.site?.participant?.participantRef ?? '') + ':' + router.route.tangent + ':' + router.route.topic
}, { immediate: true })

// The place section owns conversation; the other routes own their sections
//.
const placeVisible = computed(() =>
  articleSettingsRoute.value || !['notifications', 'following', 'search', 'settings', 'tangent-settings', 'topic-settings', 'participant', 'sign-in', 'onboarding'].includes(router.route.kind),
)

// enterRoute's dispatch: a Tangent route loads its Topics, a Topic route opens
// the conversation, a Post route opens its window. The route key guards against
// re-entering for the same place.
let dispatchedRoute = ''
async function dispatchRoute(force = false) {
  if (site.state === 'loading' || site.authorizationFailure) return
  const route = router.route
  const actor = site.site?.participant?.participantRef ?? 'visitor'
  const page = route.kind === 'topics' ? Math.max(1, Number(new URLSearchParams(window.location.search).get('page')) || 1) : 1
  const readingQuery = ['topic', 'post'].includes(route.kind) ? window.location.search : ''
  const key = [actor, route.kind, 'tangent' in route ? route.tangent : '', 'topic' in route ? route.topic : '', 'post' in route ? route.post : '', route.kind === 'topics' ? page : '', readingQuery].filter(Boolean).join(':')
  if (route.kind === 'topic-settings') {
    dispatchedRoute = key
    if (force || conversation.selectedTopic.value?.tangentKey !== route.tangent || conversation.selectedTopic.value?.key !== route.topic) {
      await Promise.all([directories.loadTopics(route.tangent), conversation.choose(route.topic)])
    }
    return
  }
  if (!placeVisible.value) {
    dispatchedRoute = ''
    conversation.invalidatePending()
    return
  }
  if (!force && key === dispatchedRoute) return
  const previousDispatched = dispatchedRoute
  dispatchedRoute = key
  const returning = pendingSettingsReturn?.actor === actor
    && pendingSettingsReturn.origin.href === window.location.pathname + window.location.search + window.location.hash
    ? pendingSettingsReturn.origin : null
  pendingSettingsReturn = null
  if ('tangent' in route && route.tangent) {
    const loads: Promise<unknown>[] = [directories.loadTopics(route.tangent, route.kind === 'topics' ? page : 1)]
    if (route.kind === 'topic' && (force || previousDispatched !== key || conversation.selectedTopic.value?.key !== route.topic || conversation.selectedTopic.value?.tangentKey !== route.tangent)) loads.push(conversation.choose(route.topic, returning?.anchor?.post))
    else if (route.kind === 'post' && (conversation.selectedTopic.value?.tangentKey !== route.tangent || (conversation.openingPost.value?.id !== route.post && !conversation.messages.value.some(message => message.id === route.post)))) loads.push(conversation.openPost(route.tangent, route.post))
    await Promise.all(loads)
    await nextTick()
    if (!returning) restoreRouteFragment()
  }
  if (returning && key === dispatchedRoute) {
    await nextTick()
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (key !== dispatchedRoute || actor !== (site.site?.participant?.participantRef ?? 'visitor')) return
      const post = returning.anchor ? document.getElementById(returning.anchor.post) : null
      const position = post && returning.anchor
        ? window.scrollY + post.getBoundingClientRect().top - returning.anchor.offset : returning.scrollY
      window.scrollTo(0, Math.max(0, position))
    }))
  }
}

function restoreRouteFragment() {
  if (!window.location.hash) return
  let id: string
  try { id = decodeURIComponent(window.location.hash.slice(1)) } catch { return }
  requestAnimationFrame(() => {
    const target = document.querySelector('#app')?.querySelector<HTMLElement>('[id="' + CSS.escape(id) + '"]')
      || (id.startsWith('post-') ? document.getElementById(id.slice(5)) : null)
    target?.scrollIntoView({ block: 'start' })
  })
}

watch(() => router.route, (_route, previous) => {
  if (_route.kind !== 'topic-settings') articleRailScope.value = ''
  if (settingsScope(previous) && !router.isSettings) {
    const actor = site.site?.participant?.participantRef ?? ''
    const origin = settingsOrigin(actor, settingsScope(previous))
    pendingSettingsReturn = origin ? { actor, origin } : null
  }
  if (router.isSettings) void settings.open()
  void dispatchRoute()
})
watch(() => site.site?.participant?.participantRef, () => {
  writingTitle.value = ''
  if (bootstrapping || activity.recovering.value) return
  articleRailScope.value = ''
  conversation.reset()
  if (router.isSettings) void settings.open()
  directories.resetForIdentity()
  dispatchedRoute = ''
  if (site.authorizationFailure) return
  void Promise.all([directories.loadTangents(), dispatchRoute(true)])
})

// Anonymous visitors see the welcome copy where they are asked to sign in.
const anonymousVisible = computed(() => !site.authorizationFailure && authenticationRoute.value && site.state !== 'loading' && !site.site?.participant)
// Onboarding's sign-in step rewrites the welcome copy for the owner-to-be
// (onboarding.js show(), state sign_in).
const onboardingState = computed(() => site.site?.onboarding || 'complete')
const onboardingSignIn = computed(() => router.route.kind === 'onboarding' && onboardingState.value === 'sign_in')
const onboardingVisible = computed(() => !site.authorizationFailure && router.route.kind === 'onboarding' && site.state !== 'loading' && onboardingState.value !== 'sign_in')

const anonymousTitle = computed(() =>
  onboardingSignIn.value ? 'A place for your conversations.' : router.route.kind === 'sign-in' ? 'Welcome back.' : 'Conversation has a home here.')
const anonymousCopy = computed(() =>
  onboardingSignIn.value ? 'Create a community for people, companions and the conversations worth keeping.' : router.route.kind === 'sign-in'
    ? 'Sign in to ' + (site.site?.name ?? 'this Space') + ' with your Bluesky or compatible account.'
    : 'A home for your people, your companions, and the conversations you want to keep.',
)

const recoverySignIn = computed(() => {
  void router.route
  return '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
})

const signInReturn = computed(() => {
  if (router.route.kind === 'onboarding') return '/onboarding/'
  const requested = new URLSearchParams(window.location.search).get('return')
  return requested && /^\/(?!\/)/.test(requested) && !/[\\\u0000-\u001f]/.test(requested) ? requested : '/'
})

const signOutForm = ref<HTMLFormElement | null>(null)

function submitSignOut() {
  signOutForm.value?.requestSubmit()
}

// The active Tangent also resolves direct links beyond the bounded directory page.
const activeTangent = selectedTangent.current
const articlePage = computed(() => router.route.kind === 'topics' ? Math.max(1, Number(new URLSearchParams(window.location.search).get('page')) || 1) : 1)
watch([() => activeTangent.value?.kind, () => conversation.selectedTopic.value?.presentation], ([kind, presentation]) => {
  if (!kind || !presentation || !['topic', 'post', 'topic-settings'].includes(router.route.kind)) return
  if (presentation === (kind === 'articles' ? 'article' : 'conversation')) return
  void conversation.refreshTopic()
})
watch(() => conversation.selectedTopic.value?.presentation, presentation => {
  const route = router.route
  const resource = settings.resource.value
  if (route.kind !== 'topic-settings' || !presentation || !resource?.topic ||
      resource.topic.key !== route.topic || resource.topic.tangentKey !== route.tangent ||
      resource.topic.presentation === presentation) return
  settings.resource.value = { ...resource, topic: { ...resource.topic, presentation } }
})

const siteSetupVisible = computed(() =>
  activeTangent.value?.canCreateTopic === true || (activeTangent.value?.permissions?.allowedActions?.includes('createTopic') ?? false))

const creatingTangent = ref(false)
const newTangentGeneration=ref(0)
const topicView=ref('all')
const creationRoute = computed(() => ['new-tangent', 'new-topic'].includes(router.route.kind))
const writingOpen = computed(() => router.route.kind === 'new-topic')
const writingTitle = ref('')
const windowPath = computed(() => router.route.kind === 'new-topic' ? '/t/' + encodeURIComponent(router.route.tangent) + '/new' : '/')
const articleFilter=ref({month:'',author:''})
watch(()=>router.route,()=>{writingTitle.value='';topicView.value='all';articleFilter.value={month:'',author:''}})
const createTangentError = ref('')
const createTangentForm = ref<HTMLFormElement | null>(null)
const startConversation = ref<InstanceType<typeof StartConversationPanel> | null>(null)

function creationOriginKey(destination = window.location.pathname) { return 'tangent:creation-return:' + encodeURIComponent(site.site?.participant?.participantRef || 'visitor') + ':' + encodeURIComponent(destination) }
function rememberCreationOrigin(destination: string) {
  if (creationRoute.value) return
  try { sessionStorage.setItem(creationOriginKey(destination), JSON.stringify({ href: window.location.pathname + window.location.search + window.location.hash, scrollY: window.scrollY })) } catch { /* Navigation remains available without storage. */ }
}
function returnFromCreation(fallback: string) {
  let destination = fallback, position = 0
  try {
    const stored = sessionStorage.getItem(creationOriginKey())
    const origin = stored ? JSON.parse(stored) as { href?: string; scrollY?: number } : null
    const saved = origin?.href
    position = Math.max(0, Number(origin?.scrollY) || 0)
    if (saved && saved.startsWith('/') && !saved.startsWith('//') && parseWorkspaceRoute(saved.split(/[?#]/)[0]!) && !['new-tangent', 'new-topic'].includes(parseWorkspaceRoute(saved.split(/[?#]/)[0]!)!.kind)) destination = saved
  } catch { /* Use the containing collection. */ }
  router.navigate(destination)
  void nextTick(() => requestAnimationFrame(() => requestAnimationFrame(() => window.scrollTo(0, position))))
}
function captureCreationOrigin(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
  const anchor = (event.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null
  if (!anchor || anchor.target || anchor.hasAttribute('download')) return
  const destination = new URL(anchor.href, window.location.origin)
  if (destination.origin !== window.location.origin) return
  const route = parseWorkspaceRoute(destination.pathname)
  if (route?.kind === 'new-tangent' || route?.kind === 'new-topic') rememberCreationOrigin(destination.pathname)
}
function openCreateTangent() {
  rememberCreationOrigin('/tangents/new')
  createTangentError.value = ''
  router.navigate('/tangents/new')
}

function openSpaceSettings() {
  rememberSettingsOrigin(site.site?.participant?.participantRef || '', 'host')
  router.navigate('/settings')
}

function openDirectorySettings(key: string) {
  rememberSettingsOrigin(site.site?.participant?.participantRef || '', `tangent:${key}`)
  router.navigate('/t/' + encodeURIComponent(key) + '/settings')
}

let pendingSettingsReturn: { actor: string; origin: SettingsOrigin } | null = null
function captureSettingsOrigin(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
  const anchor = (event.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null
  if (!anchor || anchor.target || anchor.hasAttribute('download') || router.isSettings) return
  const url = new URL(anchor.href, window.location.origin)
  if (url.origin !== window.location.origin) return
  const destination = parseWorkspaceRoute(url.pathname)
  const scope = destination ? settingsScope(destination) : ''
  if (scope) rememberSettingsOrigin(site.site?.participant?.participantRef || '', scope)
}

function returnFromSettings() {
  const route = router.route
  const scope = settingsScope(route)
  if (!scope) return
  const origin = settingsOrigin(site.site?.participant?.participantRef || '', scope)
  const fallback = route.kind === 'settings' ? '/'
    : route.kind === 'tangent-settings' ? '/t/' + encodeURIComponent(route.tangent) + '/topics'
      : route.kind === 'topic-settings' ? '/t/' + encodeURIComponent(route.tangent) + '/topics/' + encodeURIComponent(route.topic) : '/tangents'
  const closingArticleRail = route.kind === 'topic-settings' && articleSettingsRoute.value
  router.navigate(origin?.href || fallback, { replace: closingArticleRail })
  if (closingArticleRail) void nextTick(() => document.getElementById('article-settings-trigger')?.focus({ preventScroll: true }))
}

async function refreshAdmission() {
  const tangent = activeTangentKey.value
  const tasks: Promise<unknown>[] = [selectedTangent.load(true), directories.loadTangents()]
  if (tangent) tasks.push(directories.loadTopics(tangent))
  if (['topic', 'post'].includes(router.route.kind)) tasks.push(conversation.refreshTopic())
  await Promise.all(tasks)
}

function openFirstTopic() {
  if (!activeTangentKey.value) return
  const destination = '/t/' + encodeURIComponent(activeTangentKey.value) + '/new'
  rememberCreationOrigin(destination)
  router.navigate(destination)
}

async function submitCreateTangent(event: Event) {
  const form = event.target as HTMLFormElement
  const body = Object.fromEntries(['name','key','description','motto','kind','accent','artwork','readAudience'].map(key=>[key,String(new FormData(form).get(key)||'')])) as Record<string, string>
  body.name = body.name.trim()
  body.key = body.key.trim()
  body.description = (body.description ?? '').trim()
  body.motto = (body.motto ?? '').trim()
  if (!body.name || !body.key) { createTangentError.value = 'Give this Tangent a name and stable address.'; return }
  if(creatingTangent.value)return
  creatingTangent.value=true
  const creator = site.site?.participant?.participantRef || ''
  try {
    await directories.createTangent(body as never)
    if (creator !== (site.site?.participant?.participantRef || '')) return
    newTangentGeneration.value++
    try { sessionStorage.removeItem('tangent:new-tangent:' + encodeURIComponent(site.site?.participant?.participantRef || '')) } catch { /* Creation has already succeeded. */ }
    createTangentError.value = ''
    router.navigate('/t/' + encodeURIComponent(body.key) + '/topics')
  } catch (error) {
    createTangentError.value = (error as Error).message || 'This Tangent could not be created.'
  } finally {creatingTangent.value=false}
}

</script>
