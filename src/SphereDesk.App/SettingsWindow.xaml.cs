using System.Windows;
using System.Windows.Input;
using SphereDesk.Core.Models;

namespace SphereDesk.App;

public partial class SettingsWindow : Window
{
    private HotkeyGesture _hotkey = HotkeyGesture.Default;

    private SettingsWindow(UserSettings settings)
    {
        InitializeComponent();
        LaunchAtStartupBox.IsChecked = settings.LaunchAtStartup;
        AutoRotateBox.IsChecked = settings.AutoRotateEnabled;
        SpeedSlider.Value = settings.AngularSpeed;
        ReducedMotionBox.IsChecked = settings.ReducedMotion;
        _hotkey = settings.Hotkey;
        HotkeyBox.Text = Describe(_hotkey);
    }

    public static UserSettings? Edit(Window owner, UserSettings settings)
    {
        var window = new SettingsWindow(settings.Clone()) { Owner = owner };
        var accepted = window.ShowDialog() == true;
        if (!accepted)
        {
            return null;
        }

        var edited = settings.Clone();
        edited.LaunchAtStartup = window.LaunchAtStartupBox.IsChecked == true;
        edited.AutoRotateEnabled = window.AutoRotateBox.IsChecked == true;
        edited.AngularSpeed = window.SpeedSlider.Value;
        edited.ReducedMotion = window.ReducedMotionBox.IsChecked == true;
        edited.MotionPreferenceUserSet = true;
        edited.Hotkey = window._hotkey;
        return edited;
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab or Key.Escape)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            HotkeyBox.Text = "请包含 Ctrl、Alt 或 Shift";
            return;
        }

        var gesture = new HotkeyGesture(ToModifiers(Keyboard.Modifiers), KeyInterop.VirtualKeyFromKey(key));
        if (!gesture.IsValid)
        {
            HotkeyBox.Text = "这个组合无法注册";
            return;
        }

        _hotkey = gesture;
        HotkeyBox.Text = Describe(gesture);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static int ToModifiers(ModifierKeys modifiers)
    {
        var value = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            value |= HotkeyGesture.ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            value |= HotkeyGesture.ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            value |= HotkeyGesture.ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            value |= HotkeyGesture.ModWin;
        }

        return value;
    }

    private static string Describe(HotkeyGesture gesture)
    {
        var parts = new List<string>();
        if ((gesture.Modifiers & HotkeyGesture.ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((gesture.Modifiers & HotkeyGesture.ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((gesture.Modifiers & HotkeyGesture.ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((gesture.Modifiers & HotkeyGesture.ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(KeyInterop.KeyFromVirtualKey(gesture.VirtualKey).ToString());
        return string.Join("+", parts);
    }
}
