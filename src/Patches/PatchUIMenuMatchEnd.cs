using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoomerangFoo.Patches
{
    // The winners screen has 6 award cards (uiPlayers), but ShowMatchAwards gives every player a card:
    // with 7+ players it threw IndexOutOfRange partway, so the cards and REMATCH / BACK TO MENU never appeared
    // and the game was stuck. Add cards by copying the last one, and scale the card row down to fit.
    [HarmonyPatch(typeof(UIMenuMatchEnd), "Init")]
    class UIMenuMatchEndInitPatch
    {
        private static readonly FieldInfo uiPlayersField = AccessTools.Field(typeof(UIMenuMatchEnd), "uiPlayers");
        private static int originalCards = 0;
        private static Vector3 originalRowScale = Vector3.one;

        static void Prefix(UIMenuMatchEnd __instance)
        {
            try
            {
                if (!(uiPlayersField?.GetValue(__instance) is Array cards) || cards.Length == 0) return;
                if (originalCards == 0)
                {
                    originalCards = cards.Length;
                    if (cards.GetValue(0) is Component first) originalRowScale = first.transform.parent.localScale;
                }

                int players = Singleton<GameManager>.Instance.players.Count;
                if (cards.Length < players)
                {
                    Array extended = Array.CreateInstance(uiPlayersField.FieldType.GetElementType(), players);
                    Array.Copy(cards, extended, cards.Length);
                    Component template = cards.GetValue(cards.Length - 1) as Component;
                    for (int i = cards.Length; i < players && template != null; i++)
                    {
                        GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
                        extended.SetValue(copy.GetComponent(template.GetType()), i);
                    }
                    uiPlayersField.SetValue(__instance, extended);
                    cards = extended;
                }

                // Shrink the row so more than the original number of cards still fits on screen
                if (cards.GetValue(0) is Component card)
                {
                    float scale = Math.Min(1f, (float)originalCards / Math.Max(players, 1));
                    card.transform.parent.localScale = originalRowScale * scale;
                }
            }
            catch (Exception e)
            {
                BoomerangFoo.Logger.LogError($"Winners screen: could not add award cards for more than 6 players. {e}");
            }
        }
    }
}
