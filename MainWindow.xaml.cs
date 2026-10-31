using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCTrainer;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        // WPF work-area dimensions already account for Windows display scaling.
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1, workArea.Width - 24);
        MaxHeight = Math.Max(1, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        PopulateLevels();
        RefreshGameStatus();
        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _timer.Tick -= Timer_Tick; };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private const string GameDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\Splinter Cell";
    private sealed record LevelChoice(string Map, string Label);

    private void PopulateLevels()
    {
        try
        {
            var directory = System.IO.Path.Combine(GameDirectory, "maps");
            var levels = System.IO.Directory.GetFiles(directory, "*.scl")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => {
                    var map = System.IO.Path.GetFileNameWithoutExtension(path);
                    return new LevelChoice(map, FriendlyName(map));
                }).ToArray();
            LevelPicker.ItemsSource = levels;
            if (levels.Length > 0) LevelPicker.SelectedIndex = 0;
            else LevelStatus.Text = "No installed maps found.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            LevelStatus.Text = "Cannot read installed maps: " + ex.Message;
        }
    }

    private static string FriendlyName(string map)
    {
        string mission = map.Contains("Training") ? "Training"
            : map.StartsWith("1_1_") ? "Police Station"
            : map.StartsWith("1_2_") ? "Defense Ministry"
            : map.StartsWith("1_3_") ? "Oil Refinery"
            : map.StartsWith("2_1_") ? "CIA HQ"
            : map.StartsWith("2_2_") ? "Kalinatek"
            : map.StartsWith("4_1_") ? "Chinese Embassy"
            : map.StartsWith("4_2_") ? "Abattoir"
            : map.StartsWith("4_3_") ? "Chinese Embassy — Return"
            : map.StartsWith("5_1_") ? "Presidential Palace" : "Additional map";
        return $"{mission}  /  {map}";
    }

    private async void LoadLevel_Click(object sender, RoutedEventArgs e)
    {
        if (LevelPicker.SelectedItem is not LevelChoice level) return;
        try
        {
            var games = Process.GetProcessesByName("SplinterCell");
            try
            {
                if (games.Length > 0)
                {
                    if (games.Length != 1) { LevelStatus.Text = "More than one game process found. Keep only one running."; return; }
                    if (MessageBox.Show(this, "Load " + level.Label + "? Unsaved mission progress will be lost.",
                        "Load level", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    LoadLevelButton.IsEnabled = false;
                    LevelStatus.Text = "Requesting map travel…";
                    try { LevelStatus.Text = await DirectLevelLoader.RequestAsync(games[0], level.Map); }
                    finally { LoadLevelButton.IsEnabled = true; }
                    return;
                }
            }
            finally { foreach (var game in games) game.Dispose(); }

            var system = System.IO.Path.Combine(GameDirectory, "system");
            var exe = System.IO.Path.Combine(system, "splintercell.exe");
            var mapPath = System.IO.Path.Combine(GameDirectory, "maps", level.Map + ".scl");
            if (!System.IO.File.Exists(exe) || !System.IO.File.Exists(mapPath))
            {
                LevelStatus.Text = "Game executable or selected map is missing.";
                return;
            }
            var start = new ProcessStartInfo(exe) { WorkingDirectory = system, UseShellExecute = false };
            start.ArgumentList.Add(level.Map + ".scl");
            using var launched = Process.Start(start);
            LevelStatus.Text = "Launch requested: " + level.Label + ". Waiting for the game.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            LevelStatus.Text = "Could not launch level: " + ex.Message;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var games = Process.GetProcessesByName("SplinterCell");
        try
        {
            if (games.Length != 1) { LevelStatus.Text = "Start one copy of Splinter Cell and wait for the main menu."; return; }
            TestConnectionButton.IsEnabled = false;
            LevelStatus.Text = await DirectLevelLoader.RequestAsync(games[0], "--probe");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        { LevelStatus.Text = ex.Message; }
        finally { TestConnectionButton.IsEnabled = true; foreach (var game in games) game.Dispose(); }
    }
    private void Timer_Tick(object? sender, EventArgs e) => RefreshGameStatus();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshGameStatus();

    private void RefreshGameStatus()
    {
        try
        {
            var games = Process.GetProcessesByName("SplinterCell");
            try
            {
                var running = games.Length > 0;
                GameStatus.Text = running ? "Game detected" : "Waiting for Splinter Cell";
                GameDetail.Text = running
                    ? "SplinterCell.exe is running • Direct level loading available"
                    : "Launch the game through Steam. Detection updates automatically.";
                StatusDot.Fill = new SolidColorBrush(running ? Color.FromRgb(181, 239, 101) : Color.FromRgb(138, 149, 139));
            }
            finally
            {
                foreach (var game in games) game.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            GameStatus.Text = "Unable to check game status";
            GameDetail.Text = "Press Refresh to try again.";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(224, 175, 99));
        }
    }
}
