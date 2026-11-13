using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoomerangFoo.GameModes;
using BoomerangFoo.Patches;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoomerangFoo.UI
{
    // Corner panels showing each human player's current powerups.
    // Lives on its own overlay canvas and never touches the game's menus; on any error it logs once and hides itself.
    public static class PowerupHud
    {
        private const float Margin = 24f;
        private const float BadgeSize = 64f;
        private const float IconSize = 56f;
        private const float Gap = 8f;

        // P1 top-left, P2 top-right, P3 bottom-left, P4 bottom-right
        private static readonly Vector2[] Corners = [new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f)];

        private class Panel
        {
            public RectTransform root;
            public bool built = false;
            public int playerId = -1;
            public PowerupType shown = PowerupType.None;
        }

        private static readonly Dictionary<PowerupType, Sprite> icons = [];
        private static readonly Panel[] panels = new Panel[Corners.Length];
        private static Canvas canvas;
        private static TMP_FontAsset font;
        private static UIPlayerIcon badgeTemplate;
        private static int iconLogsLeft = 3;
        private static bool retriedIcons = false;
        private static bool failed = false;

        public static void Activate()
        {
            PatchUIMenuMatchSettings.OnPostInit += menu => CacheIcons("menu opened");
            PatchGameManager.OnPostUpdate += Update;
        }

        // The Available powerups grid in the Modifiers menu holds the game's icon for every powerup.
        // Frames, backgrounds and checkmarks use the same sprite on every button, so a powerup's icon
        // is the image whose sprite appears on that button only.
        private static void CacheIcons(string when)
        {
            if (!Modifiers.settings.TryGetValue("powerupSelections", out ModifierSetting grid)) return;

            var buttons = grid.gameObject.transform.GetChild(2).GetComponentsInChildren<UIPowerupToggle>(true)
                .Where(toggle => toggle.powerupType != PowerupType.None)
                .Select(toggle => (type: toggle.powerupType, images: toggle.GetComponentsInChildren<Image>(true).Where(image => image.sprite != null).ToList()))
                .ToList();
            Dictionary<Sprite, int> buttonsPerSprite = buttons
                .SelectMany(button => button.images.Select(image => image.sprite).Distinct())
                .GroupBy(sprite => sprite)
                .ToDictionary(group => group.Key, group => group.Count());

            icons.Clear();
            List<string> report = [];
            foreach (var (type, images) in buttons)
            {
                Image icon = images.FirstOrDefault(image => buttonsPerSprite[image.sprite] == 1);
                if (icon != null && !icons.ContainsKey(type))
                {
                    icons[type] = icon.sprite;
                }
                report.Add($"{type}: [{string.Join(", ", images.Select(image => $"{image.name}={image.sprite.name}{(image == icon ? " <icon>" : "")}"))}]");
            }

            if (iconLogsLeft > 0)
            {
                iconLogsLeft--;
                BoomerangFoo.Logger.LogInfo($"Powerup display ({when}): found icons for {icons.Count} of {buttons.Count} powerups. {string.Join(" ", report)}");
            }
        }

        private static void Update(GameManager gameManager)
        {
            if (failed) return;

            try
            {
                Refresh(gameManager);
            }
            catch (Exception e)
            {
                failed = true;
                BoomerangFoo.Logger.LogError($"Powerup display failed and was turned off: {e}");
                if (canvas != null) canvas.gameObject.SetActive(false);
            }
        }

        private static void Refresh(GameManager gameManager)
        {
            bool show = (GameMode.selected?.gameSettings?.ShowPowerupHud ?? false)
                && gameManager.gameState == GameManager.GameState.Playing;
            if (!show)
            {
                if (canvas != null) canvas.gameObject.SetActive(false);
                return;
            }

            EnsureCanvas();
            canvas.gameObject.SetActive(true);

            List<Player> humans = gameManager.players
                .Where(player => player != null && !CommonFunctions.IsBot(player))
                .OrderBy(player => player.playerID)
                .Take(panels.Length)
                .ToList();

            for (int corner = 0; corner < panels.Length; corner++)
            {
                Panel panel = panels[corner];
                if (corner >= humans.Count)
                {
                    panel.root.gameObject.SetActive(false);
                    continue;
                }

                Player player = humans[corner];
                panel.root.gameObject.SetActive(true);
                if (!panel.built || panel.playerId != player.playerID || panel.shown != player.activePowerup)
                {
                    Rebuild(panel, corner, player);
                }
            }
        }

        private static void EnsureCanvas()
        {
            if (canvas != null) return;

            var canvasObject = new GameObject("BFooPowerupHud");
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Behind the game's menus, in front of the level
            canvas.sortingOrder = -1;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            for (int corner = 0; corner < panels.Length; corner++)
            {
                var root = new GameObject($"Corner{corner}", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                rect.SetParent(canvasObject.transform, false);
                Vector2 anchor = Corners[corner];
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = anchor;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = new Vector2(anchor.x == 0f ? Margin : -Margin, anchor.y == 0f ? Margin : -Margin);
                panels[corner] = new Panel { root = rect };
            }
        }

        private static void Rebuild(Panel panel, int corner, Player player)
        {
            // The game may only fill in the grid's icons after the menu opens, so look again once in a round
            if (icons.Count == 0 && !retriedIcons)
            {
                retriedIcons = true;
                try
                {
                    CacheIcons("round started");
                    LogSpriteFields("AlertBar", player.alertBar);
                    var powerupBars = Singleton<GameManager>.Instance.powerupBars;
                    LogSpriteFields("PowerupBar", player.playerID < powerupBars.Length ? powerupBars[player.playerID] : null);
                }
                catch (Exception e)
                {
                    BoomerangFoo.Logger.LogWarning($"Powerup display: icon retry failed, using text. {e.Message}");
                }
            }

            foreach (Transform child in panel.root.Cast<Transform>().ToList())
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            float x = 0f;
            CreateBadge(panel.root, corner, x, player);
            x += BadgeSize + Gap;
            foreach (PowerupType powerup in OrderedPowerups(player))
            {
                CreateIcon(panel.root, corner, x, powerup);
                x += IconSize + Gap;
            }

            panel.built = true;
            panel.playerId = player.playerID;
            panel.shown = player.activePowerup;
        }

        // Current powerups in the order they were collected (newest last)
        internal static List<PowerupType> OrderedPowerups(Player player)
        {
            PowerupType active = player.activePowerup;
            List<PowerupType> ordered = [];
            foreach (PowerupType powerup in player.powerupHistory)
            {
                if (powerup != PowerupType.None && (active & powerup) != 0 && !ordered.Contains(powerup))
                {
                    ordered.Add(powerup);
                }
            }
            for (int bit = 0; bit < 31; bit++)
            {
                PowerupType powerup = (PowerupType)(1 << bit);
                if ((active & powerup) != 0 && !ordered.Contains(powerup))
                {
                    ordered.Add(powerup);
                }
            }
            return ordered;
        }

        // A square slot in the panel's row, x measured from the corner inward
        private static RectTransform CreateSlot(RectTransform parent, int corner, float x, float size, string name)
        {
            Vector2 anchor = Corners[corner];
            var slot = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)slot.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = new Vector2(size, size);
            float inset = (BadgeSize - size) / 2f;
            rect.anchoredPosition = new Vector2(anchor.x == 0f ? x : -x, anchor.y == 0f ? inset : -inset);
            return rect;
        }

        private static void CreateBadge(RectTransform parent, int corner, float x, Player player)
        {
            RectTransform slot = CreateSlot(parent, corner, x, BadgeSize, "Badge");

            // The game's own P1/P2 badge, as used on the score screen
            if (badgeTemplate == null)
            {
                badgeTemplate = Resources.FindObjectsOfTypeAll<UIMenuRoundEnd>()
                    .Select(menu => menu.uiScoreRows?.FirstOrDefault(row => row != null && row.playerIcon != null)?.playerIcon)
                    .FirstOrDefault(icon => icon != null);
            }
            if (badgeTemplate != null)
            {
                try
                {
                    GameObject badge = UnityEngine.Object.Instantiate(badgeTemplate.gameObject, slot, false);
                    badge.SetActive(true);
                    var rect = (RectTransform)badge.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                    badge.GetComponent<UIPlayerIcon>().SetPlayer(player.playerID, PlayerType.Human);
                    return;
                }
                catch (Exception e)
                {
                    BoomerangFoo.Logger.LogWarning($"Powerup display: could not use the game's player badge, using text instead. {e.Message}");
                    foreach (Transform child in slot.Cast<Transform>().ToList())
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
            }

            Color color = player.character != null ? player.character.Color : Color.white;
            CreateText(slot, $"P{player.playerID + 1}", color);
        }

        private static void CreateIcon(RectTransform parent, int corner, float x, PowerupType powerup)
        {
            RectTransform slot = CreateSlot(parent, corner, x, IconSize, powerup.ToString());
            if (icons.TryGetValue(powerup, out Sprite sprite) && sprite != null)
            {
                var image = slot.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }
            CreateText(slot, PowerupName(powerup), Color.white);
        }

        private static void CreateText(RectTransform slot, string text, Color color)
        {
            if (font == null)
            {
                font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
                if (font == null) return;
            }

            var label = slot.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.color = color;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 32f;
            label.alignment = TextAlignmentOptions.Center;
            // Outline needs the font's material; styling a label without one throws
            if (label.fontSharedMaterial != null)
            {
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color32(0, 0, 0, 255);
            }
        }

        // Diagnostic: shows where the game keeps powerup sprites, as another possible icon source
        private static void LogSpriteFields(string owner, object instance)
        {
            if (instance == null)
            {
                BoomerangFoo.Logger.LogInfo($"Powerup display: no {owner} to inspect");
                return;
            }

            List<string> parts = [];
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = instance.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    object value = field.GetValue(instance);
                    if (value is Sprite sprite)
                    {
                        parts.Add($"{field.Name}={(sprite != null ? sprite.name : "null")}");
                    }
                    else if (value is IEnumerable<Sprite> sprites)
                    {
                        parts.Add($"{field.Name}=[{string.Join(", ", sprites.Select(item => item != null ? item.name : "null"))}]");
                    }
                    else if (value is IEnumerable items && !(value is string))
                    {
                        Type element = field.FieldType.IsArray ? field.FieldType.GetElementType() : field.FieldType.GetGenericArguments().LastOrDefault();
                        FieldInfo spriteField = element?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(Sprite));
                        if (spriteField != null)
                        {
                            parts.Add($"{field.Name}={items.Cast<object>().Count()} x {element.Name}.{spriteField.Name}");
                        }
                    }
                }
            }
            BoomerangFoo.Logger.LogInfo($"Powerup display: {owner} sprite fields: {(parts.Count > 0 ? string.Join("; ", parts) : "none")}");
        }

        private static string PowerupName(PowerupType powerup)
        {
            string translation = LocalizationManager.GetTranslation($"powerup_{powerup.ToString().ToLower()}");
            return string.IsNullOrEmpty(translation) ? powerup.ToString() : translation;
        }
    }
}
