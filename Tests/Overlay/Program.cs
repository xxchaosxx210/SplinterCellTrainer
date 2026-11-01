using System.Diagnostics;
using SCTrainer;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
async Task Reject(Func<Task> action,string label){try{await action();}catch(InvalidOperationException){Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
NpcSnapshot Guard(uint id,bool spawned=true,bool present=true)=>new(id,present,false,true,0,100,0,2,"Cop","DefaultState","s_Default","Pistol","None",spawned);
var snapshots=new List<NpcSnapshot>{Guard(7),Guard(9),Guard(1,false),Guard(3,true,false)};
var calls=new List<(string Action,uint Id,int Hp,NpcConditionEdit? Condition,NpcActivityEdit? Activity)>();
using var menu=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(snapshots.ToArray()),(_,action,id,hp,condition,activity)=>{
 calls.Add((action,id,hp,condition,activity));
 if(action=="remove")snapshots.RemoveAll(n=>n.Id==id);
 return Task.FromResult("Updated");
});
Check(!menu.Frame.Entries.First(e=>e.Key=="spawn").Available,"spawn disabled until a live snapshot is available");
await menu.RefreshAsync(true);
await menu.ActivateAsync("roster");
Check(menu.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==2,"roster excludes map NPCs and removed spawned tombstones");
string Key(uint id)=>$"npc:{Environment.ProcessId}:{id}";
await menu.ActivateAsync(Key(9));
snapshots.Insert(0,Guard(2));
await menu.ActivateAsync("health:25");
Check(calls[^1] is ("health",9,25,null,null),"reordered roster still edits the selected stable actor ID");
await menu.ActivateAsync("activity:2");
Check(calls[^1].Id==9&&calls[^1].Activity==NpcActivityEdit.Walking,"walking forwards the selected NPC ID and native activity");
int before=calls.Count;
await Reject(()=>menu.ActivateAsync("condition:99"),"invalid condition does not reach native code");
Check(calls.Count==before,"invalid action leaves NPCs unchanged");
await menu.ActivateAsync("remove");
Check(menu.Frame.Title=="SPAWNED NPCs"&&!menu.Frame.Entries.Any(e=>e.Key==Key(9)),"confirmed removal drops the row and returns to roster");
await menu.ActivateAsync(Key(7));
snapshots.RemoveAll(n=>n.Id==7);
before=calls.Count;
await Reject(()=>menu.ActivateAsync("front"),"despawned selected actor rejected after fresh snapshot");
Check(calls.Count==before,"despawn never retargets an action to another NPC");
menu.Back();
before=calls.Count;
await menu.ActivateAsync("remove-all");
Check(calls.Count==before&&menu.Frame.Entries[0].Key=="cancel","bulk removal requires confirmation and defaults to cancel");
await menu.ActivateAsync("cancel");
Check(calls.Count==before,"cancelling bulk removal invokes no native actions");
await menu.ActivateAsync("remove-all");await menu.ActivateAsync("confirm-remove-all");
Check(calls.Skip(before).All(c=>c.Action=="remove"&&c.Id==2),"bulk removal only targets present trainer-spawned guards");
Check(snapshots.Any(n=>n.Id==1&&!n.TrainerSpawned),"map NPC is preserved during remove all");
snapshots.Clear();for(uint i=1;i<=8;++i)snapshots.Add(Guard(i));await menu.RefreshAsync(true);
Check(!menu.Frame.Entries.First(e=>e.Key=="spawn").Available,"eight-spawn limit disables overlay spawning");
await Reject(()=>menu.ActivateAsync("spawn"),"spawn limit also enforced before native action");
await menu.ActivateAsync("roster");await menu.ActivateAsync(Key(1));
snapshots[0]=snapshots[0] with {PawnState="s_Dying",Controller=false,Health=0};await menu.RefreshAsync(true);
Check(menu.Frame.Entries.First(e=>e.Key=="front").Available==false,"dead NPC health and movement edits disabled");
Check(menu.Back()&&menu.Back()&&!menu.Back(),"back navigation returns editor to roster to spawn to main");


var readStarted=new TaskCompletionSource<bool>();var releaseRead=new TaskCompletionSource<bool>();bool delayRead=false;int spawnCalls=0;
using var racing=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),async _=>{if(delayRead){readStarted.TrySetResult(true);await releaseRead.Task;}return Array.Empty<NpcSnapshot>();},(_,action,id,hp,c,a)=>{++spawnCalls;return Task.FromResult("Spawned");});
await racing.RefreshAsync(true);delayRead=true;
var inFlight=racing.RefreshAsync(true);await readStarted.Task;
var queued=racing.ActivateAsync("spawn");await Task.Delay(50);
Check(!queued.IsCompleted&&spawnCalls==0,"spawn waits for in-flight roster refresh instead of failing or racing");
releaseRead.SetResult(true);await inFlight;await queued;
Check(spawnCalls==1&&!racing.Busy,"queued spawn executes exactly once after refresh finishes");


