// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import TangentDirectory from '../src/components/TangentDirectory.vue'
import type { Directories } from '../src/composables/directories'

function directoriesFixture(tangents: unknown[], nextPage: number | null = null): Directories {
  return reactive({
    tangents,
    tangentsNextPage: nextPage,
    tangentsCanCreate: false,
    topics: [],
    topicsNextPage: null,
    loading: false,
    loadTangents: async () => {},
    createTangent: async () => {},
    loadTopics: async () => {},
    createTopic: async () => {},
    updateTangent: async () => {},
    setMemberRole: async () => {},
  }) as Directories
}

describe('the Tangent directory', () => {
  it('renders one card per Tangent with its settings gear for a manager', () => {
    const directories = directoriesFixture([
      { key: 'small-hours', name: 'Small Hours', description: 'A gentle place.', accent: '#e77a54', canManage: true },
      { key: 'open-floor', name: 'Open Floor', description: 'Anyone welcome.' },
    ])
    const wrapper = mount(TangentDirectory, { props: { directories, signedIn: true } })
    const cards = wrapper.findAll('.tangent-card')
    expect(cards.length).toBe(2)
    expect(cards[0]!.attributes('href')).toBe('/t/small-hours/topics')
    expect(cards[0]!.text()).toContain('Small Hours')
    expect(wrapper.findAll('.card-settings').length).toBe(1)
    expect(wrapper.find('#no-tangents').attributes('hidden')).toBeDefined()
  })

  it('offers the load-more control only when a next page exists', async () => {
    const directories = directoriesFixture([{ key: 'small-hours', name: 'Small Hours', description: '' }], 2)
    const wrapper = mount(TangentDirectory, { props: { directories, signedIn: true } })
    const more = wrapper.find('#more-tangents')
    expect(more.attributes('hidden')).toBeUndefined()
    await more.trigger('click')
    expect(directories.loadTangents).toBeTruthy()
  })

  it('tells an anonymous visitor what to do when the directory is empty', () => {
    const wrapper = mount(TangentDirectory, { props: { directories: directoriesFixture([]), signedIn: false } })
    expect(wrapper.find('#no-tangents').attributes('hidden')).toBeUndefined()
    expect(wrapper.find('#no-tangents').text()).toContain('Sign in to discover')
  })
})
