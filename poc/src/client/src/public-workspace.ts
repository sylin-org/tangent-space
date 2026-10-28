/** Keep the readable public document until the same route is ready in the workspace. */
export function preparePublicWorkspace() {
  const fallback = document.getElementById('public-document')
  const root = document.getElementById('app')
  if (!fallback || !root) return
  const styles = [...document.querySelectorAll<HTMLLinkElement>('link[data-workspace-style]')]
  const stylesReady = Promise.all(styles.map(style => style.sheet ? Promise.resolve(true) : new Promise<boolean>(resolve => {
    style.addEventListener('load', () => resolve(true), { once: true })
    style.addEventListener('error', () => resolve(false), { once: true })
  })))
  document.addEventListener('tangent:workspace-ready', async () => {
    if ((await stylesReady).some(ready => !ready)) return
    for (const style of styles) style.media = 'all'
    for (const style of document.querySelectorAll('link[data-public-style]')) style.remove()
    fallback.remove()
    root.hidden = false
    root.removeAttribute('aria-hidden')
    document.dispatchEvent(new Event('tangent:workspace-revealed'))
  }, { once: true })
}
