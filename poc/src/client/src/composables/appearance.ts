import { ref } from 'vue'
import { rolesRequest } from './roles-api'
export type Theme = 'system' | 'light' | 'dark'
const theme = ref<Theme>('system')
let account='visitor',generation=0
function apply(value:Theme){theme.value=value;if(value==='system')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=value;try{localStorage.setItem('tangent:appearance:'+account,value)}catch{}}
export function useAppearance(){
 function select(value:Theme){apply(value);generation++;if(account!=='visitor')void rolesRequest('/api/workspace/preferences',{method:'PATCH',body:{theme:value}}).catch(()=>{})}
 async function identify(actor:string){account=actor||'visitor';const current=++generation;let saved:string|null=null;try{saved=localStorage.getItem('tangent:appearance:'+account)}catch{};apply(saved==='light'||saved==='dark'?saved:'system');if(!actor)return;try{const preferences=await rolesRequest<{theme:Theme}>('/api/workspace/preferences');if(current===generation&&['system','light','dark'].includes(preferences.theme))apply(preferences.theme)}catch{}}
 return {theme,select,identify}
}
