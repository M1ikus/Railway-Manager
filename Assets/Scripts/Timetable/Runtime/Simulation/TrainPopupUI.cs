using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using DepotSystem;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Popup UI z informacjami o klikniętym pociągu.
    /// Tworzony programatycznie w MapScene, pokazuje się po kliknięciu pociągu
    /// i auto-aktualizuje dane per frame (prędkość, pozycja, opóźnienie).
    /// </summary>
    public class TrainPopupUI : MonoBehaviour
    {
        public static TrainPopupUI Instance { get; private set; }

        SimulatedTrain _shown;
        bool _followCamera;

        // M-Windows P3-ops (Mapa): treść w pływającym oknie zamiast corner-panelu.
        FloatingWindow _win;
        TextMeshProUGUI _infoText;          // live (trasa/prędkość/opóźnienie/next-stop) — UpdateInfo per frame
        TextMeshProUGUI _followButtonLabel;

        // Camera follow
        Camera _mapCamera;
        Vector3 _savedCameraOffset;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            TrainMarker.OnAnyTrainClicked += OnTrainClicked;
        }

        void OnDestroy()
        {
            TrainMarker.OnAnyTrainClicked -= OnTrainClicked;
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (_shown == null) return;

            // Sprawdź czy pociąg nadal istnieje
            if (_shown.visual == null)
            {
                Hide();
                return;
            }

            UpdateInfo();

            if (_followCamera)
                FollowCamera();
        }

        // ── Click handling ──────────────────────────────────────────

        void OnTrainClicked(TrainMarker marker)
        {
            if (marker == null || marker.SimulatedTrain == null) return;
            Show(marker.SimulatedTrain);
        }

        public void Show(SimulatedTrain st)
        {
            if (st == null) return;
            _shown = st;
            string category = IrjCategoryCatalog.GetCode(st.timetable.irjCategory);
            string title = string.Format(LocalizationService.Get("popup_train.title_format"),
                st.trainRun.trainNumberSnapshot, category);
            _win = WindowManager.Instance.OpenWindow("train:" + st.trainRun.id, title, new Vector2(380f, 480f));
            _win.OnClosed -= OnWindowClosed;
            _win.OnClosed += OnWindowClosed;
            BuildWindowContent(_win, st);
            UpdateInfo();
            Log.Info($"[TrainPopupUI] Show: '{st.trainRun.trainNumberSnapshot}'");
        }

        public void Hide()
        {
            _shown = null;
            _followCamera = false;
            if (_win != null)
            {
                _win.OnClosed -= OnWindowClosed; // programowy close nie jest user-requestem
                _win.Close();
                _win = null;
            }
        }

        void OnWindowClosed()
        {
            // user kliknął ✕ w oknie
            _win = null;
            _shown = null;
            _followCamera = false;
        }

        // ── Update info ─────────────────────────────────────────────

        void UpdateInfo()
        {
            if (_shown == null) return;

            var tr = _shown.trainRun;
            var tt = _shown.timetable;
            var stops = tt.stops;

            float kmDone = tr.currentPositionOnRouteM / 1000f;
            float kmTotal = _shown.route.totalLengthM / 1000f;
            float speedKmh = _shown.currentSpeedMps * 3.6f;

            string nextStop = LocalizationService.Get("popup_train.delay_dash");
            string nextEta = LocalizationService.Get("popup_train.delay_dash");
            if (_shown.currentStopIndex < stops.Count)
            {
                var next = stops[_shown.currentStopIndex];
                nextStop = next.stationName;
                int etaSec = (int)(_shown.departureTimeOfDaySec + next.plannedArrivalSec - GameState.GameTimeSeconds);
                if (etaSec < 0) etaSec = 0;
                nextEta = string.Format(LocalizationService.Get("popup_train.eta_format"), etaSec / 60, etaSec % 60);
            }

            string delayStr = tr.currentDelaySec > 0
                ? string.Format(LocalizationService.Get("popup_train.delay_format"), tr.currentDelaySec)
                : LocalizationService.Get("popup_train.delay_zero");

            string stateStr = _shown.state switch
            {
                TrainState.WaitingToDepart => LocalizationService.Get("popup_train.state.waiting"),
                TrainState.Running => LocalizationService.Get("popup_train.state.running"),
                TrainState.StoppedAtStation => LocalizationService.Get("popup_train.state.stopped"),
                TrainState.BlockedBySignal => LocalizationService.Get("popup_train.state.blocked"),
                TrainState.Completed => LocalizationService.Get("popup_train.state.completed"),
                _ => _shown.state.ToString()
            };

            if (_infoText == null) return;
            _infoText.text = string.Format(LocalizationService.Get("popup_train.info_format"),
                _shown.route.name, tt.name, stateStr,
                speedKmh.ToString("F0"),
                kmDone.ToString("F1"), kmTotal.ToString("F1"),
                delayStr, nextStop, nextEta);
        }

        // ── Camera follow ───────────────────────────────────────────

        void FollowCamera()
        {
            if (_mapCamera == null) _mapCamera = FindMapCamera();
            if (_mapCamera == null || _shown.visualTransform == null) return;

            var tpos = _shown.visualTransform.position;
            var cpos = _mapCamera.transform.position;
            // Utrzymuj oryginalny Y i X-Z offset (żeby zoom i kąt kamery zostały)
            var newPos = new Vector3(tpos.x + _savedCameraOffset.x, cpos.y, tpos.z + _savedCameraOffset.z);
            _mapCamera.transform.position = newPos;
        }

        void ToggleFollow()
        {
            _followCamera = !_followCamera;
            _followButtonLabel.text = LocalizationService.Get(_followCamera
                ? "popup_train.stop_follow_btn"
                : "popup_train.follow_btn");

            if (_followCamera)
            {
                if (_mapCamera == null) _mapCamera = FindMapCamera();
                if (_mapCamera != null && _shown != null && _shown.visualTransform != null)
                {
                    var cpos = _mapCamera.transform.position;
                    var tpos = _shown.visualTransform.position;
                    _savedCameraOffset = new Vector3(cpos.x - tpos.x, 0f, cpos.z - tpos.z);
                }
            }
        }

        static Camera FindMapCamera()
        {
            int mapLayerMask = 1 << 31;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.orthographic) continue;
                if ((cam.cullingMask & mapLayerMask) != 0)
                    return cam;
            }
            return Camera.main;
        }

        // ── UI build ────────────────────────────────────────────────

        void BuildWindowContent(FloatingWindow win, SimulatedTrain st)
        {
            var root = win.ContentRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);

            const float infoH = 150f;
            const float footerH = 46f;

            // Live info (góra) — trasa/stan/prędkość/km/opóźnienie/next-stop, aktualizowane w UpdateInfo
            var info = UIPrimitives.MakeTMP("Info", root, UITheme.Typography.Small,
                UIThemeTextRole.Primary, TextAlignmentOptions.TopLeft);
            info.richText = true;
            info.textWrappingMode = TextWrappingModes.Normal;
            var iRT = info.rectTransform;
            iRT.anchorMin = new Vector2(0f, 1f); iRT.anchorMax = new Vector2(1f, 1f);
            iRT.pivot = new Vector2(0.5f, 1f);
            iRT.anchoredPosition = new Vector2(0f, -UITheme.Spacing.Sm);
            iRT.sizeDelta = new Vector2(-UITheme.Spacing.Md, infoH);
            _infoText = info;

            // Chipy pojazdów (scroll) — drill-down do okna pojazdu (P2)
            var list = WindowScroll.BuildVertical(root, infoH + UITheme.Spacing.Sm, footerH);
            var ids = st.trainRun.runningVehicleIds;
            if (ids != null && ids.Count > 0)
            {
                for (int i = 0; i < ids.Count; i++)
                    ConsistWindowUI.BuildVehicleRow(list, ids[i]);
            }
            else
            {
                var empty = UIPrimitives.MakeTMP("Empty", list, UITheme.Typography.Small, UIThemeTextRole.Secondary);
                empty.text = "—";
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            }

            // Follow-camera (dół)
            var footer = new GameObject("Footer", typeof(RectTransform));
            footer.transform.SetParent(root, false);
            var fRT = (RectTransform)footer.transform;
            fRT.anchorMin = new Vector2(0f, 0f); fRT.anchorMax = new Vector2(1f, 0f);
            fRT.pivot = new Vector2(0.5f, 0f);
            fRT.anchoredPosition = new Vector2(0f, UITheme.Spacing.Xs);
            fRT.sizeDelta = new Vector2(-UITheme.Spacing.Md, footerH - UITheme.Spacing.Xs);
            var hlg = footer.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            var followBtn = UIBuilders.MakeButton(footer.transform,
                LocalizationService.Get(_followCamera ? "popup_train.stop_follow_btn" : "popup_train.follow_btn"),
                UIButtonTone.Secondary);
            followBtn.onClick.AddListener(ToggleFollow);
            _followButtonLabel = followBtn.GetComponentInChildren<TextMeshProUGUI>();
        }

    }
}
