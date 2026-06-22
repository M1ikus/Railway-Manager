using TMPro;
using RailwayManager;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MapSystem
{
    /// <summary>
    /// Pionowy suwak zoomu po prawej stronie ekranu.
    /// Synchronizuje sie z CameraController (scroll wheel tez zmienia zoom).
    /// Skala logarytmiczna dla naturalnego "feelu".
    /// </summary>
    public class MapZoomSliderUI : MonoBehaviour
    {
        private Slider slider;
        private CameraController cameraController;

        private float minZoom = 50f;
        private float maxZoom = 50000f;
        private bool updatingFromCamera;

        // Cache ostatnio zsynchronizowanego zoom — SyncSliderFromCamera skipuje pełną
        // update gdy zoom się nie zmienił. NaN initial żeby pierwszy frame zawsze przeszedł.
        private float _lastSyncedZoom = float.NaN;

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            cameraController = FindAnyObjectByType<CameraController>();

            if (cameraController != null)
            {
                minZoom = cameraController.minZoom;
                maxZoom = cameraController.maxZoom;
                SyncSliderFromCamera();
            }
        }

        void Update()
        {
            if (cameraController != null)
                SyncSliderFromCamera();

            // Deselect slider po release (żeby strzałki/scroll nie były capture'owane gdy
            // gracz nie zamierza już dragować). Wcześniej deselect całej sceny przy każdym
            // kliknięciu w mapie zabierał focus innym InputField'om — teraz tylko gdy
            // aktualnie wybrany jest sam slider.
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame
                && EventSystem.current != null
                && slider != null
                && EventSystem.current.currentSelectedGameObject == slider.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void SyncSliderFromCamera()
        {
            if (slider == null || cameraController == null) return;

            float zoom = cameraController.GetZoom();
            // Skip gdy zoom się nie zmienił — pisanie slider.value triggeruje onValueChanged
            // (chronione przez updatingFromCamera flag, ale i tak waste w typowym idle frame).
            // Epsilon 0.01 wystarczy dla zoom range 50..50000 (relative precision).
            if (!float.IsNaN(_lastSyncedZoom) && Mathf.Abs(zoom - _lastSyncedZoom) < 0.01f) return;
            _lastSyncedZoom = zoom;

            updatingFromCamera = true;
            slider.value = ZoomToSlider(zoom);
            updatingFromCamera = false;
        }

        private void OnSliderChanged(float value)
        {
            if (updatingFromCamera) return;
            if (cameraController == null) return;

            float zoom = SliderToZoom(value);
            cameraController.SetZoom(zoom);
        }

        private float SliderToZoom(float sliderValue)
        {
            float t = 1f - sliderValue;
            return minZoom * Mathf.Pow(maxZoom / minZoom, t);
        }

        private float ZoomToSlider(float zoom)
        {
            if (zoom <= minZoom) return 1f;
            if (zoom >= maxZoom) return 0f;
            float t = Mathf.Log(zoom / minZoom) / Mathf.Log(maxZoom / minZoom);
            return 1f - t;
        }

        private void BuildUI()
        {
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-16, -20);
            rt.sizeDelta = new Vector2(56, 356);

            Image bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.94f), UIShapePreset.PanelLarge);

            VerticalLayoutGroup vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(transform, false);
            titleObj.AddComponent<RectTransform>();
            titleObj.AddComponent<LayoutElement>().preferredHeight = 18f;
            TextMeshProUGUI title = titleObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(title, UIThemeTextRole.Secondary);
            title.fontSize = 10;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.text = "ZOOM";

            Button plusBtn = CreateZoomButton(transform, "ZoomIn", "+");
            plusBtn.onClick.AddListener(() =>
            {
                if (slider != null)
                    slider.value = Mathf.Clamp01(slider.value + 0.05f);
            });

            GameObject sliderObj = CreateSlider(transform);

            Button minusBtn = CreateZoomButton(transform, "ZoomOut", "\u2013");
            minusBtn.onClick.AddListener(() =>
            {
                if (slider != null)
                    slider.value = Mathf.Clamp01(slider.value - 0.05f);
            });
        }

        private Button CreateZoomButton(Transform parent, string name, string label)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(42, 32);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 32;
            le.preferredWidth = 42;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.96f), UIShapePreset.Pill);

            Button btn = obj.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.96f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.98f),
                UITheme.WithAlpha(UITheme.PrimarySurface, 0.98f),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.96f),
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.targetGraphic = bg;

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(obj.transform, false);
            RectTransform labelRt = labelObj.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            text.fontSize = 18;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;

            return btn;
        }

        private GameObject CreateSlider(Transform parent)
        {
            GameObject sliderCard = new GameObject("SliderCard");
            sliderCard.transform.SetParent(parent, false);
            sliderCard.AddComponent<RectTransform>();
            var sliderCardLe = sliderCard.AddComponent<LayoutElement>();
            sliderCardLe.flexibleHeight = 1f;
            sliderCardLe.minHeight = 160f;
            Image sliderCardBg = sliderCard.AddComponent<Image>();
            UITheme.ApplySurface(sliderCardBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.94f), UIShapePreset.Inset);

            VerticalLayoutGroup sliderCardLayout = sliderCard.AddComponent<VerticalLayoutGroup>();
            sliderCardLayout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Md);
            sliderCardLayout.spacing = UITheme.Spacing.Sm;
            sliderCardLayout.childAlignment = TextAnchor.MiddleCenter;
            sliderCardLayout.childForceExpandWidth = true;
            sliderCardLayout.childForceExpandHeight = true;
            sliderCardLayout.childControlWidth = true;
            sliderCardLayout.childControlHeight = true;

            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(sliderCard.transform, false);

            sliderObj.AddComponent<RectTransform>();

            LayoutElement le = sliderObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.preferredWidth = 30;

            slider = sliderObj.AddComponent<Slider>();
            slider.direction = Slider.Direction.BottomToTop;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.5f;
            slider.onValueChanged.AddListener(OnSliderChanged);
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRt = bgObj.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.35f, 0);
            bgRt.anchorMax = new Vector2(0.65f, 1);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bgImg = bgObj.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.96f), UIShapePreset.Pill);

            GameObject fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0.35f, 0);
            fillAreaRt.anchorMax = new Vector2(0.65f, 1);
            fillAreaRt.offsetMin = Vector2.zero;
            fillAreaRt.offsetMax = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            Image fillImg = fill.AddComponent<Image>();
            UITheme.ApplySurface(fillImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.72f), UIShapePreset.Pill);

            GameObject handleArea = new GameObject("HandleSlideArea");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(2, 8);
            handleAreaRt.offsetMax = new Vector2(-2, -8);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRt = handle.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24, 16);
            Image handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.PrimaryText, UIShapePreset.Pill);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;

            return sliderCard;
        }
    }
}
