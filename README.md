# Splinter Assault Trainer



Windows trainer for the original Tom Clancy's Splinter Cell on Steam, built with C# / WPF and a 32-bit C++ game-thread helper.



**Trainer:** v0.14.0 prototype

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



Both components check the native helper version and protocol (14). Engine/Core/WinDrv SHA-256 checks reject unrecognised binaries. The helper discovers gameplay-property offsets through reflection metadata. Alternative releases and mods are not generally supported by these checks.



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



## NPC manager (v0.7.0)



Open NPC manager from the trainer options panel. Spawn repeatedly to create up to

eight present trainer NPCs. Each has a monotonic session ID, an individually

selectable roster entry, and a panel showing health, weapon, distance, condition,

movement, AI activity, raw pawn/controller states and the current firing target.

The list refreshes every 750 ms without switching game focus. Editing or spawning

focuses the game before making a change.



Condition distinguishes Alive, Knocked out, Dead, Incapacitated (unknown zero-health

state), Removing and Removed. Unconscious/death labels use actual engine state

names, not health alone. Walking/running uses horizontal velocity and the native

BaseMoveFlags animation enum; unrecognised moving modes say Moving. Activity

includes Idle, Searching, Alerted, Stunned, Grabbed, Carried and Climbing. Other

states remain visible by their raw names; Alerted is not assumed to mean shooting.



Rename changes only the display name in the current manager window. Set health

(1–100) and Restore health affect only a living, conscious selected NPC. They do

not revive corpses or knocked-out actors. Remove selected NPC preserves the v0.6.2

AI-group/patrol cleanup and waits for that specific actor to disappear. Freeze and forced combat controls are not implemented. Condition editing is described below.



The helper keeps up to 32 recent records, reusing the oldest absent slot when

needed. Identity checks include pointer, object-table index, name and level. Map

changes, reloads and destroyed actors invalidate old pointers before any action.

Tracking covers trainer-created actors in the current game process; it does not

rediscover actors from save files. Names are not persisted. Closing the manager

leaves spawned NPCs in the game and stops polling; reopening reconnects to the

process registry.



Validation: two simultaneous Georgian cops on 1_1_2Tbilisi had independent IDs,

weapons and state snapshots. Setting NPC 2 to 68 HP left NPC 1 at 100; targeted

removal of NPC 2 left NPC 1 present. A quicksave with one present and one removed

completed without a GPF. Removing the remaining guard, saving again and quickloading

also completed without a GPF. Knockout/death and movement classification have regression

coverage; their combat transitions still need live gameplay verification.



    dotnet run --project Tests\Npcs\Npcs.csproj



## Condition editing (v0.8.0)



Choose Knocked out, Alive (revive), or Dead for the selected trainer NPC, then

Apply condition. Knockout invokes EPawn.Knocked; unconscious revival invokes the

controller's state-specific Revive handler and waits through s_Groggy recovery;

death uses EPawn.DiedE so AI groups, weapons, collision and controller cleanup

follow the normal game path. Dead NPCs cannot be resurrected: their controller has

already been destroyed. Removed, expiring and unsupported interaction states are

rejected. Reflected parameter layouts are checked before mutating the actor.

Condition changes are confirmed from live snapshots; unpaused gameplay is needed

for recovery animations. The user confirmed the condition editor worked.



## Activity editing (v0.9.0)



Select Idle / guard, Walking, Running, Search point, or Alerted and press Apply

activity. Only living, conscious trainer NPCs with an intact original controller

in a normal default/investigation/alert state can receive commands. The helper

clears the old goal, patrol references, navigation locks and routes through normal

game functions, installs a guard default goal, and adds a high-priority movement

or quick-search goal when needed. Goal and movement enum values are resolved from

loaded metadata and parameter layouts are validated before existing goals change.



Walking, running and searching capture a destination four metres in front of Sam

using his current view direction. Normal pathfinding and collision remain active;

