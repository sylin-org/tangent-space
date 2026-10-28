// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { preparePublicWorkspace } from '../src/public-workspace'

afterEach(() => { document.body.innerHTML = ''; document.head.innerHTML = '' })
describe('public workspace enhancement', () => {
  it('preserves readable HTML and canonical URL until both the workspace and styles are ready', async () => {
    window.history.replaceState({}, '', '/t/place/topics/story?before=older#article-heading-2')
    document.head.innerHTML = '<link data-public-style rel="stylesheet" href="/public.css"><link data-workspace-style rel="stylesheet" href="/assets/workspace.css" media="not all">'
    document.body.innerHTML = '<div id="public-document"><h1>A public story</h1><p>The readable article.</p></div><div id="app" hidden aria-hidden="true"><h1>A public story</h1></div>'
    preparePublicWorkspace()
    expect(document.getElementById('public-document')?.textContent).toContain('The readable article.')
    document.dispatchEvent(new Event('tangent:workspace-ready'))
    await flushPromises()
    expect(document.getElementById('public-document')).not.toBeNull()
    const style = document.querySelector<HTMLLinkElement>('[data-workspace-style]')!
    style.dispatchEvent(new Event('load'))
    await flushPromises()
    expect(document.getElementById('public-document')).toBeNull()
    expect(document.getElementById('app')?.hidden).toBe(false)
    expect(style.media).toBe('all')
    expect(document.querySelector('[data-public-style]')).toBeNull()
    expect(window.location.pathname + window.location.search + window.location.hash).toBe('/t/place/topics/story?before=older#article-heading-2')
  })
  it('keeps the public document when workspace styles cannot load', async () => {
    document.head.innerHTML = '<link data-workspace-style rel="stylesheet" href="/assets/workspace.css" media="not all">'
    document.body.innerHTML = '<div id="public-document">Readable fallback</div><div id="app" hidden></div>'
    preparePublicWorkspace()
    document.querySelector('[data-workspace-style]')!.dispatchEvent(new Event('error'))
    document.dispatchEvent(new Event('tangent:workspace-ready'))
    await flushPromises()
    expect(document.getElementById('public-document')?.textContent).toBe('Readable fallback')
    expect(document.getElementById('app')?.hidden).toBe(true)
  })
})
