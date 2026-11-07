# SensorAnalysis

**Visualization-first** simulation platform for studying autonomous mobile sensor agents.

Designed for research workflows: watch agents interact on a world canvas, step or batch-run experiments, and export metrics/logs suitable for publication analysis.

Used by the sibling **Trust** paper repo (`../Trust`) for cooperative search, deception, and reputation experiments.

> Modern rewrite of the classic [marksn-ms/SensorAnalysis](https://github.com/marksn-ms/SensorAnalysis) WinForms platform — same goals, current .NET stack.

## Highlights

- **WPF world view** — pan, zoom, select agents/incidents/messages **or tiles**; type-colored markers (circles / diamonds / triangles)
- **New World (options)** — File menu / toolbar **New…** sets title, grid size, tile px, and seed (remembered for the next New)
- **Tick delay** — toolbar **Delay ms** controls the Run timer interval
- **Record video** — optional MJPEG AVI capture (ffmpeg-free) alongside PNG frame sequences
- **Unattended GUI capture** — `SensorAnalysis.App.exe recipe.json --run --ticks 400 --record-video out.avi --exit-on-stop`
- **Directive log** — at tick *t*, add a deployer; the form is driven by that deployer’s `[DeployerParam]` options (agent-type and incident-type combos). Multiple directives may share a tick.
- **Live metrics** — pin inherent stats or registered watcher columns (Coverage, Connectivity, Clearance, TrustTel); sparklines vs tick
- **In-process agents** — implement `Agent.Decide` / `Tick`, register via `IAgentFactory`
- **Radio messaging** — `SimWorld.Transmit` delivers to in-range **inboxes** (broadcast = empty `ToAgentId`); global **Radio traffic** panel narrates contact / orbit / break / status in fighter-pilot style, color-coded by sender type
- **Watchers** — coverage, connectivity, clearance, trust telemetry (registered in the GUI live runner and batch export)
- **Experiment export** — `run-meta.json` + `metrics.csv` per run
- **Headless CLI** — multi-regime batches (`--regime honest|deceptive|mitigated|…`)
- **Deterministic seeds** — fixed base seed + trial index

## Solution layout

| Project | Role |
|---------|------|
| `SensorAnalysis.Core` | World, agents, incidents, messages, intel oracle, export |
| `SensorAnalysis.Agents` | Strategies, `GaussianDeployer` / `RandomDeployer`, watchers |
| `SensorAnalysis.App` | WPF UI (directives, live metrics, entity visuals) |
| `SensorAnalysis.Cli` | Headless batch runner |
| `SensorAnalysis.Viewport3DPrototype` | Isolated WPF `Viewport3D` experiment for closed 3D tile worlds |
| `SensorAnalysis.Core.Tests` | Unit tests |
| `SensorAnalysis.Space.*` | Separate volumetric 3-torus platform (`SensorAnalysis.Space.slnx`) |

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

Typical interactive flow:

1. **New** / **New…** — create a world (options dialog for grid size & seed)
2. Edit the **Environment directives** draft (deployer, agent/adversary types & counts, incident type) → **Add** / **Apply due**
3. **Run** or **Step** — due directives at `AtTick <= CurrentTick` apply automatically before each tick; adjust **Delay ms** for run speed
4. Click empty space to select a **tile** (col/row, coverage, objects in tile); click markers to select agents/incidents/messages
5. Optionally enable **Record frames** (PNG) and/or **Record video** (MJPEG AVI on Stop)
6. Pause (**Stop**) to **Apply now** mid-run (e.g. inject more agents at the current tick)
7. Use **+** under Live metrics to pin inherent fields or watcher columns (incl. Clearance / TrustTel); sparklines plot vs tick
8. **Legend** lists colors/shapes for agent and incident types currently in the world

### Run the 3D topology prototype

```bash
dotnet run --project src/SensorAnalysis.Viewport3DPrototype
```

This intentionally standalone prototype has no references to Core, Agents, or App. It creates
an unbounded number of evenly distributed Fibonacci center points and calculates a default
one-tile-scale sensor range from their density. The viewport renders the continuous sphere rather
than individual tiles. Use **+** / **−** to change the number of centers, **Start** to run a
continuous random walk across the surface, arrow buttons or mouse drag to orbit, and either mouse
wheel or **Zoom** controls to change distance. The inhabitant is a surface-tangent disc with
radius `r / 100`; because that footprint is sub-pixel on a unit sphere, the marker is drawn at a
visibility floor and the header reports both values. Three colored axes protrude through the
poles (red), the prime meridian (blue), and their mutual perpendicular (green) for scale.

### Run the spherical geodesic platform

```bash
dotnet run --project src/SensorAnalysis.Sphere.App
```

`SensorAnalysis.Sphere.slnx` is a separate, clean-slate platform for the post-Trust spherical
model; it does not alter the rectangular/toroidal App or CLI used by existing analyses. Worlds are
empty until tick-scheduled directives populate them (default: five CooperativeSearch agents at
tick 0 with world-default `r`/`c`, a coverage watcher at tick 0, and periodic `StationaryIncident`
waves starting at tick 20). Agents move continuously on the sphere by geodesic steps of size
`StepSize` (default 1); the geodesic resolution `n` only builds an internal `20n²`-face spatial
index for hop-limited queries and area-weighted coverage. Coverage is visualized as a smooth UV
heatmap on the sphere surface. The Sphere App shell mirrors the classic layout: File/Simulation
menus, New World options for `R`/`r`/`c`/`n`/step, hit-tested Selection, filtered Messages, and a
Deployment panel for scheduling/removing/applying directives. File → Save/Open Scenario persists
the versioned world configuration and queued directives as `*.sphere.json`; File → Export Metrics
writes coverage watcher samples as CSV.

### Run the volumetric Space platform

```bash
dotnet run --project src/SensorAnalysis.Space.App
```

`SensorAnalysis.Space.slnx` is a separate, clean-slate platform for a wrapped cubic 3-torus
(Y-up, right-handed). It does not reference the rectangular or spherical stacks. Default New World
runs `WorldInitializer` **PlanetoidBelt**: an anchored spinning planetoid at the origin, a slow
Keplerian asteroid belt in the XZ plane, and a few inertial random-walk weavers with a Δv throttle.
The viewport uses stock WPF `Viewport3D` (no extra 3D package): isometric orbit about a focus
(arrows / left-drag, pitch clamped 20° from the poles), Shift+LMB or MMB pan, wheel zoom, Home reset,
optional Follow (camera tracks selection without changing the look vector), and a toggleable Fly
god-mode (WASD + Q/E). Click an inhabitant to select it (wire box, no filled overlay). New World
can also pick **ToroidalStream**: no gravity; asteroids coast through the origin along one axis
and wrap. RGB axes stay a constant on-screen size.

### Publish CLI next to the Trust paper repo

```bash
dotnet publish src/SensorAnalysis.Cli/SensorAnalysis.Cli.csproj -c Release -o ../Trust/SensorAnalysis
```

Trust overnight scripts prefer `Trust/SensorAnalysis/SensorAnalysis.Cli.exe` (the WPF `App.exe` alone cannot run headless batches).

## Headless CLI

```bash
dotnet run --project src/SensorAnalysis.Cli -c Release -- \
  --regime honest --trials 100 --ticks 1000 \
  --agents 8 --incidents 0 --incident-period 25 --incident-wave 1 \
  --adversaries 0 \
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
| `--adversaries` | Count of infiltrators (separate Random deploy step) |
| `--incidents` | Initial one-shot incidents (UniformIncidents or PlaceNear) |
| `--incident-period` | Ticks between waves (default **25**; `0` disables) |
| `--incident-wave` | Incidents per wave (default **1**) |
| `--incidents-near-adversaries` | Hybrid load: `PeriodicIncidents` + `PlaceNear` with rates summing to full `--incident-period` load. Off by default — never auto-enabled by adversary count. |
| `--near-share` | Fraction of hybrid rate from PlaceNear (default **0.5**; rest is uniform). Periods ≈ `period/(1−share)` and `period/share`. |
| `--near-range-factor` | PlaceNear radius as fraction of host sensor range (default **0.5**) |

CLI prints an approximate disk-cover scale `n*` (sparse ≈ agents ≪ `n*`).

## Built-in agent types

### Trust-paper stack (current defaults)

| Name | Behavior |
|------|----------|
| `OrbitingCoop` | Personal-space exploration; on find/join, **orbit clockwise at r/2**; bird-in-hand join; truthful broadcasts; contributes to drain |
| `HerdingDeceptive` | Uniform-random deploy; **local sensing only** (no oracle); **no drain**; one lure decoy per victim away from a visible event; hold / orbit / standoff / herd from what is in range |
| `HerdingDeceptiveOracle` | Same lure geometry with **`IIncidentOracle`** omniscience — kept for A/B, not the paper default |
| `MitigatedOrbit` | Orbiting coop + Beta–Bernoulli trust; silence detection; false-lure disappointment; **authenticated disappointment gossip** (receivers accept only live `MitigatedOrbit` senders) |
| `PersonalSpaceCoop` / `CrowdOnlyDeceptive` / `CrowdAwareMitigated` | First-principles P-only stack (no radio) |
| `ProgressCoop` / `ProgressLoiterDeceptive` / `ProgressAwareMitigated` | Local cue-intensity shrink; loiter without drain; Beta on “present, no shrink” |
| `LloydCoop` / `LloydParkDeceptive` | Limited-range Voronoi of the sensing disc |
| `PheromoneCoop` / `PheromoneFakeDeceptive` | Unsigned decaying field (`SimWorld.Pheromones`); no speaker id |
| `BoidsCoop` / `BoidsPeelDeceptive` / `BoidsAwareMitigated` | Align + cohere + separate; peel adversary; trust on heading mismatch |

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
Register a canvas style in the App with `EntityVisualCatalog.Default.RegisterAgent(...)` (optional — unknown types get a stable hash color).

## GUI visuals

| Entity | Default cue |
|--------|-------------|
| Cooperative / baseline agents | Colored **circles** (by `Agent.AgentType`) |
| Deceptive / infiltrator agents | Colored **diamonds** |
| Incidents | Colored **triangles** (by `Incident.IncidentType`) |
| Message echoes | Small lime dots |
| Selected tile | Yellow fill + white border |

Styles live in `SensorAnalysis.App.Visualization` so Core/CLI stay UI-free. New factories appear automatically via hash colors until you register a curated palette.

**Video recording** writes a classic MJPEG AVI (managed RIFF, no ffmpeg). Compatible with VLC / Windows Media Player; not all browsers play it. No audio; classic AVI size limits apply (~2 GB).

**Unattended capture** (desktop session required):

```text
SensorAnalysis.App.exe path\to\recipe.json --run --ticks 400 --delay-ms 16 --frame-every 2 --record-video out.avi --exit-on-stop
```

Trust helper: `experiments/record-regime-clips.ps1` writes one AVI per regime under `experiments/runs/regime-clips-*`.

## World mechanics (current)

- **Topology** — **toroidal** (classic open world): leaving one edge enters the opposite edge. Sensing, radio, connectivity, drain, and steering use shortest-path distance on the torus. Soft “walls” can be modeled later as fixed inhabitants + collision avoidance rather than hard clamps.
- **Incidents** — intensity drained each tick by agents with `ContributesToIncidentDrain` in range. Drain scales **superlinearly**: `DrainPerAgentPerTick * covering^MultiAgentDrainExponent` (default exponent 1.5).
- **Messages** — `Transmit` enqueues inbox snapshots for in-range peers; optional short world echo for UI/metrics. Honest filter: broadcast (`ToAgentId == null`) or addressed to self; adversaries can disable filtering (eavesdrop-ready).
- **Deployment** — one directive / CLI step = one deployer = one kind of thing.
  - **Gaussian** / **Random**: `Count` + `Type` (+ sensor/sigma) for agents.
  - **UniformIncidents**: one-shot uniform incidents.
  - **Periodic**: waves every `PeriodTicks` (`0` = one-shot). Empty `NearTypeName` is a uniform field; otherwise spawn near that living host type (`RangeFactor` × sensor range). Recipe aliases: `PeriodicIncidents`, `PlaceNear`, `NearHost`.
- **Intel** — `IIncidentOracle` / `WorldIncidentOracle` injects privileged incident lists into deceptive agents without hard-coding IDs in the tick loop.

## Watchers & export

| Watcher | Columns (prefix) |
|---------|------------------|
| `Coverage` | coverage, active incidents, reward, messages |
| `Connectivity` | pair counts / degree |
| `Clearance` | fraction cleared, all-clear tick (end-of-trial sentinel `tick=-1`) |
| `TrustTel` | fraction cleared, mean mitigated trust vs deceptive peers, **vs other mitigated peers**, disappointment echoes |
| `CrowdIntrusion` | inside-PS ticks vs consecutive-closing herding offenses |
| `ProgressStall` | co-presence vs progress-offense counts |
| `LloydOccupancy` / `PheromoneField` / `BoidsPeel` | literature-channel telemetry |

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
| `Deployer` | `IDeployer` (tickable) / `GaussianDeployer` + `PeriodicIncidentDeployer` |
| `Watcher` | `IWatcher` + `ExperimentExporter` |

## Documentation

- [docs/Distribution.md](docs/Distribution.md) — Windows installers, versions, and updates
- [docs/DeveloperGuide.md](docs/DeveloperGuide.md) — add agents, deployers, watchers
- [docs/ExperimentExport.md](docs/ExperimentExport.md) — CSV/JSON schema
- Sibling paper workflow: `../Trust/README.md`

## License

MIT (or as chosen by the project owner).
