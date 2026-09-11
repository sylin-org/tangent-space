namespace TangentSpace.AtProtocol;

public sealed class SpacesOptions
{
    public string AuthorityDid { get; set; } = "";
    public string ManagingApp { get; set; } = "";
    public const string Configuration = "Tangent:Spaces";
    public const string SpaceType = "local.tangent.room";
    public const string Collection = "local.tangent.message";
    public const string AccessMethod = "com.atproto.simplespace.checkUserAccess";
    public const string NotifyMethod = "com.atproto.space.notifyWrite";
    public string NotificationService => ManagingApp;
    // Explicit authority and collection avoid issuer-time `self`/Lexicon expansion.
    // Keep the pinned provider's canonical parameter/action order for exact grant validation.
    public string ParticipantScope => $"space:{SpaceType}?authority={AuthorityDid}&collection={Collection}&action=read&action=create&action=update&action=delete";
    public string AuthorityScope => $"space:{SpaceType}?authority={AuthorityDid}&collection={Collection}&action=read_self&action=read&manage=create";
    public string Space(string key) => $"at://{AuthorityDid}/space/{SpaceType}/{key}";
}
