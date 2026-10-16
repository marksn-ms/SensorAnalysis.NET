using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Hallway;

/// <summary>
/// Records arrivals, progress, path length, and proximity violations.
/// At the end of a trial it closes the shared population, because that is the
/// engine's signal that a generation has finished.
/// </summary>
public sealed class HallwayScoreWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a watcher that samples every five ticks. Required for reflection loading.</summary>
    public HallwayScoreWatcher()
        : this(5)
    {
    }

    /// <summary>Creates a watcher with an explicit sample interval.</summary>
    public HallwayScoreWatcher(int sampleEveryTicks)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "Hallway";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
    [
        "tick", "trial", "generation",
        "students", "arrived", "mean_progress",
        "wall_violations", "agent_violations",
        "mean_distance", "mean_fitness"
    ];

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial)
    {
    }

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        if (tick % _sampleEvery != 0)
            return;
        _rows.Add(Sample(world, tick, trial));
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial)
    {
        _rows.Add(Sample(world, tick: -1, trial));
        var students = world.Agents.OfType<HallwayStudentAgent>().ToList();
        if (students.Count > 0)
            HallwaySession.Population.Close(students.Average(s => s.Fitness));
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static double[] Sample(SimWorld world, long tick, int trial)
    {
        var students = world.Agents.OfType<HallwayStudentAgent>().ToList();
        if (students.Count == 0)
        {
            return
            [
                tick, trial, HallwaySession.Population.Generation,
                0, 0, 0, 0, 0, 0, 0
            ];
        }

        double progress = 0;
        foreach (var student in students)
        {
            var remaining = student.Waypoints.Count == 0
                ? 0
                : (student.Waypoints[^1] - student.Position).Length;
            var initial = Math.Max(1e-6, student.InitialGoalDistance);
            progress += Math.Clamp(1 - remaining / initial, 0, 1);
        }

        return
        [
            tick,
            trial,
            HallwaySession.Population.Generation,
            students.Count,
            students.Count(s => s.Arrived),
            progress / students.Count,
            students.Sum(s => s.WallViolations),
            students.Sum(s => s.AgentViolations),
            students.Average(s => s.DistanceTraveled),
            students.Average(s => s.Fitness)
        ];
    }
}
