import { Node } from '@tiptap/core'
import { markdownOf, namedAt } from '../named'

// A name in the visual editor: one atomic piece showing the link's words, which deletes whole and is never half a name,
// and which writes back the same Markdown it was read from (named.ts). A link whose words are a sign and a name and whose
// target says who or what it means becomes one as the body loads; every other link stays a link.
export const NamedChip = Node.create({
  name: 'named',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes: () => ({
    words: { default: '' },
    target: { default: '' },
  }),

  parseHTML: () => [
    {
      tag: 'span[data-named]',
      getAttrs: (element) => ({ words: (element as HTMLElement).textContent ?? '', target: (element as HTMLElement).dataset.named ?? '' }),
    },
  ],

  renderHTML: ({ node }) => ['span', { class: 'named', 'data-named': node.attrs.target, contenteditable: 'false' }, node.attrs.words],

  renderText: ({ node }) => String(node.attrs.words),

  markdownTokenName: 'named',

  markdownTokenizer: {
    name: 'named',
    level: 'inline',
    start: (src: string) => src.search(/\[[@#]/u),
    tokenize: (src: string) => {
      const found = namedAt(src)
      return found ? { type: 'named', raw: found.raw, words: found.named.words, target: found.named.target } : undefined
    },
  },

  parseMarkdown: (token, helpers) => helpers.createNode('named', { words: token.words, target: token.target }),

  renderMarkdown: (node) => markdownOf({ words: String(node.attrs?.words ?? ''), target: String(node.attrs?.target ?? '') }),
})
