namespace SensorAnalysis.Core.Deployment;

/// <summary>Shared optional placement constraints for all deployers.</summary>
public abstract class DeployerOptionsBase
{
    /// <summary>
    /// Minimum separation from existing agents, incidents, and objects placed in the same wave.
    /// Zero disables spacing. After <see cref="IDeployer.MaxPlacementRetries"/> failed attempts,
    /// any candidate position is accepted.
    /// </summary>
    [DeployerParam("Min distance", Required = false,
        Description = "Minimum spacing in world units (0 = disabled).")]
    public double MinDistance { get; set; }
}
