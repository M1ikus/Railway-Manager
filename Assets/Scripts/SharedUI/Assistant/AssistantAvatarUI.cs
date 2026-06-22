using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-1c: stała obecność asystenta — awatar w LEWYM-DOLNYM rogu (warstwa [0] UX:
    /// strefowanie — prawy-górny to „strefa alertów" Maintenance/Personnel/Train, lewy-dolny
    /// to companion „mam pomysł / mogę pomóc"). Portret 2D placeholder = inicjał na pill
    /// (M-Models dostarczy grafikę). Klik → panel (PULL). Kropka-badge sygnalizuje sugestię.
    /// </summary>
    public class AssistantAvatarUI : MonoBehaviour
    {
        public static AssistantAvatarUI Instance { get; private set; }

        /// <summary>Pasmo 160-199 wolne wg recon UX — companion pod panelami (200+), nad gameplay toolbarami.</summary>
        public const int SortingOrder = 175;

        const float AvatarSize = 56f;
        const float BadgeSize = 14f;
        const float ScreenMargin = 12f;

        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }

        public event System.Action OnAvatarClicked;

        GameObject _badge;
        TextMeshProUGUI _initial;

        public static AssistantAvatarUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AssistantAvatarUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AssistantAvatarUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            AssistantState.OnDisplayNameChanged += RefreshInitial; // GameCreator/load zmienia imię (AS-1d)
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            AssistantState.OnDisplayNameChanged -= RefreshInitial;
        }

        /// <summary>Kropka „mam sugestię" — ambient PUSH warstwy [1].</summary>
        public void SetBadge(bool visible)
        {
            if (_badge != null) _badge.SetActive(visible);
        }

        /// <summary>Scene-aware visibility (Depot/MapScene) — steruje AssistantOrchestrator.</summary>
        public void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible)
                Root.gameObject.SetActive(visible);
        }

        /// <summary>Odświeża inicjał + tooltip po zmianie imienia (GameCreator/load — AS-1d).</summary>
        public void RefreshInitial()
        {
            var name = AssistantState.DisplayName;
            if (_initial != null)
                _initial.text = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString();
            if (_tooltip != null)
                _tooltip.text = name;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("AssistantHudCanvas");
            canvasGo.transform.SetParent(transform);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = SortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("AvatarRoot", typeof(RectTransform));
            _root.transform.SetParent(Canvas.transform, false);
            Root = _root.GetComponent<RectTransform>();
            Root.anchorMin = Vector2.zero;
            Root.anchorMax = Vector2.zero;
            Root.pivot = Vector2.zero;
            Root.sizeDelta = new Vector2(AvatarSize, AvatarSize);
            Root.anchoredPosition = new Vector2(ScreenMargin, ScreenMargin);

            // Awatar — okrągły pill button z inicjałem (placeholder portretu 2D).
            var img = _root.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.PrimaryAccent, UIShapePreset.Pill);
            var btn = _root.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                UITheme.PrimaryAccent,
                UITheme.Darken(UITheme.PrimaryAccent, 0.05f),
                UITheme.Darken(UITheme.PrimaryAccent, 0.12f),
                UITheme.PrimaryAccent,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() => OnAvatarClicked?.Invoke());

            var initialGo = new GameObject("Initial", typeof(RectTransform));
            initialGo.transform.SetParent(_root.transform, false);
            var irt = (RectTransform)initialGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            _initial = initialGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_initial, UIThemeTextRole.Inverse);
            _initial.fontSize = 26;
            _initial.alignment = TextAlignmentOptions.Center;
            _initial.raycastTarget = false;
            RefreshInitial();

            _tooltip = UIBuilders.AttachTooltip(_root.gameObject, AssistantState.DisplayName);

            // Kropka-badge (prawy-górny róg awatara), domyślnie ukryta.
            _badge = new GameObject("Badge", typeof(RectTransform));
            _badge.transform.SetParent(_root.transform, false);
            var brt = (RectTransform)_badge.transform;
            brt.anchorMin = Vector2.one;
            brt.anchorMax = Vector2.one;
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            brt.anchoredPosition = new Vector2(-4f, -4f);
            var bimg = _badge.AddComponent<Image>();
            UITheme.ApplySurface(bimg, UITheme.Warning, UIShapePreset.Pill);
            bimg.raycastTarget = false;
            _badge.SetActive(false);

            // Start niewidoczny — AssistantOrchestrator włącza per scena (Depot/MapScene).
            _root.SetActive(false);
        }

        GameObject _root;
        TooltipTrigger _tooltip;
    }
}
