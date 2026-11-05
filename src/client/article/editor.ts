import { Editor, Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet, type EditorView } from '@tiptap/pm/view'
import { draftKey, readDraft, writeDraft } from '../composer'
import { composerChanged } from '../islands/composer'
import { attachSuggestions, textareaSurface } from '../suggest'
import { bring, refusedHere, type Brought } from './bringing'
import { articleExtensions } from './extensions'
import { canEditMarkdownRichly } from './markdown-roundtrip'

// The article being written, in the place and the type it will be read in. The body's field stays the form's: the
// visual editor writes its Markdown back there on every change, so drafts, sending and everything the form does go on
// as they do without it, and the source view is that field itself. The visual view is offered only while it holds the
// Markdown's whole meaning; otherwise the field stands, and nothing it holds is touched. An edit made nowhere rewrites
// nothing: Markdown is written back only once the person changes something. What was loaded is where undo stops, and a
// body emptied in the visual view leaves its words in the draft. An image pasted, dropped or chosen appears where it
// was put at once, from the person's own file, while the writing goes on; it joins the words once the place has it, and
// the form and the view switch wait for it. Pasted text that carries a picture of itself is text.

export interface ArticleProps {
  host: HTMLElement
  door: string
  bring: string | null
  participant: string | null
  edit: string | null
  suggest?: string | null
  // A Post that is not an article — a reply, a discussion's opening, an edit of one — is written with the same editor and
  // fewer tools: no headings and no divider, in a Post's own type.
  post?: boolean
}

const noImages = 'Images come in where the place lets you write them.'
const viewWaits = 'Source opens once your image has come.'
const notSafe = (what: string) => `This ${what} uses formatting the visual editor cannot preserve. Edit its Markdown source to keep every part intact.`

// Where images on their way stand: a mark in the text that moves with it and is never part of the words.
const comingKey = new PluginKey<DecorationSet>('articleImagesComing')
type Coming = { add?: { id: number; at: number; preview: string }; remove?: number }

const ImagesComing = Extension.create({
  name: 'articleImagesComing',
  addProseMirrorPlugins: () => [
    new Plugin<DecorationSet>({
      key: comingKey,
      state: {
        init: () => DecorationSet.empty,
        apply(tr, set) {
          let next = set.map(tr.mapping, tr.doc)
          const coming = tr.getMeta(comingKey) as Coming | undefined
          if (coming?.add) {
            const element = document.createElement('span')
            element.className = 'article-image-coming'
            const image = document.createElement('img')
            image.src = coming.add.preview
            image.alt = ''
            element.append(image)
            next = next.add(tr.doc, [Decoration.widget(coming.add.at, element, { id: coming.add.id, side: -1 })])
          }
          if (coming?.remove !== undefined) next = next.remove(next.find(undefined, undefined, (spec) => spec.id === coming.remove))
          return next
        },
      },
      props: { decorations: (state) => comingKey.getState(state) },
    }),
  ],
})

function comingAt(view: EditorView, id: number): number | null {
  return comingKey.getState(view.state)?.find(undefined, undefined, (spec) => spec.id === id)[0]?.from ?? null
}

/** Puts an image in the words at `at`: in a paragraph of its own, as an article shows one, splitting the paragraph it
 *  lands in; alone in an empty paragraph it takes that one. It has no description until the person writes one in the
 *  image's panel: a file's name describes nothing. */
function placeImage(view: EditorView, at: number, src: string, id: number) {
  const { state } = view
  const { image, paragraph } = state.schema.nodes
  const node = image.create({ src })
  const tr = state.tr.setMeta(comingKey, { remove: id } satisfies Coming)
  const $at = state.doc.resolve(at)
  if ($at.parent.type !== paragraph) tr.insert(at, node)
  else if ($at.parent.content.size === 0) tr.insert(at, node)
  else {
    let boundary: number
    if ($at.parentOffset === 0) boundary = $at.before()
    else if ($at.parentOffset === $at.parent.content.size) boundary = $at.after()
    else {
      tr.split(at)
      boundary = at + 1
    }
    tr.insert(boundary, paragraph.create(null, node))
  }
  view.dispatch(tr)
}

