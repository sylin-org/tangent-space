namespace Tangent.Render;

/// <summary>A write a resource offers its reader, said one way wherever a page offers one, so its HTML form and its JSON
/// say the same: <see cref="Action"/>, the address it posts to; <see cref="Method"/>, how; and <see cref="Fields"/>, each
/// with what it holds and what it is drawn as. A field with a value carries what the page put there — the TID minted for
/// the form among them — and is sent as it is unless the field offers a choice; one without is the sender's to fill as it
/// holds, or to leave out where it holds that it may be. Tangent's own writes take the fields as a form or as one JSON
/// object, under the session's cookie. A resource carries a form only where its reader may make the write, as its HTML
/// offers one only there. It is a part: one component draws every form from this record, and what a form says to a person
/// and nothing to an agent stays out of its JSON.</summary>
public sealed record Form(string Action, string Method, IReadOnlyList<Form.Field> Fields) : IPart
{
    /// <summary>One field of a form: its name, what it holds, the value the page gives it, null where the sender writes it,
    /// and what it is drawn as (<see cref="Input"/>), which its JSON says too, with its choices, its bound and whether it is
    /// required.</summary>
    public sealed record Field(string Name, string Holds, string? Value = null, string As = Input.Text)
    {
        /// <summary>The values the field takes, where it is a choice, each with its name and what it means.</summary>
        public IReadOnlyList<Choice>? Choices { get; init; }

        /// <summary>The values chosen, where the field takes several.</summary>
        public IReadOnlyList<string>? Values { get; init; }

        /// <summary>The value the field stands at by default, where it has one.</summary>
        public string? Default { get; init; }

        /// <summary>The most characters the field holds, where it is bounded.</summary>
        public int? Limit { get; init; }

        /// <summary>Whether the sender must fill the field.</summary>
        public bool Required { get; init; }

        /// <summary>What a person reads above the field.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Label { get; init; }

        /// <summary>The sentence under the field; code between backticks.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Note { get; init; }

        /// <summary>Why what was sent at this field was refused, shown under it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Failure { get; init; }

        /// <summary>An example of what the field holds, shown while it is empty.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Placeholder { get; init; }

        /// <summary>What stands before the field's value, as an <c>@</c> before a handle.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Prefix { get; init; }

        /// <summary>The line the field and its neighbours of the same group sit behind, quiet until one is away from its
        /// default or refused.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Group { get; init; }

        /// <summary>How many lines a field of several lines shows.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int Rows { get; init; } = 3;

        /// <summary>Whether the page opens with the field focused, where no refusal concerns another.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Focus { get; init; }

        /// <summary>Whether this field alone is shown and never sent, as words sent that were not saved.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool ReadOnly { get; init; }

        /// <summary>Whether the field holds an account's handle, which the browser offers and leaves as typed.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Handle { get; init; }

        /// <summary>Whether the field holds what is read exactly as typed, as code, which no spelling check marks.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Exact { get; init; }

        /// <summary>The kinds of file the field takes.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Accept { get; init; }

        /// <summary>The form that returns the field to its default in one press, beside it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Form? Reset { get; init; }

        /// <summary>The form the field and its own button send in place of the one it stands in, as a file sent alone.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Form? Sends { get; init; }

        /// <summary>The field whose value this one carries back where the write fails, and the name it carries it as.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public (string From, string As)? Carries { get; init; }

        /// <summary>The island that makes the field its editor, and what it reads.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Island { get; init; }

        [Newtonsoft.Json.JsonIgnore]
        public string? IslandProps { get; init; }
    }

    /// <summary>One value a choice takes: the value sent, its name, what choosing it means, and the picture it is, where it is
    /// one; <see cref="Locked"/> where the sender may not change it.</summary>
    public sealed record Choice(string Value, string Name, string Detail = "")
    {
        public string? Picture { get; init; }

        public bool Locked { get; init; }

        /// <summary>The tone and letter of the tile it shows, where it is an accent.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public (string Tone, string Letter)? Tile { get; init; }

        /// <summary>The accent its swatch shows, where it is a palette.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Swatch { get; init; }
    }

    /// <summary>What a field is drawn as: <see cref="Text"/>, a line; <see cref="Search"/>, words to find; <see cref="Area"/>,
    /// a few lines; <see cref="Markdown"/>, a body in Markdown, which an island may make its editor; <see cref="Choice"/>, one
    /// of its choices as cards; <see cref="Tone"/>, an accent; <see cref="Picture"/>, an image; <see cref="Check"/>, any of
    /// its choices; <see cref="Switch"/>, one of its choices, sent by pressing it; <see cref="File"/>, a file;
    /// <see cref="Fixed"/>, a value it holds that no one changes here, said in a sentence; <see cref="Hidden"/>, a value the
    /// page gives and the sender sends as it is.</summary>
    public static class Input
    {
        public const string Text = "text";
        public const string Search = "search";
        public const string Area = "area";
        public const string Markdown = "markdown";
        public const string Choice = "choice";
        public const string Tone = "tone";
        public const string Picture = "picture";
        public const string Check = "check";
        public const string Switch = "switch";
        public const string File = "file";
        public const string Fixed = "fixed";
        public const string Hidden = "hidden";
    }

