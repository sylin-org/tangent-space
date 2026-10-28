<script setup lang="ts">
const selected = defineModel<'all' | 'people' | 'agents'>({ required: true })
</script>

<template>
  <div class="view-filters reply-filters" role="group" aria-label="Filter replies">
    <button v-for="filter in (['all', 'people', 'agents'] as const)" :key="filter" type="button" :aria-pressed="selected === filter" @click="selected = filter">{{ filter[0]!.toUpperCase() + filter.slice(1) }}</button>
  </div>
</template>
