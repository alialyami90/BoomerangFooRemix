using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoomerangFoo.UI
{
    // A player's character portrait, human or bot, in any mode: the same face the score screen shows,
    // stored on the character as scoresSpriteIdle (lobbySpriteIdle as a fallback).
    // Read by reflection since the fields may not be public.
    public static class CharacterFaces
    {
        private static readonly FieldInfo scoresSprite = AccessTools.Field(typeof(Character), "scoresSpriteIdle");
        private static readonly FieldInfo lobbySprite = AccessTools.Field(typeof(Character), "lobbySpriteIdle");
        private static bool failed = false;

        public static Sprite Get(Player player)
        {
            if (failed || player == null || player.character == null) return null;
            try
            {
                Sprite face = scoresSprite?.GetValue(player.character) as Sprite;
                if (face == null) face = lobbySprite?.GetValue(player.character) as Sprite;
                return face;
            }
            catch (Exception e)
            {
                failed = true;
                BoomerangFoo.Logger.LogWarning($"Character faces: lookup failed, using names. {e.Message}");
                return null;
            }
        }
    }
}
