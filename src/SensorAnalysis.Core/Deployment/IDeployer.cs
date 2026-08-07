using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// First-class world citizen that can place entities and optionally keep acting each tick.
/// Immediate deployers place once in <see cref="Attach"/> then become dormant;
/// periodic deployers keep spawning on their schedule from <see cref="Tick"/>.
/// Configuration is an options object whose public properties (marked with
/// <see cref="DeployerParamAttribute"/>) drive the UI form.
/// </summary>
public interface IDeployer
{
    /// <summary>Unique name for UI/CLI selection.</summary>
    string Name { get; }

    /// <summary>Short human-readable description.</summary>
    string Description { get; }

    /// <summary>
    /// When <c>true</c>, <see cref="Tick"/> is a no-op.
    /// Immediate deployers set this after their one-shot placement.
    /// </summary>
    bool IsDormant { get; }

    /// <summary>CLR type of the options object returned by <see cref="CreateOptions"/>.</summary>
    Type OptionsType { get; }

    /// <summary>Creates a fresh options instance with defaults.</summary>
    object CreateOptions();

    /// <summary>
    /// Bind to a world using a typed options instance.
    /// Immediate deployers perform placement here; periodic ones store config for <see cref="Tick"/>.
    /// </summary>
    /// <param name="world">World to populate / observe.</param>
    /// <param name="options">Deployer-specific options (<see cref="OptionsType"/>).</param>
    /// <param name="context">Agent resolution and RNG salt.</param>
    void Attach(SimWorld world, object options, DeployContext context);

    /// <summary>
    /// Called by <see cref="SimWorld.Tick"/> before agents act.
    /// </summary>
    /// <param name="ctx">Current world context.</param>
    void Tick(WorldContext ctx);
}

/// <summary>
/// Options for <see cref="PeriodicIncidentDeployer"/> (uniform incident waves).
/// </summary>
public sealed class PeriodicIncidentOptions
{
    /// <summary>Ticks between waves (including a wave at tick 0).</summary>
    [DeployerParam("Period (ticks)", Description = "Spawn on ticks 0, N, 2N, …")]
    public int PeriodTicks { get; set; } = 25;

    /// <summary>Incidents created each wave.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Logical incident kind for styling / filtering (default <c>Incident</c>).</summary>
    [DeployerParam("Incident type", IsIncidentType = true, Required = false)]
    public string IncidentType { get; set; } = "Incident";
}
