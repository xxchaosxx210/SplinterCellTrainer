using System.Diagnostics;
using System.Globalization;
namespace SCTrainer;

// Stable actor IDs are used for every action; list positions never identify an NPC.
internal sealed class NpcOverlayMenu : IDisposable
{
    private readonly Func<Process,uint,NpcPosition,Task<string>> _positionAction;
    private readonly Func<Process,uint,int,int,Task<string>> _frontAction;
    private int _moveMetres=3;
    private Page _distanceParent=Page.Position;
    private readonly Dictionary<uint,string> _names=new();
    private readonly HashSet<uint> _globalExcluded=new();
    private readonly HashSet<uint> _moveExcluded=new();
    private readonly HashSet<uint> _frozenIds=new();
    private readonly Func<Process,bool,Task<string>> _freezeControl;
    private bool _freezePing;
    private readonly Dictionary<uint,NpcPosition> _positions=new();
    private readonly Func<Process> _openGame;
    private readonly Func<Process,Task<NpcSnapshot[]?>> _read;
    private readonly Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>> _action;
    public NpcOverlayMenu(Func<Process>? openGame=null,Func<Process,Task<NpcSnapshot[]?>>? read=null,Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>>? action=null,Func<Process,uint,NpcPosition,Task<string>>? positionAction=null,Func<Process,bool,Task<string>>? freezeControl=null,Func<Process,uint,int,int,Task<string>>? frontAction=null)
    {
        _freezeControl=freezeControl??DirectLevelLoader.FreezeControlAsync;
        _positionAction=positionAction??((game,id,position)=>DirectLevelLoader.NpcActionAsync(game,"position",id,position:position));
        _openGame=openGame??OpenGame;
        _read=read??DirectLevelLoader.ReadNpcsAsync;
        _action=action??((game,act,id,hp,condition,activity)=>DirectLevelLoader.NpcActionAsync(game,act,id,hp,condition,activity,waitForCompletion:false));
        _frontAction=frontAction??(action is null?DirectLevelLoader.MoveNpcFrontAsync:(game,id,metres,slot)=>_action(game,"front",id,metres,null,null));
    }
    private Func<bool>? _readEsp,_canToggleEsp;
    private Func<string>? _toggleEsp;
    public void ConfigureEsp(Func<bool> read,Func<string> toggle,Func<bool> available){_readEsp=read;_toggleEsp=toggle;_canToggleEsp=available;}
    private Func<bool>? _readFight;
    private Func<Task<string>>? _toggleFight;
    public void ConfigureFight(Func<bool> read,Func<Task<string>> toggle){_readFight=read;_toggleFight=toggle;}
    private enum Page { Spawn, Roster, Edit, Condition, Activity, Health, Position, Details, RemoveAll, GlobalCondition, GlobalActivity, GlobalConfirm, Global, Filter, Scope, Cleanup, RemoveOne, MoveDistance }
    private Page _page;
    private sealed record GlobalEdit(int Pid,uint[] Ids,NpcConditionEdit? Condition,NpcActivityEdit? Activity,string Label,Page Parent,int Token,string Scope,bool Freeze=false,bool Move=false,int Metres=3);
    private GlobalEdit? _global;
    private int _globalToken;
    private Page _globalReturnPage=Page.GlobalCondition;
    private static string ConditionLabel(NpcConditionEdit c)=>c==NpcConditionEdit.Alive?"Alive / revive":c==NpcConditionEdit.KnockedOut?"Knocked out":"Dead";
    private static readonly string[] ActivityLabels={"Idle / guard","Walk to point in front of Sam","Run to point in front of Sam","Search point in front of Sam","Alerted"};
    private bool Eligible(NpcSnapshot n,NpcConditionEdit? condition)=>!n.ProtectedGlobal&&!_globalExcluded.Contains(n.Id)&&!_frozenIds.Contains(n.Id)&&n.Present&&(condition.HasValue?n.CanSetCondition(condition.Value):n.CanEditActivity);
    private bool CanMoveMap(NpcSnapshot n)=>!n.TrainerSpawned&&!_globalExcluded.Contains(n.Id)&&!_moveExcluded.Contains(n.Id)&&n.CanEditPosition;
    private bool GlobalPage=>_page is Page.GlobalCondition or Page.GlobalActivity or Page.GlobalConfirm;

