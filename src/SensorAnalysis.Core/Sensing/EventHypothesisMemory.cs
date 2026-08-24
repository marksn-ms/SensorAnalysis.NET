using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Sensing;

/// <summary>
/// Agent-side list of suspected events. Cues and rumors associate by proximity;
/// unmatched reports open a new track. Confidence decays when a track is not
/// reconfirmed, so a one-tick miss does not wipe a belief, and a never-confirmed
/// rumor eventually returns the agent to search.
/// </summary>
public sealed class EventHypothesisMemory
{
    private readonly List<EventHypothesis> _items = new();
    private int _nextLocalId = 1;

    /// <summary>Match a new cue/rumor to an existing track within this toroidal distance.</summary>
    public double AssociationRadius { get; set; } = 28;

    /// <summary>Noisy-OR bump scale for a sensor cue (multiplied by cue strength).</summary>
    public double CueBumpScale { get; set; } = 0.45;

    /// <summary>Confidence added by a single rumor (before trust scaling).</summary>
    public double RumorBump { get; set; } = 0.32;

    /// <summary>Per-tick multiplicative decay when a track received no evidence this tick.</summary>
    public double DecayPerTick { get; set; } = 0.06;

    /// <summary>Drop tracks below this confidence after they have gone stale.</summary>
    public double DropBelow { get; set; } = 0.10;

    /// <summary>Ticks a stale track may linger below <see cref="DropBelow"/> before removal.</summary>
    public int StaleGraceTicks { get; set; } = 6;

    /// <summary>Steer toward a track at or above this confidence.</summary>
    public double SuspectThreshold { get; set; } = 0.25;

    /// <summary>Orbit a track at or above this confidence (or when already close).</summary>
    public double OrbitThreshold { get; set; } = 0.42;

    /// <summary>Cap on simultaneous tracks.</summary>
    public int MaxTracks { get; set; } = 8;

    /// <summary>Current hypotheses, strongest first after <see cref="TickAge"/>.</summary>
    public IReadOnlyList<EventHypothesis> Items => _items;

    /// <summary>Look up a local track, or <c>null</c> if it was pruned.</summary>
    public EventHypothesis? Get(int localId) =>
        _items.FirstOrDefault(h => h.LocalId == localId);

    /// <summary>Strongest remaining hypothesis, or <c>null</c>.</summary>
    public EventHypothesis? Best() =>
        _items.Count == 0 ? null : _items.OrderByDescending(h => h.Confidence).First();

    /// <summary>Age every track one tick (call before ingesting this tick's evidence).</summary>
    public void TickAge()
    {
        foreach (var h in _items)
            h.TicksSinceEvidence++;
    }

    /// <summary>Incorporate a local sensor cue.</summary>
    public EventHypothesis IngestCue(Vec2 perceived, double strength, double worldWidth, double worldHeight)
    {
        var bump = Math.Clamp(strength, 0, 1) * CueBumpScale;
        return Upsert(perceived, bump, HypothesisSource.Sensor, informantId: null, worldWidth, worldHeight);
    }

    /// <summary>Incorporate a radio claim as a rumor (maybe true, maybe not).</summary>
    public EventHypothesis IngestRumor(
        Vec2 claimed,
        int? fromAgentId,
        double worldWidth,
        double worldHeight,
        double trustWeight = 1)
    {
        var bump = RumorBump * Math.Clamp(trustWeight, 0, 1);
        return Upsert(claimed, bump, HypothesisSource.Rumor, fromAgentId, worldWidth, worldHeight);
    }

    /// <summary>Decay tracks that got no evidence this tick and drop dead ones.</summary>
    public void DecayAndPrune()
    {
        foreach (var h in _items)
        {
            if (h.TicksSinceEvidence <= 0)
                continue;
            h.Confidence *= 1.0 - DecayPerTick;
        }

        _items.RemoveAll(h =>
            h.Confidence < DropBelow && h.TicksSinceEvidence >= StaleGraceTicks);

        if (_items.Count > MaxTracks)
        {
            foreach (var extra in _items.OrderBy(h => h.Confidence).Take(_items.Count - MaxTracks).ToList())
                _items.Remove(extra);
        }
    }

    private EventHypothesis Upsert(
        Vec2 sample,
        double bump,
        HypothesisSource source,
        int? informantId,
        double worldWidth,
        double worldHeight)
    {
        var match = FindNearest(sample, worldWidth, worldHeight);
        if (match is null)
        {
            match = new EventHypothesis
            {
                LocalId = _nextLocalId++,
                Estimate = sample,
                Confidence = 0,
                TicksSinceEvidence = 0,
                LastSource = source,
                LastInformantId = informantId
            };
            _items.Add(match);
        }
        else
        {
            var alpha = Math.Clamp(0.2 + bump, 0.2, 0.65);
            match.Estimate = BlendToroidal(match.Estimate, sample, alpha, worldWidth, worldHeight);
        }

        match.Confidence = 1.0 - (1.0 - match.Confidence) * (1.0 - Math.Clamp(bump, 0, 1));
        match.TicksSinceEvidence = 0;
        match.LastSource = source;
        match.LastInformantId = informantId;
        return match;
    }

    private EventHypothesis? FindNearest(Vec2 sample, double worldWidth, double worldHeight)
    {
        EventHypothesis? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var h in _items)
        {
            var d = Vec2.ToroidalDistance(h.Estimate, sample, worldWidth, worldHeight);
            if (d <= AssociationRadius && d < bestD)
            {
                bestD = d;
                best = h;
            }
        }

        return best;
    }

    private static Vec2 BlendToroidal(Vec2 current, Vec2 sample, double alpha, double w, double h)
    {
        var delta = Vec2.ToroidalDisplacement(current, sample, w, h);
        return Vec2.Wrap(current + delta * alpha, w, h);
    }
}
