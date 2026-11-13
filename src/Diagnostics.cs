using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace BoomerangFoo
{
    // Writes BepInEx/BoomerangFoo-diagnostics.txt: the IL of the game's round setup coroutines and the fields of
    // the game types the mod reads by reflection. The game's code isn't available to read, so this file is how
    // problems like the 7+ player round start get diagnosed. Read-only; any failure just logs a warning.
    internal static class Diagnostics
    {
        public static void Write()
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine($"Boomerang Foo diagnostics, mod {MyPluginInfo.PLUGIN_VERSION}, {DateTime.Now:yyyy-MM-dd HH:mm}");

                foreach (string coroutine in new[] { "PrepareRound", "SpawnPlayer", "StartRoundSequence", "EndMatch" })
                {
                    DumpCoroutine(report, typeof(GameManager), coroutine);
                }

                var types = new List<Type> { typeof(Player), typeof(GameManager), typeof(UICharacterGridCell), typeof(UILobbyPlayer), typeof(UIScoreRow), typeof(UIMenuRoundEnd), typeof(UIManager) };
                FieldInfo character = typeof(Player).GetField("character");
                if (character != null) types.Add(character.FieldType);
                FieldInfo playerInput = typeof(Player).GetField("playerInput");
                if (playerInput != null) types.Add(playerInput.FieldType);
                foreach (Type type in types)
                {
                    DumpFields(report, type);
                }
                DumpEnum(report, typeof(UIMenuBase).GetNestedType("MenuType"));

                // The winners screen: its fields and every method, for the 7+ player lock-up
                DumpFields(report, typeof(UIMenuMatchEnd));
                DumpAllMethods(report, typeof(UIMenuMatchEnd));

                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "BoomerangFoo-diagnostics.txt");
                File.WriteAllText(path, report.ToString());
                BoomerangFoo.Logger.LogInfo($"Wrote diagnostics to {path}");
            }
            catch (Exception e)
            {
                BoomerangFoo.Logger.LogWarning($"Could not write diagnostics: {e.Message}");
            }
        }

        private static void DumpCoroutine(StringBuilder report, Type owner, string name)
        {
            Type stateMachine = owner.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).FirstOrDefault(type => type.Name.StartsWith($"<{name}>"));
            MethodInfo moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            report.AppendLine();
            report.AppendLine($"== IL {owner.Name}.{name} ({stateMachine?.Name ?? "not found"})");
            if (moveNext == null) return;

            int index = 0;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(moveNext))
            {
                report.AppendLine($"{index++,5}: {instruction}");
            }
        }

        // IL of every method declared on the type, plus the MoveNext of its coroutines
        private static void DumpAllMethods(StringBuilder report, Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var methods = type.GetMethods(flags).Cast<MethodBase>()
                .Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                    .Select(nested => nested.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    .Where(moveNext => moveNext != null));
            foreach (MethodBase method in methods)
            {
                if (method.IsAbstract || method.GetMethodBody() == null) continue;
                report.AppendLine();
                report.AppendLine($"== IL {method.DeclaringType.Name}.{method.Name}");
                try
                {
                    int index = 0;
                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                    {
                        report.AppendLine($"{index++,5}: {instruction}");
                    }
                }
                catch (Exception e)
                {
                    report.AppendLine($"  (could not read: {e.Message})");
                }
            }
        }

        private static void DumpFields(StringBuilder report, Type type)
        {
            report.AppendLine();
            report.AppendLine($"== Fields of {type.FullName} : {type.BaseType?.Name}");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                string values = field.FieldType.IsEnum ? $" [{string.Join(", ", Enum.GetNames(field.FieldType).Take(60))}]" : "";
                report.AppendLine($"  {(field.IsStatic ? "static " : "")}{field.FieldType.Name} {field.Name}{values}");
            }
        }

        private static void DumpEnum(StringBuilder report, Type type)
        {
            if (type == null || !type.IsEnum) return;
            report.AppendLine();
            report.AppendLine($"== Enum {type.FullName}: {string.Join(", ", Enum.GetNames(type))}");
        }
    }
}
