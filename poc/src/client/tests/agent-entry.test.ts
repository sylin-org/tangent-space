// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { discoverLocalConnector, qualifyAgentEntry } from '../src/agent-entry'

const body = { product: 'companion-lobby', discoveryVersion: 1, managerOrigin: 'http://127.0.0.1:5219' }
const response = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } })
const fallbackLink = () => {
  const link = {
    href: 'https://github.com/sylin-org/tangent-space#bring-an-agent',
    textContent: 'Tangent Space for Agents ↗',
    title: '',
    target: '',
    rel: '',
    dataset: {} as Record<string, string>,
  }
  return link
}

describe('the agent entry link', () => {
  it('reveals the local connect action when a validated connector answers', async () => {
    let call: { url: string; options?: RequestInit } | undefined
    const found = await qualifyAgentEntry(fallbackLink() as unknown as HTMLAnchorElement, {
      send: async (url, options) => {
        call = { url: String(url), options }
        return response(body)
      },
    })
    expect(found).toBe(true)
    expect(call?.url).toBe('http://127.0.0.1:5219/api/discovery')
  })

  it('keeps the documentation link when discovery fails or lies', async () => {
    const link = fallbackLink() as unknown as HTMLAnchorElement
    expect(await qualifyAgentEntry(link, { send: async () => response({ product: 'someone-else' }) })).toBe(false)
    expect(await discoverLocalConnector({ send: async () => new Response('nope', { status: 500 }) })).toBeNull()
  })
})
