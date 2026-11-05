// Where a signed-in reader stopped in a Topic, kept in this browser alone: the Topic, its title and the address of the
// furthest Post they had on screen. Reading writes nothing to the place; the door reads this back as one link.

export interface Place {
  topic: string
  title: string
  address: string
}

export const placeKey = (participant: string) => `tangent:reading:${participant}`

export function readPlace(participant: string): Place | null {
  try {
    const kept = JSON.parse(window.localStorage.getItem(placeKey(participant)) ?? 'null') as Partial<Place> | null
    return kept && typeof kept.topic === 'string' && typeof kept.title === 'string' && typeof kept.address === 'string'
      ? (kept as Place)
      : null
  } catch {
    return null
  }
}

export function keepPlace(participant: string, place: Place | null): void {
  try {
    if (place) window.localStorage.setItem(placeKey(participant), JSON.stringify(place))
    else window.localStorage.removeItem(placeKey(participant))
  } catch {}
}
