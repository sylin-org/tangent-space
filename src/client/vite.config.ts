import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The built workspace is hosted by the Tangent server as static assets under /app.
// In development, `npm run dev` proxies API calls to the running server on 5220.
export default defineConfig({
  plugins: [vue()],
  base: '/app/',
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5220',
    },
  },
  build: {
    outDir: '../server/web/wwwroot/app',
    emptyOutDir: true,
  },
})
