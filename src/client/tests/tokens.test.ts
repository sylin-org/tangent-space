import { build } from 'vite'
import { expect, test } from 'vitest'
import reference from './reference/tokens.css?raw'

// The design reference's tokens, kept verbatim in reference/tokens.css, are the yardstick:
// every token it declares, in every block (Paper, each accent, Ink, Ink under System),
// is declared in the built stylesheet in the same block with the same value.

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

async function builtStylesheet(): Promise<string> {
  const result = (await build({ logLevel: 'silent', build: { write: false } })) as {
    output: { fileName: string; source?: string | Uint8Array }[]
  }
  const stylesheet = result.output.find((file) => file.fileName === 'tangent.css')
  return String(stylesheet?.source ?? '')
}

test('the built stylesheet carries every reference token with its value', async () => {
  const expected = tokens(reference)
  const built = tokens(await builtStylesheet())
  expect(expected.size).toBe(184)
  const differences = [...expected]
    .filter(([key, value]) => built.get(key) !== value)
    .map(([key, value]) => `${key}: expected ${value}, built ${built.get(key) ?? 'nothing'}`)
  expect(differences).toEqual([])
})
