<script setup lang="ts">
import SettingsToggle from './SettingsToggle.vue'
// Scenes and motion preview inside server appearance settings before saving.
import { computed, onMounted, watch } from 'vue'
import { asciiScenes } from '../atmosphere/ascii-scenes'
import type { Atmosphere } from '../composables/atmosphere'

const props = defineProps<{ atmosphere: Atmosphere; coordinated?: boolean; active?: boolean }>()

const a = props.atmosphere

const reduced = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null
const saveData = (navigator as Navigator & { connection?: { saveData?: boolean } }).connection?.saveData

const motionNote = computed(() => (reduced?.matches ? 'Your device prefers reduced motion. This scene will stay still.' : 'Data Saver is on. This scene will stay still.'))
const motionNoteVisible = computed(() => !!(reduced?.matches || saveData))

function sceneNumber(index: number): string {
  return '0' + (index + 1) + ' / 08'
}

function selectScene(id: string): void {
  a.change({ scene: id })
}

function onPaletteChange(event: Event) {
  const checked = (event.target as HTMLInputElement).checked
  a.change({ color: checked ? '' : '#d4b57c' })
}

function paintAll() {
  const preview = document.getElementById('atmosphere-preview') as HTMLCanvasElement | null
  if (preview) a.paintPreview(preview)
  for (const scene of asciiScenes) {
    const thumb = document.querySelector<HTMLCanvasElement>(`[data-scene-thumb="${scene.id}"]`)
    if (thumb) a.paintThumb(thumb, scene.id)
  }
  a.restart()
}

onMounted(() => {
  if (a.panelOpen) paintAll()
})

watch(() => [a.panelOpen, props.active], ([open, active]) => {
  if (open && active !== false) requestAnimationFrame(paintAll)
})

watch(() => a.settings.scene, () => a.restart())
watch(() => a.settings.intensity, () => a.restart())
watch(() => a.settings.color, () => a.restart())
watch(() => a.settings.motion, () => a.restart())
</script>

<template>
  <section id="atmosphere-panel" :hidden="!a.panelOpen" aria-labelledby="atmosphere-heading">
    <header class="atmosphere-heading">
      <div><h2 id="atmosphere-heading">Atmosphere</h2><p class="hint">Drawn behind the Space’s welcome. Preview it here, then save it for everyone.</p></div>
    </header>
    <div class="atmosphere-stage">
      <canvas id="atmosphere-preview" aria-hidden="true"></canvas>
      <div class="atmosphere-stage-caption">
        <span id="atmosphere-number">{{ a.settings.scene === 'none' ? 'STILL SPACE' : sceneNumber(Math.max(0, asciiScenes.findIndex(s => s.id === a.settings.scene))) }}</span>
        <h3 id="atmosphere-scene-name">{{ asciiScenes.find(s => s.id === a.settings.scene)?.name || 'A little quiet.' }}</h3>
        <p id="atmosphere-scene-note">{{ asciiScenes.find(s => s.id === a.settings.scene)?.note || 'Only the conversations. You can bring the light back anytime.' }}</p>
      </div>
    </div>
    <div class="atmosphere-scenes" role="group" aria-label="Choose an ASCII scene">
      <button
        v-for="(scene, index) in asciiScenes"
        :key="scene.id"
        type="button"
        class="atmosphere-scene"
        :data-scene="scene.id"
        :aria-pressed="a.settings.scene === scene.id"
        :disabled="a.saving"
        @click="selectScene(scene.id)"
      >
        <canvas :data-scene-thumb="scene.id" aria-hidden="true"></canvas>
        <span class="scene-number">{{ sceneNumber(index).slice(0, 2) }}</span>
        <span class="scene-name">{{ scene.name }}</span>
      </button>
    </div>
    <div class="atmosphere-controls">
      <label class="atmosphere-intensity" for="atmosphere-intensity">
        Intensity <output id="atmosphere-intensity-value">{{ a.settings.intensity }}%</output>
        <input id="atmosphere-intensity" type="range" min="0" max="100" step="1" :value="a.settings.intensity" :disabled="a.saving" @input="a.change({ intensity: Number(($event.target as HTMLInputElement).value) })">
      </label>
      <div class="atmosphere-color">
        <label><input type="checkbox" :checked="!a.settings.color" :disabled="a.saving" @change="onPaletteChange"> Scene colours</label>
        <input type="color" aria-label="Custom background colour" :value="a.settings.color || '#d4b57c'" :disabled="a.saving || !a.settings.color" @input="a.change({ color: ($event.target as HTMLInputElement).value })">
      </div>
      <SettingsToggle class="atmosphere-motion" :model-value="a.settings.motion" :disabled="a.saving" label="Gentle motion" description="Let the scene move slowly. Reduced-motion preferences keep it still." @update:model-value="a.change({ motion: $event })" />
      <SettingsToggle class="atmosphere-spotlight" :model-value="a.settings.mouseSpotlight" :disabled="a.saving" label="Mouse spotlight" description="Brighten nearby characters as you move your mouse." @update:model-value="a.change({ mouseSpotlight: $event })" />
    </div>
    <p id="atmosphere-motion-note" class="hint" :hidden="!motionNoteVisible">{{ motionNote }}</p>
    <footer class="atmosphere-footer">
      <div class="atmosphere-secondary">
        <button v-if="!coordinated" type="button" id="atmosphere-reset" class="btn btn-quiet" :disabled="a.saving || !a.dirty" @click="a.resetPreview()">Discard changes</button>
        <button type="button" id="atmosphere-off" class="btn btn-quiet" :aria-pressed="a.settings.scene === 'none'" :disabled="a.saving" @click="a.change({ scene: 'none' })">Turn the atmosphere off</button>
      </div>
      <button v-if="!coordinated" type="button" id="atmosphere-save" class="btn btn-primary" :hidden="!a.canManage" :disabled="a.saving || !a.dirty" @click="a.save()">{{ a.saving ? 'Saving…' : 'Save atmosphere' }}</button>
    </footer>
    <p id="atmosphere-status" class="hint" :class="{ error: a.statusError }" role="status">{{ a.statusMessage }}</p>
  </section>
</template>
