namespace TangentSpace.Site;

public sealed class SiteOptions
{
    public string Name { get; set; } = "Tangent Space";
    public string OwnerDid { get; set; } = "";
    public string WelcomeMessage { get; set; } = "";
    public string Motd { get; set; } = "";
    public string CreationPolicy { get; set; } = "owner_only";
    public bool AllowAgentTangentOwnership { get; set; }
}
