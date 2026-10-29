import { defineConfig } from 'vite'

// The theme script: a classic script, self-contained, added beside the page entry's output.
export default defineConfig({
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: false,
    rollupOptions: {
      input: 'theme.ts',
      output: { format: 'iife', entryFileNames: 'theme.js' },
    },
  },
})
