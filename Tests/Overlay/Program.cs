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
Check(menu.Frame.Title=="BROWSE NPCs"&&!menu.Frame.Entries.Any(e=>e.Key==Key(9)),"confirmed removal drops the row and returns to roster");
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
menu.OpenManager();snapshots.Clear();for(uint i=1;i<=8;++i)snapshots.Add(Guard(i));await menu.RefreshAsync(true);
Check(!menu.Frame.Entries.First(e=>e.Key=="spawn").Available,"eight-spawn limit disables overlay spawning");
await Reject(()=>menu.ActivateAsync("spawn"),"spawn limit also enforced before native action");
await menu.ActivateAsync("roster");await menu.ActivateAsync(Key(1));
snapshots[0]=snapshots[0] with {PawnState="s_Dying",Controller=false,Health=0};await menu.RefreshAsync(true);
await menu.ActivateAsync("position-menu");
Check(menu.Frame.Entries.First(e=>e.Key=="front").Available==false,"dead NPC health and movement edits disabled");
Check(menu.Back()&&menu.Back()&&menu.Back()&&!menu.Back(),"back navigation returns editor to roster to spawn to main");


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
Check(maps.Frame.Title=="BROWSE NPCs"&&maps.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==2&&maps.Frame.Entries.First(e=>e.Key==Key(44)).Label.StartsWith("MapGuard44"),"map roster shows named map NPCs separately from spawned guards");
await maps.ActivateAsync(Key(44));
Check(!maps.Frame.Entries.First(e=>e.Key=="remove-menu").Available,"map NPC removal stays disabled in editor");
await Reject(()=>maps.ActivateAsync("remove"),"map NPC remove action is rejected before native code");
var updating=maps.ActivateAsync("health:50");await actionStarted.Task;
Check(maps.Busy&&maps.Frame.Entries.First(e=>e.Key=="back").Available,"back remains available during a pending NPC action");
Check(maps.Back()&&maps.Frame.Title=="BROWSE NPCs","pending update does not prevent returning to the map roster");
await maps.ActivateAsync(Key(45));await maps.ActivateAsync("condition");
Check(maps.Frame.Title.Contains("CONDITION"),"condition submenu can be browsed during a pending action");
Check(!maps.Frame.Entries.First(e=>e.Key=="condition:2").Available,"mutating actions remain blocked while a native request is pending");
pendingAction.SetResult("Updated");await updating;
Check(editedId==44&&!maps.Busy,"pending update keeps its original map NPC target while browsing");
Check(maps.Back()&&maps.Back()&&maps.Back()&&!maps.Back(),"map roster opened from main returns directly to main");


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

// Global edits operate on a confirmed, stable set of map NPC IDs.
var globalSnapshots=new List<NpcSnapshot>{Guard(101,false),Guard(102,false),Guard(103) with {ProtectedGlobal=true},Guard(104,false) with {PawnState="s_Unconscious",AiState="s_Unconscious"},Guard(105,false) with {Health=0,PawnState="s_Dead",AiState="s_Dead"}};
var globalCalls=new List<(string Action,uint Id,NpcConditionEdit? Condition,NpcActivityEdit? Activity)>();
bool failGlobal=false;
using var global=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(globalSnapshots.ToArray()),(_,act,id,hp,c,a)=>{
 if(failGlobal&&id==101)throw new InvalidOperationException("Native state refusal");
 globalCalls.Add((act,id,c,a));return Task.FromResult("Requested");
});
await global.RefreshAsync(true);global.OpenMapRoster();
Check(global.Frame.Entries.Any(e=>e.Key=="filter-menu"),"browse roster contains a dedicated filter without global actions");
await global.ActivateAsync("global-condition");
Check(global.Frame.Entries.First(e=>e.Key=="global-condition:1").Value=="1 eligible","global revival only counts knocked-out map NPCs");
Check(global.Frame.Entries.First(e=>e.Key=="global-condition:2").Value=="2 eligible","global knockout excludes protected and incapacitated NPCs");
await Reject(()=>global.ActivateAsync("global-condition:99"),"invalid global condition rejected");
await Reject(()=>global.ActivateAsync("global-confirm:123"),"global action requires a valid confirmation");
await global.ActivateAsync("global-condition:2");
Check(global.Frame.Entries[0].Key=="global-cancel"&&globalCalls.Count==0,"global confirmation defaults to cancel without mutating NPCs");
string staleConfirm=global.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
await global.ActivateAsync("global-cancel");
Check(globalCalls.Count==0&&global.Frame.Title.Contains("CONDITION"),"cancel returns to global condition menu without changes");
await global.ActivateAsync("global-condition:2");
await Reject(()=>global.ActivateAsync(staleConfirm),"old confirmation cannot approve a newer global action");
string confirm=global.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
globalSnapshots.RemoveAll(n=>n.Id==102);globalSnapshots.Add(Guard(106,false));
string summary=await global.ActivateAsync(confirm);
Check(globalCalls.Count==1&&globalCalls[0]==("condition",101,NpcConditionEdit.KnockedOut,null),"confirmed batch only edits surviving captured map IDs");
Check(summary.Contains("1 requested, 1 skipped, 0 failed"),"batch summary reports disappeared NPCs as skipped");
Check(!global.Busy&&global.Frame.Title.Contains("CONDITION"),"batch completion clears busy state and returns to global submenu");
await global.ActivateAsync("global-condition:1");confirm=global.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
await global.ActivateAsync(confirm);
Check(globalCalls[^1]==("condition",104,NpcConditionEdit.Alive,null),"global revival dispatches only to knocked-out map NPC");
Check(global.Back()&&global.Frame.Title=="GLOBAL CONTROLS","Back from global submenu returns to map roster");
await global.ActivateAsync("global-activity");
await global.ActivateAsync("global-activity:3");confirm=global.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
failGlobal=true;summary=await global.ActivateAsync(confirm);failGlobal=false;
Check(globalCalls[^1]==("activity",106,null,NpcActivityEdit.Running)&&summary.Contains("1 requested, 0 skipped, 1 failed"),"global activity forwards running and continues after an individual refusal");

