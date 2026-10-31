using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SCTrainer;

internal sealed class InfiniteHealthSession : IDisposable
{
    private readonly Process _game;
    private readonly SafeProcessHandle _handle;
    private readonly uint _viewport, _player, _index, _offset, _mask;
    public bool Enabled { get; private set; }

    private InfiniteHealthSession(Process game, string reply)
    {
        _game = game;
        if (!reply.StartsWith("HEALTH:")) throw new InvalidOperationException("Unexpected health-loader response.");
        var values = reply.Substring(7).Split(':').Select(x => uint.Parse(x, NumberStyles.HexNumber)).ToArray();
        if (values.Length != 5) throw new InvalidOperationException("Invalid health-loader response.");
        (_viewport, _player, _index, _offset, _mask) = (values[0], values[1], values[2], values[3], values[4]);
        _handle = OpenProcess(0x410, false, game.Id);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        Enabled = true;
    }

    public static async Task<InfiniteHealthSession> EnableAsync(Process game)
    {
        // Open the read handle before writing, so a local handle error cannot leave an enabled feature behind.
        using var check = OpenProcess(0x410, false, game.Id);
        if (check.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var reply = await DirectLevelLoader.SetHealthAsync(game, true);
        try { return new InfiniteHealthSession(game, reply); }
        catch { await DirectLevelLoader.SetHealthAsync(game, false); throw; }
    }

    private uint U(uint address)
    {
        var buffer = new byte[4];
        if (!ReadProcessMemory(_handle, (IntPtr)(long)address, buffer, 4, out var count) || count != 4)
            throw new InvalidOperationException("Player data is unavailable.");
        return BitConverter.ToUInt32(buffer);
    }

    public bool IsCurrent()
    {
        try
        {
            return !_game.HasExited && U(_viewport + 0x34) == _player && U(_player + 4) == _index &&
                (U(_player + _offset) & _mask) != 0;
        }
        catch (InvalidOperationException) { return false; }
    }

    public async Task DisableAsync()
    {
        if (!Enabled) return;
        if (IsCurrent()) await DirectLevelLoader.SetHealthAsync(_game, false);
        Enabled = false;
    }

    public void Dispose() { _handle.Dispose(); _game.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, nuint size, out nuint read);
}
