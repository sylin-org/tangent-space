import { marked } from 'marked'

// Compare parsed meaning before letting a rich editor rewrite canonical Markdown.
// Unsupported structures stay editable in source mode, where their bytes survive.
type Token = Record<string, unknown>

function tokens(value: unknown): Token[] {
  return Array.isArray(value) ? value.filter((item): item is Token => !!item && typeof item === 'object') : []
}

function inlineSignature(value: unknown): unknown[] {
  const parts: Array<Record<string, unknown>> = []
  const walk = (token: Token, marks: string[]) => {
    const type = String(token.type ?? '')
    if (['em', 'strong', 'del', 'link'].includes(type)) {
      const mark = type === 'link' ? 'link:' + String(token.href ?? '') + ':' + String(token.title ?? '') : type
      for (const child of tokens(token.tokens)) walk(child, [...marks, mark])
      return
    }
    const sorted = [...new Set(marks)].sort()
    if (type === 'text' || type === 'escape') {
      if (tokens(token.tokens).length) for (const child of tokens(token.tokens)) walk(child, marks)
      else parts.push({ type: 'text', text: token.text ?? '', marks: sorted })
      return
    }
    if (type === 'codespan') { parts.push({ type: 'text', text: token.text ?? '', marks: [...new Set([...marks, 'code'])].sort() }); return }
    if (type === 'image') { parts.push({ type, alt: token.text ?? '', href: token.href ?? '', title: token.title ?? '', marks: sorted }); return }
    if (type === 'br') { parts.push({ type, marks: sorted }); return }
    if (type === 'html') { parts.push({ type, text: token.text ?? '', marks: sorted }); return }
    parts.push({ type, value: signature(token), marks: sorted })
  }
  for (const token of tokens(value)) walk(token, [])
  const merged: Array<Record<string, unknown>> = []
  for (const part of parts) {
    const prior = merged.at(-1)
    if (prior && prior.type === 'text' && part.type === 'text' && JSON.stringify(prior.marks) === JSON.stringify(part.marks)) prior.text = String(prior.text) + String(part.text)
    else merged.push(part)
  }
  return merged
}

function signature(token: Token): unknown {
  const type = String(token.type ?? '')
  if (type === 'space') return null
  const children = tokens(token.tokens).map(signature).filter(Boolean)
  const base: Record<string, unknown> = { type }
  if (typeof token.depth === 'number') base.depth = token.depth
  if (typeof token.ordered === 'boolean') base.ordered = token.ordered
  if (typeof token.start === 'number') base.start = token.start
  if (typeof token.href === 'string') base.href = token.href
  if (typeof token.title === 'string') base.title = token.title
  if (typeof token.lang === 'string') base.lang = token.lang
  if (children.length) base.children = ['paragraph', 'heading', 'text'].includes(type) ? inlineSignature(token.tokens) : children
  else if (typeof token.text === 'string') base.text = token.text
  if (type === 'list') base.items = tokens(token.items).map(signature)
  if (type === 'table') {
    base.header = token.header
    base.rows = token.rows
  }
  return base
}

export function markdownMeaning(markdown: string): string {
  return JSON.stringify(tokens(marked.lexer(markdown)).map(signature).filter(Boolean))
}

export function canEditMarkdownRichly(source: string, serialized: string): boolean {
  return markdownMeaning(source) === markdownMeaning(serialized)
}
