using UnityEngine;
using RailwayManager.Core;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "Dodatki" (Mods).
    /// </summary>
    public class ModsScreenUI : MonoBehaviour, IMenuScreen
    {
        // ModEntry struct + CreateModRow row builder usunięte 2026-05-14 — mod system
        // jest POST-EA, lista zawsze pusta. Gdy mod loader wejdzie: dorobimy ModEntry
        // (name/author/version/description/enabled) + CreateModRow z tag chip + toggle.
        // Aktualnie ekran pokazuje tylko intro card + empty state.

        private GameObject root;
        private RectTransform contentRT;
        private Transform contentParent;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI backButtonText;
        private TextMeshProUGUI noModsLabel;
        private TextMeshProUGUI openFolderText;
        private TextMeshProUGUI workshopText;

        // Docelowy URL Steam Workshop — uzupełnić po opublikowaniu gry na Steam (M14).
        // Sentinel `APPID` powoduje że przycisk Workshop jest disabled (patrz `IsWorkshopUrlReady`).
        private const string WorkshopUrl = "https://steamcommunity.com/app/APPID/workshop/";

        private static bool IsWorkshopUrlReady => !WorkshopUrl.Contains("APPID");

        private static readonly Color BottomBarBg   = UITheme.OverlayPanelStrong;
        private static readonly Color RowBg         = UITheme.OverlayPanel;
        private static readonly Color TextPrimary   = UITheme.PrimaryText;
        private static readonly Color TextSecondary = UITheme.SecondaryText;
        private static readonly Color AccentColor   = UITheme.PrimaryAccent;

        public System.Action OnBack;

        public void Build(Transform canvasTransform)
        {
            BuildRoot(canvasTransform);
            BuildTopBar();
            BuildScrollArea();
            BuildBottomBar();
            root.SetActive(false);
        }

        public void Show()
        {
            root.SetActive(true);
            PopulateMods();
            RefreshLanguage();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
        }

        public void Hide()  { root.SetActive(false); }
        public bool IsVisible => root != null && root.activeSelf;

        public void RefreshLanguage()
        {
            if (titleLabel != null) titleLabel.text = LocalizationService.Get("mods.title");
            backButtonText.text = "\u2190";
            if (openFolderText != null) openFolderText.text = LocalizationService.Get("mods.button.open_folder");
            if (workshopText != null)   workshopText.text   = LocalizationService.Get("mods.button.workshop");
            if (noModsLabel != null)    noModsLabel.text    = LocalizationService.Get("mods.empty");
        }

        // === i18n hot-reload (M13-4d) ===

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
                PopulateMods();
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
            }
            RefreshLanguage();
        }

        // ─────────────────────────────────────────────
        //  ROOT
        // ─────────────────────────────────────────────

        private void BuildRoot(Transform parent)
        {
            root = MenuScreenPrimitives.CreateFullscreenRoot("ModsScreen", parent);
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
                offsetMin: new Vector2(0f, 55f),
                offsetMax: new Vector2(0f, -80f),
                contentPadding: new RectOffset(60, 60, 20, 20),
                contentSpacing: UITheme.Spacing.Sm,
                contentAlignment: TextAnchor.UpperCenter,
                out contentRT);
            contentParent = contentRT.transform;
        }

        // ─────────────────────────────────────────────
        //  BOTTOM BAR
        // ─────────────────────────────────────────────

        private void BuildBottomBar()
        {
            var bar = new GameObject("BottomBar");
            bar.transform.SetParent(root.transform, false);
            UITheme.ApplySurface(bar.AddComponent<Image>(), BottomBarBg, UIShapePreset.PanelLarge);

            var barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0f);
            barRT.anchorMax = new Vector2(1f, 0f);
            barRT.pivot     = new Vector2(0.5f, 0f);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0f, 50f);

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.padding  = UITheme.Padding(UITheme.Spacing.Xxxl, UITheme.Spacing.Xl, UITheme.Spacing.Sm, UITheme.Spacing.Sm);
            layout.spacing  = UITheme.Spacing.Lg;
            layout.childAlignment      = TextAnchor.MiddleLeft;
            layout.childControlWidth   = false;
            layout.childControlHeight  = false;

            var folderBtn = MenuScreenPrimitives.CreateButton("OpenFolderBtn", bar.transform, string.Empty, 18, 240f, 34f);
            openFolderText = folderBtn.GetComponentInChildren<TextMeshProUGUI>();
            folderBtn.GetComponent<Button>().onClick.AddListener(OnOpenModsFolder);

            var workshopBtn = MenuScreenPrimitives.CreateButton("WorkshopBtn", bar.transform, string.Empty, 18, 280f, 34f);
            workshopText = workshopBtn.GetComponentInChildren<TextMeshProUGUI>();
            var workshopButton = workshopBtn.GetComponent<Button>();
            workshopButton.onClick.AddListener(OnOpenWorkshop);
            // Pre-Steam launch: APPID nieuzupełnione, klik dawałby 404. Disable przycisk.
            workshopButton.interactable = IsWorkshopUrlReady;
        }

        // ─────────────────────────────────────────────
        //  POPULATE
        // ─────────────────────────────────────────────

        private void PopulateMods()
        {
            foreach (Transform child in contentParent)
                Destroy(child.gameObject);

            var introCard = new GameObject("ModsIntro");
            introCard.transform.SetParent(contentParent, false);
            // 2026-05-17: sizeDelta height jako minimum, ContentSizeFitter rozszerza do
            // rzeczywistego content height. Fixed 150 nie wystarczał gdy body wrap'ował na 3 linie.
            introCard.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 0f);
            UITheme.ApplySurface(introCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            var introLayout = introCard.AddComponent<VerticalLayoutGroup>();
            introLayout.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            introLayout.spacing = UITheme.Spacing.Sm;
            introLayout.childAlignment = TextAnchor.UpperLeft;
            introLayout.childControlWidth = true;
            introLayout.childControlHeight = true;     // = true (był false) — VLG kontroluje child height
            introLayout.childForceExpandWidth = true;
            introLayout.childForceExpandHeight = false;
            var introCSF = introCard.AddComponent<ContentSizeFitter>();
            introCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var introEyebrow = CreateTMP("Eyebrow", introCard.transform).GetComponent<TextMeshProUGUI>();
            introEyebrow.text = LocalizationService.Get("mods.intro.eyebrow");
            introEyebrow.fontSize = 11;  // 10→11 (lepsza czytelność)
            introEyebrow.fontStyle = FontStyles.Bold;
            introEyebrow.color = AccentColor;
            introEyebrow.raycastTarget = false;

            var introTitle = CreateTMP("Title", introCard.transform).GetComponent<TextMeshProUGUI>();
            introTitle.text = LocalizationService.Get("mods.intro.title");
            introTitle.fontSize = 22;
            introTitle.color = TextPrimary;
            introTitle.raycastTarget = false;
            introTitle.textWrappingMode = TMPro.TextWrappingModes.Normal;

            var introBody = CreateTMP("Body", introCard.transform).GetComponent<TextMeshProUGUI>();
            introBody.text = LocalizationService.Get("mods.intro.body");
            introBody.fontSize = 14;  // 13→14 (lepsza czytelność)
            introBody.color = TextSecondary;
            introBody.raycastTarget = false;
            introBody.textWrappingMode = TMPro.TextWrappingModes.Normal;

            // Mod loader POST-EA: empty state pokazywany zawsze. Po wprowadzeniu loadera
            // przed tym blokiem dodać iterację po wczytanych modach (CreateModRow per).
            var emptyCard = new GameObject("EmptyState");
            emptyCard.transform.SetParent(contentParent, false);
            // 2026-05-17: ContentSizeFitter zamiast fixed height (locale może mieć dłuższy tekst).
            emptyCard.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 0f);
            UITheme.ApplySurface(emptyCard.AddComponent<Image>(), RowBg, UIShapePreset.Panel);
            var emptyLayout = emptyCard.AddComponent<VerticalLayoutGroup>();
            emptyLayout.padding = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Xl);
            emptyLayout.spacing = UITheme.Spacing.Sm;
            emptyLayout.childAlignment = TextAnchor.MiddleCenter;
            emptyLayout.childControlWidth = true;
            emptyLayout.childControlHeight = true;     // = true (był false)
            emptyLayout.childForceExpandWidth = true;
            emptyLayout.childForceExpandHeight = false;
            var emptyCSF = emptyCard.AddComponent<ContentSizeFitter>();
            emptyCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var emptyEyebrow = CreateTMP("EmptyEyebrow", emptyCard.transform).GetComponent<TextMeshProUGUI>();
            emptyEyebrow.text = LocalizationService.Get("mods.empty_state.eyebrow");
            emptyEyebrow.fontSize = 11;  // 10→11 (lepsza czytelność)
            emptyEyebrow.fontStyle = FontStyles.Bold;
            emptyEyebrow.color = AccentColor;
            emptyEyebrow.alignment = TextAlignmentOptions.Center;
            emptyEyebrow.raycastTarget = false;

            var msgObj = CreateTMP("NoMods", emptyCard.transform);
            noModsLabel = msgObj.GetComponent<TextMeshProUGUI>();
            noModsLabel.fontSize  = 24;
            noModsLabel.color     = TextSecondary;
            noModsLabel.alignment = TextAlignmentOptions.Center;
            noModsLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;
            var le = msgObj.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;

            var hintObj = CreateTMP("EmptyHint", emptyCard.transform).GetComponent<TextMeshProUGUI>();
            hintObj.text = LocalizationService.Get("mods.empty_state.hint");
            hintObj.fontSize = 14;
            hintObj.color = TextSecondary;
            hintObj.alignment = TextAlignmentOptions.Center;
            hintObj.textWrappingMode = TMPro.TextWrappingModes.Normal;
            hintObj.raycastTarget = false;
            hintObj.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
        }

        // ─────────────────────────────────────────────
        //  ACTIONS
        // ─────────────────────────────────────────────

        // Cooldown blokujący wielokrotne spawnowanie explorera/Finder'a na rapid-fire kliknięciach
        // (BUG: 5 kliknięć = 5 okien). Po 2s znów dozwolone — gdyby user zamknął okno i chciał wrócić.
        private const float OpenFolderCooldown = 2f;
        private float _lastOpenFolderTime = -10f;

        private void OnOpenModsFolder()
        {
            if (Time.unscaledTime - _lastOpenFolderTime < OpenFolderCooldown)
                return;

            var path = System.IO.Path.Combine(Application.dataPath, "..", "Mods");
            path = System.IO.Path.GetFullPath(path);

            // Idempotent — folder musi istnieć żeby explorer go otworzył (BUG-004 fix).
            try
            {
                System.IO.Directory.CreateDirectory(path);
            }
            catch (System.Exception e)
            {
                Log.Warn($"[Mods] Nie udało się utworzyć folderu Mods: {e.Message}");
                return;
            }

            _lastOpenFolderTime = Time.unscaledTime;
            Log.Info($"[Mods] Otwieranie folderu: {path}");
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", path.Replace("/", "\\"));
#else
            // Mac/Linux fallback — Unity standardowy sposób (xdg-open / open)
            Application.OpenURL("file://" + path);
#endif
        }

        private void OnOpenWorkshop()
        {
            // Defense in depth: nawet gdyby przycisk nie był zdisabled'owany (np. ktoś go enable'uje
            // ręcznie w inspectorze), nie chcemy otworzyć URL'a z literalnym `APPID` (→ 404).
            if (!IsWorkshopUrlReady)
            {
                Log.Warn("[Mods] Workshop URL nie ustawiony (APPID placeholder) — uzupełnić w M14.");
                return;
            }
            Log.Info($"[Mods] Otwieranie Steam Workshop: {WorkshopUrl}");
            Application.OpenURL(WorkshopUrl);
        }

        // ─────────────────────────────────────────────
        //  PRIMITIVE HELPERS
        // ─────────────────────────────────────────────

        private GameObject CreateTMP(string name, Transform parent) => MenuScreenPrimitives.CreateTMP(name, parent);
        private static void FillRect(GameObject go) => MenuScreenPrimitives.Fill(go);
    }
}
