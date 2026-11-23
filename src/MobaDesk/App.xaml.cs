using Microsoft.UI.Xaml;
using MobaDesk.Models;
using MobaDesk.Services;

namespace MobaDesk;

public partial class App : Application
{
    public static ProfileStore ProfileStore { get; } = new();

    public static AppSettings Settings { get; set; } = AppSettings.Load();

    public static MainWindow? Shell { get; private set; }

    public App()
    {
        Directory.CreateDirectory(AppPaths.WebViewDataDirectory);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", AppPaths.WebViewDataDirectory);

        InitializeComponent();

        Localization.SetLanguage(Settings.Language, notify: false);
        SshAuth.HostKeyVerifier = HostKeyVerifier.Verify;

        UnhandledException += (_, e) =>
        {
            var details = e.Exception?.ToString() ?? e.Message;
            Log.Error("Unhandled UI exception\n" + details);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        ProfileStore.Load();
        Log.Info("MobaDesk started.");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Shell = new MainWindow();
        Shell.Activate();
    }
}
