using System.Text.Json.Serialization;
using Tangent.Participants;

namespace Tangent.Space;

/// <summary>Whose a Tangent Space is and who answers for it, as the descriptor states a person: the handle the place
/// shows, where the participant is known, and the DID.</summary>
public sealed record DescriptorPerson(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Handle,
    string Did);

/// <summary>How an agent acts as a person's delegate: the address of the authorization server's description, and the
/// public client it presents with the Device flow.</summary>
public sealed record DescriptorDelegated(string AuthorizationServer, string ClientId);

/// <summary>What the Tangent Space says about itself at <c>tangent.json</c> under its base URL, as stable JSON:
/// its name, its address, <c>api</c>, its audience, the delegated door, and absolute links to the door and to
/// <c>llms.txt</c>; for a public place also its proprietor and operator. A private place states only what a visitor needs
/// to sign in, and nothing about its people. What a later package serves, <c>did:web</c> and the publication, is absent
/// until then.</summary>
public sealed record Descriptor(string Name, string Address, string Audience, DescriptorDelegated Delegated)
{
    /// <summary>The stability promise: a client that reads 1 keeps working.</summary>
    public int Api => 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DescriptorPerson? Proprietor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DescriptorPerson? Operator { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Door { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Llms { get; init; }

    /// <summary>The descriptor of <paramref name="space"/> at <paramref name="baseUrl"/>, with the participants who are
    /// its proprietor and operator where they are known.</summary>
    public static Descriptor Of(TangentSpace space, string baseUrl, Participant? proprietor, Participant? operatorRow)
    {
        var descriptor = new Descriptor(space.Name, baseUrl, space.Audience.ToString().ToLowerInvariant(),
            new DescriptorDelegated(DelegatedDoor.AuthorizationServer(baseUrl), DelegatedDoor.ClientId))
        {
            Door = baseUrl + "/",
            Llms = LlmsText.Address(baseUrl)
        };
        return space.Audience == Space.Audience.Private
            ? descriptor
            : descriptor with
            {
                Proprietor = Person(space.ProprietorId, proprietor),
                Operator = Person(space.OperatorId, operatorRow)
            };
    }

    private static DescriptorPerson? Person(string? did, Participant? row)
        => did is null ? null : new DescriptorPerson(row is { Handle.Length: > 0 } ? row.Handle : null, did);
}
