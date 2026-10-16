using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SensorAnalysis.App.Visualization;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.Controls;

/// <summary>
/// Renders the simulation world: tiles, agents, incidents, messages.
/// Supports pan (drag when not centered) and zoom (wheel).
/// When <see cref="ViewCentered"/> is true, the view tracks the world center or selection.
/// Agent/incident colors and shapes come from <see cref="EntityVisualCatalog"/>.
/// Left-click selects an entity or a tile.
/// </summary>
public class WorldCanvas : FrameworkElement
{
    /// <summary>Identifies the <see cref="World"/> dependency property.</summary>
    public static readonly DependencyProperty WorldProperty =
        DependencyProperty.Register(nameof(World), typeof(SimWorld), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewFocusInputChanged));

    /// <summary>Identifies the <see cref="Zoom"/> dependency property.</summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewFocusInputChanged));

    /// <summary>Identifies the <see cref="SelectedObject"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedObjectProperty =
        DependencyProperty.Register(nameof(SelectedObject), typeof(WorldObject), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnViewFocusInputChanged));

    /// <summary>Identifies the <see cref="SelectedTile"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedTileProperty =
        DependencyProperty.Register(nameof(SelectedTile), typeof(Tile), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnViewFocusInputChanged));

    /// <summary>Identifies the <see cref="ViewCentered"/> dependency property.</summary>
    public static readonly DependencyProperty ViewCenteredProperty =
        DependencyProperty.Register(nameof(ViewCentered), typeof(bool), typeof(WorldCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewFocusInputChanged));

    private const double DragThresholdPx = 3;

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

    /// <summary>Currently selected tile highlighted with a yellow/white rectangle.</summary>
    public Tile? SelectedTile
    {
        get => (Tile?)GetValue(SelectedTileProperty);
        set => SetValue(SelectedTileProperty, value);
    }

    /// <summary>
    /// When true (default), the view auto-centers on the world or current selection.
    /// When false, left-drag pans manually; a selected entity stays centered while moving.
    /// </summary>
    public bool ViewCentered
    {
        get => (bool)GetValue(ViewCenteredProperty);
        set => SetValue(ViewCenteredProperty, value);
    }

    /// <summary>
    /// Visual style catalog used for agent/incident markers.
    /// Defaults to <see cref="EntityVisualCatalog.Default"/>.
    /// </summary>
    public EntityVisualCatalog VisualCatalog { get; set; } = EntityVisualCatalog.Default;

    private Point _panOffset;
    private Point? _dragStart;
    private Point _panAtDragStart;
    private bool _didDrag;

    private static readonly Pen TilePen = new(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 1);
    private static readonly Brush MessageBrush = new SolidColorBrush(Color.FromRgb(180, 255, 120));
    private static readonly Brush SelectedFillBrush = new SolidColorBrush(Color.FromRgb(255, 220, 60));
    private static readonly Pen SelectedPen = new(Brushes.White, 1.5);
    private static readonly Brush BodyBrush = new SolidColorBrush(Color.FromRgb(150, 156, 168));
    private static readonly Brush SelectedTileFill = new SolidColorBrush(Color.FromArgb(70, 255, 220, 60));
    private static readonly Pen SelectedTilePen = new(Brushes.White, 2);
    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.Ordinal);

    static WorldCanvas()
    {
        TilePen.Freeze();
        MessageBrush.Freeze();
        BodyBrush.Freeze();
        SelectedFillBrush.Freeze();
        SelectedPen.Freeze();
        SelectedTileFill.Freeze();
        SelectedTilePen.Freeze();
    }

    /// <summary>Creates a world canvas that accepts focus and clips drawing to its bounds.</summary>
    public WorldCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) => UpdateViewFocus();
    }

    private static void OnViewFocusInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WorldCanvas)d).UpdateViewFocus();

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 8.0);
        UpdateViewFocus();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (ViewCentered)
        {
            if (e.ChangedButton == MouseButton.Left && World is not null)
            {
                _didDrag = false;
                _dragStart = e.GetPosition(this);
                CaptureMouse();
                e.Handled = true;
            }
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle or MouseButton.Right)
        {
            _didDrag = false;
            _dragStart = e.GetPosition(this);
            _panAtDragStart = _panOffset;
            CaptureMouse();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragStart is not { } start || !IsMouseCaptured)
            return;

        var now = e.GetPosition(this);
        if ((now - start).Length > DragThresholdPx)
            _didDrag = true;

        if (!ViewCentered)
        {
            _panOffset = new Point(
                _panAtDragStart.X + (now.X - start.X),
                _panAtDragStart.Y + (now.Y - start.Y));
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
            return;

        if (e.ChangedButton == MouseButton.Left && World is not null && !_didDrag)
            ApplySelectionAt(e.GetPosition(this));

        ReleaseMouseCapture();
        _dragStart = null;
        _didDrag = false;
        e.Handled = true;
    }

    private void ApplySelectionAt(Point screenPt)
    {
        if (World is null) return;

        var worldPt = ScreenToWorld(screenPt);
        var hit = HitTest(worldPt);
        if (hit is null)
        {
            SelectedObject = null;
            SelectedTile = World.TileAt(worldPt);
        }
        else
        {
            SelectedTile = null;
            SelectedObject = hit;
        }

        UpdateViewFocus();
        InvalidateVisual();
    }

    /// <inheritdoc />
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

            if (SelectedTile is not null &&
                SelectedTile.Col == tile.Col && SelectedTile.Row == tile.Row)
            {
                dc.DrawRectangle(SelectedTileFill, SelectedTilePen, rect);
            }
        }

        // Fixed-size bodies — discs and wall capsules
        foreach (var body in World.Bodies)
        {
            var selected = body.IsSelected || ReferenceEquals(body, SelectedObject);
            var brush = selected ? SelectedFillBrush : BodyBrush;
            if (body.Shape == BodyShape.Disc || body.Length <= 1e-6)
            {
                var center = WorldToScreenPoint(body.Position);
                var radius = Math.Max(1, body.Radius * z);
                dc.DrawEllipse(brush, selected ? SelectedPen : null, center, radius, radius);
            }
            else
            {
                var thickness = Math.Max(1, body.Radius * 2 * z);
                var pen = new Pen(brush, thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                dc.DrawLine(pen, WorldToScreenPoint(body.EndpointA), WorldToScreenPoint(body.EndpointB));
            }
        }

        // Incidents — triangles (or registered shape) by IncidentType
        foreach (var inc in World.Incidents)
        {
            if (inc.IsDepleted) continue;
            var style = VisualCatalog.ResolveIncident(inc.IncidentType);
            var selected = inc.IsSelected || ReferenceEquals(inc, SelectedObject);
            DrawStyledMarker(dc, inc.Position, style, selected, z);
        }

        // Messages — lime echoes that fade with remaining TTL (1 at birth, 0 at expiry)
        foreach (var msg in World.Messages)
        {
            var selected = msg.IsSelected || ReferenceEquals(msg, SelectedObject);
            dc.PushOpacity(msg.RemainingLife);
            if (selected)
                DrawEllipseMarker(dc, msg.Position, 5, SelectedFillBrush, z, SelectedPen);
            else
                DrawEllipseMarker(dc, msg.Position, 3, MessageBrush, z);
            dc.Pop();
        }

        // Agents — color/shape by AgentType
        foreach (var agent in World.Agents)
        {
            var style = VisualCatalog.ResolveAgent(agent.AgentType);
            var selected = agent.IsSelected || ReferenceEquals(agent, SelectedObject);
            DrawStyledMarker(dc, agent.Position, style, selected, z);
            if (selected)
            {
                var center = WorldToScreenPoint(agent.Position);
                var radius = agent.SensorRange * z;
                dc.DrawEllipse(null, SelectedPen, center, radius, radius);
            }
        }
    }

    private void DrawStyledMarker(DrawingContext dc, Vec2 worldPos, EntityVisualStyle style, bool selected, double z)
    {
        var brush = selected ? SelectedFillBrush : GetBrush(style.R, style.G, style.B);
        var pen = selected ? SelectedPen : null;
        var scale = Math.Max(0.5, z);
        var radius = style.Radius * scale;
        var center = WorldToScreenPoint(worldPos);

        switch (style.Shape)
        {
            case EntityMarkerShape.Square:
                dc.DrawRectangle(brush, pen,
                    new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                break;
            case EntityMarkerShape.Diamond:
                DrawPolygon(dc, brush, pen,
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius, center.Y),
                    new Point(center.X, center.Y + radius),
                    new Point(center.X - radius, center.Y));
                break;
            case EntityMarkerShape.Triangle:
                DrawPolygon(dc, brush, pen,
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius * 0.9, center.Y + radius * 0.75),
                    new Point(center.X - radius * 0.9, center.Y + radius * 0.75));
                break;
            default:
                dc.DrawEllipse(brush, pen, center, radius, radius);
                break;
        }
    }

    private void DrawEllipseMarker(DrawingContext dc, Vec2 worldPos, double radius, Brush brush, double z, Pen? pen = null)
    {
        var pt = WorldToScreenPoint(worldPos);
        var r = radius * Math.Max(0.5, z);
        dc.DrawEllipse(brush, pen, pt, r, r);
    }

    private static void DrawPolygon(DrawingContext dc, Brush brush, Pen? pen, params Point[] points)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, pen, geo);
    }

    private static Brush GetBrush(byte r, byte g, byte b)
    {
        var key = $"{r},{g},{b}";
        if (BrushCache.TryGetValue(key, out var cached))
            return cached;
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    private Point WorldToScreenPoint(Vec2 p) =>
        new(p.X * Zoom + _panOffset.X, p.Y * Zoom + _panOffset.Y);

    private Rect WorldToScreen(Rect r) =>
        new(r.X * Zoom + _panOffset.X, r.Y * Zoom + _panOffset.Y, r.Width * Zoom, r.Height * Zoom);

    private Vec2 ScreenToWorld(Point s) =>
        new((s.X - _panOffset.X) / Zoom, (s.Y - _panOffset.Y) / Zoom);

    /// <summary>
    /// Hit-tests agents, then incidents, then messages within <paramref name="hitR"/> world units.
    /// </summary>
    private WorldObject? HitTest(Vec2 worldPt, double hitR = 10)
    {
        if (World is null) return null;
        foreach (var a in World.Agents)
            if (World.Distance(a.Position, worldPt) <= hitR) return a;
        foreach (var b in World.Bodies)
            if (b.DistanceToSurface(worldPt, World.WorldWidthPx, World.WorldHeightPx) <= hitR) return b;
        foreach (var i in World.Incidents)
            if (!i.IsDepleted && World.Distance(i.Position, worldPt) <= hitR) return i;
        foreach (var m in World.Messages)
            if (World.Distance(m.Position, worldPt) <= hitR) return m;
        return null;
    }

    /// <summary>Call after each simulation tick to redraw and follow the current focus.</summary>
    public void Refresh()
    {
        UpdateViewFocus();
        InvalidateVisual();
    }

    /// <summary>Re-centers the view when auto-center or follow-selection rules apply.</summary>
    public void UpdateViewFocus()
    {
        if (GetFocusWorldPoint() is not { } focus)
            return;

        CenterOn(focus);
    }

    private Vec2? GetFocusWorldPoint()
    {
        if (SelectedObject is WorldObject obj)
            return obj.Position;

        if (SelectedTile is Tile tile)
            return tile.Center;

        if (ViewCentered && World is not null)
            return new Vec2(World.WorldWidthPx / 2.0, World.WorldHeightPx / 2.0);

        return null;
    }

    private void CenterOn(Vec2 worldPoint)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var cx = ActualWidth / 2.0;
        var cy = ActualHeight / 2.0;
        _panOffset = new Point(
            cx - worldPoint.X * Zoom,
            cy - worldPoint.Y * Zoom);
    }
}
