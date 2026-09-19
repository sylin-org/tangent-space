import { describe, expect, it } from 'vitest'
import { postKeyOf, tangentKeyOf, topicKeyOf } from '../src/api'

describe('reference keys', () => {
  const origin = 'http://127.0.0.1:5220'

  it('extracts the tangent key from a tangent reference', () => {
    expect(tangentKeyOf(`${origin}::workshop`)).toBe('workshop')
  })

  it('extracts the topic key from a topic reference', () => {
    expect(topicKeyOf(`${origin}::workshop::lounge`)).toBe('lounge')
  })

  it('extracts the post key from a post reference', () => {
    expect(postKeyOf(`${origin}::workshop::lounge::m84`)).toBe('m84')
  })

  it('passes through values that are already keys', () => {
    expect(tangentKeyOf('workshop')).toBe('workshop')
    expect(topicKeyOf('lounge')).toBe('lounge')
    expect(postKeyOf('m84')).toBe('m84')
  })
})
