// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { Editor } from '@tiptap/core'
import { articleExtensions } from '../article/extensions'
import { canEditMarkdownRichly } from '../article/markdown-roundtrip'

function richRoundtrip(source: string): string {
  const editor = new Editor({ content: source, contentType: 'markdown', extensions: articleExtensions() })
  const serialized = editor.getMarkdown()
  editor.destroy()
  return serialized
}

const samples: Record<string, string> = {
  article: '## A turning point\n\nA **bold** idea with [a source](https://example.com).\n\n#### A smaller heading\n\n![Alt text](https://example.com/photo.jpg)\n\n> A longer quotation.\n\n- First point\n- Second point',
  breaks: 'One line\nand the next\n\nanother paragraph',
  mention: 'Thanks to [@ines.bsky.social](at://did:plc:ines) for this.',
  media: 'Before.\n\n![A kiln at dusk](/media/019a2b3c-4d5e-7f60-8a9b-0c1d2e3f4a5b)\n\n*The kiln at dusk.*\n\nAfter.',
  ordered: '3. three\n4. four',
  nested: '- a\n  - b\n- c',
  code: 'Some `code` here.\n\n```js\nlet a = 1\n```',
  rule: 'Above\n\n---\n\nBelow',
  escape: String.raw`A literal \*star\* and 1\. not a list and \# not a heading`,
}

for (const [name, source] of Object.entries(samples))
  it('round trip: ' + name, () => {
    const out = richRoundtrip(source)
    expect(canEditMarkdownRichly(source, out)).toBe(true)
  })


it('keeps unsupported table and raw HTML in source mode', () => {
  const table = '## Results\n\n| Name | Value |\n| --- | --- |\n| One | Two |'
  const rawHtml = 'A paragraph.\n\n<div class="special">Embedded callout</div>'
  expect(canEditMarkdownRichly(table, richRoundtrip(table))).toBe(false)
  expect(canEditMarkdownRichly(rawHtml, richRoundtrip(rawHtml))).toBe(false)
})
