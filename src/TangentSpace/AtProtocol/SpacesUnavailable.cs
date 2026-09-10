namespace TangentSpace.AtProtocol;

public sealed class SpacesUnavailable(string stage, string code, int status = 503) : Exception("The account provider could not complete this operation.")
{
    public string Stage { get; } = stage;
    public string Code { get; } = code;
    public int Status { get; } = status;
}