var batchStarted=new TaskCompletionSource<bool>();var batchRelease=new TaskCompletionSource<string>();var batchIds=new List<uint>();
var busySnapshots=new List<NpcSnapshot>{Guard(201,false),Guard(202,false),Guard(203) with {ProtectedGlobal=true}};
using var bulkBusy=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(busySnapshots.ToArray()),(_,act,id,hp,c,a)=>{batchIds.Add(id);if(id==201){batchStarted.TrySetResult(true);return batchRelease.Task;}return Task.FromResult("Requested");});
await bulkBusy.RefreshAsync(true);bulkBusy.OpenMapRoster();await bulkBusy.ActivateAsync("global-activity");await bulkBusy.ActivateAsync("global-activity:2");
confirm=bulkBusy.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
var bulkTask=bulkBusy.ActivateAsync(confirm);await batchStarted.Task;
Check(bulkBusy.Busy&&bulkBusy.Back()&&bulkBusy.Frame.Title.Contains("ACTIVITY")&&bulkBusy.Back()&&bulkBusy.Frame.Title=="GLOBAL CONTROLS","batch keeps Back navigation available");
await bulkBusy.ActivateAsync("global-condition");
Check(!bulkBusy.Frame.Entries.First(e=>e.Key=="global-condition:2").Available,"new batch mutations remain disabled while an action is pending");
await bulkBusy.ActivateAsync("global-condition:2");
busySnapshots[1]=busySnapshots[1] with {Health=0,PawnState="s_Dead",AiState="s_Dead"};
batchRelease.SetResult("Requested");summary=await bulkTask;
Check(batchIds.SequenceEqual(new uint[]{201})&&summary.Contains("1 requested, 1 skipped, 0 failed"),"state is revalidated between batch requests and dead NPCs are skipped");
Check(bulkBusy.Frame.Title.Contains("CONDITION")&&!bulkBusy.Busy,"batch completion preserves a newly browsed page");

var partialCalls=new List<uint>();bool loseSnapshot=false;
using var partialBatch=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>loseSnapshot?throw new InvalidOperationException("Game disconnected"):Task.FromResult<NpcSnapshot[]?>(new[]{Guard(301,false),Guard(302,false)}),(_,act,id,hp,c,a)=>{partialCalls.Add(id);loseSnapshot=true;return Task.FromResult("Requested");});
await partialBatch.RefreshAsync(true);partialBatch.OpenMapRoster();await partialBatch.ActivateAsync("global-condition");await partialBatch.ActivateAsync("global-condition:3");
confirm=partialBatch.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;summary=await partialBatch.ActivateAsync(confirm);
Check(partialCalls.SequenceEqual(new uint[]{301})&&summary.Contains("1 requested")&&summary.Contains("Stopped:")&&!partialBatch.Busy,"lost snapshot aborts the batch with a partial result and clears busy state");

