using TangentSpace.Rooms;

namespace TangentSpace.Web;

public sealed record SiteWelcome(string Name, bool Established, ParticipantWelcome? Participant, string SignIn, string? SignOut,
    RoomListing Rooms);