    /// <summary>What a form's JSON says as <c>token</c>. Companion Lobby's adapter posts a form only where its
    /// <c>token.value</c> holds something and sends it back in a header, which nothing here reads.</summary>
    public sealed record Unread(string Value);

    /// <summary>The one <see cref="Unread"/> every form carries.</summary>
    public Unread Token => new("unread");

    /// <summary>The name of the form's tool, with hyphens, which its class carries, so a form, its route and its tool share
    /// one name.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Tool { get; init; }

    /// <summary>The words on the form's button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string Submit { get; init; } = "Send";

    /// <summary>What the button says while the form is sent.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Busy { get; init; }

    /// <summary>Whether the button is the page's main action, drawn large even where it is the form's only control.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Main { get; init; }

    /// <summary>Whether the button is drawn quietly, beside the page's main one.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Quiet { get; init; }

    /// <summary>The island that acts on the form in the browser, where one does, and what it reads.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Island { get; init; }

    [Newtonsoft.Json.JsonIgnore]
    public string? IslandProps { get; init; }

    /// <summary>What sets this form's fields apart from another's of the same tool on one page, for their ids.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Key { get; init; }

    /// <summary>The way back beside the form's button, where it offers one.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Onward? Cancel { get; init; }

    /// <summary>Who sends it, beside its button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Render.Signed? By { get; init; }

    /// <summary>Why what was sent was refused, where no one field is why, above the form.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Failure { get; init; }

    /// <summary>The sentence before the form's button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Note { get; init; }

    /// <summary>Whether the form is shown to a reader who may not send it: its fields read only, its choices disabled, no
    /// button.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool ReadOnly { get; init; }

    /// <summary>The line or button that opens the form in place, where it stands closed until someone opens it, and whether it
    /// opens already.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Opens { get; init; }

    [Newtonsoft.Json.JsonIgnore]
    public bool Open { get; init; }

    /// <summary>What a destructive form says before it is sent: it confirms in the page, never in a dialog.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Confirm { get; init; }

    /// <summary>Where what the form would change is shown before it is saved, beside its fields.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Preview { get; init; }

    /// <summary>Whether the form sends a file.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Multipart { get; init; }

    /// <summary>A form that posts to <paramref name="action"/> with these fields.</summary>
    public static Form Posting(string action, params Field[] fields) => new(action, HttpMethods.Post, fields);

    /// <summary>A form that reads <paramref name="action"/> with these fields.</summary>
    public static Form Reading(string action, params Field[] fields) => new(action, HttpMethods.Get, fields);

    /// <summary>The value the page gives the field named <paramref name="name"/>, null where it gives none.</summary>
    public string? Value(string name) => Fields.FirstOrDefault(field => field.Name == name)?.Value;

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/> by the page, which the
    /// sender sends as it is; a field the form does not hold yet is added.</summary>
    public Form Given(string name, string value)
        => this with
        {
            Fields = Fields.Any(field => field.Name == name)
                ? Fields.Select(field => field.Name == name ? field with { Value = value, As = Input.Hidden } : field).ToList()
                : [.. Fields, new Field(name, "", value, Input.Hidden)]
        };

    /// <summary>This form with the field named <paramref name="name"/> given <paramref name="value"/>.</summary>
    public Form With(string name, string? value)
        => this with { Fields = Fields.Select(field => field.Name == name ? field with { Value = value } : field).ToList() };

    /// <summary>This form with every field carried as the page gave it, as one button sends them.</summary>
    public Form AsGiven() => this with { Fields = Fields.Select(field => field with { As = Input.Hidden }).ToList() };

    /// <summary>This form with only the fields named <paramref name="names"/>.</summary>
    public Form Keeping(params string[] names) => this with { Fields = Fields.Where(field => names.Contains(field.Name)).ToList() };

    /// <summary>This form with the field named <paramref name="name"/> changed as <paramref name="change"/> says.</summary>
    public Form Changing(string name, Func<Field, Field> change)
        => this with { Fields = Fields.Select(field => field.Name == name ? change(field) : field).ToList() };

    /// <summary>The field that carries a new Post's TID, with the one minted for this form as its value.</summary>
    public static Field Minted(string name, string minted) => new(name, Holds.Id + " This one was minted for this form.", minted, Input.Hidden);
}
