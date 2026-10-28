namespace Tangent.Identity;

public sealed class EnrollmentOptions
{
    public const string Configuration = "Tangent:Enrollment";

    /// <summary>The service DID; the exchange audience appends the exact #tangent fragment.</summary>
    public string ProofAudience { get; set; } = "";
}
