import { defineComponent, type PropType } from 'vue'
import { draftKey, mintTid, readDraft, reconcile, send, writeDraft, type Draft, type Settled } from '../composer'

// What the composer does beyond the page, replaceable where it is tested: where a confirmed Post is shown, and how long
// a send or a check waits for its answer before its outcome counts as uncertain.
export const composing = {
  show: (address: string) => window.location.assign(address),
  timeout: 20_000,
}

type Phase = 'ready' | 'sending' | 'checking' | 'missing' | 'uncertain' | 'refused' | 'posted'

// The composer: a form that posts a reply or starts a Topic, and that never loses its words, never posts twice and
// never posts by itself. Its words are kept as a draft, by participant and scope — the Topic, the Post a reply answers,
// or the Tangent — from every keystroke until the server confirms the Post, and come back when the person does. Only
// the person's own submit sends it, as JSON to the form's action with the form's token and its TID. A refusal is said
// inline above the fields. An outcome that is not known — no answer, a timeout, a failure on the way, a reload while
// it was on its way — keeps the TID, and a GET of the Post's address for that TID (locate + TID) says whether it was
// posted before the form offers to send again; a form whose Post has no address by TID alone, a new Topic's, offers
// it at once, since the same TID posts once. A reply form gives each Post of the page a Reply action that points the
// form at it. Without script the form posts and is sent on as the server answers.
export const composer = defineComponent({
  name: 'ComposerIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
    participant: { type: String, required: true },
    scope: { type: String, required: true },
    locate: { type: String, default: null },
    noun: { type: String, default: 'post' },
  },
  setup(props) {
    const form = props.host
    const named = <T extends Element>(name: string) => {
      const element = form.elements.namedItem(name)
      return element instanceof Element ? (element as T) : null
    }
    const id = named<HTMLInputElement>('id')
    const body = named<HTMLTextAreaElement>('body')
    const title = named<HTMLInputElement>('title')
    const parent = named<HTMLInputElement>('parent')
    const button = form.querySelector<HTMLButtonElement>('button[type="submit"]')
    const label = button?.textContent ?? ''
    const replyingTo = form.querySelector<HTMLElement>('[data-reply-to]')
    if (!id || !body || !button) return () => null

    const status = document.createElement('p')
    status.className = 'composer-status'
    status.hidden = true
    form.prepend(status)

    let phase: Phase = 'ready'
    const scope = () => (parent?.value ? `post:${parent.value}` : props.scope)
    const key = () => draftKey(props.participant, scope())
    const current = (sent: boolean): Draft => ({
      id: id.value,
      body: body.value,
      title: title ? title.value : undefined,
      parent: parent?.value || undefined,
      sent,
    })

    function show(next: Phase, message = '', action = label) {
      phase = next
      form.dataset.composer = next
      const busy = next === 'sending' || next === 'checking' || next === 'posted'
      form.setAttribute('aria-busy', String(busy))
      for (const field of [body, title]) if (field) field.readOnly = busy
      button!.textContent = next === 'sending' ? (button!.dataset.busyLabel ?? label) : next === 'checking' ? 'Checking…' : action
      if (busy) button!.setAttribute('aria-disabled', 'true')
      else button!.removeAttribute('aria-disabled')
      status.textContent = message
      status.hidden = message === ''
      status.dataset.tone = next === 'refused' || next === 'missing' || next === 'uncertain' ? 'failure' : 'note'
      status.setAttribute('role', status.dataset.tone === 'failure' ? 'alert' : 'status')
    }

    function apply(draft: Draft) {
      id!.value = draft.id
      body!.value = draft.body
      if (title) title.value = draft.title ?? ''
    }

    function settle(settled: Settled) {
      switch (settled.kind) {
        case 'posted':
          writeDraft(key(), null)
          show('posted', `Posted. Opening your ${props.noun}…`)
          composing.show(settled.address)
          return
        case 'refused':
          if (settled.conflict) {
            id!.value = mintTid()
            writeDraft(key(), current(false))
            show('refused', 'This form was already sent with other words, so nothing was posted. Send it again to post these words as well.')
          } else {
            writeDraft(key(), current(false))
            show('refused', settled.message)
          }
          return
        case 'missing':
          writeDraft(key(), current(false))
          show('missing', `Your ${props.noun} wasn’t posted. Send it again: it carries the same id, so it can post only once.`, 'Send again')
          return
        case 'uncertain':
          if (props.locate)
            show('uncertain', `The Tangent Space didn’t answer, so it isn’t known whether your ${props.noun} was posted. Your words are kept; check before sending again.`, 'Check again')
          else
            show('uncertain', `The Tangent Space didn’t answer, so it isn’t known whether your ${props.noun} was posted. Sending again is safe: it carries the same id, so it can post only once.`, 'Send again')
          return
      }
    }

    async function check() {
      if (!props.locate) return settle({ kind: 'uncertain' })
      show('checking', `Checking whether your ${props.noun} was posted…`)
      settle(await reconcile(props.locate + encodeURIComponent(id!.value), composing.timeout))
    }

    async function submit() {
      // The draft says it was sent before the request leaves, so a reload while it is on its way checks first.
      writeDraft(key(), current(true))
      const fields = new URLSearchParams()
      for (const [name, value] of new FormData(form)) if (typeof value === 'string') fields.append(name, value)
      show('sending')
      const settled = await send(form.action, fields, composing.timeout)
      if (settled.kind === 'uncertain' && props.locate) return check()
      settle(settled)
    }

    form.addEventListener('submit', (event) => {
      event.preventDefault()
      if (phase === 'sending' || phase === 'checking' || phase === 'posted') return
      if (phase === 'uncertain' && props.locate) void check()
      else void submit()
    })

    form.addEventListener('input', () => {
      if (phase === 'sending' || phase === 'checking' || phase === 'posted') return
      writeDraft(key(), current(phase === 'uncertain'))
    })

    // Points a reply form at a Post, or back at the Topic: the draft for that scope comes back, or an empty one with
    // a TID of its own.
    function replyTo(post: string | null, author: string) {
      if (!parent || !replyingTo || (phase !== 'ready' && phase !== 'refused')) return
      parent.value = post ?? ''
      const link = replyingTo.querySelector<HTMLAnchorElement>('[data-reply-to-post]')
      if (link && post) {
        link.textContent = author
        link.href = `#${post}`
      }
      replyingTo.hidden = post === null
      const draft = readDraft(key())
      apply(draft ?? { id: mintTid(), body: '', sent: false })
      show('ready')
      body!.focus()
    }

    if (parent && replyingTo) {
      replyingTo.querySelector('[data-reply-to-topic]')?.addEventListener('click', (event) => {
        event.preventDefault()
        replyTo(null, '')
      })
      for (const post of form.ownerDocument.querySelectorAll<HTMLElement>('article.post[id]:not(.post-root):not(.is-removed)')) {
        const main = post.querySelector('.post-main')
        if (!main) continue
        let actions = main.querySelector('.post-actions')
        if (!actions) {
          actions = document.createElement('div')
          actions.className = 'post-actions'
          main.append(actions)
        }
        const action = document.createElement('button')
        action.type = 'button'
        action.className = 'post-action composer-reply-to'
        action.textContent = 'Reply'
        const author = post.querySelector('.post-author')?.textContent?.trim() || 'this post'
        action.addEventListener('click', () => replyTo(post.id, author))
        actions.append(action)
      }
    }

    // A page restored from the back-forward cache after its Post was confirmed starts a new one.
    window.addEventListener('pageshow', (event) => {
      if (!event.persisted || phase !== 'posted') return
      apply({ id: mintTid(), body: '', sent: false })
      show('ready')
    })

    const draft = readDraft(key())
    if (draft) apply(draft)
    show('ready')
    if (draft?.sent) void check()
    return () => null
  },
})
