using System.Collections.Concurrent;

namespace SensorAnalysis.App.Visualization;

/// <summary>
/// Maps agent / incident type names to marker styles.
/// Built-in types have curated muted colors; unknown types get a stable hash-based hue
/// so new factories appear distinct without hard-coding regimes into the canvas.
/// </summary>
public sealed class EntityVisualCatalog
{
    private readonly ConcurrentDictionary<string, EntityVisualStyle> _agentStyles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EntityVisualStyle> _incidentStyles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shared default catalog used by the main window canvas.</summary>
    public static EntityVisualCatalog Default { get; } = CreateDefault();

    /// <summary>Registers or replaces the visual style for an agent type name.</summary>
    /// <param name="style">Style whose <see cref="EntityVisualStyle.TypeKey"/> is the agent type.</param>
    public void RegisterAgent(EntityVisualStyle style) =>
        _agentStyles[style.TypeKey] = style;

    /// <summary>Registers or replaces the visual style for an incident type name.</summary>
    /// <param name="style">Style whose <see cref="EntityVisualStyle.TypeKey"/> is the incident type.</param>
    public void RegisterIncident(EntityVisualStyle style) =>
        _incidentStyles[style.TypeKey] = style;

    /// <summary>Resolves a style for an agent type, synthesizing one if unregistered.</summary>
    /// <param name="agentType">Value of <see cref="Core.Domain.Agent.AgentType"/>.</param>
    /// <returns>A concrete style suitable for drawing.</returns>
    public EntityVisualStyle ResolveAgent(string agentType)
    {
        var key = string.IsNullOrWhiteSpace(agentType) ? "Agent" : agentType;
        return _agentStyles.GetOrAdd(key, static k => Synthesize(k, EntityMarkerShape.Circle, radius: 5));
    }

    /// <summary>Resolves a style for an incident type, synthesizing one if unregistered.</summary>
    /// <param name="incidentType">Value of <see cref="Core.Domain.Incident.IncidentType"/>.</param>
    /// <returns>A concrete style suitable for drawing.</returns>
    public EntityVisualStyle ResolveIncident(string incidentType)
    {
        var key = string.IsNullOrWhiteSpace(incidentType) ? "Incident" : incidentType;
        return _incidentStyles.GetOrAdd(key, static k => Synthesize(k, EntityMarkerShape.Triangle, radius: 6.5));
    }

    /// <summary>Creates the built-in palette for known agent and incident types.</summary>
    public static EntityVisualCatalog CreateDefault()
    {
        var c = new EntityVisualCatalog();

        // Agents — circles for cooperative / baseline; diamonds for deceptive / infiltrator cues.
        c.RegisterAgent(new("RandomWalk", EntityMarkerShape.Circle, 140, 150, 165));
        c.RegisterAgent(new("GreedyIncident", EntityMarkerShape.Circle, 70, 175, 160));
        c.RegisterAgent(new("Stigmergy", EntityMarkerShape.Circle, 90, 170, 90));
        c.RegisterAgent(new("CooperativeSearch", EntityMarkerShape.Circle, 80, 140, 220));
        c.RegisterAgent(new("OrbitingCoop", EntityMarkerShape.Circle, 70, 165, 235));
        c.RegisterAgent(new("MitigatedOrbit", EntityMarkerShape.Circle, 60, 200, 200));
        c.RegisterAgent(new("TrustAwareSearch", EntityMarkerShape.Circle, 150, 110, 210));
        c.RegisterAgent(new("DeceptiveStigmergy", EntityMarkerShape.Diamond, 220, 140, 70));
        c.RegisterAgent(new("HerdingDeceptive", EntityMarkerShape.Diamond, 210, 70, 90));
        c.RegisterAgent(new("HerdingDeceptiveOracle", EntityMarkerShape.Diamond, 180, 55, 120));

        // Incidents — triangles by kind (extensible when deployers set IncidentType).
        c.RegisterIncident(new("Incident", EntityMarkerShape.Triangle, 235, 110, 85, 6.5));
        c.RegisterIncident(new("Hotspot", EntityMarkerShape.Triangle, 235, 180, 70, 6.5));
        c.RegisterIncident(new("Distress", EntityMarkerShape.Triangle, 200, 80, 160, 6.5));

        return c;
    }

    private static EntityVisualStyle Synthesize(string key, EntityMarkerShape shape, double radius)
    {
        // Stable pastel-ish HSL from type name — distinct without neon clutter.
        var hash = unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key));
        var hue = (hash % 360) / 360.0;
        var (r, g, b) = HslToRgb(hue, 0.45, 0.55);
        return new EntityVisualStyle(key, shape, r, g, b, radius);
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double Q(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        double r, g, b;
        if (s < 1e-9)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Q(p, q, h + 1.0 / 3);
            g = Q(p, q, h);
            b = Q(p, q, h - 1.0 / 3);
        }

        return ((byte)Math.Clamp((int)(r * 255), 0, 255),
            (byte)Math.Clamp((int)(g * 255), 0, 255),
            (byte)Math.Clamp((int)(b * 255), 0, 255));
    }
}
