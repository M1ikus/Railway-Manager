using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DepotSystem;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem.Furniture.UI
{
    /// <summary>
    /// MF-7 - kontekstowe menu dla wybranego PlacedFurnitureItem.
    /// </summary>
    public class FurnitureContextMenuUI : MonoBehaviour
    {
        public static FurnitureContextMenuUI Instance { get; private set; }

        private Canvas _canvas;
        private GameObject _root;
        private TMP_Text _titleLabel;
        private TMP_Text _subtitleLabel;
        private Button _moveBtn;
        private Button _rotateBtn;
        private Button _deleteBtn;

        private int _currentInstanceId = -1;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildUI();
            Hide();
        }

        void Start()
        {
            TrySubscribe();
        }

        void Update()
        {
            if (FurnitureSelector.Instance != null && _currentInstanceId == -2)
            {
                TrySubscribe();
            }
        }

        void OnDestroy()
        {
            if (FurnitureSelector.Instance != null)
            {
                FurnitureSelector.Instance.OnSelectionChanged -= OnSelectionChanged;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void TrySubscribe()
        {
            var selector = FurnitureSelector.Instance;
            if (selector == null)
            {
                _currentInstanceId = -2;
                return;
            }

            selector.OnSelectionChanged -= OnSelectionChanged;
            selector.OnSelectionChanged += OnSelectionChanged;
            _currentInstanceId = selector.SelectedInstanceId;
            if (_currentInstanceId >= 0)
            {
                Show(_currentInstanceId);
            }
        }

        public void Show(int instanceId)
        {
            _currentInstanceId = instanceId;
            if (_root != null)
            {
                _root.SetActive(true);
            }

            UpdateLabel();
        }

        public void Hide()
        {
            _currentInstanceId = -1;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        public bool IsVisible => _root != null && _root.activeSelf;

        private void OnSelectionChanged(int instanceId)
        {
            if (instanceId < 0)
            {
                Hide();
            }
            else
            {
                Show(instanceId);
            }
        }

        private void OnMoveClicked()
        {
            if (_currentInstanceId < 0)
            {
                return;
            }

            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                return;
            }

            int idCopy = _currentInstanceId;
            FurnitureSelector.Instance?.Deselect();
            placer.MoveInstance(idCopy);
        }

        private void OnRotateClicked()
        {
            if (_currentInstanceId < 0)
            {
                return;
            }

            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                return;
            }

            placer.RotateInstance(_currentInstanceId);
            FurnitureSelector.Instance?.Select(_currentInstanceId);
        }

        private void OnDeleteClicked()
        {
            if (_currentInstanceId < 0)
            {
                return;
            }

            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                return;
            }

            int idCopy = _currentInstanceId;
            FurnitureSelector.Instance?.Deselect();
            placer.DeleteInstance(idCopy);
        }

        private void BuildUI()
        {
            var canvasGO = new GameObject("FurnitureContextMenuCanvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGO.AddComponent<GraphicRaycaster>();

            _root = new GameObject("FurnitureContextMenuUI_Root");
            _root.transform.SetParent(_canvas.transform, false);

            var rootRT = _root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(1f, 1f);
            rootRT.anchorMax = new Vector2(1f, 1f);
            rootRT.pivot = new Vector2(1f, 1f);
            rootRT.sizeDelta = new Vector2(468f, 122f);
            rootRT.anchoredPosition = new Vector2(-18f, -90f);

            var rootBg = _root.AddComponent<Image>();
            UITheme.ApplySurface(rootBg, UITheme.OverlayPanelStrong, UIShapePreset.PanelLarge);

            var rootLayout = _root.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = UITheme.Spacing.Md;
            rootLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childAlignment = TextAnchor.UpperCenter;

            BuildHeaderCard();
            BuildActionsCard();
        }

        private void BuildHeaderCard()
        {
            var headerGO = new GameObject("HeaderCard");
            headerGO.transform.SetParent(_root.transform, false);

            var headerImage = headerGO.AddComponent<Image>();
            UITheme.ApplySurface(headerImage, UITheme.TopBarInset, UIShapePreset.Panel);

            var headerLayout = headerGO.AddComponent<VerticalLayoutGroup>();
            headerLayout.spacing = UITheme.Spacing.Xs;
            headerLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childAlignment = TextAnchor.UpperLeft;

            var headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 52;

            _titleLabel = CreateText(headerGO.transform, "Wybrane wyposazenie", 16f, UIThemeTextRole.Primary, FontStyles.Bold);
            _subtitleLabel = CreateText(headerGO.transform, "Zaznacz obiekt, aby pokazac akcje.", 11f, UIThemeTextRole.Secondary);
        }

        private void BuildActionsCard()
        {
            var actionsGO = new GameObject("ActionsCard");
            actionsGO.transform.SetParent(_root.transform, false);

            var actionsImage = actionsGO.AddComponent<Image>();
            UITheme.ApplySurface(actionsImage, UITheme.WithAlpha(UITheme.SecondarySurface, 0.88f), UIShapePreset.Panel);

            var actionsLayout = actionsGO.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = UITheme.Spacing.Sm;
            actionsLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childForceExpandHeight = true;
            actionsLayout.childAlignment = TextAnchor.MiddleCenter;

            var actionsLE = actionsGO.AddComponent<LayoutElement>();
            actionsLE.preferredHeight = 46;

            _moveBtn = CreateButton(actionsGO.transform, "Przenies", OnMoveClicked, 132f, primary: true);
            _rotateBtn = CreateButton(actionsGO.transform, "Obroc", OnRotateClicked, 122f);
            _deleteBtn = CreateButton(actionsGO.transform, "Usun", OnDeleteClicked, 122f, danger: true);
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float preferredWidth, bool primary = false, bool danger = false)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            Color normal = danger ? UITheme.Danger : (primary ? UITheme.PrimaryAccent : UITheme.SecondarySurface);
            Color highlighted = danger
                ? UITheme.Darken(UITheme.Danger, 0.08f)
                : (primary ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface);
            Color pressed = danger
                ? UITheme.Darken(UITheme.Danger, 0.18f)
                : (primary ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border);
            UITheme.ApplySurface(image, normal, primary ? UIShapePreset.Pill : UIShapePreset.Button);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = UITheme.CreateColorBlock(normal, highlighted, pressed, highlighted, UITheme.WithAlpha(UITheme.Border, 0.55f));
            button.onClick.AddListener(onClick);

            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = 40f;
            layout.flexibleWidth = 1f;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            StretchToParent(labelGO);

            var text = labelGO.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 12.5f;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);

            return button;
        }

        private void UpdateLabel()
        {
            if (_titleLabel == null || _subtitleLabel == null)
            {
                return;
            }

            if (_currentInstanceId < 0)
            {
                _titleLabel.text = "Wybrane wyposazenie";
                _subtitleLabel.text = "Zaznacz obiekt, aby pokazac akcje.";
                return;
            }

            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                _titleLabel.text = $"Element #{_currentInstanceId}";
                _subtitleLabel.text = "Akcje obiektu sa gotowe do uzycia.";
                return;
            }

            var instance = placer.GetInstance(_currentInstanceId);
            if (instance == null)
            {
                _titleLabel.text = $"Element #{_currentInstanceId}";
                _subtitleLabel.text = "Wybrany obiekt nie jest juz dostepny.";
                return;
            }

            var item = FurnitureCatalog.FindById(instance.itemId);
            string name = item?.displayName ?? instance.itemId;
            _titleLabel.text = name;
            _subtitleLabel.text = $"ID #{_currentInstanceId}  |  Przenies, obroc lub usun wybrany element.";
        }

        private TMP_Text CreateText(Transform parent, string value, float fontSize, UIThemeTextRole role, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontStyle = style;
            UITheme.ApplyTmpText(text, role);
            return text;
        }

        private static void StretchToParent(GameObject target)
        {
            var rect = target.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
