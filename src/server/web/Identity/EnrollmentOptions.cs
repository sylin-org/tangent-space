namespace TangentSpace.Identity;

public sealed class EnrollmentOptions
{
    public const string Configuration = "Tangent:Enrollment";

    /// <summary>A DID that replaces the proof audience derived from Tangent:Space:PublicOrigin.</summary>
    public string ProofAudience { get; set; } = "";
}
