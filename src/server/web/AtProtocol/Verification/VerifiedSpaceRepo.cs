namespace TangentSpace.AtProtocol.Verification;

public sealed record VerifiedSpaceRepo(string Space, string Author, string Revision,
    IReadOnlyList<VerifiedRecord> Records, int IndexCount, bool IncludesValues);
