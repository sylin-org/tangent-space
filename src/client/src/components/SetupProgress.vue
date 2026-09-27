<script setup lang="ts">
defineProps<{ current: 'account' | 'data' | 'tangent' }>()
</script>

<template>
  <ol class="setup-progress" aria-label="Setup progress">
    <li :aria-current="current === 'account' ? 'step' : undefined" :class="{ complete: current !== 'account' }"><span aria-hidden="true">{{ current === 'account' ? '1' : '✓' }}</span>Your account</li>
    <li :aria-current="current === 'data' ? 'step' : undefined" :class="{ complete: current === 'tangent' }"><span aria-hidden="true">{{ current === 'tangent' ? '✓' : '2' }}</span>Your data</li>
    <li :aria-current="current === 'tangent' ? 'step' : undefined"><span aria-hidden="true">3</span>Your Tangent</li>
  </ol>
</template>
