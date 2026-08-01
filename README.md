# SensorAnalysis

**Visualization-first** simulation platform for studying autonomous mobile sensor agents.

Designed for research workflows: watch agents interact on a world canvas, step or batch-run experiments, and export metrics/logs suitable for publication analysis.

Used by the sibling **Trust** paper repo (`../Trust`) for cooperative search, deception, and reputation experiments.

> Modern rewrite of the classic [marksn-ms/SensorAnalysis](https://github.com/marksn-ms/SensorAnalysis) WinForms platform — same goals, current .NET stack.

## Highlights

- **WPF world view** — pan, zoom, select agents/incidents, live metrics panel
- **In-process agents** — implement `Agent.Decide` / `Tick`, register via `IAgentFactory`
- **Radio messaging** — `SimWorld.Transmit` delivers to in-range **inboxes** (broadcast = empty `ToAgentId`)
- **Watchers** — coverage, connectivity, clearance, trust telemetry
- **Experiment export** — `run-meta.json` + `metrics.csv` per run
- **Headless CLI** — multi-regime batches (`--regime honest|deceptive|mitigated|…`)
- **Deterministic seeds** — fixed base seed + trial index

## Solution layout

| Project | Role |
|---------|------|
| `SensorAnalysis.Core` | World, agents, incidents, messages, intel oracle, export |
| `SensorAnalysis.Agents` | Strategies, `GaussianDeployer`, watchers |
| `SensorAnalysis.App` | WPF UI |
| `SensorAnalysis.Cli` | Headless batch runner |
| `SensorAnalysis.Core.Tests` | Unit tests |

## Quick start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (for the WPF app). Core + CLI run on any OS.

### Build & test

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

### Run the GUI

```bash
dotnet run --project src/SensorAnalysis.App
```

### Publish CLI next to the Trust paper repo

```bash
dotnet publish src/SensorAnalysis.Cli/SensorAnalysis.Cli.csproj -c Release -o ../Trust/SensorAnalysis
```

Trust overnight scripts prefer `Trust/SensorAnalysis/SensorAnalysis.Cli.exe` (the WPF `App.exe` alone cannot run headless batches).

## Headless CLI

```bash
dotnet run --project src/SensorAnalysis.Cli -c Release -- \
  --regime honest --trials 100 --ticks 1000 \
  --agents 8 --incidents 6 --adversaries 0 \
  --cols 10 --rows 10 --tile-px 40 \
  --sensor-range 80 --sigma 80 --seed 42 \
  --out ./experiments/honest
```

### Regimes

| `--regime` | Honest slots | Adversaries |
|------------|--------------|-------------|
| `random` | `RandomWalk` | — |
| `greedy` | `GreedyIncident` | — |
| `honest` | `OrbitingCoop` | — |
| `deceptive` | `OrbitingCoop` | `HerdingDeceptive` × `--adversaries` |
| `mitigated` | `MitigatedOrbit` | `HerdingDeceptive` × `--adversaries` |
| `legacy-honest` / `legacy-deceptive` / `legacy-mitigated` | Older simple agents retained for reference | |

### Useful flags

| Flag | Meaning |
|------|---------|
| `--cols` / `--rows` / `--tile-px` | World grid (default 10×10×40) |
| `--sensor-range` | Sense / default radio range |
| `--sigma` | Gaussian drop σ for **honest** agents |
| `--adversaries` | Count of infiltrators (deceptive/mitigated) |
| `--incidents-near-adversaries` | Optional stress flag (off by default; keep incident process identical across regimes) |

CLI prints an approximate disk-cover scale `n*` (sparse ≈ agents ≪ `n*`).

## Built-in agent types

### Trust-paper stack (current defaults)

| Name | Behavior |
|------|----------|
| `OrbitingCoop` | Personal-space exploration; on find/join, **orbit clockwise at r/2**; bird-in-hand join; truthful broadcasts; contributes to drain |
| `HerdingDeceptive` | Uniform-random deploy; **`IIncidentOracle`** omniscience; **no drain**; silence on truth; far decoys; herds by occupying personal space toward known events |
| `MitigatedOrbit` | Orbiting coop + Beta–Bernoulli trust; silence detection; false-lure disappointment; **authenticated disappointment gossip** (receivers accept only live `MitigatedOrbit` senders) |

### Simpler / legacy reference

| Name | Behavior |
|------|----------|
| `RandomWalk` | Random heading |
| `GreedyIncident` | Chase nearest visible incident |
| `Stigmergy` | Radio incident claims (legacy naming) |
| `CooperativeSearch` | Persist-explore + follow claims |
| `DeceptiveStigmergy` | Earlier decoy-only infiltrator |
| `TrustAwareSearch` | Earlier claim-verify trust filter |

Add your own: subclass `Agent`, override `Tick`/`Decide`, register an `IAgentFactory`.

## World mechanics (current)

- **Incidents** — intensity drained each tick by agents with `ContributesToIncidentDrain` in range. Drain scales **superlinearly**: `DrainPerAgentPerTick * covering^MultiAgentDrainExponent` (default exponent 1.5).
- **Messages** — `Transmit` enqueues inbox snapshots for in-range peers; optional short world echo for UI/metrics. Honest filter: broadcast (`ToAgentId == null`) or addressed to self; adversaries can disable filtering (eavesdrop-ready).
- **Deployment (`GaussianDeployer`)** — honest agents: Gaussian about drop point; **adversaries: uniform random**; incidents: uniform unless `--incidents-near-adversaries`.
- **Intel** — `IIncidentOracle` / `WorldIncidentOracle` injects privileged incident lists into deceptive agents without hard-coding IDs in the tick loop.

## Watchers & export

| Watcher | Columns (prefix) |
|---------|------------------|
| `Coverage` | coverage, active incidents, reward, messages |
| `Connectivity` | pair counts / degree |
| `Clearance` | fraction cleared, all-clear tick (end-of-trial sentinel `tick=-1`) |
| `TrustTel` | fraction cleared over time, mean mitigated trust vs deceptive peers, disappointment echo counts |

Export folder:

```
out/
  run-meta.json
  metrics.csv
```

## Concepts (classic → modern)

| Classic | Modern |
|---------|--------|
| `Inhabitant` | `Agent` |
| `Incident` | `Incident` |
| `Marker` / messages | `Message` + inbox `Transmit` |
| `Deployer` | `IDeployer` / `GaussianDeployer` |
| `Watcher` | `IWatcher` + `ExperimentExporter` |

## Documentation

- [docs/DeveloperGuide.md](docs/DeveloperGuide.md) — add agents, deployers, watchers
- [docs/ExperimentExport.md](docs/ExperimentExport.md) — CSV/JSON schema
- Sibling paper workflow: `../Trust/README.md`

## License

MIT (or as chosen by the project owner).
