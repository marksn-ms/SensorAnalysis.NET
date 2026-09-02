using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.World;

/// <summary>
/// Local unsigned digital pheromone: a decaying scalar field with no speaker id.
/// Deposit and sample are position-only. Decay once per world tick.
/// Cell size and decay are world-timescale constants, not ρ knobs.
/// </summary>
public sealed class PheromoneField
{
    private readonly double[] _mass;

    /// <summary>
    /// Creates a toroidal field covering <paramref name="worldWidth"/> × <paramref name="worldHeight"/>.
    /// </summary>
    /// <param name="worldWidth">World width in pixels.</param>
    /// <param name="worldHeight">World height in pixels.</param>
    /// <param name="cellSize">Grid cell size in pixels (default 10). Sensor range 80 covers 8 cells.</param>
    /// <param name="decayPerTick">
    /// Multiplicative decay applied once per world tick. Default 0.95 ⇒ half-life ≈ 13.5 ticks,
    /// about half an incident wave (period=30). Chosen from world timescales, not from ρ.
    /// </param>
    public PheromoneField(double worldWidth, double worldHeight, double cellSize = 10, double decayPerTick = 0.95)
    {
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        CellSize = cellSize <= 0 ? 10 : cellSize;
        DecayPerTick = decayPerTick;
        Cols = Math.Max(1, (int)Math.Ceiling(worldWidth / CellSize));
        Rows = Math.Max(1, (int)Math.Ceiling(worldHeight / CellSize));
        _mass = new double[Cols * Rows];
    }

    /// <summary>World width this field covers.</summary>
    public double WorldWidth { get; }

    /// <summary>World height this field covers.</summary>
    public double WorldHeight { get; }

    /// <summary>Grid cell size in world units.</summary>
    public double CellSize { get; }

    /// <summary>Per-tick multiplicative decay (applied by <see cref="Decay"/>).</summary>
    public double DecayPerTick { get; }

    /// <summary>Number of columns in the field grid.</summary>
    public int Cols { get; }

    /// <summary>Number of rows in the field grid.</summary>
    public int Rows { get; }

    /// <summary>Sum of all cell masses.</summary>
    public double TotalMass
    {
        get
        {
            double s = 0;
            for (int i = 0; i < _mass.Length; i++) s += _mass[i];
            return s;
        }
    }

    /// <summary>
    /// Adds <paramref name="amount"/> at <paramref name="worldPos"/>. Unsigned: there is no speaker id.
    /// </summary>
    public void Deposit(Vec2 worldPos, double amount)
    {
        if (amount <= 0) return;
        var (c, r) = CellOf(worldPos);
        _mass[Index(c, r)] += amount;
    }

    /// <summary>Scalar field value at <paramref name="worldPos"/> (the containing cell).</summary>
    public double Sample(Vec2 worldPos)
    {
        var (c, r) = CellOf(worldPos);
        return _mass[Index(c, r)];
    }

    /// <summary>
    /// Local 4-neighbor finite-difference gradient at <paramref name="worldPos"/>.
    /// Range-limited by construction (one cell). No speaker id.
    /// </summary>
    public Vec2 Gradient(Vec2 worldPos)
    {
        var (c, r) = CellOf(worldPos);
        var e = _mass[Index(WrapCol(c + 1), r)];
        var w = _mass[Index(WrapCol(c - 1), r)];
        var s = _mass[Index(c, WrapRow(r + 1))];
        var n = _mass[Index(c, WrapRow(r - 1))];
        return new Vec2(e - w, s - n);
    }

    /// <summary>
    /// Displacement from <paramref name="from"/> toward the highest-mass cell whose center
    /// is within <paramref name="range"/>. Unsigned chemotaxis, range-limited. Null if the
    /// neighborhood is empty or the max is here.
    /// </summary>
    public Vec2? Attraction(Vec2 from, double range)
    {
        var (fc, fr) = CellOf(from);
        var best = _mass[Index(fc, fr)];
        int bc = fc, br = fr;
        var found = false;
        var cellRange = Math.Max(1, (int)Math.Ceiling(range / CellSize));
        for (int dc = -cellRange; dc <= cellRange; dc++)
        for (int dr = -cellRange; dr <= cellRange; dr++)
        {
            var c = WrapCol(fc + dc);
            var r = WrapRow(fr + dr);
            var center = CellCenter(c, r);
            if (Vec2.ToroidalDistance(from, center, WorldWidth, WorldHeight) > range)
                continue;
            var m = _mass[Index(c, r)];
            if (m > best)
            {
                best = m;
                bc = c;
                br = r;
                found = true;
            }
        }

        if (!found) return null;
        var target = CellCenter(bc, br);
        var d = Vec2.ToroidalDisplacement(from, target, WorldWidth, WorldHeight);
        return d.Length < 1e-9 ? null : d;
    }

    /// <summary>
    /// Spread a fraction of each cell into its 4 neighbors (toroidal). Classic digital
    /// pheromone diffusion; rate is a field constant, not a ρ knob.
    /// </summary>
    public void Diffuse(double rate = 0.2)
    {
        if (rate <= 0) return;
        var next = new double[_mass.Length];
        var keep = 1.0 - rate;
        var share = rate / 4.0;
        for (int c = 0; c < Cols; c++)
        for (int r = 0; r < Rows; r++)
        {
            var m = _mass[Index(c, r)];
            next[Index(c, r)] += m * keep;
            next[Index(WrapCol(c + 1), r)] += m * share;
            next[Index(WrapCol(c - 1), r)] += m * share;
            next[Index(c, WrapRow(r + 1))] += m * share;
            next[Index(c, WrapRow(r - 1))] += m * share;
        }

        Array.Copy(next, _mass, _mass.Length);
    }

    /// <summary>Multiply every cell by <see cref="DecayPerTick"/> and drop tiny residuals.</summary>
    public void Decay()
    {
        var f = DecayPerTick;
        for (int i = 0; i < _mass.Length; i++)
        {
            var v = _mass[i] * f;
            _mass[i] = v < 1e-12 ? 0 : v;
        }
    }

    /// <summary>Zero the field (trial reset).</summary>
    public void Clear()
    {
        Array.Clear(_mass);
    }

    private (int c, int r) CellOf(Vec2 p)
    {
        var x = Vec2.WrapCoord(p.X, WorldWidth);
        var y = Vec2.WrapCoord(p.Y, WorldHeight);
        var c = Math.Min(Cols - 1, Math.Max(0, (int)(x / CellSize)));
        var r = Math.Min(Rows - 1, Math.Max(0, (int)(y / CellSize)));
        return (c, r);
    }

    private int WrapCol(int c)
    {
        c %= Cols;
        return c < 0 ? c + Cols : c;
    }

    private int WrapRow(int r)
    {
        r %= Rows;
        return r < 0 ? r + Rows : r;
    }

    private int Index(int c, int r) => r * Cols + c;

    private Vec2 CellCenter(int c, int r) =>
        new((c + 0.5) * CellSize, (r + 0.5) * CellSize);
}
