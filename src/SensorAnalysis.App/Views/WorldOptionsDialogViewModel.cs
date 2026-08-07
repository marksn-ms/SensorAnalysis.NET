using CommunityToolkit.Mvvm.ComponentModel;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.Views;

/// <summary>Editable fields for <see cref="WorldOptionsDialog"/>.</summary>
public partial class WorldOptionsDialogViewModel : ObservableObject
{
    /// <summary>World title.</summary>
    [ObservableProperty] private string _title = "New World";

    /// <summary>Number of tile columns.</summary>
    [ObservableProperty] private int _tileColumns = 12;

    /// <summary>Number of tile rows.</summary>
    [ObservableProperty] private int _tileRows = 12;

    /// <summary>Tile width in pixels.</summary>
    [ObservableProperty] private int _tileWidthPx = 40;

    /// <summary>Tile height in pixels.</summary>
    [ObservableProperty] private int _tileHeightPx = 40;

    /// <summary>RNG seed for the new world.</summary>
    [ObservableProperty] private int _seed = 42;

    /// <summary>Prefills fields from prior options and seed.</summary>
    public void Prefill(WorldOptions options, int seed)
    {
        Title = options.Title;
        TileColumns = options.TileCount.Width;
        TileRows = options.TileCount.Height;
        TileWidthPx = options.TileSizePx.Width;
        TileHeightPx = options.TileSizePx.Height;
        Seed = seed;
    }

    /// <summary>Builds a <see cref="WorldOptions"/> from the form values.</summary>
    public WorldOptions ToWorldOptions() => new()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "New World" : Title.Trim(),
        TileCount = new GridSize(TileColumns, TileRows),
        TileSizePx = new GridSize(TileWidthPx, TileHeightPx)
    };

    /// <summary>Validates positive grid dimensions.</summary>
    public bool TryValidate(out string error)
    {
        if (TileColumns < 1 || TileRows < 1)
        {
            error = "Tile columns and rows must be at least 1.";
            return false;
        }
        if (TileWidthPx < 1 || TileHeightPx < 1)
        {
            error = "Tile width and height must be at least 1 px.";
            return false;
        }
        error = "";
        return true;
    }
}
