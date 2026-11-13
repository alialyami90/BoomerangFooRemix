using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace BoomerangFoo.UI
{
    // Character portraits for any player, human or bot, taken from the lobby's character select grid.
    // The game's field names aren't known, so both the player's character and each grid cell's character are found
    // as fields of the enum that lists the characters (it contains "Eggplant"); a cell's portrait is the one image
    // whose sprite no other character's cell uses. What was found is logged once.
    public static class CharacterFaces
    {
        private const string KnownCharacter = "Eggplant";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Func<Player, object> characterOf;
        private static Type characterEnum;
        private static readonly Dictionary<long, Sprite> faces = [];
        private static bool searchedPlayer = false;
        private static bool logged = false;
        private static bool failed = false;
        private static float nextCacheAttempt = 0f;

        public static Sprite Get(Player player)
        {
            if (player == null || failed) return null;
            try
            {
                if (!searchedPlayer)
                {
                    searchedPlayer = true;
                    FindPlayerCharacterField(player);
                }
                if (characterOf == null) return null;
                if (faces.Count == 0 && Time.realtimeSinceStartup >= nextCacheAttempt)
                {
                    nextCacheAttempt = Time.realtimeSinceStartup + 5f;
                    CacheFaces();
                }

                object value = characterOf(player);
                return value != null && faces.TryGetValue(Convert.ToInt64(value), out Sprite face) ? face : null;
            }
            catch (Exception e)
            {
                failed = true;
                BoomerangFoo.Logger.LogWarning($"Character faces: lookup failed, using names. {e.Message}");
                return null;
            }
        }

        private static bool IsCharacterEnum(Type type)
        {
            return type.IsEnum && Enum.GetNames(type).Contains(KnownCharacter);
        }

        private static void FindPlayerCharacterField(Player player)
        {
            FieldInfo onPlayer = typeof(Player).GetFields(Flags).FirstOrDefault(field => IsCharacterEnum(field.FieldType));
            if (onPlayer != null)
            {
                characterEnum = onPlayer.FieldType;
                characterOf = p => onPlayer.GetValue(p);
                BoomerangFoo.Logger.LogInfo($"Character faces: player character from Player.{onPlayer.Name} ({characterEnum.Name})");
                return;
            }

            if (player.character != null)
            {
                FieldInfo onCharacter = player.character.GetType().GetFields(Flags).FirstOrDefault(field => IsCharacterEnum(field.FieldType));
                if (onCharacter != null)
                {
                    characterEnum = onCharacter.FieldType;
                    characterOf = p => p.character != null ? onCharacter.GetValue(p.character) : null;
                    BoomerangFoo.Logger.LogInfo($"Character faces: player character from {player.character.GetType().Name}.{onCharacter.Name} ({characterEnum.Name})");
                    return;
                }
            }
            BoomerangFoo.Logger.LogInfo("Character faces: no character field found on Player, using names.");
        }

        private static void CacheFaces()
        {
            FieldInfo cellCharacter = typeof(UICharacterGridCell).GetFields(Flags).FirstOrDefault(field => field.FieldType == characterEnum);
            if (cellCharacter == null)
            {
                LogOnce($"Character faces: UICharacterGridCell has no {characterEnum.Name} field, using names.");
                return;
            }

            var cells = Resources.FindObjectsOfTypeAll<UICharacterGridCell>()
                .Select(cell => (character: Convert.ToInt64(cellCharacter.GetValue(cell)), images: cell.GetComponentsInChildren<Image>(true).Where(image => image.sprite != null).ToList()))
                .ToList();
            // How many different characters use each sprite: frames and backgrounds are shared, portraits aren't
            Dictionary<Sprite, int> charactersPerSprite = cells
                .SelectMany(cell => cell.images.Select(image => (cell.character, image.sprite)))
                .Distinct()
                .GroupBy(pair => pair.sprite)
                .ToDictionary(group => group.Key, group => group.Count());

            foreach (var (character, images) in cells)
            {
                if (faces.ContainsKey(character)) continue;
                Image portrait = images.FirstOrDefault(image => charactersPerSprite[image.sprite] == 1);
                if (portrait != null) faces[character] = portrait.sprite;
            }

            LogOnce($"Character faces: {faces.Count} portraits from {cells.Count} grid cells: " +
                string.Join(", ", faces.Select(kvp => $"{Enum.ToObject(characterEnum, kvp.Key)}={kvp.Value.name}")));
        }

        private static void LogOnce(string message)
        {
            if (logged) return;
            logged = true;
            BoomerangFoo.Logger.LogInfo(message);
        }
    }
}
