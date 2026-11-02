using System.Diagnostics;
using System.Globalization;
namespace SCTrainer;

// Stable actor IDs are used for every action; list positions never identify an NPC.
internal sealed class NpcOverlayMenu : IDisposable
{
    private readonly Func<Process,uint,NpcPosition,Task<string>> _positionAction;
    private readonly Dictionary<uint,string> _names=new();
    private readonly HashSet<uint> _globalExcluded=new();
    private readonly Dictionary<uint,NpcPosition> _positions=new();
    private readonly Func<Process> _openGame;
    private readonly Func<Process,Task<NpcSnapshot[]?>> _read;
    private readonly Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>> _action;
    public NpcOverlayMenu(Func<Process>? openGame=null,Func<Process,Task<NpcSnapshot[]?>>? read=null,Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>>? action=null,Func<Process,uint,NpcPosition,Task<string>>? positionAction=null)
    {
        _positionAction=positionAction??((game,id,position)=>DirectLevelLoader.NpcActionAsync(game,"position",id,position:position));
        _openGame=openGame??OpenGame;
        _read=read??DirectLevelLoader.ReadNpcsAsync;
        _action=action??((game,act,id,hp,condition,activity)=>DirectLevelLoader.NpcActionAsync(game,act,id,hp,condition,activity,waitForCompletion:false));
    }
    private enum Page { Spawn, Roster, Edit, Condition, Activity, Health, Position, Details, RemoveAll, GlobalCondition, GlobalActivity, GlobalConfirm }
    private Page _page;
    private sealed record GlobalEdit(int Pid,uint[] Ids,NpcConditionEdit? Condition,NpcActivityEdit? Activity,string Label,Page Parent,int Token);
    private GlobalEdit? _global;
    private int _globalToken;
    private Page _globalReturnPage=Page.GlobalCondition;
    private static string ConditionLabel(NpcConditionEdit c)=>c==NpcConditionEdit.Alive?"Alive / revive":c==NpcConditionEdit.KnockedOut?"Knocked out":"Dead";
    private static readonly string[] ActivityLabels={"Idle / guard","Walk to point in front of Sam","Run to point in front of Sam","Search point in front of Sam","Alerted"};
    private bool Eligible(NpcSnapshot n,NpcConditionEdit? condition)=>!n.ProtectedGlobal&&!_globalExcluded.Contains(n.Id)&&n.Present&&(condition.HasValue?n.CanSetCondition(condition.Value):n.CanEditActivity);
    private bool GlobalPage=>_page is Page.GlobalCondition or Page.GlobalActivity or Page.GlobalConfirm;

