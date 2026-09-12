namespace TangentSpace.Conversation;

/// <summary>Read-time label resolution for one participant (ADR 0008): facets bind the perennial
/// identity value; this carries that value plus the fresh label and classification resolved at
/// serve time, so composers can mint reply-mention facets from a page's own payload.</summary>
public sealed record ParticipantResolution(string? Handle, string? DisplayName, string Classification, string? Value = null,
    string? Avatar = null, string? ProfileUrl = null);
