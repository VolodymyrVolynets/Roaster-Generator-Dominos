import assert from 'node:assert/strict'
import test from 'node:test'
import { draftsEqual, setRequirement, toDrafts, toListRequest, visibleTypes } from './documentSettings.js'

const types = [{ id: 'a', isActive: true }, { id: 'b', isActive: true }, { id: 'c', isActive: false }]

test('toDrafts indexes conditions by category and type', () => {
  assert.deepEqual(
    toDrafts([{ category: 'CarOwn', items: [{ documentTypeId: 'a', condition: 'Stamp2' }] }, { category: 'InStore', items: [] }]),
    { CarOwn: { a: 'Stamp2' }, InStore: {} },
  )
})

test('setRequirement adds, changes and removes without mutating', () => {
  const draft = { a: 'Always' }
  assert.deepEqual(setRequirement(draft, 'b', 'Stamp2'), { a: 'Always', b: 'Stamp2' })
  assert.deepEqual(setRequirement(draft, 'a', 'Stamp2'), { a: 'Stamp2' })
  assert.deepEqual(setRequirement(draft, 'a', null), {})
  assert.deepEqual(draft, { a: 'Always' })
})

test('draftsEqual ignores key order and notices changes', () => {
  assert.equal(draftsEqual({ a: 'Always', b: 'Stamp2' }, { b: 'Stamp2', a: 'Always' }), true)
  assert.equal(draftsEqual({ a: 'Always' }, { a: 'Stamp2' }), false)
  assert.equal(draftsEqual({ a: 'Always' }, {}), false)
  assert.equal(draftsEqual(undefined, {}), true)
})

test('toListRequest follows catalogue order', () => {
  assert.deepEqual(toListRequest({ b: 'Always', a: 'Stamp2' }, types), {
    items: [{ documentTypeId: 'a', condition: 'Stamp2' }, { documentTypeId: 'b', condition: 'Always' }],
  })
})

test('visibleTypes hides inactive types unless the list includes them', () => {
  assert.deepEqual(visibleTypes(types, {}).map((type) => type.id), ['a', 'b'])
  assert.deepEqual(visibleTypes(types, { c: 'Always' }).map((type) => type.id), ['a', 'b', 'c'])
})
