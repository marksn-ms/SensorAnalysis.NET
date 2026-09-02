using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles naïve cooperator on the local-progress stimulus. No radio.
/// Seeks a sensed event; stays to cover it; leaves a station that looks full
/// (already MaxCoveringOthers peers on it) <em>and</em> is actually resolving
/// (cue <see cref="IncidentInfo.Intensity"/> shrank over <see cref="ProgressGraceTicks"/>).
/// Full stations that are not shrinking are treated as fake coverage and still worked.
/// </summary>
/// <remarks>
/// Observation model: <c>NearbyIncidents.Intensity</c> is the remaining event
/// intensity on a successful local cue (range-limited, position-noisy when cue
/// sensing is on, detection is stochastic). Cue <c>Id</c> is 0, so tracks are
/// associated by proximity, not a world incident id. Not a GPS-global map.
/// <para>
/// Grace window default: <see cref="ProgressGraceTicks"/> = 15. That is half an
/// incident wave (<c>period=30</c>) — long enough to see ~0.15 of solo drain
/// (platform default <c>DrainPerAgentPerTick=0.01</c>) and several cue hits even
/// at p≈0.4, short enough to decide before the next spawn. Chosen from world
/// timescales, not from ρ. Do not retune after seeing ρ to force SUPPORTS.
/// </para>
/// </remarks>
public class ProgressCoopAgent : Agent
{
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    private readonly List<StationMemory> _stations = new();

    /// <summary>Creates a progress-seeking cooperative agent.</summary>
    public ProgressCoopAgent()
    {
        AgentType = "ProgressCoop";
        Velocity = 5;
        ContributesToIncidentDrain = true;
    }

    /// <summary>
    /// Ticks of local intensity history before a station is called shrinking.
    /// Default 15 = half the campaign incident period (30).
    /// </summary>
    public int ProgressGraceTicks { get; set; } = 15;

    /// <summary>
    /// Peers on-station at or above this count make the station "full"
    /// (aligned with drain cap / MaxOrbiters: 2 others ⇒ 3 covering).
    /// </summary>
    public int MaxCoveringOthers { get; set; } = 2;

    /// <summary>Match a new cue to an existing local track within this × sensor range.</summary>
    public double AssociationRadiusFactor { get; set; } = 0.35;

    /// <summary>On-station radius (half sensor range), same geometric disc as orbit/personal space.</summary>
    protected double StationRadius => SensorRange * 0.5;

    /// <summary>Sit and cover once this close to the perceived cue.</summary>
    protected double SitRadius => SensorRange * 0.35;

    /// <summary>Association radius for cue tracks.</summary>
    protected double AssociationRadius => SensorRange * AssociationRadiusFactor;

    /// <summary>
    /// Uses local cue intensity history to stay on stalled/fake-full stations and
    /// leave stations that are both full and actually resolving.
    /// Ignores radio (<see cref="AgentObservation.NearbyMessages"/>).
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        UpdateProgressMemory(observation);

        if (observation.NearbyIncidents.Count == 0)
            return Explore(observation);

        IncidentInfo? chosen = null;
        var bestDist = double.PositiveInfinity;
        foreach (var cue in observation.NearbyIncidents)
        {
            var dist = observation.DistanceTo(cue.Position);
            var others = CountCoveringOthers(observation, cue.Position);
            var shrinking = IsStationShrinking(observation, cue.Position);
            var full = others >= MaxCoveringOthers;
            // Full AND resolving → don't pile on. Sparse, or full-but-stalled → work it.
            if (full && shrinking)
                continue;
            if (dist < bestDist)
            {
                bestDist = dist;
                chosen = cue;
            }
        }

        if (chosen is null)
            return Explore(observation);

