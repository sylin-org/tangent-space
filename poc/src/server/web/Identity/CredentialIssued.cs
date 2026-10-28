namespace Tangent.Identity;

/// <summary>One-time enrollment response; subsequent listing never returns Token.</summary>
public sealed record CredentialIssued(CredentialInfo Credential, string Token);
