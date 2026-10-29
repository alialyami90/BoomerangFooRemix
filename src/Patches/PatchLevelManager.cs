using BoomerangFoo.GameModes;
using BoomerangFoo.Settings;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BoomerangFoo.Patches
{
    public class PatchLevelManager
    {
        public static bool BlockPowerupSpawn = false;
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.StartShrinkingLevelBoundary))]
    class LevelManagerStartShrinkingLevelBoundaryPatch
    {
        static void Prefix(LevelManager __instance)
        {
            // remove boomerangs when level starts shrinking
            if (_CustomSettings.ShrinkingBoundsKicksOnly)
            {
                for (int j = 0; j < Singleton<GameManager>.Instance.players.Count; j++)
                {
                    Player player = Singleton<GameManager>.Instance.players[j];
                    player.StopAiming();
                    player.character.SetMeleeDiscs(0);
                    player.attacksLeft = 0;
                    player.IsDisarmed = true;
                    player.pendingDiscs = 0;
                    player.HasDiscs = 0;
                    player.thrownDiscs.ForEach(delegate (Disc i)
                    {
                        i.GetPickedUp(vanish: true);
                    });
                    player.thrownDiscs.Clear();
                }
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), "AttemptToSpawnPowerup")]
    class LevelManagerAttemptToSpawnPowerup
    {
        static readonly FieldInfo collectedFieldInfo = typeof(LevelManager).GetField("powerupsCollectedThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo maxSpawnedFieldInfo = typeof(LevelManager).GetField("maxSpawnedPowerups", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo thisRoundFieldInfo = typeof(LevelManager).GetField("powerupsSpawnedThisRound", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo spawnAttemptsFieldInfo = typeof(LevelManager).GetField("powerupSpawnAttemptsThisRound", BindingFlags.NonPublic | BindingFlags.Instance);

        static float powerupsCollectedThreshold = -1f;
        static int maxSpawnedPowerups = -1;
        static int powerupsSpawnedThisRound = -1;
        static int powerupSpawnAttemptsThisRound = -1;

        static bool Prefix(LevelManager __instance)
        {
            if (PatchLevelManager.BlockPowerupSpawn) return false;
            // Setting to unblock powerup spawn attempt
            // powerupsCollectedThreshold = 100
            // maxSpawnedPowerups = 100
            // powerupsSpawnedThisRound = 0
            // powerupSpawnAttemptsThisRound = 0
            if (GameMode.selected.gameSettings.RapidPowerUpSpawning)
            {
                powerupsCollectedThreshold = (float)collectedFieldInfo.GetValue(__instance);
                maxSpawnedPowerups = (int)maxSpawnedFieldInfo.GetValue(__instance);
                powerupsSpawnedThisRound = (int)thisRoundFieldInfo.GetValue(__instance);
                powerupSpawnAttemptsThisRound = (int)spawnAttemptsFieldInfo.GetValue(__instance);

                collectedFieldInfo.SetValue(__instance, 1000f);
                maxSpawnedFieldInfo.SetValue(__instance, 1000);
                thisRoundFieldInfo.SetValue(__instance, 0);
                spawnAttemptsFieldInfo.SetValue(__instance, 0);
            }
            return true;
        }

        static void Postfix(LevelManager __instance)
        {
            if (powerupsCollectedThreshold >= 0)
            {
                // Reset the values

                collectedFieldInfo.SetValue(__instance, powerupsCollectedThreshold);
                maxSpawnedFieldInfo.SetValue(__instance, maxSpawnedPowerups);
                thisRoundFieldInfo.SetValue(__instance, powerupsSpawnedThisRound + (int)thisRoundFieldInfo.GetValue(__instance));
                spawnAttemptsFieldInfo.SetValue(__instance, powerupSpawnAttemptsThisRound + (int)spawnAttemptsFieldInfo.GetValue(__instance));

                powerupsCollectedThreshold = -1f;
                maxSpawnedPowerups = -1;
                powerupsSpawnedThisRound = -1;
                powerupSpawnAttemptsThisRound = -1;
            }
        }
    }

    [HarmonyPatch]
    class LevelManagerSelectRandomPowerPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(LevelManager), nameof(LevelManager.SelectRandomPower), [typeof(Player)]);
        }

        static bool Prepare()
        {
            if (TargetMethod() == null)
            {
                BoomerangFoo.Logger.LogWarning("LevelManager.SelectRandomPower(Player) not found. Pickup pools and spawn weights are disabled.");
                return false;
            }
            return true;
        }

        // Applies the human/bot pickup pools and per-powerup spawn weights.
        // Falls back to the original selection when nothing is customized or no candidate remains.
        static bool Prefix(LevelManager __instance, Player __0, ref PowerupType __result)
        {
            GameSettings gameSettings = GameMode.selected?.gameSettings;
            if (gameSettings == null || __instance.possiblePowerups == null) return true;

            Player player = __0;
            PowerupType pool = PowerupType.None;
            PowerupType owned = PowerupType.None;
            if (player != null)
            {
                pool = CommonFunctions.IsBot(player) ? gameSettings.BotPowerupPool : gameSettings.HumanPowerupPool;
                owned = player.activePowerup;
            }

            bool customized = false;
            float totalWeight = 0f;
            List<PowerupType> candidates = [];
            List<float> weights = [];
            foreach (PowerupType powerup in __instance.possiblePowerups)
            {
                if (powerup == PowerupType.None) continue;

                float weight = gameSettings.PowerupSpawnWeights.TryGetValue(powerup, out float w) ? w : 1f;
                bool inPool = player == null || (pool & powerup) != 0;
                if (weight != 1f || !inPool) customized = true;
                if (!inPool || weight <= 0f || (owned & powerup) != 0) continue;

                candidates.Add(powerup);
                weights.Add(weight);
                totalWeight += weight;
            }

            if (!customized || totalWeight <= 0f) return true;

            float roll = UnityEngine.Random.value * totalWeight;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0f)
                {
                    __result = candidates[i];
                    return false;
                }
            }
            __result = candidates[candidates.Count - 1];
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.BuildMatchPlaylist))]
    class LevelManagerBuildMatchPlaylistPatch
    {
        static void Prefix(LevelManager __instance)
        {
            // Allow levels to accomodate the new maximum number of players.
            foreach (var levelAsset in __instance.levelAssets)
            {
                if (levelAsset.maxPlayers == 6)
                {
                    levelAsset.maxPlayers = ModSettings.Instance.MaxPlayers;
                }
            }
        }
    }
}
