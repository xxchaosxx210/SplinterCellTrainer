# Splinter Assault Trainer

C# / WPF GUI with a 32-bit C++ helper for experimental direct map travel.
Targets the .NET 7 SDK currently installed on this laptop.

## Run

Double-click Launch Trainer.cmd. Start Splinter Cell through Steam in windowed mode and wait for the main menu.

1. Click Test connection. This locates the engine and active viewport without changing maps.
2. Select a map and click Load level. Confirm only after saving current progress.
3. A queued request is not proof that a map loaded successfully. Check the game.

Verified on this Steam installation: connection probe succeeded and 0_0_2_Training loaded from the main menu without a console or restart. The training level appeared and the game stayed responsive in the same process. Other map sections and mission-to-mission transitions still need gameplay testing.
Direct map loads can skip campaign setup and affect mission scripts or inventory.
Cheat toggles remain disabled placeholders.

## Implementation

Native/TravelBridge.cpp builds a 32-bit helper executable and a Windows hook DLL.
A temporary WH_CALLWNDPROC hook runs on the game's window thread. It resolves the
exported UObject array, UGameEngine and UWindowsViewport vtables, GetWindow and
SetClientTravel. It checks that the viewport belongs to the target game window
and queues absolute map travel using the engine's own function. No console,
keyboard commands, permanent game file patches, or game restart are used by this path.
The hook is removed after the request.

The C# caller verifies SHA-256 hashes of the running Engine.dll, Core.dll and
WinDrv.dll against the inspected Steam installation before using the helper.
Different game builds or mods require investigation before support is added.
The helper must run with the same Windows permissions as the game.

When no game is running, the previous executable-and-map launcher remains available.
An observed direct-executable startup GPF is under investigation; Steam startup
should be used for the first live tests.

## Build

With Visual Studio C++ x86 tools and .NET installed:

    Native\build.cmd
    dotnet build SCTrainer.csproj

Native binaries copy beside the trainer automatically. Launch Trainer.cmd uses dotnet run.
For future development, migrate to a supported .NET SDK when installed.

## Game location

C:\Program Files (x86)\Steam\steamapps\common\Splinter Cell
Executable: system\splintercell.exe
Maps: maps\*.scl
The existing StartupFullscreen=False configuration is preserved.

## Artwork

Official Steam store header for Tom Clancy's Splinter Cell (app 13560).
https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/13560/header.jpg?t=1764775959
Artwork belongs to its respective owners.
