using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CarpaNet;
using CarpaNet.Repo;
using Org.BouncyCastle.Crypto.Digests;

namespace TangentSpace.AtProtocol.Verification;

// Protocol composition for the pinned alpha; see TECHNICAL.md for the
// authenticated-transport requirement and the format's non-transferable proof.
internal static class SpaceCarVerifier
{
    public const int MaxCarBytes = 8 * 1024 * 1024;
    public const int MaxRecords = 1024;
    private static readonly Regex Nsid = new(@"^[a-zA-Z](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(?:\.[a-zA-Z](?:[a-zA-Z0-9]{0,62})?)$", RegexOptions.CultureInvariant);
    private static readonly Regex RecordKey = new(@"^[a-zA-Z0-9_~.:-]{1,512}$", RegexOptions.CultureInvariant);

    public static VerifiedSpaceRepo Verify(byte[] car, string expectedSpace, string expectedAuthor,
        ResolvedAuthorKey key, bool expectValues = true)
    {
        Require(key.SubjectDid == expectedAuthor, "Resolved key belongs to a different author");
        PreflightFrames(car);
        using var reader = new CarReader(car);
        Require(reader.Header.Version == 1 && reader.Header.Roots.Count == 2, "Expected two CAR roots");
        var blocks = reader.ReadBlocks().ToArray();
        Require(blocks.Length >= 2 && blocks.Length <= MaxRecords + 2, "Invalid block count");
        foreach (var block in blocks)
        {
            Require(block.Cid.IsAtProtoBlessedFormat, "Unsupported CID format");
            Require(CryptographicOperations.FixedTimeEquals(SHA256.HashData(block.Data), block.Cid.Hash!), "Block CID mismatch");
            ValidateDagCbor(block.Data);
        }
        Require(blocks[0].Cid == reader.Header.Roots[0] && blocks[1].Cid == reader.Header.Roots[1], "CAR root order mismatch");
        var commit = ReadCommit(blocks[0].Data);
        var context = EncodeContext(expectedSpace, expectedAuthor, commit.Revision, commit.Ikm);
        Span<byte> macKey = stackalloc byte[32];
        // Upstream hkdfSha256 uses HKDF-Expand with ikm as PRK, without Extract.
        HKDF.Expand(HashAlgorithmName.SHA256, commit.Ikm, macKey, context);
        Require(CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(macKey, commit.Hash), commit.Mac), "Commit MAC mismatch");
        Require(key.VerifySignature(context, commit.Signature), "Commit signature mismatch");
        var index = ReadIndex(blocks[1].Data);
        var state = ComputeSetState(index.Select(entry => $"{entry.Path}/{entry.Cid.Value}"));
        Require(CryptographicOperations.FixedTimeEquals(SHA256.HashData(state), commit.Hash), "Index set hash mismatch");
        var hasValues = blocks.Length > 2;
        Require((!expectValues && !hasValues) || blocks.Length == index.Count + 2, "Record completeness mismatch");
        var records = new List<VerifiedRecord>();
        for (int i = 2; i < blocks.Length; i++)
        {
            Require(i - 2 < index.Count, "Unexpected extra record block");
            var entry = index[i - 2];
            Require(blocks[i].Cid == entry.Cid, "Record block order or CID mismatch");
            var recordReader = new CborReader(blocks[i].Data, CborConformanceMode.Strict);
            Require(recordReader.PeekState() == CborReaderState.StartMap, "Record must be a map");
            var split = entry.Path.Split('/');
            records.Add(new(split[0], split[1], entry.Cid.Value, blocks[i].Data));
        }
        return new(expectedSpace, expectedAuthor, commit.Revision, records, index.Count, expectValues || hasValues);
    }

    public static byte[] EncodeContext(string space, string author, string revision, byte[] ikm)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.UTF8.GetBytes("atproto-space-v1"));
        Span<byte> size = stackalloc byte[2];
        foreach (var field in new[] { Encoding.UTF8.GetBytes(space), Encoding.UTF8.GetBytes(author), Encoding.UTF8.GetBytes(revision), ikm })
        {
            Require(field.Length <= ushort.MaxValue, "Context field exceeds uint16 length");
            BinaryPrimitives.WriteUInt16BigEndian(size, (ushort)field.Length);
            output.Write(size); output.Write(field);
        }
        return output.ToArray();
    }

    public static byte[] ComputeSetState(IEnumerable<string> elements)
    {
        var state = new byte[2048];
        foreach (var element in elements)
        {
            var input = Encoding.UTF8.GetBytes(element);
            var expanded = new byte[2048];
            var blake3 = new Blake3Digest();
            blake3.BlockUpdate(input, 0, input.Length);
            blake3.OutputFinal(expanded, 0, expanded.Length);
            // This arithmetic is the upstream protocol's LtHash composition;
            // BLAKE3 and SHA256 themselves are maintained library primitives.
            for (int i = 0; i < state.Length; i += 2)
            {
                var sum = unchecked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(i, 2)) + BinaryPrimitives.ReadUInt16LittleEndian(expanded.AsSpan(i, 2))));
                BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(i, 2), sum);
            }
        }
        return state;
    }

    private sealed record Commit(byte[] Hash, byte[] Ikm, byte[] Mac, byte[] Signature, string Revision);
    private static Commit ReadCommit(byte[] bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Strict);
        var count = reader.ReadStartMap();
        Require(count == 6, "Invalid commit shape");
        byte[]? hash = null, ikm = null, mac = null, signature = null;
        string? revision = null;
        long? version = null;
        for (int i = 0; i < count; i++)
        {
            switch (reader.ReadTextString())
            {
                case "ver": version = reader.ReadInt64(); break;
                case "hash": hash = reader.ReadByteString(); break;
                case "ikm": ikm = reader.ReadByteString(); break;
                case "mac": mac = reader.ReadByteString(); break;
                case "sig": signature = reader.ReadByteString(); break;
                case "rev": revision = reader.ReadTextString(); break;
                default: throw new InvalidDataException("Unknown commit field");
            }
        }
        reader.ReadEndMap();
        Require(version == 1 && hash?.Length == 32 && ikm?.Length == 32 && mac?.Length == 32 && signature?.Length == 64 && !string.IsNullOrEmpty(revision), "Unsupported or malformed commit");
        Require(reader.BytesRemaining == 0, "Trailing commit data");
        return new(hash!, ikm!, mac!, signature!, revision!);
    }

    private static List<(string Path, ATCid Cid)> ReadIndex(byte[] bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Strict);
        var count = reader.ReadStartMap();
        Require(count is >= 0 and <= MaxRecords, "Invalid index length");
        var entries = new List<(string, ATCid)>();
        for (int i = 0; i < count; i++)
        {
            var path = reader.ReadTextString();
            var split = path.Split('/');
            Require(split.Length == 2 && Nsid.IsMatch(split[0]) && split[0].Length <= 317 && RecordKey.IsMatch(split[1]) && split[1] is not "." and not "..", "Invalid record path");
            entries.Add((path, ReadCid(reader)));
        }
        reader.ReadEndMap();
        Require(reader.BytesRemaining == 0, "Trailing index data");
        return entries;
    }

    // CarpaNet's reader parses framing but does not verify hashes or cap declared
    // allocation sizes. Bound frame lengths before passing it untrusted bytes.
    private static void PreflightFrames(byte[] car)
    {
        Require(car.Length > 0 && car.Length <= MaxCarBytes, "CAR size outside supported bound");
        int offset = 0, frames = 0;
        while (offset < car.Length)
        {
            ulong value = 0; int shift = 0, start = offset;
            while (true)
            {
                Require(offset < car.Length && shift < 63, "Invalid frame length");
                byte next = car[offset++]; value |= (ulong)(next & 127) << shift;
                if ((next & 128) == 0) { Require(offset - start == 1 || next != 0, "Nonminimal frame length"); break; }
                shift += 7;
            }
            Require(value > 0 && value <= (ulong)(car.Length - offset), "Frame exceeds available data");
            if (frames++ == 0) ValidateHeader(car.AsSpan(offset, (int)value).ToArray());
            offset += (int)value;
            Require(frames <= MaxRecords + 3, "Too many CAR frames");
        }
    }

    private static void ValidateHeader(byte[] bytes)
    {
        ValidateDagCbor(bytes);
        var reader = new CborReader(bytes, CborConformanceMode.Strict);
        Require(reader.ReadStartMap() == 2, "Expected explicit CAR roots and version");
        Require(reader.ReadTextString() == "roots", "Expected CAR roots");
        Require(reader.ReadStartArray() == 2, "Expected two CAR roots");
        ReadCid(reader); ReadCid(reader); reader.ReadEndArray();
        Require(reader.ReadTextString() == "version" && reader.ReadInt32() == 1, "Expected CAR version 1");
        reader.ReadEndMap();
        Require(reader.BytesRemaining == 0, "Trailing CAR header data");
    }

    private static ATCid ReadCid(CborReader reader)
    {
        Require(reader.ReadTag() == (CborTag)42, "Only CID tags are permitted");
        var bytes = reader.ReadByteString();
        Require(bytes.Length == 37 && bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0x71 && bytes[3] == 0x12 && bytes[4] == 0x20, "Expected blessed DAG-CBOR CID");
        return ATCid.FromBytes(bytes[1..]);
    }

    private static void ValidateDagCbor(byte[] bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Strict);
        ValidateValue(reader, 0);
        Require(reader.BytesRemaining == 0, "Trailing CBOR data");
    }

    private static void ValidateValue(CborReader reader, int depth)
    {
        Require(depth <= 64, "CBOR nesting exceeds supported bound");
        switch (reader.PeekState())
        {
            case CborReaderState.StartMap:
                var maps = reader.ReadStartMap(); Require(maps is >= 0, "Indefinite CBOR map");
                byte[]? previous = null;
                for (int i = 0; i < maps; i++)
                {
                    var encoded = Encoding.UTF8.GetBytes(reader.ReadTextString());
                    Require(previous is null || previous.Length < encoded.Length || previous.Length == encoded.Length && previous.AsSpan().SequenceCompareTo(encoded) < 0, "Noncanonical or duplicate map key");
                    previous = encoded; ValidateValue(reader, depth + 1);
                }
                reader.ReadEndMap(); break;
            case CborReaderState.StartArray:
                var arrays = reader.ReadStartArray(); Require(arrays is >= 0, "Indefinite CBOR array");
                for (int i = 0; i < arrays; i++) ValidateValue(reader, depth + 1);
                reader.ReadEndArray(); break;
            case CborReaderState.UnsignedInteger: Require(reader.ReadUInt64() <= 9007199254740991UL, "Integer outside DRISL bound"); break;
            case CborReaderState.NegativeInteger: Require(reader.ReadInt64() >= -9007199254740991L, "Integer outside DRISL bound"); break;
            case CborReaderState.TextString: reader.ReadTextString(); break;
            case CborReaderState.ByteString: reader.ReadByteString(); break;
            case CborReaderState.Boolean: reader.ReadBoolean(); break;
            case CborReaderState.Null: reader.ReadNull(); break;
            case CborReaderState.DoublePrecisionFloat: Require(double.IsFinite(reader.ReadDouble()), "Nonfinite float"); break;
            case CborReaderState.Tag: ReadCid(reader); break;
            default: throw new InvalidDataException("Unsupported DAG-CBOR value");
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}

