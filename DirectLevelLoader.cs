using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace SCTrainer;

internal static class DirectLevelLoader
{
    private static readonly Dictionary<string, string> SupportedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Engine.dll"] = "24F23217896114C8B9F7D5B6C1C16F9002C5977BF7102E45B10E949247FA9655",
        ["Core.dll"] = "F190F53428AE40E7C0FC34809F8D22B341AF8758F7E05A5ABA35BAAF69799FE3",
        ["WinDrv.dll"] = "FCADD480793A43DD2F836839E3C5601504476972AED2340FEE667E38C2D9E688"
    };

    private static readonly SemaphoreSlim Requests = new(1, 1);

    public static async Task<string> RequestAsync(Process game, string map)
    {
        if (map != "--probe" && (map.Length == 0 || map.Length > 100 ||
            map.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))))
            throw new InvalidOperationException("Invalid map name.");
        if (!await Requests.WaitAsync(0))
            throw new InvalidOperationException("A loader request is already running.");
        try
        {
            ValidateGame(game);
            var version = await RunHelperAsync("--version");
            if (version != "SCTravelHost:0.6.2:protocol9")
                throw new InvalidOperationException("Loader version mismatch. Close the trainer and use Launch Trainer.cmd.");
            if (map == "--probe") return await RunHelperAsync(game.Id.ToString(), map);
            var state = await RunHelperAsync(game.Id.ToString(), "--state");
            if (!state.Equals("STATE:menu", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Return to the game's main menu before loading a map. No travel request was sent.");
            await GameWindowFocus.PrepareAsync(game.MainWindowHandle);
            await RunHelperAsync(game.Id.ToString(), map);
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(45))
            {
                await Task.Delay(1000);
                if (game.HasExited) throw new InvalidOperationException("The game exited during loading.");
                var title = game.MainWindowTitle;
                if (title.Contains("Critical Error", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The game reported a critical error during loading.");
                // Avoid poking the game while its loading window is active.
                game.Refresh();
                if (game.MainWindowTitle.Equals("Loading", StringComparison.OrdinalIgnoreCase)) continue;
                state = await RunHelperAsync(game.Id.ToString(), "--state");
                if (state.Equals("STATE:" + map, StringComparison.OrdinalIgnoreCase))
                    return "Loaded " + map + ".";
            }
            throw new InvalidOperationException("Loading was not confirmed. Focus the game to let it resume; do not send another request while it is loading.");
        }
        finally { Requests.Release(); }
    }

    internal static async Task<string> SetHealthAsync(Process game, bool enabled)
    {
        if (!await Requests.WaitAsync(0)) throw new InvalidOperationException("Wait for the current loader request to finish.");
        try
        {
            ValidateGame(game);
            if (await RunHelperAsync("--version") != "SCTravelHost:0.6.2:protocol9")
                throw new InvalidOperationException("Loader version mismatch. Run Launch Trainer.cmd.");
            return await RunHelperAsync(game.Id.ToString(), enabled ? "--health-on" : "--health-off");
        }
        finally { Requests.Release(); }
    }

    internal static Task<string> SetCheatsAsync(Process game, CheatSettings settings) => CheatCommandAsync(game,settings.Command);
    internal static Task<string> PingCheatsAsync(Process game) => CheatCommandAsync(game,"--cheats-ping");

    private static async Task<string> CheatCommandAsync(Process game,string command)
    {
        if(!await Requests.WaitAsync(0))throw new InvalidOperationException("Wait for the current trainer request to finish.");
        try
        {
            ValidateGame(game);
            if(await RunHelperAsync("--version")!="SCTravelHost:0.6.2:protocol9")
                throw new InvalidOperationException("Loader version mismatch. Run Launch Trainer.cmd.");
            return await RunHelperAsync(game.Id.ToString(),command);
        }
        finally{Requests.Release();}
    }

    internal static async Task<string> SpawnNpcAsync(Process game,bool remove)
    {
        if(!await Requests.WaitAsync(0))throw new InvalidOperationException("Wait for the current trainer request to finish.");
        try
        {
            ValidateGame(game);
            if(await RunHelperAsync("--version")!="SCTravelHost:0.6.2:protocol9")
                throw new InvalidOperationException("Loader version mismatch. Run Launch Trainer.cmd.");
            await GameWindowFocus.PrepareAsync(game.MainWindowHandle);
            var reply=await RunHelperAsync(game.Id.ToString(),remove?"--npc-remove":"--npc-spawn");
            if(!remove)return reply;
            for(int attempt=0;attempt<25;++attempt)
            {
                await Task.Delay(200);
                game.Refresh();
                if(game.HasExited || game.MainWindowTitle.Contains("Critical Error",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The game stopped during removal.");
                if(await RunHelperAsync(game.Id.ToString(),"--npc-status")=="NPC:none")return "Spawned guard removed.";
            }
            throw new InvalidOperationException("Removal was scheduled but not confirmed. Leave the game focused and unpaused.");
        }
        finally{Requests.Release();}
    }

    private static async Task<string> RunHelperAsync(params string[] arguments)
    {
        var host = Path.Combine(AppContext.BaseDirectory, "SCTravelHost.exe");
        if (!File.Exists(host)) throw new FileNotFoundException("Native loader missing. Run Launch Trainer.cmd.", host);
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start level loader.");
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        await helper.WaitForExitAsync();
        var message = ((await output) + (await error)).Trim();
        if (helper.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(message) ? "Loader request failed." : message);
        return message;
    }

    internal static void ValidateGame(Process game)
    {
        var checkedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessModule module in game.Modules)
        {
            if (!SupportedFiles.TryGetValue(module.ModuleName, out var expected)) continue;
            using var stream = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(stream)) != expected)
                throw new InvalidOperationException("Unsupported game build: " + module.ModuleName + ". Direct loading was not attempted.");
            checkedModules.Add(module.ModuleName);
        }
        if (checkedModules.Count != SupportedFiles.Count)
            throw new InvalidOperationException("Game is still starting or required modules are missing. Wait for the main menu.");

    }
}
