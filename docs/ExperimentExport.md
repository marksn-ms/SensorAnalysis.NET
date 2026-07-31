# Experiment export format

Each run produces a folder:

```
experiments/<name>-<timestamp>/
  run-meta.json
  metrics.csv
```

## run-meta.json

```json
{
  "ExperimentName": "cli-batch",
  "StartedAt": "2026-07-30T15:00:00+00:00",
  "FinishedAt": "2026-07-30T15:00:12+00:00",
  "Seed": 42,
  "Trials": 10,
  "TicksPerTrial": 500,
  "WorldTitle": "CLI Experiment",
  "TileColumns": 12,
  "TileRows": 12,
  "AgentCount": 20,
  "IncidentCount": 8,
  "AgentTypes": [ "GreedyIncident" ],
  "Notes": "CLI: --trials 10 ..."
}
```

## metrics.csv

Header columns are prefixed by watcher name:

```
Coverage.tick,Coverage.trial,Coverage.coverage,Coverage.active_incidents,...
Connectivity.tick,Connectivity.trial,Connectivity.agent_edges,...
```

One row per sample. With `sampleEveryTicks: 1` you get one row per tick per trial.

Load in Python:

```python
import pandas as pd
df = pd.read_csv("metrics.csv")
df.groupby("Coverage.trial")["Coverage.coverage"].max()
```

## Optional frames

The GUI can later write `snapshots/tick-000123.png` alongside the CSV for figure generation. The core export path does not require images.
