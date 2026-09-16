using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Web;

public sealed record SpaceWelcome(string Name, bool Established, ParticipantWelcome? Participant, string SignIn, string? SignOut,
    RoomListing Rooms, ServerSettings? Server = null, string Onboarding = "complete");
