// The client entry: the one stylesheet and the one script every page loads.
// Pages load it as a classic script in <head>, so the saved appearance applies before first paint.
import './fonts.css'
import './tokens.css'
import './layout.css'
import { restoreTheme } from './appearance'

restoreTheme()
