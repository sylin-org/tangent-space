using System.Text.Json.Serialization;

namespace Tangent.Community;

/// <summary>Effective admission for self-join. Open joins immediately; Approval queues a durable request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TangentAdmission>))]
public enum TangentAdmission { Open, Approval }
