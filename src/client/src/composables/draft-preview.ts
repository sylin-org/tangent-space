import { Marked, Renderer } from 'marked'

function escape(value: string): string { return value.replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!) }
function safeUrl(value: string): boolean {
  if (!value || /[\\\u0000-\u001f\u007f]/.test(value) || value.startsWith('//')) return false
  if (value.startsWith('/') || value.startsWith('#')) return true
  try { return ['http:', 'https:'].includes(new URL(value).protocol) } catch { return false }
}
const renderer = new Renderer()
renderer.html = ({ text }) => escape(text)
renderer.link = function ({ href, title, tokens }) { return `<a href="${escape(safeUrl(href) ? href : '#')}"${title ? ` title="${escape(title)}"` : ''}>${this.parser.parseInline(tokens)}</a>` }
renderer.image = ({ href, title, text }) => safeUrl(href) ? `<img src="${escape(href)}" alt="${escape(text)}"${title ? ` title="${escape(title)}"` : ''} loading="lazy" decoding="async" referrerpolicy="no-referrer">` : escape(text)
const markdown = new Marked({ renderer, async: false, gfm: true })

/** A draft preview only emits the same basic Markdown elements the reader supports. */
export function previewMarkdown(source: string): string {
  const html = markdown.parse(source) as string
  const document = new DOMParser().parseFromString(html, 'text/html')
  const allowed = new Set(['P', 'BR', 'HR', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'UL', 'OL', 'LI', 'PRE', 'CODE', 'EM', 'STRONG', 'DEL', 'A', 'IMG', 'TABLE', 'THEAD', 'TBODY', 'TR', 'TH', 'TD'])
  for (const element of Array.from(document.body.querySelectorAll('*'))) {
    if (!allowed.has(element.tagName)) { element.replaceWith(...Array.from(element.childNodes)); continue }
    for (const attribute of Array.from(element.attributes)) if (!['href', 'src', 'alt', 'title', 'loading', 'decoding', 'referrerpolicy'].includes(attribute.name)) element.removeAttribute(attribute.name)
  }
  for (const paragraph of Array.from(document.body.querySelectorAll('p'))) {
    const children = Array.from(paragraph.childNodes).filter(node => node.nodeType !== 3 || node.textContent?.trim())
    if (!(children[0] instanceof HTMLImageElement)) continue
    let caption: HTMLElement | null = null, following: Element | null = null
    if (children.length === 2 && children[1] instanceof HTMLElement && children[1].tagName === 'EM') caption = children[1]
    if (children.length === 1 && paragraph.nextElementSibling?.tagName === 'P' && paragraph.nextElementSibling.children.length === 1 && paragraph.nextElementSibling.firstElementChild?.tagName === 'EM') { following = paragraph.nextElementSibling; caption = following.firstElementChild as HTMLElement }
    if (!caption) continue
    const figure = document.createElement('figure'), figcaption = document.createElement('figcaption')
    figure.append(children[0]); figcaption.append(caption); figure.append(figcaption); paragraph.replaceWith(figure); following?.remove()
  }
  return document.body.innerHTML
}