var mapSnapshots=new[]{Guard(44,false) with {InstanceName="MapGuard44"},Guard(45,false) with {InstanceName="MapGuard45"},Guard(55)};
var pendingAction=new TaskCompletionSource<string>();var actionStarted=new TaskCompletionSource<uint>();uint editedId=0;
using var maps=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(mapSnapshots),(_,action,id,hp,c,a)=>{editedId=id;actionStarted.TrySetResult(id);return pendingAction.Task;});
await maps.RefreshAsync(true);maps.OpenMapRoster();
Check(maps.Frame.Title=="MAP NPCs"&&maps.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==2&&maps.Frame.Entries[0].Label.StartsWith("MapGuard44"),"map roster shows named map NPCs separately from spawned guards");
await maps.ActivateAsync(Key(44));
Check(!maps.Frame.Entries.First(e=>e.Key=="remove").Available,"map NPC removal stays disabled in editor");
await Reject(()=>maps.ActivateAsync("remove"),"map NPC remove action is rejected before native code");
var updating=maps.ActivateAsync("health:50");await actionStarted.Task;
Check(maps.Busy&&maps.Frame.Entries.First(e=>e.Key=="back").Available,"back remains available during a pending NPC action");
Check(maps.Back()&&maps.Frame.Title=="MAP NPCs","pending update does not prevent returning to the map roster");
await maps.ActivateAsync(Key(45));await maps.ActivateAsync("condition");
Check(maps.Frame.Title.Contains("CONDITION"),"condition submenu can be browsed during a pending action");
Check(!maps.Frame.Entries.First(e=>e.Key=="condition:2").Available,"mutating actions remain blocked while a native request is pending");
pendingAction.SetResult("Updated");await updating;
Check(editedId==44&&!maps.Busy,"pending update keeps its original map NPC target while browsing");
Check(maps.Back()&&maps.Back()&&!maps.Back(),"map roster opened from main returns directly to main");


var installedMaps=new[]{new LevelMenuChoice("1_1_0Tbilisi","Police Station  /  1_1_0Tbilisi"),new LevelMenuChoice("1_1_1Tbilisi","Police Station  /  1_1_1Tbilisi"),new LevelMenuChoice("0_0_2_Training","Training  /  0_0_2_Training")};
var levelRequests=new List<string>();var completeLoad=new TaskCompletionSource<string>();
var levels=new LevelOverlayMenu(()=>installedMaps,l=>{levelRequests.Add(l.Map);return completeLoad.Task;});
Check(levels.Frame.Entries.Count(e=>e.Key.StartsWith("mission:"))==2,"installed levels grouped by mission");
await Reject(()=>levels.ActivateAsync("load:1_1_0Tbilisi"),"direct load without selection and confirmation rejected");
await levels.ActivateAsync("mission:Police Station");
Check(levels.Frame.Entries.Count(e=>e.Key.StartsWith("level:"))==2,"mission submenu shows its installed map sections");
await Reject(()=>levels.ActivateAsync("level:0_0_2_Training"),"map from a different mission cannot be selected");
await levels.ActivateAsync("level:1_1_1Tbilisi");
Check(levels.Frame.Entries[0].Key=="level-cancel"&&levelRequests.Count==0,"load confirmation defaults to cancel and does not send travel");
await levels.ActivateAsync("level-cancel");Check(levelRequests.Count==0,"cancelled level selection never requests travel");
await levels.ActivateAsync("level:1_1_1Tbilisi");
var loading=levels.ActivateAsync("load:1_1_1Tbilisi");
Check(levels.Busy&&!levels.Frame.Entries.First(e=>e.Key.StartsWith("load:")).Available,"confirmed map load disables duplicate requests");
await levels.ActivateAsync("load:1_1_1Tbilisi");Check(levelRequests.SequenceEqual(new[]{"1_1_1Tbilisi"}),"confirmed level identity used exactly once");
Check(levels.Back()&&levels.Back()&&!levels.Back(),"back works during loading and returns through mission list to main");
completeLoad.SetResult("Loaded");await loading;Check(!levels.Busy,"loading state clears after completion");
var blockedLevels=new LevelOverlayMenu(()=>installedMaps,_=>throw new InvalidOperationException("Return to the main menu"));
await blockedLevels.ActivateAsync("mission:Training");await blockedLevels.ActivateAsync("level:0_0_2_Training");
await Reject(()=>blockedLevels.ActivateAsync("load:0_0_2_Training"),"shared loader refusal is surfaced rather than bypassed");
Check(!blockedLevels.Busy,"failed level load clears busy state");
Check(!new LevelOverlayMenu(()=>Array.Empty<LevelMenuChoice>(),_=>Task.FromResult("")).Frame.Entries[0].Available,"empty map installation cannot request travel");


