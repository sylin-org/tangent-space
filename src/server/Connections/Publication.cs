using System.Text.Json.Nodes;
using Koan.Data.Core.Model;

namespace Tangent.Connections;

/// <summary>The place as a <c>site.standard.publication</c> record under its proprietor's account, one row: who put it,
/// the account it is under, its address and the key it was put at; what it said, so a place that says something else, or
/// a proprietor bound to another account since, is put again; and the icon it carries with the mark that icon was made
/// from, so a picture goes up only for a mark or an account the record has not carried. Its proprietor's Connection
/// writes it; every article's document names it as its <c>site</c>, and <c>/.well-known/site.standard.publication</c>
/// answers its address (<see cref="PublicationController"/>).</summary>
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

    /// <summary>The place's mark as it was put, which its icon was made from, as <see cref="Space.Appearance.Mark"/> names
    /// it; empty for its letter.</summary>
    public string Mark { get; set; } = "";

    /// <summary>The icon's blob in the account's repository, as the record names it, in JSON; empty for none.</summary>
    public string Icon { get; set; } = "";

    /// <summary>The icon's blob, for the record to name again; null for none.</summary>
    public JsonObject? IconBlob() => Icon.Length == 0 ? null : JsonNode.Parse(Icon) as JsonObject;
}
