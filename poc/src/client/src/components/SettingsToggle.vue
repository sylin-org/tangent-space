<script setup lang="ts">
defineProps<{ label: string; description?: string; disabled?: boolean }>()
const value = defineModel<boolean>({ required: true })
</script>

<template>
  <label class="settings-toggle">
    <span class="settings-toggle-copy"><strong>{{ label }}</strong><small v-if="description">{{ description }}</small></span>
    <input v-model="value" type="checkbox" role="switch" :aria-checked="value" :aria-label="label" :disabled="disabled">
    <span class="settings-toggle-track" aria-hidden="true"></span>
  </label>
</template>

<style>
.settings-toggle{position:relative;display:flex;align-items:flex-start;gap:16px;padding:16px 0;border-top:1px solid var(--ts-line);font:var(--ts-type-ui-small);cursor:pointer}
.settings-toggle-copy{display:grid;gap:4px;flex:1;min-width:0}.settings-toggle-copy strong{font-size:14px;font-weight:600}.settings-toggle-copy small{font:var(--ts-type-meta);color:var(--ts-text-2)}
.settings-toggle input{position:absolute;right:0;top:16px;width:38px;height:22px;margin:0;opacity:0;z-index:1;cursor:pointer}.settings-toggle-track{position:relative;flex:none;width:38px;height:22px;border:1.5px solid var(--ts-text-3);border-radius:11px;margin-top:2px;background:transparent}.settings-toggle-track::after{content:'';position:absolute;left:2px;top:2px;width:15px;height:15px;border-radius:50%;background:var(--ts-text-3)}
.settings-toggle input:checked+.settings-toggle-track{border-color:var(--ts-accent);background:var(--ts-accent)}.settings-toggle input:checked+.settings-toggle-track::after{left:18px;top:1.5px;width:16px;height:16px;background:var(--ts-on-accent)}.settings-toggle input:focus-visible+.settings-toggle-track{outline:2px solid var(--ts-accent);outline-offset:4px}.settings-toggle:has(input:disabled){opacity:.6;cursor:default}
.integration-card{padding:18px 20px;border:1px solid var(--ts-line);border-radius:14px;background:var(--ts-surface)}.integration-heading{display:flex;align-items:center;gap:12px;margin-bottom:14px}.integration-heading h3{margin:0;font:600 16px var(--ts-font-ui)}.integration-heading p{margin:4px 0 0;color:var(--ts-text-2);font:var(--ts-type-meta)}.integration-mark{display:grid;place-items:center;width:40px;height:40px;border:1px solid var(--ts-line);border-radius:10px;color:var(--ts-text-2);font-size:24px}.integration-feed-heading{display:flex;align-items:center;gap:16px;padding:16px 0;border-top:1px solid var(--ts-line)}.integration-feed-heading>div{flex:1}.integration-feed-heading h3{margin:0 0 4px;font:600 14px var(--ts-font-ui)}.integration-feed-heading p{margin:0;color:var(--ts-text-2);font:var(--ts-type-meta)}.integration-feed-heading .btn{flex:none}.integration-status{display:flex;align-items:center;flex-wrap:wrap;gap:12px;margin-top:20px}.integration-status p{margin:0}.media-storage-panel>.stack.storage-fields{gap:24px}.storage-processing{padding:0 20px;border:1px solid var(--ts-line);border-radius:12px;background:var(--ts-surface)}.storage-processing>.settings-toggle:first-child{border-top:0}
@media(max-width:719px){.integration-feed-heading{flex-wrap:wrap}.integration-feed-heading>div{flex-basis:100%}.integration-card{padding:16px}.settings-toggle{min-height:60px}.settings-toggle input{height:44px;top:5px}.integration-feed-heading .btn{min-height:44px}}
</style>
