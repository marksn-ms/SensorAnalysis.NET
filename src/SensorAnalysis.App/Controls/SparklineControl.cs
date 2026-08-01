using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace SensorAnalysis.App.Controls;

/// <summary>
/// Draws a simple sparkline that fills its available width (reflows on parent resize).
/// X progresses left→right over the sample history; Y is min–max normalized.
/// </summary>
public sealed class SparklineControl : FrameworkElement
{
    /// <summary>Identifies the <see cref="Samples"/> dependency property.</summary>
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IEnumerable),
            typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSamplesChanged));

    /// <summary>Identifies the <see cref="Stroke"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(SparklineControl),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="StrokeThickness"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(SparklineControl),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _subscribed;

    /// <summary>Sample values plotted left-to-right (oldest → newest).</summary>
    public IEnumerable? Samples
    {
        get => (IEnumerable?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>Stroke brush for the polyline.</summary>
    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Stroke thickness in device-independent pixels.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 1 || height < 1 || Samples is null)
            return;

        var values = new List<double>();
        foreach (var item in Samples)
        {
            if (item is double d) values.Add(d);
            else if (item is IConvertible c) values.Add(c.ToDouble(null));
        }

        if (values.Count == 0)
            return;

        double min = values[0], max = values[0];
        foreach (var v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        var span = max - min;
        if (span < 1e-12) span = 1;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                var x = values.Count == 1 ? 0 : i * (width / (values.Count - 1));
                var y = height - ((values[i] - min) / span) * (height - 2) - 1;
                var pt = new Point(x, y);
                if (i == 0) ctx.BeginFigure(pt, isFilled: false, isClosed: false);
                else ctx.LineTo(pt, isStroked: true, isSmoothJoin: true);
            }
        }
        geo.Freeze();

        var pen = new Pen(Stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SparklineControl)d;
        control.Unsubscribe();
        if (e.NewValue is INotifyCollectionChanged notify)
        {
            control._subscribed = notify;
            notify.CollectionChanged += control.OnCollectionChanged;
        }
        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    private void Unsubscribe()
    {
        if (_subscribed is null) return;
        _subscribed.CollectionChanged -= OnCollectionChanged;
        _subscribed = null;
    }
}
