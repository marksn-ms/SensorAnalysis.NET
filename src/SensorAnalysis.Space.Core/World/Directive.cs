namespace SensorAnalysis.Space.Core.World;

/// <summary>A timestamped line in the world's message / radio log.</summary>
/// <param name="Tick">World tick when the line was recorded.</param>
/// <param name="Text">Human-readable payload.</param>
public readonly record struct WorldMessage(long Tick, string Text);

/// <summary>Tick-scheduled deployment action (Space analogue of a 2D/Sphere directive).</summary>
public sealed class Directive
{
    /// <summary>Gets the tick at which this directive should fire (inclusive).</summary>
    public long AtTick { get; init; }

    /// <summary>Gets the action to perform.</summary>
    public DirectiveKind Kind { get; init; }

    /// <summary>Gets how many objects to spawn for deploy actions.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Gets a value indicating whether this directive has already run.</summary>
    public bool Applied { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        Applied
            ? $"t={AtTick} {Kind} ×{Count} (applied)"
            : $"t={AtTick} {Kind} ×{Count}";
}

/// <summary>Kinds of <see cref="Directive"/> supported by a volumetric world.</summary>
public enum DirectiveKind
{
    /// <summary>Spawn <see cref="Directive.Count"/> random-walk weaver agents at random poses.</summary>
    DeployUniformAgents,

    /// <summary>Spawn <see cref="Directive.Count"/> drifting asteroids at random poses.</summary>
    DeployAsteroids,

    /// <summary>Begin sampling coverage into the live-metrics stream (coverage is always computed).</summary>
    EnableCoverageWatcher
}
