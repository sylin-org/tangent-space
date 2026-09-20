<script setup lang="ts">
// The first-run Tangent, ported from onboarding.js: starters that fill two
// fields (not templates), a first taste of the cover art, and the claim step
// that makes the signed-in account this server's owner.
import { computed, reactive, ref, watch, watchEffect } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { artworkPresets } from '../composables/artwork-presets'

const props = defineProps<{ site: SiteStore; router: RouterStore }>()

// Starting points, not templates: each one fills the two fields and leaves them editable.
const starters = [
  { name: 'The Gateway', description: 'Open to anyone who finds their way here. Introductions, questions, and where to go next.' },
  { name: 'Projects', description: 'What we are building, where each thing stands, and what it needs next.' },
  { name: 'Reading Room', description: 'Links, long reads, and the things worth keeping.' },
  { name: 'Small Hours', description: 'Half-formed thoughts, welcome as they are.' },
  { name: 'The Lab', description: 'Experiments with people and companions, including the ones that do not work.' },
]

const onboardingState = computed(() => props.site.site?.onboarding || 'complete')
const active = computed(() => props.router.route.kind === 'onboarding' && onboardingState.value !== 'complete' && onboardingState.value !== 'sign_in')

watchEffect(() => {
  // The CSS keys the whole page shell on this class (onboarding.css).
  document.body.classList.toggle(
    'onboarding-active',
    props.router.route.kind === 'onboarding' && onboardingState.value !== 'complete',
  )
})

watch(onboardingState, state => {
  if (props.router.route.kind === 'onboarding' && state === 'complete') window.location.replace('/')
})

const form = reactive({ name: '', description: '', artwork: '' })
const busy = ref(false)
const error = ref('')

const cardName = computed(() => form.name.trim() || 'Your first Tangent')
const cardDescription = computed(() => form.description.trim() || 'A place for the conversations that matter to you.')

// One cover from each mood before any mood repeats, so eight squares span the
// collection instead of showing its first page.
const starterArtwork = computed(() => {
  const moods = new Map<string, typeof artworkPresets>()
  for (const entry of artworkPresets) {
    if (!moods.has(entry.group)) moods.set(entry.group, [])
    moods.get(entry.group)!.push(entry)
  }
  const rows = [...moods.values()]
  const picked: typeof artworkPresets = []
  for (let depth = 0; picked.length < 8; depth += 1) {
    if (!rows.some(entries => entries.length > depth)) break
    for (const entries of rows) {
      if (picked.length === 8) break
      if (entries[depth]) picked.push(entries[depth]!)
    }
  }
  return picked.map(entry => ({ ...entry, url: '/tangent-art/' + entry.slug + '.png' }))
})

function applyStarter(starter: (typeof starters)[number]) {
  form.name = starter.name
  form.description = starter.description
}

function clearStarter() {
  form.name = ''
  form.description = ''
}

async function send(path: string, body: Record<string, unknown>) {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  const result = await response.json().catch(() => ({}))
  if (!response.ok) throw new Error((result as Record<string, string>).error || (result as Record<string, string>).reason || 'That step could not be completed. Please try again.')
  return result
}

