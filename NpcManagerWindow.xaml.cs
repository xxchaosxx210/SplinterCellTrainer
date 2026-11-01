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
        public string Summary => Snapshot.Present ? $"{Snapshot.Origin} / {Snapshot.Condition} / {Snapshot.Activity} / {Snapshot.Distance:0.0} m" : $"{Snapshot.Origin} / Removed";
        public Row(NpcSnapshot snapshot) { Snapshot=snapshot; Name=snapshot.TrainerSpawned?$"Guard {snapshot.Id:00}":string.IsNullOrEmpty(snapshot.InstanceName)?$"Map NPC {snapshot.Id}":snapshot.InstanceName; }
        public void Update(NpcSnapshot snapshot) { Snapshot=!snapshot.Present ? snapshot with {Type=Snapshot.Type,PawnState=Snapshot.PawnState,AiState=Snapshot.AiState} : snapshot; Notify(); }
        public void Notify() => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Row> _rows=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(750)};
    private Process? _game;
    private bool _busy,_refreshing,_closed,_stale=true,_ready;
    private Row? Selected => NpcList.SelectedItem as Row;

    public NpcManagerWindow()
    {
        InitializeComponent();
        _ready=true;
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
            snapshots=snapshots.Where(n=>n.ShowInRoster).ToArray();
            var ids=snapshots.Select(n=>n.Id).ToHashSet();
            foreach(var row in _rows.Where(n=>!ids.Contains(n.Id)).ToArray())_rows.Remove(row);
            foreach(var n in snapshots.OrderBy(n=>n.Id))
            {
                var row=_rows.FirstOrDefault(r=>r.Id==n.Id);
                if(row is null)_rows.Add(new Row(n));else row.Update(n);
            }
            var order=_rows.OrderByDescending(r=>r.Snapshot.Present).ThenBy(r=>r.Snapshot.TrainerSpawned).ThenBy(r=>r.Id).ToArray();
            for(int i=0;i<order.Length;++i){int from=_rows.IndexOf(order[i]);if(from!=i)_rows.Move(from,i);}
            _stale=false;
            if(Selected is null&&_rows.Count>0)NpcList.SelectedItem=_rows.LastOrDefault(n=>n.Snapshot.Present)??_rows[^1];
            CountText.Text=$"{_rows.Count(n=>n.Snapshot.Present&&!n.Snapshot.TrainerSpawned)} map NPCs / {_rows.Count(n=>n.Snapshot.Present&&n.Snapshot.TrainerSpawned)} of 8 spawned";
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
        SelectedTitle.Text=row is null?"Select an NPC":$"{row.Name} · #{row.Id}";
        TypeText.Text=n is null?"Load a mission to discover map NPCs.":$"{n.Origin}  /  {n.Type}  /  {n.InstanceName}";
        ConditionText.Text=n is null?"":n.Condition+(_stale?" · last observed":"");
        StatsText.Text=n is null?"":n.Present?$"Health: {n.Health} / 100\nActivity: {n.Activity}\nMovement: {n.Movement}\nDistance: {n.Distance:0.0} m\nWeapon: {n.Weapon}\nPosition: {n.X:0.00}, {n.Y:0.00}, {n.Z:0.00}":"This NPC is no longer present in the current game state.";
        StateText.Text=n is null?"":$"Pawn: {n.PawnState}\nAI: {n.AiState}\nTarget to fire at: {n.Target}\nController: {(n.Controller?"Attached":"None")}";
        if(selectionChanged){NameEdit.Text=row?.Name??"";HealthEdit.Text=(n?.Health>0?n.Health:100).ToString(CultureInfo.InvariantCulture);ShowCoordinates();}
        SpawnButton.IsEnabled=!_busy&&!_stale&&_rows.Count(r=>r.Snapshot.Present&&r.Snapshot.TrainerSpawned)<8;
        RemoveButton.IsEnabled=!_busy&&!_stale&&n?.CanRemove==true;
        HealthButton.IsEnabled=HealButton.IsEnabled=!_busy&&!_stale&&n?.CanEditHealth==true;
        RenameButton.IsEnabled=NameEdit.IsEnabled=!_busy&&row is not null;
        HealthEdit.IsEnabled=HealthButton.IsEnabled;
        bool available=!_busy&&!_stale&&n is not null;
        AliveCondition.IsEnabled=available&&n!.CanSetCondition(NpcConditionEdit.Alive);
        KnockedCondition.IsEnabled=available&&n!.CanSetCondition(NpcConditionEdit.KnockedOut);
        DeadCondition.IsEnabled=available&&n!.CanSetCondition(NpcConditionEdit.Dead);
        ConditionPicker.IsEnabled=AliveCondition.IsEnabled||KnockedCondition.IsEnabled||DeadCondition.IsEnabled;
        ActivityButton.IsEnabled=ActivityPicker.IsEnabled=available&&n!.CanEditActivity;
        FrontButton.IsEnabled=ToSamButton.IsEnabled=PositionButton.IsEnabled=PositionX.IsEnabled=PositionY.IsEnabled=PositionZ.IsEnabled=available&&n!.CanEditPosition;
        CurrentPositionButton.IsEnabled=!_busy&&n?.Present==true;
        ConditionButton.IsEnabled=available&&ReadCondition() is NpcConditionEdit desired&&n!.CanSetCondition(desired);
    }

    private void ShowCoordinates()
    {
        var n=Selected?.Snapshot;
        PositionX.Text=n?.X.ToString("0.###",CultureInfo.InvariantCulture)??"";
        PositionY.Text=n?.Y.ToString("0.###",CultureInfo.InvariantCulture)??"";
        PositionZ.Text=n?.Z.ToString("0.###",CultureInfo.InvariantCulture)??"";
    }
    private void CurrentPosition_Click(object sender,RoutedEventArgs e)=>ShowCoordinates();
    private async void Front_Click(object sender,RoutedEventArgs e)=>await ActionAsync("front");
    private async void ToSam_Click(object sender,RoutedEventArgs e)=>await ActionAsync("to-sam");
    private async void Position_Click(object sender,RoutedEventArgs e)
    {
        try {await ActionAsync("position",position:NpcPosition.Parse(PositionX.Text,PositionY.Text,PositionZ.Text));}
        catch(Exception ex) when(Expected(ex)){StatusText.Text=ex.Message;}
    }
    private async void Activity_Click(object sender,RoutedEventArgs e)
    {
        if(Selected?.Snapshot.CanEditActivity!=true||ActivityPicker.SelectedItem is not ComboBoxItem item||!int.TryParse(item.Tag?.ToString(),out int value)||!Enum.IsDefined(typeof(NpcActivityEdit),value))return;
        await ActionAsync("activity",activity:(NpcActivityEdit)value);
    }
    private NpcConditionEdit? ReadCondition() => ConditionPicker.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(),out int value) && Enum.IsDefined(typeof(NpcConditionEdit),value) ? (NpcConditionEdit)value : null;
    private void Condition_SelectionChanged(object sender,SelectionChangedEventArgs e) {if(_ready)ShowSelected(false);}
    private async void Condition_Click(object sender,RoutedEventArgs e)
    {
        if(ReadCondition() is not NpcConditionEdit desired||Selected?.Snapshot.CanSetCondition(desired)!=true)return;
        await ActionAsync("condition",condition:desired);
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
    private async Task ActionAsync(string action,int health=100,NpcConditionEdit? condition=null,NpcActivityEdit? activity=null,NpcPosition? position=null)
    {
        if(_busy||_stale)return;
        uint id=Selected?.Id??0;
        if(action!="spawn"&&id==0)return;
        _busy=true;ShowSelected(false);
        try
        {
            StatusText.Text=action=="spawn"?"Spawning a guard…":$"Updating NPC {id}…";
            StatusText.Text=await DirectLevelLoader.NpcActionAsync(Game(),action,id,health,condition,activity,position);
        }
        catch(Exception ex) when(Expected(ex)){StatusText.Text=ex.Message;}
        finally{_busy=false;await RefreshAsync();ShowSelected(false);}
        if(action=="spawn")NpcList.SelectedItem=_rows.LastOrDefault(n=>n.Snapshot.Present);
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
