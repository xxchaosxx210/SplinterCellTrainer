# Splinter Assault Trainer



Windows trainer for the original Tom Clancy's Splinter Cell on Steam, built with C# / WPF and a 32-bit C++ game-thread helper.



**Trainer:** v0.23.1 prototype

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

(1â€“100) and Restore health affect only a living, conscious selected NPC. They do

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


## Map NPCs and position editing (v0.19.0)

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

**F6 â†’ Map NPCs** lists NPCs present in the active map using their instance names, condition and activity. Select one for condition, activity, health presets or movement in front of Sam. Unsupported state transitions remain unavailable; map NPC removal is blocked in both the overlay and native helper. Spawned NPCs remain in their separate roster, and only present spawned NPCs count towards the eight-spawn limit.

Condition changes in the overlay return after the game accepts the request; they do not hold the menu while knockout/revival animations finish. Live polling shows the actual changing condition. Back, roster selection and submenu browsing remain available while any NPC action is pending, while overlapping mutations are blocked until the current native request returns. Each mutation captures its actor ID before awaiting so browsing to a different NPC cannot retarget it. The desktop manager retains its existing confirmation behavior.

Overlay regression checks cover separate named map and spawned rosters, map-removal protection, browsing another NPC's condition submenu during an outstanding edit, preserving the original edit target, and returning directly from the map roster to the main menu.

Live v0.19.0 verification in Tbilisi: the named map roster populated and keyboard navigation scrolled to its bottom; a map NPC editor opened with removal disabled. A temporary spawned guard was knocked out, revived through the overlay, and Back navigation was accepted after the revival request. The test guard was removed afterward; the game remained running without a GPF. No map NPC properties were changed during this verification.

## Overlay level loading (v0.15.0)

**F6 â†’ Load level â†’ mission â†’ map section â†’ confirm** loads an installed map through the same safe loader as the desktop window. Cancel is selected first in the confirmation page. Loading is still restricted to the game's main menu; active-mission travel remains blocked. The user confirmed successful live loading through the overlay.

## Remaining desktop controls in the overlay (v0.16.0)

- **Movement speed** opens a submenu with an enable toggle and 0.5x, 1x, 1.5x, 2x and 3x multipliers. Choosing a multiplier while disabled stores it; choosing one while enabled updates the existing speed cheat without changing other options.
- Each NPC editor now includes **Rename**, **Detailed stats**, and a **Position** submenu with X/Y/Z drafts, Apply XYZ position, Use current position, In front of Sam and Move to Sam. Draft edits do not move the NPC until Apply is selected. Rename changes the overlay display label for this process/session, not the actor's engine name.
- **Health â†’ Custom health** accepts any whole value from 1 to 100 as well as the existing presets. **Detailed stats** shows origin/type, engine instance name, health, condition, activity, movement, distance, weapon, XYZ, pawn/AI states, target and controller attachment.
- **Trainer tools / About / Exit** contains game detection refresh, connection testing, trainer version, supported Steam build/App ID, and an exit confirmation. Exit uses the existing close/cheat-restoration handler and leaves the game running.

For text fields, press Enter to edit, type the value, then Enter to apply or Escape to cancel. The initial value is selected so typing replaces it. Ctrl+A selects all; Left/Right, Home/End, Backspace and Delete edit the draft. Use a dot for decimal coordinates. F6 closes and cancels a draft; switching away from the game also cancels it. While editing, typing is consumed by the overlay instead of reaching gameplay. The trainer process must remain running to provide the overlay.

Build and automated input/NPC regression checks are provided. New live keyboard-entry, utility, speed-selection and coordinate-editing checks are left for the user's requested gameplay test.

## Tray application (v0.17.0)

Launching the trainer now starts a background system-tray application, with no large trainer window. Start Splinter Cell through Steam in windowed mode and press **F6** for all game controls. If the tray icon is hidden, use the Windows tray overflow area. A grey icon means the game is not detected, green means detected, and orange means the overlay could not start.

Right-click the tray icon for **Status**, **Restart overlay**, and **Exit**. Double-clicking the icon or launching the trainer again opens the compact Status window in the existing process. Closing or hiding Status keeps the trainer and overlay running. Restart overlay replaces its window and keyboard hook without disabling active cheats.

Exit from the tray, Status or the overlay uses the same graceful cleanup: stop accepting overlay input, wait for an outstanding update, restore additional cheat settings and infinite health, then close the background controller and remove the tray icon. Splinter Cell stays running. If cleanup fails, the trainer remains available and opens Status so the problem can be reviewed and Exit retried.

