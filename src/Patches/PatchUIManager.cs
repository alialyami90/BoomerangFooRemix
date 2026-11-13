using System;
using System.Reflection;
using HarmonyLib;

namespace BoomerangFoo.Patches
{
    public class PatchUIManager
    {
        // Fires after the game shows a menu (round end, winners screen, ...)
        public static event Action<UIMenuBase> OnPostPushMenu;
        public static void InvokePostPushMenu(UIMenuBase menu)
        {
            // Never let a subscriber break the game's own menu flow
            try
            {
                OnPostPushMenu?.Invoke(menu);
            }
            catch (Exception e)
            {
                BoomerangFoo.Logger.LogError($"Push menu hook failed: {e}");
            }
        }
    }

    [HarmonyPatch]
    class UIManagerPushMenuPatch
    {
        // Looked up rather than named in the attribute: a missing method would stop every patch from loading
        static MethodBase TargetMethod() => AccessTools.Method(typeof(UIManager), "PushMenu", [typeof(UIMenuBase), typeof(bool)]);

        static bool Prepare()
        {
            if (TargetMethod() != null) return true;
            BoomerangFoo.Logger.LogWarning("UIManager.PushMenu not found. Stats on the winners screen are disabled.");
            return false;
        }

        static void Postfix(UIMenuBase __0)
        {
            PatchUIManager.InvokePostPushMenu(__0);
        }
    }
}
