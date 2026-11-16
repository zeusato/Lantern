using System.IO;
using System.Windows;
using Lantern.Core;
using Microsoft.Win32;

namespace Lantern.App;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = "";
    public static AppSettings Settings { get; private set; } = new();
    public static bool TestMode { get; private set; }
    public static WorkspaceVault Vault { get; private set; } = null!;
    private FileStream? profileLock;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var index = Array.IndexOf(e.Args, "--data");
        DataDirectory = index >= 0 && index + 1 < e.Args.Length ? Path.GetFullPath(e.Args[index + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lantern");
        TestMode = e.Args.Contains("--local-test");
        Directory.CreateDirectory(DataDirectory);
        try { profileLock = new FileStream(Path.Combine(DataDirectory, "profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { MessageBox.Show("Hồ sơ này đang mở trong một cửa sổ Lantern khác.", "Lantern"); Shutdown(); return; }
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(path)) Settings = Codec.Decode<AppSettings>(File.ReadAllText(path));
            if (string.IsNullOrWhiteSpace(Settings.Name)) Settings.Name = Environment.UserName;
            ApplyTheme(Settings.Theme);
            var window = new MainWindow(requireLogin: true); MainWindow = window; ShutdownMode = ShutdownMode.OnMainWindowClose; window.Show();
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine);
            MessageBox.Show("Không mở được hồ sơ. Chi tiết nằm trong errors.log.\n" + ex.Message, "Lantern"); Shutdown(1);
        }
    }
    public static void SaveSettings()
    {
        var file = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(file + ".tmp", Codec.Encode(Settings)); File.Move(file + ".tmp", file, true);
    }
    public static void ResetSettings() => Settings = new AppSettings { Name = Environment.UserName };
    internal static void SetUnlockedVault(WorkspaceVault vault) => Vault = vault;
    public static void ApplyTheme(string mode)
    {
        var app = Current;
        app.ThemeMode = mode switch { "dark" => ThemeMode.Dark, "light" => ThemeMode.Light, _ => ThemeMode.System };
        bool dark = mode == "dark" || mode == "system" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0;
        var colors = dark ? new[] { "#101A21", "#17242D", "#20313C", "#EBF2F5", "#A5B6C2", "#2D404D", "#2DAA9C", "#153E3B", "#1D4945" } : new[] { "#F6F8FA", "#FFFFFF", "#EDF2F5", "#17242E", "#647580", "#E2E8EC", "#087F78", "#DFF3ED", "#DFF3ED" };
        string[] keys = ["CanvasBrush", "SurfaceBrush", "SoftBrush", "TextBrush", "MutedBrush", "LineBrush", "AccentBrush", "AccentSoftBrush", "OwnBrush"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]));
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "CanvasBrush", "SurfaceBrush", "SoftBrush", "OwnBrush", "AccentSoftBrush" }) app.Resources[key] = SystemColors.WindowBrush;
            app.Resources["TextBrush"] = SystemColors.WindowTextBrush; app.Resources["MutedBrush"] = SystemColors.WindowTextBrush; app.Resources["LineBrush"] = SystemColors.WindowTextBrush; app.Resources["AccentBrush"] = SystemColors.HighlightBrush;
        }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is MainWindow window) window.PrepareForSystemShutdown();
        base.OnSessionEnding(e);
    }
    protected override void OnExit(ExitEventArgs e) { Vault?.Dispose(); profileLock?.Dispose(); base.OnExit(e); }
}