async function finish(skip: boolean) {
  error.value = ''
  busy.value = true
  try {
    await send('/api/onboarding/tangent', {
      skip,
      name: form.name.trim(),
      description: form.description.trim(),
      artwork: form.artwork,
    })
    try {
      sessionStorage.setItem('tangent-created', props.site.site?.participant?.participantRef ?? '')
    } catch {
      /* private mode keeps no session state */
    }
    window.location.assign('/')
  } catch (err) {
    error.value = (err as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div id="first-tangent" class="onboarding-layout" :hidden="!active || onboardingState !== 'create_tangent'">
    <div class="onboarding-copy">
      <p class="eyebrow">Your first Tangent</p>
      <h1>This place is yours.</h1>
      <p class="lede">Give your first Tangent a name.</p>
      <p class="hint">This makes you the owner of this server, and its accountable human: you decide who can create Tangents and help look after the place.</p>
      <form id="first-tangent-form" class="stack" @submit.prevent="finish(false)">
        <div class="onboarding-choices">
          <p class="field-label" id="first-tangent-presets-label">Start from one of these <span class="field-note">Optional</span></p>
          <div id="first-tangent-presets" class="choice-row" role="group" aria-labelledby="first-tangent-presets-label">
            <button v-for="starter in starters" :key="starter.name" type="button" class="choice" :title="starter.description" @click="applyStarter(starter)">{{ starter.name }}</button>
            <button type="button" class="choice choice-clear" @click="clearStarter">Clear</button>
          </div>
        </div>
        <label for="first-tangent-name">Name<input id="first-tangent-name" v-model="form.name" class="input" name="name" maxlength="120" required placeholder="Small Hours" autocomplete="off"></label>
        <label for="first-tangent-description"><span class="field-label">A short description <span class="field-note">Optional</span></span><textarea id="first-tangent-description" v-model="form.description" class="input" name="description" rows="2" maxlength="1000" placeholder="What would you like to talk about?"></textarea></label>
        <div class="onboarding-choices">
          <p class="field-label" id="first-tangent-art-label">Card artwork <span class="field-note">Optional</span></p>
          <div id="first-tangent-art" class="art-row" role="group" aria-labelledby="first-tangent-art-label">
            <button type="button" class="art-choice art-none" title="No artwork" aria-label="No artwork" :aria-pressed="form.artwork === ''" @click="form.artwork = ''">✦</button>
            <button
              v-for="entry in starterArtwork"
              :key="entry.slug"
              type="button"
              class="art-choice"
              :title="entry.name"
              :aria-label="entry.name"
              :aria-pressed="form.artwork === entry.url"
              @click="form.artwork = entry.url"
            ><img :src="entry.url" alt="" loading="lazy"></button>
          </div>
          <input id="first-tangent-artwork" v-model="form.artwork" type="hidden" name="artwork" value="">
        </div>
        <p class="hint">You can make more Tangents later. Change these details anytime.</p>
        <div class="onboarding-actions">
          <button class="btn btn-primary" type="submit" :disabled="busy">Create Tangent</button>
          <button id="skip-first-tangent" class="btn btn-quiet" type="button" :disabled="busy" @click="finish(true)">Skip for now</button>
        </div>
        <p class="hint">Skip creates “My Tangent”. You can rename it later.</p>
        <div class="owner-switch">Not the account you intended? <form action="/auth/logout" method="post" class="switch-account"><input type="hidden" name="return" value="/onboarding/"><button type="submit">Switch account</button></form></div>
      </form>
    </div>
    <aside class="onboarding-preview" aria-label="Your Tangent card preview">
      <div class="tangent-card onboarding-tcg" style="--tangent-accent:#f4b942">
        <div class="tangent-card-top"><span class="card-kicker">TANGENT SPACE</span><span class="card-edition" aria-hidden="true">✧</span></div>
        <div id="first-card-face" class="card-face" :class="{ 'has-art': !!form.artwork }">
          <img v-if="form.artwork" id="first-card-art" class="card-art" :src="form.artwork" alt="">
          <span class="card-orbit" aria-hidden="true"></span>
          <span class="card-sigil" aria-hidden="true">✦</span>
        </div>
        <div><div class="card-stripe"></div><div class="card-glass"><h2 class="tangent-card-name" id="first-card-name">{{ cardName }}</h2><p class="tangent-card-description" id="first-card-description">{{ cardDescription }}</p><em class="tangent-card-rule">Make room for the next thought.</em></div></div>
      </div>
      <p class="preview-caption">Your card, as you make it.</p>
    </aside>
  </div>
  <p id="onboarding-error" class="onboarding-error" role="alert" tabindex="-1">{{ error }}</p>
  <div id="owner-waiting"" :hidden="!active || onboardingState !== 'waiting_owner'">
    <p class="eyebrow">A new beginning</p>
    <h1>This server is waiting for its owner.</h1>
    <p class="lede">The account you signed in with isn’t eligible to claim this server. Sign in with the designated human owner’s account.</p>
    <form action="/auth/logout" method="post"><input type="hidden" name="return" value="/onboarding/"><button class="btn btn-primary">Switch account</button></form>
  </div>
</template>
