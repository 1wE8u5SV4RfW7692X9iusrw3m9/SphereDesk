using System.Windows;

namespace SphereDesk.App;

public partial class PromptWindow : Window
{
    private PromptWindow(string title, string label, string? initial)
    {
        InitializeComponent();
        DataContext = new PromptText(title, label);
        InputBox.Text = initial ?? string.Empty;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public static string? Ask(Window owner, string title, string label, string? initial)
    {
        var window = new PromptWindow(title, label, initial) { Owner = owner };
        return window.ShowDialog() == true ? window.InputBox.Text : null;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private sealed record PromptText(string TitleText, string LabelText);
}
