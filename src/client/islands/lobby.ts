import { defineComponent, type PropType } from 'vue'
import { findLobby, handOff } from '../lobby'

// The bring-your-agent page. The server renders how to get Companion Lobby and, hidden, the offer without its address;
// when the Lobby answers on this machine, the island gives the offer's link the address that hands this place to it and
// shows the offer in place of how to get one. Anything else leaves the page as the server rendered it.
export const lobby = defineComponent({
  name: 'LobbyIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    address: { type: String, required: true },
  },
  setup(props) {
    void findLobby().then((found) => {
      const offer = props.host.querySelector<HTMLElement>('[data-lobby-offer]')
      const link = offer?.querySelector('a')
      const absent = props.host.querySelector<HTMLElement>('[data-lobby-absent]')
      if (!found || !offer || !link || !absent) return
      link.href = handOff(props.address)
      offer.hidden = false
      absent.hidden = true
    })
    return () => null
  },
})
