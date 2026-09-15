import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../src/server/web/wwwroot/inline-confirm.js', import.meta.url), 'utf8');

function fixture() {
  let focused;
  class Node {
    constructor(tag) { this.tagName = tag; this.children = []; this.parent = null; this.listeners = new Map(); this.attributes = new Map(); this.textContent = ''; }
    get isConnected() { return this.parent !== null; }
    append(...nodes) { for (const node of nodes) { node.parent = this; this.children.push(node); } }
    replaceWith(node) { const siblings = this.parent.children; siblings[siblings.indexOf(this)] = node; node.parent = this.parent; this.parent = null; }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    addEventListener(name, handler) { this.listeners.set(name, [...(this.listeners.get(name) || []), handler]); }
    emit(name, values = {}) { for (const handler of this.listeners.get(name) || []) handler({ stopPropagation() {}, ...values }); }
    click() { this.emit('click'); }
    focus() { focused = this; }
  }
  const window = {};
  vm.runInNewContext(source, { window, document: { createElement: tag => new Node(tag) } });
  const menu = new Node('div'), trigger = new Node('button'); trigger.textContent = 'Delete post'; menu.append(trigger);
  const confirmed = [];
  const handle = window.TangentInlineConfirm.attach(trigger, { question: 'Delete this post?', confirmLabel: 'Delete', onConfirm: () => confirmed.push(true) });
  return { menu, trigger, confirmed, handle, focused: () => focused };
}

const button = (group, label) => group.children.find(child => child.textContent === label);

test('the control becomes its question in place and keeps focus there', () => {
  const f = fixture();
  f.trigger.click();
  const group = f.menu.children[0];
  assert.equal(group.attributes.get('role'), 'group');
  assert.equal(group.attributes.get('aria-label'), 'Delete this post?');
  assert.equal(f.focused(), button(group, 'Delete'));
  assert.deepEqual(f.confirmed, []);
});

test('keeping, escaping and closing the menu restore the control without acting', () => {
  const f = fixture();
  f.trigger.click(); button(f.menu.children[0], 'Keep').click();
  assert.equal(f.menu.children[0], f.trigger);
  assert.equal(f.focused(), f.trigger);
  f.trigger.click(); f.menu.children[0].emit('keydown', { key: 'Escape' });
  assert.equal(f.menu.children[0], f.trigger);
  f.trigger.click(); f.handle.reset();
  assert.equal(f.menu.children[0], f.trigger);
  assert.deepEqual(f.confirmed, []);
});

test('confirming acts once and restores the control', () => {
  const f = fixture();
  f.trigger.click(); button(f.menu.children[0], 'Delete').click();
  assert.deepEqual(f.confirmed, [true]);
  assert.equal(f.menu.children[0], f.trigger);
});
