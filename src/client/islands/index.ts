import { createApp, type Component, type ComponentPublicInstance } from 'vue'
import { appearance } from './appearance'
import { article } from './article'
import { composer } from './composer'
import { continueReading } from './continue'
import { copy } from './copy'
import { filter } from './filter'
import { fragment } from './fragment'
import { history } from './history'
import { live } from './live'
import { lobby } from './lobby'
import { preview } from './preview'
import { quote } from './quote'
import { reading } from './reading'
import { search } from './search'
import { seen } from './seen'
import { submitting } from './submitting'
import { suggest } from './suggest'
import { unseen } from './unseen'

// Every island a page can name in data-island, by that name.
export const islands: Record<string, Component> = { appearance, article, composer, continue: continueReading, copy, filter, fragment, history, live, lobby, preview, quote, reading, search, seen, submitting, suggest, unseen }

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
