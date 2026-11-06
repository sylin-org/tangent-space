namespace Tangent.Render;

/// <summary>A write a resource offers its reader, said one way wherever a page offers one, so its HTML form and its JSON
/// say the same: <see cref="Action"/>, the address it posts to; <see cref="Method"/>, how; and <see cref="Fields"/>, each
/// with what it holds. A field with a value carries what the page put there — the TID minted for the form among them —
/// and is sent as it is unless the field offers a choice; one without is the sender's to fill as it holds, or to leave
/// out where it holds that it may be. Tangent's own writes take the fields as a form or as one JSON object, under the
/// session's cookie. A resource carries a form only where its reader may make the write, as its HTML offers one only
/// there.</summary>
public sealed record Form(string Action, string Method, IReadOnlyList<Form.Field> Fields) : IPart
{
    /// <summary>One field of a form: its name, what it holds, the value the page gives it, null where the sender writes it,
    /// and what it is drawn as (<see cref="Input"/>), which its JSON says too.</summary>
    public sealed partial record Field(string Name, string Holds, string? Value = null, string As = Input.Text);

    public sealed partial record Field
    {
        /// <summary>The values the field takes, where it is a choice, each with its name and what it means.</summary>
        public IReadOnlyList<Choice>? Choices { get; init; }

        /// <summary>The most characters the field holds, where it is bounded.</summary>
        public int? Limit { get; init; }

        /// <summary>Whether the sender must fill the field.</summary>
        public bool Required { get; init; }

        /// <summary>What a person reads above the field.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Label { get; init; }

        /// <summary>The sentence under the field.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Note { get; init; }

        /// <summary>Why what was sent at this field was refused, shown under it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Failure { get; init; }
    }

    /// <summary>One value a choice takes: the value sent, its name, and what choosing it means.</summary>
    public sealed record Choice(string Value, string Name, string Detail);

    /// <summary>What a field is drawn as: <see cref="Text"/>, a line a person writes; <see cref="Area"/>, a few lines;
    /// <see cref="Choice"/>, one of its <see cref="Field.Choices"/>; <see cref="Hidden"/>, a value the page gives and the sender
    /// sends as it is.</summary>
    public static class Input
    {
        public const string Text = "text";
        public const string Area = "area";
        public const string Choice = "choice";
        public const string Hidden = "hidden";
    }

    /// <summary>The way back beside the form's button, where it offers one.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Onward? Cancel { get; init; }

    /// <summary>What the button says while the form is sent.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Busy { get; init; }

    /// <summary>Whether the form is shown to a reader who may not send it: its fields read only, its choices disabled, no
    /// button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool ReadOnly { get; init; }

    /// <summary>What sets this form's fields apart from another's of the same tool on one page, for their ids.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Key { get; init; }

    /// <summary>The name of the form's tool, with hyphens, which its class carries, so a form, its route and its tool share
    /// one name.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Tool { get; init; }

    /// <summary>The words on the form's button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string Submit { get; init; } = "Send";

    /// <summary>Whether the button is drawn quietly, beside the page's main one.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Quiet { get; init; }

    /// <summary>The island that acts on the form in the browser, where one does.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Island { get; init; }

    /// <summary>What a form's JSON says as <c>token</c>. Companion Lobby's adapter posts a form only where its
    /// <c>token.value</c> holds something and sends it back in a header, which nothing here reads.</summary>
    public sealed record Unread(string Value);

    /// <summary>The one <see cref="Unread"/> every form carries.</summary>
    public Unread Token => new("unread");

    /// <summary>A form that posts to <paramref name="action"/> with these fields.</summary>
    public static Form Posting(string action, params Field[] fields) => new(action, HttpMethods.Post, fields);

    /// <summary>The value the page gives the field named <paramref name="name"/>, null where it gives none.</summary>
    public string? Value(string name) => Fields.FirstOrDefault(field => field.Name == name)?.Value;

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/> by the page, which the
    /// sender sends as it is.</summary>
    public Form Given(string name, string value)
        => this with { Fields = Fields.Select(field => field.Name == name ? field with { Value = value, As = Input.Hidden } : field).ToList() };

    /// <summary>What a destructive form says before it is sent, where it confirms in the page: its button opens the sentence and
    /// the button that sends it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Confirm { get; init; }

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/>.</summary>
    public Form With(string name, string? value)
        => this with { Fields = Fields.Select(field => field.Name == name ? field with { Value = value } : field).ToList() };

    /// <summary>The field that carries a new Post's TID, with the one minted for this form as its value.</summary>
    public static Field Minted(string name, string minted) => new(name, Holds.Id + " This one was minted for this form.", minted, Input.Hidden);
}
