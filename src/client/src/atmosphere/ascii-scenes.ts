// One bounded glyph grid, ported from ascii-scenes.js. Scenes paint light into
// it; the renderer draws ASCII from an atlas.

const TAU = Math.PI * 2
const clamp = (x: number, a = 0, b = 1) => Math.max(a, Math.min(b, x))
const fract = (x: number) => x - Math.floor(x)
const hash = (n: number) => fract(Math.sin(n * 127.1 + 311.7) * 43758.5453)

export interface AsciiScene {
  id: string
  name: string
  note: string
  colors: [string, string, string]
}

export const asciiScenes: AsciiScene[] = [
  { id: 'galaxy', name: 'Spiral Galaxy', note: 'A thousand small lights, finding their way home.', colors: ['#b4a9ee', '#7bc7cf', '#ffe0aa'] },
  { id: 'synapses', name: 'Synapses', note: 'An idea travels. Another answers.', colors: ['#608ab7', '#60d8cb', '#d2f8e0'] },
  { id: 'aurora', name: 'Aurora', note: 'Curtains of light across a midnight sky.', colors: ['#8e82da', '#4dcbb1', '#c1f3d1'] },
  { id: 'tides', name: 'Tidal Lines', note: 'Slow currents. Nothing stays quite the same.', colors: ['#648ab6', '#65cbd5', '#c2e8de'] },
  { id: 'orrery', name: 'Orrery', note: 'Little worlds, quietly keeping one another company.', colors: ['#9b9bc9', '#d2aa79', '#ffe5b5'] },
  { id: 'mycelium', name: 'Mycelium', note: 'Something is growing between us.', colors: ['#7b9c8f', '#b3c795', '#e4cca0'] },
  { id: 'rain', name: 'Silver Rain', note: 'The world outside. A warm place within.', colors: ['#777fae', '#aaa5d1', '#dbe3ed'] },
  { id: 'nebula', name: 'Nebula', note: 'Clouds of possibility, before they become stars.', colors: ['#7c99d8', '#c390bb', '#e9c4b0'] },
]

const noiseTable = Float32Array.from({ length: 4096 }, (_, i) => hash(i + 219))

function noise(x: number, y: number): number {
  const ix = Math.floor(x)
  const iy = Math.floor(y)
  const fx = fract(x)
  const fy = fract(y)
  const sx = fx * fx * (3 - 2 * fx)
  const sy = fy * fy * (3 - 2 * fy)
  const a = noiseTable[(ix & 63) + ((iy & 63) << 6)]!
  const b = noiseTable[((ix + 1) & 63) + ((iy & 63) << 6)]!
  const c = noiseTable[(ix & 63) + (((iy + 1) & 63) << 6)]!
  const d = noiseTable[((ix + 1) & 63) + (((iy + 1) & 63) << 6)]!
  return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy
}

interface Graph {
  nodes: Array<{ x: number; y: number }>
  edges: Array<[number, number]>
}

function graph(count: number): Graph {
  const nodes = Array.from({ length: count }, (_, i) => ({ x: 0.04 + hash(i * 4 + 1) * 0.92, y: 0.05 + hash(i * 4 + 2) * 0.9 }))
  const edges: Array<[number, number]> = []
  nodes.forEach((p, i) => {
    const near = nodes
      .map((q, j) => ({ j, d: Math.hypot(p.x - q.x, p.y - q.y) }))
      .filter(n => n.j !== i)
      .sort((a, b) => a.d - b.d)
      .slice(0, 3)
    near.forEach(({ j }) => {
      if (j > i) edges.push([i, j])
    })
  })
  return { nodes, edges }
}

const networks = [graph(18), graph(28), graph(40), graph(55)]

interface Root {
  x: number
  y: number
  ex: number
  ey: number
  bend: number
  age: number
  seed: number
  depth: number
}