        if (bestDist <= SitRadius)
            return AgentAction.Stay;
        return Steering.DirectionToward(observation.DisplacementTo(chosen.Value.Position));
    }

    /// <summary>Peers that count as covering for the full-station test (all peers for naïve).</summary>
    protected virtual IReadOnlyList<AgentInfo> NeighborsForCovering(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <summary>Count covering neighbors within <see cref="StationRadius"/> of a cue. Does not read AgentType.</summary>
    protected int CountCoveringOthers(AgentObservation observation, Vec2 cueRelative)
    {
        var r = StationRadius;
        var n = 0;
        foreach (var a in NeighborsForCovering(observation))
        {
            if (observation.DistanceBetween(a.Position, cueRelative) <= r)
                n++;
        }
        return n;
    }

    /// <summary>True when the associated local track completed a grace window with Intensity drop.</summary>
    protected bool IsStationShrinking(AgentObservation observation, Vec2 cueRelative)
    {
        var mem = FindStation(observation, cueRelative);
        return mem is { LastWindowShrunk: true };
    }

    /// <summary>Fold this tick's range-limited cues into local intensity tracks. No radio, no AgentType.</summary>
    protected void UpdateProgressMemory(AgentObservation observation)
    {
        var seen = new HashSet<StationMemory>();
        foreach (var cue in observation.NearbyIncidents)
        {
            var abs = observation.AbsoluteFromRelative(cue.Position);
            var mem = FindStation(observation, cue.Position);
            if (mem is null)
            {
                mem = new StationMemory
                {
                    Absolute = abs,
                    IntensityAtWindowStart = cue.Intensity,
                    WindowStartTick = observation.Tick,
                    LastIntensity = cue.Intensity,
                    LastSampleTick = observation.Tick,
                    LastWindowShrunk = false
                };
                _stations.Add(mem);
            }
            else
            {
                mem.Absolute = abs;
                mem.LastIntensity = cue.Intensity;
                mem.LastSampleTick = observation.Tick;
                if (observation.Tick - mem.WindowStartTick >= ProgressGraceTicks)
                {
                    var dI = mem.IntensityAtWindowStart - cue.Intensity;
                    mem.LastWindowShrunk = dI > 1e-4;
                    mem.IntensityAtWindowStart = cue.Intensity;
                    mem.WindowStartTick = observation.Tick;
                }
            }

            seen.Add(mem);
            OnStationObserved(observation, cue, mem);
        }

        var staleAfter = ProgressGraceTicks * 2;
        _stations.RemoveAll(s => !seen.Contains(s) && observation.Tick - s.LastSampleTick > staleAfter);
    }

    /// <summary>Hook after a cue is folded into memory (skeptic scores co-present peers here).</summary>
    protected virtual void OnStationObserved(AgentObservation observation, IncidentInfo cue, StationMemory mem)
    {
    }

    /// <summary>Nearest local track to this observer-relative cue, or null.</summary>
    protected StationMemory? FindStation(AgentObservation observation, Vec2 cueRelative)
    {
        StationMemory? best = null;
        var bestD = double.PositiveInfinity;
        var r = AssociationRadius;
        foreach (var s in _stations)
        {
            var rel = observation.RelativeFromAbsolute(s.Absolute);
            var d = observation.DistanceBetween(rel, cueRelative);
            if (d <= r && d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>Persistent random explore. No personal-space channel (this stack is progress, not P).</summary>
    protected AgentAction Explore(AgentObservation observation)
    {
        if (_explorePersist <= 0 || _exploreHeading == AgentAction.Stay)
        {
            _exploreHeading = Steering.RandomAction(Rng);
            if (_exploreHeading == AgentAction.Stay)
                _exploreHeading = AgentAction.East;
            _explorePersist = HeadingChangeInterval;
        }
        _explorePersist--;
        return _exploreHeading;
    }

    /// <summary>Local, id-less intensity track for one perceived station.</summary>
    protected sealed class StationMemory
    {
        /// <summary>Smoothed absolute cue position.</summary>
        public Vec2 Absolute;
        /// <summary>Intensity at the start of the current grace window.</summary>
        public double IntensityAtWindowStart;
        /// <summary>Tick when the current grace window opened.</summary>
        public long WindowStartTick;
        /// <summary>Most recent sampled intensity.</summary>
        public double LastIntensity;
        /// <summary>Tick of <see cref="LastIntensity"/>.</summary>
        public long LastSampleTick;
        /// <summary>True if the last completed window showed an intensity drop.</summary>
        public bool LastWindowShrunk;
    }
}

/// <summary>Factory for <see cref="ProgressCoopAgent"/>.</summary>
public sealed class ProgressCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "ProgressCoop";

    /// <inheritdoc />
    public string Description =>
        "No radio: seek a sensed event; leave full stations that are actually shrinking; work stalled ones.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new ProgressCoopAgent { Label = label ?? "PC" };
}
