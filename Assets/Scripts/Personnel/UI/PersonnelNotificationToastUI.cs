using System;
using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-6 / D21: Toast notifications w prawym gornym rogu dla eventow Personnel.
    ///
    /// Typy toast'ow:
    /// - <b>CrewVacancy</b> (urgent): L4/urlop/emerytura uniemozliwia OnShift dzis/jutro.
    ///   3 buttony: [Auto-dyspozytor] [Manual...] [Cancel]
    ///   Auto-timeout 1h game time → jesli dyspozytor dostepny = auto-assign (M8-7 stub).
    /// - <b>Info</b>: emerytura ogloszona, pracownik wrocil z L4, nowy zatrudniony.
    ///   1 button [X] (dismiss).
    ///
    /// UI: stack od gory, max 5 widocznych (reszta w queue), 1 toast ~400×100.
    /// </summary>
    public class PersonnelNotificationToastUI : MonoBehaviour
    {
        public static PersonnelNotificationToastUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _stack;
        readonly List<ToastEntry> _active = new();
        readonly Queue<ToastEntry> _pending = new();

        private static readonly Color VacancyToastBg = UITheme.WithAlpha(UITheme.Warning, 0.95f);
        private static readonly Color InfoToastBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.95f);
        private static readonly Color SuccessToastBg = UITheme.WithAlpha(UITheme.Success, 0.95f);
        private static readonly Color DangerToastBg = UITheme.WithAlpha(UITheme.Danger, 0.98f);

        const int MaxVisibleToasts = 5;
        const float AutoExpireGameSec = 3600f; // 1h game time (countdown przy normal speed)
        // BUG-041: real-time max — toast znika nawet przy pauzie (po N sec real time).
        // Bez tego toasty kumulują się gdy gracz pauzuje grę.
        const float AutoExpireRealSec = 60f; // 60s real-time wall-clock cap

        public static PersonnelNotificationToastUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PersonnelNotificationToastUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PersonnelNotificationToastUI>();
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
            PersonnelEvents.OnCrewVacancyDetected += OnCrewVacancy;
            PersonnelEvents.OnRetirementAnnounced += OnRetirementAnnounced;
            PersonnelEvents.OnEmployeeRetired += OnEmployeeRetired;
            PersonnelEvents.OnEmployeeRecovered += OnEmployeeRecovered;
        }

        void OnDisable()
        {
            PersonnelEvents.OnCrewVacancyDetected -= OnCrewVacancy;
            PersonnelEvents.OnRetirementAnnounced -= OnRetirementAnnounced;
            PersonnelEvents.OnEmployeeRetired -= OnEmployeeRetired;
            PersonnelEvents.OnEmployeeRecovered -= OnEmployeeRecovered;
        }

        void Update()
        {
            // BUG-041: hybrid expire — game-time (gdy gra biegnie) LUB real-time (gdy pauza/x500).
            // Bez real-time fallback toasty zamrażały się przy pauzie i kumulowały na ekranie.
            float deltaGame = Time.deltaTime * GameState.TimeScale;
            float deltaReal = Time.unscaledDeltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var t = _active[i];
                t.gameTimeRemaining -= deltaGame;
                t.realTimeRemaining -= deltaReal;
                if (t.gameTimeRemaining <= 0f || t.realTimeRemaining <= 0f)
                {
                    OnAutoExpire(t);
                    RemoveToast(t);
                }
            }
        }

        // ═══ Event handlers ═══

        void OnCrewVacancy(CrewVacancyData data)
        {
            var emp = PersonnelService.GetById(data.employeeId);
            if (emp == null) return;

            string msg = data.customMessage ?? BuildCrewVacancyMessage(emp, data);
            var toast = new ToastEntry
            {
                type = ToastType.CrewVacancy,
                message = msg,
                vacancyData = data,
                gameTimeRemaining = AutoExpireGameSec,
                realTimeRemaining = AutoExpireRealSec,
                color = VacancyToastBg
            };
            Enqueue(toast);
        }

        void OnRetirementAnnounced(Employee e)
        {
            string msg = string.Format(LocalizationService.Get("personnel.toast.retirement_announced_format"),
                e.DisplayFullName, RoleDefinitions.GetDisplayNamePl(e.role),
                e.age, e.retirementEndDateIso);
            Enqueue(new ToastEntry
            {
                type = ToastType.Info,
                message = msg,
                gameTimeRemaining = AutoExpireGameSec * 2f,
                realTimeRemaining = AutoExpireRealSec * 2f,
                color = InfoToastBg
            });
        }

        void OnEmployeeRetired(Employee e)
        {
            string msg = string.Format(LocalizationService.Get("personnel.toast.retirement_done_format"),
                e.DisplayFullName,
                e.currentSalaryGroszy * PersonnelBalanceConstants.RetirementSeveranceMonths / 100);
            Enqueue(new ToastEntry
            {
                type = ToastType.Info,
                message = msg,
                gameTimeRemaining = AutoExpireGameSec,
                realTimeRemaining = AutoExpireRealSec,
                color = InfoToastBg
            });
        }

        void OnEmployeeRecovered(Employee e)
        {
            string msg = string.Format(LocalizationService.Get("personnel.toast.recovered_format"),
                e.DisplayFullName);
            Enqueue(new ToastEntry
            {
                type = ToastType.Info,
                message = msg,
                gameTimeRemaining = AutoExpireGameSec * 0.5f,
                realTimeRemaining = AutoExpireRealSec * 0.5f,
                color = SuccessToastBg
            });
        }

        static string BuildCrewVacancyMessage(Employee e, CrewVacancyData data)
        {
            string reasonText = data.reason switch
            {
                CrewVacancyReason.SickLeave => string.Format(LocalizationService.Get("personnel.toast.reason.sick_format"), e.sickUntilDateIso),
                CrewVacancyReason.Vacation => LocalizationService.Get("personnel.toast.reason.vacation"),
                CrewVacancyReason.RetirementDeparture => LocalizationService.Get("personnel.toast.reason.retirement"),
                CrewVacancyReason.Training => LocalizationService.Get("personnel.toast.reason.training"),
                _ => LocalizationService.Get("personnel.toast.reason.other")
            };
            return string.Format(LocalizationService.Get("personnel.toast.vacancy_format"),
                e.DisplayFullName, RoleDefinitions.GetDisplayNamePl(e.role), reasonText, data.affectedDateIso);
        }

        // ═══ Queue management ═══

        void Enqueue(ToastEntry toast)
        {
            if (_active.Count < MaxVisibleToasts) SpawnToast(toast);
            else _pending.Enqueue(toast);
        }

        void SpawnToast(ToastEntry toast)
        {
            toast.go = BuildToastGO(toast);
            _active.Add(toast);
            Relayout();
        }

        void RemoveToast(ToastEntry toast)
        {
            if (toast.go != null) Destroy(toast.go);
            _active.Remove(toast);
            Relayout();
            // Promote pending
            while (_active.Count < MaxVisibleToasts && _pending.Count > 0)
            {
                var next = _pending.Dequeue();
                SpawnToast(next);
            }
        }

        void Relayout()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].go == null) continue;
                var rt = _active[i].go.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(-15, -15 - i * 115);
            }
        }

        // ═══ Toast GO ═══

        GameObject BuildToastGO(ToastEntry toast)
        {
            var go = new GameObject($"Toast_{toast.type}");
            go.transform.SetParent(_stack.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(440, 105);
            UITheme.ApplySurface(go.AddComponent<Image>(), toast.color, UIShapePreset.Panel);

            // Message
            var msgText = UiHelper.CreateText(go.transform, "Msg", toast.message, 12, TextAlignmentOptions.TopLeft);
            msgText.color = UITheme.PrimaryText;
            var mrt = msgText.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0, 0.5f); mrt.anchorMax = new Vector2(1, 1);
            mrt.offsetMin = new Vector2(10, 5); mrt.offsetMax = new Vector2(-10, -5);
            msgText.textWrappingMode = TextWrappingModes.Normal;
            msgText.overflowMode = TextOverflowModes.Truncate;

            // Buttons
            if (toast.type == ToastType.CrewVacancy)
            {
                BuildCrewVacancyButtons(go.transform, toast);
            }
            else
            {
                var dismiss = UiHelper.CreateButton(go.transform, "Dismiss", LocalizationService.Get("personnel.toast.btn.dismiss"), () => RemoveToast(toast));
                var dr = dismiss.GetComponent<RectTransform>();
                dr.anchorMin = new Vector2(1, 0); dr.anchorMax = new Vector2(1, 0);
                dr.pivot = new Vector2(1, 0);
                dr.anchoredPosition = new Vector2(-5, 5);
                dr.sizeDelta = new Vector2(40, 30);
            }

            return go;
        }

        void BuildCrewVacancyButtons(Transform parent, ToastEntry toast)
        {
            // [Auto-dyspozytor] [Manual...] [Cancel]
            var autoBtn = UiHelper.CreateButton(parent, "AutoBtn", LocalizationService.Get("personnel.toast.btn.auto_dispatcher"),
                () => OnCrewVacancyAuto(toast));
            var ar = autoBtn.GetComponent<RectTransform>();
            ar.anchorMin = new Vector2(0, 0); ar.anchorMax = new Vector2(0, 0);
            ar.pivot = new Vector2(0, 0);
            ar.anchoredPosition = new Vector2(5, 5);
            ar.sizeDelta = new Vector2(135, 32);

            var manualBtn = UiHelper.CreateButton(parent, "ManualBtn", LocalizationService.Get("personnel.toast.btn.manual"),
                () => OnCrewVacancyManual(toast));
            var mr = manualBtn.GetComponent<RectTransform>();
            mr.anchorMin = new Vector2(0, 0); mr.anchorMax = new Vector2(0, 0);
            mr.pivot = new Vector2(0, 0);
            mr.anchoredPosition = new Vector2(145, 5);
            mr.sizeDelta = new Vector2(100, 32);

            var cancelBtn = UiHelper.CreateButton(parent, "CancelBtn", LocalizationService.Get("personnel.toast.btn.ignore"),
                () => RemoveToast(toast));
            var cr = cancelBtn.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 0); cr.anchorMax = new Vector2(0, 0);
            cr.pivot = new Vector2(0, 0);
            cr.anchoredPosition = new Vector2(250, 5);
            cr.sizeDelta = new Vector2(100, 32);
        }

        // ═══ Actions ═══

        void OnCrewVacancyAuto(ToastEntry toast)
        {
            // M8-7: integracja z DispatcherService (D21 + D27).
            if (toast.vacancyData == null) { RemoveToast(toast); return; }

            var result = DispatcherService.TryAutoAssignReplacement(toast.vacancyData);
            switch (result)
            {
                case DispatchResult.Success:
                    Log.Info($"[PersonnelNotificationToastUI] Dispatcher auto-assigned replacement for " +
                             $"vacancy employee #{toast.vacancyData.employeeId}");
                    RemoveToast(toast);
                    break;

                case DispatchResult.NoDispatcher:
                    // Zostaw toast — gracz musi zatrudnic dyspozytora lub kliknac Manual
                    Log.Warn("[PersonnelNotificationToastUI] No active dispatcher — can't auto-assign");
                    ShowInlineWarning(toast, LocalizationService.Get("personnel.toast.warn.no_dispatcher"));
                    break;

                case DispatchResult.NoCandidateFound:
                    Log.Warn("[PersonnelNotificationToastUI] No replacement candidate available");
                    ShowInlineWarning(toast, LocalizationService.Get("personnel.toast.warn.no_candidate"));
                    break;

                case DispatchResult.Missed:
                    Log.Warn("[PersonnelNotificationToastUI] Dispatcher over-capacity — action missed");
                    ShowInlineWarning(toast, LocalizationService.Get("personnel.toast.warn.overloaded"));
                    break;

                default:
                    Log.Warn($"[PersonnelNotificationToastUI] Dispatch failed: {result}");
                    RemoveToast(toast);
                    break;
            }
        }

        void ShowInlineWarning(ToastEntry toast, string warning)
        {
            if (toast.go == null) return;
            // Dopisz ostrzezenie do message i zaswietl toast na czerwono
            var txt = toast.go.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null)
                txt.text += string.Format(LocalizationService.Get("personnel.toast.warn.format"), warning);
            var img = toast.go.GetComponent<Image>();
            if (img != null) UITheme.ApplySurface(img, DangerToastBg, UIShapePreset.Panel);
        }

        void OnCrewVacancyManual(ToastEntry toast)
        {
            // M8-6: stub — modal z lista wolnych (M8-13 gdy integracja M5+M9 gotowa).
            if (toast.vacancyData == null) { RemoveToast(toast); return; }
            Log.Info($"[PersonnelNotificationToastUI] [STUB] Manual replacement modal — " +
                     $"will open in M8-11/M8-13 when CrewAssignmentService active.");
            RemoveToast(toast);
        }

        void OnAutoExpire(ToastEntry toast)
        {
            if (toast.type == ToastType.CrewVacancy)
            {
                // D21: timeout 1h — jesli dispatcher dostepny = auto-assign, inaczej = cancel pociag
                Log.Warn($"[PersonnelNotificationToastUI] Vacancy auto-timeout expired " +
                         $"(D21: would trigger dispatcher check or cancel in M8-7+)");
            }
        }

        // ═══ Build UI canvas ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("ToastCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200; // above everything
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _stack = new GameObject("Stack");
            _stack.transform.SetParent(canvasGo.transform, false);
            var rt = _stack.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-12, -72);
            rt.sizeDelta = new Vector2(468, 620);
        }

        // ═══ Debug API ═══

        public void DebugClearAll()
        {
            foreach (var t in new List<ToastEntry>(_active))
                if (t.go != null) Destroy(t.go);
            _active.Clear();
            _pending.Clear();
        }

        public int ActiveCount => _active.Count;
        public int PendingCount => _pending.Count;

        // ═══ Internal types ═══

        enum ToastType { Info, CrewVacancy }

        class ToastEntry
        {
            public ToastType type;
            public string message;
            public CrewVacancyData vacancyData;
            public float gameTimeRemaining;
            public float realTimeRemaining; // BUG-041
            public Color color;
            public GameObject go;
        }
    }
}
