using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace MobaDesk.Services;

/// <summary>
/// "Elegant" adds soft entrance/transition animations and translucent surfaces;
/// "Simple" keeps everything instant and flat. Both keep the chosen window material.
/// </summary>
public static class UiModeService
{
    public const string Elegant = "Elegant";
    public const string Simple = "Simple";

    /// <summary>Tracks the storyboard currently running per element so rapid toggles don't overlap.</summary>
    private static readonly ConditionalWeakTable<FrameworkElement, Storyboard> RunningStoryboards = new();

    public static bool IsElegant(string? mode) =>
        !string.Equals(mode, Simple, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureList(ListView list, string mode)
    {
        list.ItemContainerTransitions.Clear();
        if (IsElegant(mode))
        {
            list.ItemContainerTransitions.Add(new RepositionThemeTransition());
        }
    }

    /// <summary>Fades and lifts an element into view. Instant in simple mode.</summary>
    public static void PlayEntrance(FrameworkElement element, string mode, double offset = 10,
        int durationMs = 260)
    {
        if (!IsElegant(mode) || !element.IsLoaded)
        {
            element.Opacity = 1;
            ResetTranslate(element);
            return;
        }

        var transform = EnsureTransform(element);
        transform.TranslateY = offset;
        element.Opacity = 0;

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = offset,
            To = 0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "TranslateY");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        BeginTracked(element, storyboard);
    }

    /// <summary>Shows or hides the sidebar with a short fade/slide in elegant mode.</summary>
    public static void SetSidebarVisible(FrameworkElement sidebar, ColumnDefinition column,
        bool visible, string mode)
    {
        if (!IsElegant(mode))
        {
            sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            column.Width = visible ? new GridLength(296) : new GridLength(0);
            sidebar.Opacity = 1;
            ResetTranslate(sidebar);
            return;
        }

        var transform = EnsureTransform(sidebar);

        if (visible)
        {
            sidebar.Visibility = Visibility.Visible;
            column.Width = new GridLength(296);
            sidebar.Opacity = 0;
            transform.TranslateX = -28;
            AnimateSidebar(sidebar, transform, 1, 0, 220);
        }
        else
        {
            AnimateSidebar(sidebar, transform, 0, -28, 180, () =>
            {
                sidebar.Visibility = Visibility.Collapsed;
                column.Width = new GridLength(0);
                sidebar.Opacity = 1;
                transform.TranslateX = 0;
            });
        }
    }

    private static void AnimateSidebar(FrameworkElement sidebar, CompositeTransform transform,
        double opacity, double offsetX, int durationMs, Action? completed = null)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            To = opacity,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fade, sidebar);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            To = offsetX,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "TranslateX");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        BeginTracked(sidebar, storyboard, completed);
    }

    private static void BeginTracked(FrameworkElement owner, Storyboard storyboard, Action? completed = null)
    {
        if (RunningStoryboards.TryGetValue(owner, out var previous))
        {
            previous.Stop();
            RunningStoryboards.Remove(owner);
        }

        RunningStoryboards.Add(owner, storyboard);
        storyboard.Completed += (_, _) =>
        {
            if (RunningStoryboards.TryGetValue(owner, out var current) &&
                ReferenceEquals(current, storyboard))
            {
                RunningStoryboards.Remove(owner);
            }

            completed?.Invoke();
        };

        storyboard.Begin();
    }

    private static CompositeTransform EnsureTransform(FrameworkElement element)
    {
        if (element.RenderTransform is CompositeTransform transform)
        {
            return transform;
        }

        transform = new CompositeTransform();
        element.RenderTransform = transform;
        return transform;
    }

    private static void ResetTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is CompositeTransform transform)
        {
            transform.TranslateX = 0;
            transform.TranslateY = 0;
        }
    }
}
