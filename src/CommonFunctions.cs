using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BoomerangFoo
{
    public static class CommonFunctions
    {
        public static List<PowerupType> GetEnumPowerUpValues(PowerupType powerups)
        {
            return (from PowerupType ps in Enum.GetValues(typeof(PowerupType))
                    where powerups.HasFlag(ps)
                    select ps).ToList();
        }

        public static Player GetActorAsPlayer(Actor actor)
        {
            return (actor is Player) ? (actor as Player) : null;
        }

        public static PlayerState GetPlayerState(Player player)
        {
            return player.GetComponent<PlayerState>();
        }

        // Player has no documented bot flag, so find a PlayerType member once by reflection
        private static readonly MemberInfo playerTypeMember = FindPlayerTypeMember();
        private static bool loggedBotDetection = false;

        private static MemberInfo FindPlayerTypeMember()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = typeof(Player); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetFields(flags).FirstOrDefault(f => f.FieldType == typeof(PlayerType));
                if (field != null) return field;
                PropertyInfo property = type.GetProperties(flags).FirstOrDefault(p => p.PropertyType == typeof(PlayerType) && p.CanRead && p.GetIndexParameters().Length == 0);
                if (property != null) return property;
            }
            return null;
        }

        public static bool IsBot(Player player)
        {
            if (player == null) return false;

            if (!loggedBotDetection)
            {
                loggedBotDetection = true;
                string method = playerTypeMember != null ? $"Player.{playerTypeMember.Name}" : "AI component lookup";
                BoomerangFoo.Logger.LogInfo($"Detecting bots using {method}");
            }

            if (playerTypeMember is FieldInfo field)
            {
                return (PlayerType)field.GetValue(player) == PlayerType.Bot;
            }
            if (playerTypeMember is PropertyInfo property)
            {
                return (PlayerType)property.GetValue(player) == PlayerType.Bot;
            }
            // Fallback: bots are driven by an AI component (e.g. PlayerAI)
            return player.GetComponents<MonoBehaviour>().Any(c => c != null && c.GetType().Name.Contains("AI"));
        }

        public static PowerupType PreviousActive(List<PowerupType> powerupHistory)
        {
            PowerupType previous = PowerupType.None;
            for (int i = 0; i < powerupHistory.Count - 1; i++)
            {
                previous |= powerupHistory[i];
            }
            return previous;
        }
    }
}