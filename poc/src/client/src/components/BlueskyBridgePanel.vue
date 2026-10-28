<script setup lang="ts">
import {computed,reactive,ref,watch} from 'vue'
import SettingsToggle from './SettingsToggle.vue'
import {rolesRequest} from '../composables/roles-api'
import {registerSettingsChange} from '../composables/settings-changes'
const props=defineProps<{actorKey:string;active:boolean}>()
const form=reactive({bringReplies:false,showCounts:false,flows:[] as {kind:string;source:string;tangentKey:string}[]}),saved=ref(''),busy=ref(false),error=ref(''),status=ref(''),refreshedAt=ref(''),loaded=ref(false)
const tangents=ref<{key:string;name:string;canManage?:boolean;canCreateTopic?:boolean}[]>([])
const snapshot=()=>JSON.stringify(form)
const dirty=computed(()=>!!saved.value&&snapshot()!==saved.value)
let generation=0
async function load(){const token=++generation;busy.value=true;error.value='';try{const [data,directory]=await Promise.all([rolesRequest<typeof form & {status:string;refreshedAt?:string}>('/api/v1/content/bluesky/bridge'),rolesRequest<{tangents:typeof tangents.value}>('/api/v1/tangents')]);if(token!==generation)return;Object.assign(form,{bringReplies:data.bringReplies,showCounts:data.showCounts,flows:data.flows});saved.value=snapshot();status.value=data.status;refreshedAt.value=data.refreshedAt||'';tangents.value=directory.tangents.filter(item=>item.canManage&&item.canCreateTopic);loaded.value=true}catch(e){if(token===generation)error.value=(e as Error).message}finally{if(token===generation)busy.value=false}}
async function save(){if(!loaded.value||busy.value)return;const token=generation,submitted=snapshot();busy.value=true;error.value='';try{await rolesRequest('/api/v1/content/bluesky/bridge',{method:'PATCH',body:JSON.parse(submitted)});if(token===generation)saved.value=submitted}catch(e){if(token===generation)error.value=(e as Error).message}finally{if(token===generation)busy.value=false}}
async function refresh(){if(busy.value||!loaded.value||dirty.value)return;const token=generation;busy.value=true;error.value='';try{const result=await rolesRequest<{status:string;refreshedAt?:string}>('/api/v1/content/bluesky/bridge/refresh',{method:'POST',body:{}});if(token!==generation)return;status.value=result.status;refreshedAt.value=result.refreshedAt||''}catch(e){if(token===generation)error.value=(e as Error).message}finally{if(token===generation)busy.value=false}}
watch(()=>props.actorKey,actor=>{generation++;loaded.value=false;saved.value='';error.value='';status.value='';refreshedAt.value='';busy.value=false;Object.assign(form,{bringReplies:false,showCounts:false,flows:[]});if(actor&&props.active)void load()}, {immediate:true})
watch(()=>props.active,active=>{if(active&&props.actorKey&&!loaded.value&&!busy.value)void load()})
registerSettingsChange({label:'Sources',count:()=>dirty.value?1:0,busy:()=>busy.value,save,discard:()=>{if(saved.value)Object.assign(form,JSON.parse(saved.value))},status:()=>error.value})
</script>
<template>
<section class="bluesky-bridge-panel stack" aria-labelledby="sources-title">
  <header><h2 id="sources-title">Sources</h2><p>Places this Space brings posts in from.</p></header>
  <p v-if="busy && !loaded" role="status">Loading sources…</p>
  <button v-if="error && !loaded" class="btn btn-quiet" type="button" @click="load">Retry sources</button>
  <fieldset v-if="loaded" class="source-fields" :disabled="busy">
    <section class="integration-card">
      <header class="integration-heading"><span class="integration-mark" aria-hidden="true">≈</span><div><h3>Bluesky</h3><p>Public conversations from the accounts and feeds you choose.</p></div></header>
      <SettingsToggle v-model="form.bringReplies" label="Bring replies home" description="Replies on Bluesky to a post published from here appear under it, with their source and author." :disabled="busy" />
      <SettingsToggle v-model="form.showCounts" label="Show likes and reposts" description="Counts next to each post published from here." :disabled="busy" />
      <div class="integration-feed-heading"><div><h3>Accounts and feeds</h3><p>Bring new public posts into a Tangent as topics.</p></div><button v-if="form.flows.length<5" class="btn" type="button" @click="form.flows.push({kind:'account',source:'',tangentKey:tangents[0]?.key||''})">+ Add an account or feed</button></div>
      <p v-if="!form.flows.length" class="hint">No feeds yet.</p>
      <div v-for="(flow,index) in form.flows" :key="index" class="source-flow">
        <label>Source kind<select v-model="flow.kind" class="input"><option value="account">Account</option><option value="feed">Feed</option></select></label>
        <label>{{flow.kind==='feed'?'Feed at:// URI':'Account handle or DID'}}<input v-model="flow.source" class="input" maxlength="2048" :placeholder="flow.kind==='feed'?'at://did:plc:…/app.bsky.feed.generator/…':'alice.bsky.social'"></label>
        <label>Into<select v-model="flow.tangentKey" class="input"><option v-for="tangent in tangents" :key="tangent.key" :value="tangent.key">{{tangent.name}}</option></select></label>
        <button type="button" class="btn btn-quiet" @click="form.flows.splice(index,1)">Remove flow</button>
      </div>
      <p v-if="form.flows.length" class="hint">Refreshes run every ten minutes, importing up to 25 original posts per flow. Each topic follows its Tangent’s audience. Removing a flow keeps the topics already imported.</p>
    </section>
    <div class="integration-status"><button class="btn" :disabled="busy||dirty" type="button" @click="refresh">{{busy?'Refreshing…':'Refresh saved sources now'}}</button><p v-if="refreshedAt" class="hint">Last refresh {{new Date(refreshedAt).toLocaleString()}}</p><p v-if="status" role="status" class="hint">{{status}}</p></div>
  </fieldset>
  <p v-if="error" role="alert" class="error">{{error}}</p>
</section>
</template>
<style scoped>.source-fields{border:0;padding:0;margin:0;min-width:0}.source-flow{margin-bottom:12px;background:var(--ts-page)}</style>
