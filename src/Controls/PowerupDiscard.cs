using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoomerangFoo.Patches;
using BoomerangFoo.UI;
using UnityEngine;

namespace BoomerangFoo.Controls
{
    // D-pad down (or Q for the keyboard player) throws away a human player's most recently collected powerup.
    // The player's powerups are cleared and the others given back, so the game itself turns off the discarded
    // one's effects (shield bubble, caffeine speed, ...). A partly used shield keeps its remaining hits.
    public static class PowerupDiscard
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo stopShield = typeof(Player).GetMethod("StopShield", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Func<Player, Rewired.Player> rewiredOf;
        private static bool failed = false;

        public static void Activate()
        {
            PatchGameManager.OnPostUpdate += Update;
        }

        private static void Update(GameManager gameManager)
        {
            if (failed || gameManager.gameState != GameManager.GameState.Playing) return;

            try
            {
                List<Player> humans = gameManager.players.Where(player => player != null && !CommonFunctions.IsBot(player)).ToList();
                foreach (Player player in humans)
                {
                    if (player.actorState == Actor.ActorState.Dead) continue;
                    if (DiscardPressed(player, humans.Count)) Discard(player);
                }
            }
            catch (Exception e)
            {
                failed = true;
                BoomerangFoo.Logger.LogError($"Powerup discard failed and was turned off: {e}");
            }
        }

        private static bool DiscardPressed(Player player, int humanCount)
        {
            Rewired.Player input = RewiredPlayer(player);
            if (input == null)
            {
                // Without the player's own input, the keyboard can only be told apart when there's one human
                return humanCount == 1 && Input.GetKeyDown(KeyCode.Q);
            }

            if (input.controllers.hasKeyboard && input.controllers.Keyboard.GetKeyDown(Rewired.KeyboardKeyCode.Q)) return true;
            foreach (Rewired.Joystick joystick in input.controllers.Joysticks)
            {
                Rewired.IGamepadTemplate gamepad = joystick.GetTemplate<Rewired.IGamepadTemplate>();
                if (gamepad != null && gamepad.dPad.down.justPressed) return true;
            }
            return false;
        }

        // Where the game keeps a player's Rewired input isn't known, so look for it once and log what was used
        private static Rewired.Player RewiredPlayer(Player player)
        {
            if (rewiredOf == null)
            {
                FieldInfo onPlayer = typeof(Player).GetFields(Flags).FirstOrDefault(field => field.FieldType == typeof(Rewired.Player));
                FieldInfo inputField = typeof(Player).GetField("playerInput", Flags);
                FieldInfo onInput = inputField?.FieldType.GetFields(Flags).FirstOrDefault(field => field.FieldType == typeof(Rewired.Player));
                if (onPlayer != null)
                {
                    rewiredOf = p => (Rewired.Player)onPlayer.GetValue(p);
                    BoomerangFoo.Logger.LogInfo($"Powerup discard: input from Player.{onPlayer.Name}");
                }
                else if (onInput != null)
                {
                    rewiredOf = p =>
                    {
                        object playerInput = inputField.GetValue(p);
                        return playerInput == null ? null : (Rewired.Player)onInput.GetValue(playerInput);
                    };
                    BoomerangFoo.Logger.LogInfo($"Powerup discard: input from Player.playerInput.{onInput.Name}");
                }
                else
                {
                    rewiredOf = p => p.playerID < Rewired.ReInput.players.playerCount ? Rewired.ReInput.players.GetPlayer(p.playerID) : null;
                    BoomerangFoo.Logger.LogInfo("Powerup discard: input from Rewired player matching the player number");
                }
            }
            return rewiredOf(player);
        }

        private static void Discard(Player player)
        {
            List<PowerupType> powerups = PowerupHud.OrderedPowerups(player);
            if (powerups.Count == 0) return;

            PowerupType discarded = powerups[powerups.Count - 1];
            bool hadShield = player.IsShielded;
            int shieldHits = player.shieldHitsLeft;

            player.ClearPowerups();
            foreach (PowerupType powerup in powerups.Take(powerups.Count - 1))
            {
                player.StartPowerup(powerup);
            }

            // Giving Shield back would refill it: keep the hits it had, or keep it broken
            if ((player.activePowerup & PowerupType.Shield) != 0)
            {
                if (hadShield) player.shieldHitsLeft = shieldHits;
                else if (player.IsShielded) stopShield?.Invoke(player, null);
            }

            Singleton<AudioManager>.Instance.PlayOneShot("event:/ui/remove_point");
            BoomerangFoo.Logger.LogInfo($"P{player.playerID + 1} discarded {discarded}");
        }
    }
}
