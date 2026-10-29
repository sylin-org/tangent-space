import { defineComponent, type PropType } from 'vue'
import { draftKey, draftsUnder, mintTid, postScope, readDraft, reconcile, send, sentKey, writeDraft, type Draft, type Settled } from '../composer'

// What the composer does beyond the page, replaceable where it is tested: where a confirmed Post is shown, and how long
// a send or a check waits for its answer before its outcome counts as uncertain.
export const composing = {
  show: (address: string) => window.location.assign(address),
  timeout: 20_000,
}

type Phase = 'ready' | 'sending' | 'checking' | 'missing' | 'uncertain' | 'refused' | 'posted'

// The composer: a form that posts a reply or starts a Topic, and that never loses its words, never posts twice and
// never posts by itself. Its words are kept as a draft, by participant and scope — the Topic, a Post of it a reply
// answers, or the Tangent — from every keystroke until the server confirms the Post, and come back when the person
// does. Only the person's own submit sends it, as JSON to the form's action with the form's token and its TID. A
// refusal is said inline above the fields.
//
// A send's words are kept beside the scope's draft, by their TID, from the moment they leave until they are settled,
// so typing in another tab never writes over them. A send whose outcome is not known — no answer, a timeout, a failure
// on the way, a reload while it was on its way — keeps its TID and its words, which take no typing until a GET of the
// Post's address for that TID (locate + TID) says whether it was posted: found, they are cleared and the Post shown;
// not found, the words are the person's again, back in the scope's draft unless another tab's words are there, and
// sending them goes under the same TID. A conflict on that TID can only mean an earlier send of it landed, so it is
// checked the same way and never answered with a new TID. On arrival the composer checks every send of its scope, and
// of the scopes under it, that had no answer before it offers anything else. A reply form gives each Post of the page
// a Reply action that points the form at it. Without script the form posts and is sent on as the server answers.
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
    const scopeOf = (post: string | null | undefined) => (post ? postScope(props.scope, post) : props.scope)
    // The scope's draft, where typed words are kept.
    const key = () => draftKey(props.participant, scopeOf(parent?.value))
    // Where the words the form holds are kept: the scope's draft, or, once sent, beside it by their TID.
    let home = key()
    const current = (sent: boolean): Draft => ({
      id: id.value,
      body: body.value,
      title: title ? title.value : undefined,
      parent: parent?.value || undefined,
      sent,
    })
    // The words are the person's to change only once nothing is on its way and nothing sent is unaccounted for.
    const settled = () => phase === 'ready' || phase === 'refused' || phase === 'missing'

    function show(next: Phase, message = '', action = label) {
      phase = next
      form.dataset.composer = next
      const busy = next === 'sending' || next === 'checking' || next === 'posted'
      form.setAttribute('aria-busy', String(busy))
      for (const field of [body, title]) if (field) field.readOnly = !settled()
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

    // Points a reply form at a Post, or at the Topic: the parent field and the line that names whom it answers.
    function point(post: string | null, author?: string) {
      if (!parent) return
      parent.value = post ?? ''
      if (!replyingTo) return
      replyingTo.hidden = post === null
      const link = replyingTo.querySelector<HTMLAnchorElement>('[data-reply-to-post]')
      if (!link || !post) return
      const shown = form.ownerDocument.getElementById(post)
      link.textContent = author ?? shown?.querySelector('.post-author')?.textContent?.trim() ?? 'an earlier post'
      link.href = shown ? `#${post}` : `${props.locate ?? ''}${encodeURIComponent(post)}`
    }

    // Takes up words kept under a key: the form shows them, and keeps them there while they are unsent.
    function takeUp(entry: { key: string; draft: Draft }) {
      point(entry.draft.parent ?? null)
      apply(entry.draft)
      home = entry.key
    }

    // Unsent words go back to the scope's draft, unless another tab's words are there; then they stay beside it by
    // their TID, still found on arrival.
    function keep(draft: Draft) {
      const own = key()
      const there = home === own ? null : readDraft(own)
      if (home !== own && (!there || there.id === draft.id || (there.body === '' && !there.title))) {
        writeDraft(home, null)
        home = own
      }
      writeDraft(home, draft)
    }

    function settle(answer: Settled, conflicted = false) {
      switch (answer.kind) {
        case 'posted': {
          writeDraft(home, null)
          if (readDraft(key())?.id === id!.value) writeDraft(key(), null)
          home = key()
          show('posted', conflicted ? `Your ${props.noun} was already posted, with the words first sent. Opening it…` : `Posted. Opening your ${props.noun}…`)
          composing.show(answer.address)
          return
        }
        case 'refused':
          keep(current(false))
          show('refused', answer.message)
          return
        case 'missing':
          if (conflicted) return settle({ kind: 'uncertain' })
          keep(current(false))
          show('missing', `Your ${props.noun} wasn’t posted. Send it again: it carries the same id, so it can post only once.`, 'Send again')
          return
        case 'uncertain':
          writeDraft(home, current(true))
          if (props.locate)
            show('uncertain', `The Tangent Space didn’t answer, so it isn’t known whether your ${props.noun} was posted. Your words are kept as they were sent; check before anything else.`, 'Check again')
          else
            show('uncertain', `The Tangent Space didn’t answer, so it isn’t known whether your ${props.noun} was posted. Your words are kept as they were sent; sending them again posts them once.`, 'Send again')
          return
      }
    }

    async function check(conflicted = false) {
      if (!props.locate) return settle({ kind: 'uncertain' })
      show('checking', `Checking whether your ${props.noun} was posted…`)
      settle(await reconcile(props.locate + encodeURIComponent(id!.value), id!.value, composing.timeout), conflicted)
    }

    async function submit() {
      // The words leave for their own place, by TID, before the request does, so a reload while they are on their way
      // checks them first and no other tab's typing writes over them.
      const sending = current(true)
      const away = sentKey(key(), sending.id)
      writeDraft(away, sending)
      if (home !== away) {
        if (readDraft(home)?.id === sending.id) writeDraft(home, null)
        home = away
      }
      const fields = new URLSearchParams()
      for (const [name, value] of new FormData(form)) if (typeof value === 'string') fields.append(name, value)
      show('sending')
      const answer = await send(form.action, fields, composing.timeout)
      if (answer.kind === 'uncertain') return check()
      // This TID names other words: only an earlier send of it can have put them there.
      if (answer.kind === 'refused' && answer.conflict) return check(true)
      settle(answer)
    }

    // Every send of this scope and those under it that had no answer is checked before anything else is offered: one
    // found is shown; otherwise the form takes the first still not known, or else the first not posted, and a send
    // found not posted earlier is offered when nothing else is.
    async function recover() {
      const entries = draftsUnder(props.participant, props.scope)
      const sent = entries.filter((entry) => entry.draft.sent)
      const parked = entries.filter((entry) => !entry.draft.sent && entry.key.includes(':sent:'))
      if (sent.length === 0) {
        if (parked.length === 0) return
        takeUp(parked[0])
        return settle({ kind: 'missing' })
      }
      if (!props.locate) {
        takeUp(sent[0])
        return settle({ kind: 'uncertain' })
      }
      show('checking', `Checking whether your ${props.noun} was posted…`)
      const answers: { key: string; draft: Draft; answer: Settled }[] = []
      for (const entry of sent) {
        const answer = await reconcile(props.locate + encodeURIComponent(entry.draft.id), entry.draft.id, composing.timeout)
        if (answer.kind === 'posted') {
          takeUp(entry)
          return settle(answer)
        }
        answers.push({ ...entry, answer })
      }
      const shown = answers.find((entry) => entry.answer.kind === 'uncertain') ?? answers[0]
      for (const entry of answers)
        if (entry !== shown && entry.answer.kind === 'missing') writeDraft(entry.key, { ...entry.draft, sent: false })
      takeUp(shown)
      settle(shown.answer)
    }

    form.addEventListener('submit', (event) => {
      event.preventDefault()
      if (phase === 'sending' || phase === 'checking' || phase === 'posted') return
      if (phase === 'uncertain' && props.locate) void check()
      else void submit()
    })

    form.addEventListener('input', () => {
      if (settled()) writeDraft(home, current(false))
    })

    // Points a reply form at a Post, or back at the Topic: a send of that scope that had no answer comes back checked
    // first, then one found not posted, then the scope's draft, or an empty one with a TID of its own.
    function replyTo(post: string | null, author: string) {
      if (!parent || !settled()) return
      point(post, author)
      const here = draftsUnder(props.participant, scopeOf(post)).filter((entry) => (entry.draft.parent ?? null) === post)
      const unanswered = here.find((entry) => entry.draft.sent)
      const parkedHere = here.find((entry) => !entry.draft.sent && entry.key.includes(':sent:'))
      const draft = readDraft(key())
      home = key()
      apply(draft ?? { id: mintTid(), body: '', sent: false })
      show('ready')
      body!.focus()
      if (unanswered) {
        takeUp(unanswered)
        void check()
      } else if (parkedHere) {
        takeUp(parkedHere)
        settle({ kind: 'missing' })
      }
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
      home = key()
      apply({ id: mintTid(), body: '', sent: false })
      show('ready')
    })

    const draft = readDraft(home)
    if (draft && !draft.sent) apply(draft)
    show('ready')
    void recover()
    return () => null
  },
})
