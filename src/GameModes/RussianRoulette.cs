using System;
using System.Collections.Generic;
using System.Linq;
using BoomerangFoo.Patches;
using BoomerangFoo.UI;

namespace BoomerangFoo.GameModes
{
    public class RussianRoulette : GameMode
    {
        public int RandomPowerupCount = 3;

        public RussianRoulette() : base("RussianRoulette", "GameModeRussianRouletteName", "GameModeRussianRouletteHint", SettingsManager.MatchType.DeathMatch, false, 0) { }

        // The roulette replaces the global starting powers
        public override bool GivesStartingPowers => false;

        // Pickups stack on top of the random powers instead of pushing out the oldest
        public override int GetMaxPowerups(Player player)
        {
            return Math.Max(gameSettings.MaxPowerups, SinglePowerups(Powerup.AllPowerups()).Count);
        }

        public override void Hook()
        {
            PatchPlayer.OnPostSpawnIn += RollPowerups;
        }

        public override void Unhook()
        {
            PatchPlayer.OnPostSpawnIn -= RollPowerups;
        }

        public override void RegisterSettings()
        {
            string headerId = $"gameMode.{id}.header";
            Modifiers.CloneModifierSetting(headerId, name, "ui_boomerangs", "ui_label_friendlyfire");

            string countId = $"gameMode.{id}.count";
            var count = Modifiers.CloneModifierSetting(countId, "GameModeRussianRouletteCount", "ui_label_edgeprotection", headerId);
            string[] options = new string[8];
            string[] hints = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                options[i] = (i + 1).ToString();
                hints[i] = $"GameModeRussianRouletteCountHint__{i + 1}";
            }
            count.SetSliderOptions(options, 2, hints);
            count.SetGameStartCallback((gameMode, sliderIndex) =>
            {
                if (gameMode is RussianRoulette roulette)
                {
                    roulette.RandomPowerupCount = sliderIndex + 1;
                }
            });
        }

        // Every round: clear the player's powerups and give distinct random ones from the available powerups
        private void RollPowerups(Player player)
        {
            if (player == null) return;

            player.ClearPowerups();

            List<PowerupType> options = SinglePowerups(Singleton<SettingsManager>.Instance.MatchSettings.availablePowerups);
            for (int i = 0; i < RandomPowerupCount && options.Count > 0; i++)
            {
                int index = UnityEngine.Random.Range(0, options.Count);
                player.StartPowerup(options[index]);
                options.RemoveAt(index);
            }
        }

        private static List<PowerupType> SinglePowerups(PowerupType mask)
        {
            List<PowerupType> powerups = [];
            for (int bit = 0; bit < 31; bit++)
            {
                PowerupType powerup = (PowerupType)(1 << bit);
                if ((mask & powerup) != 0)
                {
                    powerups.Add(powerup);
                }
            }
            return powerups;
        }
    }
}
