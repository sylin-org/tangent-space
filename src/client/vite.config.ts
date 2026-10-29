import { defineConfig } from 'vitest/config'

// The page entry: one module script and one stylesheet with fixed names, written where the server serves
// its static files; fonts keep content hashes. Relative URLs let the Tangent Space answer under a path of
// its base URL. The theme script is built after this one by vite.theme.config.ts into the same folder.
export default defineConfig({
  base: './',
  // Vue's compile-time flags: islands use the Composition API, and nothing ships for devtools.
  define: {
    __VUE_OPTIONS_API__: 'false',
    __VUE_PROD_DEVTOOLS__: 'false',
    __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: 'false',
  },
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: true,
    cssCodeSplit: false,
    cssMinify: false,
    rollupOptions: {
      input: 'tangent.ts',
      output: {
        format: 'es',
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
