using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SensorAnalysis.App.Models;

/// <summary>Catalog entry for a metric the user can pin in the live metrics pane.</summary>
/// <param name="Key">Stable id, e.g. <c>inherent.agents</c> or <c>Coverage.coverage</c>.</param>
/// <param name="DisplayName">Label shown in the picker and series header.</param>
/// <param name="Format">.NET format string for the current value.</param>
public sealed record MetricCatalogEntry(string Key, string DisplayName, string Format = "G4");

/// <summary>A user-selected live metric series with recent samples for sparkline display.</summary>
public partial class LiveMetricSeries : ObservableObject
{
    private const int MaxPoints = 120;

    /// <summary>Creates a series bound to a catalog entry.</summary>
    /// <param name="entry">Catalog metadata describing the metric source.</param>
    public LiveMetricSeries(MetricCatalogEntry entry)
    {
        Entry = entry;
        DisplayName = entry.DisplayName;
    }

    /// <summary>Catalog metadata for this series.</summary>
    public MetricCatalogEntry Entry { get; }

    /// <summary>Series key (same as <see cref="MetricCatalogEntry.Key"/>).</summary>
    public string Key => Entry.Key;

    /// <summary>Header text.</summary>
    public string DisplayName { get; }

    /// <summary>Formatted current value.</summary>
    [ObservableProperty] private string _currentText = "—";

    /// <summary>Raw sample history (tick-ordered). Bound by <see cref="Controls.SparklineControl"/>.</summary>
    public ObservableCollection<double> Samples { get; } = new();

    /// <summary>Appends a sample and refreshes the formatted current value.</summary>
    /// <param name="value">Latest metric sample.</param>
    /// <param name="format">Optional format override; defaults to the catalog entry format.</param>
    public void Push(double value, string? format = null)
    {
        Samples.Add(value);
        while (Samples.Count > MaxPoints)
            Samples.RemoveAt(0);

        var fmt = format ?? Entry.Format;
        CurrentText = value.ToString(fmt);
    }

    /// <summary>Clears history (e.g. on New World).</summary>
    public void Reset()
    {
        Samples.Clear();
        CurrentText = "—";
    }
}
