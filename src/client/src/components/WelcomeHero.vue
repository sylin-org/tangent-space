<script setup lang="ts">
// The welcome hero, ported from pages.js hero(): the server's face on home,
// the Tangent's on its Topics page, the Topic's while reading — with the
// breadcrumbs and actions each of those places earns.
import { computed, watchEffect } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'

interface HeroTangent {
  key: string
  name?: string
  description?: string
  motto?: string
  artwork?: string
  canManage?: boolean
  isOwner?: boolean
  canCreateTopic?: boolean
}

interface HeroTopic {
  key: string
  tangentKey: string
  title: string
  topic?: string
  canManage?: boolean
}

const props = defineProps<{
  site: SiteStore
  router: RouterStore
  tangent: HeroTangent | null
  topic: HeroTopic | null
}>()

const HIDDEN_KINDS = ['settings', 'tangent-settings', 'topic-settings', 'participant', 'sign-in', 'onboarding']

const kind = computed(() => props.router.route.kind)
const routeTangent = computed(() => ('tangent' in props.router.route ? props.router.route.tangent : undefined))
const nested = computed(() => ['topics', 'topic', 'post'].includes(kind.value))
const reading = computed(() => ['topic', 'post'].includes(kind.value))

const visible = computed(() => !HIDDEN_KINDS.includes(kind.value))

const server = computed(() => props.site.server ?? {})
const title = computed(() => {
  if (reading.value) return props.topic?.title || 'Opening the conversation…'
  if (nested.value) return props.tangent?.name || 'Opening this Tangent…'
  if (kind.value === 'tangents') return 'Find your next conversation.'
  return server.value.name || props.site.site?.name || 'Tangent Space'
})
const eyebrow = computed(() =>
  reading.value ? 'Topic' : nested.value ? 'Tangent · Topics' : kind.value === 'tangents' ? 'The Tangent collection' : 'The bulletin board',
)
const byline = computed(() => (reading.value ? '' : nested.value ? (props.tangent?.motto ?? '') : (server.value.byline ?? '')))
const message = computed(() => {
  const value = reading.value
    ? (props.topic?.topic ?? '')
    : nested.value
      ? (props.tangent?.description ?? '')
      : (server.value.welcomeMessage ?? '')
  return value.trim() === byline.value.trim() ? '' : value
})
const motd = computed(() => server.value.motd ?? '')

const imageUrl = computed(() => {
  const image = nested.value ? props.tangent?.artwork || server.value.coverImageUrl : server.value.coverImageUrl
  if (typeof image !== 'string') return ''
  return (/^https:\/\//i.test(image) || /^\/(?!\/)/.test(image)) && !/[\u0000-\u001f\\]/.test(image) ? image : ''
})

const crumbs = computed<Array<{ label: string; href: string | null }>>(() => {
  if (kind.value === 'home') return []
  const serverName = server.value.name || props.site.site?.name || 'Tangent Space'
  const values: Array<{ label: string; href: string | null }> = [{ label: serverName, href: '/' }]
  if (reading.value && props.tangent) {
    values.push({ label: props.tangent.name || routeTangent.value || 'Tangent', href: '/t/' + encodeURIComponent(props.tangent.key) + '/topics' })
  }
  if (kind.value === 'post' && props.topic) {
    values.push({ label: props.topic.title, href: '/t/' + encodeURIComponent(props.topic.tangentKey) + '/topics/' + encodeURIComponent(props.topic.key) })
  }
  const last =
    kind.value === 'post'
      ? 'Post'
      : nested.value
        ? reading.value
          ? props.topic?.title || 'Topic'
          : props.tangent?.name || 'Topics'
        : 'Tangents'
  values.push({ label: last, href: null })
  return values
})

interface HeroAction {
  label: string
  href: string
  variant: 'primary' | 'quiet'
}

const actions = computed<HeroAction[]>(() => {
  const values: HeroAction[] = []
  if (kind.value === 'home' && props.site.site?.onboarding !== 'complete') {
    const label = props.site.site?.participant?.isOwner ? 'Finish setting up' : 'Set up this server'
    values.push({ label, href: '/onboarding/', variant: 'primary' })
  }
  if (!props.site.site?.participant && props.site.site?.established) {
    values.push({ label: 'Sign in to take part', href: signInHref(), variant: 'primary' })
  }
  if (kind.value === 'post' && props.topic) {
    values.push({
      label: 'Open conversation',
      href: '/t/' + encodeURIComponent(props.topic.tangentKey) + '/topics/' + encodeURIComponent(props.topic.key),
      variant: 'quiet',
    })
  }
  if (kind.value === 'topics' && props.tangent?.canManage) {
    values.push({ label: '⚙ Tangent settings', href: '/t/' + encodeURIComponent(props.tangent.key) + '/settings', variant: 'quiet' })
  }
  if (reading.value && props.topic?.canManage) {
    values.push({
      label: '⚙ Topic settings',
      href: '/t/' + encodeURIComponent(props.topic.tangentKey) + '/topics/' + encodeURIComponent(props.topic.key) + '/settings',
      variant: 'quiet',
    })
  }
  return values
})

const serverAllowedActions = computed<string[]>(() => {
  const permissions = server.value.permissions
  return permissions && !Array.isArray(permissions) ? (permissions.allowedActions ?? []) : []
})

const settingsToggleVisible = computed(
  () =>
    kind.value === 'home' &&
    (props.site.site?.participant?.isOwner === true ||
      serverAllowedActions.value.some(action => action === 'manageServer' || action === 'editServer')),
)

function signInHref(): string {
  return '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)
}

watchEffect(() => {
  if (visible.value) document.title = title.value + ' · Tangent Space'
})
</script>

<template>
  <section id="server-welcome" class="server-welcome" :hidden="!visible" aria-labelledby="server-welcome-title">
    <img v-if="imageUrl" id="hero-image" class="hero-image" :src="imageUrl" alt="" referrerpolicy="no-referrer">
    <img v-else id="hero-image" class="hero-image" alt="" referrerpolicy="no-referrer" hidden>
    <div class="hero-inner">
      <nav id="hero-breadcrumbs" class="breadcrumbs" aria-label="Breadcrumb" :hidden="!crumbs.length">
        <template v-for="(crumb, index) in crumbs" :key="index">
          <a v-if="crumb.href" :href="crumb.href">{{ crumb.label }}</a>
          <span v-else aria-current="page">{{ crumb.label }}</span>
        </template>
      </nav>
      <div class="server-welcome-copy">
        <p id="hero-eyebrow" class="eyebrow">{{ eyebrow }}</p>
        <h1 id="server-welcome-title">{{ title }}</h1>
        <p id="hero-byline" class="hero-byline" :hidden="!byline">{{ byline }}</p>
        <p id="server-welcome-message" class="lede" :hidden="!message">{{ message }}</p>
        <p id="server-motd" class="server-motd" :hidden="kind !== 'home' || !motd">{{ motd }}</p>
      </div>
      <div class="server-welcome-actions">
        <div id="hero-actions">
          <a
            v-for="action in actions"
            :key="action.label"
            :class="action.variant === 'primary' ? 'btn btn-primary' : 'btn btn-quiet'"
            :href="action.href"
          >{{ action.label }}</a>
        </div>
        <a
          id="server-settings-toggle"
          class="server-settings-link"
          href="/settings"
          aria-label="Server settings"
          title="Server settings"
          :hidden="!settingsToggleVisible"
        >⚙</a>
      </div>
    </div>
  </section>
</template>
