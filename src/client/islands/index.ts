import { createApp, type Component, type ComponentPublicInstance } from 'vue'
import { appearance } from './appearance'
import { fragment } from './fragment'
import { submitting } from './submitting'

// Every island a page can name in data-island, by that name.
export const islands: Record<string, Component> = { appearance, fragment, submitting }

export interface MountedIsland {
  name: string
  element: HTMLElement
  island: ComponentPublicInstance
}

// Mounts a Vue app for each element that names an island, with the props of its data-props JSON and the
// element itself as host. The app mounts beside the page rather than into the element, since Vue empties
// its mount point: the server's HTML stays the first render, and the island changes its host only when it
// acts. An element whose name is not in the registry is left as the server rendered it.
export function mountIslands(root: ParentNode): MountedIsland[] {
  const mounted: MountedIsland[] = []
  for (const element of root.querySelectorAll<HTMLElement>('[data-island]')) {
    const name = element.dataset.island ?? ''
    const component = islands[name]
    if (!component) continue
    const props = element.dataset.props ? (JSON.parse(element.dataset.props) as Record<string, unknown>) : {}
    const island = createApp(component, { ...props, host: element }).mount(document.createElement('div'))
    mounted.push({ name, element, island })
  }
  return mounted
}
