// @vitest-environment jsdom
import { afterEach, expect, test } from 'vitest'
import { mountIslands } from '../islands'
import { applied, badge, logEvent } from '../islands/unseen'
import type { LiveEvent } from '../live'

// The masthead's count of unseen notifications: the server renders it, hidden at zero, with the reader, the newest
// sequence its count read and whether the applications that wait count among them; the page's stream raises it by one
// for each row past that sequence that targets the reader and that someone else did, and, where applications count, for
// each application, and by nothing else.

const reader = 'did:plc:ada'

function render(count: number, atLeast: boolean, through: number, applications = false): HTMLElement {
  const props = JSON.stringify({ reader, count, atLeast, through, applications }).replaceAll('"', '&quot;')
  document.body.innerHTML =
    `<a class="masthead-unseen" href="https://tangent.example/notifications"${count === 0 ? ' hidden' : ''} aria-label="${count} new notifications" ` +
    `data-island="unseen" data-props="${props}"><span class="masthead-unseen-count">${badge(count, atLeast)}</span></a>`
  mountIslands(document)
  return document.querySelector<HTMLElement>('.masthead-unseen')!
}

function delivered(...rows: Partial<LiveEvent>[]): void {
  const events = rows.map((row) => ({ kind: 'post.created', at: '2026-10-30T12:00:00Z', ...row }) as LiveEvent)
  document.dispatchEvent(new CustomEvent(logEvent, { detail: events }))
}

afterEach(() => {
  document.body.innerHTML = ''
})

test('a reply to the reader delivered past the count shows the count and raises it', () => {
  const host = render(0, false, 40)
  expect(host.hidden).toBe(true)

  delivered({ seq: 41, actor: 'did:plc:bea', target: reader })

  expect(host.hidden).toBe(false)
  expect(host.querySelector('.masthead-unseen-count')!.textContent).toBe('1')
  expect(host.getAttribute('aria-label')).toBe('1 new notification')

  delivered({ seq: 42, kind: 'mention', actor: 'did:plc:mo', target: reader }, { seq: 43, actor: 'did:plc:mo', target: null })
  expect(host.querySelector('.masthead-unseen-count')!.textContent).toBe('2')
})

test('rows the count already read, the reader’s own and those for someone else raise nothing', () => {
  const host = render(3, false, 40)

  delivered(
    { seq: 39, actor: 'did:plc:bea', target: reader },
    { seq: 40, actor: 'did:plc:bea', target: reader },
    { seq: 41, actor: reader, target: reader },
    { seq: 42, actor: 'did:plc:bea', target: 'did:plc:mo' },
  )
  delivered({ seq: 42, actor: 'did:plc:bea', target: reader })

  expect(host.querySelector('.masthead-unseen-count')!.textContent).toBe('3')
})

test('a count past its bound reads at least, and stays at 99+ as it rises', () => {
  const host = render(100, true, 500)
  expect(host.querySelector('.masthead-unseen-count')!.textContent).toBe('99+')

  delivered({ seq: 501, actor: 'did:plc:bea', target: reader })

  expect(host.querySelector('.masthead-unseen-count')!.textContent).toBe('99+')
  expect(host.getAttribute('aria-label')).toBe('At least 101 new notifications')
  expect(badge(40, true)).toBe('40+')
  expect(badge(7, false)).toBe('7')
})

test('an application raises the count of whoever decides it, and no one else’s', () => {
  const application = { kind: 'standing.changed', actor: 'did:plc:wren', target: 'did:plc:wren', standing: applied }
  const decider = render(0, false, 40, true)

  delivered({ seq: 41, ...application }, { seq: 42, kind: 'standing.changed', actor: 'did:plc:wren', target: 'did:plc:wren', standing: 'Admitted' })

  expect(decider.hidden).toBe(false)
  expect(decider.querySelector('.masthead-unseen-count')!.textContent).toBe('1')

  const member = render(0, false, 40)
  delivered({ seq: 41, ...application })
  expect(member.hidden).toBe(true)
})
