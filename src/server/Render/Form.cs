namespace Tangent.Render;

/// <summary>A write a resource offers its reader, said one way wherever a page offers one, so its HTML form and its JSON
/// say the same: <see cref="Action"/>, the address it posts to; <see cref="Method"/>, how; <see cref="Fields"/>, each
/// with what it holds; and <see cref="Token"/>, the antiforgery token a session sends in the field it names, which a
/// request with a bearer leaves out. A field with a value carries what the page put there — the TID minted for the form
/// among them — and is sent as it is unless the field offers a choice; one without is the sender's to fill as it holds,
/// or to leave out where it holds that it may be. Tangent's own writes take the fields as a form or as one JSON object.
/// A resource carries a form only where its reader may make the write, as its HTML offers one only there.</summary>
public sealed record Form(string Action, string Method, IReadOnlyList<Form.Field> Fields, FormToken Token)
{
    /// <summary>One field of a form: its name, what it holds, and the value the page gives it, null where the sender
    /// writes it.</summary>
    public sealed record Field(string Name, string Holds, string? Value = null);

    /// <summary>A form that posts to <paramref name="action"/> with <paramref name="token"/> and these fields.</summary>
    public static Form Posting(string action, FormToken token, params Field[] fields) => new(action, HttpMethods.Post, fields, token);

    /// <summary>The value the page gives the field named <paramref name="name"/>, null where it gives none.</summary>
    public string? Value(string name) => Fields.FirstOrDefault(field => field.Name == name)?.Value;

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/>.</summary>
    public Form With(string name, string? value)
        => this with { Fields = Fields.Select(field => field.Name == name ? field with { Value = value } : field).ToList() };

    /// <summary>The field that carries a new Post's TID, with the one minted for this form as its value: the
    /// <paramref name="noun"/>'s Id, which its sender mints, so sending the same TID again never writes a second one.</summary>
    public static Field Minted(string name, string noun, string minted)
        => new(name, $"The {noun}'s Id: a TID, atproto's timestamp identifier, which its sender mints. This one was minted for this form; the same TID sent again never makes a second {noun}, and each other {noun} takes one of its own.", minted);
}
