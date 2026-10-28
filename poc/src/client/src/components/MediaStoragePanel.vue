<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import SettingsToggle from './SettingsToggle.vue'
import { rolesRequest } from '../composables/roles-api'
import { registerSettingsChange } from '../composables/settings-changes'
const props=defineProps<{actorKey:string;active:boolean}>()
const defaults={location:'local',maximumUploadMb:5,resizeImages:true,removeLocationData:true,endpoint:'',bucket:'',region:'us-east-1'}
const form=reactive({...defaults,accessKey:'',secretKey:''})
const saved=ref({...defaults})
const loaded=ref(false),busy=ref(false),error=ref(''),credentialsConfigured=ref(false)
let generation=0
const count=computed(()=>Object.keys(saved.value).filter(key=>form[key as keyof typeof saved.value]!==saved.value[key as keyof typeof saved.value]).length+Number(!!form.accessKey)+Number(!!form.secretKey))
async function load(){if(!props.active||!props.actorKey||loaded.value)return;const token=++generation;busy.value=true;error.value='';try{const result=await rolesRequest<typeof defaults&{credentialsConfigured:boolean}>('/api/media/storage');if(token!==generation)return;saved.value={location:result.location,maximumUploadMb:result.maximumUploadMb,resizeImages:result.resizeImages,removeLocationData:result.removeLocationData,endpoint:result.endpoint,bucket:result.bucket,region:result.region};Object.assign(form,saved.value,{accessKey:'',secretKey:''});credentialsConfigured.value=result.credentialsConfigured;loaded.value=true}catch(cause){if(token===generation)error.value=(cause as Error).message}finally{if(token===generation)busy.value=false}}
watch(()=>props.actorKey,()=>{generation++;loaded.value=false;busy.value=false;saved.value={...defaults};Object.assign(form,defaults,{accessKey:'',secretKey:''});void load()},{immediate:true})
watch(()=>props.active,()=>{if(!busy.value)void load()})
async function save(){if(busy.value||!loaded.value||!count.value)return;const token=generation,submitted={...form};busy.value=true;error.value='';try{await rolesRequest('/api/media/storage',{method:'PATCH',body:submitted});if(token!==generation)return;const {accessKey,secretKey,...values}=submitted;saved.value={...values};credentialsConfigured.value=submitted.location==='s3';if(form.accessKey===accessKey)form.accessKey='';if(form.secretKey===secretKey)form.secretKey=''}catch(cause){if(token===generation)error.value=(cause as Error).message}finally{if(token===generation)busy.value=false}}
registerSettingsChange({label:'Media storage',count:()=>count.value,busy:()=>busy.value,save,discard:()=>{Object.assign(form,saved.value,{accessKey:'',secretKey:''});error.value=''},status:()=>error.value})
</script>
<template>
<section class="media-storage-panel stack" aria-labelledby="storage-title" :aria-busy="busy">
<header><h2 id="storage-title">Storage</h2><p>Where images live, and how they arrive.</p></header>
<p v-if="busy&&!loaded" role="status">Loading storage…</p><p v-if="error" class="error" role="alert">{{error}} <button v-if="!loaded" class="btn btn-quiet" type="button" :disabled="busy" @click="load">Retry storage</button></p>
<fieldset v-if="loaded" class="stack storage-fields" :disabled="busy"><fieldset class="storage-locations"><legend>Image storage</legend><label><input v-model="form.location" type="radio" value="local"><span><strong>This machine</strong><small>Stored with this Space. Include images in your backups.</small></span></label><label><input v-model="form.location" type="radio" value="s3"><span><strong>S3-compatible bucket</strong><small>A private bucket. Access follows each post’s audience.</small></span></label></fieldset>
<div v-if="form.location==='s3'" class="stack"><label>Service address<input v-model="form.endpoint" class="input" type="url" placeholder="https://s3.example.org" autocomplete="off"></label><label>Bucket<input v-model="form.bucket" class="input" autocomplete="off"></label><label>Region<input v-model="form.region" class="input" autocomplete="off"></label><label>Access key<input v-model="form.accessKey" class="input" type="password" autocomplete="new-password" :placeholder="credentialsConfigured?'Leave empty to keep the saved key':''"></label><label>Secret key<input v-model="form.secretKey" class="input" type="password" autocomplete="new-password" :placeholder="credentialsConfigured?'Leave empty to keep the saved secret':''"></label><p class="hint">Save tests a write, read and delete before using this connection. Credentials stay on the host.</p></div>
<p class="hint">This choice applies to new uploads. Existing images keep their storage location and audience.</p><label>Largest upload<select v-model.number="form.maximumUploadMb" class="input"><option v-for="size in [1,2,5,10,20]" :key="size" :value="size">{{size}} MB</option></select></label><div class="storage-processing"><SettingsToggle v-model="form.resizeImages" label="Resize large images" description="Keep images within 2,400 pixels while preserving their proportions." :disabled="busy" /><SettingsToggle v-model="form.removeLocationData" label="Remove location data" description="Keep location and camera metadata out of uploaded images." :disabled="busy" /></div><p class="hint">Processing preserves orientation and removes image metadata. These settings apply to new uploads.</p><section class="storage-copy"><h3>A copy you can keep</h3><p>Download images and their post references. The copy includes private and unpublished images; keep it somewhere private.</p><a class="btn btn-quiet" href="/api/media/storage/download">Download a copy</a></section></fieldset>
</section>
</template>
<style scoped>.storage-fields { border: 0; padding: 0; margin: 0; min-width: 0; }</style>

