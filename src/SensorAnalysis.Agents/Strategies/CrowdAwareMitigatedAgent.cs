using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles skeptic: same motion as <see cref="PersonalSpaceCoopAgent"/>, but
/// only yields personal space to trusted peers, and only lowers trust for herding
/// intrusion (peer keeps closing inside personal space).
/// </summary>
public sealed class CrowdAwareMitigatedAgent : PersonalSpaceCoopAgent
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private readonly Dictionary<int, int> _offenseCount = new();
    private readonly Dictionary<int, double> _prevPeerDist = new();
    private readonly Dictionary<int, int> _intrusionTicks = new();

    /// <summary>Creates a crowd-aware mitigated agent.</summary>
    public CrowdAwareMitigatedAgent()
    {
        AgentType = "CrowdAwareMitigated";
    }

    /// <summary>Minimum mean trust to yield personal space.</summary>
    public double TrustThreshold { get; set; } = 0.38;

    /// <summary>Beta prior alpha (with PriorBeta ⇒ prior mean 0.5).</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Beta prior beta.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <summary>Base weight for a first herding offense.</summary>
    public double FirstOffenseWeight { get; set; } = 0.4;

    /// <summary>Ticks of persistent closing-after-entry before a herding offense.</summary>
    public int HerdingGraceTicks { get; set; } = 10;

    /// <summary>Mean trust toward a peer (prior if unknown).</summary>
    public double MeanTrustToward(int agentId) => MeanTrust(agentId);

    /// <summary>
    /// Tracks herding intrusions against peers, then delegates exploration to the personal-space base.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        ObserveHerdingIntrusions(observation);
        return base.Decide(observation);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<AgentInfo> NeighborsForPersonalSpace(AgentObservation observation) =>
        observation.NearbyAgents
            .Where(a => MeanTrust(a.Id) >= TrustThreshold)
            .ToList();

    private void ObserveHerdingIntrusions(AgentObservation observation)
    {
        var personal = PersonalSpaceRadius;
        var seen = new HashSet<int>();

        foreach (var peer in observation.NearbyAgents)
        {
            seen.Add(peer.Id);
            var dist = observation.DistanceTo(peer.Position);
            var prev = _prevPeerDist.GetValueOrDefault(peer.Id, dist);
            _prevPeerDist[peer.Id] = dist;

            if (dist >= personal)
            {
                _intrusionTicks[peer.Id] = 0;
                continue;
            }

            var closing = dist + 1e-6 < prev;
            if (closing)
                _intrusionTicks[peer.Id] = _intrusionTicks.GetValueOrDefault(peer.Id) + 1;
            else if (dist > prev + 0.5)
                _intrusionTicks[peer.Id] = 0;

            if (_intrusionTicks.GetValueOrDefault(peer.Id) >= HerdingGraceTicks)
            {
                RegisterOffense(peer.Id);
                _intrusionTicks[peer.Id] = 0;
            }
        }

        foreach (var id in _prevPeerDist.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                _prevPeerDist.Remove(id);
                _intrusionTicks.Remove(id);
            }
        }
    }

    private void RegisterOffense(int senderId)
    {
        var n = _offenseCount.GetValueOrDefault(senderId) + 1;
        _offenseCount[senderId] = n;
        var weight = FirstOffenseWeight * Math.Pow(2, n - 1);
        UpdateTrust(senderId, success: false, weight: weight);
    }

    private double MeanTrust(int senderId)
    {
        var t = GetTrust(senderId);
        return t.Alpha / (t.Alpha + t.Beta);
    }

    private void UpdateTrust(int senderId, bool success, double weight = 1)
    {
        var t = GetTrust(senderId);
        _trust[senderId] = success
            ? t with { Alpha = t.Alpha + weight }
            : t with { Beta = t.Beta + weight };
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

/// <summary>Factory for <see cref="CrowdAwareMitigatedAgent"/>.</summary>
public sealed class CrowdAwareMitigatedAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "CrowdAwareMitigated";

    /// <inheritdoc />
    public string Description =>
        "Personal-space coop + private trust; only herding offenses; yield only to trusted peers.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new CrowdAwareMitigatedAgent { Label = label ?? "CA" };
}
