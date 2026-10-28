import {expect,it,vi} from 'vitest'
import {createSettingsChanges} from '../src/composables/settings-changes'
it('discards only dirty sections without initializing untouched drafts',()=>{
 const changes=createSettingsChanges();let dirty=1;const untouched=vi.fn()
 changes.entries.set(Symbol(),{label:'General',count:()=>dirty,busy:()=>false,save:async()=>{},discard:()=>{dirty=0},status:()=>''})
 changes.entries.set(Symbol(),{label:'Unopened section',count:()=>0,busy:()=>false,save:async()=>{},discard:untouched,status:()=>''})
 changes.discard();expect(changes.count.value).toBe(0);expect(untouched).not.toHaveBeenCalled()
})
