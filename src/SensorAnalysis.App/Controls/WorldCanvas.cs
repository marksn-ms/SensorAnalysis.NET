using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.Controls;

/// <summary>
/// Renders the simulation world: tiles, agents, incidents, messages.
/// Supports pan (middle/right drag) and zoom (wheel).
/// </summary>
public class WorldCanvas : FrameworkElement
{
    /// <summary>Identifies the <see cref="World"/> dependency property.</summary>
    public static readonly DependencyProperty WorldProperty =
        DependencyProperty.Register(nameof(World), typeof(SimWorld), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Zoom"/> dependency property.</summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="SelectedObject"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedObjectProperty =
        DependencyProperty.Register(nameof(SelectedObject), typeof(WorldObject), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Simulation world to render, or <c>null</c> when none is loaded.</summary>
    public SimWorld? World
    {
        get => (SimWorld?)GetValue(WorldProperty);
        set => SetValue(WorldProperty, value);
    }

    /// <summary>View zoom factor (clamped by mouse-wheel handling). Default: 1.0.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Currently selected world object highlighted in the canvas.</summary>
    public WorldObject? SelectedObject
    {
        get => (WorldObject?)GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    private Point _panOffset;
    private Point? _dragStart;
    private Point _panAtDragStart;

    private static readonly Pen TilePen = new(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 1);
    private static readonly Brush AgentBrush = new SolidColorBrush(Color.FromRgb(80, 160, 255));
    private static readonly Brush IncidentBrush = new SolidColorBrush(Color.FromRgb(255, 100, 80));
    private static readonly Brush MessageBrush = new SolidColorBrush(Color.FromRgb(180, 255, 120));
    private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromRgb(255, 220, 60));
    private static readonly Pen SelectedPen = new(Brushes.White, 1.5);

    static WorldCanvas()
    {
        TilePen.Freeze();
        AgentBrush.Freeze();
        IncidentBrush.Freeze();
        MessageBrush.Freeze();
        SelectedBrush.Freeze();
        SelectedPen.Freeze();
    }

    /// <summary>Creates a world canvas that accepts focus and clips drawing to its bounds.</summary>
    public WorldCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 8.0);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _dragStart = e.GetPosition(this);
            _panAtDragStart = _panOffset;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && World is not null)
        {
            var worldPt = ScreenToWorld(e.GetPosition(this));
            SelectedObject = HitTest(worldPt);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragStart is { } start && IsMouseCaptured)
        {
            var now = e.GetPosition(this);
            _panOffset = new Point(
                _panAtDragStart.X + (now.X - start.X),
                _panAtDragStart.Y + (now.Y - start.Y));
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _dragStart = null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (World is null) return;

        var z = Zoom;
        var opts = World.Options;

        // Tiles
        for (int c = 0; c < opts.TileCount.Width; c++)
        for (int r = 0; r < opts.TileCount.Height; r++)
        {
            var tile = World.Tiles[c, r];
            var x = tile.Center.X - opts.TileSizePx.Width / 2.0;
            var y = tile.Center.Y - opts.TileSizePx.Height / 2.0;
            var rect = WorldToScreen(new Rect(x, y, opts.TileSizePx.Width, opts.TileSizePx.Height));

            Brush? fill = null;
            if (tile.LastCoveredTick >= 0)
            {
                var age = Math.Max(0, World.CurrentTick - tile.LastCoveredTick);
                var alpha = (byte)Math.Clamp(40 + 80 / (1 + age / 20.0), 40, 120);
                fill = new SolidColorBrush(Color.FromArgb(alpha, 40, 90, 40));
            }
            dc.DrawRectangle(fill, TilePen, rect);
        }

        // Incidents
        foreach (var inc in World.Incidents)
        {
            if (inc.IsDepleted) continue;
            var brush = inc.IsSelected || ReferenceEquals(inc, SelectedObject) ? SelectedBrush : IncidentBrush;
            DrawMarker(dc, inc.Position, 6, brush, z);
        }

        // Messages
        foreach (var msg in World.Messages)
            DrawMarker(dc, msg.Position, 3, MessageBrush, z);

        // Agents
        foreach (var agent in World.Agents)
        {
            var brush = agent.IsSelected || ReferenceEquals(agent, SelectedObject) ? SelectedBrush : AgentBrush;
            DrawMarker(dc, agent.Position, 5, brush, z);
            if (ReferenceEquals(agent, SelectedObject))
            {
                // sensor range ring
                var center = WorldToScreenPoint(agent.Position);
                var radius = agent.SensorRange * z;
                dc.DrawEllipse(null, SelectedPen, center, radius, radius);
            }
        }
    }

    private void DrawMarker(DrawingContext dc, Vec2 worldPos, double radius, Brush brush, double z)
    {
        var pt = WorldToScreenPoint(worldPos);
        dc.DrawEllipse(brush, null, pt, radius * Math.Max(0.5, z), radius * Math.Max(0.5, z));
    }

    private Point WorldToScreenPoint(Vec2 p) =>
        new(p.X * Zoom + _panOffset.X, p.Y * Zoom + _panOffset.Y);

    private Rect WorldToScreen(Rect r) =>
        new(r.X * Zoom + _panOffset.X, r.Y * Zoom + _panOffset.Y, r.Width * Zoom, r.Height * Zoom);

    private Vec2 ScreenToWorld(Point s) =>
        new((s.X - _panOffset.X) / Zoom, (s.Y - _panOffset.Y) / Zoom);

    private WorldObject? HitTest(Vec2 worldPt)
    {
        if (World is null) return null;
        const double hitR = 10;
        foreach (var a in World.Agents)
            if (Vec2.Distance(a.Position, worldPt) <= hitR) return a;
        foreach (var i in World.Incidents)
            if (!i.IsDepleted && Vec2.Distance(i.Position, worldPt) <= hitR) return i;
        return null;
    }

    /// <summary>Call after each simulation tick to redraw.</summary>
    public void Refresh() => InvalidateVisual();
}
