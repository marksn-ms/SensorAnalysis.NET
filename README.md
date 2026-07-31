# SensorAnalysis

**Visualization-first** simulation platform for studying autonomous mobile sensor agents.

Designed for research workflows: watch agents interact on a world canvas, step or batch-run experiments, and export metrics/logs suitable for publication analysis.

> Modern rewrite of the classic [marksn-ms/SensorAnalysis](https://github.com/marksn-ms/SensorAnalysis) WinForms platform — same goals, current .NET stack.

## Highlights

- **WPF world view** — pan, zoom, select agents/incidents, live metrics panel
- **In-process agents** — implement `Agent.Decide`, register via `IAgentFactory` (no RPC required)
- **Watchers** — sample coverage, connectivity, rewards every tick
- **Experiment export** — `run-meta.json` + `metrics.csv` per run (ready for R / Python / Excel)
- **Headless CLI** — batch trials without opening the GUI
- **Deterministic** — fixed seed → reproducible trajectories

## Solution layout

| Project | Role |
|---------|------|
| `SensorAnalysis.Core` | World, agents, incidents, messages, watchers, export |
| `SensorAnalysis.Agents` | Built-in strategies, deployers, extra watchers |
| `SensorAnalysis.App` | WPF UI (primary surface) |
| `SensorAnalysis.Cli` | Headless batch runner |
| `SensorAnalysis.Core.Tests` | Unit tests (determinism, sensing, export) |

## Quick start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (for the WPF app). Core + CLI run on any OS.

### Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

### Run the GUI

```bash
dotnet run --project src/SensorAnalysis.App
```

1. **Deploy** — set agent count / type / incidents, click Deploy  
2. **Step** or **Run** — advance the simulation; watch the canvas  
3. Click an agent to see sensor range and properties  
4. **File → Export Metrics** or **Simulation → Run Experiment** for CSV + JSON  

### Headless batch (publication data)

```bash
dotnet run --project src/SensorAnalysis.Cli -- \
  --trials 10 --ticks 500 --agents 20 --incidents 8 \
  --agent-type GreedyIncident --seed 42 \
  --out ./experiments/greedy-vs-coverage
```

Output folder:

```
experiments/greedy-vs-coverage/
  run-meta.json    # scenario, seeds, agent types, counts
  metrics.csv      # time series from all watchers
```

## Built-in agent types

| Name | Behavior |
|------|----------|
| `RandomWalk` | Uniform random direction each tick |
| `GreedyIncident` | Move toward nearest visible incident; wander otherwise |
| `Stigmergy` | Drops markers near incidents; follows markers from others |

Add your own: subclass `Agent`, override `Decide`, register an `IAgentFactory`.

## Scenarios & frames

- **File → Open/Save Scenario** — JSON placements (`samples/scenarios/demo.json`)
- **Record frames** checkbox — while Running, writes `experiments/frames-*/tick-NNNNNN.png`
- **File → Capture Frame** — single PNG of the current view
- Reward model: incident proximity, crowding penalty, first-coverage bonus (`RewardModel`)

## Concepts (mapped from the classic platform)

| Classic | Modern |
|---------|--------|
| `World` / `WorldControl` | `SimWorld` + WPF `WorldCanvas` |
| `Inhabitant` | `Agent` |
| `Incident` | `Incident` |
| `Marker` / messages | `Message` |
| `Deployer` | `IDeployer` |
| `Watcher` | `IWatcher` + `ExperimentExporter` |
| `app.config` agent list | `AgentFactoryRegistry` |

## Documentation

- [docs/DeveloperGuide.md](docs/DeveloperGuide.md) — add agents, deployers, watchers
- [docs/ExperimentExport.md](docs/ExperimentExport.md) — CSV/JSON schema for analysis

## License

MIT (or as chosen by the project owner).
