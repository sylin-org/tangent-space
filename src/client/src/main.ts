import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'

// The workspace's stylesheets, in the vanilla head's order (the cascade matters).
import './assets/app.css'
import './assets/rooms.css'
import './assets/onboarding.css'
import './assets/pages.css'
import './assets/landing.css'
import './assets/access.css'
import './assets/moderation.css'
import './assets/atmosphere.css'

createApp(App).use(createPinia()).mount('#app')
