# CLAUDE.md

Guidance for working in this repository.

## What this is

**Boomerang Foo: Chaos Mod** is a [BepInEx 5](https://github.com/BepInEx/BepInEx) + Harmony plugin for the game *Boomerang Fu*. It adds game modes, custom match settings, powerup modifiers, up to 12 players, and an "Impossible" bot difficulty. This repo is a fork (remix) of `Jeffjewett27/BoomerangFoo`. The plugin GUID is still `Jeffjewett27.plugins.BoomerangFoo`.

## Building

- Project: `src/BoomerangFoo.csproj` (`netstandard2.1`, C# 12). Version is set by `<Version>` there and shown in the main menu as `MOD <version>`.
- The build references the game's DLLs from `$(GamePath)\Boomerang Fu_Data\Managed\` (`Assembly-CSharp`, `Rewired_Core`, `FMODUnity`, TextMeshPro, etc.). `GamePath` defaults to the Windows Steam path. **It cannot be built without a local game install.** Those DLLs are not in the repo.
- Post-build targets use PowerShell. They install BepInEx into the game folder if it's missing, then copy `BoomerangFoo.dll` and `Newtonsoft.Json.dll` to `BepInEx/plugins/BoomerangFoo`. A Release build also produces a zip in `src/bin/export/`.
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
- Registered in `BoomerangFoo.Awake` with `GameMode.Register(mode, GameMode.Slot.X)`. Slots 0–3 are the vanilla modes; `Extra1`–`Extra4` hold PowerDrain, RamboHulk (Juggernaut), SurviveTillDawn and TeamGolden. `Extra5` is free. Buttons for every slot are built in `UIMenuMatchSettingsInitPatch`. With more than 6 modes they are split into two rows; that code is marked "Still buggy".
- `GameMode.selected` is the active mode. `MatchSelected` unhooks the previous mode, hooks the new one, shows its settings rows and reloads saved settings.
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
- `eGameMode.cs` is unused (superseded by `GameMode.Slot`).
