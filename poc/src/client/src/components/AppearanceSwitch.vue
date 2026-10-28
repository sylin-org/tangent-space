<script setup lang="ts">
import { useAppearance, type Theme } from '../composables/appearance'
const { theme, select } = useAppearance()
const choices: Theme[] = ['system', 'light', 'dark']
</script>
<template>
  <div class="appearance-switch" role="group" aria-label="Appearance">
    <button v-for="choice in choices" :key="choice" type="button" :aria-pressed="theme === choice" @click="select(choice)">{{ choice[0]!.toUpperCase() + choice.slice(1) }}</button>
  </div>
</template>