function button(label: string, title: string, run: () => void): HTMLButtonElement {
  const element = document.createElement('button')
  element.type = 'button'
  element.className = 'article-tool'
  element.textContent = label
  element.title = title
  element.setAttribute('aria-label', title)
  element.addEventListener('click', run)
  return element
}

function field(label: string, type: string, placeholder: string): { wrap: HTMLLabelElement; input: HTMLInputElement } {
  const wrap = document.createElement('label')
  wrap.className = 'article-panel-field'
  wrap.textContent = label
  const input = document.createElement('input')
  input.type = type
  input.placeholder = placeholder
  wrap.append(input)
  return { wrap, input }
}

/** An address a link or an image may hold, as the server's own rule has it; null where it holds none of them. */
function safeAddress(value: string, image: boolean): string | null {
  const candidate = value.trim()
  if (!candidate) return null
  if (image && candidate.startsWith('/media/')) return candidate
  if (candidate.startsWith('//')) return null
  if (!image && (candidate.startsWith('/') || candidate.startsWith('#'))) return candidate
  try {
    return ['http:', 'https:'].includes(new URL(candidate).protocol) ? candidate : null
  } catch {
    return null
  }
}

export function writeArticle(props: ArticleProps): () => void {
  const host = props.host
  const form = host.closest('form') as HTMLFormElement
  const source = host.querySelector<HTMLTextAreaElement>('textarea')!
  const titleField = form.querySelector<HTMLTextAreaElement>('textarea[name="title"]')
  const coverField = form.elements.namedItem('cover') as HTMLInputElement | null
  const coverSlot = form.querySelector<HTMLElement>('[data-article-cover]')
  const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]')
  const minutes = form.querySelector<HTMLElement>('[data-article-minutes]')
  const uploads = new AbortController()
  const cleanups: (() => void)[] = [() => uploads.abort()]
  let coming = 0
  let comingInWords = 0
  let nextComing = 0
  let lastEmitted = source.value

  const label = host.querySelector('label')
  const named = label?.textContent?.trim() || 'Post'
  if (label) label.hidden = true
  source.required = false

  const status = document.createElement('p')
  status.className = 'composer-status article-status'
  status.setAttribute('role', 'status')
  status.hidden = true
  const say = (words: string) => {
    status.textContent = words
    status.hidden = words === ''
  }

  // An edit keeps its own draft, by the version it was made from.
  const editKey = props.edit && props.participant ? draftKey(props.participant, props.edit) : null
  const keepEdit = () => {
    if (!editKey) return
    const changed = source.value !== source.defaultValue || (titleField && titleField.value !== titleField.defaultValue) || (coverField && coverField.value !== coverField.defaultValue)
    writeDraft(editKey, changed ? { id: '2222222222222', body: source.value || source.dataset.heldWords || '', title: titleField?.value, cover: coverField?.value, sent: false } : null)
  }
  if (editKey) {
    // A draft is of one version: those of an earlier one stand for words that were saved or set aside.
    const under = editKey.slice(0, editKey.lastIndexOf(':') + 1)
    try {
      for (let index = window.localStorage.length - 1; index >= 0; index--) {
        const key = window.localStorage.key(index)
        if (key?.startsWith(under) && key !== editKey) window.localStorage.removeItem(key)
      }
    } catch {}
    const kept = readDraft(editKey)
    if (kept) {
      source.value = kept.body
      lastEmitted = kept.body
      if (titleField && kept.title !== undefined) titleField.value = kept.title
      if (coverField && kept.cover !== undefined) coverField.value = kept.cover
    }
    form.addEventListener('input', keepEdit)
    cleanups.push(() => form.removeEventListener('input', keepEdit))
    // Words set aside with Cancel stay set aside.
    const cancel = form.querySelector<HTMLAnchorElement>('.post-form-cancel')
    const setAside = () => writeDraft(editKey, null)
    cancel?.addEventListener('click', setAside)
    cleanups.push(() => cancel?.removeEventListener('click', setAside))
  }

  const countWords = () => {
    if (!minutes) return
    const words = source.value.trim().split(/\s+/).filter((word) => /[\p{L}\p{N}]/u.test(word)).length
    minutes.textContent = `${Math.max(1, Math.ceil(words / 230))} min read`
  }

  const editor = new Editor({
    extensions: [...articleExtensions(props.door), ImagesComing],
    editorProps: {
      attributes: {
        class: props.post ? 'post-body article-surface post-surface' : 'article-prose article-surface',
        role: 'textbox',
        'aria-multiline': 'true',
        'aria-label': props.post ? named : 'Article',
      },
      handlePaste(_view, event) {
        // Word and Excel put a picture of what was copied beside its text: the text is what was meant.
        const files = event.clipboardData?.getData('text/plain').trim() ? [] : imagesIn(event.clipboardData)
        if (files.length === 0) return false
        event.preventDefault()
        for (const file of files) void bringInto(file, editor.state.selection.from)
        return true
      },
      handleDrop(view, event) {
        const files = imagesIn((event as DragEvent).dataTransfer)
        if (files.length === 0) return false
        event.preventDefault()
        const at = view.posAtCoords({ left: event.clientX, top: event.clientY })?.pos ?? editor.state.selection.from
        for (const file of [...files].reverse()) void bringInto(file, at)
        return true
      },
    },
    onUpdate: ({ editor: current }) => {
      if (rich) writeBack(current.getMarkdown().trimEnd())
    },
    onTransaction: () => reflect(),
  })

  function writeBack(markdown: string) {
    if (markdown === lastEmitted) return
    if (markdown === '') source.dataset.heldWords = lastEmitted || source.dataset.heldWords || ''
    else delete source.dataset.heldWords
    lastEmitted = markdown
    source.value = markdown
    // The form's own listeners keep the draft from here.
    source.dispatchEvent(new Event('input', { bubbles: true }))
    countWords()
  }

  const imagesIn = (data: DataTransfer | null) => Array.from(data?.files ?? []).filter((file) => file.type.startsWith('image/'))

  function holdForm() {
    if (submit) submit.disabled = coming > 0
    markdown.setAttribute('aria-disabled', String(comingInWords > 0))
    if (comingInWords === 0 && status.textContent === viewWaits) say('')
  }

  async function bringInto(file: File, at: number) {
    const refused = refusedHere(file)
    if (refused) return say(refused)
    if (!props.bring) return say(noImages)
    const id = ++nextComing
    const preview = URL.createObjectURL(file)
    coming++
    comingInWords++
    holdForm()
    say('')
    editor.view.dispatch(editor.state.tr.setMeta(comingKey, { add: { id, at, preview } } satisfies Coming))
    try {
      const brought = await bring(props.bring, file, uploads.signal)
      if (editor.isDestroyed) return
      const place = comingAt(editor.view, id)
      if (place === null) return
      placeImage(editor.view, place, brought.reference, id)
    } catch (cause) {
      if (uploads.signal.aborted || editor.isDestroyed) return
      editor.view.dispatch(editor.state.tr.setMeta(comingKey, { remove: id } satisfies Coming))
      say((cause as Error).message)
    } finally {
      URL.revokeObjectURL(preview)
      coming--
      comingInWords--
      holdForm()
    }
  }

  // The toolbar, sticking to the top of the article while it is written.
  const chain = () => editor.chain().focus()
  const toolbar = document.createElement('div')
  toolbar.className = 'article-toolbar'
  toolbar.setAttribute('role', 'toolbar')
  toolbar.setAttribute('aria-label', 'Text formatting')
  type Tool = { element: HTMLButtonElement; active?: () => boolean }
  const tool = (name: string, title: string, run: () => void, active?: () => boolean): Tool => ({ element: button(name, title, run), active })
  const article = !props.post
  const tools: Tool[] = [
    tool('B', 'Bold', () => chain().toggleBold().run(), () => editor.isActive('bold')),
    tool('I', 'Italic', () => chain().toggleItalic().run(), () => editor.isActive('italic')),
    ...(article
      ? [
          tool('H2', 'Heading 2', () => chain().toggleHeading({ level: 2 }).run(), () => editor.isActive('heading', { level: 2 })),
          tool('H3', 'Heading 3', () => chain().toggleHeading({ level: 3 }).run(), () => editor.isActive('heading', { level: 3 })),
        ]
      : []),
    tool('• List', 'Bullet list', () => chain().toggleBulletList().run(), () => editor.isActive('bulletList')),
    tool('1. List', 'Numbered list', () => chain().toggleOrderedList().run(), () => editor.isActive('orderedList')),
    tool('❝', 'Quote', () => chain().toggleBlockquote().run(), () => editor.isActive('blockquote')),
    tool('Link', 'Insert link', () => openPanel(linkPanel), () => !linkPanel.hidden),
    tool('Image', 'Insert image', () => openImagePanel(), () => !imagePanel.hidden),
    ...(article ? [tool('—', 'Insert divider', () => chain().setHorizontalRule().run())] : []),
    tool('↶', 'Undo', () => chain().undo().run()),
    tool('↷', 'Redo', () => chain().redo().run()),
  ]
  const formatting = document.createElement('div')
  formatting.className = 'article-tools'
  formatting.append(...tools.map((entry) => entry.element))

  const views = document.createElement('div')
  views.className = 'article-views'
  views.setAttribute('role', 'group')
  views.setAttribute('aria-label', 'Editor view')
  const visual = button('Visual', 'Visual', () => {
    if (!rich) showRich()
  })
  const markdown = button('Source', 'Markdown source', () => {
    if (comingInWords > 0) say(viewWaits)
    else if (rich) showSource()
  })
  views.append(visual, markdown)
  toolbar.append(formatting, views)

  // The panels that open in the page under the toolbar: a link, and an image with its description.
  const panel = () => {
    const element = document.createElement('div')
    element.className = 'article-panel'
    element.hidden = true
    return element
  }
  const linkPanel = panel()
  const linkAddress = field('Link address', 'url', 'https://example.com')
  const linkWords = field('Words (optional)', 'text', 'Link text')
  const addLink = button('Insert link', 'Insert link', () => {
    const href = safeAddress(linkAddress.input.value, false)
    if (!href) return say('Enter a valid http or https link.')
    if (editor.state.selection.empty) {
      chain().insertContent({ type: 'text', text: linkWords.input.value.trim() || href, marks: [{ type: 'link', attrs: { href } }] }).run()
    } else {
      chain().setLink({ href }).run()
    }
    linkAddress.input.value = linkWords.input.value = ''
    say('')
    linkPanel.hidden = true
    reflect()
  })
  addLink.className = 'button'
  linkPanel.append(linkAddress.wrap, linkWords.wrap, addLink)
  // Enter in a panel's field answers the panel; it never sends the form around it.
  const enterAnswers = (element: HTMLElement, answer: () => void) =>
    element.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' || !(event.target instanceof HTMLInputElement)) return
      event.preventDefault()
      event.stopPropagation()
      answer()
    })
  enterAnswers(linkPanel, () => addLink.click())

  const imagePanel = panel()
  const chooser = document.createElement('input')
  chooser.type = 'file'
  chooser.accept = 'image/png,image/jpeg,image/webp'
  chooser.hidden = true
  chooser.setAttribute('aria-label', 'Choose an image to bring in')
  chooser.addEventListener('change', () => {
    const file = chooser.files?.[0]
    chooser.value = ''
    imagePanel.hidden = true
    reflect()
    if (file) void bringInto(file, editor.state.selection.from)
  })
  const chooseImage = button('Choose an image', 'Choose an image', () => chooser.click())
  chooseImage.className = 'button'
  const imageAddress = field('Image address', 'url', 'https://example.com/image.jpg')
  const imageWords = field('Description', 'text', 'Describe the image')
  const placeAddress = button('Insert', 'Insert the image', () => {
    const src = safeAddress(imageAddress.input.value, true)
    if (!src) return say('Enter a valid http or https image address.')
    chain().setImage({ src, alt: imageWords.input.value.trim() }).run()
    imageAddress.input.value = imageWords.input.value = ''
    say('')
    imagePanel.hidden = true
    reflect()
  })
  placeAddress.className = 'button'
  const removeImage = button('Remove image', 'Remove image', () => {
    chain().deleteSelection().run()
    imagePanel.hidden = true
    reflect()
  })
  removeImage.className = 'button'
  imagePanel.append(chooseImage, imageAddress.wrap, imageWords.wrap, placeAddress, removeImage, chooser)
  enterAnswers(imagePanel, () => placeAddress.click())

  function openPanel(which: HTMLElement) {
    const open = which.hidden
    linkPanel.hidden = imagePanel.hidden = true
    which.hidden = !open
    reflect()
  }

  function openImagePanel() {
    const selected = editor.isActive('image')
    if (selected) {
      const attributes = editor.getAttributes('image')
      imageAddress.input.value = String(attributes.src ?? '')
      imageWords.input.value = String(attributes.alt ?? '')
    }
    placeAddress.textContent = selected ? 'Update image' : 'Insert'
    removeImage.hidden = !selected
    openPanel(imagePanel)
  }

  const surface = document.createElement('div')
  surface.className = 'article-surface-wrap'
  surface.append(editor.view.dom)
  host.prepend(toolbar, status, linkPanel, imagePanel, surface)

  // The two views. The source view is the form's own field, in the page's mono type; the visual view opens only where
  // what the field holds survives it.
  let rich = false
  function reflect() {
    for (const { element, active } of tools) {
      element.setAttribute('aria-pressed', String(active?.() ?? false))
      element.disabled = !rich
    }
    visual.setAttribute('aria-pressed', String(rich))
    markdown.setAttribute('aria-pressed', String(!rich))
    host.classList.toggle('is-visual', rich)
  }

  function showRich() {
    // Loading is no step of the writing: undo stops at what was loaded.
    editor.chain().setMeta('addToHistory', false).setContent(source.value, { contentType: 'markdown', emitUpdate: false }).run()
    if (!canEditMarkdownRichly(source.value, editor.getMarkdown())) {
      say(notSafe(props.post ? 'post' : 'article'))
      showSource(true)
      return
    }
    say('')
    rich = true
    lastEmitted = source.value
    source.hidden = true
    surface.hidden = false
    reflect()
  }

  function showSource(keepWords = false) {
    if (!keepWords) say('')
    rich = false
    linkPanel.hidden = imagePanel.hidden = true
    surface.hidden = true
    source.hidden = false
    reflect()
  }

  // The composer changes the words when it brings a draft back, and whether they may change while they are on their way.
  const composerAnswered = () => {
    if (source.value) delete source.dataset.heldWords
    if (rich && source.value !== lastEmitted) showRich()
    else if (!rich) lastEmitted = source.value
    editor.setEditable(!source.readOnly)
    countWords()
    renderCover()
  }
  form.addEventListener(composerChanged, composerAnswered)
  cleanups.push(() => form.removeEventListener(composerChanged, composerAnswered))
  const sourceTyped = () => {
    if (!rich) delete source.dataset.heldWords
    countWords()
  }
  source.addEventListener('input', sourceTyped)
  cleanups.push(() => source.removeEventListener('input', sourceTyped))

  // The title reads as the title: it grows as it wraps, and Enter moves on to the words.
  if (titleField) {
    const fit = () => {
      titleField.style.height = 'auto'
      titleField.style.height = `${titleField.scrollHeight}px`
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Enter') return
      event.preventDefault()
      if (rich) {
        editor.commands.setTextSelection(1)
        editor.view.focus()
      } else {
        source.focus()
      }
    }
    titleField.addEventListener('input', fit)
    titleField.addEventListener('keydown', onKey)
    fit()
    document.fonts?.ready.then(fit).catch(() => {})
    window.addEventListener('resize', fit)
    cleanups.push(() => {
      titleField.removeEventListener('input', fit)
      titleField.removeEventListener('keydown', onKey)
      window.removeEventListener('resize', fit)
    })
  }

  // The cover, in its place: a quiet line that offers one, the image with its two answers once there is one. Dropping
  // an image on it works as choosing one does.
  const coverChooser = document.createElement('input')
  coverChooser.type = 'file'
  coverChooser.accept = 'image/png,image/jpeg,image/webp'
  coverChooser.hidden = true
  coverChooser.setAttribute('aria-label', 'Choose a cover')
  let coverPreview: string | null = null
  let coverSerial = 0

  function renderCover(busy = false) {
    if (!coverSlot || !coverField) return
    const current = coverField.value
    const address = coverPreview ?? (current ? props.door + 'media/' + current : null)
    const children: Node[] = [coverChooser]
    if (address) {
      const figure = document.createElement('figure')
      figure.className = 'article-figure' + (busy ? ' is-coming' : '')
      const image = document.createElement('img')
      image.src = address
      image.alt = ''
      figure.append(image)
      const actions = document.createElement('div')
      actions.className = 'article-cover-actions'
      actions.append(
        button('Replace', 'Replace the cover', () => coverChooser.click()),
        button('Remove', 'Remove the cover', () => setCover('')),
      )
      figure.append(actions)
      children.push(figure)
    } else if (props.bring) {
      const offer = button('Add a cover', 'Add a cover', () => coverChooser.click())
      offer.className = 'article-cover-offer'
      children.push(offer)
    }
    coverSlot.replaceChildren(...children)
  }

  function setCover(id: string) {
    coverField!.value = id
    coverField!.dispatchEvent(new Event('input', { bubbles: true }))
    renderCover()
  }

  async function bringCover(file: File) {
    const refused = refusedHere(file)
    if (refused) return say(refused)
    if (!props.bring) return say(noImages)
    const serial = ++coverSerial
    const preview = URL.createObjectURL(file)
    coverPreview = preview
    coming++
    holdForm()
    say('')
    renderCover(true)
    try {
      const brought: Brought = await bring(props.bring, file, uploads.signal)
      if (serial === coverSerial) {
        coverPreview = null
        setCover(brought.id)
      }
    } catch (cause) {
      if (uploads.signal.aborted) return
      if (serial === coverSerial) {
        coverPreview = null
        renderCover()
        say((cause as Error).message)
      }
    } finally {
      URL.revokeObjectURL(preview)
      coming--
      holdForm()
    }
  }

  coverChooser.addEventListener('change', () => {
    const file = coverChooser.files?.[0]
    coverChooser.value = ''
    if (file) void bringCover(file)
  })
  if (coverSlot) {
    const onDragOver = (event: DragEvent) => event.preventDefault()
    const onDrop = (event: DragEvent) => {
      const file = imagesIn(event.dataTransfer)[0]
      if (!file) return
      event.preventDefault()
      void bringCover(file)
    }
    coverSlot.addEventListener('dragover', onDragOver)
    coverSlot.addEventListener('drop', onDrop)
    cleanups.push(() => {
      coverSlot.removeEventListener('dragover', onDragOver)
      coverSlot.removeEventListener('drop', onDrop)
    })
  }

  // The shortcut a person already knows sends the form, as its button does.
  const onKeydown = (event: KeyboardEvent) => {
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) return
    event.preventDefault()
    event.stopPropagation()
    if (coming === 0) form.requestSubmit()
  }
  form.addEventListener('keydown', onKeydown, { capture: true })
  cleanups.push(() => form.removeEventListener('keydown', onKeydown, { capture: true }))

  // Typing @ or # offers who and where to name, in either view.
  if (props.suggest) {
    cleanups.push(attachSuggestions(textareaSurface(source), props.suggest))
    cleanups.push(attachSuggestions({
      element: editor.view.dom,
      keys: surface,
      before: () => {
        const { $from } = editor.state.selection
        return $from.parent.textBetween(0, $from.parentOffset, undefined, '￼')
      },
      pointBack: (characters) => {
        const point = editor.view.coordsAtPos(Math.max(0, editor.state.selection.from - characters))
        return { left: point.left, bottom: point.bottom }
      },
      replaceBack: (characters, words) => {
        const to = editor.state.selection.from
        editor.view.dispatch(editor.state.tr.insertText(words, to - characters, to))
      },
      writable: () => rich && editor.isEditable,
      onChange: (listener) => {
        editor.on('update', listener)
        editor.on('selectionUpdate', listener)
        return () => {
          editor.off('update', listener)
          editor.off('selectionUpdate', listener)
        }
      },
    }, props.suggest))
  }

  renderCover()
  countWords()
  showRich()
  host.dataset.articleReady = 'true'

  return () => {
    for (const cleanup of cleanups) cleanup()
    editor.destroy()
    if (label) label.hidden = false
    source.hidden = false
    source.required = true
  }
}
