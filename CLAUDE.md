# CLAUDE.md

Guidance for working in this repository.

## What this is

**Boomerang Foo: Chaos Mod** is a [BepInEx 5](https://github.com/BepInEx/BepInEx) + Harmony plugin for the game *Boomerang Fu*. It adds game modes, custom match settings, powerup modifiers, up to 12 players, and an "Impossible" bot difficulty. This repo is a fork (remix) of `Jeffjewett27/BoomerangFoo`. The plugin GUID is still `Jeffjewett27.plugins.BoomerangFoo`.

## Building

- Project: `src/BoomerangFoo.csproj` (`netstandard2.1`, C# 12). Version is set by `<Version>` there and shown in the main menu as `MOD <version>`.
- The build references the game's DLLs from `$(GamePath)\Boomerang Fu_Data\Managed\` (`Assembly-CSharp`, `Rewired_Core`, `FMODUnity`, TextMeshPro, etc.). `GamePath` defaults to the Windows Steam path. **It cannot be built without a local game install.** Those DLLs are not in the repo.
- Post-build targets use PowerShell. They install BepInEx into the game folder if it's missing, then copy `BoomerangFoo.dll` and `Newtonsoft.Json.dll` to `BepInEx/plugins/BoomerangFoo`. A Release build also produces a zip in `src/bin/export/`.
- One-click install: `Install-BoomerangFoo.bat` (repo root, CRLF) downloads `tools/install-mod.ps1` from the branch named in the .bat and runs it. The script (Windows PowerShell 5.1) does these steps in order:
  1. Checks for `dotnet`.
  2. Finds the game: a saved path (`%APPDATA%\BoomerangFooRemix\install-config.json`), then Steam's registry and `libraryfolders.vdf`, then a prompt.
  3. Offers to close a running game.
  4. Downloads the branch zip into `%TEMP%`.
  5. Builds with `-c Release -p:SkipReleaseZip=true` (skips the export zip), so the project's copy step installs the DLLs.
  6. Checks that the installed DLL was updated.
  When work moves to another branch, update `BRANCH` in the .bat and the README link.
- There are no automated tests. Changes have to be verified in-game. BepInEx logs go through `BoomerangFoo.Logger`.

## Layout

```
src/
  BoomerangFoo.cs        Plugin entry (Awake): PatchAll, register powerups and game modes, per-frame game speed and F1 debug toggle
  GameModes/             GameMode base + custom modes
  Powerups/              CustomPowerup base + one singleton per powerup modifier
  Patches/               Harmony patches that expose events/delegates (PatchPlayer, PatchGameManager, ...)
  UI/Modifiers.cs        Clones the game's Modifiers-menu rows to create mod settings; save/load
  UI/GameMode.cs         Helpers for game-mode buttons
  Settings/              GameSettings (per-match values), ModSettings (MaxPlayers = 12)
  Localization/          localization.json (embedded resource) + loader
  PlayerState.cs         MonoBehaviour added to each Player; holds per-player powerup state
  Powerups.cs            PowerupManager: patches List<PowerupType>.Add/Remove to raise acquire/remove events
  _CustomSettings.cs     Legacy (deprecated) static settings; defaults set in its static ctor, optionally read from customConfig.ini
*.txt (root)             Reference dumps: levels, characters, hats, original settings rows
```

## Core patterns

### Patches expose hooks, features subscribe
Patch classes in `Patches/` don't hold feature logic. Each one pairs a `[HarmonyPatch]` prefix/postfix with either:
- a static `event` with an `Invoke...` helper, e.g. `PatchPlayer.OnPostDie` or `PatchGameManager.OnPreAddPlayerKill`; or
- a static `Func`/`Predicate` override that is used when non-null, e.g. `PatchGameManager.PlayerRelationship`, `PatchGameManager.OpponentsLeftStandingNow`, `PatchSettingsManager.GoldenDiscHoldTime` or `PatchPlayer.GoldenDiscPenalty`.

Game modes and powerups subscribe in `Hook()`/`Activate()` and unsubscribe in `Unhook()`/`Deactivate()`. Whatever you subscribe or assign, undo it symmetrically, because modes are switched at runtime. Private game members are reached with reflection (`FieldInfo`/`MethodInfo` cached in static readonly fields) or `AccessTools`. Prefer adding a new event to the relevant `PatchX` class over patching the same method twice.

### Game modes (`GameModes/GameMode.cs`)
- `GameMode(id, nameTerm, hintTerm, SettingsManager.MatchType, teamMatch, slotTemplate)`. `slotTemplate` (0–3) chooses which vanilla icon to clone.
- Registered in `BoomerangFoo.Awake` with `GameMode.Register(mode, GameMode.Slot.X)`. Slots 0–3 are the vanilla modes; `Extra1`–`Extra5` hold PowerDrain, RamboHulk (Juggernaut), SurviveTillDawn, TeamGolden and RussianRoulette. All slots are now used. Buttons for every slot are built in `UIMenuMatchSettingsInitPatch`. With more than 6 modes they are split into two rows; that code is marked "Still buggy".
- `GameMode.selected` is the active mode. `MatchSelected` unhooks the previous mode, hooks the new one, shows its settings rows and reloads saved settings.
- Per-mode virtuals: `GivesStartingPowers` (whether the global Human/Bot Starting Powers apply) and `GetMaxPowerups(player)`, the powerup cap. `PatchPlayer.ApplyMaxPowerups` applies the cap in `Player.Init` and again at every `SpawnIn`, so a mode's cap never leaks into the next mode.
- RussianRoulette: on every `OnPostSpawnIn` it clears the player's powerups and gives `RandomPowerupCount` distinct random ones (setting `gameMode.RussianRoulette.count`, 1–8, default 3) from `MatchSettings.availablePowerups`. Powerup pickups don't spawn in this mode (`PatchLevelManager.BlockPowerupSpawn`, as in Juggernaut). Its cap is `max(Max Number, RandomPowerupCount)` so all the random powers fit.
- Overrides: `Hook()`, `Unhook()`, `RegisterSettings()`. The base `RegisterSettings()` registers the global settings (bounciness, boomerang size, max powerups, spawn rate, sudden death, match length, no boomerangs, knockback, game speed, starting powers), then calls `GenerateUI()` on every powerup. Mode-specific settings use IDs prefixed `gameMode.<id>.`. `Modifiers.ShowSelectedGameMode` shows or hides rows based on that prefix.

### Settings UI (`UI/Modifiers.cs`)
- `Modifiers.settings` is a dictionary keyed by setting ID. It is seeded from the vanilla menu rows, whose IDs are their I2 localization terms (e.g. `ui_label_matchlength`, `ui_label_warmuplevel`, `ui_label_edgeprotection`, `ui_boomerangs`, `ui_label_friendlyfire`, `powerupSelections`).
- New row: `Modifiers.CloneModifierSetting(newId, labelTerm, cloneFromId, insertAfterId)`. Clone from `ui_label_warmuplevel` (on/off style), `ui_label_edgeprotection` (multi-value), `ui_boomerangs` (section header) or `powerupSelections` (powerup toggle grid).
- Then call `SetSliderOptions(labels[], defaultIndex, hints[])` or `PreparePowerupToggles(defaultMask)`, and then `SetGameStartCallback((gameMode, index) => ...)`. The callbacks don't fire when a slider moves. They all run once in `GameMode.GameStart()`, which `GameManager.StartMatch` calls through the prefix hook. Write the value into `gameMode.gameSettings`, a field on the mode, or the powerup singleton.
- Insertion order matters. `insertAfterId` must already exist, so register rows in order.
- Selected indices are saved to `customModifiers.json` in the game's platform data path. Newtonsoft.Json is loaded through reflection; it must ship next to the plugin DLL. Changing a slider's options list or default can shift saved indices.

### Powerup modifiers (`Powerups/`)
- Each one is a singleton subclass of `CustomPowerup`, with `Name`, `LocalizationTerm` and `Bitmask` (`PowerupType`). It is added to `CustomPowerup.Registered` in `Awake`, and `ActivateAll()` subscribes them all once.
- `GenerateUI()` calls `base.GenerateUI()` (header row `customPowerup.<Name>.header`), then clones rows with IDs `customPowerup.<Name>.<setting>`.
- Per-player values live on `PlayerState`. Its field initializers copy from the singletons when the component is added in `Player.Start`. Retrieve it with `CommonFunctions.GetPlayerState(player)`.

### Human vs bot powerups and spawn weights
- Global settings `humanStartPowers` and `botStartPowers` are toggle grids that give their side starting powerups (round 1 only, `PlayerSpawnInPatch`). `humanPickupPool` and `botPickupPool` restrict what each side can get from pickups. `spawnWeights.<PowerupType>` sliders run Off/Rare/Low/Normal/High/Very High (0, 0.25, 0.5, 1, 2, 4×), with one generated per toggle in the vanilla `powerupSelections` grid. Values go into `GameSettings` (`Human/BotStartupPowerUps`, `Human/BotPowerupPool`, `PowerupSpawnWeights`).
- `LevelManagerSelectRandomPowerPatch` (in `PatchLevelManager.cs`) prefixes `LevelManager.SelectRandomPower(Player)`. It filters `possiblePowerups` by side pool, weight > 0 and powerups not already owned, then makes a weighted pick. It returns to the original method when nothing is customized or no candidate is left. Its `Prepare()` skips the patch if the method is not found. The game's selection code isn't available to read, so any deck or exclusion rules inside the original are bypassed only while customizations are active.
- `CommonFunctions.IsBot(player)` finds bots by reflection: it uses the first `PlayerType` field or property on `Player`, falling back to an `*AI*` component. Which method it used is logged once.

### Powerup display (HUD)
- `UI/PowerupHud.cs` shows each human player's current powerups during play: P1 top-left, P2 top-right, P3 bottom-left, P4 bottom-right. It runs on its own `ScreenSpaceOverlay` canvas (`BFooPowerupHud`, DontDestroyOnLoad, sortingOrder -1, scaled by height from 1080p) and never modifies game menus.
- Icons are cached from the `UIPowerupToggle`s of the vanilla `powerupSelections` grid when the match-settings menu opens (`PatchUIMenuMatchSettings.OnPostInit`). A powerup's icon is the image whose sprite appears on that button only; frames and backgrounds are shared by all buttons, and picking by name or position chose a blank ring. Every image found per button is logged once. Badges clone the `UIPlayerIcon` of a `UIMenuRoundEnd` score row and call `SetPlayer`. Either one falls back to TextMeshPro text.
- It refreshes on `PatchGameManager.OnPostUpdate` while `gameState == Playing`, rebuilding a panel only when that player's `activePowerup` changes. Any exception logs once and turns the display off. The `powerupHud` On/Off setting (below Max Number) maps to `GameSettings.ShowPowerupHud`.
- Lesson from the first match stats build: a TextMeshPro label created at runtime needs its `font` set before `outlineWidth` is touched, or it throws. An exception inside a postfix on a game UI method aborts the game's own flow (it left the round-end screen stuck), so wrap hook invocations that run inside game UI code.

### Match stats (round-end screen)
- `Stats/MatchStats.cs` tracks per-playerID kills (opponents only, via `GameManager.AreOpponents`), deaths, self-eliminations, clean rounds and who-killed-whom tallies (nemesis / favorite victim). It is activated once in `Awake` and reset on `OnPreStartMatch`.
- Deaths come from `PatchPlayer.OnPostDieConfirmed`, which fires only when `Player.Die` actually took the player from alive to `Actor.ActorState.Dead`. Decoy revives and no-op calls are excluded; `OnPostDie` still fires for every call. A death is a self-elimination when no other player was credited through `OnPreAddPlayerKill` during that `Die` call.
- Clean rounds are counted once per `roundNumber` when the round-end screen opens (`PatchUIMenuRoundEnd.OnPostInit`).
- `UI/MatchStatsUI.cs` adds a `BFooMatchStats` TextMeshPro label to each `UIScoreRow`. It sits right of the row's visible graphics and auto-sizes to the space left before the canvas edge. In team matches it shows team totals without nemesis/victim. The `matchStats` On/Off setting maps to `GameSettings.ShowMatchStats`.

### Players > 6
`ModSettings.Instance.MaxPlayers` (12) drives several patches:
- the lobby bot slider and cloned `UILobbyPlayer`s (`PatchUIMenuLobby`)
- score rows (`PatchUIMenuRoundEnd`)
- hint, alert and powerup bars plus character colors (`GameManagerSetupPatch`)
- level `maxPlayers` (`LevelManagerBuildMatchPlaylistPatch`)
- duplicated spawn points (`PatchLevel`)

### Localization
- `src/Localization/localization.json` maps `term → { description, contributions, translations: { English, ... } }`. Missing languages fall back to English.
- It is registered into I2's `LocalizationManager` when the match-settings menu initializes. A `BepInEx/plugins/BoomerangFoo/localization.json` file overrides the embedded copy.
- Labels and hints can be terms or plain text. A term that isn't found is shown verbatim (`LocalizedStringToStringPatch`).
- Parameters use `Term__value`, e.g. `ModifierShieldHint__3`. Each `__` piece replaces the next `%s%` in the translation.
- Add new user-facing strings to `localization.json` instead of hardcoding English.

## Conventions

- Namespace `BoomerangFoo` (sub-namespaces `.Patches`, `.GameModes`, `.Powerups`, `.UI`, `.Settings`). Patch classes are named `<Type><Method>Patch`.
- C# 12 collection expressions (`[...]`) are used widely.
- Files keep their existing BOM and CRLF style (`.gitattributes`). Match the surrounding file.
- Guard against `GameMode.selected` / `gameSettings` assumptions in new patches. Many existing patches dereference `GameMode.selected.gameSettings` directly.

## Known quirks

- `GameSettings.NoBoomerangs` defaults to `true`. It only works because the `noBoomerangs` start callback overwrites it.
- `ShieldPowerup.Deactivate` doesn't unsubscribe `OnPostSpawnIn`, and `TeamGolden.Unhook` doesn't clear `PatchPlayer.GoldenDiscPenalty`.
- `_CustomSettings` is deprecated but still live. Its static constructor sets defaults; for example, `EnableDuplicatedCharacters = true` and `ShrinkingBoundsKicksOnly = false`. It also parses `customConfig.ini` from the working directory if that file exists. A few patches still read it directly (`EnableDuplicatedCharacters`, `ShrinkingBoundsKicksOnly`, `TeamGoldenBoomerang`, `SuddenDeathTimeLimit`, `BoomerangBouncinessMultiplierTiedToPowerUp`), and `GameSettings` field initializers copy their defaults from it. New settings belong in `GameSettings` and the Modifiers UI, not here.
- `PatchPlayer.OnPreStartFall` is declared but never invoked; `PlayerStartFallPatch` only uses the `PreStartFall` override.
- `eGameMode.cs` is unused (superseded by `GameMode.Slot`).
