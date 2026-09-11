namespace TangentSpace.AtProtocol.Verification;

public sealed record VerifiedRecord(string Collection, string RecordKey, string Cid, byte[] DagCbor);
