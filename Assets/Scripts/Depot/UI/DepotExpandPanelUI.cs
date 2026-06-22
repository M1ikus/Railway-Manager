using TMPro;
using RailwayManager.Core;
using RailwayManager.Economy;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Floating panel z 1 przyciskiem "Rozszerz zajezdnię".
    /// Q1 interpretacja A (depot-visual-direction.md 2026-05-17) — kupowanie kolejnych
    /// tier'ów (pakiety length+width). 5 tier (0=start 800×300m, 4=max 2000×400m).
    ///
    /// Cennik per tier — placeholder, **TODO M-Balance**. Wartości w `TIER_COSTS`.
    ///
    /// Pozycja: lewy-dolny róg ekranu, nad BuildMenuUI dolnym paskiem.
    /// Widoczny TYLKO przy aktywnej zakładce BUDOWANIE (2026-06-12 — wcześniej wisiał
    /// na stałe; rozbudowa terenu to akcja budowlana, nie permanentny HUD).
    /// Auto-spawn przy załadowaniu sceny Depot (RuntimeInitializeOnLoadMethod).
    /// </summary>
    public class DepotExpandPanelUI : MonoBehaviour
    {
        // ── Cennik placeholder (TODO M-Balance) ────────────────────────
        // Indeks = tier do którego rozszerzasz (Tier 0 nie ma kosztu bo to start).
        // Progresja x2 na każdy tier — typowa balance management sim'ów.
        private static readonly long[] TIER_COSTS = { 0L, 50_000L, 100_000L, 200_000L, 400_000L };

        private static DepotExpandPanelUI _instance;
        public static DepotExpandPanelUI Instance => _instance;

        private GameObject panel;
        private TMP_Text infoLabel;
        private Button expandButton;
        private TMP_Text expandButtonText;
        private bool _moneyEventSubscribed;
        private bool _boundsEventSubscribed;
        private MainTabBarUI _tabBar;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "Depot") return;
            if (FindAnyObjectByType<DepotExpandPanelUI>() != null) return;

            var go = new GameObject("DepotExpandPanelUI (auto-spawn)");
            go.AddComponent<DepotExpandPanelUI>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        void Start()
        {
            // Subscribe na money + bounds events
            GameState.OnMoneyChanged += OnMoneyChanged;
            _moneyEventSubscribed = true;

            var gg = DepotServices.Get<GroundGenerator>();
            if (gg != null)
            {
                gg.OnBoundsChanged += RefreshUI;
                _boundsEventSubscribed = true;
            }

            // Widoczność spięta z zakładką BUDOWANIE. Bez tab bara (scena testowa)
            // panel zostaje widoczny jak dotąd — nie byłoby jak go otworzyć.
            _tabBar = DepotServices.Get<MainTabBarUI>();
            if (_tabBar != null)
            {
                _tabBar.OnTabChanged += OnMainTabChanged;
                OnMainTabChanged(_tabBar.ActiveTab); // stan startowy (default Select → ukryty)
            }

            RefreshUI();
        }

        void OnDestroy()
        {
            if (_moneyEventSubscribed)
            {
                GameState.OnMoneyChanged -= OnMoneyChanged;
                _moneyEventSubscribed = false;
            }
            if (_boundsEventSubscribed)
            {
                var gg = DepotServices.Get<GroundGenerator>();
                if (gg != null) gg.OnBoundsChanged -= RefreshUI;
                _boundsEventSubscribed = false;
            }
            if (_tabBar != null)
            {
                _tabBar.OnTabChanged -= OnMainTabChanged;
                _tabBar = null;
            }
            if (_instance == this) _instance = null;
        }

        /// <summary>Panel żyje tylko w zakładce BUDOWANIE (komponent dalej tyka — eventy money/bounds aktualizują ukryty panel).</summary>
        private void OnMainTabChanged(MainTab tab)
        {
            if (panel == null) return;
            bool show = tab == MainTab.Build;
            if (panel.activeSelf == show) return;
            panel.SetActive(show);
            if (show) RefreshUI();
        }

        private void OnMoneyChanged(long oldVal, long newVal) => RefreshUI();

        private void BuildUI()
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Log.Warn("[DepotExpandPanelUI] No Canvas in scene, panel not created");
                return;
            }

            // Panel container — floating left-bottom (nad BuildMenuUI dolnym paskiem)
            panel = new GameObject("DepotExpandPanel");
            panel.transform.SetParent(canvas.transform, false);
            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(20f, 160f);  // above bottom toolbar
            rt.sizeDelta = new Vector2(320f, 100f);
            var bg = panel.AddComponent<Image>();
            bg.color = UITheme.OverlayPanelStrong;

            // Info label (current tier + dimensions + next)
            var labelGo = new GameObject("InfoLabel");
            labelGo.transform.SetParent(panel.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0.45f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.offsetMin = new Vector2(12f, 0f);
            labelRt.offsetMax = new Vector2(-12f, -5f);
            infoLabel = labelGo.AddComponent<TextMeshProUGUI>();
            infoLabel.fontSize = 13;
            infoLabel.color = UITheme.PrimaryText;
            infoLabel.alignment = TextAlignmentOptions.MidlineLeft;
            infoLabel.text = "...";

            // Expand button
            var btnGo = new GameObject("ExpandButton");
            btnGo.transform.SetParent(panel.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 0f);
            btnRt.anchorMax = new Vector2(1f, 0.45f);
            btnRt.offsetMin = new Vector2(12f, 10f);
            btnRt.offsetMax = new Vector2(-12f, -2f);
            var btnBg = btnGo.AddComponent<Image>();
            btnBg.color = UITheme.PrimaryAccent;
            expandButton = btnGo.AddComponent<Button>();
            expandButton.targetGraphic = btnBg;
            var colors = expandButton.colors;
            colors.normalColor = UITheme.PrimaryAccent;
            colors.highlightedColor = UITheme.PrimaryAccentHover;
            colors.pressedColor = UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f);
            colors.disabledColor = UITheme.Border;
            expandButton.colors = colors;
            expandButton.onClick.AddListener(OnExpandClicked);

            var btnTextGo = new GameObject("Label");
            btnTextGo.transform.SetParent(btnGo.transform, false);
            var btnTextRt = btnTextGo.AddComponent<RectTransform>();
            btnTextRt.anchorMin = Vector2.zero;
            btnTextRt.anchorMax = Vector2.one;
            btnTextRt.offsetMin = Vector2.zero;
            btnTextRt.offsetMax = Vector2.zero;
            expandButtonText = btnTextGo.AddComponent<TextMeshProUGUI>();
            expandButtonText.fontSize = 13;
            expandButtonText.color = Color.white;
            expandButtonText.alignment = TextAlignmentOptions.Center;
            expandButtonText.text = "Rozszerz zajezdnię";
        }

        private void RefreshUI()
        {
            if (infoLabel == null || expandButton == null) return;
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg == null) return;

            int cur = gg.CurrentTier;
            float curL = gg.CurrentLengthX;
            float curW = gg.CurrentWidthZ;
            bool canExpand = gg.CanExpand();

            if (canExpand)
            {
                var next = gg.PreviewNextTierSize();
                int nextTier = cur + 1;
                long cost = TIER_COSTS[nextTier];
                bool canAfford = GameState.Money >= cost;

                infoLabel.text = $"Tier {cur}/{gg.MaxTier}: {curL:F0}×{curW:F0}m\n" +
                                 $"<color=#9DA8B5>Następny: {next.x:F0}×{next.y:F0}m</color>";
                expandButtonText.text = $"Rozszerz do Tier {nextTier} ({cost:N0} zł)";
                expandButton.interactable = canAfford;
            }
            else
            {
                infoLabel.text = $"Tier {cur}/{gg.MaxTier} <color=#76B06F>(MAX)</color>\n" +
                                 $"{curL:F0}×{curW:F0}m";
                expandButtonText.text = "Zajezdnia maksymalna";
                expandButton.interactable = false;
            }
        }

        private void OnExpandClicked()
        {
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg == null || !gg.CanExpand()) return;

            int nextTier = gg.CurrentTier + 1;
            long cost = TIER_COSTS[nextTier];
            if (GameState.Money < cost)
            {
                Log.Warn($"[DepotExpandPanelUI] Brak kasy: potrzeba {cost:N0} zł, mamy {GameState.Money:N0} zł");
                return;
            }

            MoneyLedger.Spend(cost * 100L, "depot_expansion", $"Rozbudowa zajezdni tier {nextTier}");
            bool expanded = gg.ExpandToNextTier();
            if (expanded)
                Log.Info($"[DepotExpandPanelUI] Tier {nextTier} kupiony za {cost:N0} zł");
            else
            {
                // Rollback jeśli expand failed (nie powinno się zdarzyć po CanExpand check, ale safety)
                MoneyLedger.Earn(cost * 100L, "depot_expansion_refund", "rollback rozbudowy");
                Log.Warn("[DepotExpandPanelUI] ExpandToNextTier zwróciło false po CanExpand=true (race?)");
            }
        }
    }
}
