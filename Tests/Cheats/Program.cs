using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using SCTrainer;

static void Check(bool condition,string message)
{
    if(!condition)throw new Exception("FAIL: "+message);
    Console.WriteLine("PASS: "+message);
}
Check(new CheatSettings(CheatOptions.UnlimitedAmmo).Command=="--cheats:1:100","ammo does not select no-reload");
Check(new CheatSettings(CheatOptions.NoReload).Command=="--cheats:2:100","no-reload is independently selectable");
try { new CheatSettings(CheatOptions.MovementSpeed,301).Validate();throw new Exception("Invalid speed accepted"); }
catch(ArgumentOutOfRangeException){Console.WriteLine("PASS: out-of-range speed rejected");}
Check(new CheatSettings(CheatOptions.Invisibility).Command=="--cheats:32:100","invisibility does not enable health or other cheats");
Check(new CheatSettings(CheatOptions.Noclip).Command=="--cheats:64:100","noclip is independently selectable");
Check(new CheatSettings(CheatOptions.Invisibility|CheatOptions.Noclip).Command=="--cheats:96:100","invisibility and noclip combine independently");
try { new CheatSettings((CheatOptions)512).Validate();throw new Exception("Unknown option accepted"); }
catch(ArgumentOutOfRangeException){Console.WriteLine("PASS: unknown option rejected");}
Check(new CheatSettings(CheatOptions.BypassLocks).Command=="--cheats:128:100","lock bypass is independently selectable");
Check(new CheatSettings(CheatOptions.BypassLocks|CheatOptions.Invisibility).Command=="--cheats:160:100","lock bypass preserves independent visibility selection");
Check(new CheatSettings(CheatOptions.QuietLights).Command=="--cheats:256:100","quiet lights is independently selectable");
Check(new CheatSettings(CheatOptions.QuietLights|CheatOptions.BypassLocks).Command=="--cheats:384:100","quiet lights combines with lock bypass without changing other options");
if(!args.Contains("--live")){Console.WriteLine("Pass --live to test against one running mission.");return;}

