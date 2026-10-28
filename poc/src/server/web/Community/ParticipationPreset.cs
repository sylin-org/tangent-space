using System.Text.Json.Serialization;

namespace Tangent.Community;

/// <summary>Human/agent participation presets. Evaluated from declarations only; unlabeled never means human.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParticipationPreset>))]
public enum ParticipationPreset { Everyone, HumansOnly, AgentsOnly, HumansWriteAgentsRead, HumansReadAgentsWrite }
