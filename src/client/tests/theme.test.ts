import { build } from 'vite'
import { beforeAll, expect, test } from 'vitest'
import { storageKey } from '../appearance'

// The theme script as the server serves it: a classic script, self-contained, that puts the reader's choice on <html>
// when it runs, which in <head> is before the first paint.

let script = ''

beforeAll(async () => {
  const result = await build({ configFile: 'vite.theme.config.ts', logLevel: 'silent', build: { write: false } })
  const { output } = result as { output: { fileName: string; code?: string }[] }
  script = output.find((file) => file.fileName === 'theme.js')?.code ?? ''
})

// Runs the script over a bare <html> and a browser's storage, and answers what the page's first paint would read.
function firstPaint(storage: { getItem: (key: string) => string | null }): string | undefined {
  const root = { dataset: {} as Record<string, string> }
  new Function('document', 'localStorage', script)({ documentElement: root }, storage)
  return root.dataset.theme
}

const saved = (theme: string | null) => ({ getItem: (key: string) => (key === storageKey ? theme : null) })

test('the script is classic: it imports nothing', () => {
  expect(script).not.toBe('')
  expect(script).not.toMatch(/\b(import|export)\b/)
})

test('the first paint takes the theme the reader chose', () => {
  expect(firstPaint(saved('dark'))).toBe('dark')
  expect(firstPaint(saved('light'))).toBe('light')
})

test('with no choice, or one it does not know, the device decides', () => {
  expect(firstPaint(saved(null))).toBeUndefined()
  expect(firstPaint(saved('system'))).toBeUndefined()
  expect(firstPaint(saved('sepia'))).toBeUndefined()
})

test('a browser that keeps nothing still paints, and the device decides', () => {
  const blocked = {
    getItem: () => {
      throw new Error('storage is off')
    },
  }
  expect(firstPaint(blocked)).toBeUndefined()
})
