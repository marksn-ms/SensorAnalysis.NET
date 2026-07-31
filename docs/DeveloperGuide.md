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

## Adding a watcher

Implement `IWatcher`, append rows in `OnTick`, declare `ColumnNames`.  
The exporter prefixes columns with the watcher name (`Coverage.tick`, …).

## Adding a deployer

Implement `IDeployer.Deploy(SimWorld, DeployRequest)` and place agents/incidents using `world.Add(...)`.

## Determinism

- Construct `SimWorld` with a fixed `seed`.
- Prefer seeded `Random` inside agents when randomness is required.
- Avoid wall-clock time and unordered parallelism in `Decide`.

## Headless vs GUI

Both use `SimulationRunner`. The GUI drives it from a `DispatcherTimer`; the CLI calls `RunExperiment` and writes the export folder.
