// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { effectScope, ref } from 'vue'
import { createProfiles } from '../src/composables/profiles'
import type { Activity } from '../src/composables/activity'

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('hands the unchanged profile selection to the shared connection when it becomes ready', async () => {
  vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  const close = vi.fn()
  const streams = vi.fn(function () { return { addEventListener: vi.fn(), close } })
  vi.stubGlobal('EventSource', streams)
  const setProfiles = vi.fn(() => false)
  const scope = effectScope()
  const profiles = scope.run(() => createProfiles({ setProfiles, profileEvents: ref([]) } as unknown as Activity))!
  profiles.wire()
  profiles.sync(['did:plc:reader'])
  expect(streams).toHaveBeenCalledTimes(1)
  setProfiles.mockReturnValue(true)
  window.dispatchEvent(new Event('tangent:live-ready'))
  await Promise.resolve()
  expect(close).toHaveBeenCalledTimes(1)
  expect(setProfiles).toHaveBeenLastCalledWith(['did:plc:reader'])
  profiles.sync(['did:plc:reader'])
  expect(streams).toHaveBeenCalledTimes(1)
  scope.stop()
})

it('releases a fallback socket while hidden and on disposal, reopening only when visible', () => {
  let hidden = false
  vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden)
  const close = vi.fn()
  const streams = vi.fn(function () { return { addEventListener: vi.fn(), close } })
  vi.stubGlobal('EventSource', streams)
  const scope = effectScope()
  const profiles = scope.run(() => createProfiles({ setProfiles: () => false, profileEvents: ref([]) } as unknown as Activity))!
  profiles.wire()
  profiles.sync(['did:plc:reader'])
  profiles.sync(['did:plc:reader'])
  expect(streams).toHaveBeenCalledTimes(1)
  hidden = true
  document.dispatchEvent(new Event('visibilitychange'))
  expect(close).toHaveBeenCalledTimes(1)
  hidden = false
  document.dispatchEvent(new Event('visibilitychange'))
  expect(streams).toHaveBeenCalledTimes(2)
  scope.stop()
  expect(close).toHaveBeenCalledTimes(2)
  document.dispatchEvent(new Event('visibilitychange'))
  expect(streams).toHaveBeenCalledTimes(2)
})
