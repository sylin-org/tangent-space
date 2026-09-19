import { defineStore } from 'pinia'
import { api, EnvelopeError, tangentKeyOf, topicKeyOf, type Identity, type PostDto, type TangentDto, type TopicDto } from '../api'

export type WatchMode = 'all' | 'replies' | 'none'

let nextRequestId = 0
const requestId = (prefix: string) => `${prefix}-${Date.now().toString(36)}-${++nextRequestId}`

export const useWorkspace = defineStore('workspace', {
  state: () => ({
    identity: null as Identity | null,
    serverRef: '',
    serverLabel: '',
    error: null as string | null,
    loading: false,
    tangents: [] as TangentDto[],
    activeTangentKey: null as string | null,
    topics: [] as TopicDto[],
    activeTopicKey: null as string | null,
    topicTitle: '',
    posts: [] as PostDto[],
    posting: false,
    readCursor: null as string | null,
    watchMode: 'all' as WatchMode,
  }),
  getters: {
    activeTangent: (s) => s.tangents.find((t) => tangentKeyOf(t.tangentRef) === s.activeTangentKey) ?? null,
  },
  actions: {
    async arrive() {
      this.loading = true
      this.error = null
      try {
        const envelope = await api.arrive()
        this.identity = envelope.identity
        this.serverRef = envelope.place?.serverRef ?? ''
        this.tangents = envelope.result.data?.tangents ?? []
        if (this.tangents.length > 0 && !this.activeTangentKey) await this.openTangent(tangentKeyOf(this.tangents[0].tangentRef))
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The server could not be reached.'
      } finally {
        this.loading = false
      }
    },
    async openTangent(tangentKey: string) {
      this.loading = true
      this.error = null
      this.activeTopicKey = null
      this.posts = []
      this.topicTitle = ''
      try {
        const envelope = await api.topics(tangentKey)
        this.activeTangentKey = tangentKey
        this.topics = envelope.result.data?.topics ?? []
        if (this.topics.length > 0) await this.openTopic(topicKeyOf(this.topics[0].topicRef))
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The server could not be reached.'
      } finally {
        this.loading = false
      }
    },
    async openTopic(topicKey: string) {
      this.loading = true
      this.error = null
      try {
        const envelope = await api.readTopic(topicKey)
        this.activeTopicKey = topicKey
        this.topicTitle = envelope.result.data?.title ?? topicKey
        this.posts = envelope.result.data?.posts ?? []
        this.readCursor = envelope.continuation?.readCursor ?? null
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The server could not be reached.'
      } finally {
        this.loading = false
      }
    },
    async post(text: string, replyTo?: string) {
      if (!this.activeTopicKey || !text.trim()) return
      this.posting = true
      this.error = null
      try {
        await api.createPost(this.activeTopicKey, requestId('post'), text.trim(), replyTo)
        await this.openTopic(this.activeTopicKey)
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The post could not be saved.'
      } finally {
        this.posting = false
      }
    },
    async markRead() {
      if (!this.activeTopicKey || !this.readCursor) return
      this.error = null
      try {
        await api.readPosition(this.activeTopicKey, this.readCursor)
        this.readCursor = null
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The read position could not be saved.'
      }
    },
    async watchTangent(mode: WatchMode) {
      if (!this.activeTangentKey || !this.serverRef) return
      this.error = null
      try {
        await api.watch(`${this.serverRef}::${this.activeTangentKey}`, mode)
      } catch (error) {
        this.error = error instanceof EnvelopeError ? error.message : 'The watch could not be saved.'
      }
    },
  },
})
