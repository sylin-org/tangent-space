<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { contentApi, type BlueskyAuthor } from '../api'

const authors = ref<BlueskyAuthor[]>([])
const error = ref('')
const busy = ref(false)

async function load() {
  busy.value = true
  error.value = ''
  try { authors.value = (await contentApi.blueskyAuthors()).authors }
  catch { error.value = 'Bluesky sources could not be loaded.' }
  finally { busy.value = false }
}

function readingLink(value: string): string | undefined {
  try {
    const url = new URL(value, window.location.origin)
    return url.origin === window.location.origin && url.pathname.startsWith('/sources/bluesky/')
      ? url.pathname + url.search : undefined
  } catch { return undefined }
}

onMounted(load)
</script>

<template>
  <section v-if="authors.length || error" class="bluesky-directory" aria-labelledby="bluesky-directory-title" :aria-busy="busy">
    <header><h2 id="bluesky-directory-title">From Bluesky</h2><p class="hint">Public Posts from recently refreshed accounts. Open an account to read here.</p></header>
    <nav v-if="authors.length" aria-label="Public Bluesky sources">
      <ul>
        <li v-for="author in authors" :key="author.actorDid">
          <a class="bluesky-author-row" :href="readingLink(author.postsUrl)">
            <strong>{{ author.handle ? '@' + author.handle.replace(/^@/, '') : author.actorDid }}</strong>
            <span class="hint">Refreshed <time :datetime="author.refreshedAt">{{ new Date(author.refreshedAt).toLocaleDateString() }}</time></span>
          </a>
        </li>
      </ul>
    </nav>
    <p v-if="error" class="hint" role="status">{{ error }}</p>
    <button v-if="error" type="button" class="btn btn-quiet" :disabled="busy" @click="load">{{ busy ? 'Loading…' : 'Retry' }}</button>
  </section>
</template>

<style scoped>
.bluesky-directory { margin-top: 2rem; padding-top: 1.5rem; border-top: 1px solid var(--line); }
ul { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 15rem), 1fr)); gap: .75rem; }
li { overflow-wrap: anywhere; }
.bluesky-author-row { display:grid; gap:.25rem; padding:.75rem; color:inherit; text-decoration:none; border-radius:6px; }
.bluesky-author-row:hover { background:var(--surface-soft); }
.bluesky-author-row:focus-visible { outline:2px solid var(--accent); outline-offset:-2px; }
.bluesky-author-row strong { color:var(--accent); }
</style>
