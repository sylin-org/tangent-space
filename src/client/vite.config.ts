import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { defineConfig, type Plugin } from 'vitest/config'

// The page entry, one module script, the order of the layers, layers.css, and the stylesheet of each of Tangent's layers with fixed names: base.css,
// tangent.css (shapes and tangent) and palette.css; and each bundled base, bases/<name>.css — a classless stylesheet
// with its adapter, or Concord — its own palette, bases/<name>.palette.css, and a vendored one's licence,
// bases/<name>/LICENSE; all written where the server serves its
// static files. Fonts keep content hashes. Relative URLs let the Tangent Space answer under a path of its base URL. The
// theme script is built after this one by vite.theme.config.ts into the same folder.
const bases = readdirSync('bases').filter((file) => file.endsWith('.css')).map((file) => file.slice(0, -'.css'.length))

// Each vendored base's licence travels beside it, as its package ships it; Concord is Tangent's own and has none.
const licences: Plugin = {
  name: 'licences',
  generateBundle() {
    for (const base of bases.filter((name) => !name.endsWith('.palette') && existsSync(`bases/${name}/LICENSE`)))
      this.emitFile({ type: 'asset', fileName: `bases/${base}/LICENSE`, source: readFileSync(`bases/${base}/LICENSE`) })
  },
}

export default defineConfig({
  base: './',
  plugins: [licences],
  // Vue's compile-time flags: islands use the Composition API, and nothing ships for devtools.
  define: {
    __VUE_OPTIONS_API__: 'false',
    __VUE_PROD_DEVTOOLS__: 'false',
    __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: 'false',
  },
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: true,
    cssCodeSplit: true,
    cssMinify: false,
    rollupOptions: {
      input: {
        tangent: 'tangent.ts',
        layers: 'layers.css',
        base: 'base.css',
        palette: 'palette.css',
        ...Object.fromEntries(bases.map((base) => [`bases/${base}`, `bases/${base}.css`])),
      },
      output: {
        format: 'es',
        entryFileNames: 'tangent.js',
        chunkFileNames: '[name]-[hash].js',
        // A stylesheet keeps the name of the file it is built from; the page entry's is tangent.css.
        assetFileNames: (asset) =>
          !asset.names.some((name) => name.endsWith('.css')) ? 'fonts/[name]-[hash][extname]'
            : asset.originalFileNames.find((file) => file.endsWith('.css')) ?? '[name].css',
      },
    },
  },
  test: {
    include: ['tests/**/*.test.ts'],
    // The reference stylesheet is read as text by the token parity test.
    css: { include: [/reference\/tokens\.css/] },
  },
})
