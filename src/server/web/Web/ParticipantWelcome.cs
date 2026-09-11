namespace TangentSpace.Web;

public sealed record ParticipantWelcome(string Did, string? Handle, bool IsOwner, DateTimeOffset JoinedAt);
