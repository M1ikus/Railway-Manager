using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P3-ops: OPERACYJNE okno składu w zajezdni — pełny replace <c>ConsistPopupUI</c>.
    /// Renderuje treść w pływającym <see cref="FloatingWindow"/>: nagłówek + chipy pojazdów
    /// (drill-down → okno pojazdu P2) + akcje (Wyjazd / Rozprzęgnij z pickerem / Sprzęgnij) +
    /// „Jedź" gdy ustawiony pending-target (flow 3D klik toru).
    ///
    /// <para><b>Logika manewrów NIE jest tu</b> — okno tylko podnosi te same eventy co stary popup
    /// (OnExit/Decouple/CoupleAdjacent/Go/Close), a <see cref="DepotConsistSelectionHandler"/>
    /// wykonuje je przez <c>DepotMovementSimulator</c>. Kontrakt 1:1 z ConsistPopupUI → handler
    /// przepięty minimalnie (Instance + Show/Hide/SetPendingTarget/PendingTargetTrackId).</para>
    /// </summary>
    public sealed class ConsistOperationalWindow
    {
        static ConsistOperationalWindow _instance;
        public static ConsistOperationalWindow Instance => _instance ??= new ConsistOperationalWindow();

        public event Action OnCloseRequested;
        public event Action OnGoRequested;
        public event Action OnExitRequested;
        public event Action<int> OnDecoupleRequested;
        public event Action OnCoupleAdjacentRequested;

        static readonly Vector2 WinSize = new Vector2(400f, 560f);
        const float HeaderH = 46f;

        ConsistMarker _shown;
        FloatingWindow _win;
        int _pendingTargetTrackId = -1;
        Vector3? _pendingTargetWorldPos;

        // decouple mode (picker miejsca cięcia)
        bool _decoupleMode;
        int _decoupleCutIndex = -1;
        bool _decoupleInCirc;
        readonly List<Image> _gapBgs = new List<Image>();
        readonly List<TextMeshProUGUI> _gapTxts = new List<TextMeshProUGUI>();
        TextMeshProUGUI _decoupleStatus;
        Button _decoupleConfirm;

        public int PendingTargetTrackId => _pendingTargetTrackId;
        public Vector3? PendingTargetWorldPos => _pendingTargetWorldPos;

        // ── API zgodne z ConsistPopupUI (woła handler) ──────────────────

        public void Show(ConsistMarker marker, bool waitingForTarget)
        {
            _shown = marker;
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            _decoupleMode = false;
            if (marker == null) return;

            var view = ConsistView.FromConsistMarker(marker);
            _win = WindowManager.Instance.OpenWindow(view.Key, view.Title, WinSize);
            _win.OnClosed -= HandleWindowClosed;
            _win.OnClosed += HandleWindowClosed;
            Rebuild();
        }

        public void Hide()
        {
            _shown = null;
            _decoupleMode = false;
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            if (_win != null)
            {
                _win.OnClosed -= HandleWindowClosed; // nie traktuj programowego close jako request
                _win.Close();
                _win = null;
            }
        }

        public void SetPendingTarget(int trackId, Vector3 worldPos)
        {
            _pendingTargetTrackId = trackId;
            _pendingTargetWorldPos = worldPos;
            Rebuild();
        }

        public void ClearPendingTarget()
        {
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            Rebuild();
        }

        // ── Lifecycle ───────────────────────────────────────────────────

        void HandleWindowClosed()
        {
            // user kliknął ✕ → potraktuj jak żądanie zamknięcia (handler zrobi Deselect)
            _win = null;
            var had = _shown != null;
            _shown = null;
            if (had) OnCloseRequested?.Invoke();
        }

        void Rebuild()
        {
            if (_win == null || _shown == null) return;
            var root = _win.ContentRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);

            if (_decoupleMode) BuildDecoupleView(root);
            else BuildNormalView(root);
        }

        // ── Widok normalny: nagłówek + chipy (drill-down) + akcje ───────

        void BuildNormalView(Transform root)
        {
            var view = ConsistView.FromConsistMarker(_shown);
            BuildHeader(root, view);

            var ids = _shown.vehicleIds ?? new List<int>();
            bool moving = DepotMovementSimulator.Instance != null
                && DepotMovementSimulator.Instance.HasTaskForConsist(_shown.consistId);
            bool canDecouple = ids.Count >= 2 && !moving;
            bool hasPending = _pendingTargetTrackId >= 0;

            int nButtons = 2 + (canDecouple ? 1 : 0) + (hasPending ? 1 : 0); // Wyjazd + Sprzęgnij (+Rozprzęgnij)(+Jedź)
            float opsH = 16f + 24f + nButtons * 34f + (nButtons - 1) * 6f;

            var list = WindowScroll.BuildVertical(root, HeaderH, opsH);
            for (int i = 0; i < ids.Count; i++)
                ConsistWindowUI.BuildVehicleRow(list, ids[i]);

            BuildOpsPanel(root, opsH, canDecouple, hasPending);
        }

        void BuildHeader(Transform root, ConsistView view)
        {
            var header = NewRect("Header", root);
            header.anchorMin = new Vector2(0f, 1f); header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, HeaderH);
            var vlg = header.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var ctx = UIPrimitives.MakeTMP("Context", header, UITheme.Typography.Small, UIThemeTextRole.Secondary);
            ctx.text = view.Context;
            var st = UIPrimitives.MakeTMP("Status", header, UITheme.Typography.Small, UIThemeTextRole.Accent);
            st.text = view.Status + "  ·  " + view.VehicleIds.Count + " poj.";
        }

        void BuildOpsPanel(Transform root, float opsH, bool canDecouple, bool hasPending)
        {
            var panel = NewRect("Ops", root);
            panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(0f, opsH);
            UITheme.ApplySurface(panel.gameObject.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.6f), UIShapePreset.Inset);

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var hint = UIPrimitives.MakeTMP("Hint", panel, UITheme.Typography.Tiny, UIThemeTextRole.Secondary);
            hint.text = hasPending ? "Kliknięto tor docelowy — potwierdź jazdę" : "Kliknij tor w zajezdni, aby wskazać cel";
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            if (hasPending)
                AddButton(panel, "Jedź na wskazany tor", UIButtonTone.Primary, () => OnGoRequested?.Invoke());

            AddButton(panel, "Wyjazd z zajezdni", UIButtonTone.Danger, () => OnExitRequested?.Invoke());
            if (canDecouple)
                AddButton(panel, "Rozprzęgnij…", UIButtonTone.Secondary, EnterDecoupleMode);
            AddButton(panel, "Sprzęgnij z sąsiednim", UIButtonTone.Secondary, () => OnCoupleAdjacentRequested?.Invoke());
        }

        // ── Widok rozprzęgania (picker miejsca cięcia) ──────────────────

        void EnterDecoupleMode()
        {
            var ids = _shown?.vehicleIds;
            if (ids == null || ids.Count < 2) return;
            _decoupleMode = true;
            _decoupleCutIndex = -1;
            _decoupleInCirc = DepotMovementSimulator.IsConsistInActiveCirculation(ids);
            Rebuild();
        }

        void BuildDecoupleView(Transform root)
        {
            _gapBgs.Clear();
            _gapTxts.Clear();
            var ids = _shown.vehicleIds ?? new List<int>();

            var title = UIPrimitives.MakeTMP("DTitle", root, UITheme.Typography.Body, UIThemeTextRole.Primary, TextAlignmentOptions.Center, FontStyles.Bold);
            var tRT = title.rectTransform;
            tRT.anchorMin = new Vector2(0f, 1f); tRT.anchorMax = new Vector2(1f, 1f);
            tRT.pivot = new Vector2(0.5f, 1f); tRT.anchoredPosition = new Vector2(0f, -UITheme.Spacing.Sm);
            tRT.sizeDelta = new Vector2(-UITheme.Spacing.Md, 24f);
            title.text = "Wybierz miejsce cięcia";

            // pasek chipów + przerw (poziomy scroll)
            var strip = BuildHorizontalStrip(root);
            for (int i = 0; i < ids.Count; i++)
            {
                CreateChip(strip, ids[i]);
                if (i < ids.Count - 1) CreateGapButton(strip, i + 1);
            }

            _decoupleStatus = UIPrimitives.MakeTMP("DStatus", root, UITheme.Typography.Small, UIThemeTextRole.Secondary, TextAlignmentOptions.Center);
            var sRT = _decoupleStatus.rectTransform;
            sRT.anchorMin = new Vector2(0f, 0f); sRT.anchorMax = new Vector2(1f, 0f);
            sRT.pivot = new Vector2(0.5f, 0f); sRT.anchoredPosition = new Vector2(0f, 96f);
            sRT.sizeDelta = new Vector2(-UITheme.Spacing.Md, 22f);
            _decoupleStatus.text = _decoupleInCirc ? "⚠ Skład jest w aktywnym obiegu" : "Kliknij przerwę między pojazdami";

            // przyciski Anuluj / Potwierdź (dół)
            var btnRow = NewRect("DBtns", root);
            btnRow.anchorMin = new Vector2(0f, 0f); btnRow.anchorMax = new Vector2(1f, 0f);
            btnRow.pivot = new Vector2(0.5f, 0f);
            btnRow.anchoredPosition = new Vector2(0f, UITheme.Spacing.Sm);
            btnRow.sizeDelta = new Vector2(-UITheme.Spacing.Md, 38f);
            var hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Sm;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            AddButton(btnRow.transform, "Anuluj", UIButtonTone.Secondary, () => { _decoupleMode = false; Rebuild(); });
            _decoupleConfirm = AddButton(btnRow.transform, "Rozprzęgnij", UIButtonTone.Primary, ConfirmDecouple);
            _decoupleConfirm.interactable = false;
        }

        Transform BuildHorizontalStrip(Transform root)
        {
            var scrollGO = new GameObject("Strip", typeof(RectTransform));
            scrollGO.transform.SetParent(root, false);
            var srRT = (RectTransform)scrollGO.transform;
            srRT.anchorMin = new Vector2(0f, 1f); srRT.anchorMax = new Vector2(1f, 1f);
            srRT.pivot = new Vector2(0.5f, 1f);
            srRT.anchoredPosition = new Vector2(0f, -40f);
            srRT.sizeDelta = new Vector2(-UITheme.Spacing.Md, 52f);

            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGO.transform, false);
            var vpRT = (RectTransform)viewport.transform;
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRT = (RectTransform)content.transform;
            cRT.anchorMin = new Vector2(0f, 0f); cRT.anchorMax = new Vector2(0f, 1f);
            cRT.pivot = new Vector2(0f, 0.5f);
            var hlg = content.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 0f; hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cRT;
            return content.transform;
        }

        void CreateChip(Transform parent, int vehicleId)
        {
            var (label, color) = VehicleChipStyle.ChipForVehicle(vehicleId);
            var go = new GameObject("Chip_" + vehicleId, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 60f; le.preferredHeight = 44f;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, color, UIShapePreset.Button);
            img.raycastTarget = false;
            var txt = UIPrimitives.MakeTMP("Lbl", go.transform, UITheme.Typography.Small, UIThemeTextRole.Inverse, TextAlignmentOptions.Center, FontStyles.Bold);
            UIPrimitives.Stretch(txt.rectTransform);
            txt.text = label;
        }

        void CreateGapButton(Transform parent, int cutIndex)
        {
            var go = new GameObject("Gap_" + cutIndex, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 26f; le.preferredHeight = 44f;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(UITheme.Border, 0.30f), UIShapePreset.Button);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            int cut = cutIndex;
            btn.onClick.AddListener(() => SelectGap(cut));
            var txt = UIPrimitives.MakeTMP("G", go.transform, UITheme.Typography.Body, UIThemeTextRole.Secondary, TextAlignmentOptions.Center);
            UIPrimitives.Stretch(txt.rectTransform);
            txt.raycastTarget = false;
            txt.text = "┆";
            _gapBgs.Add(img);
            _gapTxts.Add(txt);
        }

        void SelectGap(int cutIndex)
        {
            _decoupleCutIndex = cutIndex;
            for (int i = 0; i < _gapBgs.Count; i++)
            {
                bool sel = (i + 1) == cutIndex;
                if (_gapBgs[i] != null)
                    UITheme.ApplySurface(_gapBgs[i],
                        sel ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.95f) : UITheme.WithAlpha(UITheme.Border, 0.30f),
                        UIShapePreset.Button);
                if (_gapTxts[i] != null) _gapTxts[i].text = sel ? "✂" : "┆";
            }
            int n = _shown?.vehicleIds?.Count ?? 0;
            string counts = "Przód " + cutIndex + "  ·  Tył " + (n - cutIndex);
            if (_decoupleStatus != null)
                _decoupleStatus.text = _decoupleInCirc ? "⚠ obieg   " + counts : counts;
            if (_decoupleConfirm != null)
                _decoupleConfirm.interactable = cutIndex >= 1 && cutIndex <= n - 1;
        }

        void ConfirmDecouple()
        {
            int cut = _decoupleCutIndex;
            int n = _shown?.vehicleIds?.Count ?? 0;
            _decoupleMode = false;
            if (cut >= 1 && cut <= n - 1)
                OnDecoupleRequested?.Invoke(cut); // handler → DecoupleConsist + Deselect (→ Hide)
            else
                Rebuild();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Button AddButton(Transform parent, string label, UIButtonTone tone, Action onClick)
        {
            var btn = UIBuilders.MakeButton(parent, label, tone);
            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 34f;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }
    }
}
