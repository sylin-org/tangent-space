// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createAtmosphere } from '../src/composables/atmosphere'
import type { ServerView } from '../src/stores/site'
import { httpRequest } from '../src/api'

vi.mock('../src/api', () => ({ httpRequest: vi.fn() }))
const server = { canManage: true, backgroundScene: 'none', backgroundIntensity: 35,
  backgroundMotion: false, backgroundMouseSpotlight: false } as ServerView

beforeEach(() => {
  sessionStorage.clear()
  vi.mocked(httpRequest).mockReset()
  vi.stubGlobal('requestAnimationFrame', vi.fn(() => 1))
  vi.stubGlobal('cancelAnimationFrame', vi.fn())
})
afterEach(() => { vi.unstubAllGlobals() })

describe('Atmosphere settings', () => {
  it('saves its staged values after another settings section closes the preview', async () => {
    vi.mocked(httpRequest).mockResolvedValue(new Response(JSON.stringify({ ...server, backgroundIntensity: 57 })))
    const editor = createAtmosphere()
    editor.configure(server, { participantRef: 'alice' })
    editor.open()
    editor.change({ intensity: 57 })
    editor.close()
    await editor.save()
    expect(JSON.parse(vi.mocked(httpRequest).mock.calls[0]![1]!.body as string).backgroundIntensity).toBe(57)
    expect(editor.dirty).toBe(false)
    expect(editor.settings.intensity).toBe(57)
  })
  it('restores a draft after the account loads before server permissions and isolates other accounts', () => {
    const first = createAtmosphere()
    first.configure(server, { participantRef: 'alice' })
    first.open()
    first.change({ intensity: 57 })
    first.close()
    expect(first.settings.intensity).toBe(35)
    expect(first.dirty).toBe(true)

    const reloaded = createAtmosphere()
    reloaded.configure(null, { participantRef: 'alice' })
    reloaded.configure(server, { participantRef: 'alice' })
    reloaded.open()
    expect(reloaded.settings.intensity).toBe(57)
    reloaded.configure(server, { participantRef: 'bob' })
    reloaded.open()
    expect(reloaded.settings.intensity).toBe(35)
    expect(reloaded.dirty).toBe(false)
    reloaded.configure(server, { participantRef: 'alice' })
    reloaded.open()
    expect(reloaded.settings.intensity).toBe(57)
    reloaded.resetPreview()
    expect(reloaded.dirty).toBe(false)
    expect(sessionStorage.getItem('tangent:atmosphere-draft:alice')).toBeNull()
  })

  it('ignores a failed save from the previous account', async () => {
    let reject!: (error: Error) => void
    vi.mocked(httpRequest).mockReturnValue(new Promise((_, fail) => { reject = fail }))
    const editor = createAtmosphere()
    editor.configure(server, { participantRef: 'alice' })
    editor.open()
    editor.change({ intensity: 57 })
    const pending = editor.save()
    editor.configure(server, { participantRef: 'bob' })
    editor.open()
    const message = editor.statusMessage
    reject(new Error('Alice save failed'))
    await pending
    expect(editor.statusMessage).toBe(message)
    expect(editor.statusError).toBe(false)
    expect(editor.saving).toBe(false)
    expect(editor.settings.intensity).toBe(35)
  })
})
