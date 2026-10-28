<script setup lang="ts">
import type { Directories } from '../composables/directories'
import TangentShelf from './TangentShelf.vue'
defineProps<{directories:Directories;signedIn:boolean}>()
defineEmits<{settings:[key:string]}>()
</script>
<template>
<div id="tangent-list"><TangentShelf v-for="tangent in directories.tangents" :key="tangent.key" :tangent="tangent" :signed-in="signedIn"/></div><p v-if="directories.tangentsLoading" class="hint" role="status">Loading Tangents…</p><p v-if="directories.tangentsError" class="error" role="alert">{{directories.tangentsError}}</p><p id="no-tangents" v-if="!directories.tangents.length&&directories.tangentsLoaded&&!directories.tangentsError" class="hint">{{signedIn?'No Tangents are available to your account here yet.':'There are no public conversations here yet.'}}</p><p v-if="directories.tangentsIncomplete" class="directory-partial">This view is partial. Open a Tangent directly if it is not listed.</p><button v-if="directories.tangentsNextPage" id="more-tangents" class="btn btn-quiet" type="button" @click="directories.loadTangents(directories.tangentsNextPage??undefined)">More Tangents</button>
</template>
