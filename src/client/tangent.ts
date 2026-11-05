// The page entry: Tangent's components' stylesheet and the islands. Pages load it as a module, which runs once the
// document is parsed, so every island's element is already in the document. A Post the live island places in the
// page later has its islands mounted as it arrives.
import './tangent.css'
import { mountIslands } from './islands'
import { postPlaced } from './islands/live'

mountIslands(document)
document.addEventListener(postPlaced, (event) => mountIslands((event as CustomEvent<HTMLElement>).detail))

// A Topic's page opened at one of its replies' own address opens at that reply; an address that names a place on the
// page itself is the browser's to follow.
if (!location.hash) document.querySelector<HTMLElement>('article.post.is-current:not(.post-root)')?.scrollIntoView({ block: 'start' })