const roots: Root[] = []
function branch(x: number, y: number, angle: number, length: number, depth: number, seed: number, age: number): void {
  if (!depth) return
  const bend = (hash(seed) - 0.5) * 0.45
  const ex = x + Math.cos(angle + bend) * length
  const ey = y + Math.sin(angle + bend) * length
  roots.push({ x, y, ex, ey, bend, age, seed, depth })
  branch(ex, ey, angle - 0.36 - hash(seed + 7) * 0.3, length * 0.74, depth - 1, seed + 23, age + length)
  branch(ex, ey, angle + 0.35 + hash(seed + 8) * 0.3, length * 0.73, depth - 1, seed + 51, age + length)
}
branch(0.02, 0.98, -0.74, 0.27, 6, 2, 0)
branch(0.98, 0.04, 2.38, 0.25, 6, 71, 0)

class Field {
  w: number
  h: number
  aspect: number
  detail: number
  light: Float32Array
  tint: Uint8Array
  glyph: Uint8Array

  constructor(columns: number, rows: number, aspect: number) {
    this.w = columns
    this.h = rows
    this.aspect = aspect
    this.detail = Math.min(3, Math.floor((columns * rows) / 8000))
    this.light = new Float32Array(columns * rows)
    this.tint = new Uint8Array(columns * rows)
    this.glyph = new Uint8Array(columns * rows)
  }

  clear(): void {
    this.light.fill(0)
    this.tint.fill(0)
    this.glyph.fill(0)
  }

  point(x: number, y: number, value: number, color = 0, glyph = 0): void {
    const px = x * this.w
    const py = y * this.h
    const ix = Math.floor(px)
    const iy = Math.floor(py)
    // Bilinear coverage makes travel smooth without making letters themselves swim.
    for (let dy = 0; dy < 2; dy++) {
      for (let dx = 0; dx < 2; dx++) {
        const xx = ix + dx
        const yy = iy + dy
        if (xx < 0 || yy < 0 || xx >= this.w || yy >= this.h) continue
        const v = value * (dx ? px - ix : 1 - (px - ix)) * (dy ? py - iy : 1 - (py - iy))
        const index = yy * this.w + xx
        if (v > this.light[index]!) {
          this.light[index] = v
          this.tint[index] = color
          this.glyph[index] = glyph
        }
      }
    }
  }

  halo(x: number, y: number, radius: number, value: number, color = 2): void {
    const px = x * this.w
    const py = y * this.h
    for (let j = -radius; j <= radius; j++) {
      for (let i = -radius; i <= radius; i++) {
        const xx = Math.floor(px + i)
        const yy = Math.floor(py + j)
        if (xx < 0 || yy < 0 || xx >= this.w || yy >= this.h) continue
        const v = value * Math.exp(-(i * i + j * j * 2.1) / (radius * radius * 0.45))
        const index = yy * this.w + xx
        if (v > this.light[index]!) {
          this.light[index] = v
          this.tint[index] = color
        }
      }
    }
  }

  stars(t: number, amount = 100): void {
    amount = Math.round(amount * Math.sqrt((this.w * this.h) / 7000))
    for (let n = 0; n < amount; n++) {
      const x = hash(n * 3 + 191)
      const y = hash(n * 3 + 712)
      this.point(x, y, 0.15 + 0.2 * Math.pow(0.5 + 0.5 * Math.sin(t * 0.15 + n), 3), n % 3, n % 7 === 0 ? 7 : 1)
    }
  }
}

