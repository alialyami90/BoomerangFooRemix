using System;
using System.Collections.Generic;
using System.Text;

namespace BoomerangFoo.Settings
{
    public class GameSettings
    {
        public float BoomerangBouncinessMultiplier = _CustomSettings.BoomerangBouncinessMultiplier;
        public float BoomerangSize = _CustomSettings.BoomerangSize;
        public int MaxPowerups = _CustomSettings.MaxPowerUps;

        public bool RapidPowerUpSpawning = _CustomSettings.RapidPowerUpSpawning;
        public int MatchScoreLimit = _CustomSettings.MatchScoreLimit;
        public float SuddenDeathTimeLimit = _CustomSettings.SuddenDeathTimeLimit;

        public PowerupType HumanStartupPowerUps = _CustomSettings.StartupPowerUps;
        public PowerupType BotStartupPowerUps = _CustomSettings.StartupPowerUps;

        // Powerups each side may receive from pickups (intersected with the game's available powerups)
        public PowerupType HumanPowerupPool = Powerup.AllPowerups();
        public PowerupType BotPowerupPool = Powerup.AllPowerups();
        // Relative pickup weight per powerup; missing entries count as 1
        public Dictionary<PowerupType, float> PowerupSpawnWeights = [];

        public bool NoBoomerangs = true;

        public float KnockbackFactor = 1f;
        public float GameSpeed = 1f;
    }
}
