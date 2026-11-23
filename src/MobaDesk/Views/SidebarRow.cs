using Microsoft.UI.Xaml;
using MobaDesk.Models;

namespace MobaDesk.Views;

/// <summary>
/// One flattened line in the session sidebar: either a folder header or a session.
/// </summary>
public sealed class SidebarRow
{
    private SidebarRow()
    {
    }

    public bool IsFolder { get; private init; }
    public ConnectionProfile? Profile { get; private init; }
    public string Folder { get; private init; } = "";
    public bool IsExpanded { get; private init; }
    public int Count { get; private init; }
    public double Indent { get; private init; }

    public string ChevronGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    public string CountText => Count.ToString();

    public Thickness RowMargin => new(Indent, 0, 0, 0);

    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProfileVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CountVisibility => Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static SidebarRow ForFolder(string name, int count, bool expanded) => new()
    {
        IsFolder = true,
        Folder = name,
        Count = count,
        IsExpanded = expanded
    };

    public static SidebarRow ForProfile(ConnectionProfile profile, double indent) => new()
    {
        Profile = profile,
        Folder = profile.Group ?? "",
        Indent = indent
    };
}
