using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MobaDesk.Services;

namespace MobaDesk.Dialogs;

public sealed partial class PasswordDialog : ContentDialog
{
    public string Password => PasswordInput.Password;

    public bool RememberPassword => RememberCheck.IsChecked == true;

    public PasswordDialog(string title, string message, string? primaryButtonText = null,
        string? passwordPlaceholder = null, bool allowRemember = true)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        PrimaryButtonText = primaryButtonText ?? Localization.T("Password.Connect");
        CloseButtonText = Localization.T("Common.Cancel");
        DefaultButton = ContentDialogButton.Primary;
        PasswordInput.PlaceholderText = passwordPlaceholder ?? Localization.T("Common.Password");
        RememberCheck.Visibility = allowRemember ? Visibility.Visible : Visibility.Collapsed;

        Opened += (_, _) => PasswordInput.Focus(FocusState.Programmatic);
        PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrEmpty(PasswordInput.Password))
            {
                args.Cancel = true;
            }
        };
    }
}
