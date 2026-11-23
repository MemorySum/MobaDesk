using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MobaDesk.Controls;

/// <summary>A slim draggable handle for resizing adjacent panes top/bottom.</summary>
public sealed class HorizontalResizeHandle : Grid
{
    private bool _dragging;

    public HorizontalResizeHandle()
    {
        Height = 8;
        Background = new SolidColorBrush(Color.FromArgb(16, 0, 0, 0));
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

        Children.Add(new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(96, 148, 163, 184))
        });

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    /// <summary>Raised with the pointer Y relative to the handle's parent while dragging.</summary>
    public event EventHandler<double>? DragMoved;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var parent = Parent as UIElement ?? this;
        DragMoved?.Invoke(this, e.GetCurrentPoint(parent).Position.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => _dragging = false;
}
