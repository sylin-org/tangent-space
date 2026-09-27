using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Spaces;

namespace Tangent.Api;

public sealed record SpaceWelcome(string Name, bool Established, ParticipantWelcome? Participant, string SignIn, string? SignOut,
    TopicListing Topics, ServerSettings? Server = null, string Onboarding = "complete",
    InitialTangentPolicy? InitialTangent = null, string? InitialTangentKey = null);

public sealed record InitialTangentPolicy(string Audience, string AudienceDescription,
    string Admission, string AdmissionDescription);
