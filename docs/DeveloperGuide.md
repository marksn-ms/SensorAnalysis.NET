# Developer Guide

## Adding an agent strategy

```csharp
// in SensorAnalysis.Agents (or your own plugin assembly)
public sealed class MySearchAgent : Agent
{
    public MySearchAgent() => AgentType = "MySearch";

    public override AgentAction Decide(AgentObservation obs)
    {
        // use obs.NearbyIncidents, NearbyAgents, NearbyMessages
        return AgentAction.North;
    }
}

public sealed class MySearchAgentFactory : IAgentFactory
{
    public string Name => "MySearch";
    public string Description => "Custom search policy";
    public Agent Create(string? label = null) =>
        new MySearchAgent { Label = label ?? "MS" };
}
```

Register in the GUI / CLI startup:

```csharp
registry.Register(new MySearchAgentFactory());
```

Optionally give the WPF canvas a distinct marker (App layer only):

```csharp
EntityVisualCatalog.Default.RegisterAgent(
    new EntityVisualStyle("MySearch", EntityMarkerShape.Square, 90, 160, 200));
```

Unknown agent/incident type names still render with a stable hash-based pastel color.

Incidents may set `IncidentType` (default `"Incident"`) so different event kinds use different triangle colors via `RegisterIncident`.

## Adding a watcher

Implement `IWatcher`, append rows in `OnTick`, declare `ColumnNames`.  
The exporter prefixes columns with the watcher name (`Coverage.tick`, …).

## Adding a deployer

1. Create an options class with public get/set properties marked `[DeployerParam("Label")]`.
2. Implement `IDeployer` (`CreateOptions`, `OptionsType`, `Attach(world, options, context)`, `Tick`).
3. Register a factory in the App (`RegisterDeployers`) and, if needed, the CLI.

The UI builds the directive form from those attributes — no hard-coded wave/agent fields in the panel.
CLI and UI both attach deployers via `SimulationRunner.Deploy` / `AttachDeployer` with the same options types.

## Determinism

- Construct `SimWorld` with a fixed `seed`.
- Prefer seeded `Random` inside agents when randomness is required.
- Avoid wall-clock time and unordered parallelism in `Decide`.
- The world is **toroidal**: use `world.Wrap` / `world.Distance` (or `observation.DistanceTo` / `DisplacementTo`) rather than Euclidean deltas across edges.

## Headless vs GUI

Both use `SimulationRunner`. The GUI drives it from a `DispatcherTimer` and applies an **environment directive** log (tick-scheduled deployments) without encoding paper “regimes.” The CLI calls `RunExperiment` with regime helpers and writes the export folder.
