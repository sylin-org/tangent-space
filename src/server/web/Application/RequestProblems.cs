namespace Tangent.Application;

/// <summary>A request argument rejected before any domain work; <see cref="Field"/> names the argument.</summary>
public sealed class RequestArgumentException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>The requestId already names a different action under the same participant credential.</summary>
public sealed class RequestConflictException(string requestId) : Exception
{
    public string RequestId { get; } = requestId;
}
