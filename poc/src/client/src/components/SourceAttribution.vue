<script setup lang="ts">
import {computed,ref,watch} from 'vue'
import {httpRequest} from '../api'
const props=defineProps<{postId:string;sourceUri?:string}>()
const counts=ref<{likes:number;reposts:number;fetchedAt:string}|null>(null)
const url=computed(()=>{const match=/^at:\/\/(did:[^/]+)\/app\.bsky\.feed\.post\/([a-z0-9]+)$/.exec(props.sourceUri||'');return match?'https://bsky.app/profile/'+encodeURIComponent(match[1]! )+'/post/'+match[2]:''})
let generation=0
watch(()=>props.postId,async id=>{const current=++generation;counts.value=null;try{const r=await httpRequest('/api/v1/content/bluesky/bridge/counts/'+encodeURIComponent(id),{cache:'no-store'});const data=r.ok?await r.json():null;if(current===generation)counts.value=data&&Number.isInteger(data.likes)&&Number.isInteger(data.reposts)?data:null}catch{}}, {immediate:true})
</script>
<template><span v-if="url||counts" class="source-attribution"><a v-if="url" :href="url" target="_blank" rel="noopener noreferrer">From Bluesky ↗</a><span v-if="counts" :title="'Checked '+new Date(counts.fetchedAt).toLocaleString()">{{counts.likes}} likes · {{counts.reposts}} reposts on Bluesky</span></span></template>
