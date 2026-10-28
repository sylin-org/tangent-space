// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { Editor } from '@tiptap/core'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import { Markdown } from '@tiptap/markdown'
import { canEditMarkdownRichly } from '../src/composables/markdown-roundtrip'

function richRoundtrip(source: string): string {
  const editor = new Editor({
    content: source, contentType: 'markdown',
    extensions: [StarterKit.configure({ link: { openOnClick: false, autolink: false } }), Image.configure({ inline: true }), Markdown],
  })
  const serialized = editor.getMarkdown()
  editor.destroy()
  return serialized
}

it('allows visual editing when article headings, links and images survive Markdown serialization', () => {
  const source = '## A turning point\n\nA **bold** idea with [a source](https://example.com).\n\n#### A smaller heading\n\n![Alt text](https://example.com/photo.jpg)\n\n> A longer quotation.\n\n- First point\n- Second point'
  expect(canEditMarkdownRichly(source, richRoundtrip(source))).toBe(true)
})

it('keeps unsupported table and raw HTML in source mode', () => {
  const table = '## Results\n\n| Name | Value |\n| --- | --- |\n| One | Two |'
  const rawHtml = 'A paragraph.\n\n<div class="special">Embedded callout</div>'
  expect(canEditMarkdownRichly(table, richRoundtrip(table))).toBe(false)
  expect(canEditMarkdownRichly(rawHtml, richRoundtrip(rawHtml))).toBe(false)
})
