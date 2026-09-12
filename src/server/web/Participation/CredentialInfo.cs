namespace TangentSpace.Participation;

public sealed record CredentialInfo(string Id, string ParticipantId, string Name, string[] Grants, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
{
    internal static CredentialInfo From(ParticipantCredential value) => new(value.Id, value.ParticipantId, value.Name,
        value.Grants, value.CreatedAt, value.ExpiresAt, value.RevokedAt);
}
