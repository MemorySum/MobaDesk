using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Services;

namespace MobaDesk.Ui;

/// <summary>
/// Attached properties that keep XAML text in sync with <see cref="Localization"/>.
/// Register visual roots so a language switch updates already-realized elements instantly.
/// </summary>
public static class Loc
{
    private static readonly List<WeakReference<DependencyObject>> Roots = new();

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Loc), new PropertyMetadata(null, OnAnyPropertyChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(Loc), new PropertyMetadata(null, OnAnyPropertyChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Loc), new PropertyMetadata(null, OnAnyPropertyChanged));

    public static readonly DependencyProperty ToolTipProperty = DependencyProperty.RegisterAttached(
        "ToolTip", typeof(string), typeof(Loc), new PropertyMetadata(null, OnAnyPropertyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    public static string? GetHeader(DependencyObject element) => (string?)element.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject element, string? value) => element.SetValue(HeaderProperty, value);

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    public static string? GetToolTip(DependencyObject element) => (string?)element.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject element, string? value) => element.SetValue(ToolTipProperty, value);

    public static void Register(DependencyObject root)
    {
        lock (Roots)
        {
            for (var i = Roots.Count - 1; i >= 0; i--)
            {
                if (!Roots[i].TryGetTarget(out _))
                {
                    Roots.RemoveAt(i);
                }
            }

            if (!Roots.Any(r => r.TryGetTarget(out var target) && ReferenceEquals(target, root)))
            {
                Roots.Add(new WeakReference<DependencyObject>(root));
            }
        }

        ApplyTree(root);
    }

    public static void ApplyTree(DependencyObject? root)
    {
        if (root == null)
        {
            return;
        }

        Apply(root);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    public static void ApplyAll()
    {
        List<DependencyObject> roots = new();
        lock (Roots)
        {
            for (var i = Roots.Count - 1; i >= 0; i--)
            {
                if (Roots[i].TryGetTarget(out var target))
                {
                    roots.Add(target);
                }
                else
                {
                    Roots.RemoveAt(i);
                }
            }
        }

        foreach (var root in roots)
        {
            ApplyTree(root);
        }
    }

    private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => Apply(d);

    private static void Apply(DependencyObject element)
    {
        var key = GetKey(element);
        if (!string.IsNullOrEmpty(key))
        {
            var text = Localization.T(key);
            switch (element)
            {
                case TextBlock textBlock:
                    textBlock.Text = text;
                    break;
                case MenuFlyoutItem menuItem:
                    menuItem.Text = text;
                    break;
                case MenuFlyoutSubItem menuSubItem:
                    menuSubItem.Text = text;
                    break;
                case AppBarButton appBarButton:
                    appBarButton.Label = text;
                    break;
                case ContentControl contentControl:
                    contentControl.Content = text;
                    break;
            }
        }

        var header = GetHeader(element);
        if (!string.IsNullOrEmpty(header))
        {
            var text = Localization.T(header);
            switch (element)
            {
                case TextBox textBox:
                    textBox.Header = text;
                    break;
                case PasswordBox passwordBox:
                    passwordBox.Header = text;
                    break;
                case ComboBox comboBox:
                    comboBox.Header = text;
                    break;
                case NumberBox numberBox:
                    numberBox.Header = text;
                    break;
            }
        }

        var placeholder = GetPlaceholder(element);
        if (!string.IsNullOrEmpty(placeholder))
        {
            var text = Localization.T(placeholder);
            switch (element)
            {
                case TextBox textBox:
                    textBox.PlaceholderText = text;
                    break;
                case PasswordBox passwordBox:
                    passwordBox.PlaceholderText = text;
                    break;
            }
        }

        var toolTip = GetToolTip(element);
        if (!string.IsNullOrEmpty(toolTip))
        {
            ToolTipService.SetToolTip(element, Localization.T(toolTip));
        }
    }
}
