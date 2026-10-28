// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useRouter } from '../src/stores/router'

it('leaves anonymous conversation links to public documents while signed-in clicks stay in the workspace', () => {
  setActivePinia(createPinia())
  window.history.replaceState(null, '', '/')
  window.scrollTo = () => {}
  const router = useRouter()
  let signedIn = false
  router.bind(route => signedIn || (route.kind !== 'topic' && route.kind !== 'post'))
  const anchor = document.createElement('a')
  anchor.href = '/t/garden/topics/welcome'
  document.body.append(anchor)
  const intercepted: boolean[] = []
  const recordClick = (event: MouseEvent) => { intercepted.push(event.defaultPrevented); event.preventDefault() }
  window.addEventListener('click', recordClick)

  anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(intercepted.at(-1)).toBe(false)
  expect(router.route.kind).toBe('home')

  signedIn = true
  anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(intercepted.at(-1)).toBe(true)
  expect(router.route).toEqual({ kind: 'topic', tangent: 'garden', topic: 'welcome' })

  anchor.href = '/t/garden/post-1'
  signedIn = false
  anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(intercepted.at(-1)).toBe(false)
  expect(router.route.kind).toBe('topic')

  signedIn = true
  anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(intercepted.at(-1)).toBe(true)
  expect(router.route).toEqual({ kind: 'post', tangent: 'garden', post: 'post-1' })
  window.removeEventListener('click', recordClick)
  anchor.remove()
})
