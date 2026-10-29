namespace Tangent.Writer;

/// <summary>What a sender receives for its command: the result, a refusal with its reason, a conflict, or the
/// failure. Only <see cref="Done"/> follows a commit; every other outcome leaves no row behind.</summary>
public abstract record Outcome<TResult>
{
    private Outcome() { }

    /// <summary>The command committed; this is its result.</summary>
    public sealed record Done(TResult Result) : Outcome<TResult>;

    /// <summary>The command was refused, for this reason.</summary>
    public sealed record Refused(string Reason) : Outcome<TResult>;

    /// <summary>The command's Id already names a row whose body differs from the one sent.</summary>
    public sealed record Conflict(string Reason) : Outcome<TResult>;

    /// <summary>The command threw, its commit failed, or the writer stopped before it ran.</summary>
    public sealed record Failed(Exception Cause) : Outcome<TResult>;
}

/// <summary>What an entity throws when it refuses a change. Its reason reaches the sender as the refused outcome.</summary>
public sealed class Refusal(string reason) : Exception(reason);
