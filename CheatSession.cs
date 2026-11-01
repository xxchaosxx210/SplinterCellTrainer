using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SCTrainer;

[Flags]
public enum CheatOptions { None=0, UnlimitedAmmo=1, NoReload=2, UnlimitedGadgets=4, ReducedDetection=8, MovementSpeed=16, Invisibility=32, Noclip=64, BypassLocks=128, QuietLights=256 }

public sealed record CheatSettings(CheatOptions Options, int SpeedPercent=100)
{
    public void Validate()
    {
        if (((int)Options & ~511) != 0 || SpeedPercent < 50 || SpeedPercent > 300)
            throw new ArgumentOutOfRangeException(nameof(SpeedPercent), "Speed must be between 0.5x and 3x.");
    }
    public string Command { get { Validate(); return $"--cheats:{(int)Options}:{SpeedPercent}"; } }
}

internal sealed class CheatSession : IDisposable
{
    private readonly Process _game;
    private readonly SafeProcessHandle _handle;
    private readonly uint _viewport,_player,_index,_maskAddress;
    private DateTime _lastPing = DateTime.UtcNow;
    public CheatSettings Settings { get; }

    private CheatSession(Process game, CheatSettings settings, string reply)
    {
        _game=game; Settings=settings;
        if(!reply.StartsWith("CHEATS:"))throw new InvalidOperationException("Unexpected cheat-loader response.");
        var values=reply.Substring(7).Split(':').Select(x=>uint.Parse(x,NumberStyles.HexNumber)).ToArray();
        if(values.Length!=4||values.Any(x=>x==0))throw new InvalidOperationException("Incomplete cheat-loader response.");
        (_viewport,_player,_index,_maskAddress)=(values[0],values[1],values[2],values[3]);
        _handle=OpenProcess(0x410,false,game.Id);
        if(_handle.IsInvalid){_handle.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
    }

    public static async Task<CheatSession> ApplyAsync(Process game, CheatSettings settings)
    {
        settings.Validate();
        if(settings.Options==CheatOptions.None)throw new InvalidOperationException("No options selected.");
        using var check=OpenProcess(0x410,false,game.Id);
        if(check.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var reply=await DirectLevelLoader.SetCheatsAsync(game,settings);
        try{return new CheatSession(game,settings,reply);}
        catch{await DirectLevelLoader.SetCheatsAsync(game,new(CheatOptions.None));throw;}
    }

    private uint U(uint address)
    {
        var bytes=new byte[4];
        if(!ReadProcessMemory(_handle,(IntPtr)(long)address,bytes,4,out var count)||count!=4)
            throw new InvalidOperationException("Player data unavailable.");
        return BitConverter.ToUInt32(bytes);
    }

    public bool IsCurrent()
    {
        try{return !_game.HasExited && U(_viewport+0x34)==_player && U(_player+4)==_index &&
            U(_maskAddress)==(uint)Settings.Options;}
        catch(InvalidOperationException){return false;}
    }

    public async Task PulseAsync()
    {
        if(DateTime.UtcNow-_lastPing<TimeSpan.FromSeconds(5))return;
        await DirectLevelLoader.PingCheatsAsync(_game);
        _lastPing=DateTime.UtcNow;
    }

    public async Task DisableAsync()
    {
        if(IsCurrent())await DirectLevelLoader.SetCheatsAsync(_game,new(CheatOptions.None));
    }

    public void Dispose(){_handle.Dispose();_game.Dispose();}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process,IntPtr address,[Out]byte[] bytes,nuint size,out nuint count);
}
