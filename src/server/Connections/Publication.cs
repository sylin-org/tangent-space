using Koan.Data.Core.Model;

namespace Tangent.Connections;

/// <summary>The place as a <c>site.standard.publication</c> record under its proprietor's account, one row: who put it,
/// the account it is under, its address and the key it was put at, and what it said, so a renamed or described place, or
/// a proprietor bound to another account since, is put again. Its proprietor's Connection
/// writes it; every article's document names it as its <c>site</c>, and
/// <c>/.well-known/site.standard.publication</c> answers its address (<see cref="PublicationController"/>).</summary>
public sealed class Publication : Entity<Publication>
{
    public const string SingleRow = "place";

    public Publication() => Id = SingleRow;

    /// <summary>The participant who put it, the proprietor then.</summary>
    public string Proprietor { get; set; } = "";

    /// <summary>The DID of the account it is under.</summary>
    public string Did { get; set; } = "";

    /// <summary>The record key, a TID minted once.</summary>
    public string Key { get; set; } = "";

    public string Uri { get; set; } = "";

    public string Cid { get; set; } = "";

    public string Fingerprint { get; set; } = "";
}
