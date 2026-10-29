// The page entry: the one stylesheet and the islands. Pages load it as a module, which runs once the
// document is parsed, so every island's element is already in the document.
import './fonts.css'
import './tokens.css'
import './layout.css'
import './sign-in.css'
import { mountIslands } from './islands'

mountIslands(document)
