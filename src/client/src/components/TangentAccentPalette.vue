<script setup lang="ts">
import { tangentAccents } from '../composables/tangent-accents'
defineProps<{modelValue: string; name?: string}>()
const emit = defineEmits<{'update:modelValue':[value:string]}>()
</script>
<template><fieldset><legend>Tangent accent</legend><div class="tangent-accent-palette"><label v-for="tone in tangentAccents" :key="tone.name"><input type="radio" :name="name || 'tangent-accent'" :value="tone.value" :checked="modelValue === tone.value" @change="emit('update:modelValue', tone.value)"><span :style="{background:tone.value}" aria-hidden="true"></span>{{tone.name}}</label></div></fieldset></template>
