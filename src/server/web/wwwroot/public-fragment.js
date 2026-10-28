document.querySelectorAll('[data-public-sign-in]').forEach((link) => {
  link.addEventListener('click', () => {
    const signIn = new URL(link.href)
    const returnTo = signIn.searchParams.get('return')
    if (!returnTo || !returnTo.startsWith('/') || returnTo.startsWith('//')) return
    const destination = new URL(returnTo, window.location.origin)
    destination.hash = window.location.hash
    signIn.searchParams.set('return', destination.pathname + destination.search + destination.hash)
    link.href = signIn.pathname + signIn.search
  })
})

const outlineLinks = [...document.querySelectorAll(
  '.article-outline a[href^="#article-heading-"], .article-outline-mobile a[href^="#article-heading-"]',
)]
if (outlineLinks.length) {
  const headings = [...new Set(outlineLinks.map((link) => link.getAttribute('href')?.slice(1)))]
    .map((id) => document.getElementById(id))
    .filter(Boolean)
  let pending = false
  function updateCurrentSection() {
    pending = false
    let current = headings[0]?.id
    for (const heading of headings) {
      if (heading.getBoundingClientRect().top <= 145) current = heading.id
    }
    if (window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 2) {
      current = headings.at(-1)?.id
    }
    for (const link of outlineLinks) {
      if (link.getAttribute('href') === '#' + current) link.setAttribute('aria-current', 'location')
      else link.removeAttribute('aria-current')
    }
  }
  function scheduleSectionUpdate() {
    if (pending) return
    pending = true
    requestAnimationFrame(updateCurrentSection)
  }
  window.addEventListener('scroll', scheduleSectionUpdate, { passive: true })
  window.addEventListener('hashchange', scheduleSectionUpdate)
  document.addEventListener('tangent:workspace-revealed', () => {
    window.removeEventListener('scroll', scheduleSectionUpdate)
    window.removeEventListener('hashchange', scheduleSectionUpdate)
  }, { once: true })
  scheduleSectionUpdate()
}

const appearanceButtons=[...document.querySelectorAll('[data-public-theme]')];
function publicAppearance(theme){if(!['light','dark','system'].includes(theme))theme='system';if(theme==='system')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=theme;for(const button of appearanceButtons)button.setAttribute('aria-pressed',String(button.dataset.publicTheme===theme));try{localStorage.setItem('tangent:appearance:visitor',theme)}catch{}}
let publicTheme='system';try{publicTheme=localStorage.getItem('tangent:appearance:visitor')||'system'}catch{};publicAppearance(publicTheme);for(const button of appearanceButtons)button.addEventListener('click',()=>publicAppearance(button.dataset.publicTheme));
