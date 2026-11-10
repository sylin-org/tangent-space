using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What the routes that create a Tangent, change its settings and delete it share (<see cref="TangentsController"/>):
/// their fields and the words each field takes, the forms that send them, and what a write that committed reads as.</summary>
public static class TangentWrites
{
    public const string NameField = "name";
    public const string DescriptionField = "description";
    public const string KindField = "kind";
    public const string AudienceField = "audience";
    public const string MoveField = "move";
    public const string AccentField = "accent";
    public const string ImageField = "image";
    public const string JoiningField = "joining";

    /// <summary>The settings page's segment under a Tangent's address; <see cref="Slug.Reserved"/> keeps it from every
    /// Topic.</summary>
    public const string SettingsSegment = "settings";

    /// <summary>Where an image of a person's own comes in as a Tangent's, under its settings.</summary>
    public const string ImageSegment = "image";

    public const string TopicsField = "topics";

    public static readonly Reason KindRule = Reason.Invalid(KindField, "A Tangent’s default kind is topic or article.");
    public static readonly Reason AudienceRule = Reason.Invalid(AudienceField, "A Tangent’s audience is public or private.");
    public static readonly Reason MoveRule = Reason.Invalid(MoveField, "A Tangent moves earlier or later.");

    /// <summary>The default kinds a Tangent may take, as words.</summary>
    public static readonly IReadOnlyList<string> Kinds = Enum.GetValues<PostKind>().Select(PostWrites.Word).ToList();

    /// <summary>The audiences a Tangent may have, as words.</summary>
    public static readonly IReadOnlyList<string> Audiences = Enum.GetValues<Audience>().Select(Word).ToList();

    /// <summary>The Tangents' moves, as words.</summary>
    public static readonly IReadOnlyList<string> Moves = Enum.GetValues<Move>().Select(Word).ToList();

    /// <summary>The form that creates a Tangent at <paramref name="action"/>: its name, and what else stands at its default
    /// until it is sent.</summary>
    public static Form CreateForm(string action) => Form.Posting(action,
        new Form.Field(NameField, Holds.TangentName) { Label = "Name", Limit = Tangent.NameLimit, Required = true, Placeholder = "The workshop", Focus = true },
        new Form.Field(DescriptionField, Holds.TangentDescription, As: Form.Input.Area) { Label = "Description", Limit = Tangent.DescriptionLimit, Default = "", Group = Quietly },
        new Form.Field(KindField, Holds.TangentKind, As: Form.Input.Choice) { Label = "Default kind", Choices = KindChoices, Default = PostWrites.Word(PostKind.Topic), Group = Quietly },
        new Form.Field(AudienceField, Holds.TangentAudience, As: Form.Input.Choice) { Label = "Audience", Choices = AudienceChoices, Default = Word(Audience.Public), Group = Quietly })
        with { Tool = "create-tangent", Submit = "Create", Busy = "Creating…", Island = "submitting" };

    /// <summary>The line the fields of a new Tangent other than its name sit behind, which stays true whatever is chosen.</summary>
    private const string Quietly = "Description, kind and audience";

    /// <summary>A Tangent's default kinds as a form offers them, each with what it means.</summary>
    public static readonly IReadOnlyList<Form.Choice> KindChoices =
    [
        new(PostWrites.Word(PostKind.Topic), "Discussions", "Topics are conversations, latest activity first."),
        new(PostWrites.Word(PostKind.Article), "Articles", "Each topic opens with a published piece, newest first.")
    ];

    /// <summary>A Tangent's audiences as a form offers them, each with what it means.</summary>
    public static readonly IReadOnlyList<Form.Choice> AudienceChoices =
    [
        new(Word(Audience.Public), "Public", "Everyone who reads this place reads it; members post."),
        new(Word(Audience.Private), "Private", "Only administrators, and roles given access, read it.")
    ];