function paint(field: Field, id: string, t: number): void {
  field.clear()
  const { w, h, aspect, light, tint, glyph } = field
  if (id === 'none') return
  if (id === 'synapses') {
    const network = networks[field.detail]!
    const nodes = network.nodes.map((p, n) => ({ x: p.x + 0.012 * Math.sin(t * 0.055 + n), y: p.y + 0.009 * Math.cos(t * 0.045 + n * 2) }))
    network.edges.forEach(([a, b], edge) => {
      const p = nodes[a]!
      const q = nodes[b]!
      const count = Math.ceil(Math.hypot((p.x - q.x) * w, (p.y - q.y) * h) * 2)
      const pulse = fract(t * 0.075 + hash(edge))
      for (let k = 0; k <= count; k++) {
        const f = k / count
        const signal = Math.exp(-Math.pow((f - pulse) * 16, 2))
        field.point(p.x + (q.x - p.x) * f, p.y + (q.y - p.y) * f, 0.38 + signal * 0.9, signal > 0.3 ? 2 : 0, Math.abs(p.x - q.x) > Math.abs(p.y - q.y) ? 5 : 4)
      }
    })
    nodes.forEach((p, n) => {
      field.halo(p.x, p.y, 3, 0.24 + 0.15 * Math.sin(t * 0.5 + n), 1)
      field.point(p.x, p.y, 1.6, 2, 9)
    })
    return
  }
  if (id === 'orrery') {
    field.stars(t, 110)
    const cx = 0.67
    const cy = 0.46
    const scale = Math.min(1.25, aspect)
    const ringCount = 5 + field.detail
    for (let ring = 0; ring < ringCount; ring++) {
      const radius = 0.105 + ring * (0.36 / (ringCount - 1))
      const tilt = -0.7 + ring * (1.2 / ringCount)
      const orbit = (a: number) => {
        const x = Math.cos(a) * radius
        const y = Math.sin(a) * radius * (0.27 + ring * 0.035)
        return { x: cx + (x * Math.cos(tilt) - y * Math.sin(tilt)) / scale, y: cy + x * Math.sin(tilt) + y * Math.cos(tilt) }
      }
      for (let k = 0; k < w * 7; k++) {
        const a = (k / (w * 7)) * TAU
        const p = orbit(a)
        field.point(p.x, p.y, 0.52 + 0.18 * Math.sin(a + ring), ring % 2, 1)
      }
      const a = t * (0.11 / (1 + ring * 0.4)) + ring * 1.95
      const p = orbit(a)
      field.halo(p.x, p.y, 3, 0.6, 1)
      field.point(p.x, p.y, 2, 2, 9)
    }
    field.halo(cx, cy, 6, 0.7, 1)
    field.point(cx, cy, 2, 2, 7)
    return
  }
  if (id === 'mycelium') {
    roots.forEach((p, n) => {
      const count = Math.ceil(Math.hypot((p.ex - p.x) * w, (p.ey - p.y) * h) * 2)
      for (let k = 0; k <= count; k++) {
        const f = k / count
        const bend = Math.sin(f * Math.PI) * p.bend * 0.1
        const pulse = Math.pow(0.5 + 0.5 * Math.sin((p.age + f * 0.18) * 18 - t * 0.65), 12)
        field.point(p.x + (p.ex - p.x) * f + bend, p.y + (p.ey - p.y) * f, 0.36 + pulse * 0.65, pulse > 0.3 ? 2 : 0, p.ex > p.x ? 6 : 8)
      }
      if (p.depth < 3) field.halo(p.ex, p.ey, 2, 0.13 + 0.11 * Math.sin(t * 0.2 + n), 1)
    })
    return
  }
  if (id === 'rain') {
    for (let drop = 0; drop < w * 0.55; drop++) {
      const seed = hash(drop + 33)
      const x = hash(drop * 4 + 22)
      const y = fract(hash(drop + 95) + t * (0.018 + seed * 0.025))
      const length = 3 + seed * 9
      for (let k = 0; k < length; k++) {
        field.point(fract(x + y * 0.055), y - k / h, Math.pow(1 - k / length, 1.6) * (0.3 + seed * 0.7), seed > 0.7 ? 2 : 0, k === 0 ? 2 : 4)
      }
      if (y > 0.87) {
        const r = (y - 0.87) * 0.22
        for (let n = 0; n < 50; n++) {
          const a = (n / 50) * TAU
          field.point(x + 0.05 + (Math.cos(a) * r) / aspect, 0.9 + Math.sin(a) * r * 0.22, (1 - (y - 0.87) / 0.13) * 0.22, 1, 1)
        }
      }
    }
    return
  }
  for (let j = 0; j < h; j++) {
    for (let i = 0; i < w; i++) {
      const u = i / w
      const v = j / h
      const index = j * w + i
      let value = 0
      let color = 0
      let shape = 0
      if (id === 'galaxy') {
        const dx = (u - 0.67) * Math.min(aspect, 1.9)
        const dy = v - 0.43
        const x = dx * 0.94 + dy * 0.34
        const y = (-dx * 0.34 + dy * 0.94) / 0.57
        const r = Math.hypot(x, y)
        const a = Math.atan2(y, x)
        const arm = Math.pow(0.5 + 0.5 * Math.cos(3 * a - 6.8 * Math.log(r + 0.055) - t * 0.065), 9)
        const dust = 0.5 + 0.5 * noise(x * 42 + t * 0.018, y * 42)
        value = 0.63 * arm * Math.exp(-r * 2.9) * dust + 0.65 * Math.exp(-r * 12) + 0.055 * Math.exp(-r * 2)
        color = r < 0.16 ? 2 : arm > 0.6 ? 0 : 1
        if (hash(index + 841) > 0.982) {
          value += 0.25 * arm
          shape = 7
        }
      } else if (id === 'aurora') {
        for (let curtain = 0; curtain < 3; curtain++) {
          const line = 0.2 + curtain * 0.14 + 0.085 * Math.sin(u * 7 + t * 0.065 + curtain) + 0.04 * Math.sin(u * 18 - t * 0.045 + curtain * 3)
          const d = v - line
          const beam = 0.56 + 0.44 * Math.sin(u * 72 + Math.sin(u * 11 + t * 0.1) * 3 + curtain)
          const veil = Math.exp((-d * d) / (d > 0 ? 0.0008 : 0.013)) * (0.22 + 0.45 * beam)
          if (veil > value) {
            value = veil
            color = curtain === 1 ? 0 : d > -0.018 ? 2 : 1
          }
        }
        value *= 0.55 + 0.45 * Math.sin(u * Math.PI)
        shape = value > 0.32 ? 4 : 2
      } else if (id === 'tides') {
        const x = u * Math.min(aspect, 2)
        const y = v
        const elevation =
          y * 7 + 0.7 * Math.sin(x * 6 + t * 0.07) + 0.42 * Math.sin(x * 12 - y * 4 - t * 0.08) + 0.25 * Math.cos(y * 10 + x * 3 + t * 0.04)
        const ridge = Math.pow(0.5 + 0.5 * Math.cos(elevation * 5), 18)
        value = ridge * (0.24 + 0.26 * noise(x * 5 - t * 0.03, y * 7)) + 0.028
        color = ridge > 0.75 ? 1 : 0
        shape = ridge > 0.65 ? 10 : 1
      } else if (id === 'nebula') {
        const x = u * Math.min(aspect, 2) * 3
        const y = v * 3
        const warp = noise(x + t * 0.018, y - t * 0.012)
        const cloud = noise(x + warp * 1.3, y + t * 0.024) * 0.57 + noise(x * 2.1 - t * 0.021, y * 2.1) * 0.28 + noise(x * 4.4, y * 4.4 + t * 0.016) * 0.15
        value = Math.pow(clamp((cloud - 0.28) * 1.9), 1.4) * 0.85
        const cavity = Math.exp(-Math.pow((u - 0.45) * 2, 2) - Math.pow((v - 0.52) * 2, 2))
        value *= 1 - cavity * 0.55
        color = cloud > 0.62 ? 2 : warp > 0.5 ? 1 : 0
      }
      light[index] = value
      tint[index] = color
      glyph[index] = shape
    }
  }
  if (['galaxy', 'aurora', 'nebula'].includes(id)) field.stars(t, id === 'aurora' ? 45 : 100)
}

