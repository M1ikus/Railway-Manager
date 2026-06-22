using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-8 / D20: Modal wyboru noclegu dla Overnight duty w multi-day turnusie.
    ///
    /// Pola:
    /// - Miasto (stacja noclegu) — InputField
    /// - Liczba nocy — dropdown 1-2 (EA max durationDays=3 = max 2 noce)
    /// - Hotel tier — radio buttons Basic/Standard/Premium + preview kosztu i morale delta
    ///
    /// Po OK: callback z utworzonym <see cref="HotelBooking"/>.
    /// Cancel: callback null.
    /// </summary>
    public class HotelBookingModal : MonoBehaviour
    {
        public static HotelBookingModal Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TMP_InputField _cityInput;
        TMP_Dropdown _nightsDropdown;
        TMP_Dropdown _tierDropdown;
        TextMeshProUGUI _costPreviewText;
        TextMeshProUGUI _moralePreviewText;

        Action<HotelBooking> _callback;
        int _currentEmployeeId = -1;
        string _checkInDateIso;
        bool _isVisible;

        private static readonly Color OverlayBg = UITheme.WithAlpha(Color.black, 0.82f);
        private static readonly Color BoxBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);

        public static HotelBookingModal EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("HotelBookingModal");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<HotelBookingModal>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Cancel();
        }

        public void Show(int employeeId, string checkInDateIso, string defaultCity, int defaultNights, Action<HotelBooking> callback)
        {
            _currentEmployeeId = employeeId;
            _checkInDateIso = checkInDateIso ?? "";
            _callback = callback;

            _cityInput.text = defaultCity ?? "";
            _nightsDropdown.SetValueWithoutNotify(Mathf.Clamp(defaultNights - 1, 0, 1));
            _tierDropdown.SetValueWithoutNotify((int)HotelBookingService.CompanyDefaultHotelTier);

            _root.SetActive(true);
            _isVisible = true;
            UpdatePreview();
        }

        void Cancel()
        {
            _root.SetActive(false);
            _isVisible = false;
            _callback?.Invoke(null);
            _callback = null;
        }

        void Confirm()
        {
            int nights = _nightsDropdown.value + 1;
            var tier = (HotelTier)Mathf.Clamp(_tierDropdown.value, 0, 2);
            string city = string.IsNullOrWhiteSpace(_cityInput.text) ? LocalizationService.Get("personnel.hotel.city_default") : _cityInput.text;

            var booking = HotelBookingService.CreateBooking(_currentEmployeeId, city, tier, nights, _checkInDateIso);
            _root.SetActive(false);
            _isVisible = false;
            _callback?.Invoke(booking);
            _callback = null;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("HotelModalCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150; // above editor
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = OverlayBg;

            var box = new GameObject("Box");
            box.transform.SetParent(_root.transform, false);
            var boxRt = box.AddComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0.5f, 0.5f); boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(520, 450);
            boxRt.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(box.AddComponent<Image>(), BoxBg, UIShapePreset.PanelLarge);

            // Title
            var title = UiHelper.CreateText(box.transform, "Title", LocalizationService.Get("personnel.hotel.title"), 17, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            Place(title.GetComponent<RectTransform>(), 0, 1, 0, 1, 0.5f, 1, new Vector2(0, -15), new Vector2(-20, 30));

            // City
            var cityLbl = UiHelper.CreateText(box.transform, "CityLbl", LocalizationService.Get("personnel.hotel.city_label"), 13, TextAlignmentOptions.MidlineLeft);
            Place(cityLbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(25, -65), new Vector2(150, 28));

            _cityInput = UiHelper.CreateInputField(box.transform, "CityInput", LocalizationService.Get("personnel.hotel.city_placeholder"));
            Place(_cityInput.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -60), new Vector2(-200, 30));

            // Nights
            var nightsLbl = UiHelper.CreateText(box.transform, "NightsLbl", LocalizationService.Get("personnel.hotel.nights_label"), 13, TextAlignmentOptions.MidlineLeft);
            Place(nightsLbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(25, -110), new Vector2(150, 28));

            _nightsDropdown = UiHelper.CreateDropdown(box.transform, "NightsDd",
                new System.Collections.Generic.List<string> {
                    LocalizationService.Get("personnel.hotel.nights_1"),
                    LocalizationService.Get("personnel.hotel.nights_2")
                });
            _nightsDropdown.onValueChanged.AddListener(_ => UpdatePreview());
            Place(_nightsDropdown.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -105), new Vector2(-200, 30));

            // Tier
            var tierLbl = UiHelper.CreateText(box.transform, "TierLbl", LocalizationService.Get("personnel.hotel.tier_label"), 13, TextAlignmentOptions.MidlineLeft);
            Place(tierLbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(25, -155), new Vector2(150, 28));

            _tierDropdown = UiHelper.CreateDropdown(box.transform, "TierDd",
                new System.Collections.Generic.List<string>
                {
                    LocalizationService.Get("personnel.hotel.tier_basic"),
                    LocalizationService.Get("personnel.hotel.tier_standard"),
                    LocalizationService.Get("personnel.hotel.tier_premium")
                });
            _tierDropdown.onValueChanged.AddListener(_ => UpdatePreview());
            Place(_tierDropdown.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -150), new Vector2(-200, 30));

            // Cost preview
            _costPreviewText = UiHelper.CreateText(box.transform, "CostPrev", "", 15, TextAlignmentOptions.Center);
            _costPreviewText.fontStyle = FontStyles.Bold;
            Place(_costPreviewText.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -215), new Vector2(-40, 30));

            _moralePreviewText = UiHelper.CreateText(box.transform, "MoralePrev", "", 12, TextAlignmentOptions.Center);
            _moralePreviewText.color = UITheme.SecondaryText;
            Place(_moralePreviewText.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -250), new Vector2(-40, 25));

            // Info
            var info = UiHelper.CreateText(box.transform, "Info",
                string.Format(LocalizationService.Get("personnel.hotel.info_format"), HotelBookingService.CompanyDefaultHotelTier),
                10, TextAlignmentOptions.Center);
            info.color = UITheme.SecondaryText;
            Place(info.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -290), new Vector2(-40, 40));

            // Buttons
            var okBtn = UiHelper.CreateButton(box.transform, "OK", LocalizationService.Get("personnel.hotel.ok_btn"), Confirm);
            Place(okBtn.GetComponent<RectTransform>(), 0.5f, 0, 0.5f, 0, 1, 0, new Vector2(-10, 20), new Vector2(200, 40));
            if (okBtn.TryGetComponent<Image>(out var okImg))
                UITheme.ApplySurface(okImg, UITheme.WithAlpha(UITheme.Success, 0.8f), UIShapePreset.Button);

            var cancelBtn = UiHelper.CreateButton(box.transform, "Cancel", LocalizationService.Get("personnel.hotel.cancel_btn"), Cancel);
            Place(cancelBtn.GetComponent<RectTransform>(), 0.5f, 0, 0.5f, 0, 0, 0, new Vector2(10, 20), new Vector2(200, 40));
            if (cancelBtn.TryGetComponent<Image>(out var cancelImg))
                UITheme.ApplySurface(cancelImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.86f), UIShapePreset.Button);
        }

        void UpdatePreview()
        {
            int nights = _nightsDropdown.value + 1;
            var tier = (HotelTier)Mathf.Clamp(_tierDropdown.value, 0, 2);
            int cost = HotelBookingService.ComputeCost(tier, nights);
            _costPreviewText.text = string.Format(LocalizationService.Get("personnel.hotel.cost_format"), cost / 100);

            int moraleDelta = tier switch
            {
                HotelTier.Basic => PersonnelBalanceConstants.HotelMoraleBasicPenalty * nights,
                HotelTier.Premium => PersonnelBalanceConstants.HotelMoralePremiumBonus * nights,
                _ => 0
            };
            string mcolor = ToHtmlColor(moraleDelta > 0 ? UITheme.Success : moraleDelta < 0 ? UITheme.Danger : UITheme.SecondaryText);
            _moralePreviewText.text = string.Format(LocalizationService.Get("personnel.hotel.morale_format"),
                mcolor, moraleDelta.ToString("+0;-0;0"));
        }

        static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }

        static void Place(RectTransform rt, float amin_x, float amin_y, float amax_x, float amax_y, float piv_x, float piv_y, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(amin_x, amin_y);
            rt.anchorMax = new Vector2(amax_x, amax_y);
            rt.pivot = new Vector2(piv_x, piv_y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