The former MainWindow is retained internally as a hidden control host for the existing cheat sessions; startup never shows it. The old NPC manager is no longer exposed by the tray UI. Application shutdown is explicit so hiding the Status window does not terminate the process.

Verification: clean build; overlay/cheat regression checks; STA lifecycle checks for invisible controller startup, close-request routing, replacing only the overlay, and orderly shutdown/window cleanup. Desktop smoke checks confirmed no large startup window and second-launch activation of the compact Status window. The user confirmed the tray workflow works, including F6, hiding Status, restarting the overlay and exiting.

## Bypass locks (v0.18.0)

Enable **F6 â†’ Bypass locks**, close the overlay, then use the game's normal interaction:

- Nearby ordinary, usable, closed swinging doors with knob openers are temporarily treated as unlocked, so Open Door skips the lockpick minigame. Keypad/retinal opener doors, unusable doors and distant locks are excluded from this direct unlock path. Explicitly linked ordinary door leaves are handled together.
- When Sam actually starts using a keypad, his active EKeyPadInteraction is checked before the keypad enters its existing s_AccessGranted state. The game's own code handles access feedback, mission events and linked-door unlocking. Codes are neither rewritten nor entered by the trainer, and NPC/idle keypad interactions are excluded. After access is granted, use the door normally.
- Unused prepared locks are restored when disabling the toggle, leaving the area, losing the session heartbeat or exiting. Once normal door opening has begun, the unlock is committed instead of relocking completed progress. Enable before starting a lockpick interaction; an already-running pick minigame is not forcibly interrupted. Script-blocked doors remain blocked.

