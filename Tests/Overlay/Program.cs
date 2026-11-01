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

namespace SCTrainer {
 internal sealed record GameMenuEntry(string Label,bool On,bool Available,string Key,string? Value=null,bool Toggle=true,bool Navigation=false);
 internal sealed record GameMenuFrame(string Key,string Title,GameMenuEntry[] Entries);
 internal static class DirectLevelLoader {
  public static void ValidateGame(Process game)=>throw new Exception("Unexpected real game validation in test");
  public static Task<NpcSnapshot[]?> ReadNpcsAsync(Process game)=>throw new Exception("Unexpected real read in test");
  public static Task<string> NpcActionAsync(Process game,string action,uint id=0,int hp=100,NpcConditionEdit? condition=null,NpcActivityEdit? activity=null,bool waitForCompletion=true)=>throw new Exception("Unexpected real action in test");
 }
}
