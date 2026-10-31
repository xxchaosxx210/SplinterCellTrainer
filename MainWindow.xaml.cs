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
        _cheatControlsReady=true;
        // WPF work-area dimensions already account for Windows display scaling.
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1, workArea.Width - 24);
        MaxHeight = Math.Max(1, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        Closing += Window_Closing;
        PopulateLevels();
        RefreshGameStatus();
        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closed += (_, _) => { _enemyOverlay?.Close(); _timer.Stop(); _timer.Tick -= Timer_Tick; };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private EnemyOverlay? _enemyOverlay;

    private void EnemyEsp_Checked(object sender, RoutedEventArgs e)
    {
        var games = Process.GetProcessesByName("SplinterCell");
        EnemyMemoryReader? reader = null;
        try
        {
            if (games.Length != 1) throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission.");
            reader = new EnemyMemoryReader(games[0]);
            _enemyOverlay = new EnemyOverlay(reader, text => EspStatus.Text = text);
            _enemyOverlay.Closed += (_, _) =>
            {
                _enemyOverlay = null;
                EnemyEspToggle.IsChecked = false;
            };
            _enemyOverlay.Show();
            EspStatus.Text = "Enemy ESP enabled. Focus the game to display markers.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            reader?.Dispose();
            EnemyEspToggle.IsChecked = false;
            EspStatus.Text = ex.Message;
        }
        finally
        {
            foreach (var game in games)
                if (reader is null || _enemyOverlay is null) game.Dispose();
        }
    }

    private void EnemyEsp_Unchecked(object sender, RoutedEventArgs e)
    {
        var overlay = _enemyOverlay;
        _enemyOverlay = null;
        overlay?.Close();
        EspStatus.Text = "Enemy ESP is off.";
    }

    private InfiniteHealthSession? _healthSession;
    private bool _healthChanging;

    private async void InfiniteHealth_Changed(object sender, RoutedEventArgs e)
    {
        if (_healthChanging) return;
        await SetInfiniteHealthAsync(InfiniteHealthToggle.IsChecked == true);
    }

    private async Task<bool> SetInfiniteHealthAsync(bool enabled)
    {
        _healthChanging = true;
        InfiniteHealthToggle.IsEnabled = false;
        try
        {
            if (enabled)
            {
                var games = Process.GetProcessesByName("SplinterCell");
                if (games.Length != 1)
                {
                    foreach (var game in games) game.Dispose();
                    throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission first.");
                }
                try { _healthSession = await InfiniteHealthSession.EnableAsync(games[0]); }
                catch { games[0].Dispose(); throw; }
                HealthStatus.Text = "Infinite health is ON.";
            }
            else if (_healthSession is not null)
            {
                await _healthSession.DisableAsync();
                _healthSession.Dispose();
                _healthSession = null;
                HealthStatus.Text = "Infinite health is off.";
            }
            InfiniteHealthToggle.IsChecked = enabled;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or FormatException)
        {
            InfiniteHealthToggle.IsChecked = _healthSession is not null;
            HealthStatus.Text = ex.Message;
            return false;
        }
        finally { _healthChanging = false; InfiniteHealthToggle.IsEnabled = true; }
    }

    private void RefreshHealthStatus()
    {
        if (_healthChanging || _healthSession is null || _healthSession.IsCurrent()) return;
        _healthSession.Dispose();
        _healthSession = null;
        _healthChanging = true;
        InfiniteHealthToggle.IsChecked = false;
        _healthChanging = false;
        HealthStatus.Text = "Infinite health reset after the player changed. Enable it again in the new mission.";
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_healthChanging || _cheatsChanging || _cheatRefreshBusy) { e.Cancel=true; return; }
        if (_healthSession is null && _cheatSession is null) return;
        e.Cancel=true;
        if (!await ApplyCheatSettingsAsync(new(CheatOptions.None)))return;
        if (await SetInfiniteHealthAsync(false))Close();
    }
    private CheatSession? _cheatSession;
    private bool _cheatsChanging, _cheatRefreshBusy, _cheatControlsReady;

    private CheatSettings ReadCheatSettings()
    {
        CheatOptions options=CheatOptions.None;
        if(UnlimitedAmmoToggle.IsChecked==true)options|=CheatOptions.UnlimitedAmmo;
        if(NoReloadToggle.IsChecked==true)options|=CheatOptions.NoReload;
        if(UnlimitedGadgetsToggle.IsChecked==true)options|=CheatOptions.UnlimitedGadgets;
        if(ReducedDetectionToggle.IsChecked==true)options|=CheatOptions.ReducedDetection;
        if(MovementSpeedToggle.IsChecked==true)options|=CheatOptions.MovementSpeed;
        var selected=SpeedPicker.SelectedItem as System.Windows.Controls.ComboBoxItem;
        int speed=selected?.Tag is string value ? int.Parse(value,System.Globalization.CultureInfo.InvariantCulture) : 100;
        return new(options,speed);
    }

    private void SetCheatControls(bool available)
    {
        UnlimitedAmmoToggle.IsEnabled=NoReloadToggle.IsEnabled=UnlimitedGadgetsToggle.IsEnabled=
            ReducedDetectionToggle.IsEnabled=MovementSpeedToggle.IsEnabled=available;
        SpeedPicker.IsEnabled=available && MovementSpeedToggle.IsChecked==true;
    }

    private void ShowCheatSettings(CheatSettings settings)
    {
        UnlimitedAmmoToggle.IsChecked=settings.Options.HasFlag(CheatOptions.UnlimitedAmmo);
        NoReloadToggle.IsChecked=settings.Options.HasFlag(CheatOptions.NoReload);
        UnlimitedGadgetsToggle.IsChecked=settings.Options.HasFlag(CheatOptions.UnlimitedGadgets);
        ReducedDetectionToggle.IsChecked=settings.Options.HasFlag(CheatOptions.ReducedDetection);
        MovementSpeedToggle.IsChecked=settings.Options.HasFlag(CheatOptions.MovementSpeed);
        foreach(System.Windows.Controls.ComboBoxItem item in SpeedPicker.Items)
            if((string)item.Tag==settings.SpeedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture))SpeedPicker.SelectedItem=item;
    }

    private async void CheatOption_Changed(object sender,RoutedEventArgs e)
    {
        if(!_cheatControlsReady||_cheatsChanging)return;
        await ApplyCheatSettingsAsync(ReadCheatSettings());
    }

    private async void SpeedPicker_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(!_cheatControlsReady||_cheatsChanging||MovementSpeedToggle.IsChecked!=true)return;
        await ApplyCheatSettingsAsync(ReadCheatSettings());
    }

    private async Task<bool> ApplyCheatSettingsAsync(CheatSettings settings)
    {
        if(_cheatsChanging)return false;
        _cheatsChanging=true;SetCheatControls(false);
        try
        {
            if(settings.Options==CheatOptions.None)
            {
                if(_cheatSession is not null){await _cheatSession.DisableAsync();_cheatSession.Dispose();_cheatSession=null;}
                CheatStatus.Text="Additional cheats are off.";
            }
            else
            {
                var games=Process.GetProcessesByName("SplinterCell");
                if(games.Length!=1){foreach(var g in games)g.Dispose();throw new InvalidOperationException("Start one game and load a mission first.");}
                CheatSession updated;
                try{updated=await CheatSession.ApplyAsync(games[0],settings);}
                catch{games[0].Dispose();throw;}
                _cheatSession?.Dispose();_cheatSession=updated;
                CheatStatus.Text="Active: "+settings.Options+
                    (settings.Options.HasFlag(CheatOptions.MovementSpeed)?$" • Speed {settings.SpeedPercent/100.0:0.##}x":"");
            }
            ShowCheatSettings(settings);
            return true;
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            ShowCheatSettings(_cheatSession?.Settings??new(CheatOptions.None));
            CheatStatus.Text=ex.Message;
            return false;
        }
        finally{_cheatsChanging=false;SetCheatControls(true);}
    }

    private async Task RefreshCheatStatusAsync()
    {
        if(_cheatRefreshBusy||_cheatsChanging||_cheatSession is null)return;
        _cheatRefreshBusy=true;
        try
        {
            var session=_cheatSession;
            if(!session.IsCurrent())
            {
                session.Dispose();_cheatSession=null;
                _cheatsChanging=true;ShowCheatSettings(new(CheatOptions.None));_cheatsChanging=false;
                SetCheatControls(true);
                CheatStatus.Text="Cheats reset after a player change or expired session. Enable them again in the mission.";
            }
            else await session.PulseAsync();
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {CheatStatus.Text=ex.Message;}
        finally{_cheatRefreshBusy=false;}
    }


    private async void SpawnGuard_Click(object sender,RoutedEventArgs e) => await SpawnGuardAsync(false);
    private async void RemoveGuard_Click(object sender,RoutedEventArgs e) => await SpawnGuardAsync(true);

    private async Task SpawnGuardAsync(bool remove)
    {
        SpawnGuardButton.IsEnabled=RemoveGuardButton.IsEnabled=false;
        var games=Process.GetProcessesByName("SplinterCell");
        try
        {
            if(games.Length!=1)throw new InvalidOperationException("Start one game and load a mission first.");
            SpawnStatus.Text=remove?"Removing spawned guard…":"Spawning one guard…";
            SpawnStatus.Text=await DirectLevelLoader.SpawnNpcAsync(games[0],remove);
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {SpawnStatus.Text=ex.Message;}
        finally{foreach(var game in games)game.Dispose();SpawnGuardButton.IsEnabled=RemoveGuardButton.IsEnabled=true;}
    }

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
    private async void Timer_Tick(object? sender, EventArgs e) { RefreshGameStatus(); RefreshHealthStatus(); await RefreshCheatStatusAsync(); }
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
                    ? "SplinterCell.exe is running • Level loading requires the main menu"
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
