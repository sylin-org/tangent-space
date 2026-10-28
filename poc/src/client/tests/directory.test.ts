// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import TangentDirectory from '../src/components/TangentDirectory.vue'
import TopicList from '../src/components/TopicList.vue'
import ArticleCollection from '../src/components/ArticleCollection.vue'
import SpaceHero from '../src/components/SpaceHero.vue'
import type { Directories } from '../src/composables/directories'
import { createDirectories } from '../src/composables/directories'
beforeEach(() => { setActivePinia(createPinia()) })

function directoriesFixture(tangents: unknown[], nextPage: number | null = null): Directories {
  return reactive({
    tangents,
    tangentsNextPage: nextPage,
    tangentsCanCreate: false,
    tangentsIncomplete: false,
    tangentsLoading: false,
    tangentsLoaded: true,
    tangentsError: '',
    topics: [],
    topicsNextPage: null,
    topicsLoading: false,
    topicsLoaded: true,
    topicsError: '',
    topicsIncomplete: false,
    loading: false,
    loadTangents: async () => {},
    createTangent: async () => {},
    loadTopics: async () => {},
    createTopic: async () => {},
    updateTangent: async () => {},
    setMemberRole: async () => {},
    resetForIdentity: () => {},
  }) as Directories
}

describe('the Tangent directory', () => {
  it('shows visible totals as lower bounds when incomplete, without inventing participant totals', () => {
    const directories = directoriesFixture([
      { key: 'visible', name: 'Visible', summary: { topics: { value: 4, atLeast: false }, posts: { value: 18, atLeast: true }, recentTopics: [] } },
    ], 2)
    const wrapper = mount(SpaceHero, { props: { name: 'Space', directories } })
    expect(wrapper.find('.space-hero-stats').text()).toContain('1+ Tangent')
    expect(wrapper.find('.space-hero-stats').text()).toContain('4+ topics')
    expect(wrapper.find('.space-hero-stats').text()).toContain('18+ posts')
    expect(wrapper.find('.space-hero-stats').text()).not.toContain('participants')
    expect(wrapper.find('.space-welcome-edit').exists()).toBe(false)
  })
  it('loads Topics from the versioned read route', async () => {
    const fetch = vi.fn(async (_path: RequestInfo | URL) => new Response(JSON.stringify({ topics: [{ key: 'first', title: 'First' }], nextPage: null })))
    vi.stubGlobal('fetch', fetch)
    const directories = createDirectories()
    await directories.loadTopics('home')
    expect(fetch.mock.calls[0]?.[0]).toBe('/api/v1/tangents/home/topics')
    expect(directories.topics.map(topic => topic.key)).toEqual(['first'])
    vi.unstubAllGlobals()
  })

  it('loads a requested collection page without replacing it with page one', async () => {
    const fetch = vi.fn(async (_path: RequestInfo | URL) => new Response(JSON.stringify({ topics: [{ key: 'page-two', title: 'Page two' }], nextPage: 3 })))
    vi.stubGlobal('fetch', fetch)
    const directories = createDirectories()
    await directories.loadTopics('essays', 2)
    expect(fetch.mock.calls[0]?.[0]).toBe('/api/v1/tangents/essays/topics?page=2')
    expect(directories.topics.map(topic => topic.key)).toEqual(['page-two'])
    vi.unstubAllGlobals()
  })

  it('rejects an old Tangent directory response after navigating to another Tangent', async () => {
    const replies = new Map<string, (value: Response) => void>()
    vi.stubGlobal('fetch', vi.fn((path: RequestInfo | URL) => new Promise<Response>(resolve => { replies.set(String(path), resolve) })))
    const directories = createDirectories()
    const oldLoad = directories.loadTopics('older')
    const currentLoad = directories.loadTopics('current')
    replies.get('/api/v1/tangents/current/topics')!(new Response(JSON.stringify({ topics: [{ key: 'current-topic', title: 'Current' }] })))
    await currentLoad
    replies.get('/api/v1/tangents/older/topics')!(new Response(JSON.stringify({ topics: [{ key: 'old-topic', title: 'Old' }] })))
    await oldLoad
    expect(directories.topics.map(topic => topic.key)).toEqual(['current-topic'])
    vi.unstubAllGlobals()
  })

  it('keeps community and exact Post destinations independent', () => {
    const directories = directoriesFixture([
      { key: 'small-hours', name: 'Small Hours', description: 'A gentle place.', accent: '#e77a54', canManage: true,
        summary: { topics: { value: 3, atLeast: false }, posts: { value: 12, atLeast: true }, recentTopics: [{ key: 'welcome', title: 'Welcome' }], incomplete: true,
          latestPost: { id: 'post-9', topicKey: 'welcome', topicTitle: 'Welcome', url: '/t/small-hours/post-9', excerpt: 'A reply worth reading.', createdAt: '2026-09-27T18:00:00Z', author: { id: 'a', displayName: 'Alex', handle: 'alex.example', avatar: null, classification: 'Human', profileUrl: '/u/alex' } } } },
      { key: 'open-floor', name: 'Open Floor', description: 'Anyone welcome.' },
    ])
    const wrapper = mount(TangentDirectory, { props: { directories, signedIn: true } })
    const shelves = wrapper.findAll('.tangent-shelf')
    expect(shelves).toHaveLength(2)
    expect(shelves[0]!.find('h2 a').attributes('href')).toBe('/t/small-hours/topics')
    expect(shelves[0]!.find('.topic-row h3 a').attributes('href')).toBe('/t/small-hours/topics/welcome')
    expect(shelves[0]!.text()).toContain('All topics')
    expect(shelves[1]!.text()).toContain('No topics yet.')
    expect(wrapper.find('#no-tangents').exists()).toBe(false)
  })

  it('shows a Topic start and latest Post as separate links with server counts', () => {
    const directories = directoriesFixture([])
    directories.topics = [{ key: 'welcome', title: 'Welcome', summary: { posts: { value: 4, atLeast: false }, participants: { value: 3, atLeast: false }, createdAt: '2026-09-27T17:00:00Z', starter: null,
      latestPost: { id: 'reply-3', topicKey: 'welcome', topicTitle: 'Welcome', url: '/t/small-hours/reply-3', excerpt: 'The latest reply.', createdAt: '2026-09-27T18:00:00Z', author: { id: 'a', displayName: 'Alex', handle: null, avatar: null, classification: 'Human', profileUrl: '/u/a' } } } }]
    const wrapper = mount(TopicList, { props: { directories, tangentKey: 'small-hours', signedIn: true } })
    expect(wrapper.find('.topic-row h3 a').attributes('href')).toBe('/t/small-hours/topics/welcome')
    expect(wrapper.find('.topic-row-latest').attributes('href')).toBe('/t/small-hours/reply-3')
    expect(wrapper.find('.topic-row-counts').text()).toContain('3 replies')
    expect(wrapper.find('.topic-row-people').attributes('aria-label')).toBe('3 participants')
  })

  it('uses one canonical article link and only an owned cover', () => {
    const directories = directoriesFixture([])
    const author = { id: 'a', displayName: 'Alex', handle: null, avatar: null, classification: 'Human', profileUrl: '/u/a' }
    directories.topics = [
      { key: 'first', title: 'First article', summary: { posts: null, participants: null, starter: null, createdAt: '', latestPost: null,
        article: { openingPostId: 'opener', coverImageUrl: '/media/0123456789abcdef0123456789abcdef', author, createdAt: '2026-09-28T12:00:00Z', readingMinutes: 4, removed: false } } },
      { key: 'second', title: 'Second article', summary: { posts: null, participants: null, starter: null, createdAt: '', latestPost: null,
        article: { openingPostId: 'opener2', coverImageUrl: 'https://other.example/image.jpg', author, createdAt: '2026-09-28T12:00:00Z', readingMinutes: 1, removed: false } } },
      { key: 'third', title: 'Unavailable opener', summary: { posts: null, participants: null, starter: null, createdAt: '', latestPost: null, article: null } },
    ]
    const wrapper = mount(ArticleCollection, { props: { directories, tangentKey: 'essays', signedIn: false, page: 1 } })
    const cards = wrapper.findAll('.article-row')
    expect(cards).toHaveLength(3)
    expect(cards[0]!.findAll('a')).toHaveLength(2)
    expect(cards[0]!.find('a').attributes('href')).toBe('/t/essays/topics/first')
    expect(cards[0]!.find('img').attributes('src')).toBe('/media/0123456789abcdef0123456789abcdef')
    expect(cards[1]!.find('img').exists()).toBe(false)
    expect(cards[2]!.text()).not.toContain('participant')
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
    expect(wrapper.find('#no-tangents').exists()).toBe(true)
    expect(wrapper.find('#no-tangents').text()).toContain('There are no public conversations here yet.')
  })
})