var included=new List<NpcSnapshot>{Guard(401,false),Guard(402),Guard(403,false) with {ProtectedGlobal=true}};var includedIds=new List<uint>();
using var allOrigins=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(included.ToArray()),(_,act,id,hp,c,a)=>{includedIds.Add(id);return Task.FromResult("Requested");});
await allOrigins.RefreshAsync(true);allOrigins.OpenMapRoster();await allOrigins.ActivateAsync("global-condition");await allOrigins.ActivateAsync("global-condition:2");
confirm=allOrigins.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;await allOrigins.ActivateAsync(confirm);
Check(includedIds.SequenceEqual(new uint[]{401,402}),"global conditions include map and spawned NPCs while excluding protected NPCs");
allOrigins.OpenSpawns();await allOrigins.ActivateAsync("roster");await allOrigins.ActivateAsync(Key(402));await allOrigins.ActivateAsync("global-exclude");
await allOrigins.ActivateAsync("global-activity");await allOrigins.ActivateAsync("global-activity:2");confirm=allOrigins.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
includedIds.Clear();await allOrigins.ActivateAsync(confirm);
Check(includedIds.SequenceEqual(new uint[]{401}),"manual exclusion applies to global activity for spawned NPCs");
await allOrigins.ActivateAsync(Key(402));await allOrigins.ActivateAsync("global-exclude");await allOrigins.ActivateAsync("global-activity");await allOrigins.ActivateAsync("global-activity:3");confirm=allOrigins.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;
includedIds.Clear();included[0]=included[0] with {ProtectedGlobal=true};await allOrigins.ActivateAsync(confirm);
Check(includedIds.SequenceEqual(new uint[]{402}),"global activity includes spawned NPCs and rechecks protection before dispatch");

var recoverySnapshots=new[]{Guard(501,false) with {Controller=false,AiState="None"},Guard(502,false) with {AiState="s_Groggy"}};
using var recovery=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(recoverySnapshots),(_,act,id,hp,c,a)=>Task.FromResult("Requested"));
await recovery.RefreshAsync(true);recovery.OpenMapRoster();await recovery.ActivateAsync("global-condition");
Check(!recovery.Frame.Entries.First(e=>e.Key=="global-condition:2").Available,"missing controllers and unfinished revival are excluded temporarily");
recoverySnapshots[0]=recoverySnapshots[0] with {Controller=true,AiState="s_Alert"};recoverySnapshots[1]=recoverySnapshots[1] with {AiState="s_Investigate"};
await recovery.RefreshAsync(true);
Check(recovery.Frame.Entries.First(e=>e.Key=="global-condition:2").Value=="2 eligible","refreshed controllers and completed revival restore global knockout counts without new IDs");

