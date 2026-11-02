using Xunit;

namespace Tangent.Commands;

/// <summary>Runs alone: its proofs fill the process's bound on tries that wait (DATA-0167), which every other try in the
/// process meets.</summary>
[CollectionDefinition(nameof(Tries), DisableParallelization = true)]
public sealed class Tries;
