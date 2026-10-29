// The theme entry: a classic script every page loads blocking in <head>, so the saved appearance is on
// <html> before the first paint. It holds nothing else, since all of it delays the first render.
import { restoreTheme } from './appearance'

restoreTheme()
