using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M7-7: Floating alerts panel — prawy górny róg, pod TopBar'em.
    ///
    /// 3 badge z licznikami + klik otwiera właściwy panel:
    /// - Overdue przeglądy (>=1.0 urgency) → otwiera Warsztaty
    /// - Active breakdowns (BrokenDown/AwaitingRescue) → otwiera Warsztaty
    /// - Low-stock parts (<3 szt) → otwiera Magazyn
    ///
    /// Badge ukryty gdy count=0.
    /// </summary>
    public class MaintenanceAlertsUI : MonoBehaviour
    {
        public static MaintenanceAlertsUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        Button _overdueBtn;
        TextMeshProUGUI _overdueTxt;
        Button _breakdownBtn;
        TextMeshProUGUI _breakdownTxt;
        Button _partsBtn;
        TextMeshProUGUI _partsTxt;

        private static readonly Color OverdueBadge = UITheme.WithAlpha(UITheme.Danger, 0.92f);
        private static readonly Color BreakdownBadge = UITheme.WithAlpha(UITheme.Danger, 0.98f);
        private static readonly Color PartsBadge = UITheme.WithAlpha(UITheme.Warning, 0.92f);

        float _refreshTimer;
        const float RefreshInterval = 1f;

        public static MaintenanceAlertsUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MaintenanceAlertsUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MaintenanceAlertsUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // 2026-05-17: scene-aware visibility. MaintenanceAlertsUI ma DontDestroyOnLoad,
            // czyli przeżywa transition Depot → MainMenu (gracz wychodzi z gry → main menu).
            // W MainMenu/GameCreator alerts nie mają sensu (brak active gameplay), ale wciąż
            // pokazują last-known count z save'a → pomarańczowy badge widoczny w Settings.
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool inGameplay = sceneName == "Depot" || sceneName == "MapScene";
            // Ustępuj miejsca pełnoekranowym popupom (kreator rozkładów / pickery) — inaczej
            // pomarańczowy badge koliduje z ich UI w prawym-górnym rogu.
            bool popupOpen = SceneController.TimetablePopupOpen || SceneController.FullscreenOverlayOpen;
            bool show = inGameplay && !popupOpen;
            if (_root != null && _root.activeSelf != show)
            {
                _root.SetActive(show);
            }
            if (!show) return;

            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer >= RefreshInterval)
            {
                _refreshTimer = 0f;
                Refresh();
            }
        }

        void Refresh()
        {
            int overdueCount = CountOverdue();
            int breakdownCount = CountBreakdowns();
            int lowStockCount = CountLowStockParts();

            SetBadge(_overdueBtn, _overdueTxt, overdueCount,
                string.Format(LocalizationService.Get("maintenance.alerts.overdue_format"), overdueCount), OverdueBadge);
            SetBadge(_breakdownBtn, _breakdownTxt, breakdownCount,
                string.Format(LocalizationService.Get("maintenance.alerts.breakdown_format"), breakdownCount), BreakdownBadge);
            SetBadge(_partsBtn, _partsTxt, lowStockCount,
                string.Format(LocalizationService.Get("maintenance.alerts.parts_format"), lowStockCount), PartsBadge);
        }

        static int CountOverdue()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null) return 0;
            return wm.GetOverdueVehicles(1.0f).Count; // tylko faktyczne overdue (>=100%)
        }

        static int CountBreakdowns()
        {
            var sim = TrainRunSimulator.Instance;
            if (sim == null) return 0;
            int n = 0;
            foreach (var kvp in sim.ActiveTrains)
            {
                var st = kvp.Value;
                if (st.state == TrainState.BrokenDown || st.state == TrainState.AwaitingRescue)
                    n++;
            }
            return n;
        }

        static int CountLowStockParts()
        {
            var inv = PartInventoryService.Instance;
            if (inv == null) return 0;
            int n = 0;
            foreach (var type in PartCatalog.AllTypes)
                if (inv.GetStock(type) < 3) n++;
            return n;
        }

        static void SetBadge(Button btn, TextMeshProUGUI txt, int count, string label, Color color)
        {
            if (btn == null) return;
            bool visible = count > 0;
            btn.gameObject.SetActive(visible);
            if (!visible) return;
            if (txt != null) txt.text = label;
            if (btn.image != null)
            {
                UITheme.ApplySurface(btn.image, color, UIShapePreset.Pill);
                btn.colors = UITheme.CreateColorBlock(
                    color,
                    UITheme.Darken(color, 0.03f),
                    UITheme.Darken(color, 0.1f),
                    color,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
            }
        }

        // ── UI build ──────────────────────────────────────

        void BuildUI()
        {
            var canvasGo = new GameObject("MaintenanceAlertsCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 220;  // nad TopBar (200), pod fullscreen panels (230)
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(520f, 36f);
            rt.anchoredPosition = new Vector2(-10f, -44f); // 4px pod TopBar'em (40px wys.)

            // HorizontalLayoutGroup dla 3 badges
            var hlg = _root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Sm;
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;

            _overdueBtn = CreateBadge("Overdue", 170f, () =>
                UIIntents.Emit(UIIntent.OpenWorkshopsPanel),
                out _overdueTxt);
            _breakdownBtn = CreateBadge("Breakdown", 160f, () =>
                UIIntents.Emit(UIIntent.OpenWorkshopsPanel),
                out _breakdownTxt);
            _partsBtn = CreateBadge("Parts", 180f, () =>
                UIIntents.Emit(UIIntent.OpenPartsPanel),
                out _partsTxt);

            _overdueBtn.gameObject.SetActive(false);
            _breakdownBtn.gameObject.SetActive(false);
            _partsBtn.gameObject.SetActive(false);
        }

        Button CreateBadge(string name, float width, System.Action onClick, out TextMeshProUGUI labelTxt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 32f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, OverdueBadge, UIShapePreset.Pill);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                OverdueBadge,
                UITheme.Darken(OverdueBadge, 0.03f),
                UITheme.Darken(OverdueBadge, 0.1f),
                OverdueBadge,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 0f);
            trt.offsetMax = new Vector2(-8f, 0f);

            labelTxt = txtGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(labelTxt, UIThemeTextRole.Inverse);
            labelTxt.fontSize = 13;
            labelTxt.alignment = TextAlignmentOptions.Center;
            labelTxt.raycastTarget = false;
            labelTxt.text = name;

            return btn;
        }
    }
}
