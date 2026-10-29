import { build } from 'vite'
import { expect, test } from 'vitest'
import reference from './reference/tokens.css?raw'
import catalog from './reference/tokens.json'

// The design reference's tokens, kept verbatim in reference/tokens.css and reference/tokens.json, are
// the yardstick: every token either file defines, in every block (Paper, each accent, Ink, Ink under
// System), is declared in the built stylesheet in the same block with the same value.

const paper = ':root'
const ink = [':root[data-theme="dark"]', '@media (prefers-color-scheme: dark) :root:not([data-theme]), :root[data-theme="system"]']

// Reads every --ts-* declaration as "context | name" -> value, where the context is the selector,
// preceded by its at-rule when there is one.
function tokens(css: string): Map<string, string> {
  const found = new Map<string, string>()
  const walk = (body: string, context: string) => {
    let index = 0
    while (index < body.length) {
      const open = body.indexOf('{', index)
      if (open < 0) break
      let depth = 1
      let close = open + 1
      for (; depth > 0; close++) {
        if (body[close] === '{') depth++
        else if (body[close] === '}') depth--
      }
      const selector = body.slice(index, open).trim().replace(/\s+/g, ' ')
      const prelude = context + selector
      const inner = body.slice(open + 1, close - 1)
      if (selector.startsWith('@')) walk(inner, `${prelude} `)
      else
        for (const [, name, value] of inner.matchAll(/(--ts-[\w-]+)\s*:\s*([^;]+);/g))
          found.set(`${prelude} | ${name}`, value.trim().replace(/\s+/g, ' '))
      index = close
    }
  }
  walk(css.replace(/\/\*[\s\S]*?\*\//g, ''), '')
  return found
}

// The JSON's sections as the same "context | name" -> value entries. Notes, contrast ratios and the
// font source describe the tokens and are not tokens themselves.
function catalogTokens(): Map<string, string> {
  const found = new Map<string, string>()
  const put = (contexts: string | string[], name: string, value: string | number) => {
    for (const context of [contexts].flat()) found.set(`${context} | --ts-${name}`, String(value))
  }
  const kebab = (name: string) => name.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`)
  for (const [name, value] of Object.entries(catalog.font)) put(paper, `font-${name}`, value)
  for (const [name, value] of Object.entries(catalog.color.light)) put(paper, name, value)
  for (const [name, value] of Object.entries(catalog.color.dark)) put(ink, name, value)
  for (const [accent, tones] of Object.entries(catalog.spaceAccent.options)) {
    const context = accent === catalog.spaceAccent.default ? paper : `:root[data-accent="${accent}"]`
    put(context, 'accent-light', tones.light)
    put(context, 'accent-dark', tones.dark)
  }
  for (const [name, mix] of Object.entries(catalog.spaceAccent.derived)) {
    const share = (text: string) => `color-mix(in srgb, var(--ts-accent) ${/(\d+)%/.exec(text)![1]}%, transparent)`
    put(paper, name, share(mix.light))
    put(ink, name, share(mix.dark))
  }
  for (const [tone, themes] of Object.entries(catalog.tangentAccent.options))
    for (const [contexts, values] of [[paper, themes.light], [ink, themes.dark]] as const) {
      put(contexts, `tangent-${tone}`, values.edge)
      put(contexts, `tangent-${tone}-tint`, values.tint)
      put(contexts, `tangent-${tone}-ink`, values.ink)
    }
  for (const [role, themes] of Object.entries(catalog.roleColor.options)) {
    put(paper, `role-${role}`, themes.light)
    put(ink, `role-${role}`, themes.dark)
  }
  for (const type of catalog.type)
    put(paper, `type-${type.name}`, `${type.weight} ${type.size}px/${type.lineHeight} var(--ts-font-${type.family})`)
  for (const [name, size] of Object.entries(catalog.radius)) put(paper, `radius-${name}`, `${size}px`)
  for (const [name, size] of Object.entries(catalog.layout)) put(paper, name, `${size}px`)
  for (const [name, value] of Object.entries(catalog.shadow)) put(paper, `shadow-${kebab(name)}`, value)
  return found
}

let built: Promise<Map<string, string>> | undefined
function builtTokens(): Promise<Map<string, string>> {
  built ??= build({ logLevel: 'silent', build: { write: false } }).then((result) => {
    const { output } = result as { output: { fileName: string; source?: string | Uint8Array }[] }
    return tokens(String(output.find((file) => file.fileName === 'tangent.css')?.source ?? ''))
  })
  return built
}

function differences(expected: Map<string, string>, actual: Map<string, string>, same = (a: string, b: string) => a === b) {
  return [...expected]
    .filter(([key, value]) => !actual.has(key) || !same(value, actual.get(key)!))
    .map(([key, value]) => `${key}: expected ${value}, built ${actual.get(key) ?? 'nothing'}`)
}

test('the built stylesheet carries every token of the reference stylesheet with its value', async () => {
  const expected = tokens(reference)
  expect(expected.size).toBe(184)
  expect(differences(expected, await builtTokens())).toEqual([])
})

test('the built stylesheet carries every token of the reference catalog with its value', async () => {
  expect(Object.keys(catalog).sort()).toEqual(
    ['$meta', 'color', 'contrast', 'font', 'fontSource', 'layout', 'radius', 'roleColor', 'shadow', 'spaceAccent', 'tangentAccent', 'type'].sort(),
  )
  const expected = catalogTokens()
  expect(expected.size).toBe(184)
  // The catalog writes a line height of 1 where the stylesheet writes 1.0.
  const same = (a: string, b: string) => a === b.replace(/\/(\d+)\.0 /, '/$1 ')
  expect(differences(expected, await builtTokens(), same)).toEqual([])
})
