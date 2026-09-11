using Koan.Data.Core.Model;

namespace TangentSpace.Activity;

public sealed class ActivityHead : Entity<ActivityHead>
{
    public const string Key = "activity-head";
    public long LastSequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
