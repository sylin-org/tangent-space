// The page entry: Tangent's components' stylesheet and the islands. Pages load it as a module, which runs once the
// document is parsed, so every island's element is already in the document.
import './tangent.css'
import { mountIslands } from './islands'

mountIslands(document)
