import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import { Markdown } from '@tiptap/markdown'
import { Placeholder } from '@tiptap/extensions'
import type { Extensions } from '@tiptap/core'

export const articleExtensions = (door = '/'): Extensions => [
  StarterKit.configure({
    strike: false,
    underline: false,
    link: { openOnClick: false, autolink: false, protocols: ['at'], isAllowedUri: (url) => /^(https?:\/\/|\/|#|at:\/\/)/i.test(url ?? '') },
  }),
  Image.extend({
    renderHTML({ HTMLAttributes }) {
      const src = String(HTMLAttributes.src ?? '')
      return ['img', { ...HTMLAttributes, src: src.startsWith('/media/') ? door + src.slice(1) : src, referrerpolicy: 'no-referrer' }]
    },
  }).configure({ inline: true, allowBase64: false }),
  Placeholder.configure({ placeholder: 'Start writing…' }),
  Markdown.configure({ markedOptions: { gfm: true, breaks: true } }),
]
