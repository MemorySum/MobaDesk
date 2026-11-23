using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace MobaDesk.Controls;

/// <summary>
/// Lightweight real-time line chart (no charting dependency).
/// </summary>
public sealed partial class Sparkline : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly Polyline _line = new();
    private readonly Polygon _area = new();
    private readonly List<double> _values = new();
    private readonly SolidColorBrush _stroke = new(Color.FromArgb(255, 56, 189, 248));
    private readonly LinearGradientBrush _fill;
    private double _maximum = 100;
    private double _minimum;
    private bool _autoScale;

    public Sparkline()
    {
        _line.StrokeThickness = 1.8;
        _line.StrokeLineJoin = PenLineJoin.Round;
        _line.Stroke = _stroke;

        _fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        _fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(90, 56, 189, 248), Offset = 0 });
        _fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 56, 189, 248), Offset = 1 });
        _area.Fill = _fill;

        _canvas.Children.Add(_area);
        _canvas.Children.Add(_line);
        Content = _canvas;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => Redraw();
    }

    public int Capacity { get; set; } = 90;

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value <= 0 ? 1 : value;
            Redraw();
        }
    }

    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Redraw();
        }
    }

    public bool AutoScale
    {
        get => _autoScale;
        set
        {
            _autoScale = value;
            Redraw();
        }
    }

    public Color Color
    {
        get => _stroke.Color;
        set
        {
            _stroke.Color = value;
            _fill.GradientStops[0].Color = Color.FromArgb(90, value.R, value.G, value.B);
            _fill.GradientStops[1].Color = Color.FromArgb(0, value.R, value.G, value.B);
        }
    }

    public double StrokeThickness
    {
        get => _line.StrokeThickness;
        set => _line.StrokeThickness = value;
    }

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0;
        }

        _values.Add(value);
        if (_values.Count > Capacity)
        {
            _values.RemoveRange(0, _values.Count - Capacity);
        }

        Redraw();
    }

    public void Clear()
    {
        _values.Clear();
        Redraw();
    }

    private void Redraw()
    {
        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (width < 4 || height < 4)
        {
            return;
        }

        if (_values.Count == 0)
        {
            _line.Points.Clear();
            _area.Points.Clear();
            return;
        }

        var max = _maximum;
        if (_autoScale)
        {
            max = Math.Max(1, _values.Max() * 1.2);
        }

        var range = Math.Max(0.0001, max - _minimum);
        var stepX = width / Math.Max(1, Capacity - 1);
        var startIndex = Capacity - _values.Count;
        var points = new PointCollection();
        for (var i = 0; i < _values.Count; i++)
        {
            var x = (startIndex + i) * stepX;
            var normalized = Math.Clamp((_values[i] - _minimum) / range, 0, 1);
            var y = height - Math.Clamp(normalized, 0, 1) * (height - 2) - 1;
            points.Add(new Point(x, y));
        }

        _line.Points = points;

        var area = new PointCollection { new Point(points[0].X, height) };
        foreach (var point in points)
        {
            area.Add(point);
        }

        area.Add(new Point(points[^1].X, height));
        _area.Points = area;
        _area.Visibility = _values.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
