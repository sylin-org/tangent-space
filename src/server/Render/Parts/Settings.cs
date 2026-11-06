namespace Tangent.Render;

/// <summary>The sections of the place's settings the reader may use, the one the page shows marked.</summary>
public sealed record SettingsRail(IReadOnlyList<Space.SettingsSection> Sections) : IPart;

/// <summary>Why a page's form is not the reader's to send, or why what they sent was refused, in one line above it: a gate's
/// sentence, with the way to sign in for a visitor, or a refusal's, as a <see cref="Render.Sentence"/> with its links.</summary>
public sealed record Notice(string Sentence, string? Gate = null, string? SignIn = null) : IPart
{
    /// <summary>The line for a page's <paramref name="gate"/>, else its <paramref name="failure"/>, each read as written; none
    /// where it has neither.</summary>
    public static Notice? Of(string? gate, string? failure, string? signIn)
        => gate is not null ? new Notice(Render.Sentence.Plain(Access.Capabilities.Sentence(gate)), gate, signIn)
            : failure is not null ? new Notice(Render.Sentence.Plain(failure)) : null;
}
