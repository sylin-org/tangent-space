namespace Tangent.Community;

public sealed class TangentRuleViolation(TangentDenial denial, string message) : InvalidOperationException(message)
{
    public TangentDenial Denial { get; } = denial;
}
