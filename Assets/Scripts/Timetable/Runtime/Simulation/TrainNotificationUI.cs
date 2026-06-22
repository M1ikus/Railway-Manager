using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-2: Prosty corner popup notyfikacji o nadchodzących odjazdach z depot.
    /// Subskrybuje DispatchService.OnDepartureImminent, pokazuje listę pending runs
    /// w prawym górnym rogu ekranu. Popup znika gdy run zostanie usunięty (spawnowany lub expired).
    ///
    /// Na razie widoczny w OBU scenach (screen-space overlay). W M9c-5 może przenieść się
    /// do scene-specific HUD lub dodać scene filtering.
    /// </summary>
    public class TrainNotificationUI : MonoBehaviour
    {
        public static TrainNotificationUI Instance { get; private set; }

        readonly List<int> _activeRunIds = new();
        readonly Dictionary<int, NotificationEntry> _entries = new();

        // Reusable buffer dla collect-and-remove patternu w Update — unika
        // alokacji `new List<int>()` per frame.
        readonly List<int> _toRemoveBuffer = new();

        Canvas _canvas;
        GameObject _panel;

        private static readonly Color NotificationBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.32f);
        private static readonly Color NotificationBgLate = UITheme.WithAlpha(UITheme.Warning, 0.36f);

        class NotificationEntry
        {
            public GameObject root;
            public TextMeshProUGUI labelText;
            public TrainRun run;
        }

        public static TrainNotificationUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TrainNotificationUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TrainNotificationUI>();
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

        void OnEnable()
        {
            // Subscribe with delay — DispatchService może jeszcze nie istnieć przy Awake
            Invoke(nameof(SubscribeToDispatch), 0.1f);
        }

        void OnDisable()
        {
            if (DispatchService.Instance != null)
                DispatchService.Instance.OnDepartureImminent -= OnDepartureImminent;
        }

        void SubscribeToDispatch()
        {
            var svc = DispatchService.Instance ?? DispatchService.EnsureExists();
            svc.OnDepartureImminent += OnDepartureImminent;
            Log.Info("[TrainNotificationUI] Subscribed to DispatchService");
        }

        void Update()
        {
            // Per-frame refresh labeli (countdown). Iteruj kopię bo możemy usuwać.
            if (_entries.Count == 0) return;

            float now = GameState.GameTimeSeconds;
            _toRemoveBuffer.Clear();

            foreach (var kvp in _entries)
            {
                var entry = kvp.Value;
                if (entry.run == null || entry.run.isCompleted || entry.run.isCancelled)
                {
                    _toRemoveBuffer.Add(kvp.Key);
                    continue;
                }
                float depSec = entry.run.startMinutesFromMidnight * 60f + entry.run.currentDelaySec;
                float minutesLeft = (depSec - now) / 60f;

                // Auto-hide gdy vehicles już nie w depocie (spawn się zdarzył)
                if (!AnyVehicleInDepotForRun(entry.run))
                {
                    _toRemoveBuffer.Add(kvp.Key);
                    continue;
                }

                string prefix = minutesLeft >= 0 ? $"za {minutesLeft:F0}min" : $"SPÓŹNIONY o {-minutesLeft:F0}min";
                entry.labelText.text = $"<b>{entry.run.trainNumberSnapshot}</b> — {prefix}";
            }

            foreach (var kvp in _entries)
            {
                if (!_activeRunIds.Contains(kvp.Key))
                    continue;
                if (kvp.Value.run == null)
                    continue;

                float depSec = kvp.Value.run.startMinutesFromMidnight * 60f + kvp.Value.run.currentDelaySec;
                float minutesLeft = (depSec - now) / 60f;
                var bg = kvp.Value.root != null ? kvp.Value.root.GetComponent<Image>() : null;
                if (bg != null)
                    UITheme.ApplySurface(bg, minutesLeft >= 0 ? NotificationBg : NotificationBgLate, UIShapePreset.Pill);
            }

            foreach (int runId in _toRemoveBuffer)
                RemoveEntry(runId);
        }

        void OnDepartureImminent(TrainRun run, List<int> vehicleIds)
        {
            if (_entries.ContainsKey(run.id)) return;

            var entryGo = new GameObject($"Notif_{run.id}", typeof(RectTransform));
            entryGo.transform.SetParent(_panel.transform, false);
            var rt = entryGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(320f, 36f);

            // Explicit LayoutElement — VerticalLayoutGroup z childControlHeight=true
            // potrzebuje preferredHeight, inaczej entry ma wysokość 0
            var layoutEl = entryGo.AddComponent<LayoutElement>();
            layoutEl.preferredHeight = 36f;
            layoutEl.minHeight = 36f;

            var bg = entryGo.AddComponent<Image>();
            UITheme.ApplySurface(bg, NotificationBg, UIShapePreset.Pill);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(entryGo.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 2f);
            trt.offsetMax = new Vector2(-8f, -2f);

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.fontSize = 14;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.richText = true;
            txt.text = $"<b>{run.trainNumberSnapshot}</b> — nadchodzi";
            txt.raycastTarget = false;

            _entries[run.id] = new NotificationEntry { root = entryGo, labelText = txt, run = run };
            _activeRunIds.Add(run.id);

            Log.Info($"[TrainNotificationUI] Shown notification dla run#{run.id} '{run.trainNumberSnapshot}'");
        }

        void RemoveEntry(int runId)
        {
            if (_entries.TryGetValue(runId, out var e))
            {
                if (e.root != null) Destroy(e.root);
                _entries.Remove(runId);
                _activeRunIds.Remove(runId);
            }
        }

        static bool AnyVehicleInDepotForRun(TrainRun run)
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return false;
            var circulation = CirculationService.GetCirculation(run.circulationId);
            if (circulation == null) return false;
            var vehicles = circulation.GetVehiclesForDate(run.runDateIso);
            if (vehicles == null) return false;

            foreach (int vid in vehicles)
            {
                var rec = locSvc.Get(vid);
                if (rec != null && rec.type == VehicleLocationType.InDepot) return true;
            }
            return false;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("TrainNotificationCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 250;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("NotificationPanel", typeof(RectTransform));
            _panel.transform.SetParent(_canvas.transform, false);
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-20f, -80f); // pod TopBarUI
            prt.sizeDelta = new Vector2(348f, 400f);

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Xs;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
    }
}
