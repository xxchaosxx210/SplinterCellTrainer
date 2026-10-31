using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SCTrainer;

internal sealed record EnemyMarker(Vector3 Position, float Height, string Name);
internal sealed record EnemySnapshot(Vector3 Camera, int Pitch, int Yaw, int Roll, float Fov, EnemyMarker[] Enemies);

internal sealed class EnemyMemoryReader : IDisposable
{
    private readonly Process _game;
    private readonly SafeProcessHandle _handle;
    private readonly uint _core;
    private readonly uint _viewportVtable;
    private uint _viewport;
    private readonly Dictionary<uint, string> _names = new();
    private readonly Dictionary<uint, string[]> _classes = new();
    private uint[] _actors = Array.Empty<uint>();
    private DateTime _nextScan;
    private uint _lastLevel;
    private byte[]? _scanEntries;
    private int _scanIndex;
    private readonly List<uint> _scanActors = new();
    public IntPtr GameWindow { get; }
    public int GameId => _game.Id;

    public EnemyMemoryReader(Process game)
    {
        _game = game;
        DirectLevelLoader.ValidateGame(game);
        uint core = 0, win = 0;
        foreach (ProcessModule module in game.Modules)
        {
            if (module.ModuleName.Equals("Core.dll", StringComparison.OrdinalIgnoreCase)) core = (uint)module.BaseAddress.ToInt64();
            if (module.ModuleName.Equals("WinDrv.dll", StringComparison.OrdinalIgnoreCase)) win = (uint)module.BaseAddress.ToInt64();
        }
        if (core == 0 || win == 0) throw new InvalidOperationException("Game modules not ready.");
        _core = core;
        // These RVAs and reflected field offsets are restricted to the validated Steam build.
        _viewportVtable = win + 0x12240;
        GameWindow = game.MainWindowHandle;
        if (GameWindow == IntPtr.Zero) throw new InvalidOperationException("Game window not ready.");
        _handle = OpenProcess(0x410, false, game.Id);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    }

    private byte[] Read(uint address, int size)
    {
        if (address < 0x10000 || size < 0 || size > 8_000_000) throw new InvalidOperationException("Invalid game data.");
        var bytes = new byte[size];
        if (!ReadProcessMemory(_handle, (IntPtr)(long)address, bytes, (nuint)size, out var got) || got != (nuint)size)
            throw new InvalidOperationException("Game data changed. Waiting for the next frame.");
        return bytes;
    }

    private uint U(uint address) => BitConverter.ToUInt32(Read(address, 4));
    private float F(uint address) => BitConverter.ToSingle(Read(address, 4));
    private Vector3 V(uint address)
    {
        var bytes = Read(address, 12);
        var vector = new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new InvalidOperationException("Camera or actor position is not ready.");
        return vector;
    }

    private string Name(uint index)
    {
        if (_names.TryGetValue(index, out var name)) return name;
        uint count = U(_core + 0x18DD80);
        if (index >= count || count > 1_000_000) return "";
        uint entry = U(U(_core + 0x18DD7C) + index * 4);
        var text = System.Text.Encoding.Unicode.GetString(Read(entry + 12, 128)).Split('\0')[0];
        _names[index] = text;
        return text;
    }

    private string[] ClassChain(uint cls)
    {
        if (_classes.TryGetValue(cls, out var chain)) return chain;
        var list = new List<string>();
        uint current = cls;
        for (int i = 0; i < 24 && current != 0; i++)
        {
            list.Add(Name(U(current + 0x20)));
            current = U(current + 0x2C);
        }
        chain = list.ToArray();
        _classes[cls] = chain;
        return chain;
    }