const characters = ' .:,|-/*\\o~+#@'

function spotlightColor(hex: string): string {
  const rgb = [1, 3, 5].map(start => parseInt(hex.slice(start, start + 2), 16))
  const mid = (Math.max(...rgb) + Math.min(...rgb)) / 2
  return 'rgb(' + rgb.map(c => Math.round(clamp((c - mid) * 1.7 + mid * 1.18, 0, 255))).join(',') + ')'
}

export interface Spotlight {
  x: number
  y: number
  radius: number
}

export interface DrawOptions {
  opacity?: number
  spotlight?: Spotlight
}

export interface SceneRenderer {
  resize(width: number, height: number): void
  draw(scene: string, time: number, customColor?: string, options?: DrawOptions): void
}

export function sceneRenderer(canvas: HTMLCanvasElement, thumbnail = false): SceneRenderer {
  const ctx = canvas.getContext('2d', { alpha: true })!
  let field: Field | undefined
  let atlas: HTMLCanvasElement | undefined
  let cellW = 0
  let cellH = 0
  let pixelRatio = 1
  let paletteKey = ''
  let lastWidth = 0
  let lastHeight = 0
  let paintedScene: string | undefined
  let paintedTime: number | undefined

  function resize(width: number, height: number): void {
    if (width === lastWidth && height === lastHeight) return
    lastWidth = width
    lastHeight = height
    // Constant-size glyphs add real detail as the viewport grows. Only ultrawide/4K
    // displays hit the 42k-cell budget; device pixel ratio affects sharpness, not density.
    cellW = thumbnail ? Math.max(4, width / 60) : Math.max(9, Math.sqrt((width * height) / (42000 * 1.65)))
    cellH = cellW * 1.65
    pixelRatio = Math.min(devicePixelRatio || 1, 1.5)
    canvas.width = Math.round(width * pixelRatio)
    canvas.height = Math.round(height * pixelRatio)
    field = new Field(Math.ceil(width / cellW), Math.ceil(height / cellH), width / height)
    if (!thumbnail) {
      canvas.dataset.columns = String(field.w)
      canvas.dataset.rows = String(field.h)
      canvas.dataset.detail = String(field.detail)
    }
    paletteKey = ''
    paintedScene = undefined
  }

  function draw(scene: string, time: number, customColor = '', { opacity = 1, spotlight }: DrawOptions = {}): void {
    if (!field || !ctx) return
    const definition = asciiScenes.find(s => s.id === scene) ?? asciiScenes[0]!
    const basePalette = customColor ? [customColor, customColor, '#edf1ef'] : definition.colors
    const palette = [...basePalette, ...basePalette.map(spotlightColor)]
    const key = palette.join(',')
    const aw = Math.ceil(cellW * pixelRatio)
    const ah = Math.ceil(cellH * pixelRatio)
    if (key !== paletteKey) {
      paletteKey = key
      atlas = document.createElement('canvas')
      atlas.width = aw * characters.length
      atlas.height = ah * 48
      const ac = atlas.getContext('2d')!
      ac.font = `${Math.round(cellH * 0.83 * pixelRatio)}px ui-monospace, Consolas, monospace`
      ac.textBaseline = 'middle'
      ac.textAlign = 'center'
      for (let color = 0; color < 6; color++) {
        for (let level = 0; level < 8; level++) {
          ac.fillStyle = palette[color]!
          ac.globalAlpha = (level + 1) / 8
          for (let c = 1; c < characters.length; c++) ac.fillText(characters[c]!, c * aw + aw / 2, (color * 8 + level) * ah + ah / 2)
        }
      }
    }
    // Pointer-only updates reuse the frozen scene, including when ambient motion is paused.
    if (paintedScene !== scene || paintedTime !== time) {
      paint(field, scene, time)
      paintedScene = scene
      paintedTime = time
    }
    ctx.clearRect(0, 0, canvas.width, canvas.height)
    const radiusSquared = spotlight ? spotlight.radius * spotlight.radius : 1
    for (let i = 0; i < field.light.length; i++) {
      const light = clamp(field.light[i]! * 1.65)
      if (light < 0.055) continue
      const level = Math.min(7, Math.floor(light * 8))
      const glyph = field.glyph[i] || (light < 0.14 ? 1 : light < 0.3 ? 2 : light < 0.48 ? 3 : light < 0.7 ? 7 : light < 0.88 ? 11 : 12)
      const x = (i % field.w) * cellW
      const y = Math.floor(i / field.w) * cellH
      ctx.globalAlpha = opacity
      ctx.drawImage(atlas!, glyph * aw, (field.tint[i]! * 8 + level) * ah, aw, ah, x * pixelRatio, y * pixelRatio, aw, ah)
      if (spotlight) {
        const dx = x + cellW / 2 - spotlight.x
        const dy = y + cellH / 2 - spotlight.y
        const falloff = clamp(1 - (dx * dx + dy * dy) / radiusSquared)
        if (falloff > 0) {
          ctx.globalAlpha = falloff * falloff * (3 - 2 * falloff) * 0.7 * Math.min(1, opacity / 0.25)
          ctx.drawImage(atlas!, glyph * aw, ((field.tint[i]! + 3) * 8 + Math.min(7, level + 2)) * ah, aw, ah, x * pixelRatio, y * pixelRatio, aw, ah)
        }
      }
    }
    ctx.globalAlpha = 1
  }

  return { resize, draw }
}