    private enum NpcScope { All, Map, Spawned }
    private NpcScope _filter=NpcScope.All,_scope=NpcScope.All;
    private static string ScopeLabel(NpcScope scope)=>scope switch {NpcScope.Map=>"Map",NpcScope.Spawned=>"Spawned",_=>"Map + Spawned"};
    private static bool InScope(NpcSnapshot n,NpcScope scope)=>scope==NpcScope.All||(scope==NpcScope.Spawned)==n.TrainerSpawned;
    public void OpenManager(){_global=null;_page=Page.Spawn;}

    public void OpenSpawns(){OpenManager();}
    public void OpenMapRoster(){_global=null;_filter=NpcScope.Map;_page=Page.Roster;}
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
            string title="NPC MANAGER";
            switch(_page) {
                case Page.Spawn:
                    rows.Add(new("Enemy ESP",_readEsp?.Invoke()==true,!Busy&&_toggleEsp is not null&&_canToggleEsp?.Invoke()==true,"npc-esp"));
                    if(_toggleFight is not null)rows.Add(new("Enemies fight each other",_readFight?.Invoke()==true,!Busy&&_canToggleEsp?.Invoke()==true,"npc-fight"));
                    rows.Add(Row("Spawn guard","spawn",ready&&Spawned.Length<8,$"{Spawned.Length} / 8"));
                    rows.Add(Row("Browse NPCs","browse",navigation:true));
                    rows.Add(Row("Global controls","global-menu",navigation:true));
                    rows.Add(Row("Cleanup","cleanup",navigation:true));
                    break;
                case Page.Roster:
                    title="BROWSE NPCs"+(_stale?" / UPDATING":"");
                    rows.Add(Row("Filter","filter-menu",value:_filter==NpcScope.All?"All":ScopeLabel(_filter),navigation:true));
                    var roster=_npcs.Where(n=>InScope(n,_filter)).OrderBy(n=>float.IsFinite(n.Distance)&&n.Distance>=0?n.Distance:float.PositiveInfinity).ThenBy(n=>n.Id).ToArray();
                    foreach(var npc in roster)rows.Add(Row(DisplayName(npc),$"npc:{_game?.Id??0}:{npc.Id}",!_stale,
                        (_frozenIds.Contains(npc.Id)?"Frozen":npc.ProtectedGlobal?"Protected":_globalExcluded.Contains(npc.Id)?"Excluded":npc.Condition)+
                        (float.IsFinite(npc.Distance)&&npc.Distance>=0?$" / {npc.Distance:0.0} m":" / -- m"),true));
                    if(roster.Length==0)rows.Add(Row(_stale?"Load a mission to connect":"No NPCs in this filter","empty",false,""));
                    break;
                case Page.Filter:
                case Page.Scope:
                    title=_page==Page.Filter?"BROWSE FILTER":"GLOBAL TARGET SCOPE";
                    foreach(var choice in Enum.GetValues<NpcScope>())rows.Add(Row(choice==NpcScope.All?"All (map + spawned)":ScopeLabel(choice),
                        (_page==Page.Filter?"filter:":"scope:")+(int)choice,value:(_page==Page.Filter?_filter:_scope)==choice?"Selected":"",navigation:true));
                    break;
                case Page.Global:
                    title="GLOBAL CONTROLS";
                    rows.Add(Row("Scope","scope-menu",value:ScopeLabel(_scope),navigation:true));
                    rows.Add(Row("Condition","global-condition",navigation:true));
                    rows.Add(Row("Activity","global-activity",navigation:true));
                    rows.Add(new(_frozenIds.Count>0?"Unfreeze NPCs":"Freeze NPCs",_frozenIds.Count>0,ready,"global-freeze",_frozenIds.Count>0?$"{_frozenIds.Count} frozen":null));
                    rows.Add(Row("Move distance in front of Sam","move-distance-global",value:$"{_moveMetres} m",navigation:true));
                    rows.Add(Row("Move all map NPCs in front of Sam","move-map-all",ready&&_npcs.Any(npc=>CanMoveMap(npc)),$"{_npcs.Count(npc=>CanMoveMap(npc))} eligible",true));
                    int available=_npcs.Count(npc=>InScope(npc,_scope)&&!npc.ProtectedGlobal&&!_globalExcluded.Contains(npc.Id)&&!_frozenIds.Contains(npc.Id)&&
                        (npc.CanEditActivity||npc.CanSetCondition(NpcConditionEdit.Alive)));
                    int excluded=_npcs.Count(npc=>InScope(npc,_scope)&&(npc.ProtectedGlobal||_globalExcluded.Contains(npc.Id)));
                    if(_frozenIds.Count>0)rows.Add(Row("Unfreeze before saving or editing states","freeze-info",false,""));
                    rows.Add(Row($"Eligible: {available} / Excluded: {excluded}","scope-counts",false,""));
                    break;
                case Page.Cleanup:
                    title="NPC CLEANUP";
                    rows.Add(Row("Remove all spawned NPCs","remove-all",ready&&_npcs.Any(npc=>npc.CanRemove)));
                    rows.Add(Row("Map NPCs cannot be removed","cleanup-info",false,""));
                    break;
                case Page.RemoveOne:
                    title=$"REMOVE {SelectedName}?";
                    rows.Add(Row("Cancel","remove-cancel",navigation:true));
                    rows.Add(Row("Yes / remove NPC",$"remove-confirm:{_selectedPid}:{_selected}",ready&&n?.CanRemove==true));
                    break;
                case Page.Edit:
                    title=$"{SelectedName} / {(n?.TrainerSpawned==true?"SPAWNED":"MAP")}";
                    rows.Add(Row(n is null?"Unavailable":$"{n.Condition} / {n.Activity} / {n.Health} HP","npc-summary",false,""));
                    rows.Add(Row("Condition","condition",n is not null,n?.Condition??"--",navigation:true));
                    rows.Add(Row("Activity","activity",n is not null,n?.Activity??"--",navigation:true));
                    rows.Add(Row("Health","health",n is not null,n is null?"--":$"{n.Health} HP",navigation:true));
                    rows.Add(Row("Position","position-menu",n is not null,navigation:true));
                    rows.Add(Row("Details / Rename","details",n is not null,navigation:true));
                    rows.Add(new("Exclude from global edits",n is not null&&(n.ProtectedGlobal||_globalExcluded.Contains(n.Id)),n is not null&&!Busy&&!n.ProtectedGlobal,"global-exclude"));
                    rows.Add(Row("Remove NPC","remove-menu",ready&&n?.CanRemove==true));
                    break;
                case Page.Condition:
                    title=$"{SelectedName} / CONDITION";
                    foreach(var c in Enum.GetValues<NpcConditionEdit>())rows.Add(Row(ConditionLabel(c),$"condition:{(int)c}",ready&&!_frozenIds.Contains(_selected)&&n?.CanSetCondition(c)==true));
                    break;
                case Page.Activity:
                    title=$"{SelectedName} / ACTIVITY";
                    var activities=ActivityLabels;
                    for(int i=0;i<activities.Length;++i)rows.Add(Row(activities[i],$"activity:{i+1}",ready&&!_frozenIds.Contains(_selected)&&n?.CanEditActivity==true));
                    break;
                case Page.Health:
                    title=$"{SelectedName} / HEALTH";
                    rows.Add(new("Custom health (1-100)",false,ready&&n?.CanEditHealth==true,InputKey("custom-health"),null,false,false,new("NPC health",(n?.Health??100).ToString(CultureInfo.InvariantCulture),MenuInputKind.Health,3)));
                    foreach(int hp in new[]{100,75,50,25,1})rows.Add(Row($"Set health to {hp}",$"health:{hp}",ready&&n?.CanEditHealth==true));
                    break;
                case Page.MoveDistance:
                    title="MOVE DISTANCE";
                    for(int metres=3;metres<=6;++metres)rows.Add(Row($"{metres} metres","move-distance:"+metres,value:_moveMetres==metres?"Selected":"",navigation:true));
                    break;
                case Page.Position:
                    title=$"{SelectedName} / POSITION";
                    if(n is not null){
                        var draft=Draft(n);
                        foreach(var axis in new[]{("X",draft.X),("Y",draft.Y),("Z",draft.Z)})rows.Add(new($"{axis.Item1}: {axis.Item2:0.###}",false,ready&&n.CanEditPosition,InputKey("coord")+":"+axis.Item1,null,false,false,new($"Position {axis.Item1}",axis.Item2.ToString("0.###",CultureInfo.InvariantCulture),MenuInputKind.Coordinate,24)));
                        rows.Add(Row("Apply XYZ position","apply-position",ready&&n.CanEditPosition));
                        rows.Add(Row("Use current position","use-current",!_stale));
                        rows.Add(Row("Distance in front of Sam","move-distance-position",value:$"{_moveMetres} m",navigation:true));
                        rows.Add(Row("In front of Sam","front",ready&&n.CanEditPosition));
                        rows.Add(Row("Move to Sam","to-sam",ready&&n.CanEditPosition));
                        if(!n.TrainerSpawned)rows.Add(new("Exclude from group moves",_moveExcluded.Contains(n.Id),!Busy,"move-exclude"));
                    }
                    break;
                case Page.Details:
                    title=$"{SelectedName} / DETAILS";
                    if(n is not null){
                        rows.Add(new("Rename",false,!Busy,InputKey("rename"),null,false,false,new("NPC display name",SelectedName,MenuInputKind.Name)));
                        string[] stats={n.Origin+" / "+n.Type,"Instance: "+n.InstanceName,$"Health: {n.Health} / 100","Condition: "+n.Condition,"Activity: "+n.Activity,"Movement: "+n.Movement,$"Distance: {n.Distance:0.0} m","Weapon: "+n.Weapon,$"XYZ: {n.X:0.###}, {n.Y:0.###}, {n.Z:0.###}","Pawn state: "+n.PawnState,"AI state: "+n.AiState,"Target: "+n.Target,"Controller: "+(n.Controller?"Attached":"None")};
                        for(int i=0;i<stats.Length;++i)rows.Add(Row(stats[i],"stat:"+i,false,""));
                    }
                    break;
                case Page.GlobalCondition:
                    title=ScopeLabel(_scope).ToUpperInvariant()+" / CONDITION";
                    foreach(var c in Enum.GetValues<NpcConditionEdit>()){
                        int count=_npcs.Count(npc=>InScope(npc,_scope)&&Eligible(npc,c));
                        rows.Add(Row(ConditionLabel(c),$"global-condition:{(int)c}",ready&&count>0,$"{count} eligible",true));
                    }
                    break;
                case Page.GlobalActivity:
                    title=ScopeLabel(_scope).ToUpperInvariant()+" / ACTIVITY";
                    int eligible=_npcs.Count(npc=>InScope(npc,_scope)&&Eligible(npc,null));
                    for(int i=0;i<ActivityLabels.Length;++i)rows.Add(Row(ActivityLabels[i],$"global-activity:{i+1}",ready&&eligible>0,$"{eligible} eligible",true));
                    break;
                case Page.GlobalConfirm:
                    title=$"APPLY TO {_global?.Scope??ScopeLabel(_scope)}?";
                    if(_global is not null)rows.Add(Row("Cancel","global-cancel",navigation:true));
                    else if(Busy)rows.Add(Row("Applying requests...","global-pending",false,""));
                    if(_global is not null)rows.Add(Row($"{_global.Label} / {_global.Ids.Length} NPCs",$"global-confirm:{_global.Token}",ready));
                    break;
                case Page.RemoveAll:
                    title="REMOVE ALL SPAWNED NPCs?";
                    rows.Add(Row("Cancel","cancel",navigation:true));
                    rows.Add(Row($"Yes / remove {_npcs.Count(n=>n.CanRemove)} guards","confirm-remove-all",ready&&_npcs.Any(n=>n.CanRemove)));
                    break;
            }
            rows.Add(BackRow());
            return new($"npc:{_page}:{_filter}:{_scope}:{_selected}:{_game?.Id??0}",title,rows.ToArray());
        }
    }
    public bool Back()
    {
        if(_page==Page.Spawn)return false;
        if(GlobalPage){_page=_page==Page.GlobalConfirm?(_global?.Parent??_globalReturnPage):Page.Global;_global=null;return true;}
        _page=_page switch {
            Page.Roster or Page.Global or Page.Cleanup=>Page.Spawn,
            Page.Filter=>Page.Roster,Page.Scope=>Page.Global,Page.RemoveAll=>Page.Cleanup,
            Page.Edit=>Page.Roster,Page.RemoveOne=>Page.Edit,Page.MoveDistance=>_distanceParent,_=>Page.Edit
        };
        return true;
    }
    private Process Game()
    {
        if(_game is not null&&!_game.HasExited)return _game;
        _game?.Dispose();_game=null;_names.Clear();_globalExcluded.Clear();_moveExcluded.Clear();_frozenIds.Clear();_positions.Clear();_npcs=Array.Empty<NpcSnapshot>();_stale=true;_selected=0;_selectedPid=0;
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
                if(_page is Page.Edit or Page.Condition or Page.Activity or Page.Health or Page.Position or Page.Details or Page.RemoveOne)_page=Page.Roster;
            }
        }
        catch{_stale=true;throw;}
        finally{Refreshing=false;}
    }
    public async Task<string> ActivateAsync(string key)
    {
        if(key is "move-distance-global" or "move-distance-position"){
            _distanceParent=key=="move-distance-global"?Page.Global:Page.Position;_page=Page.MoveDistance;return "Choose 3, 4, 5 or 6 metres in front of Sam.";
        }
        if(key.StartsWith("move-distance:")){
            if(_page!=Page.MoveDistance||!int.TryParse(key.AsSpan(14),out int metres)||metres<3||metres>6)throw new InvalidOperationException("Move distance must be from 3 to 6 metres.");
            _moveMetres=metres;_page=_distanceParent;return $"Move distance: {metres} metres.";
        }
        if(key=="move-map-all"){
            if(Busy)return "An NPC update is completing; you can keep browsing.";
            Busy=true;
            try{
                while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
                await RefreshAsync(true);var game=Game();
                var ids=_npcs.Where(CanMoveMap).Select(n=>n.Id).ToArray();
                if(ids.Length==0)throw new InvalidOperationException("No map NPCs can currently be moved.");
                _global=new(game.Id,ids,null,null,$"Move map NPCs / {_moveMetres} m",Page.Global,++_globalToken,"Map (including protected NPCs)",Move:true,Metres:_moveMetres);
                _globalReturnPage=Page.Global;_page=Page.GlobalConfirm;
                return "Moves existing map NPCs, including mission characters. This may affect mission scripts. Leave Sam still; blocked moves are skipped.";
            }finally{Busy=false;}
        }
        if(key=="move-exclude"){
            if(Busy||Selected is null||Selected.TrainerSpawned)return "Select a map NPC outside a pending update.";
            if(!_moveExcluded.Add(Selected.Id))_moveExcluded.Remove(Selected.Id);
            return _moveExcluded.Contains(Selected.Id)?"NPC excluded from group moves.":"NPC included in group moves.";
        }
        if(key=="npc-fight"){
            if(Busy||_toggleFight is null||_canToggleEsp?.Invoke()!=true)return "Wait for the current trainer update to finish.";
            return await _toggleFight();
        }
        if(key=="npc-esp"){
            if(Busy||_toggleEsp is null||_canToggleEsp?.Invoke()!=true)return "Wait for the current trainer update to finish.";
            return _toggleEsp();
        }
        if(key is "browse" or "global-menu" or "cleanup" or "filter-menu" or "scope-menu"){
            _global=null;
            _page=key switch {"browse"=>Page.Roster,"global-menu"=>Page.Global,"cleanup"=>Page.Cleanup,"filter-menu"=>Page.Filter,_=>Page.Scope};return "";
        }
        if(key.StartsWith("filter:")||key.StartsWith("scope:")){
            bool filter=key.StartsWith("filter:");
            if(_page!=(filter?Page.Filter:Page.Scope)||!int.TryParse(key[(key.IndexOf(':')+1)..],out int value)||!Enum.IsDefined(typeof(NpcScope),value))
                throw new InvalidOperationException("Select a valid NPC scope.");
            if(filter){_filter=(NpcScope)value;_page=Page.Roster;}
            else{_scope=(NpcScope)value;_page=Page.Global;}
            return "";
        }
        if(key=="remove-menu"){
            if(Selected?.CanRemove!=true)throw new InvalidOperationException("Only trainer-spawned NPCs can be removed.");
            _page=Page.RemoveOne;return "Confirm removal of this spawned NPC.";
        }
        if(key=="remove-cancel"){_page=Page.Edit;return "Removal cancelled.";}
        if(key.StartsWith("remove-confirm:")){
            if(_page!=Page.RemoveOne||key!=$"remove-confirm:{_selectedPid}:{_selected}")throw new InvalidOperationException("Confirm the selected NPC first.");
            key="remove";
        }
        if(key=="global-freeze"){
            if(Busy)return "An NPC update is completing; you can keep browsing.";
            if(_frozenIds.Count>0)return await RestoreFrozenAsync();
            Busy=true;
            try{
                while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
                await RefreshAsync(true);var game=Game();
                var ids=_npcs.Where(n=>InScope(n,_scope)&&Eligible(n,null)).Select(n=>n.Id).ToArray();
                if(ids.Length==0)throw new InvalidOperationException("No NPCs are eligible for freezing.");
                _global=new(game.Id,ids,null,null,"Freeze NPCs",Page.Global,++_globalToken,ScopeLabel(_scope),true);
                _globalReturnPage=Page.Global;_page=Page.GlobalConfirm;return "Freeze the captured NPCs? Protected and excluded NPCs are skipped.";
            }finally{Busy=false;}
        }
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
                var ids=_npcs.Where(n=>InScope(n,_scope)&&Eligible(n,c)).Select(n=>n.Id).ToArray();
                if(ids.Length==0)throw new InvalidOperationException("No NPCs are eligible for that edit.");
                _global=new(game.Id,ids,c,condition?null:(NpcActivityEdit)value,condition?ConditionLabel(c!.Value):ActivityLabels[value-1],
                    condition?Page.GlobalCondition:Page.GlobalActivity,++_globalToken,ScopeLabel(_scope));
                _globalReturnPage=_global.Parent;_page=Page.GlobalConfirm;
                return c==NpcConditionEdit.Dead?"This kills the selected map NPCs. Mission consequences still apply.":"Confirm to apply to the captured NPCs. Ineligible NPCs will be skipped.";
            } finally{Busy=false;}
        }
        if(key.StartsWith("global-confirm:"))return await ApplyGlobalAsync(key);
        if(key=="roster"){_filter=NpcScope.Spawned;_page=Page.Roster;return "";}
        if(key=="map-roster"){_filter=NpcScope.Map;_page=Page.Roster;return "";}
        if(key=="cancel"){_page=Page.Cleanup;return "Removal cancelled.";}
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
        int moveMetres=_moveMetres;
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
                await RefreshAsync(true);if(_page==Page.RemoveAll)_page=Page.Cleanup;return $"Removed {removed} spawned NPCs.";
            }
            var npc=_npcs.FirstOrDefault(n=>n.Id==targetId);
            if(targetPid!=game.Id||npc is null)throw new InvalidOperationException("The selected NPC is no longer present.");
            if(_frozenIds.Contains(npc.Id)&&(key.StartsWith("condition:")||key.StartsWith("activity:")))throw new InvalidOperationException("Unfreeze NPCs before changing their condition or activity.");
            string reply;
            if(key=="remove"&&npc.CanRemove)reply=await _action(game,"remove",npc.Id,100,null,null);
            else if(key=="apply-position"&&npc.CanEditPosition&&position.HasValue&&position.Value.IsValid)reply=await _positionAction(game,npc.Id,position.Value);
            else if(key=="to-sam"&&npc.CanEditPosition)reply=await _action(game,"to-sam",npc.Id,100,null,null);
            else if(key=="front"&&npc.CanEditPosition)reply=await _frontAction(game,npc.Id,moveMetres,0);
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
        _global=null;Busy=true;int requested=0,skipped=0,failed=0;string? stopped=null,firstFailure=null;
        try {
            while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("The NPC menu has closed.");}
            int slot=0;
            foreach(uint id in edit.Ids){
                int moveSlot=slot++;
                if(_disposed){stopped="NPC menu closed";break;}
                try {
                    await RefreshAsync(true);var game=Game();
                    if(game.Id!=edit.Pid){stopped="game session changed";break;}
                    var npc=_npcs.FirstOrDefault(n=>n.Id==id);
                    if(npc is null||(edit.Move?!CanMoveMap(npc):!Eligible(npc,edit.Condition))){++skipped;continue;}
                    if(edit.Move){
                        if(_frozenIds.Count>0&&await _freezeControl(game,false)=="FROZEN:0")_frozenIds.Clear();
                        await _frontAction(game,id,edit.Metres,moveSlot);
                    }
                    else await _action(game,edit.Freeze?"freeze":edit.Condition.HasValue?"condition":"activity",id,100,edit.Condition,edit.Activity);
                    if(edit.Freeze)_frozenIds.Add(id);
                    ++requested;
                }
                catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){
                    ++failed;firstFailure??=ex.Message;
                    if(_stale){stopped=ex.Message;break;}
                }
            }
            try{await RefreshAsync(true);}catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){stopped=ex.Message;}
            if(_page==Page.GlobalConfirm)_page=edit.Parent;
            int remaining=edit.Ids.Length-requested-skipped-failed;
            return $"{edit.Label}: {requested} requested, {skipped} skipped, {failed} failed."+
                (stopped is null?(firstFailure is null?" Leave gameplay unpaused to complete transitions.":" First failure: "+firstFailure):$" Stopped: {stopped}. {remaining} not processed.");
        }
        finally{Busy=false;}
    }
    public async Task PingFreezeAsync(){
        if(_disposed||Busy||Refreshing||_freezePing||_frozenIds.Count==0)return;
        _freezePing=true;
        try{if(await _freezeControl(Game(),false)=="FROZEN:0")_frozenIds.Clear();}
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException){}
        finally{_freezePing=false;}
    }
    public async Task<string> RestoreFrozenAsync(){
        if(Busy)throw new InvalidOperationException("Wait for the current NPC update.");
        if(_frozenIds.Count==0)return "No NPCs frozen.";
        if(_game is null||_game.HasExited){_frozenIds.Clear();return "Game closed; freeze cleared.";}
        Busy=true;
        try{
            while(_freezePing)await Task.Delay(25);
            var result=await _freezeControl(_game,true);
            if(result!="FROZEN:0")throw new InvalidOperationException("NPC restoration was not confirmed.");
            _frozenIds.Clear();return "NPCs unfrozen; original AI settings restored.";
        }finally{Busy=false;}
    }
    public void Dispose(){_disposed=true;_game?.Dispose();}
}