var layoutSnapshots=new List<NpcSnapshot>{Guard(601,false),Guard(602),Guard(603,false) with {ProtectedGlobal=true}};
var layoutCalls=new List<uint>();
using var layout=new NpcOverlayMenu(()=>Process.GetCurrentProcess(),_=>Task.FromResult<NpcSnapshot[]?>(layoutSnapshots.ToArray()),(_,act,id,hp,c,a)=>{layoutCalls.Add(id);if(act=="remove")layoutSnapshots.RemoveAll(n=>n.Id==id);return Task.FromResult("Requested");});
await layout.RefreshAsync(true);layout.OpenManager();
Check(layout.Frame.Title=="NPC MANAGER"&&layout.Frame.Entries.Select(e=>e.Key).SequenceEqual(new[]{"spawn","browse","global-menu","cleanup","back"}),"manager root only contains the four main sections and Back");
await layout.ActivateAsync("browse");
Check(layout.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==3&&layout.Frame.Entries.All(e=>!e.Label.Contains('\n')),"All filter shows compact single-line rows for both origins");
Check(!layout.Frame.Entries.Any(e=>e.Key=="global-condition"||e.Key=="remove-all"),"browse list contains no global or cleanup actions");
Check(layout.Frame.Entries.First(e=>e.Key==Key(603)).Value=="Protected","protected NPCs are marked in the compact roster");
await layout.ActivateAsync("filter-menu");await layout.ActivateAsync("filter:1");
Check(layout.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==2&&!layout.Frame.Entries.Any(e=>e.Key==Key(602)),"Map filter only shows map NPCs");
await layout.ActivateAsync("filter-menu");await layout.ActivateAsync("filter:2");
Check(layout.Frame.Entries.Count(e=>e.Key.StartsWith("npc:"))==1&&layout.Frame.Entries.Any(e=>e.Key==Key(602)),"Spawned filter only shows trainer spawns");
Check(layout.Back()&&layout.Frame.Title=="NPC MANAGER","browse returns to manager");
await layout.ActivateAsync("global-menu");
Check(layout.Frame.Entries.First(e=>e.Key=="scope-menu").Value=="Map + Spawned","browse filter does not change the default global scope");
await layout.ActivateAsync("scope-menu");await layout.ActivateAsync("scope:1");await layout.ActivateAsync("global-condition");
Check(layout.Frame.Entries.First(e=>e.Key=="global-condition:2").Value=="1 eligible","Map global scope respects protected exclusions");
await layout.ActivateAsync("global-condition:2");confirm=layout.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;await layout.ActivateAsync(confirm);
Check(layoutCalls.SequenceEqual(new uint[]{601}),"Map scope only dispatches to eligible map NPCs");
layout.Back();await layout.ActivateAsync("scope-menu");await layout.ActivateAsync("scope:2");await layout.ActivateAsync("global-activity");await layout.ActivateAsync("global-activity:2");
confirm=layout.Frame.Entries.First(e=>e.Key.StartsWith("global-confirm:")).Key;layoutCalls.Clear();await layout.ActivateAsync(confirm);
Check(layoutCalls.SequenceEqual(new uint[]{602}),"Spawned global scope dispatches activity only to spawns");
layout.OpenManager();await layout.ActivateAsync("browse");await layout.ActivateAsync(Key(602));
Check(!layout.Frame.Entries.Any(e=>e.Key=="front"||e.Key.StartsWith("rename:")),"editor keeps position shortcuts and rename off its main page");
await layout.ActivateAsync("details");Check(layout.Frame.Entries.Any(e=>e.Key.StartsWith("rename:")),"Details contains Rename and stats together");layout.Back();
await layout.ActivateAsync("position-menu");Check(layout.Frame.Entries.Any(e=>e.Key=="front")&&layout.Frame.Entries.Any(e=>e.Key=="to-sam"),"Position groups both movement shortcuts");layout.Back();
int layoutBefore=layoutCalls.Count;await layout.ActivateAsync("remove-menu");
Check(layout.Frame.Entries[0].Key=="remove-cancel"&&layoutCalls.Count==layoutBefore,"individual removal opens a Cancel-first confirmation without acting");
await layout.ActivateAsync("remove-cancel");Check(layoutCalls.Count==layoutBefore,"cancelling individual removal preserves the NPC");
await layout.ActivateAsync("remove-menu");await layout.ActivateAsync($"remove-confirm:{Environment.ProcessId}:602");
Check(layoutCalls[^1]==602&&!layout.Frame.Entries.Any(e=>e.Key==Key(602)),"confirmed individual removal preserves actor identity and drops the roster row");
layout.OpenManager();await layout.ActivateAsync("cleanup");await layout.ActivateAsync("remove-all");await layout.ActivateAsync("cancel");
Check(layout.Frame.Title=="NPC CLEANUP"&&layout.Back()&&layout.Frame.Title=="NPC MANAGER"&&!layout.Back(),"cleanup confirmation returns to cleanup then manager then main menu");

namespace SCTrainer {
 internal sealed record GameMenuEntry(string Label,bool On,bool Available,string Key,string? Value=null,bool Toggle=true,bool Navigation=false,GameMenuInput? Input=null);
 internal sealed record GameMenuFrame(string Key,string Title,GameMenuEntry[] Entries);
 internal static class DirectLevelLoader {
  public static void ValidateGame(Process game)=>throw new Exception("Unexpected real game validation in test");
  public static Task<NpcSnapshot[]?> ReadNpcsAsync(Process game)=>throw new Exception("Unexpected real read in test");
  public static Task<string> NpcActionAsync(Process game,string action,uint id=0,int hp=100,NpcConditionEdit? condition=null,NpcActivityEdit? activity=null,NpcPosition? position=null,bool waitForCompletion=true)=>throw new Exception("Unexpected real action in test");
 }
}
