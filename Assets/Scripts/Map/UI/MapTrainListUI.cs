using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace MapSystem
{
    /// <summary>
    /// Rozwijany panel z lista pociagow na mapie (lewa strona).
    /// Klikniecie strzalki obok "Pociagi" rozwija/zwija liste.
    /// Dane zasilane z M9a <c>TrainRunSimulator</c> przez Add/Update/RemoveTrain.
    /// </summary>
    public class MapTrainListUI : MonoBehaviour
    {
        /// <summary>
        /// Scene-scoped singleton (zyje w MapScene). Eliminuje O(scena)
        /// FindAnyObjectByType per spawn/despawn/UI refresh w TrainRunSimulator.
        /// </summary>
        public static MapTrainListUI Instance { get; private set; }

        [Header("Colors")]
        [SerializeField] private Color panelBgColor = default;
        [SerializeField] private Color headerBgColor = default;
        [SerializeField] private Color itemNormalColor = default;
        [SerializeField] private Color itemHoverColor = default;

        private bool isExpanded;
        private TextMeshProUGUI arrowText;
        private TextMeshProUGUI subtitleText;
        private GameObject scrollArea;
        private Transform listContent;
        private TextMeshProUGUI placeholderText;
        private RectTransform panelRt;

        // O(1) lookup zamiast O(N) foreach Transform z child.name == $"Train_{id}" compare
        // (alokacja stringu per check). HasTrain/UpdateTrainStatus/RemoveTrain wołane per
        // train event w hot path TrainRunSimulator.
        private readonly Dictionary<int, GameObject> _itemsById = new();

        private const float PanelWidth = 248f;
        private const float HeaderHeight = 56f;
        private const float ExpandedHeight = 424f;
        private const float TopBarOffset = 52f;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            Instance = this;

            ApplyDefaultPalette();
            BuildUI();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            SetExpanded(false);
        }

        public void ToggleExpanded()
        {
            SetExpanded(!isExpanded);
        }

        public void SetExpanded(bool expanded)
        {
            isExpanded = expanded;

            if (arrowText != null)
                arrowText.text = isExpanded ? "\u25BC" : "\u25B6";

            if (scrollArea != null)
                scrollArea.SetActive(isExpanded);

            if (panelRt != null)
            {
                float height = isExpanded ? ExpandedHeight : HeaderHeight;
                panelRt.sizeDelta = new Vector2(PanelWidth, height);
            }

            RefreshHeaderState();
        }

        /// <summary>
        /// Dodaj pociag do listy (do uzycia pozniej przez system ruchu).
        /// </summary>
        public void AddTrain(int trainId, string name, string status, Color lineColor,
                             System.Action onClick = null)
        {
            if (listContent == null) return;

            if (placeholderText != null)
                placeholderText.gameObject.SetActive(false);

            GameObject item = CreateTrainItem(trainId, name, status, lineColor, onClick);
            item.transform.SetParent(listContent, false);
            _itemsById[trainId] = item;
            RefreshHeaderState();
        }

        /// <summary>Aktualizuj status istniejacego pociagu.</summary>
        public void UpdateTrainStatus(int trainId, string name, string status, Color lineColor)
        {
            if (!_itemsById.TryGetValue(trainId, out var item) || item == null) return;

            var t = item.transform;
            var nameText = t.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var statusText = t.Find("Status")?.GetComponent<TextMeshProUGUI>();
            var stripImg = t.Find("LineStrip")?.GetComponent<Image>();
            if (nameText != null) nameText.text = name;
            if (statusText != null) statusText.text = status;
            if (stripImg != null) stripImg.color = lineColor;
            RefreshHeaderState();
        }

        public bool HasTrain(int trainId)
        {
            return _itemsById.TryGetValue(trainId, out var item) && item != null;
        }

        /// <summary>
        /// Usun pociag z listy.
        /// </summary>
        public void RemoveTrain(int trainId)
        {
            if (_itemsById.TryGetValue(trainId, out var item))
            {
                if (item != null) Destroy(item);
                _itemsById.Remove(trainId);
            }

            if (_itemsById.Count == 0 && placeholderText != null)
                placeholderText.gameObject.SetActive(true);

            RefreshHeaderState();
        }

        /// <summary>
        /// Wyczysc cala liste.
        /// </summary>
        public void ClearTrains()
        {
            foreach (var kvp in _itemsById)
                if (kvp.Value != null) Destroy(kvp.Value);
            _itemsById.Clear();

            if (placeholderText != null)
                placeholderText.gameObject.SetActive(true);

            RefreshHeaderState();
        }

        private void ApplyDefaultPalette()
        {
            if (panelBgColor == default)
                panelBgColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.94f);
            if (headerBgColor == default)
                headerBgColor = UITheme.WithAlpha(UITheme.TopBarInset, 0.96f);
            if (itemNormalColor == default)
                itemNormalColor = UITheme.WithAlpha(UITheme.SecondarySurface, 0.90f);
            if (itemHoverColor == default)
                itemHoverColor = UITheme.WithAlpha(UITheme.RaisedSurface, 0.95f);
        }

        private void RefreshHeaderState()
        {
            if (subtitleText == null) return;

            int count = _itemsById.Count;
            subtitleText.text = isExpanded
                ? $"Aktywne na mapie • {count} składów"
                : $"Pokaż ruch • {count} składów";
        }

        private void BuildUI()
        {
            panelRt = GetComponent<RectTransform>();
            if (panelRt == null) panelRt = gameObject.AddComponent<RectTransform>();

            panelRt.anchorMin = new Vector2(0, 1);
            panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot = new Vector2(0, 1);
            panelRt.anchoredPosition = new Vector2(10, -TopBarOffset);
            panelRt.sizeDelta = new Vector2(PanelWidth, HeaderHeight);

            Image bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, panelBgColor, UIShapePreset.PanelLarge);

            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(transform, false);

            RectTransform headerRt = headerObj.AddComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0, 1);
            headerRt.anchorMax = new Vector2(1, 1);
            headerRt.pivot = new Vector2(0.5f, 1);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(0, HeaderHeight);

            Image headerBg = headerObj.AddComponent<Image>();
            UITheme.ApplySurface(headerBg, headerBgColor, UIShapePreset.Panel);

            Button headerBtn = headerObj.AddComponent<Button>();
            headerBtn.colors = UITheme.CreateColorBlock(
                headerBgColor,
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.98f),
                UITheme.WithAlpha(UITheme.PrimarySurface, 0.98f),
                headerBgColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            headerBtn.onClick.AddListener(ToggleExpanded);

            GameObject arrowBadge = new GameObject("Arrow");
            arrowBadge.transform.SetParent(headerObj.transform, false);
            RectTransform arrowRt = arrowBadge.AddComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(0, 0.5f);
            arrowRt.anchorMax = new Vector2(0, 0.5f);
            arrowRt.pivot = new Vector2(0, 0.5f);
            arrowRt.anchoredPosition = new Vector2(10, 0);
            arrowRt.sizeDelta = new Vector2(34, 24);
            Image arrowBg = arrowBadge.AddComponent<Image>();
            UITheme.ApplySurface(arrowBg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.96f), UIShapePreset.Pill);

            GameObject arrowTextObj = new GameObject("ArrowText");
            arrowTextObj.transform.SetParent(arrowBadge.transform, false);
            RectTransform arrowTextRt = arrowTextObj.AddComponent<RectTransform>();
            arrowTextRt.anchorMin = Vector2.zero;
            arrowTextRt.anchorMax = Vector2.one;
            arrowTextRt.offsetMin = Vector2.zero;
            arrowTextRt.offsetMax = Vector2.zero;

            arrowText = arrowTextObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(arrowText, UIThemeTextRole.Warning);
            arrowText.fontSize = 14;
            arrowText.fontStyle = FontStyles.Bold;
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.text = "\u25B6";

            GameObject textColumn = new GameObject("TextColumn");
            textColumn.transform.SetParent(headerObj.transform, false);
            RectTransform textColRt = textColumn.AddComponent<RectTransform>();
            textColRt.anchorMin = new Vector2(0, 0);
            textColRt.anchorMax = new Vector2(1, 1);
            textColRt.offsetMin = new Vector2(52, 8);
            textColRt.offsetMax = new Vector2(-8, -8);

            VerticalLayoutGroup textLayout = textColumn.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = UITheme.Spacing.Xxs;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = false;
            textLayout.childAlignment = TextAnchor.MiddleLeft;

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(textColumn.transform, false);
            labelObj.AddComponent<RectTransform>();
            labelObj.AddComponent<LayoutElement>().preferredHeight = 20f;

            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(labelText, UIThemeTextRole.Primary);
            labelText.fontSize = 14;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.text = "Pociagi";

            GameObject subtitleObj = new GameObject("Subtitle");
            subtitleObj.transform.SetParent(textColumn.transform, false);
            subtitleObj.AddComponent<RectTransform>();
            subtitleObj.AddComponent<LayoutElement>().preferredHeight = 16f;

            subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(subtitleText, UIThemeTextRole.Secondary);
            subtitleText.fontSize = 10;
            subtitleText.alignment = TextAlignmentOptions.MidlineLeft;
            subtitleText.text = "Pokaż ruch • 0 składów";

            scrollArea = new GameObject("ScrollArea");
            scrollArea.transform.SetParent(transform, false);

            RectTransform scrollRt = scrollArea.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(0, 0);
            scrollRt.offsetMax = new Vector2(0, -HeaderHeight);

            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollArea.transform, false);
            RectTransform viewportRt = viewportObj.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(6, 0);
            viewportRt.offsetMax = new Vector2(-6, -6);
            var viewportBg = viewportObj.AddComponent<Image>();
            UITheme.ApplySurface(viewportBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.94f), UIShapePreset.Inset);
            viewportObj.AddComponent<Mask>().showMaskGraphic = true;

            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);
            listContent = contentObj.transform;

            RectTransform contentRt = contentObj.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup contentVlg = contentObj.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = UITheme.Spacing.Sm;
            contentVlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = false;

            ContentSizeFitter csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollArea.AddComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = viewportRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 20f;

            GameObject phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(listContent, false);
            RectTransform phRt = phObj.AddComponent<RectTransform>();
            phRt.sizeDelta = new Vector2(220, 52);
            LayoutElement phLe = phObj.AddComponent<LayoutElement>();
            phLe.preferredHeight = 52;
            Image phBg = phObj.AddComponent<Image>();
            UITheme.ApplySurface(phBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.75f), UIShapePreset.Inset);

            GameObject phTextObj = new GameObject("PlaceholderText");
            phTextObj.transform.SetParent(phObj.transform, false);
            RectTransform phTextRt = phTextObj.AddComponent<RectTransform>();
            phTextRt.anchorMin = Vector2.zero;
            phTextRt.anchorMax = Vector2.one;
            phTextRt.offsetMin = Vector2.zero;
            phTextRt.offsetMax = Vector2.zero;

            placeholderText = phTextObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(placeholderText, UIThemeTextRole.Secondary);
            placeholderText.fontSize = 12;
            placeholderText.alignment = TextAlignmentOptions.Center;
            placeholderText.text = "Brak pociagow";

            RefreshHeaderState();
        }

        private GameObject CreateTrainItem(int trainId, string name, string status, Color lineColor,
                                           System.Action onClick)
        {
            GameObject item = new GameObject($"Train_{trainId}");

            RectTransform rt = item.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220, 54);

            LayoutElement le = item.AddComponent<LayoutElement>();
            le.preferredHeight = 54;

            Image bg = item.AddComponent<Image>();
            UITheme.ApplySurface(bg, itemNormalColor, UIShapePreset.Inset);

            if (onClick != null)
            {
                var btn = item.AddComponent<Button>();
                btn.colors = UITheme.CreateColorBlock(
                    itemNormalColor,
                    itemHoverColor,
                    UITheme.Darken(itemHoverColor, 0.08f),
                    itemNormalColor,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => onClick());
            }

            GameObject stripObj = new GameObject("LineStrip");
            stripObj.transform.SetParent(item.transform, false);
            RectTransform stripRt = stripObj.AddComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0, 0.5f);
            stripRt.anchorMax = new Vector2(0, 0.5f);
            stripRt.pivot = new Vector2(0, 0.5f);
            stripRt.anchoredPosition = new Vector2(6, 0);
            stripRt.sizeDelta = new Vector2(8, 38);
            Image stripImg = stripObj.AddComponent<Image>();
            UITheme.ApplySurface(stripImg, lineColor, UIShapePreset.Pill);

            GameObject nameObj = new GameObject("Name");
            nameObj.transform.SetParent(item.transform, false);
            RectTransform nameRt = nameObj.AddComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 0.5f);
            nameRt.anchorMax = new Vector2(1, 1);
            nameRt.offsetMin = new Vector2(22, 1);
            nameRt.offsetMax = new Vector2(-8, -4);
            TextMeshProUGUI nameText = nameObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(nameText, UIThemeTextRole.Primary);
            nameText.fontSize = 12;
            nameText.fontStyle = FontStyles.Bold;
            nameText.text = name;

            GameObject statusObj = new GameObject("Status");
            statusObj.transform.SetParent(item.transform, false);
            RectTransform statusRt = statusObj.AddComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0, 0);
            statusRt.anchorMax = new Vector2(1, 0.5f);
            statusRt.offsetMin = new Vector2(22, 4);
            statusRt.offsetMax = new Vector2(-8, 1);
            TextMeshProUGUI statusText = statusObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(statusText, UIThemeTextRole.Secondary);
            statusText.fontSize = 10;
            statusText.text = status;

            return item;
        }
    }
}
