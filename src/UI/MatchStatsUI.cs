using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BoomerangFoo.GameModes;
using BoomerangFoo.Stats;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoomerangFoo.UI
{
    public static class MatchStatsUI
    {
        private const string LabelName = "BFooMatchStats";
        private const float Margin = 16f;
        private const float MinWidth = 120f;
        private static readonly Vector3[] corners = new Vector3[4];

        public static void Show(UIMenuRoundEnd menu)
        {
            bool enabled = GameMode.selected?.gameSettings?.ShowMatchStats ?? true;
            GameManager gameManager = Singleton<GameManager>.Instance;
            bool teamMatch = Singleton<SettingsManager>.Instance.teamMatch;
            TextMeshProUGUI fontSource = menu.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(text => text.name != LabelName);

            // Make sure the rows have their final layout before measuring them
            Canvas.ForceUpdateCanvases();

            for (int row = 0; row < menu.uiScoreRows.Length; row++)
            {
                UIScoreRow scoreRow = menu.uiScoreRows[row];
                if (scoreRow == null) continue;

                TextMeshProUGUI label = GetOrCreateLabel(scoreRow, fontSource);
                List<Player> rowPlayers = gameManager.players.Where(player => gameManager.GetRowID(player) == row).ToList();
                if (!enabled || !scoreRow.gameObject.activeInHierarchy || rowPlayers.Count == 0)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }

                label.gameObject.SetActive(true);
                label.text = teamMatch ? TeamText(rowPlayers) : PlayerText(rowPlayers[0], gameManager);
                PlaceLabel(scoreRow, label);
            }
        }

        private static TextMeshProUGUI GetOrCreateLabel(UIScoreRow scoreRow, TextMeshProUGUI fontSource)
        {
            Transform existing = scoreRow.transform.Find(LabelName);
            if (existing != null)
            {
                return existing.GetComponent<TextMeshProUGUI>();
            }

            var labelObject = new GameObject(LabelName, typeof(RectTransform));
            labelObject.transform.SetParent(scoreRow.transform, false);
            labelObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            if (fontSource != null)
            {
                label.font = fontSource.font;
            }
            label.richText = true;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 28f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(0, 0, 0, 255);
            return label;
        }

        // Puts the label just right of the row's visible content, sized to the space left before the screen edge
        private static void PlaceLabel(UIScoreRow scoreRow, TextMeshProUGUI label)
        {
            RectTransform rowRect = (RectTransform)scoreRow.transform;

            float rightEdge = float.MinValue;
            foreach (Graphic graphic in scoreRow.GetComponentsInChildren<Graphic>())
            {
                if (graphic.transform == label.transform) continue;
                graphic.rectTransform.GetWorldCorners(corners);
                rightEdge = Mathf.Max(rightEdge, rowRect.InverseTransformPoint(corners[2]).x);
            }
            if (rightEdge == float.MinValue)
            {
                rightEdge = rowRect.rect.xMax;
            }

            float width = 400f;
            Canvas canvas = scoreRow.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(corners);
                float canvasRight = rowRect.InverseTransformPoint(corners[2]).x;
                width = canvasRight - rightEdge - 2 * Margin;
            }
            width = Mathf.Max(width, MinWidth);

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = rowRect.pivot;
            labelRect.anchorMax = rowRect.pivot;
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(rightEdge + Margin, rowRect.rect.center.y);
            labelRect.sizeDelta = new Vector2(width, rowRect.rect.height);
        }

        private static string PlayerText(Player player, GameManager gameManager)
        {
            PlayerMatchStats stats = MatchStats.Get(player.playerID);
            string line1 = $"{Term("MatchStatsKills", "K")} {stats.Kills}   {Term("MatchStatsDeaths", "D")} {stats.Deaths}   " +
                $"{Term("MatchStatsKD", "K/D")} {stats.KillDeathRatio.ToString("0.00", CultureInfo.InvariantCulture)}   " +
                $"{Term("MatchStatsSelf", "Self")} {stats.SelfEliminations}   {Term("MatchStatsClean", "Clean")} {stats.CleanRounds}";
            string line2 = $"{Term("MatchStatsNemesis", "Nemesis")} {Who(stats.Nemesis, gameManager)}   " +
                $"{Term("MatchStatsVictim", "Victim")} {Who(stats.FavoriteVictim, gameManager)}";
            return $"{line1}\n{line2}";
        }

        private static string TeamText(List<Player> players)
        {
            var stats = players.Select(player => MatchStats.Get(player.playerID)).ToList();
            int kills = stats.Sum(s => s.Kills);
            int deaths = stats.Sum(s => s.Deaths);
            float ratio = deaths == 0 ? kills : (float)kills / deaths;
            return $"{Term("MatchStatsKills", "K")} {kills}   {Term("MatchStatsDeaths", "D")} {deaths}   " +
                $"{Term("MatchStatsKD", "K/D")} {ratio.ToString("0.00", CultureInfo.InvariantCulture)}   " +
                $"{Term("MatchStatsSelf", "Self")} {stats.Sum(s => s.SelfEliminations)}   {Term("MatchStatsClean", "Clean")} {stats.Sum(s => s.CleanRounds)}";
        }

        private static string Who((int playerId, int count)? entry, GameManager gameManager)
        {
            if (entry == null) return "-";

            int playerId = entry.Value.playerId;
            Player player = gameManager.players.FirstOrDefault(p => p.playerID == playerId);
            Color color = player != null && player.character != null ? player.character.Color : Color.white;
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>P{playerId + 1}</color> x{entry.Value.count}";
        }

        private static string Term(string term, string fallback)
        {
            string translation = LocalizationManager.GetTranslation(term);
            return string.IsNullOrEmpty(translation) ? fallback : translation;
        }
    }
}
