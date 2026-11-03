namespace Tangent.Render;

/// <summary>A write a resource offers its reader, said one way wherever a page offers one, so its HTML form and its JSON
/// say the same: <see cref="Action"/>, the address it posts to; <see cref="Method"/>, how; and <see cref="Fields"/>, each
/// with what it holds. A field with a value carries what the page put there — the TID minted for the form among them —
/// and is sent as it is unless the field offers a choice; one without is the sender's to fill as it holds, or to leave
/// out where it holds that it may be. Tangent's own writes take the fields as a form or as one JSON object, under the
/// session's cookie. A resource carries a form only where its reader may make the write, as its HTML offers one only
/// there.</summary>
public sealed record Form(string Action, string Method, IReadOnlyList<Form.Field> Fields)
{
    /// <summary>One field of a form: its name, what it holds, and the value the page gives it, null where the sender
    /// writes it.</summary>
    public sealed record Field(string Name, string Holds, string? Value = null);

    /// <summary>What a form's JSON says as <c>token</c>. Companion Lobby's adapter posts a form only where its
    /// <c>token.value</c> holds something and sends it back in a header, which nothing here reads.</summary>
    public sealed record Unread(string Value);

    /// <summary>The one <see cref="Unread"/> every form carries.</summary>
    public Unread Token => new("unread");

    /// <summary>A form that posts to <paramref name="action"/> with these fields.</summary>
    public static Form Posting(string action, params Field[] fields) => new(action, HttpMethods.Post, fields);

    /// <summary>The value the page gives the field named <paramref name="name"/>, null where it gives none.</summary>
    public string? Value(string name) => Fields.FirstOrDefault(field => field.Name == name)?.Value;

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/>.</summary>
    public Form With(string name, string? value)
        => this with { Fields = Fields.Select(field => field.Name == name ? field with { Value = value } : field).ToList() };

    /// <summary>The field that carries a new Post's TID, with the one minted for this form as its value.</summary>
    public static Field Minted(string name, string minted) => new(name, Holds.Id + " This one was minted for this form.", minted);
}
