using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles skeptic on the local-progress stimulus. Same motion as
/// <see cref="ProgressCoopAgent"/>, but only counts trusted peers as covering,
/// and lowers private Beta when a peer is co-present at a station whose cue
/// intensity does not drop by more than a one-drainer prior over the grace window
/// ("present with me, no extra local shrink").
/// Does not read <see cref="AgentInfo.AgentType"/> or <c>ContributesToIncidentDrain</c>.
/// No radio.
/// </summary>
/// <remarks>
/// Extra-shrink test: over <see cref="ProgressCoopAgent.ProgressGraceTicks"/> of
/// co-presence, if observed ΔIntensity is at most
/// <c>DrainPriorPerTick × grace × SoloSlack</c> (default 0.01 × 15 × 1.15 = 0.1725),
/// the shrink is consistent with at most one drainer (the observer) so co-present
/// peers did not add coverage. The 0.01 prior matches the platform incident default
/// <c>DrainPerAgentPerTick</c>; 1.15 is cue-miss slack. Not fit to ρ.
/// </remarks>
public sealed class ProgressAwareMitigatedAgent : ProgressCoopAgent, IProgressStallTelemetry
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private readonly Dictionary<int, int> _offenseCount = new();
    private readonly Dictionary<int, DisreputeBucket> _disreputeToward = new();
    private readonly Dictionary<int, PeerWindow> _peerWindow = new();
    private readonly Dictionary<int, int> _lifetimeCopresent = new();
    private readonly Dictionary<int, int> _currentlyOnStation = new();
    private readonly Dictionary<int, int> _currentCopresent = new();
    private double _lastIntensity = double.NaN;
    private int _lifetimeOffenseWindows;

    /// <summary>Creates a progress-aware mitigated agent.</summary>
    public ProgressAwareMitigatedAgent()
    {
        AgentType = "ProgressAwareMitigated";
    }

    /// <summary>Minimum mean trust to count a peer as covering.</summary>
    public double TrustThreshold { get; set; } = 0.38;

    /// <summary>Beta prior alpha (with PriorBeta ⇒ prior mean 0.5).</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Beta prior beta.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <summary>Base weight for a first progress offense (escalates as base·2^(n-1)).</summary>
    public double FirstOffenseWeight { get; set; } = 0.4;

    /// <summary>
    /// Prior intensity drop per tick for a single covering agent.
    /// Matches <c>Incident.DrainPerAgentPerTick</c> default; not a ρ knob.
    /// </summary>
    public double DrainPriorPerTick { get; set; } = 0.01;

    /// <summary>
    /// Slack on the one-drainer prior so cue misses do not forgive a solo-rate stall.
    /// Default 1.15. Not fit to ρ.
    /// </summary>
    public double SoloSlack { get; set; } = 1.15;

    /// <summary>Mean trust toward a peer (prior if unknown).</summary>
    public double MeanTrustToward(int agentId) => MeanTrust(agentId);

    /// <summary>Penalty buckets toward each peer (progress channel only).</summary>
    public IReadOnlyDictionary<int, DisreputeBucket> DisreputeToward => _disreputeToward;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> CurrentCopresentTicks => _currentCopresent;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> LifetimeCopresentTicks => _lifetimeCopresent;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> CurrentlyOnStation => _currentlyOnStation;

    /// <inheritdoc />
    public int ProgressOffenseCountToward(int targetId) =>
        _disreputeToward.TryGetValue(targetId, out var b) ? b.Progress : 0;

    /// <inheritdoc />
    public double LastSampledIntensity => _lastIntensity;

    /// <inheritdoc />
    public int LifetimeProgressOffenseWindows => _lifetimeOffenseWindows;

    /// <summary>Only trusted peers count as covering (distrusted loiterers do not make a station "full").</summary>
    protected override IReadOnlyList<AgentInfo> NeighborsForCovering(AgentObservation observation) =>
        observation.NearbyAgents.Where(a => MeanTrust(a.Id) >= TrustThreshold).ToList();

    /// <inheritdoc />
    protected override void OnStationObserved(AgentObservation observation, IncidentInfo cue, StationMemory mem)
    {
        _lastIntensity = cue.Intensity;
        ObserveProgressStalls(observation, cue);
    }

    /// <summary>
    /// Score co-present peers at this cue. Uses peer <see cref="AgentInfo.Id"/> and geometry
    /// plus local intensity; never AgentType or drain flags.
    /// </summary>
    private void ObserveProgressStalls(AgentObservation observation, IncidentInfo cue)
    {
        var r = StationRadius;
        var seen = new HashSet<int>();
        var soloCap = DrainPriorPerTick * ProgressGraceTicks * SoloSlack;

        foreach (var peer in observation.NearbyAgents)
        {
            // Geometry only — AgentType is available on AgentInfo and is not consulted.
            if (observation.DistanceBetween(peer.Position, cue.Position) > r)
            {
                _currentlyOnStation[peer.Id] = 0;
                _currentCopresent[peer.Id] = 0;
                _peerWindow.Remove(peer.Id);
                continue;
            }

            seen.Add(peer.Id);
            _currentlyOnStation[peer.Id] = 1;
            _lifetimeCopresent[peer.Id] = _lifetimeCopresent.GetValueOrDefault(peer.Id) + 1;

            if (!_peerWindow.TryGetValue(peer.Id, out var win))
            {
                win = new PeerWindow(observation.Tick, cue.Intensity, 0);
            }

            win.CopresentTicks++;
            _currentCopresent[peer.Id] = win.CopresentTicks;
            _peerWindow[peer.Id] = win;

            if (win.CopresentTicks < ProgressGraceTicks)
                continue;
            if (observation.Tick - win.StartTick < ProgressGraceTicks)
                continue;

            var dI = win.IntensityAtStart - cue.Intensity;
            if (dI <= soloCap)
            {
                RegisterOffense(peer.Id);
                _lifetimeOffenseWindows++;
            }

            _peerWindow[peer.Id] = new PeerWindow(observation.Tick, cue.Intensity, 0);
            _currentCopresent[peer.Id] = 0;
        }

        foreach (var id in _peerWindow.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                _peerWindow.Remove(id);
                _currentCopresent[id] = 0;
                _currentlyOnStation[id] = 0;
            }
        }
    }

    private void RegisterOffense(int senderId)
    {
        var n = _offenseCount.GetValueOrDefault(senderId) + 1;
        _offenseCount[senderId] = n;
        var weight = FirstOffenseWeight * Math.Pow(2, n - 1);
        UpdateTrust(senderId, success: false, weight: weight);
        _disreputeToward.TryGetValue(senderId, out var bucket);
        _disreputeToward[senderId] = bucket.Add(ReputationOffenseKind.Progress, weight);
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

    private struct PeerWindow
    {
        public long StartTick;
        public double IntensityAtStart;
        public int CopresentTicks;

        public PeerWindow(long startTick, double intensityAtStart, int copresentTicks)
        {
            StartTick = startTick;
            IntensityAtStart = intensityAtStart;
            CopresentTicks = copresentTicks;
        }
    }
}

/// <summary>Factory for <see cref="ProgressAwareMitigatedAgent"/>.</summary>
public sealed class ProgressAwareMitigatedAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "ProgressAwareMitigated";

    /// <inheritdoc />
    public string Description =>
        "Progress coop + private trust; offense = co-present with no extra local shrink; count only trusted as covering.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new ProgressAwareMitigatedAgent { Label = label ?? "PM" };
}