    /// <summary>The form that changes the Tangent at <paramref name="action"/>, its address, each field left as it is when not
    /// sent: its audience only where it is not <paramref name="fixedAudience"/>, its place among the Tangents only where the
    /// reader <paramref name="movable"/> it.</summary>
    public static Form ChangeForm(string action, bool fixedAudience, bool movable)
    {
        List<Form.Field> fields =
        [
            new(NameField, Holds.ChangedTangentName) { Label = "Name", Limit = Tangent.NameLimit, Required = true },
            new(DescriptionField, Holds.ChangedTangentDescription, As: Form.Input.Area) { Label = "Description", Limit = Tangent.DescriptionLimit },
            new(AccentField, Holds.TangentAccent, As: Form.Input.Tone) { Label = "Its accent" },
            new(ImageField, Holds.TangentImage, As: Form.Input.Picture) { Label = "Its image, in place of its letter" },
            new(KindField, Holds.ChangedTangentKind, As: Form.Input.Choice) { Label = "Default kind", Choices = KindChoices },
            new(AudienceField, Holds.ChangedTangentAudience, As: Form.Input.Choice) { Label = "Audience", Choices = AudienceChoices }
        ];
        if (fixedAudience) fields.RemoveAll(field => field.Name == AudienceField);
        if (movable && !fixedAudience) fields.Add(new(MoveField, Holds.TangentMove, As: Form.Input.Switch));
        return new Form(action, HttpMethods.Patch, fields) { Tool = "change-tangent", Submit = "Save", Busy = "Saving…", Island = "submitting" };
    }

    /// <summary>The form that deletes the Tangent <paramref name="slug"/> under <paramref name="baseAddress"/> with the
    /// <paramref name="topics"/> it holds, which go with it.</summary>
    public static Form DeleteForm(string baseAddress, string slug, int topics) => Form.Deleting(
            PostWrites.TangentAddress(baseAddress, slug),
            new Form.Field(TopicsField, Holds.DeletedTopics, topics.ToString(System.Globalization.CultureInfo.InvariantCulture), Form.Input.Hidden))
        with { Tool = "delete-tangent", Submit = "Delete", Busy = "Deleting…" };

    public static string Word(Audience audience) => audience.ToString().ToLowerInvariant();

    public static string Word(Move move) => move.ToString().ToLowerInvariant();

    /// <summary>The word a field names among <paramref name="words"/>, by its place there; null for none, and false in
    /// <paramref name="valid"/> for a word that is not one of them.</summary>
    public static int? Choice(string? word, IReadOnlyList<string> words, out bool valid)
    {
        valid = true;
        if (string.IsNullOrEmpty(word)) return null;
        for (var index = 0; index < words.Count; index++)
        {
            if (string.Equals(words[index], word, StringComparison.OrdinalIgnoreCase)) return index;
        }

        valid = false;
        return null;
    }

    /// <summary>The value of <typeparamref name="TValue"/> a word names among <paramref name="words"/>, its values' words in
    /// their order; null for none, and refused with <paramref name="rule"/> for a word that is not one of them.</summary>
    public static TValue? Chosen<TValue>(string? word, IReadOnlyList<string> words, Reason rule) where TValue : struct, Enum
    {
        var index = Choice(word, words, out var valid);
        if (!valid) throw new Refusal(rule);
        return index is { } at ? Enum.GetValues<TValue>()[at] : null;
    }
}

/// <summary>A Tangent as it stood when it was deleted, for an agent: its slug, its name, how many Topics went with it,
/// and where the Tangents that stand are listed.</summary>
public sealed record DeletedTangent(string Slug, string Name, int Topics, string Tangents);

/// <summary>A Tangent as a write leaves it, for an agent: its slug, name, description, default kind and audience, its
/// <see cref="Order"/> for a sender who holds <c>ManageSpace</c> and so arranges the place's list, its page, its settings,
/// and what the sender <see cref="Can"/> do there.</summary>
public sealed record WrittenTangent(
    string Slug,
    string Name,
    string Description,
    string Kind,
    string Audience,
    int? Order,
    string Address,
    string Settings,
    IReadOnlyList<string> Can)
{
    /// <summary>The Tangent as it now stands for <paramref name="personId"/>, under <paramref name="baseAddress"/>: what they
    /// hold there and in the place, and its audience, each a keyed read.</summary>
    public static async Task<WrittenTangent> Of(Tangent tangent, string baseAddress, string? personId, CancellationToken ct)
    {
        var address = PostWrites.TangentAddress(baseAddress, tangent.Id);
        var capabilities = await Capabilities.Load(personId, tangent.Id, null, ct);
        var arranges = (await Capabilities.Load(personId, null, null, ct)).Holds(Bit.ManageSpace);
        var audience = await Overwrites.AudienceOf(tangent.Id, ct);
        return new WrittenTangent(tangent.Id, tangent.Name, tangent.Description, PostWrites.Word(tangent.DefaultKind),
            TangentWrites.Word(audience ?? Space.Audience.Public), arranges ? tangent.Order : null, address,
            address + "/" + TangentWrites.SettingsSegment, capabilities.Can);
    }
}
