<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { clearSetupState, readSetupArrival, type SetupArrival } from '../composables/onboarding-state'
const props = defineProps<{ site: SiteStore; router: RouterStore }>()
const emit = defineEmits<{ start: [] }>()
const arrival = ref<SetupArrival | null>(null)
watch(() => props.site.site?.participant?.did, did => { arrival.value = did ? readSetupArrival(did) : null }, { immediate: true })
const visible = computed(() => props.site.site?.participant?.isOwner && props.router.route.kind === 'topics' && props.router.route.tangent === arrival.value?.key)
function dismiss() { clearSetupState('arrival', props.site.site?.participant?.did || ''); arrival.value = null }
function start() { emit('start'); dismiss() }
</script>

<template>
  <section v-if="visible && arrival" class="first-arrival" aria-labelledby="first-arrival-title">
    <span class="first-arrival-mark" aria-hidden="true">✦</span>
    <div class="first-arrival-copy"><p class="eyebrow">Your place is ready</p><h2 id="first-arrival-title">Welcome to {{ arrival.name }}.</h2><p>What would you like to talk about? Start a Topic for your first conversation.</p><div class="onboarding-actions"><button class="btn btn-primary" type="button" @click="start">Start a conversation</button><button class="btn btn-quiet" type="button" @click="dismiss">Explore my Tangent</button></div><a class="first-arrival-settings" :href="'/t/' + encodeURIComponent(arrival.key) + '/settings'">Review who can take part →</a></div>
  </section>
</template>