void InvalidInput(GameMenuInput descriptor,string label){try{new OverlayTextEditor("",descriptor).Value();}catch(ArgumentException){Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
var edit=new OverlayTextEditor("name",new("Name","Guard",MenuInputKind.Name));edit.Insert("Alpha: One");Check(edit.Value()=="Alpha: One","typing replaces initially selected input and supports names containing punctuation");
edit.Home();edit.Delete();edit.End();edit.Backspace();Check(edit.Value()=="lpha: On","input supports caret navigation, delete and backspace");
edit.SelectAll();edit.Insert("New");Check(edit.Value()=="New","select all replaces the current draft");
InvalidInput(new("Health","0",MenuInputKind.Health),"zero health rejected by custom editor");
InvalidInput(new("Health","101",MenuInputKind.Health),"health above 100 rejected by custom editor");
InvalidInput(new("X","NaN",MenuInputKind.Coordinate),"nonfinite coordinate rejected by custom editor");
InvalidInput(new("X","1000001",MenuInputKind.Coordinate),"out-of-range coordinate rejected by custom editor");
InvalidInput(new("Name","   ",MenuInputKind.Name),"blank display name rejected");
Check(new OverlayTextEditor("",new("X","-12.75",MenuInputKind.Coordinate)).Value()=="-12.75","signed decimal coordinates accepted");
var editSnapshots=new[]{Guard(71) with {X=10,Y=20,Z=30},Guard(72)};
var editCalls=new List<(string Action,uint Id,int Hp)>();var positionCalls=new List<(uint Id,NpcPosition Position)>();
using var editable=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(editSnapshots),(_,act,id,hp,c,a)=>{editCalls.Add((act,id,hp));return Task.FromResult("Updated");},(_,id,position)=>{positionCalls.Add((id,position));return Task.FromResult("Moved");});
await editable.RefreshAsync(true);await editable.ActivateAsync("roster");await editable.ActivateAsync(Key(71));
await editable.ActivateAsync($"rename:{Environment.ProcessId}:71:"+Uri.EscapeDataString("Alpha: One"));
Check(editable.Frame.Title.StartsWith("Alpha: One")&&editCalls.Count==0,"renaming updates overlay label without modifying the game actor");
await editable.ActivateAsync($"custom-health:{Environment.ProcessId}:71:63");Check(editCalls[^1]==("health",71,63),"custom health preserves selected actor ID and exact requested value");
await editable.ActivateAsync("position-menu");await editable.ActivateAsync($"coord:{Environment.ProcessId}:71:X:-123.5");
Check(positionCalls.Count==0,"editing coordinates only changes the draft");
editSnapshots[0]=editSnapshots[0] with {X=99};await editable.RefreshAsync(true);await editable.ActivateAsync("apply-position");
Check(positionCalls[^1]==(71,new NpcPosition(-123.5f,20,30)),"live position updates do not overwrite an edited coordinate draft");
await editable.ActivateAsync("use-current");await editable.ActivateAsync("apply-position");
Check(positionCalls[^1].Position.X==99,"use current explicitly refreshes the coordinate draft");
await editable.ActivateAsync("to-sam");Check(editCalls[^1].Action=="to-sam"&&editCalls[^1].Id==71,"Move to Sam forwards selected stable actor ID");
await editable.ActivateAsync("details");Check(editable.Frame.Entries.Any(e=>e.Label.StartsWith("Weapon:"))&&editable.Frame.Entries.Any(e=>e.Label.StartsWith("AI state:"))&&editable.Frame.Entries.Any(e=>e.Label.StartsWith("Target:")),"detailed NPC stats include weapon, AI state and target");
await editable.ActivateAsync(Key(72));await editable.ActivateAsync($"custom-health:{Environment.ProcessId}:71:37");
Check(editCalls[^1]==("health",71,37),"captured text input cannot accidentally edit the newly selected NPC");

namespace SCTrainer {
 internal sealed record GameMenuEntry(string Label,bool On,bool Available,string Key,string? Value=null,bool Toggle=true,bool Navigation=false,GameMenuInput? Input=null);
 internal sealed record GameMenuFrame(string Key,string Title,GameMenuEntry[] Entries);
 internal static class DirectLevelLoader {
  public static void ValidateGame(Process game)=>throw new Exception("Unexpected real game validation in test");
  public static Task<NpcSnapshot[]?> ReadNpcsAsync(Process game)=>throw new Exception("Unexpected real read in test");
  public static Task<string> NpcActionAsync(Process game,string action,uint id=0,int hp=100,NpcConditionEdit? condition=null,NpcActivityEdit? activity=null,NpcPosition? position=null,bool waitForCompletion=true)=>throw new Exception("Unexpected real action in test");
 }
}
