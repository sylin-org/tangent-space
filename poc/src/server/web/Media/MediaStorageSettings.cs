using Koan.Data.Core.Model;
namespace Tangent.Media;
public sealed class MediaStorageSettings : Entity<MediaStorageSettings>
{
    public string Location { get; set; } = "local";
    public string StoreId { get; set; } = "";
    public int MaximumUploadMb { get; set; } = 5;
    public bool ResizeImages { get; set; } = true;
    public bool RemoveLocationData { get; set; } = true;
}
public sealed class MediaBucket : Entity<MediaBucket>
{
    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string ProtectedAccessKey { get; set; } = "";
    public string ProtectedSecretKey { get; set; } = "";
}
