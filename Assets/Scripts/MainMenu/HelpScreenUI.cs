using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "Pomoc" (Help).
    /// </summary>
    public class HelpScreenUI : MonoBehaviour, IMenuScreen
    {
        private struct HelpTopic
        {
            public string keyBase;  // i18n key prefix, np. "help.topics.navigation"
                                    // header key = {keyBase}.header, body = {keyBase}.body
        }

        private GameObject root;
        private RectTransform contentRT;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI backButtonText;
        private Transform contentParent;

        private readonly HelpTopic[] topics = new HelpTopic[]
        {
            new HelpTopic { keyBase = "help.topics.navigation" },
            new HelpTopic { keyBase = "help.topics.build_modes" },
            new HelpTopic { keyBase = "help.topics.train_management" },
            new HelpTopic { keyBase = "help.topics.keybindings" },
            new HelpTopic { keyBase = "help.topics.save_load" },
        };

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
            PopulateTopics();
            RefreshLanguage();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
        }
        public void Hide()  { root.SetActive(false); }
        public bool IsVisible => root != null && root.activeSelf;

        public void RefreshLanguage()
        {
            if (titleLabel != null) titleLabel.text = LocalizationService.Get("help.title");
            backButtonText.text = "\u2190";
            PopulateTopics();
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

        private void OnLocaleChanged()
        {
            if (IsVisible)
            {
                PopulateTopics();
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
            }
            RefreshLanguage();
        }

        // ─────────────────────────────────────────────
        //  ROOT
        // ─────────────────────────────────────────────

        private void BuildRoot(Transform parent)
        {
            root = MenuScreenPrimitives.CreateFullscreenRoot("HelpScreen", parent);
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
                contentSpacing: UITheme.Spacing.Lg,
                contentAlignment: TextAnchor.UpperCenter,
                out contentRT);
            contentParent = contentRT.transform;
        }

        private void PopulateTopics()
        {
            foreach (Transform child in contentParent)
                Destroy(child.gameObject);
            foreach (var topic in topics)
                AddTopicBlock(topic);
        }

        // ─────────────────────────────────────────────
        //  TOPIC BLOCK
        // ─────────────────────────────────────────────

        private void AddTopicBlock(HelpTopic topic)
        {
            var block = new GameObject("TopicBlock");
            block.transform.SetParent(contentParent, false);
            var blockRT = block.AddComponent<RectTransform>();
            blockRT.sizeDelta = new Vector2(0f, 0f);
            block.AddComponent<LayoutElement>().preferredHeight = 0f;

            var bg = block.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.OverlayPanel, UIShapePreset.Panel);

            var vl = block.AddComponent<VerticalLayoutGroup>();
            vl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vl.spacing  = UITheme.Spacing.Md;
            vl.childControlWidth   = true;
            vl.childControlHeight  = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var csf = block.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Header
            var headerCard = new GameObject("HeaderCard");
            headerCard.transform.SetParent(block.transform, false);
            headerCard.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);
            UITheme.ApplySurface(headerCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.78f), UIShapePreset.Inset);
            headerCard.AddComponent<LayoutElement>().preferredHeight = 40f;
            var headerLayout = headerCard.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            var headerObj = MenuScreenPrimitives.CreateTMP("Header", headerCard.transform);
            var headerTmp = headerObj.GetComponent<TextMeshProUGUI>();
            headerTmp.text      = LocalizationService.Get(topic.keyBase + ".header");
            headerTmp.fontSize  = 21;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.color     = UITheme.PrimaryAccent;
            headerTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            headerObj.AddComponent<LayoutElement>().preferredHeight = 24f;

            // Divider
            var div = new GameObject("Divider");
            div.transform.SetParent(block.transform, false);
            var divImg = div.AddComponent<Image>();
            divImg.color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.65f);
            var divLE = div.AddComponent<LayoutElement>();
            divLE.preferredHeight = 1f;

            // Body
            var bodyCard = new GameObject("BodyCard");
            bodyCard.transform.SetParent(block.transform, false);
            bodyCard.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 0f);
            UITheme.ApplySurface(bodyCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f), UIShapePreset.Inset);
            var bodyLayout = bodyCard.AddComponent<VerticalLayoutGroup>();
            bodyLayout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = false;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;
            bodyCard.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bodyObj = MenuScreenPrimitives.CreateTMP("Body", bodyCard.transform);
            var bodyTmp = bodyObj.GetComponent<TextMeshProUGUI>();
            bodyTmp.text      = LocalizationService.Get(topic.keyBase + ".body");
            bodyTmp.fontSize  = 18;
            bodyTmp.color     = UITheme.SecondaryText;
            bodyTmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            bodyTmp.lineSpacing = 6f;

            var bodyLE = bodyObj.AddComponent<LayoutElement>();
            bodyLE.flexibleWidth = 1f;
        }

        // ─────────────────────────────────────────────
        //  PRIMITIVE HELPERS
        // ─────────────────────────────────────────────

    }
}
