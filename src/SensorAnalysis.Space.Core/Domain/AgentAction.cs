namespace SensorAnalysis.Space.Core.Domain;

/// <summary>Thrust (Δv) and optional look-at issued by an <see cref="Agent"/> for one tick.</summary>
/// <param name="DeltaV">Requested velocity change this tick (world units). Clamped by fuel and <see cref="Agent.MaxDeltaVPerTick"/>.</param>
/// <param name="LookAt">Optional desired forward; when null the agent keeps or derives orientation from velocity.</param>
public readonly record struct AgentAction(Vec3 DeltaV, Vec3? LookAt)
{
    /// <summary>No thrust and no orientation change.</summary>
    public static AgentAction Coast { get; } = new(Vec3.Zero, null);
}
