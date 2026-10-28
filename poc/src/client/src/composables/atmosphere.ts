import { httpRequest } from '../api'
import { computed, reactive, ref, watchEffect } from 'vue'
import { asciiScenes, sceneRenderer, type SceneRenderer } from '../atmosphere/ascii-scenes'
import type { ServerView } from '../stores/site'

// The server's atmosphere layer previews an actor-scoped draft while its editor
// is open. Closing the editor restores the saved backdrop and keeps the draft.

export interface AtmosphereSettings {
  scene: string
  color: string
  intensity: number
  motion: boolean
  mouseSpotlight: boolean
}

const DEFAULTS: AtmosphereSettings = { scene: 'galaxy', color: '', intensity: 35, motion: true, mouseSpotlight: true }

function sanitize(value: unknown): Partial<AtmosphereSettings> {
  const clean: Partial<AtmosphereSettings> = {}
  if (value && typeof value === 'object') {
    const v = value as Record<string, unknown>
    if (asciiScenes.some(s => s.id === v.scene) || v.scene === 'none') clean.scene = v.scene as string
    if (typeof v.color === 'string' && /^(#[0-9a-f]{6})?$/i.test(v.color)) clean.color = v.color
    if (Number.isFinite(v.intensity)) clean.intensity = Math.round(Math.max(0, Math.min(100, v.intensity as number)))
    if (typeof v.motion === 'boolean') clean.motion = v.motion
    if (typeof v.mouseSpotlight === 'boolean') clean.mouseSpotlight = v.mouseSpotlight
  }
  return clean
}

function fromServer(value: ServerView | null): AtmosphereSettings {
  return {
    ...DEFAULTS,
    ...sanitize({
      scene: value?.backgroundScene,
      color: value?.backgroundColor,
      intensity: value?.backgroundIntensity,
      motion: value?.backgroundMotion,
      mouseSpotlight: value?.backgroundMouseSpotlight,
    }),
  }
}

let installed = false
let background: SceneRenderer | null = null
let previewPainter: SceneRenderer | null = null
let frame: number | undefined
let lastFrame = 0
let time = 47
let interval = 1000 / 15
let previewWidth = 0
let backgroundWidth = 0
let backgroundHeight = 0
const pointer = { x: 0, y: 0, active: false }
let pointerDirty = false

const reduced = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null
const forced = typeof matchMedia === 'function' ? matchMedia('(forced-colors: active)') : null

export function createAtmosphere() {
  const serverSettings = ref<AtmosphereSettings>({ ...DEFAULTS })
  const draft = ref<AtmosphereSettings | null>(null)
  const panelOpen = ref(false)
  const saving = ref(false)
  const statusMessage = ref('Choose the atmosphere everyone sees here. Changes stay in preview until saved.')
  const statusError = ref(false)
  const canManage = ref(false)
  let participantRef = ''
  let saveGeneration = 0

  const settings = computed<AtmosphereSettings>(() => panelOpen.value && draft.value ? draft.value : serverSettings.value)
  const dirty = computed(() => !!draft.value && JSON.stringify(draft.value) !== JSON.stringify(serverSettings.value))

  function draftKey(): string {
    return `tangent:atmosphere-draft:${encodeURIComponent(participantRef)}`
  }

  function persistDraft(): void {
    if (!participantRef) return
    try {
      if (!dirty.value || !draft.value) sessionStorage.removeItem(draftKey())
      else sessionStorage.setItem(draftKey(), JSON.stringify(Object.fromEntries(
        (Object.keys(serverSettings.value) as (keyof AtmosphereSettings)[])
          .filter(field => draft.value![field] !== serverSettings.value[field])
          .map(field => [field, draft.value![field]]),
      )))
    } catch { /* Preview remains usable without browser storage. */ }
  }

  function restoreDraft(): void {
    if (!participantRef) return
    try {
      const stored = sessionStorage.getItem(draftKey())
      draft.value = stored ? { ...serverSettings.value, ...sanitize(JSON.parse(stored)) } : null
    } catch { draft.value = null }
  }

  function moving(): boolean {
    const s = settings.value
    const saveData = (navigator as Navigator & { connection?: { saveData?: boolean } }).connection?.saveData
    return !!s.motion && !reduced?.matches && !forced?.matches && !saveData && s.scene !== 'none' && s.intensity > 0
  }

  function install(): void {
    if (installed) return
    installed = true
    const canvas = document.createElement('canvas')
    canvas.id = 'ascii-atmosphere'
    canvas.setAttribute('aria-hidden', 'true')
    document.body.prepend(canvas)
    background = sceneRenderer(canvas)
    const placeCanvas = () => {
      const hero = document.querySelector('.space-hero:not(.space-hero-preview)')
      canvas.hidden = !hero
      if (hero && canvas.parentElement !== hero) hero.prepend(canvas)
      else if (!hero && canvas.parentElement !== document.body) document.body.prepend(canvas)
      if (resize()) restart()
    }
    placeCanvas()
    new MutationObserver(placeCanvas).observe(document.body, { childList: true, subtree: true })

    // The welcome illustration follows the saved atmosphere and editor preview.
    watchEffect(() => {
      const s = settings.value
      document.body.classList.toggle('atmosphere-on', s.scene !== 'none' && s.intensity > 0)
      const target = document.getElementById('ascii-atmosphere')
      if (target) {
        target.style.opacity = s.intensity > 0 ? '1' : '0'
        target.dataset.scene = s.scene
        target.dataset.motion = moving() ? 'animated' : 'still'
      }
    })

    window.addEventListener('resize', () => {
      resize()
      restart()
    }, { passive: true })
    document.addEventListener('visibilitychange', restart)
    reduced?.addEventListener?.('change', restart)
    forced?.addEventListener?.('change', restart)

    // Mouse coordinates remain in this page. Touch does not acquire a sticky spotlight.
    document.addEventListener('pointermove', event => {
      if (event.pointerType !== 'mouse') {
        clearPointer()
        return
      }
      const s = settings.value
      if (!s.mouseSpotlight || s.scene === 'none' || s.intensity === 0 || forced?.matches) return
      pointer.x = event.clientX
      pointer.y = event.clientY
      pointer.active = true
      pointerFrame()
    }, { passive: true })
    document.addEventListener('pointerout', event => {
      if (event.relatedTarget === null) clearPointer()
    })
    window.addEventListener('blur', clearPointer)

    draw()
  }

  function resize(): boolean {
    const hero = document.querySelector('.space-hero:not(.space-hero-preview)')
    const box = hero?.getBoundingClientRect()
    if (!box?.width || !box.height || (box.width === backgroundWidth && box.height === backgroundHeight)) return false
    backgroundWidth = box.width
    backgroundHeight = box.height
    background?.resize(box.width, box.height)
    return true
  }

  function paintPreview(canvas: HTMLCanvasElement): void {
    previewPainter = sceneRenderer(canvas)
    const box = canvas.getBoundingClientRect()
    previewWidth = box.width
    previewPainter.resize(box.width, box.height)
  }

  function paintThumb(canvas: HTMLCanvasElement, sceneId: string): void {
    const painter = sceneRenderer(canvas, true)
    const width = canvas.clientWidth
    if (width > 0) painter.resize(width, canvas.clientHeight)
    painter.draw(sceneId, 47)
  }

  function draw(): void {
    const s = settings.value
    const start = performance.now()
    const spotlight =
      s.mouseSpotlight && pointer.active && !forced?.matches && s.scene !== 'none' && s.intensity > 0
        ? { x: pointer.x, y: pointer.y, radius: Math.max(180, Math.min(420, Math.min(window.innerWidth, window.innerHeight) * 0.26)) }
        : undefined
    const hero = document.querySelector('.space-hero:not(.space-hero-preview)')?.getBoundingClientRect()
    const heroSpotlight = spotlight && hero ? { ...spotlight, x: spotlight.x - hero.left, y: spotlight.y - hero.top } : undefined
    if (hero) background?.draw(s.scene, time, s.color, { opacity: (s.intensity / 100) * 0.85, spotlight: heroSpotlight })
    const preview = document.getElementById('atmosphere-preview') as HTMLCanvasElement | null
    if (panelOpen.value && previewPainter && previewWidth > 0 && preview) {
      const box = preview.getBoundingClientRect()
      const inside =
        spotlight && pointer.x >= box.left && pointer.x <= box.right && pointer.y >= box.top && pointer.y <= box.bottom
          ? { x: pointer.x - box.left, y: pointer.y - box.top, radius: Math.max(85, Math.min(180, box.width * 0.23)) }
          : undefined
      previewPainter.draw(s.scene, time, s.color, { opacity: Math.sqrt(s.intensity / 100), spotlight: inside })
    }
    // Slow devices keep the same detailed composition and render fewer frames.
    interval = performance.now() - start > 28 ? 1000 / 10 : 1000 / 15
  }

  function tick(now: number): void {
    if (document.hidden || (!moving() && !pointerDirty)) {
      frame = undefined
      return
    }
    if (now - lastFrame >= interval) {
      if (moving()) time += Math.min((now - lastFrame) / 1000, 0.15)
      lastFrame = now
      draw()
      pointerDirty = false
    }
    frame = moving() || pointerDirty ? requestAnimationFrame(tick) : undefined
  }

  function restart(): void {
    if (frame !== undefined) cancelAnimationFrame(frame)
    frame = undefined
    lastFrame = performance.now()
    if (document.hidden) {
      pointer.active = false
      pointerDirty = false
      return
    }
    draw()
    pointerDirty = false
    if (moving()) frame = requestAnimationFrame(tick)
  }

  function pointerFrame(): void {
    if (document.hidden) return
    pointerDirty = true
    if (frame === undefined) frame = requestAnimationFrame(tick)
  }

  function clearPointer(): void {
    if (!pointer.active) return
    pointer.active = false
    pointerFrame()
  }

  /** Apply current server settings and isolate previews by signed-in actor. */
  function configure(server: ServerView | null, participant: { participantRef: string } | null): void {
    const changedActor = participantRef !== (participant?.participantRef ?? '')
    const previouslyAuthorized = canManage.value
    serverSettings.value = fromServer(server)
    participantRef = participant?.participantRef ?? ''
    canManage.value = server?.canManage === true && !!participantRef
    if (changedActor || !canManage.value) {
      saveGeneration++
      saving.value = false
      draft.value = null
      close()
      statusError.value = false
      statusMessage.value = ''
      if (canManage.value) restoreDraft()
    }
    else if (!previouslyAuthorized) restoreDraft()
    restart()
  }

  function change(patch: Partial<AtmosphereSettings>): void {
    if (!canManage.value || !panelOpen.value || saving.value) return
    draft.value = { ...settings.value, ...sanitize(patch) }
    persistDraft()
    restart()
    statusError.value = false
    statusMessage.value = 'Previewing your changes. Save when it feels right.'
  }

  function open(): void {
    if (!canManage.value) return
    if (panelOpen.value) return
    draft.value ??= { ...serverSettings.value }
    panelOpen.value = true
    restart()
    statusMessage.value = dirty.value ? 'Your unsaved atmosphere is ready to preview.' : 'Preview changes here, then save them for everyone.'
  }

  function close(): void {
    if (!panelOpen.value) return
    panelOpen.value = false
    previewWidth = 0
    restart()
  }

  function resetPreview(): void {
    if (saving.value) return
    draft.value = { ...serverSettings.value }
    persistDraft()
    restart()
    statusError.value = false
    statusMessage.value = 'Preview restored to the saved atmosphere.'
  }

  async function save(): Promise<void> {
    if (!canManage.value || !draft.value || !dirty.value || saving.value) return
    const actor = participantRef
    const generation = ++saveGeneration
    const s = { ...draft.value }
    saving.value = true
    statusError.value = false
    statusMessage.value = 'Saving atmosphere…'
    try {
      const response = await httpRequest('/api/server', {
        method: 'PATCH',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          backgroundScene: s.scene,
          backgroundColor: s.color,
          backgroundIntensity: s.intensity,
          backgroundMotion: s.motion,
          backgroundMouseSpotlight: s.mouseSpotlight,
        }),
      })
      const result = await response.json().catch(() => ({}))
      if (!response.ok) throw new Error((result as Record<string, string>).error || (result as Record<string, string>).reason || 'The atmosphere could not be saved. Try again.')
      if (generation !== saveGeneration || participantRef !== actor) return
      serverSettings.value = fromServer(result as ServerView)
      draft.value = panelOpen.value ? { ...serverSettings.value } : null
      persistDraft()
      restart()
      statusMessage.value = 'Atmosphere saved.'
    } catch (error) {
      if (generation !== saveGeneration || participantRef !== actor) return
      statusError.value = true
      statusMessage.value = (error as Error).message
    } finally {
      if (generation === saveGeneration) saving.value = false
    }
  }

  return reactive({
    settings, serverSettings, draft, panelOpen, saving, statusMessage, statusError, canManage, dirty,
    install, configure, change, open, close, resetPreview, save,
    paintPreview, paintThumb, restart,
  })
}

export type Atmosphere = ReturnType<typeof createAtmosphere>
