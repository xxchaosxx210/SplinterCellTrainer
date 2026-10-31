# Splinter Assault Trainer

Windows trainer for the original Tom Clancy's Splinter Cell on Steam, built with C# / WPF and a 32-bit C++ game-thread helper.

**Trainer:** v0.6.2 prototype
**Supported Steam build:** 252083 (App ID 13560)

## Features

| Option | Behaviour |
| --- | --- |
| Enemy ESP | Green markers for living hostile NPCs, including through walls. Read-only overlay. |
| Infinite health | Uses the game's reflected invincibility flag. |
| Unlimited ammunition | Replenishes total ammunition for carried weapons. Normal magazine/reload behaviour remains unless No reload is enabled. |
| No reload | Fills magazines and uses the game's no-consumption flag. Shots therefore do not consume reserve ammo either. |
| Unlimited gadgets | Replenishes carried consumable gadgets and medkits. Does not grant missing items. |
| Reduced detection | Scales hostile NPC sight distances and visibility-distance tables to 25%. Hearing, cameras, existing alerts and scripted events remain active. |
| Movement speed | Select 0.5x, 1x, 1.5x, 2x or 3x for running, walking, crouching, aiming and back-to-wall movement. |
| Load level | Select an installed map section and load it from the game's main menu. |

Speed, sight distances and the no-consumption flag restore their original values when disabled. Refilled ammo and consumables remain in the inventory. Options reset when the player changes; enable them again after loading a mission.

The five additional options run on a renewable session. If the trainer stops renewing it, the native helper disables the session and restores reversible changes after approximately 15 seconds of game-window message processing. Normal trainer closure disables active options immediately. Infinite health has separate normal-close cleanup.

## Run

1. Start Splinter Cell through Steam **in windowed mode**.
2. Double-click **Launch Trainer.cmd**. It builds matching native and C# components, then starts the trainer.
3. Load a mission before enabling cheats. Focus the game to display ESP markers.

The launcher builds each run into a fresh folder under bin/Runs/v0.6.1-*. This avoids overwriting a helper DLL retained by a running game. Use Launch Trainer.cmd --build-only to print the compiled executable path without opening it.

The default game folder is:

    C:\Program Files (x86)\Steam\steamapps\common\Splinter Cell

Game-folder discovery is currently limited to this location.

## Map-loading safeguards

Use the game's own menus to return to the main menu before selecting a map in the trainer. Active-mission trainer map switches are blocked.

A captured loading crash occurred in D3DDrv.dll's Unlock routine with a null render-interface argument. Restoring StartupFullscreen=False and focusing the game before travel resolved the tested main-menu loads. The trainer restores/focuses the game before sending travel and refuses the request if focus cannot be obtained. Leave the game focused until loading finishes.

The bridge uses the game's internal viewport OPEN handler without showing the console or sending keyboard commands. It verifies the current map before travel and the requested map after loading. Queuing travel alone is not reported as success.

## Build

Requirements on this machine:

- .NET 7 Windows SDK / desktop runtime
- Visual Studio 2022 Community with the x86 MSVC C++ tools

Run:

    Native\build.cmd
    dotnet build SCTrainer.csproj

Native/build.cmd currently uses the installed Visual Studio Community path; adjust it for a different installation. Native output is copied beside the managed application. Successful launcher build output is saved in bin/launcher-build.log; failures display that log.

Both components check the native helper version and protocol (9). Engine/Core/WinDrv SHA-256 checks reject unrecognised binaries. The helper discovers gameplay-property offsets through reflection metadata. Alternative releases and mods are not generally supported by these checks.

## Validation

    dotnet run --project Tests\Projection\Projection.csproj
    dotnet run --project Tests\Cheats\Cheats.csproj

The following opt-in command temporarily changes a running mission and requires a carried weapon, a medkit and hostile guards:

    dotnet run --project Tests\Cheats\Cheats.csproj -- --live

Live tests passed for ammo refill, magazine fill, no-consumption on/off, medkit refill, 2x/1.5x speed without compounding, speed restoration, guard sight-range reduction/restoration, all five options together, preservation of the health flag, and session-expiry cleanup. All five options were left off afterward.

ESP projection checks pass. Sampling measurements with six hostiles were 0.94 ms median and 3.15 ms at the 95th percentile. The overlay targets a 16 ms update interval and spreads object discovery across frames.

These checks verify live game state, not every combat interaction or mission. Gameplay testing of all gadget types, unusual movement states and scripted damage remains useful.

## Artwork

Official Steam store header for Splinter Cell (app 13560), used as the trainer background:

https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/13560/header.jpg?t=1764775959

Artwork belongs to its respective owners.

## NPC spawning prototype (v0.6.0)

Spawn nearby guard selects the nearest living armed EAIProfessional in the current
map as a source of already-loaded pawn, controller and weapon classes. It creates
a fresh pawn rather than copying the existing NPC's runtime pointers. Normal spawn
collision checks remain enabled. Placement tries two short distances in front of
Sam, with small side offsets if the centre is obstructed.

Gameplay-spawned pawns are not automatically possessed in this game. The prototype
creates the loaded controller class, supplies an initial weapon class through an
engine-allocated reflected array, and invokes normal controller possession.
It does not copy the source guard's mission tags or patrol assignments.

Only one prototype guard may exist at a time. Remove spawned guard removes that
pawn and its controller. Changing maps also discards the prototype's old references.
Spawned actors may affect combat and saved mission state; use a test save.

Live verification passed in Tbilisi: a Georgian cop spawned with 100 health, an EAIController, reciprocal possession links and a Makarov pistol. The user confirmed normal reactions. The one-guard cap and removal passed across separate build folders. The test guard was removed afterward. Other maps and guard classes still need testing.
### Removal correction (v0.6.1)

A user reproduced a GPF with the initial immediate-removal path. That path called
DestroyActor on the pawn and then its controller, even though Pawn.Destroyed already
calls Controller.PawnDied, which destroys an AI controller. Immediate removal is no
longer used by the Remove button. Both spawning and removal now focus the game first.

Remove sets Actor.LifeSpan to 0.1 seconds and lets the normal engine tick destroy
the pawn and execute its built-in controller cleanup. The trainer waits up to five
seconds to confirm that the tracked pawn is gone before reporting removal. This
correction passed a live test: the user confirmed the guard disappeared after gameplay resumed. Both the pawn and AI controller were verified as pending deletion, and the game remained responsive. If the game is paused, expiry remains scheduled until normal ticking resumes.

### Save cleanup correction (v0.6.2)

Generic pawn expiry did not run the AI death state's group and goal cleanup.
Removal now puts the spawned controller into its normal s_Dead state and invokes
RemoveReferencesInPatrolInfo before scheduling pawn expiry. The helper checks that
the controller has left its AI group before proceeding.

Live checks passed on 1_1_2Tbilisi: quicksave before spawning, quicksave with the
guard alive, and quicksave after removal. The post-removal quickload returned to
gameplay without a GPF. These checks cover this map and guard class; other missions
and existing saves created by older removal code have not been validated.