    private void Scan()
    {
        if (_scanEntries is null)
        {
            uint table = U(_core + 0x19518C), count = U(_core + 0x195190);
            if (count > 250_000) throw new InvalidOperationException("Unsupported actor table.");
            _scanEntries = Read(table, checked((int)count * 4));
            _scanIndex = 0;
            _scanActors.Clear();
            _classes.Clear();
        }
        // Bound discovery work so a complete object scan cannot stall one camera frame.
        int end = Math.Min(_scanEntries.Length, _scanIndex + 256 * 4);
        for (; _scanIndex < end; _scanIndex += 4)
        {
            uint obj = BitConverter.ToUInt32(_scanEntries, _scanIndex);
            if (obj == 0) continue;
            try
            {
                var header = Read(obj, 40);
                if (BitConverter.ToUInt32(header, 0) == _viewportVtable) _viewport = obj;
                var chain = ClassChain(BitConverter.ToUInt32(header, 0x24));
                if (chain.Contains("EAIPawn") && !chain.Contains("EAINonHostile")) _scanActors.Add(obj);
            }
            catch (InvalidOperationException) { }
        }
        if (_scanIndex == _scanEntries.Length)
        {
            _actors = _scanActors.ToArray();
            _scanEntries = null;
            _nextScan = DateTime.UtcNow.AddSeconds(2);
        }
    }
    public EnemySnapshot Snapshot()
    {
        if (_game.HasExited) throw new InvalidOperationException("Game closed.");
        if (_scanEntries is not null || _viewport == 0 || DateTime.UtcNow >= _nextScan) Scan();
        if (_viewport == 0) throw new InvalidOperationException("Finding game actors…");
        uint player = U(_viewport + 0x34);
        if (!ClassChain(U(player + 0x24)).Contains("EPlayerController"))
            throw new InvalidOperationException("Load a mission to show enemy markers.");
        uint level = U(player + 0x6C);
        if (level != _lastLevel)
        {
            _lastLevel = level;
            _actors = Array.Empty<uint>();
            _scanEntries = null;
            _scanActors.Clear();
            _classes.Clear();
            _nextScan = DateTime.MinValue;
            // Never reuse an actor list from the previous map.
            return new EnemySnapshot(Vector3.Zero, 0, 0, 0, 75, Array.Empty<EnemyMarker>());
        }
        uint camera = U(player + 0x608);
        if (!ClassChain(U(camera + 0x24)).Contains("EPlayerCam"))
            throw new InvalidOperationException("Waiting for the gameplay camera.");
        var markers = new List<EnemyMarker>();
        foreach (uint actor in _actors)
        {
            try
            {
                var data = Read(actor, 0xA54);
                if (BitConverter.ToUInt32(data, 0x6C) != level || !ClassChain(BitConverter.ToUInt32(data, 0x24)).Contains("EAIPawn")) continue;
                if ((BitConverter.ToUInt32(data, 0x2C) & 0x10) != 0 || (BitConverter.ToUInt32(data, 0xA50) & 1) == 0) continue;
                if (BitConverter.ToInt32(data, 0x330) <= 0) continue;
                float height = BitConverter.ToSingle(data, 0x1B0);
                if (!float.IsFinite(height) || height < 5 || height > 200) height = 40;
                var location = new Vector3(BitConverter.ToSingle(data, 0xD4), BitConverter.ToSingle(data, 0xD8), BitConverter.ToSingle(data, 0xDC));
                if (!float.IsFinite(location.X) || !float.IsFinite(location.Y) || !float.IsFinite(location.Z)) continue;
                markers.Add(new EnemyMarker(location, height, Name(BitConverter.ToUInt32(data, 0x20))));
            }
            catch (InvalidOperationException) { }
        }
        // PlayerController caches the rendered camera view.
        var position = V(player + 0xD4);
        var rotation = Read(player + 0xE0, 12);
        float fov = F(player + 0x2B0);
        if (!float.IsFinite(fov) || fov < 10 || fov > 170) throw new InvalidOperationException("Camera field of view unavailable.");
        return new EnemySnapshot(position, BitConverter.ToInt32(rotation, 0), BitConverter.ToInt32(rotation, 4),
            BitConverter.ToInt32(rotation, 8), fov, markers.ToArray());
    }

    public void Dispose() { _handle.Dispose(); _game.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, nuint size, out nuint read);
}
