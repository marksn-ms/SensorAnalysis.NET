namespace SensorAnalysis.Hallway;

/// <summary>
/// One shared genome for every student in a generation. Trials are generations.
/// The first trial scores the prior weights. Later trials score a mutation and keep it
/// when the team fitness does not fall.
/// </summary>
public sealed class HallwayPopulation
{
    /// <summary>
    /// Prior weights: goal, avoid, stay threshold, eye gain, spare, miss gain, radio gain.
    /// </summary>
    public static double[] Prior { get; } = [1.0, 1.2, 0.85, 1.0, 1.0, 1.0, 0.8];

    private bool _evaluateChampion = true;
    private Random? _rng;

    /// <summary>Generation about to be scored, or just scored after <see cref="Close"/>.</summary>
    public int Generation { get; private set; }

    /// <summary>Best team fitness accepted so far.</summary>
    public double ChampionFitness { get; private set; } = double.NegativeInfinity;

    /// <summary>Weights that produced <see cref="ChampionFitness"/>.</summary>
    public double[] Champion { get; private set; } = Prior.ToArray();

    /// <summary>Mutation waiting for the next trial after the prior has been scored.</summary>
    public double[] Candidate { get; private set; } = Prior.ToArray();

    /// <summary>Seed the mutation stream once per experiment.</summary>
    public void EnsureSeed(int seed) => _rng ??= new Random(seed);

    /// <summary>Weights copied onto every student for the trial that is about to start.</summary>
    public double[] WeightsForThisTrial()
    {
        EnsureSeed(1);
        return (_evaluateChampion ? Champion : Candidate).ToArray();
    }

    /// <summary>Record the team fitness for the trial that just finished and prepare the next candidate.</summary>
    public void Close(double fitness)
    {
        EnsureSeed(1);
        if (_evaluateChampion)
        {
            ChampionFitness = fitness;
            _evaluateChampion = false;
            Candidate = Mutate(Champion);
        }
        else if (fitness >= ChampionFitness)
        {
            Champion = Candidate.ToArray();
            ChampionFitness = fitness;
            Candidate = Mutate(Champion);
        }
        else
        {
            Candidate = Mutate(Champion);
        }

        Generation++;
    }

    private double[] Mutate(double[] source)
    {
        var rng = _rng ?? new Random(1);
        var next = new double[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i] + (rng.NextDouble() * 2 - 1) * 0.2;
            next[i] = i == 2 ? Math.Clamp(value, 0.05, 2.5) : Math.Clamp(value, 0, 4);
        }

        return next;
    }
}

/// <summary>
/// Process-wide population. Each trial builds a fresh deployer and fresh students,
/// and this object is what survives between those trials.
/// </summary>
public static class HallwaySession
{
    /// <summary>Population used by the deployer and the score watcher.</summary>
    public static HallwayPopulation Population { get; private set; } = new();

    /// <summary>Start a new experiment. Tests call this so generations do not leak.</summary>
    public static void Reset() => Population = new HallwayPopulation();
}
