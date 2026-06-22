using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "O grze" (Credits).
    /// </summary>
    public class CreditsScreenUI : MonoBehaviour, IMenuScreen
    {
        private GameObject root;
        private RectTransform contentRT;
        private Transform contentParent;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI backButtonText;

        public System.Action OnBack;

        public void Build(Transform canvasTransform)
        {
            BuildRoot(canvasTransform);
            BuildTopBar();
            BuildScrollArea();
            root.SetActive(false);
        }

        public void Show()
        {
            root.SetActive(true);
            PopulateCredits(contentParent);
            RefreshLanguage();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
        }
        public void Hide()  { root.SetActive(false); }
        public bool IsVisible => root != null && root.activeSelf;

        public void RefreshLanguage()
        {
            if (titleLabel != null) titleLabel.text = LocalizationService.Get("credits.title");
            backButtonText.text = "\u2190";
        }

        // === i18n hot-reload (M13-4c) ===

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
        }

        // Re-populate całość przy zmianie języka — credits ma dużo dynamicznych
        // sections/rows które są budowane w PopulateCredits, więc najprostsze
        // jest wyrzucenie i rebuilt zamiast trzymać refs do wszystkich TMP.
        private void OnLocaleChanged()
        {
            if (IsVisible)
            {
                PopulateCredits(contentParent);
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
            }
            RefreshLanguage();
        }

        // ─────────────────────────────────────────────
        //  ROOT
        // ─────────────────────────────────────────────

        private void BuildRoot(Transform parent)
        {
            root = MenuScreenPrimitives.CreateFullscreenRoot("CreditsScreen", parent);
        }

        // ─────────────────────────────────────────────
        //  TOP BAR
        // ─────────────────────────────────────────────

        private void BuildTopBar()
        {
            MenuScreenPrimitives.CreateTopBar("TopBar", root.transform, () => OnBack?.Invoke(), out backButtonText, out titleLabel);
        }

        // ─────────────────────────────────────────────
        //  SCROLL AREA
        // ─────────────────────────────────────────────

        private void BuildScrollArea()
        {
            MenuScreenPrimitives.BuildVerticalScrollArea(
                root.transform,
                offsetMin: Vector2.zero,
                offsetMax: new Vector2(0f, -80f),
                contentPadding: new RectOffset(80, 80, 40, 40),
                contentSpacing: UITheme.Spacing.Xxl,
                contentAlignment: TextAnchor.UpperCenter,
                out contentRT);
            contentParent = contentRT.transform;
        }

        private void PopulateCredits(Transform parent)
        {
            foreach (Transform child in parent)
                Destroy(child.gameObject);

            // Wspólna nazwa zespołu jako single source — łatwa podmiana przed launchem
            // (publisher name / studio rename) bez wyszukiwania w 4 miejscach.
            string teamName = LocalizationService.Get("credits.team_name");

            AddSectionHeader(parent, LocalizationService.Get("credits.section.railway_manager"));
            AddBodyText(parent, LocalizationService.Get("credits.body.railway_manager"));

            AddSpacer(parent, 10f);
            AddSectionHeader(parent, LocalizationService.Get("credits.section.developers"));
            AddCreditsRow(parent, LocalizationService.Get("credits.role.design_programming"), teamName);
            AddCreditsRow(parent, LocalizationService.Get("credits.role.graphics_ui"),         teamName);
            // Sound/music — literal "—" (TBD aż do M12b audio polish + M-Models sfx). Brak i18n key bo
            // znak placeholder'a jest sam w sobie language-neutral.
            AddCreditsRow(parent, LocalizationService.Get("credits.role.sound_music"),         "—");
            AddCreditsRow(parent, LocalizationService.Get("credits.role.translation"),         teamName);

            AddSpacer(parent, 10f);
            AddSectionHeader(parent, LocalizationService.Get("credits.section.technologies"));
            AddCreditsRow(parent, LocalizationService.Get("credits.tech.engine"),    "Unity 6");
            AddCreditsRow(parent, LocalizationService.Get("credits.tech.language"),  "C# (.NET)");
            AddCreditsRow(parent, LocalizationService.Get("credits.tech.ui"),        "Unity UI + TextMesh Pro");

            AddSpacer(parent, 10f);
            AddSectionHeader(parent, LocalizationService.Get("credits.section.version"));
            AddCreditsRow(parent, LocalizationService.Get("credits.field.build"), Application.version);
            // Build year automatycznie z bieżącego roku — set'owany przez CI/build pipeline w przyszłości,
            // tymczasowo DateTime.Now.Year żeby nie pamiętać o ręcznej aktualizacji co rok.
            AddCreditsRow(parent, LocalizationService.Get("credits.field.date"),  System.DateTime.Now.Year.ToString());

            // Atrybucja OpenStreetMap (wymóg ODbL / OSMF Attribution Guidelines — patrz
            // docs/DATA_LICENSES.md). Nota licencyjna + klikalny link do copyright/licencji.
            AddSpacer(parent, 10f);
            AddSectionHeader(parent, LocalizationService.Get("credits.section.data_sources"));
            AddBodyText(parent, LocalizationService.Get("credits.body.openstreetmap"));
            AddLinkButton(parent, LocalizationService.Get("credits.action.osm_copyright"),
                "https://www.openstreetmap.org/copyright");

            AddSpacer(parent, 20f);
            AddBodyText(parent, LocalizationService.Get("credits.body.copyright"));
        }

        // ─────────────────────────────────────────────
        //  CONTENT HELPERS
        // ─────────────────────────────────────────────

        private void AddSectionHeader(Transform parent, string text)
        {
            var card = new GameObject("SectionHeaderCard");
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            UITheme.ApplySurface(card.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Panel);
            card.AddComponent<LayoutElement>().preferredHeight = 44f;
            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var obj = MenuScreenPrimitives.CreateTMP("SectionHeader", card.transform);
            var tmp = obj.GetComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 24;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = UITheme.PrimaryAccent;
            tmp.alignment = TextAlignmentOptions.Left;
            obj.AddComponent<LayoutElement>().preferredHeight = 24f;
        }

        private void AddBodyText(Transform parent, string text)
        {
            var card = new GameObject("BodyCard");
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 0f);
            UITheme.ApplySurface(card.AddComponent<Image>(), UITheme.OverlayPanel, UIShapePreset.Panel);
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var obj = MenuScreenPrimitives.CreateTMP("Body", card.transform);
            var tmp = obj.GetComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 19;
            tmp.color     = UITheme.SecondaryText;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            obj.AddComponent<LayoutElement>().preferredHeight = 68f;
        }

        private void AddCreditsRow(Transform parent, string role, string name)
        {
            var row = new GameObject("CreditsRow");
            row.transform.SetParent(parent, false);
            var rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0f, 44f);

            var bg = row.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.OverlayPanel, UIShapePreset.Panel);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            layout.spacing  = UITheme.Spacing.Xl;
            layout.childAlignment      = TextAnchor.MiddleLeft;
            layout.childControlWidth   = false;
            layout.childControlHeight  = false;
            layout.childForceExpandWidth  = false;

            var roleShell = new GameObject("RoleShell");
            roleShell.transform.SetParent(row.transform, false);
            roleShell.AddComponent<RectTransform>().sizeDelta = new Vector2(400f, 28f);
            UITheme.ApplySurface(roleShell.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.94f), UIShapePreset.Inset);
            roleShell.AddComponent<LayoutElement>().preferredWidth = 400f;

            var roleObj = MenuScreenPrimitives.CreateTMP("Role", roleShell.transform);
            var roleTmp = roleObj.GetComponent<TextMeshProUGUI>();
            roleTmp.text      = role;
            roleTmp.fontSize  = 18;
            roleTmp.color     = UITheme.SecondaryText;
            roleTmp.alignment = TextAlignmentOptions.Center;
            roleTmp.raycastTarget = false;
            FillRect(roleObj);

            var nameObj = MenuScreenPrimitives.CreateTMP("Name", row.transform);
            var nameTmp = nameObj.GetComponent<TextMeshProUGUI>();
            nameTmp.text      = name;
            nameTmp.fontSize  = 19;
            nameTmp.color     = UITheme.PrimaryText;
            nameTmp.raycastTarget = false;
            nameObj.AddComponent<LayoutElement>().preferredWidth = 400f;
        }

        private void AddSpacer(Transform parent, float h)
        {
            var obj = new GameObject("Spacer");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, h);
            obj.AddComponent<LayoutElement>().preferredHeight = h;
        }

        // Klikalny przycisk-link (wzorzec Application.OpenURL jak w ModsScreenUI.OnOpenWorkshop).
        private void AddLinkButton(Transform parent, string label, string url)
        {
            var btn = MenuScreenPrimitives.CreateButton("OSMCopyrightLink", parent, label, 18, 360f, 40f);
            btn.GetComponent<Button>().onClick.AddListener(() => Application.OpenURL(url));
        }

        private static void FillRect(GameObject go) => MenuScreenPrimitives.Fill(go);

        // ─────────────────────────────────────────────
        //  PRIMITIVE HELPERS
        // ─────────────────────────────────────────────

    }
}
