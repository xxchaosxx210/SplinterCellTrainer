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

    public static async Task<string> RequestAsync(Process game, string map)
    {
        if (map != "--probe" && (map.Length == 0 || map.Length > 100 ||
            map.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))))
            throw new InvalidOperationException("Invalid map name.");
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

        var host = Path.Combine(AppContext.BaseDirectory, "SCTravelHost.exe");
        if (!File.Exists(host))
            throw new FileNotFoundException("Native level loader missing. Run Native\\build.cmd and rebuild the trainer.", host);
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(game.Id.ToString());
        start.ArgumentList.Add(map);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start level loader.");
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        await helper.WaitForExitAsync();
        var message = ((await output) + (await error)).Trim();
        if (helper.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(message) ? "Direct load failed." : message);
        return message;
    }
}
