using System.Windows;
using System.Windows.Input;
using JmComic.App.Services;
using JmComic.Core.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Dialogs;

/// <summary>模态登录对话框：账号 + 密码，成功后关闭。</summary>
public partial class LoginDialog : Window
{
    private readonly SessionService _session;

    public LoginDialog()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<SessionService>();
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = TryLoginAsync();
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

    private async Task TryLoginAsync()
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("请输入账号和密码");
            return;
        }

        LoginButton.IsEnabled = false;
        BusyIcon.Visibility = Visibility.Visible;
        BusyText.Visibility = Visibility.Visible;
        NormalText.Visibility = Visibility.Collapsed;
        ErrorCapsule.Visibility = Visibility.Collapsed;
        try
        {
            await _session.LoginAsync(username, password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(JmErrorClassifier.Classify(ex).Message);
            LoginButton.IsEnabled = true;
            BusyIcon.Visibility = Visibility.Collapsed;
            BusyText.Visibility = Visibility.Collapsed;
            NormalText.Visibility = Visibility.Visible;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorCapsule.Visibility = Visibility.Visible;
    }
}

