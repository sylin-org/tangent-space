import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import Paragraph from '@tiptap/extension-paragraph'
import { Markdown } from '@tiptap/markdown'
import { Placeholder } from '@tiptap/extensions'
import type { Extensions } from '@tiptap/core'
import { NamedChip } from './chip'

export const articleExtensions = (door = '/'): Extensions => [
  StarterKit.configure({
    paragraph: false,
    strike: false,
    underline: false,
    link: { openOnClick: false, autolink: false, protocols: ['at'], isAllowedUri: (url) => /^(https?:\/\/|\/|#|at:\/\/)/i.test(url ?? '') },
  }),
  // An image is inline here, so a paragraph that holds one image alone stays a paragraph: two pictures written one under
  // the other stay two paragraphs, not one line.
  Paragraph.extend({
    parseMarkdown(token, helpers) {
      const tokens = token.tokens ?? []
      if (tokens.length === 1 && tokens[0].type === 'image') return helpers.createNode('paragraph', undefined, helpers.parseInline(tokens))
      return this.parent!(token, helpers)
    },
  }),
  Image.extend({
    renderHTML({ HTMLAttributes }) {
      const src = String(HTMLAttributes.src ?? '')
      return ['img', { ...HTMLAttributes, src: src.startsWith('/media/') ? door + src.slice(1) : src, referrerpolicy: 'no-referrer' }]
    },
  }).configure({ inline: true, allowBase64: false }),
  // A name a pick wrote, as one chip, ahead of the link it would otherwise be.
  NamedChip,
  Placeholder.configure({ placeholder: 'Start writing…' }),
  Markdown.configure({ markedOptions: { gfm: true, breaks: true } }),
]
