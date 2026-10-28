using System.Text.Json.Serialization;

namespace Tangent.Activity;

/// <summary>Personal interest only: never affects admission, access, or read positions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WatchMode>))]
public enum WatchMode { All, Replies, None }
