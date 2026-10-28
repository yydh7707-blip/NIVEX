using System.Windows.Media;

namespace NIVEX.Desktop;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Silver, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values { get => (double[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 160 : available.Width, double.IsNaN(Height) ? 48 : Height);

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4) return;
        var baseline = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xC5, 0xCC, 0xD4)), 1);
        baseline.Freeze();
        context.DrawLine(baseline, new Point(0, height - 0.5), new Point(width, height - 0.5));

        var values = Values;
        if (values is null || values.Length < 2) return;
        var step = width / (MonitorSampleCapacity - 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < values.Length; i++)
            {
                var x = width - (values.Length - 1 - i) * step;
                var y = height - 2 - Math.Clamp(values[i], 0, 100) / 100d * (height - 4);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, true);
            }
        }
        geometry.Freeze();
        var pen = new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round };
        context.DrawGeometry(null, pen, geometry);
    }

    private const int MonitorSampleCapacity = 120;
}
