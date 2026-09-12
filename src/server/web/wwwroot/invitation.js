const button = document.getElementById('join')
button?.addEventListener('click', async () => {
  const status = document.getElementById('status')
  button.disabled = true
  status.textContent = 'Making room for you…'
  try {
    const response = await fetch(`/invite/${encodeURIComponent(button.dataset.invitation)}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: '{}', credentials: 'same-origin'
    })
    const result = await response.json()
    if (!response.ok) throw new Error('InvitationUnavailable')
    const destination = new URL(result.destination, location.origin)
    if (destination.origin !== location.origin) throw new Error('InvalidDestination')
    location.assign(destination.href)
  } catch {
    status.textContent = 'We couldn’t confirm your arrival. Reload this page to check your invitation.'
    button.disabled = false
  }
})
