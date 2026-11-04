using System.Collections.Generic;
using System.Linq;
using BoomerangFoo.Patches;
using BoomerangFoo.UI;

namespace BoomerangFoo.Stats
{
    public class PlayerMatchStats
    {
        public int Kills;
        public int Deaths;
        public int SelfEliminations;
        public int CleanRounds;

        // other playerID -> (count, sequence of the latest occurrence) for tie-breaking
        public Dictionary<int, (int count, int last)> KilledBy = [];
        public Dictionary<int, (int count, int last)> Victims = [];

        public float KillDeathRatio => Deaths == 0 ? Kills : (float)Kills / Deaths;

        public (int playerId, int count)? Nemesis => MostFrequent(KilledBy);
        public (int playerId, int count)? FavoriteVictim => MostFrequent(Victims);

        private static (int playerId, int count)? MostFrequent(Dictionary<int, (int count, int last)> tally)
        {
            if (tally.Count == 0) return null;
            var top = tally.OrderByDescending(kvp => kvp.Value.count).ThenByDescending(kvp => kvp.Value.last).First();
            return (top.Key, top.Value.count);
        }
    }

    public static class MatchStats
    {
        public static Dictionary<int, PlayerMatchStats> Players { get; } = [];

        private static readonly HashSet<int> diedThisRound = [];
        private static int countedRound = -1;
        private static int sequence = 0;

        // Death in progress, used to tell kills from self-eliminations
        private static Player pendingVictim = null;
        private static bool pendingVictimCredited = false;

        public static void Activate()
        {
            PatchGameManager.OnPreStartMatch += StartMatch;
            PatchPlayer.OnPreDie += PreDie;
            PatchGameManager.OnPreAddPlayerKill += AddPlayerKill;
            PatchPlayer.OnPostDieConfirmed += DieConfirmed;
            PatchUIMenuRoundEnd.OnPostInit += RoundEnd;
            MatchStatsUI.Activate();
        }

        public static PlayerMatchStats Get(int playerId)
        {
            if (!Players.TryGetValue(playerId, out var stats))
            {
                stats = new PlayerMatchStats();
                Players[playerId] = stats;
            }
            return stats;
        }

        private static void StartMatch(GameManager gameManager)
        {
            Players.Clear();
            diedThisRound.Clear();
            countedRound = -1;
            sequence = 0;
            pendingVictim = null;
        }

        private static void PreDie(Player player)
        {
            pendingVictim = player;
            pendingVictimCredited = false;
        }

        private static void AddPlayerKill(GameManager gameManager, Player killer, Player killed)
        {
            if (killer == null || killed == null || killer == killed) return;

            if (killed == pendingVictim)
            {
                pendingVictimCredited = true;
            }

            // Team kills don't count as kills
            if (!gameManager.AreOpponents(killer, killed)) return;

            sequence++;
            var killerStats = Get(killer.playerID);
            killerStats.Kills++;
            Tally(killerStats.Victims, killed.playerID);
            Tally(Get(killed.playerID).KilledBy, killer.playerID);
        }

        private static void DieConfirmed(Player player)
        {
            var stats = Get(player.playerID);
            stats.Deaths++;
            diedThisRound.Add(player.playerID);
            if (player == pendingVictim && !pendingVictimCredited)
            {
                stats.SelfEliminations++;
            }
            pendingVictim = null;
        }

        private static void RoundEnd(UIMenuRoundEnd menu)
        {
            GameManager gameManager = Singleton<GameManager>.Instance;
            if (gameManager.roundNumber != countedRound)
            {
                countedRound = gameManager.roundNumber;
                foreach (Player player in gameManager.players)
                {
                    var stats = Get(player.playerID);
                    if (!diedThisRound.Contains(player.playerID))
                    {
                        stats.CleanRounds++;
                    }
                }
                diedThisRound.Clear();
            }

            MatchStatsUI.Show(menu);
        }

        private static void Tally(Dictionary<int, (int count, int last)> tally, int playerId)
        {
            tally.TryGetValue(playerId, out var entry);
            tally[playerId] = (entry.count + 1, sequence);
        }
    }
}
