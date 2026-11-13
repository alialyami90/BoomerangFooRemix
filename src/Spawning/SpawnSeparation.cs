using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoomerangFoo.Patches;
using HarmonyLib;
using UnityEngine;

namespace BoomerangFoo.Spawning
{
    // Levels have 6 spawn points. With more players the game reuses points (free-for-all: copies added by
    // PatchLevel, then shuffled; teams: a fixed 6-slot order per team), so players can start stacked.
    // Before each round starts, a bot standing on a human, or a second human on the same point, is moved to the
    // spawn point furthest from the humans. Bots may still share points with other bots.
    public static class SpawnSeparation
    {
        // Reused points share an exact position, so anything this close counts as stacked
        private const float StackedDistance = 0.5f;

        private static readonly FieldInfo levelField = AccessTools.Field(typeof(LevelManager), "level");
        private static readonly FieldInfo spawnPointsField = AccessTools.Field(typeof(Level), "playerSpawnPoints");
        private static readonly MethodInfo forgetPreviousTransforms = AccessTools.Method(typeof(InterpolatedTransform), "ForgetPreviousTransforms");
        private static bool failed = false;

        public static void Activate()
        {
            PatchGameManager.OnPreStartRoundSequence += Separate;
        }

        private static void Separate(GameManager gameManager)
        {
            if (failed) return;
            try
            {
                List<Player> players = gameManager.players.Where(player => player != null && player.gameObject.activeSelf).ToList();
                if (players.Count <= 6) return;

                object level = levelField?.GetValue(Singleton<LevelManager>.Instance);
                if (!(spawnPointsField?.GetValue(level) is List<Transform> spawnPoints) || spawnPoints.Count == 0) return;

                List<Vector3> humanSpots = [];
                int moved = 0;
                // Humans first, so each human keeps a point of their own when there's one free
                foreach (Player player in players.OrderBy(player => CommonFunctions.IsBot(player)))
                {
                    bool isBot = CommonFunctions.IsBot(player);
                    if (humanSpots.Any(spot => Flat(spot - player.transform.position) < StackedDistance))
                    {
                        Vector3 target = FurthestFrom(spawnPoints, humanSpots);
                        MoveTo(player, target);
                        moved++;
                    }
                    if (!isBot) humanSpots.Add(player.transform.position);
                }
                if (moved > 0)
                {
                    BoomerangFoo.Logger.LogInfo($"Spawn separation: moved {moved} player(s) off a human's spawn point");
                }
            }
            catch (Exception e)
            {
                failed = true;
                BoomerangFoo.Logger.LogError($"Spawn separation failed and was turned off: {e}");
            }
        }

        // The spawn point whose nearest human is furthest away
        private static Vector3 FurthestFrom(List<Transform> spawnPoints, List<Vector3> humanSpots)
        {
            return spawnPoints
                .Where(point => point != null)
                .Select(point => point.position)
                .OrderByDescending(position => humanSpots.Count == 0 ? 0f : humanSpots.Min(spot => Flat(spot - position)))
                .First();
        }

        private static void MoveTo(Player player, Vector3 position)
        {
            // Keep the player's own depth, as the game does when it places players
            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            InterpolatedTransform interpolated = player.GetComponent<InterpolatedTransform>();
            if (interpolated != null) forgetPreviousTransforms?.Invoke(interpolated, null);
        }

        // Distance in the level's plane (the game positions players by x and y)
        private static float Flat(Vector3 offset)
        {
            return new Vector2(offset.x, offset.y).magnitude;
        }
    }
}
