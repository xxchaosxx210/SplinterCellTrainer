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
        _overlayNpcs.ConfigureEsp(()=>EnemyEspToggle.IsChecked==true,()=>{
            EnemyEspToggle.IsChecked=EnemyEspToggle.IsChecked!=true;
            return EspStatus.Text;
        },()=>!IsBusy&&!_shutdownRunning);
        _equipment.Available=()=>!_levelLoading&&!_shutdownRunning&&!_overlayNpcs.Busy&&!_healthChanging&&!_cheatsChanging&&!_cheatRefreshBusy&&!_worldActionChanging;
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
        Closed += (_, _) => { _gameMenu?.Close(); _overlayNpcs.Dispose(); _equipment.Dispose(); _enemyOverlay?.Close(); _timer.Stop(); _timer.Tick -= Timer_Tick; };
    }

    // The former window is a hidden control host during the tray migration; it is never shown.
    public event Action? ExitRequested;
    private bool _shutdownRunning,_shutdownApproved,_worldActionChanging;
    public bool IsBusy=>_equipment.Busy||_equipment.Refreshing||_levelLoading||_overlayNpcs.Busy||_overlayNpcs.Refreshing||_healthChanging||_cheatsChanging||_cheatRefreshBusy||_worldActionChanging;
    public void StartBackground()=>StartGameMenu();
    internal TrainerStatusSnapshot Status=>new(GameStatus.Text,GameDetail.Text,MenuStatus.Text,CheatStatus.Text,HealthStatus.Text,LevelStatus.Text,
        GameStatus.Text=="Game detected",MenuStatus.Text.StartsWith("In-game menu unavailable",StringComparison.Ordinal),_shutdownRunning);
    public string RestartOverlay()
    {
        if(_shutdownRunning||IsBusy)return MenuStatus.Text="Wait for the current trainer update to finish.";
        _gameMenu?.Close();_gameMenu=null;
        _showOverlayLevels=false;_showOverlayNpcs=false;_showEquipment=false;_utilityPage=null;
        StartGameMenu();return MenuStatus.Text;
    }
    public async Task<bool> ShutdownTrainerAsync()
    {
        if(_shutdownRunning)return false;
        _shutdownRunning=true;
        _gameMenu?.Close();_gameMenu=null;
        MenuStatus.Text="Stopping trainer and restoring active cheats…";
        bool success=false;
        try {
            var waiting=Stopwatch.StartNew();
            while(IsBusy){if(waiting.Elapsed>TimeSpan.FromSeconds(60)){MenuStatus.Text="An update is still running. Wait for it to finish, then retry Exit.";return false;}await Task.Delay(100);}
            try{await _overlayNpcs.RestoreFrozenAsync();}
            catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){MenuStatus.Text="Could not restore frozen NPCs: "+ex.Message;return false;}
            if(!await ApplyCheatSettingsAsync(new(CheatOptions.None))||!await SetInfiniteHealthAsync(false))return false;
            _shutdownApproved=true;success=true;Close();return true;
        }
        finally {
            if(!success){_shutdownRunning=false;StartGameMenu();}
        }
    }
    private void RequestExit()=>ExitRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => RequestExit();

    private GameMenuOverlay? _gameMenu;
    private readonly NpcOverlayMenu _overlayNpcs=new();
    private readonly EquipmentMenu _equipment=new();
    private bool _showEquipment;
    private bool _showOverlayNpcs,_showOverlayLevels,_levelLoading;
    private string? _utilityPage;
    private MissionsOverlayMenu? _overlayLevels;
    private LevelChoice[] _levels=Array.Empty<LevelChoice>();
    private static readonly CheatOptions[] MenuOptions={CheatOptions.UnlimitedAmmo,CheatOptions.NoReload,CheatOptions.UnlimitedGadgets,CheatOptions.ReducedDetection,CheatOptions.MovementSpeed,CheatOptions.Invisibility,CheatOptions.Noclip,CheatOptions.BypassLocks,CheatOptions.QuietLights,CheatOptions.CamerasOff,CheatOptions.TurretsOff,CheatOptions.IffOff,CheatOptions.SilentMovement,CheatOptions.SilentSam};
    private void StartGameMenu()
    {
        if(_gameMenu is not null)return;
        try {
            _overlayLevels=new MissionsOverlayMenu(new LevelOverlayMenu(()=>_levels.Select(l=>new LevelMenuChoice(l.Map,l.Label)).ToArray(),LoadOverlayLevelAsync),CompleteNextObjectiveAsync,()=>!IsBusy);
            _gameMenu=new GameMenuOverlay(ReadGameMenu,ToggleGameMenuAsync,()=>_levelLoading?Task.CompletedTask:_showEquipment?_equipment.RefreshAsync():_showOverlayNpcs?_overlayNpcs.RefreshAsync():Task.CompletedTask);
            _gameMenu.Show();
            MenuStatus.Text="In-game menu: F6 to open / close; arrows to select; Enter to toggle.";
        }
        catch(System.ComponentModel.Win32Exception ex){_gameMenu?.Close();_gameMenu=null;MenuStatus.Text="In-game menu unavailable: "+ex.Message;}
    }
    private GameMenuFrame ReadGameMenu()
    {
        if(_shutdownRunning)return new("stopping","TRAINER STOPPING",new[]{new GameMenuEntry("Restoring active cheats…",false,false,"stopping",null,false)});
        if(_showEquipment)return _equipment.Frame;
        if(_utilityPage is not null)return ReadUtilityMenu();
        if(_showOverlayLevels&&_overlayLevels is not null)return _overlayLevels.Frame;
        if(_showOverlayNpcs)return _overlayNpcs.Frame;
        bool ready=!_levelLoading&&!_overlayNpcs.Busy&&!_healthChanging&&!_cheatsChanging&&!_cheatRefreshBusy&&!_worldActionChanging;
        var settings=ReadCheatSettings();
        var items=new List<GameMenuEntry> {
            new("Infinite health",InfiniteHealthToggle.IsChecked==true,ready,"cheat:1")
        };
        string[] labels={"Unlimited ammunition","No reload","Unlimited gadgets","Reduced detection",$"Movement speed ({settings.SpeedPercent/100.0:0.##}x)","Invisibility","Noclip (walls / doors)","Bypass locks","Quiet lights out","Disable security cameras","Deactivate turrets","Disable turret IFF","Silent movement","Silent Sam"};
        for(int i=0;i<MenuOptions.Length;++i){
            if((MenuOptions[i]&(CheatOptions.CamerasOff|CheatOptions.TurretsOff|CheatOptions.IffOff|CheatOptions.QuietLights|CheatOptions.Invisibility|CheatOptions.ReducedDetection|CheatOptions.SilentMovement|CheatOptions.SilentSam))!=0)continue;
            if(MenuOptions[i]==CheatOptions.MovementSpeed)items.Add(new("Movement speed",false,true,"speed-menu",settings.Options.HasFlag(CheatOptions.MovementSpeed)?$"{settings.SpeedPercent/100.0:0.##}x":"OFF",false,true));
            else items.Add(new(labels[i],settings.Options.HasFlag(MenuOptions[i]),ready,$"cheat:{i+2}"));
        }
        items.Add(new("Stealth",false,true,"stealth-menu",null,false,true));
        items.Add(new("Security",false,true,"security-menu",null,false,true));
        items.Add(new("Enemies fight each other (30s)",false,ready,"guards-fight",null,false));
        items.Add(new("Trainer tools / About / Exit",false,true,"tools-menu",null,false,true));
        items.Insert(0,new("Sam Equipment",false,true,"equipment",null,false,true));
        items.Insert(0,new("NPC Manager",false,true,"npcs",null,false,true));
        items.Insert(0,new("Missions",false,true,"missions",null,false,true));
        return new("main","TRAINER MENU",items.ToArray());
    }
    private async Task<string> ToggleGameMenuAsync(string key)
    {
        if(key=="equipment"){
            _showEquipment=true;_showOverlayNpcs=false;_showOverlayLevels=false;_utilityPage=null;
            _equipment.Open();await _equipment.RefreshAsync(true);return "Give items, set quantities, or select equipment.";
        }
        if(key=="back"&&_showEquipment){if(!_equipment.Back())_showEquipment=false;return "";}
        if(_showEquipment)return await _equipment.ActivateAsync(key);
        if(key is "speed-menu" or "tools-menu" or "security-menu" or "stealth-menu"){
            _showOverlayNpcs=false;_showOverlayLevels=false;_utilityPage=key switch {"speed-menu"=>"speed","security-menu"=>"security","stealth-menu"=>"stealth",_=>"tools"};return "";
        }
        if(key=="back"&&_utilityPage is not null){_utilityPage=_utilityPage switch {"about" or "exit"=>"tools","alarms-search"=>"security",_=>null};return "";}
        if(_utilityPage is not null&&!((_utilityPage is "security" or "stealth")&&key.StartsWith("cheat:")))return await ActivateUtilityAsync(key);
        if(key=="missions"){
            _showOverlayNpcs=false;_showOverlayLevels=true;_overlayLevels!.Open();return "Run the next objective completion script, or load a level.";
        }
        if(key=="back"&&_showOverlayLevels){if(!_overlayLevels!.Back())_showOverlayLevels=false;return "";}
        if(_showOverlayLevels)return await _overlayLevels!.ActivateAsync(key);
        if(key=="npcs"){
            _showOverlayLevels=false;_showOverlayNpcs=true;
            _overlayNpcs.OpenManager();
            await _overlayNpcs.RefreshAsync();return "";
        }
        if(key=="back"){if(!_overlayNpcs.Back())_showOverlayNpcs=false;return "";}
        if(_showOverlayNpcs){if(_levelLoading)return "Wait for level loading to finish.";return await _overlayNpcs.ActivateAsync(key);}
        if(key=="guards-fight")return await ApplyWorldActionAsync();
        if(!key.StartsWith("cheat:")||!int.TryParse(key.AsSpan(6),out int selected))return "Select an option.";
        if(_levelLoading||_healthChanging||_cheatsChanging||_cheatRefreshBusy||_worldActionChanging)return "Please wait for the current update.";
        if(selected==0){EnemyEspToggle.IsChecked=EnemyEspToggle.IsChecked!=true;return EspStatus.Text;}
        if(selected==1){await SetInfiniteHealthAsync(InfiniteHealthToggle.IsChecked!=true);return HealthStatus.Text;}
        if(selected<2||selected>=MenuOptions.Length+2)return "Select an option.";
        var settings=ReadCheatSettings();
        await ApplyCheatSettingsAsync(settings with {Options=settings.Options^MenuOptions[selected-2]});
        return CheatStatus.Text;
    }

    private async Task<string> CompleteNextObjectiveAsync()
    {
        if(IsBusy)return "Please wait for the current update.";
        _worldActionChanging=true;
        var games=Process.GetProcessesByName("SplinterCell");
        try {
            if(games.Length!=1)throw new InvalidOperationException("Start one game and load a mission first.");
            return await DirectLevelLoader.CompleteNextObjectiveAsync(games[0]);
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){return ex.Message;}
        finally{foreach(var game in games)game.Dispose();_worldActionChanging=false;}
    }

    private async Task<string> ApplyAlarmSearchAsync(string key)
    {
        if(IsBusy)return "Please wait for the current update.";
        _worldActionChanging=true;
        var games=Process.GetProcessesByName("SplinterCell");
        try
        {
            if(games.Length!=1)return "Start one game and load a mission first.";
            var game=games[0];
            if(key is "alarm-start" or "alarm-clear")
                return await DirectLevelLoader.ChangeAlarmAsync(game,key=="alarm-start");

            bool start=key=="search-start";
            var snapshot=await DirectLevelLoader.ReadNpcsAsync(game);
            if(snapshot is null)return "Guard list unavailable. Try again in a running mission.";
            string state=start?"s_Default":"s_Investigate";
            var eligible=snapshot.Where(n=>n.Present&&n.Controller&&!n.ProtectedGlobal&&n.Condition=="Alive"&&
                n.AiState.Equals(state,StringComparison.OrdinalIgnoreCase));
            var targets=start?eligible.Where(n=>float.IsFinite(n.Distance)).OrderBy(n=>n.Distance).Take(1):eligible;
            int changed=0,skipped=0;
            foreach(var guard in targets)
            {
                try
                {
                    await DirectLevelLoader.NpcActionAsync(game,"activity",guard.Id,
                        activity:start?NpcActivityEdit.Searching:NpcActivityEdit.Idle);
                    ++changed;
                }
                catch(InvalidOperationException){++skipped;}
            }
            return start?$"Guards sent to search: {changed}. Skipped: {skipped}."
                :$"Guard searches cleared: {changed}. Skipped: {skipped}.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {return ex.Message;}
        finally{foreach(var game in games)game.Dispose();_worldActionChanging=false;}
    }

    private async Task<string> ApplyWorldActionAsync()
    {
        if(IsBusy)return "Please wait for the current update.";
        _worldActionChanging=true;
        var games=Process.GetProcessesByName("SplinterCell");
        try {
            if(games.Length!=1)throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission.");
            return MenuStatus.Text=await DirectLevelLoader.FightGuardsAsync(games[0]);
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){return MenuStatus.Text=ex.Message;}
        finally{foreach(var game in games)game.Dispose();_worldActionChanging=false;}
    }

    private GameMenuFrame ReadUtilityMenu()
    {
        bool ready=!_levelLoading&&!_overlayNpcs.Busy&&!_overlayNpcs.Refreshing&&!_healthChanging&&!_cheatsChanging&&!_cheatRefreshBusy&&!_worldActionChanging;
        var rows=new List<GameMenuEntry>();string title="TRAINER TOOLS";
        GameMenuEntry Row(string label,string key,bool enabled=true,bool navigation=false,string? value=null)=>new(label,false,enabled,key,value,false,navigation);
        if(_utilityPage is "security" or "stealth"){
            bool stealth=_utilityPage=="stealth";
            title=stealth?"STEALTH":"SECURITY";var settings=ReadCheatSettings();
            string[] labels=stealth?new[]{"Quiet lights out","Invisibility","Reduced detection","Silent movement","Silent Sam"}:new[]{"Disable security cameras","Deactivate turrets","Disable turret IFF"};
            CheatOptions[] options=stealth?new[]{CheatOptions.QuietLights,CheatOptions.Invisibility,CheatOptions.ReducedDetection,CheatOptions.SilentMovement,CheatOptions.SilentSam}:new[]{CheatOptions.CamerasOff,CheatOptions.TurretsOff,CheatOptions.IffOff};
            for(int i=0;i<options.Length;++i)rows.Add(new(labels[i],settings.Options.HasFlag(options[i]),ready,$"cheat:{Array.IndexOf(MenuOptions,options[i])+2}"));
            if(!stealth)rows.Add(Row("Alarms & Search","alarms-search",navigation:true));
        } else if(_utilityPage=="alarms-search"){
            title="ALARMS & SEARCH";
            rows.Add(Row("Clear active alarm","alarm-clear",ready));
            rows.Add(Row("Start alarm","alarm-start",ready));
            rows.Add(Row("Clear guard search","search-clear",ready));
            rows.Add(Row("Start guard search","search-start",ready));
        } else if(_utilityPage=="speed"){
            title="MOVEMENT SPEED";var settings=ReadCheatSettings();
            rows.Add(new("Enable movement speed",settings.Options.HasFlag(CheatOptions.MovementSpeed),ready,"speed-toggle"));
            foreach(int percent in new[]{50,100,150,200,300})rows.Add(Row($"{percent/100.0:0.##}x"+(percent==100?" (normal)":""),"speed:"+percent,ready,value:settings.SpeedPercent==percent?"Selected":""));
        } else if(_utilityPage=="about"){
            title="ABOUT / SUPPORTED GAME";
            rows.Add(Row(VersionInfo.TrainerLabel,"info:version",false,value:""));
            rows.Add(Row("Steam build "+VersionInfo.SupportedSteamBuild,"info:build",false,value:""));
            rows.Add(Row("Steam App ID 13560","info:app",false,value:""));
            rows.Add(Row("F6: menu / arrows: select / Enter: apply","info:keys",false,value:""));
        } else if(_utilityPage=="exit"){
            title="EXIT TRAINER?";
            rows.Add(Row("Cancel","exit-cancel",navigation:true));
            rows.Add(Row("Exit trainer and restore active cheats","exit-confirm",ready));
        } else {
            rows.Add(Row("Refresh game detection","detect-refresh",ready));
            rows.Add(Row("Test game connection","connection-test",ready));
            rows.Add(Row("Version / supported Steam build","about",navigation:true));
            rows.Add(Row("Exit trainer","exit-menu",navigation:true));
        }
        rows.Add(Row("Back","back",navigation:true));
        return new("utility:"+_utilityPage,title,rows.ToArray());
    }
    private async Task<string> ActivateUtilityAsync(string key)
    {
        if(key=="alarms-search"){_utilityPage="alarms-search";return "";}
        if(_utilityPage=="alarms-search"&&key is ("alarm-clear" or "alarm-start" or "search-clear" or "search-start"))
            return await ApplyAlarmSearchAsync(key);
        if(key=="about"){_utilityPage="about";return "";}
        if(key=="exit-menu"){_utilityPage="exit";return "The game will remain running.";}
        if(key=="exit-cancel"){_utilityPage="tools";return "Exit cancelled.";}
        if(_levelLoading||_overlayNpcs.Busy||_overlayNpcs.Refreshing||_healthChanging||_cheatsChanging||_cheatRefreshBusy||_worldActionChanging)return "Please wait for the current update.";
        if(key=="exit-confirm"&&_utilityPage=="exit"){
            // Uses the existing close handler, which restores active cheat sessions first.
            _ = Dispatcher.BeginInvoke(new Action(RequestExit));return "Closing trainer…";
        }
        if(key=="detect-refresh"){RefreshGameStatus();return GameStatus.Text+" / "+GameDetail.Text;}
        if(key=="connection-test"){
            var games=Process.GetProcessesByName("SplinterCell");
            try {
                if(games.Length!=1)throw new InvalidOperationException("Start one copy of Splinter Cell first.");
                return LevelStatus.Text=await DirectLevelLoader.RequestAsync(games[0],"--probe");
            } finally {foreach(var game in games)game.Dispose();}
        }
        var settings=ReadCheatSettings();
        if(key=="speed-toggle"){
            await ApplyCheatSettingsAsync(settings with {Options=settings.Options^CheatOptions.MovementSpeed});return CheatStatus.Text;
        }
        if(key.StartsWith("speed:")&&int.TryParse(key.AsSpan(6),out int percent)&&new[]{50,100,150,200,300}.Contains(percent)){
            var changed=settings with {SpeedPercent=percent};
            if(settings.Options.HasFlag(CheatOptions.MovementSpeed)){
                await ApplyCheatSettingsAsync(changed);return CheatStatus.Text;
            }
            _cheatsChanging=true;
            try{ShowCheatSettings(changed);}finally{_cheatsChanging=false;}
            return $"Speed set to {percent/100.0:0.##}x. Enable movement speed to apply it.";
        }
        return "Select an option.";
    }

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

    private void Window_Closing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(_shutdownApproved)return;
        e.Cancel=true;RequestExit();
    }
    private CheatSession? _cheatSession;
    private CheatOptions _overlayOptions;
    private bool _cheatsChanging, _cheatRefreshBusy, _cheatControlsReady;

    private CheatSettings ReadCheatSettings()
    {
        CheatOptions options=_overlayOptions;
        if(UnlimitedAmmoToggle.IsChecked==true)options|=CheatOptions.UnlimitedAmmo;
        if(NoReloadToggle.IsChecked==true)options|=CheatOptions.NoReload;
        if(UnlimitedGadgetsToggle.IsChecked==true)options|=CheatOptions.UnlimitedGadgets;
        if(ReducedDetectionToggle.IsChecked==true)options|=CheatOptions.ReducedDetection;
        if(MovementSpeedToggle.IsChecked==true)options|=CheatOptions.MovementSpeed;
        if(InvisibilityToggle.IsChecked==true)options|=CheatOptions.Invisibility;
        if(NoclipToggle.IsChecked==true)options|=CheatOptions.Noclip;
        if(BypassLocksToggle.IsChecked==true)options|=CheatOptions.BypassLocks;
        if(QuietLightsToggle.IsChecked==true)options|=CheatOptions.QuietLights;
        var selected=SpeedPicker.SelectedItem as System.Windows.Controls.ComboBoxItem;
        int speed=selected?.Tag is string value ? int.Parse(value,System.Globalization.CultureInfo.InvariantCulture) : 100;
        return new(options,speed);
    }

    private void SetCheatControls(bool available)
    {
        UnlimitedAmmoToggle.IsEnabled=NoReloadToggle.IsEnabled=UnlimitedGadgetsToggle.IsEnabled=
            ReducedDetectionToggle.IsEnabled=MovementSpeedToggle.IsEnabled=InvisibilityToggle.IsEnabled=NoclipToggle.IsEnabled=BypassLocksToggle.IsEnabled=QuietLightsToggle.IsEnabled=available;
        SpeedPicker.IsEnabled=available && MovementSpeedToggle.IsChecked==true;
    }

    private void ShowCheatSettings(CheatSettings settings)
    {
        _overlayOptions=settings.Options&(CheatOptions.CamerasOff|CheatOptions.TurretsOff|CheatOptions.IffOff|CheatOptions.SilentMovement|CheatOptions.SilentSam);
        UnlimitedAmmoToggle.IsChecked=settings.Options.HasFlag(CheatOptions.UnlimitedAmmo);
        NoReloadToggle.IsChecked=settings.Options.HasFlag(CheatOptions.NoReload);
        UnlimitedGadgetsToggle.IsChecked=settings.Options.HasFlag(CheatOptions.UnlimitedGadgets);
        ReducedDetectionToggle.IsChecked=settings.Options.HasFlag(CheatOptions.ReducedDetection);
        MovementSpeedToggle.IsChecked=settings.Options.HasFlag(CheatOptions.MovementSpeed);
        InvisibilityToggle.IsChecked=settings.Options.HasFlag(CheatOptions.Invisibility);
        NoclipToggle.IsChecked=settings.Options.HasFlag(CheatOptions.Noclip);
        BypassLocksToggle.IsChecked=settings.Options.HasFlag(CheatOptions.BypassLocks);
        QuietLightsToggle.IsChecked=settings.Options.HasFlag(CheatOptions.QuietLights);
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
                    (settings.Options.HasFlag(CheatOptions.MovementSpeed)?$" • Speed {settings.SpeedPercent/100.0:0.##}x":"")+
                    (settings.Options.HasFlag(CheatOptions.IffOff)?" • Turrets can target guards and Sam.":"");
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


    private NpcManagerWindow? _npcManager;
    private void OpenNpcManager_Click(object sender,RoutedEventArgs e)
    {
        if(_npcManager is null) {
            _npcManager=new NpcManagerWindow {Owner=this};
            _npcManager.Closed+=(_,_)=>_npcManager=null;
            _npcManager.Show();
        } else _npcManager.Activate();
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
            _levels=levels;
            LevelPicker.ItemsSource = levels;
            if (levels.Length > 0) LevelPicker.SelectedIndex = 0;
            else LevelStatus.Text = "No installed maps found.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _levels=Array.Empty<LevelChoice>();
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

    private async Task<string> LoadOverlayLevelAsync(LevelMenuChoice level)
    {
        if(_levelLoading||_overlayNpcs.Busy||_overlayNpcs.Refreshing||_healthChanging||_cheatsChanging||_cheatRefreshBusy||_worldActionChanging)
            throw new InvalidOperationException("Wait for the current trainer update to finish.");
        if(!_levels.Any(l=>l.Map==level.Map)||!System.IO.File.Exists(System.IO.Path.Combine(GameDirectory,"maps",level.Map+".scl")))
            throw new InvalidOperationException("The selected map is no longer installed.");
        var games=Process.GetProcessesByName("SplinterCell");
        try {
            if(games.Length!=1)throw new InvalidOperationException("Start one copy of Splinter Cell and return to its main menu.");
            _levelLoading=true;LoadLevelButton.IsEnabled=false;
            LevelStatus.Text="Requesting map travel…";
            // Shared loader checks the current map and refuses active-mission travel before sending OPEN.
            return LevelStatus.Text=await DirectLevelLoader.RequestAsync(games[0],level.Map);
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException) {
            LevelStatus.Text=ex.Message;throw;
        }
        finally {_levelLoading=false;LoadLevelButton.IsEnabled=true;foreach(var game in games)game.Dispose();}
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
                    try { LevelStatus.Text = await LoadOverlayLevelAsync(new(level.Map,level.Label)); }
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
    private async void Timer_Tick(object? sender, EventArgs e) { RefreshGameStatus(); RefreshHealthStatus(); await RefreshCheatStatusAsync(); await _overlayNpcs.PingFreezeAsync(); }
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
