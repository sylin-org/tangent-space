import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { copyFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// The built workspace IS the server's workspace: the entry lands at wwwroot/index.html
// (PagesController serves it for every workspace route) and hashed assets at /assets.
// The server-rendered surfaces keep their own files in wwwroot, so the build never
// empties the output directory; `npm run clean` removes stale bundles instead.
export default defineConfig({
  plugins: [vue(), { name: 'public-design-assets', closeBundle() { for (const name of ['fonts', 'tokens']) copyFileSync(fileURLToPath(new URL('./src/assets/' + name + '.css', import.meta.url)), fileURLToPath(new URL('../server/web/wwwroot/design-' + name + '.css', import.meta.url))) } }],
  base: '/',
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5220',
      '/auth': 'http://127.0.0.1:5220',
      '/u': 'http://127.0.0.1:5220',
      '/t': 'http://127.0.0.1:5220',
      '/tangents': 'http://127.0.0.1:5220',
      '/settings': 'http://127.0.0.1:5220',
      '/onboarding': 'http://127.0.0.1:5220',
      '/tangent-art': 'http://127.0.0.1:5220',
      '/artwork': 'http://127.0.0.1:5220',
    },
  },
  build: {
    outDir: '../server/web/wwwroot',
    emptyOutDir: false,
  },
})
