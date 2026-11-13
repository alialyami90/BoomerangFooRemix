using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BoomerangFoo.GameModes;
using BoomerangFoo.Patches;
using BoomerangFoo.Stats;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoomerangFoo.UI
{
    // Stats table under the round-end score bars, styled like them (player-coloured rounded bars, game font,
    // P1/BOT badges, character faces). It lives on its own overlay canvas, so the game's score screen is never modified.
    public static class MatchStatsUI
    {
        private const float Gap = 16f;
        private const float BottomMargin = 24f;
        private const float RowSpacing = 6f;
        private const float MaxRowHeight = 48f;
        private const float MinRowHeight = 24f;
        // Headers get a full row so long column names can wrap onto two lines
        private const float HeaderShare = 1f;
        private const float SectionGap = 14f;
        // Team rows show the first columns only: kills, deaths, K/D and fell / self
        private const int TeamColumnCount = 4;
        // Keeps the table between SAVE REPLAY and CONTINUE
        private const float MaxWidthShare = 0.56f;
        private const float BadgeWeight = 0.09f;
        // On the winners screen the table starts this far up from the bottom of the screen
        private const float WinnersTableTop = 0.45f;

        private enum Stat { Kills, Deaths, KillDeath, Self, Clean, Nemesis, Victim }

        private static readonly (Stat stat, string term, string fallback, float weight)[] Columns =
        [
            (Stat.Kills, "MatchStatsKills", "KILLS", 0.10f),
            (Stat.Deaths, "MatchStatsDeaths", "DEATHS", 0.10f),
            (Stat.KillDeath, "MatchStatsKD", "K/D", 0.09f),
            (Stat.Self, "MatchStatsSelf", "FELL / SELF", 0.12f),
            (Stat.Clean, "MatchStatsClean", "ROUNDS SURVIVED", 0.16f),
            (Stat.Nemesis, "MatchStatsNemesis", "KILLED YOU MOST", 0.17f),
            (Stat.Victim, "MatchStatsVictim", "YOU KILLED MOST", 0.17f),
        ];

        private class Entry
        {
            public Player player;          // set for human rows
            public string label;           // TEAM RED, P1, ...
            public UIScoreRow scoreRow;    // set for team rows: the team's score bar
            public Sprite barSprite;
            public Color color;
            public int kills, deaths, self, clean;
            public (int playerId, int count)? nemesis, victim;
        }

        private class Section
        {
            public string term, fallback;
            public List<Entry> entries;
            public List<(Stat stat, string term, string fallback, float weight)> columns;
        }

        private static readonly Vector3[] corners = new Vector3[4];
        private static Canvas canvas;
        private static RectTransform tableRoot;
        // The menu the table belongs to (score screen or winners screen); it hides when that menu closes
        private static UIMenuBase currentMenu;
        // Score screen the rows' colours and bar shape come from; still used on the winners screen
        private static UIMenuRoundEnd scoreMenu;
        private static bool onWinnersScreen = false;
        private static Rect lastBars;
        private static bool loggedNoRoom = false;

        public static void Activate()
        {
            PatchGameManager.OnPostUpdate += Tick;
        }

        public static void Show(UIMenuRoundEnd menu)
        {
            scoreMenu = menu;
            currentMenu = menu;
            onWinnersScreen = false;
            Layout(force: true);
        }

        // The winners screen has no score bars, so the table sits in the lower part of the screen
        public static void ShowOnWinnersScreen(UIMenuBase menu)
        {
            if (scoreMenu == null)
            {
                scoreMenu = Resources.FindObjectsOfTypeAll<UIMenuRoundEnd>().FirstOrDefault(roundEnd => roundEnd.gameObject.scene.IsValid());
            }
            if (scoreMenu == null) return;

            currentMenu = menu;
            onWinnersScreen = true;
            Layout(force: true);
        }

        // Hides the table with the score screen, and follows the score bars while they slide into place
        private static void Tick(GameManager gameManager)
        {
            try
            {
                if (currentMenu == null || !currentMenu.gameObject.activeInHierarchy)
                {
                    currentMenu = null;
                    Hide();
                    return;
                }
                Layout(force: false);
            }
            catch (Exception e)
            {
                BoomerangFoo.Logger.LogError($"Match stats table failed: {e}");
                currentMenu = null;
                Hide();
            }
        }

        private static void Hide()
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
        }

        private static void Layout(bool force)
        {
            if (!(GameMode.selected?.gameSettings?.ShowMatchStats ?? true))
            {
                Hide();
                return;
            }

            EnsureCanvas();
            Rect? bars = onWinnersScreen ? WinnersScreenAnchor() : BarsRect(scoreMenu);
            if (bars == null)
            {
                Hide();
                return;
            }
            if (!force && Mathf.Abs(bars.Value.yMin - lastBars.yMin) < 1f && Mathf.Abs(bars.Value.center.x - lastBars.center.x) < 1f) return;

            lastBars = bars.Value;
            canvas.gameObject.SetActive(true);
            Build(scoreMenu, bars.Value);
        }

        private static void EnsureCanvas()
        {
            if (canvas != null) return;

            var canvasObject = new GameObject("BFooMatchStats");
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the score screen
            canvas.sortingOrder = 1000;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var root = new GameObject("Table", typeof(RectTransform));
            tableRoot = (RectTransform)root.transform;
            tableRoot.SetParent(canvasObject.transform, false);
            tableRoot.anchorMin = new Vector2(0.5f, 0.5f);
            tableRoot.anchorMax = new Vector2(0.5f, 0.5f);
            tableRoot.pivot = new Vector2(0.5f, 1f);
        }

        // A zero-height strip whose bottom is where the table's top should be
        private static Rect WinnersScreenAnchor()
        {
            Rect canvasRect = ((RectTransform)canvas.transform).rect;
            float width = canvasRect.width * MaxWidthShare;
            float top = canvasRect.yMin + canvasRect.height * WinnersTableTop + Gap;
            return Rect.MinMaxRect(-width / 2f, top, width / 2f, top);
        }

        // Bounds of the visible score rows in this canvas's units (origin at the screen centre)
        private static Rect? BarsRect(UIMenuRoundEnd menu)
        {
            Canvas menuCanvas = menu.GetComponentInParent<Canvas>();
            Camera camera = menuCanvas == null || menuCanvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : menuCanvas.rootCanvas.worldCamera;
            var canvasRect = (RectTransform)canvas.transform;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool any = false;
            foreach (UIScoreRow row in menu.uiScoreRows)
            {
                if (row == null || !row.gameObject.activeInHierarchy) continue;
                ((RectTransform)row.transform).GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
                    minX = Mathf.Min(minX, local.x);
                    maxX = Mathf.Max(maxX, local.x);
                    minY = Mathf.Min(minY, local.y);
                    maxY = Mathf.Max(maxY, local.y);
                    any = true;
                }
            }
            return any ? Rect.MinMaxRect(minX, minY, maxX, maxY) : null;
        }

        private static void Build(UIMenuRoundEnd menu, Rect bars)
        {
            foreach (Transform child in tableRoot.Cast<Transform>().ToList())
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            TMP_FontAsset font = FindFont(menu);
            if (font == null)
            {
                BoomerangFoo.Logger.LogWarning("Match stats: no TextMeshPro font found, not showing stats.");
                return;
            }

            GameManager gameManager = Singleton<GameManager>.Instance;
            bool teamMatch = Singleton<SettingsManager>.Instance.teamMatch;
            List<UIScoreRow> activeRows = menu.uiScoreRows.Where(row => row != null && row.gameObject.activeSelf).ToList();

            // Teams mode: a table of teams, then the human players. Other modes: human players only.
            List<Section> sections = [];
            if (teamMatch)
            {
                List<Entry> teams = TeamEntries(gameManager, activeRows);
                if (teams.Count > 0) sections.Add(new Section { term = "MatchStatsTeams", fallback = "TEAMS", entries = teams, columns = Columns.Take(TeamColumnCount).ToList() });
            }
            List<Entry> humans = HumanEntries(gameManager, activeRows);
            if (humans.Count > 0) sections.Add(new Section { term = "MatchStatsPlayers", fallback = "PLAYERS", entries = humans, columns = Columns.ToList() });
            if (sections.Count == 0) return;

            // Fit everything into the space under the score bars; when cramped, keep the top players by kills
            var canvasRect = ((RectTransform)canvas.transform).rect;
            float width = Mathf.Min(bars.width, canvasRect.width * MaxWidthShare);
            float top = bars.yMin - Gap;
            float available = top - (canvasRect.yMin + BottomMargin);
            float rowHeight = RowHeightFor(sections, available);
            Section players = sections.LastOrDefault(section => section.term == "MatchStatsPlayers");
            while (rowHeight < MinRowHeight && players != null && players.entries.Count > 1)
            {
                players.entries = players.entries.OrderByDescending(entry => entry.kills).Take(players.entries.Count - 1).ToList();
                rowHeight = RowHeightFor(sections, available);
            }
            if (rowHeight < MinRowHeight)
            {
                if (!loggedNoRoom)
                {
                    loggedNoRoom = true;
                    BoomerangFoo.Logger.LogInfo("Match stats: no room under the score bars, not showing the table.");
                }
                return;
            }
            rowHeight = Mathf.Min(rowHeight, MaxRowHeight);
            float headerHeight = rowHeight * HeaderShare;

            float y = 0f;
            foreach (Section section in sections)
            {
                if (y > 0f) y += SectionGap;
                y = BuildSection(menu, gameManager, font, section, teamMatch, y, rowHeight, headerHeight);
            }
            tableRoot.anchoredPosition = new Vector2(0f, top);
            tableRoot.sizeDelta = new Vector2(width, y);
        }

        // Header plus one bar per entry; returns the y below the section
        private static float BuildSection(UIMenuRoundEnd menu, GameManager gameManager, TMP_FontAsset font, Section section, bool teamMatch, float y, float rowHeight, float headerHeight)
        {
            var columns = section.columns;
            float totalWeight = BadgeWeight + columns.Sum(column => column.weight);
            float[] starts = new float[columns.Count];
            float cursor = BadgeWeight / totalWeight;
            for (int i = 0; i < columns.Count; i++)
            {
                starts[i] = cursor;
                cursor += columns[i].weight / totalWeight;
            }

            RectTransform titleCell = Cell(tableRoot, "Title", 0f, BadgeWeight / totalWeight, y, headerHeight);
            TextMeshProUGUI title = CreateHeaderText(titleCell, font, Term(section.term, section.fallback), headerHeight);
            if (title != null) title.alignment = TextAlignmentOptions.BottomLeft;
            for (int i = 0; i < columns.Count; i++)
            {
                RectTransform cell = Cell(tableRoot, "Header", starts[i], columns[i].weight / totalWeight, y, headerHeight);
                CreateHeaderText(cell, font, Term(columns[i].term, columns[i].fallback), headerHeight);
            }
            y += headerHeight;

            foreach (Entry entry in section.entries)
            {
                RectTransform rowRect = Cell(tableRoot, "Row", 0f, 1f, y, rowHeight);
                StyleRow(rowRect, entry, rowHeight);

                RectTransform badgeCell = Cell(rowRect, "Badge", 0f, BadgeWeight / totalWeight, 0f, rowHeight);
                CreateBadge(badgeCell, font, entry, rowHeight);

                for (int i = 0; i < columns.Count; i++)
                {
                    RectTransform cell = Cell(rowRect, columns[i].stat.ToString(), starts[i], columns[i].weight / totalWeight, 0f, rowHeight);
                    switch (columns[i].stat)
                    {
                        case Stat.Nemesis: CreateWho(cell, font, menu, gameManager, entry.nemesis, teamMatch, rowHeight); break;
                        case Stat.Victim: CreateWho(cell, font, menu, gameManager, entry.victim, teamMatch, rowHeight); break;
                        default: CreateText(cell, font, Value(entry, columns[i].stat), rowHeight * 0.55f); break;
                    }
                }
                y += rowHeight + RowSpacing;
            }
            return y;
        }

        private static float RowHeightFor(List<Section> sections, float available)
        {
            int rows = sections.Sum(section => section.entries.Count);
            float fixedSpace = RowSpacing * rows + SectionGap * (sections.Count - 1);
            return (available - fixedSpace) / (rows + HeaderShare * sections.Count);
        }

        // One entry per team, in team order, matched to the visible score bars in the same order
        private static List<Entry> TeamEntries(GameManager gameManager, List<UIScoreRow> activeRows)
        {
            List<Entry> entries = [];
            var teams = gameManager.players.Where(player => player != null).GroupBy(player => player.team).OrderBy(team => team.Key).ToList();
            for (int i = 0; i < teams.Count; i++)
            {
                var stats = teams[i].Select(player => MatchStats.Get(player.playerID)).ToList();
                UIScoreRow scoreRow = i < activeRows.Count ? activeRows[i] : null;
                Entry entry = new Entry
                {
                    scoreRow = scoreRow,
                    kills = stats.Sum(s => s.Kills),
                    deaths = stats.Sum(s => s.Deaths),
                    self = stats.Sum(s => s.SelfEliminations),
                    clean = stats.Sum(s => s.CleanRounds),
                };
                ApplyBarStyle(entry, scoreRow, null);
                entry.label = TeamName(entry.color, i + 1);
                entries.Add(entry);
            }
            return entries;
        }

        private static List<Entry> HumanEntries(GameManager gameManager, List<UIScoreRow> activeRows)
        {
            List<Entry> entries = [];
            foreach (Player player in gameManager.players.Where(player => player != null && !CommonFunctions.IsBot(player)).OrderBy(player => player.playerID))
            {
                PlayerMatchStats stats = MatchStats.Get(player.playerID);
                Entry entry = new Entry
                {
                    player = player,
                    kills = stats.Kills,
                    deaths = stats.Deaths,
                    self = stats.SelfEliminations,
                    clean = stats.CleanRounds,
                    nemesis = stats.Nemesis,
                    victim = stats.FavoriteVictim,
                };
                Color? color = player.character != null ? player.character.Color : (Color?)null;
                ApplyBarStyle(entry, activeRows.FirstOrDefault(), color);
                entry.label = $"P{player.playerID + 1}";
                entries.Add(entry);
            }
            return entries;
        }

        // Same rounded bar as the score screen; the colour is the bar's own unless one is given
        private static void ApplyBarStyle(Entry entry, UIScoreRow scoreRow, Color? color)
        {
            Image bar = scoreRow == null ? null : scoreRow.GetComponentsInChildren<Image>()
                .Where(image => image.sprite != null && image.color.a > 0.2f)
                .OrderByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height)
                .FirstOrDefault();
            entry.barSprite = bar != null ? bar.sprite : null;
            entry.color = color ?? (bar != null ? bar.color : new Color(0f, 0f, 0f, 0.5f));
        }

        private static string Value(Entry entry, Stat stat)
        {
            switch (stat)
            {
                case Stat.Kills: return entry.kills.ToString();
                case Stat.Deaths: return entry.deaths.ToString();
                case Stat.KillDeath:
                    float ratio = entry.deaths == 0 ? entry.kills : (float)entry.kills / entry.deaths;
                    return ratio.ToString("0.##", CultureInfo.InvariantCulture);
                case Stat.Self: return entry.self.ToString();
                case Stat.Clean: return entry.clean.ToString();
                default: return "";
            }
        }

        // A horizontal slice of the parent: x and width as fractions, y down from the parent's top in canvas units
        private static RectTransform Cell(RectTransform parent, string name, float xStart, float xWidth, float yFromTop, float height)
        {
            var cellObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)cellObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(xStart, 1f);
            rect.anchorMax = new Vector2(xStart + xWidth, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -yFromTop);
            rect.sizeDelta = new Vector2(0f, height);
            return rect;
        }

        private static void StyleRow(RectTransform rowRect, Entry entry, float rowHeight)
        {
            var background = rowRect.gameObject.AddComponent<Image>();
            background.raycastTarget = false;
            background.color = entry.color;
            if (entry.barSprite == null) return;

            background.sprite = entry.barSprite;
            Vector4 border = entry.barSprite.border;
            background.type = border != Vector4.zero && border.y + border.w <= rowHeight ? Image.Type.Sliced : Image.Type.Simple;
        }

        // The row's name (P1, TEAM RED) on a darker pill of the row's own bar, like the game's badges.
        // The game's own badge and the team row's portrait can't be reused: in team matches the badge is hidden
        // and the portrait is a character, not the team.
        private static void CreateBadge(RectTransform cell, TMP_FontAsset font, Entry entry, float rowHeight)
        {
            var pillObject = new GameObject("Pill", typeof(RectTransform));
            var pill = (RectTransform)pillObject.transform;
            pill.SetParent(cell, false);
            pill.anchorMin = Vector2.zero;
            pill.anchorMax = Vector2.one;
            pill.offsetMin = new Vector2(6f, rowHeight * 0.12f);
            pill.offsetMax = new Vector2(-6f, -rowHeight * 0.12f);

            var image = pillObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.Lerp(entry.color, Color.black, 0.3f);
            if (entry.barSprite != null)
            {
                image.sprite = entry.barSprite;
                Vector4 border = entry.barSprite.border;
                image.type = border != Vector4.zero && border.y + border.w <= rowHeight * 0.76f ? Image.Type.Sliced : Image.Type.Simple;
            }

            CreateText(pill, font, entry.label ?? "", rowHeight * 0.5f);
        }

        // Teams are named after their bar colour
        private static string TeamName(Color color, int number)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            string colorName;
            float degrees = hue * 360f;
            if (saturation < 0.2f) colorName = value > 0.5f ? "White" : "Grey";
            else if (degrees < 15f || degrees >= 345f) colorName = "Red";
            else if (degrees < 45f) colorName = "Orange";
            else if (degrees < 70f) colorName = "Yellow";
            else if (degrees < 170f) colorName = "Green";
            else if (degrees < 260f) colorName = "Blue";
            else if (degrees < 290f) colorName = "Purple";
            else colorName = "Pink";
            return Term($"MatchStatsTeam{colorName}", $"TEAM {colorName.ToUpperInvariant()}");
        }

        // The other player's character face plus "x2". In free-for-all the face comes from their score bar; team score
        // bars don't show players, so otherwise it's their portrait from the character select grid, or failing that
        // their name in their colour.
        private static void CreateWho(RectTransform cell, TMP_FontAsset font, UIMenuRoundEnd menu, GameManager gameManager, (int playerId, int count)? who, bool teamMatch, float rowHeight)
        {
            if (who == null)
            {
                CreateText(cell, font, "-", rowHeight * 0.55f);
                return;
            }

            int playerId = who.Value.playerId;
            Player other = gameManager.players.FirstOrDefault(player => player != null && player.playerID == playerId);
            string count = $"x{who.Value.count}";
            Sprite face = null;
            if (other != null && !teamMatch)
            {
                int otherRow = gameManager.GetRowID(other);
                if (otherRow >= 0 && otherRow < menu.uiScoreRows.Length && menu.uiScoreRows[otherRow] != null && menu.uiScoreRows[otherRow].characterIconSprite != null)
                {
                    face = menu.uiScoreRows[otherRow].characterIconSprite.sprite;
                }
            }

            if (face == null && other != null)
            {
                face = CharacterFaces.Get(other);
            }

            if (face == null)
            {
                string name = other != null && CommonFunctions.IsBot(other) ? "BOT" : $"P{playerId + 1}";
                Color color = other != null && other.character != null ? other.character.Color : Color.white;
                CreateText(cell, font, $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{name}</color> {count}", rowHeight * 0.55f);
                return;
            }

            float size = rowHeight * 0.8f;
            var faceObject = new GameObject("Face", typeof(RectTransform));
            var faceRect = (RectTransform)faceObject.transform;
            faceRect.SetParent(cell, false);
            faceRect.anchorMin = new Vector2(0.5f, 0.5f);
            faceRect.anchorMax = new Vector2(0.5f, 0.5f);
            faceRect.pivot = new Vector2(1f, 0.5f);
            faceRect.anchoredPosition = new Vector2(-2f, 0f);
            faceRect.sizeDelta = new Vector2(size, size);
            var image = faceObject.AddComponent<Image>();
            image.sprite = face;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var countObject = new GameObject("Count", typeof(RectTransform));
            var countRect = (RectTransform)countObject.transform;
            countRect.SetParent(cell, false);
            countRect.anchorMin = new Vector2(0.5f, 0f);
            countRect.anchorMax = new Vector2(1f, 1f);
            countRect.offsetMin = new Vector2(2f, 0f);
            countRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = CreateText(countRect, font, count, rowHeight * 0.55f);
            if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // Column headers share one size and wrap onto two lines instead of shrinking into each other
        private static TextMeshProUGUI CreateHeaderText(RectTransform cell, TMP_FontAsset font, string text, float headerHeight)
        {
            TextMeshProUGUI label = CreateText(cell, font, text, headerHeight * 0.38f);
            if (label == null) return null;
            label.enableAutoSizing = false;
            label.fontSize = headerHeight * 0.38f;
            label.enableWordWrapping = true;
            label.alignment = TextAlignmentOptions.Bottom;
            label.rectTransform.offsetMin = new Vector2(4f, 2f);
            label.rectTransform.offsetMax = new Vector2(-4f, 0f);
            return label;
        }

        private static TextMeshProUGUI CreateText(RectTransform rect, TMP_FontAsset font, string text, float maxSize)
        {
            var labelObject = new GameObject("Text", typeof(RectTransform));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            // Font before any styling: a label without one has no material and TextMeshPro throws
            label.font = font;
            label.text = text;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = maxSize;
            label.alignment = TextAlignmentOptions.Center;
            if (label.fontSharedMaterial != null)
            {
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(40, 40, 40, 255);
            }
            return label;
        }

        private static TMP_FontAsset FindFont(UIMenuRoundEnd menu)
        {
            TextMeshProUGUI source = menu.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(text => text.font != null)
                ?? UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>().FirstOrDefault(text => text.font != null);
            if (source != null) return source.font;
            return Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        }

        private static string Term(string term, string fallback)
        {
            string translation = LocalizationManager.GetTranslation(term);
            return string.IsNullOrEmpty(translation) ? fallback : translation;
        }
    }
}
