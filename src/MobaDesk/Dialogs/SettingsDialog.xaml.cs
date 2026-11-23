using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;

namespace MobaDesk.Dialogs;

public sealed partial class SettingsDialog : ContentDialog
{
    public const int AboutCategory = 4;

    private readonly AppSettings _original;
    private FrameworkElement[] _panels = Array.Empty<FrameworkElement>();
    private FrameworkElement? _dialogCard;
    private bool _initializing = true;
    private bool _committed;

    // Title-bar dragging.
    private bool _dragging;
    private Windows.Foundation.Point _dragStart;
    private Vector3 _dragBase;
    private double _cardLeft;
    private double _cardTop;

    public AppSettings? Result { get; private set; }

    /// <summary>Set when the user asked to download and install an available update.</summary>
    public UpdateCheckResult? PendingUpdate { get; private set; }

    private UpdateCheckResult? _availableUpdate;

    public SettingsDialog(AppSettings current, int initialCategory = 0)
    {
        InitializeComponent();
        _original = current.Clone();

        PrimaryButtonText = Localization.T("Common.Save");
        CloseButtonText = Localization.T("Common.Cancel");
        DefaultButton = ContentDialogButton.Primary;
        TitleText.Text = Localization.T("Settings.Title");

        FontSizeBox.Value = current.TerminalFontSize;
        ScrollbackBox.Value = current.ScrollbackLines;
        MonitorIntervalBox.Value = current.MonitorIntervalSeconds;
        ConnectionTimeoutBox.Value = current.ConnectionTimeoutSeconds;
        CopyOnSelectCheck.IsChecked = current.CopyOnSelect;

        AboutVersionText.Text = $"v{UpdateService.CurrentVersion}";
        UpdateFeedBox.Text = current.UpdateFeedUrl;
        AutoUpdateCheck.IsChecked = current.CheckUpdatesOnStartup;

        ThemeCombo.SelectedIndex = current.Theme switch
        {
            "Dark" => 1,
            "Light" => 2,
            _ => 0
        };

        MaterialCombo.SelectedIndex = current.Material switch
        {
            "MicaAlt" => 1,
            "Acrylic" => 2,
            "Solid" => 3,
            _ => 0
        };

        InterfaceModeCombo.SelectedIndex = current.InterfaceMode == UiModeService.Simple ? 1 : 0;

        LanguageCombo.SelectedIndex = Localization.IsEnglish ? 1 : 0;

        _initializing = false;
        RequestedTheme = ThemeService.ToElementTheme(current.Theme);

        _panels = new FrameworkElement[]
        {
            AppearancePanel, TerminalPanel, ConnectionsPanel, GeneralPanel, AboutPanel
        };
        CategoryList.SelectedIndex = Math.Clamp(initialCategory, 0, _panels.Length - 1);
        CategoryList.SelectionChanged += (_, _) => ShowCategory();
        ShowCategory();

        // The whole header band of the card behaves like a window title bar.
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnDialogPointerPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnDialogPointerMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnDialogPointerReleased), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnDialogPointerCaptureLost), true);

        ThemeCombo.SelectionChanged += (_, _) => PreviewChanges();
        MaterialCombo.SelectionChanged += (_, _) => PreviewChanges();
        InterfaceModeCombo.SelectionChanged += (_, _) => PreviewChanges();
        LanguageCombo.SelectionChanged += (_, _) => PreviewChanges();

        PrimaryButtonClick += OnSaveClick;
        Opened += (_, _) =>
        {
            if (Content is DependencyObject content)
            {
                Loc.Register(content);
            }

            UpdateTexts();
        };
        Closed += (_, _) =>
        {
            Localization.LanguageChanged -= OnLanguageChanged;
            if (!_committed)
            {
                Revert();
            }
        };

        Localization.LanguageChanged += OnLanguageChanged;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dialogCard = GetTemplateChild("BackgroundElement") as FrameworkElement;

        // The WinUI dialog template leaves the card square; match the main window's rounded frame.
        if (_dialogCard is Border card && card.CornerRadius.TopLeft == 0)
        {
            card.CornerRadius = new CornerRadius(8);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateTexts();

    private void OnDialogPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dialogCard == null || _dragging)
        {
            return;
        }

        var cardPoint = e.GetCurrentPoint(_dialogCard);
        if (!cardPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (cardPoint.Position.X < 0 || cardPoint.Position.X > _dialogCard.ActualWidth ||
            cardPoint.Position.Y < 0 || cardPoint.Position.Y > HeaderBottom())
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        _dragBase = _dialogCard.Translation;
        var origin = _dialogCard.TransformToVisual(this)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        _cardLeft = origin.X - _dragBase.X;
        _cardTop = origin.Y - _dragBase.Y;
        _dragStart = point.Position;
        _dragging = true;

        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnDialogPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _dialogCard == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        var minX = -_cardLeft;
        var maxX = Math.Max(minX, ActualWidth - _cardLeft - _dialogCard.ActualWidth);
        var minY = -_cardTop;
        var maxY = Math.Max(minY, ActualHeight - _cardTop - _dialogCard.ActualHeight);

        var x = Math.Clamp(_dragBase.X + point.Position.X - _dragStart.X, minX, maxX);
        var y = Math.Clamp(_dragBase.Y + point.Position.Y - _dragStart.Y, minY, maxY);
        _dialogCard.Translation = new Vector3((float)x, (float)y, 0);
        e.Handled = true;
    }

    private void OnDialogPointerReleased(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void OnDialogPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _dragging = false;

    /// <summary>Bottom edge (card-relative) of the draggable title band.</summary>
    private double HeaderBottom()
    {
        if (Content is FrameworkElement content && _dialogCard != null)
        {
            return content.TransformToVisual(_dialogCard)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        }

        return 48;
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCaptures();
    }

    private void ShowCategory()
    {
        var selected = Math.Clamp(CategoryList.SelectedIndex, 0, _panels.Length - 1);
        for (var i = 0; i < _panels.Length; i++)
        {
            _panels[i].Visibility = i == selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateTexts()
    {
        TitleText.Text = Localization.T("Settings.Title");
        PrimaryButtonText = Localization.T("Common.Save");
        CloseButtonText = Localization.T("Common.Cancel");

        Loc.ApplyTree(DragHeader);
        Loc.ApplyTree(Content as DependencyObject);
        foreach (var item in CategoryList.Items)
        {
            Loc.ApplyTree(item as DependencyObject);
        }

        foreach (var item in ThemeCombo.Items)
        {
            Loc.ApplyTree(item as DependencyObject);
        }

        foreach (var item in MaterialCombo.Items)
        {
            Loc.ApplyTree(item as DependencyObject);
        }

        foreach (var item in InterfaceModeCombo.Items)
        {
            Loc.ApplyTree(item as DependencyObject);
        }

        foreach (var item in LanguageCombo.Items)
        {
            Loc.ApplyTree(item as DependencyObject);
        }

        MaterialInfoBar.Message = Localization.T("Settings.MaterialFallback");
    }

    private void PreviewChanges()
    {
        if (_initializing)
        {
            return;
        }

        var theme = SelectedTheme();
        RequestedTheme = ThemeService.ToElementTheme(theme);
        App.Shell?.ApplyTheme(theme);
        var supported = App.Shell?.ApplyMaterial(SelectedMaterial()) ?? true;
        App.Shell?.ApplyInterfaceMode(SelectedInterfaceMode());

        MaterialInfoBar.IsOpen = !supported;
        MaterialInfoBar.Message = Localization.T("Settings.MaterialFallback");

        Localization.SetLanguage(SelectedLanguage());
        UpdateTexts();
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = BuildResult();
        _committed = true;
    }

    private AppSettings BuildResult() => new()
    {
        TerminalFontSize = ToInt(FontSizeBox.Value, _original.TerminalFontSize),
        ScrollbackLines = ToInt(ScrollbackBox.Value, _original.ScrollbackLines),
        MonitorIntervalSeconds = ToInt(MonitorIntervalBox.Value, _original.MonitorIntervalSeconds),
        ConnectionTimeoutSeconds = ToInt(ConnectionTimeoutBox.Value, _original.ConnectionTimeoutSeconds),
        TerminalFontFamily = _original.TerminalFontFamily,
        CopyOnSelect = CopyOnSelectCheck.IsChecked == true,
        Theme = SelectedTheme(),
        Material = SelectedMaterial(),
        InterfaceMode = SelectedInterfaceMode(),
        Language = SelectedLanguage(),
        UpdateFeedUrl = (UpdateFeedBox.Text ?? "").Trim(),
        CheckUpdatesOnStartup = AutoUpdateCheck.IsChecked == true,
        LastUpdateCheckUtc = _original.LastUpdateCheckUtc
    };

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        var feed = UpdateService.ResolveFeed(UpdateFeedBox.Text);
        if (string.IsNullOrWhiteSpace(feed))
        {
            UpdateStatusText.Text = Localization.T("Update.FeedRequired");
            return;
        }

        _availableUpdate = null;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Localization.T("Update.Checking");

        try
        {
            var result = await UpdateService.CheckAsync(feed, CancellationToken.None);
            if (result.IsNewer)
            {
                _availableUpdate = result;
                UpdateStatusText.Text = Localization.T("Update.Available", result.Manifest.Version);
                InstallUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = Localization.T("Update.UpToDate", result.Manifest.Version);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = Localization.T("Update.CheckFailed", ex.Message);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null)
        {
            return;
        }

        PendingUpdate = _availableUpdate;
        Result = BuildResult();
        _committed = true;
        Hide();
    }

    private void Revert()
    {
        App.Shell?.ApplyTheme(_original.Theme);
        App.Shell?.ApplyMaterial(_original.Material);
        App.Shell?.ApplyInterfaceMode(_original.InterfaceMode);
        Localization.SetLanguage(_original.Language);
    }

    private string SelectedTheme() =>
        (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";

    private string SelectedMaterial() =>
        (MaterialCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Mica";

    private string SelectedInterfaceMode() =>
        (InterfaceModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? UiModeService.Elegant;

    private string SelectedLanguage() =>
        (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? Localization.Chinese;

    private static int ToInt(double value, int fallback) =>
        double.IsNaN(value) ? fallback : (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
