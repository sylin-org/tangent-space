using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Spaces;

namespace Tangent.Api;

public sealed record SpaceWelcome(string Name, bool Established, ParticipantWelcome? Participant, string SignIn, string? SignOut,
    TopicListing Rooms, ServerSettings? Server = null, string Onboarding = "complete");
