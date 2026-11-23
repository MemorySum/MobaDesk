using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MobaDesk.Models;
using MobaDesk.Ui;

namespace MobaDesk.Views;

/// <summary>One SSH terminal inside a concurrent tab, with its own title header and close button.</summary>
public sealed partial class ConcurrentPane : UserControl
{
    public ConcurrentPane(ConnectionProfile profile, SshTerminalView terminal)
    {
        InitializeComponent();

        Profile = profile;
        Terminal = terminal;

        TitleText.Text = profile.Name;
        EndpointText.Text = profile.Subtitle;
        TerminalHost.Children.Add(terminal);

        Loc.Register(this);
    }

    public ConnectionProfile Profile { get; }

    public SshTerminalView Terminal { get; }

    public event Action<ConcurrentPane>? CloseRequested;

    public void UpdateTitle()
    {
        TitleText.Text = Profile.Name;
        EndpointText.Text = Profile.Subtitle;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);
}
