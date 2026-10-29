import { defineConfig } from 'vitest/config'

// One classic script and one stylesheet with fixed names; fonts keep content hashes.
// Relative URLs let the Tangent Space answer under a path of its base URL.
export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    cssCodeSplit: false,
    cssMinify: false,
    rollupOptions: {
      input: 'main.ts',
      output: {
        format: 'iife',
        entryFileNames: 'tangent.js',
        assetFileNames: (asset) =>
          asset.names.some((name) => name.endsWith('.css')) ? 'tangent.css' : 'fonts/[name]-[hash][extname]',
      },
    },
  },
  test: {
    include: ['tests/**/*.test.ts'],
    // The reference stylesheet is read as text by the token parity test.
    css: { include: [/reference\/tokens\.css/] },
  },
})
