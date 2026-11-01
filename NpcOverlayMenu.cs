using System.Diagnostics;
namespace SCTrainer;

// Stable actor IDs are used for every action; list positions never identify an NPC.
internal sealed class NpcOverlayMenu : IDisposable
{
    private readonly Func<Process> _openGame;
    private readonly Func<Process,Task<NpcSnapshot[]?>> _read;
    private readonly Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>> _action;
    public NpcOverlayMenu(Func<Process>? openGame=null,Func<Process,Task<NpcSnapshot[]?>>? read=null,Func<Process,string,uint,int,NpcConditionEdit?,NpcActivityEdit?,Task<string>>? action=null)
    {
        _openGame=openGame??OpenGame;
        _read=read??DirectLevelLoader.ReadNpcsAsync;
        _action=action??((game,act,id,hp,condition,activity)=>DirectLevelLoader.NpcActionAsync(game,act,id,hp,condition,activity,waitForCompletion:false));
    }
    private enum Page { Spawn, Roster, Edit, Condition, Activity, Health, RemoveAll }
    private Page _page;
    private bool _mapRoster,_mapRoot;
    public void OpenSpawns(){_mapRoot=false;_mapRoster=false;_page=Page.Spawn;}
    public void OpenMapRoster(){_mapRoot=true;_mapRoster=true;_page=Page.Roster;}
    private NpcSnapshot[] Spawned=>_npcs.Where(n=>n.TrainerSpawned).ToArray();
    private string SelectedName=>Selected?.TrainerSpawned==false?(string.IsNullOrEmpty(Selected.InstanceName)?$"Map NPC {_selected}":Selected.InstanceName):$"Guard {_selected:00}";
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
                    var roster=_npcs.Where(n=>n.TrainerSpawned!=_mapRoster).ToArray();
                    foreach(var npc in roster)rows.Add(Row($"{(npc.TrainerSpawned?$"Guard {npc.Id:00}":string.IsNullOrEmpty(npc.InstanceName)?$"Map NPC {npc.Id}":npc.InstanceName)}\n{npc.Condition} / {npc.Activity}",$"npc:{_game?.Id??0}:{npc.Id}",!_stale,navigation:true));
                    if(roster.Length==0)rows.Add(Row(_stale?"Load a mission to connect":_mapRoster?"No map NPCs":"No spawned NPCs","empty",false,""));
                    break;
                case Page.Edit:
                    title=$"{SelectedName} / {(n is null?"UNAVAILABLE":n.Condition)}";
                    rows.Add(Row("Condition","condition",n is not null,n?.Condition??"--",navigation:true));
                    rows.Add(Row("Activity","activity",n is not null,n?.Activity??"--",navigation:true));
                    rows.Add(Row("Health","health",n is not null,n is null?"--":$"{n.Health} HP",navigation:true));
                    rows.Add(Row("Move in front of Sam","front",ready&&n?.CanEditPosition==true));
                    rows.Add(Row("Remove this NPC","remove",ready&&n?.CanRemove==true));
                    break;
                case Page.Condition:
                    title=$"{SelectedName} / CONDITION";
                    foreach(var c in Enum.GetValues<NpcConditionEdit>())rows.Add(Row(c==NpcConditionEdit.Alive?"Alive / revive":c==NpcConditionEdit.KnockedOut?"Knocked out":"Dead",$"condition:{(int)c}",ready&&n?.CanSetCondition(c)==true));
                    break;
                case Page.Activity:
                    title=$"{SelectedName} / ACTIVITY";
                    string[] activities={"Idle / guard","Walk to point in front of Sam","Run to point in front of Sam","Search point in front of Sam","Alerted"};
                    for(int i=0;i<activities.Length;++i)rows.Add(Row(activities[i],$"activity:{i+1}",ready&&n?.CanEditActivity==true));
                    break;
                case Page.Health:
                    title=$"{SelectedName} / HEALTH";
                    foreach(int hp in new[]{100,75,50,25,1})rows.Add(Row($"Set health to {hp}",$"health:{hp}",ready&&n?.CanEditHealth==true));
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
        _page=_page switch {Page.Roster or Page.RemoveAll=>Page.Spawn,Page.Edit=>Page.Roster,_=>Page.Edit};
        return true;
    }
    private Process Game()
    {
        if(_game is not null&&!_game.HasExited)return _game;
        _game?.Dispose();_game=null;_npcs=Array.Empty<NpcSnapshot>();_stale=true;_selected=0;_selectedPid=0;
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
                if(_page is Page.Edit or Page.Condition or Page.Activity or Page.Health)_page=Page.Roster;
            }
        }
        catch{_stale=true;throw;}
        finally{Refreshing=false;}
    }
    public async Task<string> ActivateAsync(string key)
    {
        if(key=="roster"){_mapRoster=false;_page=Page.Roster;return "";}
        if(key=="map-roster"){_mapRoot=false;_mapRoster=true;_page=Page.Roster;return "";}
        if(key=="cancel"){_page=Page.Spawn;return "Removal cancelled.";}
        if(key=="remove-all"){_page=Page.RemoveAll;return "Only trainer-spawned NPCs will be removed.";}
        if(key.StartsWith("npc:")){
            var parts=key.Split(':');
            if(parts.Length!=3||!int.TryParse(parts[1],out int pid)||!uint.TryParse(parts[2],out uint id)||_game?.Id!=pid||!_npcs.Any(n=>n.Id==id))throw new InvalidOperationException("That NPC is no longer available.");
            _selected=id;_selectedPid=pid;_page=Page.Edit;return "";
        }
        if(key is "condition" or "activity" or "health"){
            _page=key=="condition"?Page.Condition:key=="activity"?Page.Activity:Page.Health;return "";
        }
        if(Busy)return "An NPC update is completing; you can keep browsing.";
        uint targetId=_selected;int targetPid=_selectedPid;
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
            else if(key=="front"&&npc.CanEditPosition)reply=await _action(game,"front",npc.Id,100,null,null);
            else if(key.StartsWith("condition:")&&int.TryParse(key.AsSpan(10),out int c)&&Enum.IsDefined(typeof(NpcConditionEdit),c)&&npc.CanSetCondition((NpcConditionEdit)c))reply=await _action(game,"condition",npc.Id,100,(NpcConditionEdit)c,null);
            else if(key.StartsWith("activity:")&&int.TryParse(key.AsSpan(9),out int a)&&Enum.IsDefined(typeof(NpcActivityEdit),a)&&npc.CanEditActivity)reply=await _action(game,"activity",npc.Id,100,null,(NpcActivityEdit)a);
            else if(key.StartsWith("health:")&&int.TryParse(key.AsSpan(7),out int hp)&&hp is >=1 and <=100&&npc.CanEditHealth)reply=await _action(game,"health",npc.Id,hp,null,null);
            else throw new InvalidOperationException("This action is unavailable in the NPC's current state.");
            await RefreshAsync(true);return reply;
        }
        finally {Busy=false;}
    }
    public void Dispose(){_disposed=true;_game?.Dispose();}
}
