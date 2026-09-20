import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'

// The workspace's stylesheets, in the vanilla head's order (the cascade matters).
import '../../server/web/wwwroot/app.css'
import '../../server/web/wwwroot/rooms.css'
import '../../server/web/wwwroot/onboarding.css'
import '../../server/web/wwwroot/pages.css'
import '../../server/web/wwwroot/landing.css'
import '../../server/web/wwwroot/access.css'
import '../../server/web/wwwroot/moderation.css'
import '../../server/web/wwwroot/atmosphere.css'

createApp(App).use(createPinia()).mount('#app')