    private bool _mapRoster,_mapRoot;
    public void OpenSpawns(){_global=null;_mapRoot=false;_mapRoster=false;_page=Page.Spawn;}
    public void OpenMapRoster(){_global=null;_mapRoot=true;_mapRoster=true;_page=Page.Roster;}
    private NpcSnapshot[] Spawned=>_npcs.Where(n=>n.TrainerSpawned).ToArray();
    private string DisplayName(NpcSnapshot npc)=>_names.TryGetValue(npc.Id,out var name)?name:npc.TrainerSpawned?$"Guard {npc.Id:00}":string.IsNullOrEmpty(npc.InstanceName)?$"Map NPC {npc.Id}":npc.InstanceName;
    private string SelectedName=>Selected is NpcSnapshot npc?DisplayName(npc):$"NPC {_selected}";
    private NpcPosition Draft(NpcSnapshot npc)=>_positions.TryGetValue(npc.Id,out var value)?value:npc.Position;
    private string InputKey(string kind)=>$"{kind}:{_game?.Id??0}:{_selected}";
    private Process? _game;
    private NpcSnapshot[] _npcs=Array.Empty<NpcSnapshot>();
    private uint _selected;
    private int _selectedPid;
    private DateTime _nextRefresh;
    private bool _stale=true,_disposed;
    public bool Busy {get;private set;}
    public bool Refreshing {get;private set;}
    private NpcSnapshot? Selected=>_npcs.FirstOrDefault(n=>n.Id==_selected);
    private static GameMenuEntry Row(string label,string key,bool available=true,string? value=null,bool navigation=false)=>new(label,false,available,key,value,false,navigation);
    private GameMenuEntry BackRow()=>Row("Back","back",navigation:true);
    public GameMenuFrame Frame
    {
        get {
            bool ready=!Busy&&!_stale;
            var n=Selected;
            var rows=new List<GameMenuEntry>();
            string title="SPAWN NPCs";
            switch(_page) {
                case Page.Spawn:
                    rows.Add(Row("Spawn guard in front of Sam","spawn",ready&&Spawned.Length<8));
                    rows.Add(Row($"Manage spawned NPCs ({Spawned.Length} / 8)","roster",navigation:true));
                    rows.Add(Row($"Map NPCs ({_npcs.Count(n=>!n.TrainerSpawned)})","map-roster",navigation:true));
                    rows.Add(Row("Remove all spawned NPCs","remove-all",ready&&_npcs.Any(n=>n.CanRemove)));
                    break;
                case Page.Roster:
                    title=(_mapRoster?"MAP NPCs":"SPAWNED NPCs")+(_stale?" / UPDATING":"");
                    {
                        rows.Add(Row("Global condition (map + spawned NPCs)","global-condition",navigation:true));
                        rows.Add(Row("Global activity (map + spawned NPCs)","global-activity",navigation:true));
                    }
                    var roster=_npcs.Where(n=>n.TrainerSpawned!=_mapRoster).ToArray();
                    foreach(var npc in roster)rows.Add(Row($"{DisplayName(npc)}\n{npc.Condition} / {npc.Activity}",$"npc:{_game?.Id??0}:{npc.Id}",!_stale,navigation:true));
                    if(roster.Length==0)rows.Add(Row(_stale?"Load a mission to connect":_mapRoster?"No map NPCs":"No spawned NPCs","empty",false,""));
                    break;
                case Page.Edit:
                    title=$"{SelectedName} / {(n is null?"UNAVAILABLE":n.Condition)}";
                    rows.Add(Row("Condition","condition",n is not null,n?.Condition??"--",navigation:true));
                    rows.Add(Row("Activity","activity",n is not null,n?.Activity??"--",navigation:true));
                    rows.Add(Row("Health","health",n is not null,n is null?"--":$"{n.Health} HP",navigation:true));
                    rows.Add(Row("Position","position-menu",n is not null,navigation:true));
                    rows.Add(Row("Move in front of Sam","front",ready&&n?.CanEditPosition==true));
                    rows.Add(new("Rename",false,n is not null&&!Busy,InputKey("rename"),null,false,false,new("NPC display name",SelectedName,MenuInputKind.Name)));
                    rows.Add(new("Exclude from global edits",n is not null&&(n.ProtectedGlobal||_globalExcluded.Contains(n.Id)),n is not null&&!Busy&&!n.ProtectedGlobal,"global-exclude"));
                    rows.Add(Row("Detailed stats","details",n is not null,navigation:true));
                    rows.Add(Row("Remove this NPC","remove",ready&&n?.CanRemove==true));
                    break;
                case Page.Condition:
                    title=$"{SelectedName} / CONDITION";
                    foreach(var c in Enum.GetValues<NpcConditionEdit>())rows.Add(Row(ConditionLabel(c),$"condition:{(int)c}",ready&&n?.CanSetCondition(c)==true));
                    break;
                case Page.Activity:
                    title=$"{SelectedName} / ACTIVITY";
                    var activities=ActivityLabels;
                    for(int i=0;i<activities.Length;++i)rows.Add(Row(activities[i],$"activity:{i+1}",ready&&n?.CanEditActivity==true));
                    break;
                case Page.Health:
                    title=$"{SelectedName} / HEALTH";
                    rows.Add(new("Custom health (1â€“100)",false,ready&&n?.CanEditHealth==true,InputKey("custom-health"),null,false,false,new("NPC health",(n?.Health??100).ToString(CultureInfo.InvariantCulture),MenuInputKind.Health,3)));
                    foreach(int hp in new[]{100,75,50,25,1})rows.Add(Row($"Set health to {hp}",$"health:{hp}",ready&&n?.CanEditHealth==true));
                    break;
                case Page.Position:
                    title=$"{SelectedName} / POSITION";
                    if(n is not null){
                        var draft=Draft(n);
                        foreach(var axis in new[]{("X",draft.X),("Y",draft.Y),("Z",draft.Z)})rows.Add(new($"{axis.Item1}: {axis.Item2:0.###}",false,ready&&n.CanEditPosition,InputKey("coord")+":"+axis.Item1,null,false,false,new($"Position {axis.Item1}",axis.Item2.ToString("0.###",CultureInfo.InvariantCulture),MenuInputKind.Coordinate,24)));
                        rows.Add(Row("Apply XYZ position","apply-position",ready&&n.CanEditPosition));
                        rows.Add(Row("Use current position","use-current",!_stale));
                        rows.Add(Row("In front of Sam","front",ready&&n.CanEditPosition));
                        rows.Add(Row("Move to Sam","to-sam",ready&&n.CanEditPosition));
                    }
                    break;
                case Page.Details:
                    title=$"{SelectedName} / STATS";
                    if(n is not null){
                        string[] stats={n.Origin+" / "+n.Type,"Instance: "+n.InstanceName,$"Health: {n.Health} / 100","Condition: "+n.Condition,"Activity: "+n.Activity,"Movement: "+n.Movement,$"Distance: {n.Distance:0.0} m","Weapon: "+n.Weapon,$"XYZ: {n.X:0.###}, {n.Y:0.###}, {n.Z:0.###}","Pawn state: "+n.PawnState,"AI state: "+n.AiState,"Target: "+n.Target,"Controller: "+(n.Controller?"Attached":"None")};
                        for(int i=0;i<stats.Length;++i)rows.Add(Row(stats[i],"stat:"+i,false,""));
                    }
                    break;
                case Page.GlobalCondition:
                    title="ALL NPCs / CONDITION";
                    foreach(var c in Enum.GetValues<NpcConditionEdit>()){
                        int count=_npcs.Count(npc=>Eligible(npc,c));
                        rows.Add(Row(ConditionLabel(c),$"global-condition:{(int)c}",ready&&count>0,$"{count} eligible",true));
                    }
                    break;
                case Page.GlobalActivity:
                    title="ALL NPCs / ACTIVITY";
                    int eligible=_npcs.Count(npc=>Eligible(npc,null));
                    for(int i=0;i<ActivityLabels.Length;++i)rows.Add(Row(ActivityLabels[i],$"global-activity:{i+1}",ready&&eligible>0,$"{eligible} eligible",true));
                    break;
                case Page.GlobalConfirm:
                    title="APPLY TO ALL NPCs?";
                    rows.Add(Row("Cancel","global-cancel",navigation:true));
                    if(_global is not null)rows.Add(Row($"{_global.Label} / {_global.Ids.Length} NPCs",$"global-confirm:{_global.Token}",ready));
                    break;
                case Page.RemoveAll:
                    title="REMOVE ALL SPAWNED NPCs?";
                    rows.Add(Row("Cancel","cancel",navigation:true));
                    rows.Add(Row($"Yes / remove {_npcs.Count(n=>n.CanRemove)} guards","confirm-remove-all",ready&&_npcs.Any(n=>n.CanRemove)));
                    break;
            }
            rows.Add(BackRow());
            return new($"npc:{_page}:{_mapRoster}:{_selected}:{_game?.Id??0}",title,rows.ToArray());
        }
    }
    public bool Back()
    {
        if(_page==Page.Spawn||(_page==Page.Roster&&_mapRoster&&_mapRoot))return false;
        if(GlobalPage){_page=_page==Page.GlobalConfirm?(_global?.Parent??_globalReturnPage):Page.Roster;_global=null;return true;}
        _page=_page switch {Page.Roster or Page.RemoveAll=>Page.Spawn,Page.Edit=>Page.Roster,_=>Page.Edit};
        return true;
    }
    private Process Game()
    {
        if(_game is not null&&!_game.HasExited)return _game;
        _game?.Dispose();_game=null;_names.Clear();_globalExcluded.Clear();_positions.Clear();_npcs=Array.Empty<NpcSnapshot>();_stale=true;_selected=0;_selectedPid=0;
        return _game=_openGame();
    }
    private static Process OpenGame()
    {
        var games=Process.GetProcessesByName("SplinterCell");
        if(games.Length!=1){foreach(var g in games)g.Dispose();throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission.");}
        try{DirectLevelLoader.ValidateGame(games[0]);return games[0];}
        catch{games[0].Dispose();throw;}
    }
    public async Task RefreshAsync(bool force=false)
    {
        if(_disposed||Refreshing||(!force&&(Busy||DateTime.UtcNow<_nextRefresh)))return;
        Refreshing=true;_nextRefresh=DateTime.UtcNow.AddMilliseconds(750);
        try {
            var game=Game();
            var snapshot=await _read(game);
            if(snapshot is null){if(force)throw new InvalidOperationException("Wait for the current trainer request to finish.");return;}
            if(_disposed)return;
            _npcs=snapshot.Where(n=>n.Present).OrderBy(n=>n.Id).ToArray();_stale=false;
            if(_selected!=0&&!_npcs.Any(n=>n.Id==_selected)){
                _selected=0;_selectedPid=0;
                if(!GlobalPage&&_page is not (Page.Spawn or Page.Roster or Page.RemoveAll))_page=Page.Roster;
            }
        }
        catch{_stale=true;throw;}
        finally{Refreshing=false;}
    }
    public async Task<string> ActivateAsync(string key)
    {
        if(key=="global-exclude"){
            if(Busy||Selected is null||Selected.ProtectedGlobal)return "That NPC is protected or an update is pending.";
            if(!_globalExcluded.Add(Selected.Id))_globalExcluded.Remove(Selected.Id);
            return _globalExcluded.Contains(Selected.Id)?"NPC excluded from global edits.":"NPC included in global edits.";
        }
        if(key is "global-condition" or "global-activity"){

            _selected=0;_selectedPid=0;_global=null;
            _page=key=="global-condition"?Page.GlobalCondition:Page.GlobalActivity;
            return "Includes map and spawned NPCs. Protected and manually excluded NPCs are skipped.";
        }
        if(key=="global-cancel"){
            _page=_global?.Parent??_globalReturnPage;_global=null;return "Global edit cancelled.";
        }
        if(key.StartsWith("global-condition:")||key.StartsWith("global-activity:")){
            if(Busy)return "An NPC update is completing; you can keep browsing.";
            bool condition=key.StartsWith("global-condition:");
            if(_page!=(condition?Page.GlobalCondition:Page.GlobalActivity)||
                !int.TryParse(key[(key.IndexOf(':')+1)..],out int value)||
                !(condition?Enum.IsDefined(typeof(NpcConditionEdit),value):Enum.IsDefined(typeof(NpcActivityEdit),value)))
                throw new InvalidOperationException("Select a valid global edit.");
            Busy=true;
            try {
                while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
                await RefreshAsync(true);var game=Game();
                NpcConditionEdit? c=condition?(NpcConditionEdit)value:null;
                var ids=_npcs.Where(n=>Eligible(n,c)).Select(n=>n.Id).ToArray();
                if(ids.Length==0)throw new InvalidOperationException("No NPCs are eligible for that edit.");
                _global=new(game.Id,ids,c,condition?null:(NpcActivityEdit)value,condition?ConditionLabel(c!.Value):ActivityLabels[value-1],
                    condition?Page.GlobalCondition:Page.GlobalActivity,++_globalToken);
                _globalReturnPage=_global.Parent;_page=Page.GlobalConfirm;
                return c==NpcConditionEdit.Dead?"This kills the selected map NPCs. Mission consequences still apply.":"Confirm to apply to the captured NPCs. Ineligible NPCs will be skipped.";
            } finally{Busy=false;}
        }
        if(key.StartsWith("global-confirm:"))return await ApplyGlobalAsync(key);
        if(key=="roster"){_mapRoster=false;_page=Page.Roster;return "";}
        if(key=="map-roster"){_mapRoot=false;_mapRoster=true;_page=Page.Roster;return "";}
        if(key=="cancel"){_page=Page.Spawn;return "Removal cancelled.";}
        if(key=="remove-all"){_page=Page.RemoveAll;return "Only trainer-spawned NPCs will be removed.";}
        if(key.StartsWith("npc:")){
            var parts=key.Split(':');
            if(parts.Length!=3||!int.TryParse(parts[1],out int pid)||!uint.TryParse(parts[2],out uint id)||_game?.Id!=pid||!_npcs.Any(n=>n.Id==id))throw new InvalidOperationException("That NPC is no longer available.");
            _selected=id;_selectedPid=pid;_page=Page.Edit;return "";
        }
        if(key=="position-menu"){_page=Page.Position;return "Draft coordinates stay unchanged during live updates. Apply XYZ to move the NPC.";}
        if(key=="details"){_page=Page.Details;return _stale?"Values may be stale.":"Live NPC details.";}
        if(key=="use-current"){
            if(_stale||Selected is null)throw new InvalidOperationException("A current NPC snapshot is required.");
            _positions[_selected]=Selected.Position;return "Current coordinates copied to the position draft.";
        }
        int? inputPid=null;uint? inputId=null;
        if(key.StartsWith("rename:")||key.StartsWith("custom-health:")||key.StartsWith("coord:")){
            var parts=key.Split(':');bool coordinate=parts[0]=="coord";
            if(parts.Length!=(coordinate?5:4)||!int.TryParse(parts[1],out int pid)||!uint.TryParse(parts[2],out uint id)||_game?.Id!=pid||!_npcs.Any(n=>n.Id==id))throw new InvalidOperationException("The edited NPC is no longer present.");
            string value=Uri.UnescapeDataString(parts[^1]);
            if(parts[0]=="rename"){
                var validated=new OverlayTextEditor("",new("",value,MenuInputKind.Name)).Value();
                _names[id]=validated;return "Display name changed for this overlay session.";
            }
            if(coordinate){
                float number=float.Parse(new OverlayTextEditor("",new("",value,MenuInputKind.Coordinate)).Value(),CultureInfo.InvariantCulture);
                var npc=_npcs.First(n=>n.Id==id);var draft=Draft(npc);
                _positions[id]=parts[3] switch {"X"=>draft with {X=number},"Y"=>draft with {Y=number},"Z"=>draft with {Z=number},_=>throw new ArgumentException("Unknown coordinate axis.")};
                return "Coordinate draft updated. Select Apply XYZ position to move the NPC.";
            }
            string health=new OverlayTextEditor("",new("",value,MenuInputKind.Health)).Value();inputPid=pid;inputId=id;key="health:"+health;
        }
        if(key is "condition" or "activity" or "health"){
            _page=key=="condition"?Page.Condition:key=="activity"?Page.Activity:Page.Health;return "";
        }
        if(Busy)return "An NPC update is completing; you can keep browsing.";
        uint targetId=inputId??_selected;int targetPid=inputPid??_selectedPid;
        NpcPosition? position=key=="apply-position"&&Selected is not null?Draft(Selected):null;
        Busy=true;
        try {
            // Stop new polling while this action waits for the in-flight snapshot.
            while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
            await RefreshAsync(true);
            var game=Game();
            if(key=="spawn"){
                if(Spawned.Length>=8)throw new InvalidOperationException("Eight spawned NPCs are already present.");
                string result=await _action(game,"spawn",0,100,null,null);
                await RefreshAsync(true);return result;
            }
            if(key=="confirm-remove-all"){
                // Snapshot the confirmed set. New spawns cannot accidentally join the batch.
                var ids=_npcs.Where(n=>n.CanRemove).Select(n=>n.Id).ToArray();
                int removed=0;
                foreach(uint id in ids){await _action(game,"remove",id,100,null,null);++removed;}
                await RefreshAsync(true);if(_page==Page.RemoveAll)_page=Page.Spawn;return $"Removed {removed} spawned NPCs.";
            }
            var npc=_npcs.FirstOrDefault(n=>n.Id==targetId);
            if(targetPid!=game.Id||npc is null)throw new InvalidOperationException("The selected NPC is no longer present.");
            string reply;
            if(key=="remove"&&npc.CanRemove)reply=await _action(game,"remove",npc.Id,100,null,null);
            else if(key=="apply-position"&&npc.CanEditPosition&&position.HasValue&&position.Value.IsValid)reply=await _positionAction(game,npc.Id,position.Value);
            else if(key=="to-sam"&&npc.CanEditPosition)reply=await _action(game,"to-sam",npc.Id,100,null,null);
            else if(key=="front"&&npc.CanEditPosition)reply=await _action(game,"front",npc.Id,100,null,null);
            else if(key.StartsWith("condition:")&&int.TryParse(key.AsSpan(10),out int c)&&Enum.IsDefined(typeof(NpcConditionEdit),c)&&npc.CanSetCondition((NpcConditionEdit)c))reply=await _action(game,"condition",npc.Id,100,(NpcConditionEdit)c,null);
            else if(key.StartsWith("activity:")&&int.TryParse(key.AsSpan(9),out int a)&&Enum.IsDefined(typeof(NpcActivityEdit),a)&&npc.CanEditActivity)reply=await _action(game,"activity",npc.Id,100,null,(NpcActivityEdit)a);
            else if(key.StartsWith("health:")&&int.TryParse(key.AsSpan(7),out int hp)&&hp is >=1 and <=100&&npc.CanEditHealth)reply=await _action(game,"health",npc.Id,hp,null,null);
            else throw new InvalidOperationException("This action is unavailable in the NPC's current state.");
            await RefreshAsync(true);return reply;
        }
        finally {Busy=false;}
    }
    private async Task<string> ApplyGlobalAsync(string key)
    {
        if(Busy)return "An NPC update is completing; you can keep browsing.";
        var edit=_global;
        if(_page!=Page.GlobalConfirm||edit is null||key!=$"global-confirm:{edit.Token}")
            throw new InvalidOperationException("Select and confirm a global edit first.");
        _global=null;Busy=true;int requested=0,skipped=0,failed=0;string? stopped=null;
        try {
            while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
            foreach(uint id in edit.Ids){
                if(_disposed){stopped="NPC menu closed";break;}
                try {
                    await RefreshAsync(true);var game=Game();
                    if(game.Id!=edit.Pid){stopped="game session changed";break;}
                    var npc=_npcs.FirstOrDefault(n=>n.Id==id);
                    if(npc is null||!Eligible(npc,edit.Condition)){++skipped;continue;}
                    await _action(game,edit.Condition.HasValue?"condition":"activity",id,100,edit.Condition,edit.Activity);
                    ++requested;
                }
                catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){
                    ++failed;
                    if(_stale){stopped=ex.Message;break;}
                }
            }
            try{await RefreshAsync(true);}catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){stopped=ex.Message;}
            if(_page==Page.GlobalConfirm)_page=edit.Parent;
            int remaining=edit.Ids.Length-requested-skipped-failed;
            return $"{edit.Label}: {requested} requested, {skipped} skipped, {failed} failed."+
                (stopped is null?" Leave gameplay unpaused to complete transitions.":$" Stopped: {stopped}. {remaining} not processed.");
        }
        finally{Busy=false;}
    }
    public void Dispose(){_disposed=true;_game?.Dispose();}
}
