using System.Text.Json.Serialization;

namespace TangentSpace.Participants;

/// <summary>An explicit, owner-reviewed declaration. Undeclared is a distinct state and is never treated as human.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParticipantClassification>))]
public enum ParticipantClassification { Undeclared, Human, Agent }
