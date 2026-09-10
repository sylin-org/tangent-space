namespace TangentSpace.Participation;

public sealed record CredentialInfo(string Id, string Did, string Name, string[] Grants, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
{
    internal static CredentialInfo From(ParticipantCredential value) => new(value.Id, value.ParticipantDid, value.Name,
        value.Grants, value.CreatedAt, value.ExpiresAt, value.RevokedAt);
}