blocked destinations or other NPCs may prevent arrival. These are one-shot goals,

not permanent AI overrides. After arrival, the NPC guards the destination and may

still respond to mission events. Search point enters investigation; Alerted sets

alert awareness without forcing the NPC to fire at a target. The live activity

label continues to report observed behavior, independently of the last command.



Live checks on 1_1_2Tbilisi: walking moved a guard from 1.6 m to about 4.0 m from

Sam; the running test recorded 350 game units/s; search entered s_Investigate and

alerting entered s_Alert. Applying activity to NPC 6 left NPC 5 in s_Default.

The initial idle command also passed.



After all activity tests, both test NPCs were removed, a new quicksave was written,

and quickload returned to gameplay without a GPF. Regression checks also cover

activity eligibility for normal versus unconscious/dead/removed/special states.


## Map NPCs and position editing (v0.14.0)

The manager automatically discovers EAIPawn instances in the current loaded
mission, including guards, civilians, bodies and scripted characters. Each row
shows Map NPC or Trainer spawned. Present map NPCs appear above recent history;
the eight-spawn cap counts only trainer-created actors. Up to 256 current/recent
identities use pointer, object-index, name and level checks. Updating imports the
old registry so tracked spawns retain their origin. Reloaded actors receive new
identities when their previous identity becomes invalid.

Map NPCs have condition, health and activity controls where their controller and
state support the edit. Scripted special states can be read-only. Edits can disrupt
mission objectives, scripts or patrols. Map NPC removal is blocked in the UI and
native helper. Actors loaded from saves without a matching tracked spawn identity
are treated as map NPCs conservatively.

Position editing is available for living, conscious NPCs in normal AI states.
Live game-unit coordinates accompany In front of Sam (two metres), Move to Sam,
and explicit X/Y/Z controls. Use current copies the observed position into the
fields; periodic updates preserve values being edited. Quick moves adjust actor
centre height relative to Sam. Values must be finite and within +/-1,000,000 units.

Movement uses ULevel.FarMoveActor, matching Actor.SetLocation with all three optional
flags zero. Normal collision checks, volume updates and AI reactions remain active.
Occupied destinations may be rejected. Accepted moves report actual coordinates.
Existing goals are retained, so NPCs may move again after repositioning.

Live validation found 11 map NPCs in Tbilisi. EGeorgianCop8 was moved in front of
Sam and restored to (-686.203, 2471.363, 1361.650). Saving and quickloading after
restoration returned to gameplay without a GPF. Map removal was disabled in the
manager. Regression checks cover origins, removal protection, position eligibility,
signed coordinates and invalid/nonfinite destination values.

Removed trainer-spawned NPCs disappear from the manager list once the game confirms they are absent. Pending removals remain visible, and present unconscious or dead NPCs stay listed. Reopening the manager does not restore removed spawned entries.

## Sam invisibility and noclip (v0.11.0)

- **Invisibility (Sam)** applies the original Invisible cheat's player properties: Actor.bHidden and Pawn.Visibility=0. It hides Sam's model and causes guard SeePlayer handlers to ignore him. Sound, existing alerts, cameras and mission scripts are not guaranteed to ignore him. Switching off restores his original hidden bit and visibility byte, preserving adjacent fields.
- **Noclip (walls and doors)** invokes the game's built-in Ghost command through the internal viewport command handler; no console window opens. Ghost disables world/actor collision and enters PlayerFlying. Enable while Sam is standing normally, outside ladders and interactions. Movement follows the camera direction. Move clear of walls, doors and floors before switching off; Walk restores normal walking and collision.
- Both options use the existing game-thread session, automatic heartbeat expiry cleanup and trainer-close cleanup. They reset on player changes and do not enable infinite health. Native protocol 15 prevents older pinned helper code from handling the new options.

Live checks passed against the running Steam mission: invisibility set the hidden bit and visibility 0, then restored original values (visible, 128). Noclip cleared world/actor blocking flags and changed walking physics/state to flying; switching off restored the original collision bits, walking physics and s_PlayerWalking. Both options together also restored correctly. Traversing specific walls and doors and enemy reactions still require gameplay verification.

