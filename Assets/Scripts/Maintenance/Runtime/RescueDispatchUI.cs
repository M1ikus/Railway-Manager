using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M7-3b: UI dispatch rescue lokomotywy gdy pociąg w stanie AwaitingRescue.
    ///
    /// Pokazuje się automatycznie gdy któryś TrainRun jest w AwaitingRescue —
    /// lista wolnych lok, player wybiera, rescue wykonane. Jeśli player nie
    /// zareaguje przez 15 min game time → auto-dispatch.
    /// </summary>
    public class RescueDispatchUI : MonoBehaviour
    {
        public static RescueDispatchUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _panel;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _infoText;
        RectTransform _listContent;
        readonly List<GameObject> _entryRows = new();

        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color ListBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.84f);
        private static readonly Color ListViewportBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.16f);
        private static readonly Color ActionRowBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.34f);
        private static readonly Color PassiveRowBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f);

        int _currentTrainRunId = -1;

        public static RescueDispatchUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RescueDispatchUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RescueDispatchUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var rescueSvc = RescueService.Instance;

            // Najpierw: jeśli jest aktywne rescue (InProgress po dispatch) — pokaż countdown
            if (rescueSvc != null && rescueSvc.Ongoing.Count > 0)
            {
                ShowOngoing(rescueSvc.Ongoing[0]);
                return;
            }

            // Dispatch mode: sprawdź czy któryś run w AwaitingRescue
            var sim = TrainRunSimulator.Instance;
            if (sim == null) return;

            SimulatedTrain waitingTrain = null;
            foreach (var kvp in sim.ActiveTrains)
            {
                if (kvp.Value.state == TrainState.AwaitingRescue)
                {
                    waitingTrain = kvp.Value;
                    break;
                }
            }

            if (waitingTrain == null)
            {
                if (_panel.activeSelf) Hide();
                return;
            }

            if (_currentTrainRunId != waitingTrain.trainRun.id)
            {
                ShowForTrain(waitingTrain);
            }
            else
            {
                UpdateTimer(waitingTrain);
            }

            // Auto-dispatch po timeout
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            long elapsed = now - waitingTrain.breakdownStartedGameTime;
            if (elapsed > RescueService.AutoDispatchTimeoutSec)
            {
                AutoDispatch(waitingTrain);
            }
        }

        /// <summary>M7-3c: panel countdown podczas aktywnego rescue (phase Inbound/Returning).</summary>
        void ShowOngoing(OngoingRescue r)
        {
            if (!_panel.activeSelf) _panel.SetActive(true);

            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            long remaining;
            string phaseLabel;
            if (r.phase == RescuePhase.Inbound)
            {
                remaining = r.inboundFinishGameTime - now;
                phaseLabel = LocalizationService.Get("maintenance.rescue.phase.inbound");
            }
            else
            {
                remaining = r.returnFinishGameTime - now;
                phaseLabel = LocalizationService.Get("maintenance.rescue.phase.returning");
            }
            if (remaining < 0) remaining = 0;

            var rescueV = FleetService.GetOwnedById(r.rescueLocoId);
            string rescueName = rescueV != null ? $"{rescueV.series}-{rescueV.number}"
                : string.Format(LocalizationService.Get("maintenance.rescue.vehicle_fallback_format"), r.rescueLocoId);

            _titleText.text = string.Format(LocalizationService.Get("maintenance.rescue.ongoing.title_format"), r.brokenTrainRunId);
            _infoText.text = string.Format(LocalizationService.Get("maintenance.rescue.ongoing.info_format"),
                rescueName, phaseLabel,
                (r.pathLengthM / 1000f).ToString("F1"),
                remaining / 60, remaining % 60);

            // Wyczyść listę lok (w dispatch mode była lista — teraz tylko info)
            foreach (var row in _entryRows) if (row != null) Destroy(row);
            _entryRows.Clear();

            _currentTrainRunId = r.brokenTrainRunId;
        }

        void ShowForTrain(SimulatedTrain st)
        {
            _currentTrainRunId = st.trainRun.id;
            _panel.SetActive(true);

            var v = FleetService.GetOwnedById(st.brokenVehicleId);
            string vehName = v != null ? $"{v.series}-{v.number}"
                : string.Format(LocalizationService.Get("maintenance.rescue.broken_vehicle_fallback_format"), st.brokenVehicleId);
            string componentName = ((ComponentType)st.brokenComponentIndex).ToString();

            _titleText.text = string.Format(LocalizationService.Get("maintenance.rescue.dispatch.title_format"), st.trainRun.id, st.trainRun.trainNumberSnapshot);
            _infoText.text = string.Format(LocalizationService.Get("maintenance.rescue.dispatch.info_format"), vehName, componentName);

            RefreshCandidateList(st);
        }

        void UpdateTimer(SimulatedTrain st)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            long elapsed = now - st.breakdownStartedGameTime;
            long remaining = RescueService.AutoDispatchTimeoutSec - elapsed;
            if (remaining < 0) remaining = 0;

            var v = FleetService.GetOwnedById(st.brokenVehicleId);
            string vehName = v != null ? $"{v.series}-{v.number}"
                : string.Format(LocalizationService.Get("maintenance.rescue.broken_vehicle_fallback_format"), st.brokenVehicleId);
            string componentName = ((ComponentType)st.brokenComponentIndex).ToString();

            _infoText.text = string.Format(LocalizationService.Get("maintenance.rescue.dispatch.info_with_timer_format"),
                vehName, componentName, remaining / 60, remaining % 60);
        }

        void RefreshCandidateList(SimulatedTrain st)
        {
            foreach (var r in _entryRows)
                if (r != null) Destroy(r);
            _entryRows.Clear();

            // MVP: zakładamy linia elektryfikowana (post-EA: check railway layer segment)
            bool onElectrifiedLine = true;
            var candidates = RescueService.FindCandidates(onElectrifiedLine);

            if (candidates.Count == 0)
            {
                var empty = CreateEntryRow(LocalizationService.Get("maintenance.rescue.dispatch.no_candidates"), null);
                _entryRows.Add(empty);
                return;
            }

            foreach (var c in candidates)
            {
                string label = string.Format(LocalizationService.Get("maintenance.rescue.dispatch.candidate_format"),
                    c.series, c.number, c.type, c.conditionPercent.ToString("F0"));
                var captured = c;
                var row = CreateEntryRow(label, () => DispatchRescue(st, captured.id, "manual"));
                _entryRows.Add(row);
            }
        }

        void DispatchRescue(SimulatedTrain st, int rescueLocoId, string mode)
        {
            var rescueV = FleetService.GetOwnedById(rescueLocoId);
            string rescueName = rescueV != null ? $"{rescueV.series}-{rescueV.number}" : $"#{rescueLocoId}";

            Log.Info($"[Rescue] Dispatch ({mode}): loko {rescueName} → pociąg run#{st.trainRun.id}");

            // M7-3c: pełne rescue z pathfindingiem via RescueService
            var svc = RescueService.Instance ?? RescueService.EnsureExists();
            bool ok = svc.InitiateRescue(st, rescueLocoId);
            if (ok)
            {
                // UI pokaże countdown w ShowOngoing() w następnym framie (panel zostaje widoczny)
                _currentTrainRunId = st.trainRun.id;
                // Nie hide — panel będzie przełączony do InProgress mode
            }
            else
            {
                // Fallback instant-despawn — service sam załatwił vehicles, UI ukrywamy
                Hide();
            }
        }

        void AutoDispatch(SimulatedTrain st)
        {
            bool onElectrifiedLine = true;
            var candidates = RescueService.FindCandidates(onElectrifiedLine);
            if (candidates.Count == 0)
            {
                Log.Warn($"[Rescue] Auto-dispatch FAIL — brak wolnych lok. Pociąg run#{st.trainRun.id} stoi dalej.");
                // Reset timer żeby nie spamować logów
                st.breakdownStartedGameTime = (long)GameState.GameTimeSeconds
                                            + GameState.GameDay * 86400L;
                return;
            }

            // Pick first
            DispatchRescue(st, candidates[0].id, "auto");
        }

        void Hide()
        {
            _panel.SetActive(false);
            _currentTrainRunId = -1;
            foreach (var r in _entryRows) if (r != null) Destroy(r);
            _entryRows.Clear();
        }

        // ── UI builder ──────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGo = new GameObject("RescueDispatchCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel", typeof(RectTransform));
            _panel.transform.SetParent(_canvas.transform, false);
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(500f, 520f);
            var bg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(bg, PanelBg, UIShapePreset.PanelLarge);

            // Title
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(_panel.transform, false);
            var trt = titleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(-20f, 40f);
            trt.anchoredPosition = new Vector2(0f, -10f);
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_titleText, UIThemeTextRole.Danger);
            _titleText.fontSize = 18;
            _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.richText = true;
            _titleText.raycastTarget = false;

            // Info
            var infoGo = new GameObject("Info", typeof(RectTransform));
            infoGo.transform.SetParent(_panel.transform, false);
            var irt = infoGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 1f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(-20f, 110f);
            irt.anchoredPosition = new Vector2(0f, -60f);
            _infoText = infoGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_infoText, UIThemeTextRole.Primary);
            _infoText.fontSize = 14;
            _infoText.alignment = TextAlignmentOptions.TopLeft;
            _infoText.richText = true;
            _infoText.raycastTarget = false;
            _infoText.textWrappingMode = TextWrappingModes.Normal;
            _infoText.overflowMode = TextOverflowModes.Overflow;

            // Divider text
            var dvGo = new GameObject("Dv", typeof(RectTransform));
            dvGo.transform.SetParent(_panel.transform, false);
            var dvrt = dvGo.GetComponent<RectTransform>();
            dvrt.anchorMin = new Vector2(0f, 1f);
            dvrt.anchorMax = new Vector2(1f, 1f);
            dvrt.pivot = new Vector2(0.5f, 1f);
            dvrt.sizeDelta = new Vector2(-20f, 24f);
            dvrt.anchoredPosition = new Vector2(0f, -180f);
            var dv = dvGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(dv, UIThemeTextRole.Accent);
            dv.fontSize = 14;
            dv.alignment = TextAlignmentOptions.MidlineLeft;
            dv.raycastTarget = false;
            dv.text = LocalizationService.Get("maintenance.rescue.dispatch.candidates_label");

            // Scroll view for candidate list
            var listGo = new GameObject("List", typeof(RectTransform));
            listGo.transform.SetParent(_panel.transform, false);
            var lrt = listGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(10f, 10f);
            lrt.offsetMax = new Vector2(-10f, -210f);
            UITheme.ApplySurface(listGo.AddComponent<Image>(), ListBg, UIShapePreset.Panel);

            var scroll = listGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(listGo.transform, false);
            var vprt = viewport.GetComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = Vector2.zero; vprt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(viewport.AddComponent<Image>(), ListViewportBg, UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _listContent = content.GetComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = vprt; scroll.content = _listContent;
        }

        GameObject CreateEntryRow(string label, System.Action onClick)
        {
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 40f;
            var bg = rowGo.AddComponent<Image>();
            Color bgColor = onClick != null ? ActionRowBg : PassiveRowBg;
            UITheme.ApplySurface(bg, bgColor, onClick != null ? UIShapePreset.Button : UIShapePreset.Inset);

            if (onClick != null)
            {
                var btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.colors = UITheme.CreateColorBlock(
                    bgColor,
                    UITheme.RaisedSurface,
                    UITheme.Border,
                    bgColor,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                btn.onClick.AddListener(() => onClick());
            }

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(rowGo.transform, false);
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 0f); trt.offsetMax = new Vector2(-12f, 0f);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.fontSize = 14;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.text = label;

            return rowGo;
        }
    }
}
