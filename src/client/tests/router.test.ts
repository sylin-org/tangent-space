import { describe, expect, it } from 'vitest'
import { parseWorkspaceRoute } from '../src/stores/router'

// The workspace path grammar, ported from spa-router.js.

describe('parseWorkspaceRoute', () => {
  it('reads the home route from the root', () => {
    expect(parseWorkspaceRoute('/')).toEqual({ kind: 'home' })
    expect(parseWorkspaceRoute('')).toEqual({ kind: 'home' })
  })

  it('reads the directories and settings pages', () => {
    expect(parseWorkspaceRoute('/tangents')).toEqual({ kind: 'tangents' })
    expect(parseWorkspaceRoute('/tangents/')).toEqual({ kind: 'tangents' })
    expect(parseWorkspaceRoute('/settings')).toEqual({ kind: 'settings' })
  })

  it('reads a participant page', () => {
    expect(parseWorkspaceRoute('/u/did:plc:example')).toEqual({ kind: 'participant', identifier: 'did:plc:example' })
  })

  it('reads the Tangent routes', () => {
    expect(parseWorkspaceRoute('/t/small-hours/settings')).toEqual({ kind: 'tangent-settings', tangent: 'small-hours' })
    expect(parseWorkspaceRoute('/t/small-hours/topics')).toEqual({ kind: 'topics', tangent: 'small-hours' })
    expect(parseWorkspaceRoute('/t/small-hours/topics/first-thoughts')).toEqual({
      kind: 'topic',
      tangent: 'small-hours',
      topic: 'first-thoughts',
    })
    expect(parseWorkspaceRoute('/t/small-hours/topics/first-thoughts/settings')).toEqual({
      kind: 'topic-settings',
      tangent: 'small-hours',
      topic: 'first-thoughts',
    })
  })

  it('reads a permalink to a Post', () => {
    expect(parseWorkspaceRoute('/t/small-hours/01JABCDEF')).toEqual({ kind: 'post', tangent: 'small-hours', post: '01JABCDEF' })
  })

  it('decodes encoded segments and refuses unsafe ones', () => {
    expect(parseWorkspaceRoute('/u/a%20b')).toEqual({ kind: 'participant', identifier: 'a b' })
    expect(parseWorkspaceRoute('/u/a%2Fb')).toBeNull()
    expect(parseWorkspaceRoute('/u/a%5C..%2Fb')).toBeNull()
    expect(parseWorkspaceRoute('/u/')).toBeNull()
  })

  it('refuses paths the workspace does not own', () => {
    expect(parseWorkspaceRoute('/auth/logout')).toBeNull()
    expect(parseWorkspaceRoute('/t/')).toBeNull()
    expect(parseWorkspaceRoute('/t/small-hours/elsewhere/here')).toBeNull()
    expect(parseWorkspaceRoute('/sign-in/')).toBeNull()
    expect(parseWorkspaceRoute('/onboarding/')).toBeNull()
  })
})