## In-game overlay menu (v0.12.0)

Keep the trainer running and focus the windowed game. Press **F6** to open the menu on the left, **Up/Down** to select an option, **Enter** to toggle it, and **F6** or **Escape** to close it. Options share the desktop trainer's live state and existing validation, including mission-only restrictions. Movement speed uses the multiplier selected in the trainer.

The menu is a transparent, click-through desktop overlay; it does not inject rendering into the game or open its console. While open, menu navigation keys are consumed so they do not also navigate the game. Other gameplay keys remain active; opening the menu does not pause gameplay. The menu closes on loss of game focus and removes its keyboard hook when the trainer closes. Keyboard handling is restricted to the foreground Splinter Cell window.

Overlay verification: clean build (zero warnings/errors), Insert open/close, Down selection, and Enter routing to the existing mission-only safeguard all passed at the live game main menu. Cheat activation from the overlay during an active mission still needs gameplay verification.

The overlay shortcut is now F6, replacing Insert for laptop keyboard compatibility. On HP action-key layouts, Fn+F6 may be required to send F6.

## Spawning through the overlay (v0.13.0)

Press **F6**, select **Spawn NPCs**, then use **Spawn guard in front of Sam**, **Manage spawned NPCs**, or **Remove all spawned NPCs**. The live roster only shows present trainer-spawned guards (maximum eight); removed guards disappear automatically. Select a guard for condition, activity, health presets (100/75/50/25/1), movement in front of Sam, and removal. The same actor-state checks and native handlers used by the desktop NPC manager apply.

**Left** or **Escape** goes back one submenu; from the main menu it closes the overlay. **F6** closes from any page. Remove all opens a confirmation page with **Cancel** selected first and only affects trainer-spawned NPCs. Menu actions wait for any current snapshot refresh, obtain fresh state, and identify the selected actor by game process and NPC ID, never by changing list position.

Deterministic overlay tests cover stable-ID selection after reordering, removed actors, map-NPC protection, bulk confirmation/cancellation, native command forwarding, spawn limits, unavailable corpse actions, back navigation and actions queued behind snapshot refresh. Live Tbilisi verification passed for spawning, roster updates, knockout, revival, and single removal with immediate roster cleanup.

Live bulk-removal verification also passed: confirmation defaulted to Cancel, confirmed removal left zero present spawned NPCs and all 11 map NPCs present, and the game remained running without a GPF. Both test guards were removed after testing.

## Responsive NPC navigation and map roster (v0.14.0)

**F6 → Map NPCs** lists NPCs present in the active map using their instance names, condition and activity. Select one for condition, activity, health presets or movement in front of Sam. Unsupported state transitions remain unavailable; map NPC removal is blocked in both the overlay and native helper. Spawned NPCs remain in their separate roster, and only present spawned NPCs count towards the eight-spawn limit.

Condition changes in the overlay return after the game accepts the request; they do not hold the menu while knockout/revival animations finish. Live polling shows the actual changing condition. Back, roster selection and submenu browsing remain available while any NPC action is pending, while overlapping mutations are blocked until the current native request returns. Each mutation captures its actor ID before awaiting so browsing to a different NPC cannot retarget it. The desktop manager retains its existing confirmation behavior.

Overlay regression checks cover separate named map and spawned rosters, map-removal protection, browsing another NPC's condition submenu during an outstanding edit, preserving the original edit target, and returning directly from the map roster to the main menu.

Live v0.14.0 verification in Tbilisi: the named map roster populated and keyboard navigation scrolled to its bottom; a map NPC editor opened with removal disabled. A temporary spawned guard was knocked out, revived through the overlay, and Back navigation was accepted after the revival request. The test guard was removed afterward; the game remained running without a GPF. No map NPC properties were changed during this verification.
