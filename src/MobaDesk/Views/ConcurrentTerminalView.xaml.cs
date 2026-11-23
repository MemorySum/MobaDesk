using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MobaDesk.Models;

namespace MobaDesk.Views;

public enum TerminalSplitOrientation
{
    SideBySide,
    Stacked
}

/// <summary>
/// Hosts several SSH terminals in a single tab. Panes flow two per row; a lone trailing
/// pane stretches across the remaining width instead of leaving an empty cell.
/// </summary>
public sealed partial class ConcurrentTerminalView : UserControl, IAsyncDisposable
{
    private const int ColumnCount = 2;
    private const double SplitterWidth = 8;
    private const double SplitterHeight = 8;

    private readonly List<ConcurrentPane> _panes = new();
    private readonly TerminalSplitOrientation _orientation;
    private double _leftRatio = 0.5;
    private double _topRatio = 0.5;

    public ConcurrentTerminalView(TerminalSplitOrientation orientation = TerminalSplitOrientation.SideBySide)
    {
        InitializeComponent();
        _orientation = orientation;
        ColumnSplitter.DragMoved += OnColumnSplitterDragMoved;
        RowSplitter.DragMoved += OnRowSplitterDragMoved;
    }

    public IReadOnlyList<SshTerminalView> Terminals => _panes.Select(pane => pane.Terminal).ToList();

    public int Count => _panes.Count;

    public bool Contains(SshTerminalView terminal) =>
        _panes.Any(pane => ReferenceEquals(pane.Terminal, terminal));

    /// <summary>Raised after a pane is removed, so the shell can refresh counters.</summary>
    public event Action? PanesChanged;

    /// <summary>Raised when the last pane has been closed.</summary>
    public event Action? Empty;

    public void Add(ConnectionProfile profile, SshTerminalView terminal)
    {
        var pane = new ConcurrentPane(profile, terminal);
        pane.CloseRequested += OnPaneCloseRequested;

        _panes.Add(pane);
        LayoutGrid.Children.Add(pane);
        Relayout();
        PanesChanged?.Invoke();
    }

    public void UpdateTitle(ConnectionProfile profile)
    {
        foreach (var pane in _panes.Where(pane => ReferenceEquals(pane.Profile, profile)))
        {
            pane.UpdateTitle();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pane in _panes)
        {
            pane.CloseRequested -= OnPaneCloseRequested;
            await pane.Terminal.DisposeAsync();
        }

        _panes.Clear();
        LayoutGrid.Children.Clear();
    }

    private async void OnPaneCloseRequested(ConcurrentPane pane)
    {
        if (!_panes.Remove(pane))
        {
            return;
        }

        pane.CloseRequested -= OnPaneCloseRequested;
        LayoutGrid.Children.Remove(pane);
        Relayout();
        PanesChanged?.Invoke();

        await pane.Terminal.DisposeAsync();

        if (_panes.Count == 0)
        {
            Empty?.Invoke();
        }
    }

    private void Relayout()
    {
        LayoutGrid.RowDefinitions.Clear();
        LayoutGrid.ColumnDefinitions.Clear();

        if (_orientation == TerminalSplitOrientation.Stacked && _panes.Count <= 2)
        {
            RelayoutStacked();
            return;
        }

        LayoutGrid.RowSpacing = 6;
        LayoutGrid.ColumnSpacing = 0;
        RowSplitter.Visibility = Visibility.Collapsed;

        LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(_leftRatio, GridUnitType.Star)
        });
        LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(SplitterWidth)
        });
        LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1 - _leftRatio, GridUnitType.Star)
        });

        var rows = Math.Max(1, (_panes.Count + ColumnCount - 1) / ColumnCount);
        for (var row = 0; row < rows; row++)
        {
            LayoutGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        }

        Grid.SetRow(ColumnSplitter, 0);
        Grid.SetColumn(ColumnSplitter, 1);
        Grid.SetRowSpan(ColumnSplitter, rows);
        ColumnSplitter.Visibility = _panes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        for (var index = 0; index < _panes.Count; index++)
        {
            var pane = _panes[index];
            var isLoneTrailingPane = _panes.Count % ColumnCount == 1 && index == _panes.Count - 1;

            Grid.SetRow(pane, index / ColumnCount);
            Grid.SetColumn(pane, index % ColumnCount == 0 ? 0 : 2);
            Grid.SetColumnSpan(pane, isLoneTrailingPane ? 3 : 1);
            Grid.SetRowSpan(pane, 1);
        }
    }

    private void RelayoutStacked()
    {
        LayoutGrid.RowSpacing = 0;
        LayoutGrid.ColumnSpacing = 0;
        ColumnSplitter.Visibility = Visibility.Collapsed;

        LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        LayoutGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(_topRatio, GridUnitType.Star)
        });
        LayoutGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(SplitterHeight)
        });
        LayoutGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1 - _topRatio, GridUnitType.Star)
        });

        Grid.SetRow(RowSplitter, 1);
        Grid.SetColumn(RowSplitter, 0);
        Grid.SetColumnSpan(RowSplitter, 1);
        RowSplitter.Visibility = _panes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        for (var index = 0; index < _panes.Count; index++)
        {
            var pane = _panes[index];
            Grid.SetRow(pane, index == 0 ? 0 : 2);
            Grid.SetColumn(pane, 0);
            Grid.SetColumnSpan(pane, 1);
            Grid.SetRowSpan(pane, _panes.Count == 1 ? 3 : 1);
        }
    }

    private void OnColumnSplitterDragMoved(object? sender, double pointerX)
    {
        var available = LayoutGrid.ActualWidth - LayoutGrid.Padding.Left - LayoutGrid.Padding.Right - SplitterWidth;
        if (available <= 0 || LayoutGrid.ColumnDefinitions.Count != 3)
        {
            return;
        }

        _leftRatio = Math.Clamp((pointerX - LayoutGrid.Padding.Left - SplitterWidth / 2) / available, 0.2, 0.8);
        LayoutGrid.ColumnDefinitions[0].Width = new GridLength(_leftRatio, GridUnitType.Star);
        LayoutGrid.ColumnDefinitions[2].Width = new GridLength(1 - _leftRatio, GridUnitType.Star);
    }

    private void OnRowSplitterDragMoved(object? sender, double pointerY)
    {
        var available = LayoutGrid.ActualHeight - LayoutGrid.Padding.Top - LayoutGrid.Padding.Bottom - SplitterHeight;
        if (available <= 0 || LayoutGrid.RowDefinitions.Count != 3)
        {
            return;
        }

        _topRatio = Math.Clamp((pointerY - LayoutGrid.Padding.Top - SplitterHeight / 2) / available, 0.2, 0.8);
        LayoutGrid.RowDefinitions[0].Height = new GridLength(_topRatio, GridUnitType.Star);
        LayoutGrid.RowDefinitions[2].Height = new GridLength(1 - _topRatio, GridUnitType.Star);
    }
}