Lock metadata is resolved separately when this option is enabled, so unsupported lock scripts do not prevent unrelated cheats. Native protocol 16 separates this helper from earlier pinned code. The normal interaction paths were researched using the primary [EDoorInteraction](https://github.com/Joshhhuaaa/EnhancedSC/blob/main/UnrealScript/EchelonIngredient/Classes/EDoorInteraction.uc), [ESwingingDoor](https://github.com/Joshhhuaaa/EnhancedSC/blob/main/UnrealScript/EchelonIngredient/Classes/ESwingingDoor.uc) and [EKeyPad](https://github.com/Joshhhuaaa/EnhancedSC/blob/main/UnrealScript/EchelonIngredient/Classes/EKeyPad.uc) sources and checked against loaded property metadata.

Validation: clean native/WPF build, native policy checks for scope/restoration/player-owned keypad use, cheat-mask checks, overlay and tray regression checks. A live toggle smoke test in Tbilisi part 2 accepted the new flag and confirmed that the nearby keypad door remained locked and its access-code storage unchanged before interaction; the toggle was switched off afterward. Actual keypad success and ordinary lockpick-door interaction still require gameplay confirmation.

## Quiet lights out (v0.19.0)

Enable **F6 â†’ Quiet lights out**, then close the overlay to inspect the level. Toggle it off to restore surviving lights. The feature suppresses supported Light/ELight and EGamePlayObjectLight actors throughout the active level, while skipping player/inventory-owned lighting and deleted actors. It changes output type/brightness, glow and beam flags, and prevents automatic initial-type restoration while active. Renderer cache flags are invalidated using reflected fields; no damage, sound, trigger, switch-state or Level.AddChange functions are called.

Original output values are retained by actor pointer, object index, name and level identity. New nonzero output values written by the game while enabled are remembered before being suppressed. Disabling, heartbeat expiry and normal trainer exit restore surviving identities whose values are still muted. Known destroyed fixture states are not relit. Physical meshes, collision, health, alarm flags and AI state are unchanged by this feature.

This is output suppression rather than a no-suspicion guarantee: guards or scripts may still react to the resulting darkness. Baked/ambient illumination and emissive materials may remain visible. World changes made while the toggle is active, including saving or operating real switches, still require gameplay testing.

The primary [ELight](https://github.com/Joshhhuaaa/EnhancedSC/blob/main/UnrealScript/Echelon/Classes/ELight.uc) and [EGamePlayObjectLight](https://github.com/Joshhhuaaa/EnhancedSC/blob/main/UnrealScript/Echelon/Classes/EGamePlayObjectLight.uc) implementations were consulted, and field owners/types, LT_None=0, and renderer cache metadata were verified read-only against the running game. Native protocol 17 separates the helper from earlier pinned versions.

Validation: clean native/WPF build, native light scope/restoration policy checks, cheat flag checks and overlay/NPC regression checks. The user confirmed the quiet-lights feature works in gameplay.


## Disable all security cameras (v0.20.0)

F6 â†’ **Disable all security cameras** â†’ Enter. This one-shot action deactivates security cameras in the current mission, including unbreakable security cameras, and reports the count. It uses the camera's scripted `s_Deactivated` state while suppressing its disable sound and AI change notification; it does not damage or destroy cameras. Sam's camera and sticky cameras are excluded. Destroyed/jammed cameras are left alone. Already deactivated cameras are counted separately.

Load a mission first. Reloading a level or later mission scripts can restore cameras; use the action again if needed. Existing alarms are not cleared. Gameplay verification is pending.

Implementation reference: EnhancedSC's `EBaseCam.uc` and `ESensor.uc` (EchelonIngredient), matching the supported game's reflected classes.


## Enemies fight each other (v0.21.0, experimental)

F6 â†’ **Enemies fight each other (30s)** â†’ Enter. Issues a standard 30-second `GOAL_Attack` order to each eligible armed, conscious hostile guard, targeting the nearest eligible guard within 2,000 game units. Includes map NPCs and trainer spawns. Sam, civilians, unconscious/dead NPCs, and guards in special interaction states are excluded. At least two nearby eligible guards are needed. The result reports how many orders were issued.

v0.21.1 keeps a bounded 30-second game-thread session that revalidates participants, redirects attack goals and firing targets away from Sam, clears player-seen flags, and chooses a new opponent when needed. It does not change NPC health. The session expires automatically; after it expires normal AI can target Sam again. Gunfire, injuries and deaths can still trigger normal mission consequences. Reload a previous save to undo those consequences. Live gameplay verification is pending.

Source references: EnhancedSC `EAIController.uc`, `EPattern.uc`, and Engine `EchelonEnums.uc`; input layouts are checked against the loaded game's reflection data before issuing orders.


## Global map NPC editing (v0.22.0)

F6 â†’ Map NPCs â†’ **Global condition (all map NPCs)** or **Global activity (all map NPCs)**. Select an action, then confirm; Cancel is selected first. Condition options are Alive / revive (knocked-out NPCs only), Knocked out, and Dead. Activity options match individual editing: idle / guard, walk, run, search the point in front of Sam, and alerted. Dead NPCs cannot be revived by this feature.

Eligible map and trainer-spawned NPCs captured when the action is selected are included; automatic and manual protections are excluded. Every target is refreshed and checked again before its individual request; disappeared or ineligible NPCs are skipped. A summary reports requested, skipped and failed edits. Requests run sequentially; navigation remains available while the batch completes. Keep gameplay unpaused for transitions to finish. Mission consequences still apply. Live gameplay verification is pending.


### Global edit targeting (v0.22.1)

Global condition and activity now include both map and trainer-spawned NPCs. The global controls are available from either roster. Map NPCs with `bKeepNPCAlive`, non-hostile map NPCs, and map NPCs whose protection metadata cannot be resolved are excluded automatically. Individual editing remains available. An NPC editor also provides **Exclude from global edits**, remembered for the current game process.

Mission scripts can protect characters without these flags. These checks cannot guarantee every mission-critical character is detected; manually exclude any additional required NPC before a global edit. Protection is checked again for every batch request. Gameplay verification is pending.


### Revival tracking fix (v0.22.2)

NPC snapshots and actions refresh each surviving pawn's current controller reference, validating its class, level and reciprocal pawn link. Revival or mission-script controller replacement no longer leaves the trainer using a stale controller. Pawn IDs, origin, custom names and manual global exclusions remain unchanged. A detached controller is reported unavailable until a valid controller attaches. NPCs actually in `s_Groggy` remain reviving until the game finishes recovery.

The user confirmed the v0.22.2 revival tracking fix works in gameplay after testing global NPC edits.


## NPC Manager layout (v0.23.0)

F6 → **NPC Manager** now groups Spawn guard, Browse NPCs, Global controls and Cleanup. Browse uses one-line NPC rows and an All / Map / Spawned filter. Global controls has an independent Map + Spawned / Map / Spawned scope and keeps existing protection, confirmation and per-target validation. Changing the browse filter does not change global scope. Each NPC editor groups Condition, Activity, Health, Position and Details / Rename; position shortcuts live inside Position, and full stats and renaming live inside Details. Cleanup holds bulk spawned-NPC removal. Individual removal now opens a Cancel-first confirmation. Back returns through the current section to NPC Manager and then the main menu.


### ESP and proximity browsing (v0.23.1)

Enemy ESP is now the first toggle under F6 → NPC Manager, using the existing ESP implementation and shared state. It has been removed from the main menu. Browse NPCs sorts each All / Map / Spawned filter by current distance to Sam (nearest first), shows distances in metres and breaks ties by stable NPC ID. Live snapshots update the order; selections and edits remain bound to actor IDs. Global batch ordering is unchanged.