var games=Process.GetProcessesByName("SplinterCell");
Check(games.Length==1,"one running game");
using var game=games[0];
using var memory=new Remote(game.Id);
var type=typeof(CheatSettings).Assembly.GetType("SCTrainer.CheatSession")!;
async Task<object> Apply(CheatOptions options,int percent=100)
{
    var task=(Task)type.GetMethod("ApplyAsync")!.Invoke(null,new object[]{Process.GetProcessById(game.Id),new CheatSettings(options,percent)})!;
    await task;
    return task.GetType().GetProperty("Result")!.GetValue(task)!;
}
async Task Off(object session)
{
    try { await (Task)type.GetMethod("DisableAsync")!.Invoke(session,null)!; }
    finally { ((IDisposable)session).Dispose(); }
}
uint Player(object session)=>(uint)type.GetField("_player",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
object? active=null;
try
{
    active=await Apply(CheatOptions.UnlimitedAmmo);
    uint player=Player(active), pawn=memory.U(player+0x454),inv=memory.U(pawn+0x744);
    uint flags=memory.U(player+0x518);
    var owned=memory.Inventory(inv);
    uint weapon=owned.First(p=>memory.IsA(p,"EWeapon",game));
    uint consumable=owned.First(p=>memory.NameOf(p,game).StartsWith("EMedKit"));
    Check(memory.U(weapon+0x3B8)==memory.U(weapon+0x3BC),"reserve ammunition refilled");
    await Off(active);active=null;

    active=await Apply(CheatOptions.NoReload);
    Check((memory.U(player+0x518)&0x10)!=0,"native no-consumption flag enabled");
    Check(memory.U(weapon+0x3C0)==memory.U(weapon+0x3C4),"magazine filled");
    Check((memory.U(player+0x518)&8)==(flags&8),"infinite-health bit preserved");
    await Off(active);active=null;
    Check((memory.U(player+0x518)&0x10)==(flags&0x10),"original no-consumption flag restored");

    active=await Apply(CheatOptions.UnlimitedGadgets);
    Check(memory.U(consumable+0x3A8)>=memory.U(consumable+0x3AC),"carried medkit replenished");
    await Off(active);active=null;

    float speed=memory.F(player+0x5C0);
    active=await Apply(CheatOptions.MovementSpeed,200);
    Check(Math.Abs(memory.F(player+0x5C0)-speed*2)<0.01,"2x speed applied");
    var updated=await Apply(CheatOptions.MovementSpeed,150);
    ((IDisposable)active).Dispose();active=updated;
    Check(Math.Abs(memory.F(player+0x5C0)-speed*1.5f)<0.01,"speed changes do not compound");
    await Off(active);active=null;
    Check(Math.Abs(memory.F(player+0x5C0)-speed)<0.01,"original speed restored");

    uint guard=memory.Objects(game).First(p=>memory.IsA(p,"EAIProfessional",game));
    float range=memory.F(guard+0x668);
    active=await Apply(CheatOptions.ReducedDetection);
    Check(Math.Abs(memory.F(guard+0x668)-range*.25f)<0.01,"guard sight range reduced to 25%");
    await Off(active);active=null;
    Check(Math.Abs(memory.F(guard+0x668)-range)<0.01,"guard sight range restored");

    active=await Apply((CheatOptions)31,200);
    Check((bool)type.GetMethod("IsCurrent")!.Invoke(active,null)!,"all five options enabled together");
    await Off(active);active=null;
    Check(Math.Abs(memory.F(player+0x5C0)-speed)<.01 && Math.Abs(memory.F(guard+0x668)-range)<.01,"combined cleanup restores speed and detection");
    Check((memory.U(player+0x518)&8)==(flags&8),"combined cleanup preserves health flag");

    active=await Apply(CheatOptions.MovementSpeed,150);
    // Deliberately do not heartbeat: verifies cleanup when a trainer disappears.
    await Task.Delay(16500);
    Check(Math.Abs(memory.F(player+0x5C0)-speed)<.01,"expired session restores original speed");
    ((IDisposable)active).Dispose();active=null;
    game.Refresh();
    Check(!game.HasExited && !game.MainWindowTitle.Contains("Critical Error"),"game remains running after all toggles");
}
finally{if(active is not null)await Off(active);}

sealed class Remote : IDisposable
{
    readonly IntPtr handle;
    readonly Dictionary<uint,string> names=new();
    uint core;
    public Remote(int pid){handle=OpenProcess(0x410,false,pid);if(handle==IntPtr.Zero)throw new Exception("Read handle unavailable");}
    public byte[] Read(uint address,int count){var b=new byte[count];if(!ReadProcessMemory(handle,(IntPtr)(long)address,b,(nuint)count,out var read)||read!=(nuint)count)throw new Exception("Read failed");return b;}
    public uint U(uint address)=>BitConverter.ToUInt32(Read(address,4));
    public float F(uint address)=>BitConverter.ToSingle(Read(address,4));
    void Core(Process game){if(core==0)foreach(ProcessModule m in game.Modules)if(m.ModuleName=="Core.dll")core=(uint)m.BaseAddress.ToInt64();}
    public string NameOf(uint p,Process game)
    {
        Core(game);uint index=U(p+0x20);
        if(names.TryGetValue(index,out var value))return value;
        uint table=U(core+0x18DD7C),entry=U(table+index*4);
        return names[index]=System.Text.Encoding.Unicode.GetString(Read(entry+12,128)).Split('\0')[0];
    }
    public bool IsA(uint p,string name,Process game)
    {
        if(p==0)return false;
        uint cls=U(p+0x24);
        for(int depth=0;cls!=0&&depth<24;depth++){if(NameOf(cls,game)==name)return true;cls=U(cls+0x2C);}
        return false;
    }
    public IEnumerable<uint> Objects(Process game)
    {
        Core(game);uint table=U(core+0x19518C),count=U(core+0x195190);
        var data=Read(table,checked((int)count*4));
        for(int i=0;i<count;i++){var p=BitConverter.ToUInt32(data,i*4);if(p!=0)yield return p;}
    }
    public uint[] Inventory(uint inv)
    {
        uint categories=U(inv+0x2A8),count=U(inv+0x2AC);
        var items=new List<uint>();
        for(uint i=0;i<count;i++){uint table=U(categories+i*12),n=U(categories+i*12+4);for(uint j=0;j<n;j++)items.Add(U(table+j*4));}
        return items.ToArray();
    }
    public void Dispose()=>CloseHandle(handle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr h,IntPtr address,[Out]byte[] bytes,nuint size,out nuint read);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
}
