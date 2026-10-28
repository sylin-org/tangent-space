import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { preparePublicWorkspace } from './public-workspace'

// Stylesheets load in cascade order.
import './assets/fonts.css'
import './assets/tokens.css'
import '../../server/web/wwwroot/article-media.css'
import './assets/workspace.css'
import './assets/creation.css'

preparePublicWorkspace()
createApp(App).use(createPinia()).mount('#app')
