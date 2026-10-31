using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SCTrainer;

public partial class NpcManagerWindow : Window
{
    private sealed class Row : INotifyPropertyChanged
    {
        public NpcSnapshot Snapshot { get; private set; }
        public uint Id => Snapshot.Id;
        public string Name { get; set; }
        public string HealthText => Snapshot.Present ? $"{Snapshot.Health} HP" : "—";
        public string Summary => Snapshot.Present ? $"{Snapshot.Condition} · {Snapshot.Activity} · {Snapshot.Distance:0.0} m" : "Removed";
        public Row(NpcSnapshot snapshot) { Snapshot=snapshot; Name=$"Guard {snapshot.Id:00}"; }
        public void Update(NpcSnapshot snapshot) { Snapshot=!snapshot.Present ? snapshot with {Type=Snapshot.Type,PawnState=Snapshot.PawnState,AiState=Snapshot.AiState} : snapshot; Notify(); }
        public void Notify() => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Row> _rows=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(750)};
    private Process? _game;
    private bool _busy,_refreshing,_closed,_stale=true;
    private Row? Selected => NpcList.SelectedItem as Row;

    public NpcManagerWindow()
    {
        InitializeComponent();
        var work=SystemParameters.WorkArea;
        MaxWidth=Math.Max(1,work.Width-24); MaxHeight=Math.Max(1,work.Height-24);
        MinWidth=Math.Min(MinWidth,MaxWidth);MinHeight=Math.Min(MinHeight,MaxHeight);
        Width=Math.Min(Width,MaxWidth);Height=Math.Min(Height,MaxHeight);
        NpcList.ItemsSource=_rows;
        _timer.Tick+=Refresh_Tick;
        Loaded+=async(_,_)=>{_timer.Start();await RefreshAsync();};
        Closing+=(_,e)=>{if(_busy)e.Cancel=true;};
        Closed+=(_,_)=>{_closed=true;_timer.Stop();_timer.Tick-=Refresh_Tick;_game?.Dispose();};
        ShowSelected(true);
    }

    private Process Game()
    {
        if(_game is not null&&!_game.HasExited)return _game;
        _game?.Dispose();_game=null;_rows.Clear();
        var games=Process.GetProcessesByName("SplinterCell");
        if(games.Length!=1) {foreach(var g in games)g.Dispose();throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission.");}
        try {DirectLevelLoader.ValidateGame(games[0]);_game=games[0];return _game;}
        catch {games[0].Dispose();throw;}
    }

    private static bool Expected(Exception ex) => ex is InvalidOperationException or System.ComponentModel.Win32Exception or
        System.IO.IOException or UnauthorizedAccessException or FormatException or ArgumentException;

    private async void Refresh_Tick(object? sender,EventArgs e)=>await RefreshAsync();
    private async Task RefreshAsync()
    {
        if(_closed||_busy||_refreshing)return;
        _refreshing=true;
        try
        {
            var snapshots=await DirectLevelLoader.ReadNpcsAsync(Game());
            if(snapshots is null)return;
            if(_closed)return;
            var ids=snapshots.Select(n=>n.Id).ToHashSet();
            foreach(var row in _rows.Where(n=>!ids.Contains(n.Id)).ToArray())_rows.Remove(row);
            foreach(var n in snapshots.OrderBy(n=>n.Id))
            {
                var row=_rows.FirstOrDefault(r=>r.Id==n.Id);
                if(row is null)_rows.Add(new Row(n));else row.Update(n);
            }
            _stale=false;
            if(Selected is null&&_rows.Count>0)NpcList.SelectedItem=_rows.LastOrDefault(n=>n.Snapshot.Present)??_rows[^1];
            CountText.Text=$"Tracked NPCs · {_rows.Count(n=>n.Snapshot.Present)} / 8 present";
            ConnectionText.Text="Connected · Live updates every 0.75 seconds";
            ShowSelected(false);
        }
        catch(Exception ex) when(Expected(ex))
        {
            _stale=true;ConnectionText.Text="Updates unavailable — displayed values may be stale";StatusText.Text=ex.Message;
            ShowSelected(false);
        }
        finally{_refreshing=false;}
    }

    private void ShowSelected(bool selectionChanged)
    {
        var row=Selected;var n=row?.Snapshot;
        SelectedTitle.Text=row is null?"Select a spawned NPC":$"{row.Name} · #{row.Id}";
        TypeText.Text=n?.Type??"Spawn a guard to start the roster.";
        ConditionText.Text=n is null?"":n.Condition+(_stale?" · last observed":"");
        StatsText.Text=n is null?"":n.Present?$"Health: {n.Health} / 100\nActivity: {n.Activity}\nMovement: {n.Movement}\nDistance: {n.Distance:0.0} m\nWeapon: {n.Weapon}":"This NPC is no longer present in the current game state.";
        StateText.Text=n is null?"":$"Pawn: {n.PawnState}\nAI: {n.AiState}\nTarget to fire at: {n.Target}\nController: {(n.Controller?"Attached":"None")}";
        if(selectionChanged){NameEdit.Text=row?.Name??"";HealthEdit.Text=(n?.Health>0?n.Health:100).ToString(CultureInfo.InvariantCulture);}
        SpawnButton.IsEnabled=!_busy&&!_stale&&_rows.Count(r=>r.Snapshot.Present)<8;
        RemoveButton.IsEnabled=!_busy&&!_stale&&n is {Present:true,Removing:false};
        HealthButton.IsEnabled=HealButton.IsEnabled=!_busy&&!_stale&&n?.CanEditHealth==true;
        RenameButton.IsEnabled=NameEdit.IsEnabled=!_busy&&row is not null;
        HealthEdit.IsEnabled=HealthButton.IsEnabled;
    }

    private void NpcList_SelectionChanged(object sender,SelectionChangedEventArgs e)=>ShowSelected(true);
    private void Rename_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not Row row)return;
        string name=NameEdit.Text.Trim();if(name.Length==0){StatusText.Text="Enter a display name.";return;}
        row.Name=name;row.Notify();ShowSelected(false);StatusText.Text="Display name changed in this trainer window.";
    }
    private async void Spawn_Click(object sender,RoutedEventArgs e)=>await ActionAsync("spawn");
    private async void Remove_Click(object sender,RoutedEventArgs e)=>await ActionAsync("remove");
    private async void Heal_Click(object sender,RoutedEventArgs e)=>await ActionAsync("health",100);
    private async void Health_Click(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(HealthEdit.Text,NumberStyles.None,CultureInfo.InvariantCulture,out int health)||health is <1 or >100)
        {StatusText.Text="Health must be a whole number from 1 to 100.";return;}
        await ActionAsync("health",health);
    }
    private async Task ActionAsync(string action,int health=100)
    {
        if(_busy||_stale)return;
        uint id=Selected?.Id??0;
        if(action!="spawn"&&id==0)return;
        _busy=true;ShowSelected(false);
        try
        {
            StatusText.Text=action=="spawn"?"Spawning a guard…":$"Updating NPC {id}…";
            StatusText.Text=await DirectLevelLoader.NpcActionAsync(Game(),action,id,health);
        }
        catch(Exception ex) when(Expected(ex)){StatusText.Text=ex.Message;}
        finally{_busy=false;await RefreshAsync();ShowSelected(false);}
        if(action=="spawn")NpcList.SelectedItem=_rows.LastOrDefault(n=>n.Snapshot.Present);
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
