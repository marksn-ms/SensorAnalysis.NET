using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Boids-channel skeptic. Same motion as <see cref="BoidsCoopAgent"/>, but align/cohere
/// only with trusted peers. Offense = a close peer whose heading is anti-aligned with the
/// local flock mean for <see cref="PeelGraceTicks"/> consecutive ticks (firsthand heading,
/// peer id; never <see cref="AgentInfo.AgentType"/>). Separation still applies to everyone.
/// </summary>
/// <remarks>
/// Grace default 8 = platform HeadingChangeInterval. Not fit to ρ. If this detector is
/// noisy, that is a finding — do not retune to force C3.
/// </remarks>
public sealed class BoidsAwareMitigatedAgent : BoidsCoopAgent, IBoidsPeelTelemetry
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private readonly Dictionary<int, int> _offenseCount = new();
    private readonly Dictionary<int, int> _antiStreak = new();
    private readonly Dictionary<int, int> _lifetimeAnti = new();
    private readonly Dictionary<int, int> _currentlyAnti = new();

    /// <summary>Creates a boids-aware mitigated agent.</summary>
    public BoidsAwareMitigatedAgent()
    {
        AgentType = "BoidsAwareMitigated";
    }

    /// <summary>Minimum mean trust to use a peer for align/cohere.</summary>
    public double TrustThreshold { get; set; } = 0.38;

    /// <summary>Beta prior alpha (with PriorBeta ⇒ prior mean 0.5).</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Beta prior beta.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <summary>Base weight for a first peel offense (escalates as base·2^(n-1)).</summary>
    public double FirstOffenseWeight { get; set; } = 0.4;

    /// <inheritdoc />
    public int PeelGraceTicks { get; set; } = 8;

    /// <summary>Dot product of unit headings below this is anti-aligned (≤ 0 ⇒ ≥ 90°).</summary>
    public double AntiAlignDot { get; set; } = 0.0;

    /// <summary>A peer closer than this × sensor range is "in the flock" for the mismatch test.</summary>
    public double FlockRadiusFactor { get; set; } = 0.5;

    /// <inheritdoc />
    public double MeanTrustToward(int agentId) => MeanTrust(agentId);

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> LifetimeAntiAlignTicks => _lifetimeAnti;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> CurrentlyAntiAligned => _currentlyAnti;

    /// <inheritdoc />
    public int PeelOffenseCountToward(int targetId) => _offenseCount.GetValueOrDefault(targetId);

    /// <summary>Align/cohere only with trusted peers. Geometry + trust; never AgentType.</summary>
    protected override IReadOnlyList<AgentInfo> NeighborsForFlock(AgentObservation observation) =>
        observation.NearbyAgents.Where(a => MeanTrust(a.Id) >= TrustThreshold).ToList();

    /// <summary>Still separate from everyone (collision), including distrusted peelers.</summary>
    protected override IReadOnlyList<AgentInfo> NeighborsForSeparate(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        ObservePeelMismatches(observation);
        return base.Decide(observation);
    }

    /// <summary>
    /// Score close peers whose heading disagrees with the local flock mean.
    /// Uses <see cref="AgentInfo.Id"/> and <see cref="AgentInfo.Heading"/> only.
    /// </summary>
    private void ObservePeelMismatches(AgentObservation observation)
    {
        var flockR = SensorRange * FlockRadiusFactor;
        var close = observation.NearbyAgents
            .Where(a => observation.DistanceTo(a.Position) <= flockR)
            .ToList();

        var meanH = Vec2.Zero;
        foreach (var p in close)
            meanH += p.Heading.ToStep();
        // Include self heading so a singleton neighbor is compared to us, not to itself.
        meanH += LastAction.ToStep();

        var seen = new HashSet<int>();
        foreach (var peer in close)
        {
            seen.Add(peer.Id);
            var peerH = peer.Heading.ToStep();
            var anti = false;
            if (meanH.Length > 1e-6 && peerH.Length > 1e-6)
            {
                var mh = meanH.Normalize();
                var ph = peerH.Normalize();
                var dot = mh.X * ph.X + mh.Y * ph.Y;
                anti = dot <= AntiAlignDot;
            }

            _currentlyAnti[peer.Id] = anti ? 1 : 0;
            if (!anti)
            {
                _antiStreak[peer.Id] = 0;
                continue;
            }

            _lifetimeAnti[peer.Id] = _lifetimeAnti.GetValueOrDefault(peer.Id) + 1;
            var streak = _antiStreak.GetValueOrDefault(peer.Id) + 1;
            _antiStreak[peer.Id] = streak;
            if (streak >= PeelGraceTicks)
            {
                RegisterOffense(peer.Id);
                _antiStreak[peer.Id] = 0;
            }
        }

        foreach (var id in _antiStreak.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                _antiStreak[id] = 0;
                _currentlyAnti[id] = 0;
            }
        }
    }

    private void RegisterOffense(int senderId)
    {
        var n = _offenseCount.GetValueOrDefault(senderId) + 1;
        _offenseCount[senderId] = n;
        var weight = FirstOffenseWeight * Math.Pow(2, n - 1);
        var t = GetTrust(senderId);
        _trust[senderId] = t with { Beta = t.Beta + weight };
    }

    private double MeanTrust(int senderId)
    {
        var t = GetTrust(senderId);
        return t.Alpha / (t.Alpha + t.Beta);
    }

    private BetaTrust GetTrust(int senderId)
    {
        if (_trust.TryGetValue(senderId, out var t)) return t;
        t = new BetaTrust(PriorAlpha, PriorBeta);
        _trust[senderId] = t;
        return t;
    }

    private readonly record struct BetaTrust(double Alpha, double Beta);
}

/// <summary>Factory for <see cref="BoidsAwareMitigatedAgent"/>.</summary>
public sealed class BoidsAwareMitigatedAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "BoidsAwareMitigated";

    /// <inheritdoc />
    public string Description =>
        "Boids coop + private trust; offense = close peer anti-aligned with flock mean; align/cohere only trusted.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new BoidsAwareMitigatedAgent { Label = label ?? "BM" };
}
