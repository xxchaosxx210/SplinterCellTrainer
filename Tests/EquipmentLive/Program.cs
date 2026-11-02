using System.Diagnostics;
using SCTrainer;
if(args.Length!=2||args[0]!="--one-wallmine")throw new ArgumentException("Explicit --one-wallmine and helper path required; this changes game inventory.");
string helper=Path.GetFullPath(args[1]);if(!helper.EndsWith("SCTravelHost.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(helper))throw new ArgumentException("Built helper required.");
var games=Process.GetProcessesByName("SplinterCell");if(games.Length!=1)throw new InvalidOperationException("One running game required.");using var game=games[0];
async Task<string> Request(string command){var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(game.Id.ToString());start.ArgumentList.Add(command);using var host=Process.Start(start)!;var output=host.StandardOutput.ReadToEndAsync();await host.WaitForExitAsync();string text=(await output).Trim();Console.WriteLine(text);return text;}
int Count(string snapshot){var line=snapshot.Split('\n').First(l=>l.StartsWith("8|"));return int.Parse(line.Split('|')[2]);}
int before=Count(await Request("--equipment-list"));if(before>=3)throw new InvalidOperationException("Wall Mines already full; no grant attempted.");
await GameWindowFocus.PrepareAsync(game.MainWindowHandle);
await Request("--equipment:8:0:1");
int after=Count(await Request("--equipment-list"));
Console.WriteLine($"WALL_MINE_BEFORE={before} AFTER={after}");if(after!=before+1)Environment.ExitCode=1;
