using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Panel po prawej stronie z opisem klawiszologii aktywnego narzędzia budowania.
    /// Aktualizuje się automatycznie przy zmianie ToolMode/SubMode.
    /// </summary>
    public class KeyboardLegendUI : MonoBehaviour
    {
        private GameObject panel;
        private Transform contentParent;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI subtitleText;
        private TextMeshProUGUI hintText;

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged += _ => Refresh();
                DepotUIManager.Instance.OnTrackSubModeChanged += _ => Refresh();
            }
            Refresh();
        }

        private void Refresh()
        {
            if (DepotUIManager.Instance == null) { Hide(); return; }

            var tool = DepotUIManager.Instance.CurrentTool;
            var trackSub = DepotUIManager.Instance.CurrentTrackSubMode;

            if (tool == ToolMode.Select) { Hide(); return; }

            var shortcuts = GetShortcuts(tool, trackSub);
            if (shortcuts.Count == 0) { Hide(); return; }

            Show();
            PopulateShortcuts(shortcuts);
            if (titleText != null)
                titleText.text = GetLegendTitle(tool, trackSub);
            if (subtitleText != null)
                subtitleText.text = $"{GetLegendSubtitle(tool, trackSub)} • {shortcuts.Count} skrótów";
            if (hintText != null)
                hintText.text = GetLegendHint(tool, trackSub);

            // Pozycja — dostosuj do aktywnego trybu
            var rt = GetComponent<RectTransform>();
            float rightOffset = tool == ToolMode.BuildRoom ? -190f : -10f;
            float bottomOffset = tool == ToolMode.Demolish ? 70f : 120f; // Demolish nie ma subbara
            rt.anchoredPosition = new Vector2(rightOffset, bottomOffset);
        }

        private List<(string key, string desc)> GetShortcuts(ToolMode tool, TrackBuildSubMode trackSub)
        {
            var list = new List<(string, string)>();

            switch (tool)
            {
                case ToolMode.BuildTrack:
                    bool isTurnout = trackSub is TrackBuildSubMode.TurnoutR190
                        or TrackBuildSubMode.TurnoutR300
                        or TrackBuildSubMode.DoubleCrossoverR190;

                    if (isTurnout)
                    {
                        list.Add(("LMB", "Postaw rozjazd"));
                        list.Add(("R", "Obróć (lewo/prawo)"));
                        list.Add(("P", "Tryb pary"));
                        list.Add(("O", "Strona pary (auto/L/P)"));
                        list.Add(("U", "Odgałęzienie z powrotem"));
                        list.Add(("Ctrl+Scroll", "Obróć wolnostojący"));
                    }
                    else
                    {
                        list.Add(("LMB", "Rozpocznij / Zakończ tor"));
                    }
                    list.Add(("Del", "Usuń najbliższy tor"));
                    list.Add(("Ctrl+Z", "Cofnij"));
                    list.Add(("ESC", "Anuluj"));
                    break;

                case ToolMode.BuildCatenary:
                    list.Add(("LMB drag", "Maluj sieć trakcyjną"));
                    list.Add(("Del", "Usuń sieć z toru"));
                    list.Add(("Ctrl+Z", "Cofnij"));
                    list.Add(("ESC", "Anuluj"));
                    break;

                case ToolMode.BuildPath:
                    var pathSub = DepotUIManager.Instance.CurrentPathSubMode;
                    if (pathSub == PathBuildSubMode.Parking)
                    {
                        list.Add(("LMB", "Narożnik A → Narożnik B"));
                        list.Add(("PPM", "Anuluj"));
                    }
                    else
                    {
                        list.Add(("LMB drag", "Maluj ścieżkę/drogę"));
                        list.Add(("LMB", "Potwierdź podgląd"));
                    }
                    list.Add(("Ctrl+Z", "Cofnij"));
                    list.Add(("ESC", "Anuluj"));
                    break;

                case ToolMode.BuildRoom:
                    list.Add(("LMB", "Narożnik A → Narożnik B"));
                    list.Add(("Del", "Usuń wybraną ścianę"));
                    list.Add(("Ctrl+Z", "Cofnij"));
                    list.Add(("PPM / ESC", "Anuluj"));
                    break;

                case ToolMode.Demolish:
                    list.Add(("LMB", "Wyburz obiekt"));
                    list.Add(("ESC", "Wróć do selekcji"));
                    break;

                case ToolMode.Furniture:
                    list.Add(("LMB", "Postaw mebel (MF-4)"));
                    list.Add(("R", "Obróc 90° (MF-4)"));
                    list.Add(("ESC", "Anuluj"));
                    break;
            }

            return list;
        }

        private static string GetLegendTitle(ToolMode tool, TrackBuildSubMode trackSub)
        {
            return tool switch
            {
                ToolMode.BuildTrack when trackSub is TrackBuildSubMode.TurnoutR190 or TrackBuildSubMode.TurnoutR300 or TrackBuildSubMode.DoubleCrossoverR190
                    => "Rozjazdy",
                ToolMode.BuildTrack => "Budowa toru",
                ToolMode.BuildCatenary => "Siec trakcyjna",
                ToolMode.BuildPath => "Sciezki i drogi",
                ToolMode.BuildRoom => "Budowa pomieszczen",
                ToolMode.Furniture => "Meble",
                ToolMode.Demolish => "Wyburzanie",
                _ => "Skroty"
            };
        }

        private static string GetLegendSubtitle(ToolMode tool, TrackBuildSubMode trackSub)
        {
            return tool switch
            {
                ToolMode.BuildTrack when trackSub is TrackBuildSubMode.TurnoutR190 or TrackBuildSubMode.TurnoutR300 or TrackBuildSubMode.DoubleCrossoverR190
                    => "Układanie rozjazdów",
                ToolMode.BuildTrack => "Budowa infrastruktury",
                ToolMode.BuildCatenary => "Praca na sieci",
                ToolMode.BuildPath => "Wyznaczanie przejść",
                ToolMode.BuildRoom => "Planowanie pomieszczeń",
                ToolMode.Furniture => "Wyposażenie wnętrz",
                ToolMode.Demolish => "Tryb porządkowania",
                _ => "Aktywne narzędzie"
            };
        }

        private static string GetLegendHint(ToolMode tool, TrackBuildSubMode trackSub)
        {
            return tool switch
            {
                ToolMode.BuildTrack when trackSub is TrackBuildSubMode.TurnoutR190 or TrackBuildSubMode.TurnoutR300 or TrackBuildSubMode.DoubleCrossoverR190
                    => "Najpierw ustaw geometrię rozjazdu, potem dopnij warianty pary i odgałęzienia.",
                ToolMode.BuildTrack => "Buduj odcinki krok po kroku; ESC anuluje, a Ctrl+Z cofa ostatnią akcję.",
                ToolMode.BuildCatenary => "Maluj sieć po istniejących torach i kontroluj szybko korekty klawiszem Del.",
                ToolMode.BuildPath => "Najpierw wyznacz przebieg, potem potwierdź gotowy układ ścieżki lub drogi.",
                ToolMode.BuildRoom => "Wyznacz dwa narożniki pokoju i pilnuj proporcji przed zatwierdzeniem układu.",
                ToolMode.Furniture => "Wybierz mebel z biblioteki po lewej i ustaw go w pomieszczeniu odpowiedniego typu.",
                ToolMode.Demolish => "Usuwaj ostrożnie tylko aktywne elementy; ESC szybko wraca do selekcji.",
                _ => "Skorzystaj z podpowiedzi skrótów, żeby utrzymać płynne tempo budowania."
            };
        }

        private static Color GetKeyBadgeColor(string key)
        {
            if (key.Contains("ESC") || key.Contains("Del") || key.Contains("PPM"))
                return UITheme.Danger;
            if (key.Contains("Ctrl"))
                return UITheme.Warning;
            if (key.Contains("LMB"))
                return UITheme.PrimaryAccent;
            return UITheme.PrimarySurface;
        }

        // ─────────────────────────────────────────────
        //  PROCEDURAL UI
        // ─────────────────────────────────────────────

        private void BuildUI()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            // Prawy dół, nad BuildMenu (64) + SubToolbar (50) = 114px od dołu
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-10, 120);
            rt.sizeDelta = new Vector2(264, 10); // height auto-resized

            Image bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.94f), UIShapePreset.PanelLarge);

            var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var csf = gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            header.transform.SetParent(transform, false);
            UITheme.ApplySurface(header.GetComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.96f), UIShapePreset.Inset);
            header.GetComponent<LayoutElement>().preferredHeight = 46f;

            var headerLayout = header.GetComponent<VerticalLayoutGroup>();
            headerLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            headerLayout.spacing = UITheme.Spacing.Xxs;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            var titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(titleText, UIThemeTextRole.Primary);
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.text = "Skroty";

            var subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(subtitleText, UIThemeTextRole.Secondary);
            subtitleText.fontSize = 10;
            subtitleText.alignment = TextAlignmentOptions.MidlineLeft;
            subtitleText.text = "Aktywne narzędzie • 0 skrótów";

            var hintCard = new GameObject("HintCard", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            hintCard.transform.SetParent(transform, false);
            UITheme.ApplySurface(hintCard.GetComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.88f), UIShapePreset.Inset);
            hintCard.GetComponent<LayoutElement>().preferredHeight = 52f;

            var hintLayout = hintCard.GetComponent<VerticalLayoutGroup>();
            hintLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hintLayout.spacing = 0;
            hintLayout.childForceExpandWidth = true;
            hintLayout.childForceExpandHeight = false;
            hintLayout.childControlWidth = true;
            hintLayout.childControlHeight = true;

            var hintObj = new GameObject("Hint", typeof(RectTransform));
            hintObj.transform.SetParent(hintCard.transform, false);
            hintText = hintObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(hintText, UIThemeTextRole.Secondary);
            hintText.fontSize = 10;
            hintText.alignment = TextAlignmentOptions.TopLeft;
            hintText.textWrappingMode = TextWrappingModes.Normal;
            hintText.overflowMode = TextOverflowModes.Overflow;
            hintText.text = "Wybierz aktywne narzędzie, aby zobaczyć najlepsze skróty do bieżącej pracy.";

            var contentRoot = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentRoot.transform.SetParent(transform, false);
            contentRoot.AddComponent<LayoutElement>().preferredHeight = 10f;

            var contentLayout = contentRoot.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = UITheme.Spacing.Xs;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.padding = new RectOffset(0, 0, 0, 0);

            contentRoot.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            contentParent = contentRoot.transform;
            panel = gameObject;
        }

        private void PopulateShortcuts(List<(string key, string desc)> shortcuts)
        {
            // Wyczyść stare
            for (int i = contentParent.childCount - 1; i >= 0; i--)
                Destroy(contentParent.GetChild(i).gameObject);

            foreach (var (key, desc) in shortcuts)
            {
                var row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(contentParent, false);
                UITheme.ApplySurface(row.GetComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.78f), UIShapePreset.Inset);
                row.GetComponent<LayoutElement>().preferredHeight = 32;
                var hlg = row.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = UITheme.Spacing.Md;
                hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childAlignment = TextAnchor.MiddleLeft;

                // Key badge
                var keyObj = new GameObject("Key", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                keyObj.transform.SetParent(row.transform, false);
                UITheme.ApplySurface(keyObj.GetComponent<Image>(), GetKeyBadgeColor(key), UIShapePreset.Pill);
                keyObj.GetComponent<LayoutElement>().preferredWidth = Mathf.Max(50, key.Length * 10 + 16);
                keyObj.GetComponent<LayoutElement>().preferredHeight = 20;

                var keyText = new GameObject("Label", typeof(RectTransform));
                keyText.transform.SetParent(keyObj.transform, false);
                var krt = keyText.GetComponent<RectTransform>();
                krt.anchorMin = Vector2.zero;
                krt.anchorMax = Vector2.one;
                krt.offsetMin = new Vector2(4, 0);
                krt.offsetMax = new Vector2(-4, 0);
                var kt = keyText.AddComponent<TextMeshProUGUI>();
                UITheme.ApplyTmpText(kt, UIThemeTextRole.Inverse);
                kt.text = key;
                kt.fontSize = 11;
                kt.fontStyle = FontStyles.Bold;
                kt.alignment = TextAlignmentOptions.Center;

                // Description
                var descObj = new GameObject("Desc", typeof(RectTransform), typeof(LayoutElement));
                descObj.transform.SetParent(row.transform, false);
                descObj.GetComponent<LayoutElement>().flexibleWidth = 1;
                var dt = descObj.AddComponent<TextMeshProUGUI>();
                UITheme.ApplyTmpText(dt, UIThemeTextRole.Secondary);
                dt.text = desc;
                dt.fontSize = 11;
                dt.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        private void Show() => panel.SetActive(true);
        private void Hide() => panel.SetActive(false);
    }
}
