using System.Reflection;

namespace SCTrainer;

public static class VersionInfo
{
    public static string TrainerVersion => typeof(VersionInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Unknown";
    public static string TrainerLabel => $"Splinter Assault Trainer v{TrainerVersion}";
    public const string SupportedSteamBuild = "252083";
    public static string GameLabel => $"Supported game: Steam build {SupportedSteamBuild} • App ID 13560";
}
