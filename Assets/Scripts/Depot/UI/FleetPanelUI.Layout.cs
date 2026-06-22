using RailwayManager.SharedUI.Localization;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI - wspolny scaffold panelu i layout glownego widoku.
    /// </summary>
    public partial class FleetPanelUI
    {
        private void BuildUI()
        {
            BuildRoot();
            BuildTopBar();
            BuildTabBar();
            BuildFilterBar();
            BuildScrollArea();
            BuildMarketSubTabBar();
            BuildMarketFilterBar();
            BuildConfigurator();
            BuildCartButton();
        }

        private void BuildRoot()
        {
            _root = new GameObject("FleetPanelRoot");
            _root.transform.SetParent(transform, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var background = _root.AddComponent<Image>();
            // MUI re-skin (TD-043 Wzorzec 1): full-bleed panel → ostre rogi (zwykły Image, bez rounded sprite)
            background.color = PanelBg;
        }

        private void BuildTopBar()
        {
            var bar = NewGO("TopBar", _root.transform);
            var barImage = bar.AddComponent<Image>();
            UITheme.ApplySurface(barImage, TopBarBg, UIShapePreset.Panel);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, TopBarH);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            hl.spacing = UITheme.Spacing.Xl;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            _titleLbl = MakeTMP("Title", bar.transform);
            _titleLbl.text = LocalizationService.Get("fleet.title");
            _titleLbl.fontSize = 30;
            _titleLbl.fontStyle = FontStyles.Bold;
            _titleLbl.color = TextPrimary;
            _titleLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

            _counterLbl = MakeTMP("Counter", bar.transform);
            _counterLbl.fontSize = 18;
            _counterLbl.color = TextMuted;
            _counterLbl.alignment = TextAlignmentOptions.Center;
            _counterLbl.textWrappingMode = TextWrappingModes.NoWrap;
            _counterLbl.raycastTarget = false;
            var counterLE = _counterLbl.gameObject.AddComponent<LayoutElement>();
            counterLE.flexibleWidth = 1f;
            counterLE.minWidth = 400f;
            UpdateCounter();

            var closeGO = NewGO("ClosePanelBtn", bar.transform);
            var closeImage = closeGO.AddComponent<Image>();
            UITheme.ApplySurface(closeImage, BtnSecondary, UIShapePreset.Pill);
            var closeLE = closeGO.AddComponent<LayoutElement>();
            closeLE.preferredWidth = 36f;
            closeLE.preferredHeight = 36f;
            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.targetGraphic = closeImage;
            closeBtn.colors = UITheme.CreateColorBlock(
                BtnSecondary,
                UITheme.RaisedSurface,
                UITheme.Border,
                BtnSecondary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            closeBtn.onClick.AddListener(Hide);
            var closeLbl = MakeTMP("X", closeGO.transform);
            closeLbl.text = "X";
            closeLbl.fontSize = 18;
            closeLbl.fontStyle = FontStyles.Bold;
            closeLbl.color = TextPrimary;
            closeLbl.alignment = TextAlignmentOptions.Center;
            closeLbl.raycastTarget = false;
            FillRT(closeLbl.gameObject);
        }

        private void BuildTabBar()
        {
            var bar = NewGO("TabBar", _root.transform);
            var barImage = bar.AddComponent<Image>();
            UITheme.ApplySurface(barImage, TabBarBg, UIShapePreset.Inset);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -TopBarH);
            rt.sizeDelta = new Vector2(0f, TabBarH);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            CreateInnerTab(bar.transform, FleetTab.MyFleet, LocalizationService.Get("fleet.tabs.my_fleet"));
            CreateInnerTab(bar.transform, FleetTab.Consists, LocalizationService.Get("fleet.tabs.consists"));
            CreateInnerTab(bar.transform, FleetTab.BuyFleet, LocalizationService.Get("fleet.tabs.buy_fleet"));
        }

        private void CreateInnerTab(Transform parent, FleetTab tab, string label)
        {
            var go = NewGO($"Tab_{tab}", parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 38f);
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, tab == _activeTab ? TabActive : TabNormal, tab == _activeTab ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                tab == _activeTab ? TabActive : TabNormal,
                tab == _activeTab ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                tab == _activeTab ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                tab == _activeTab ? TabActive : TabNormal,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            var captured = tab;
            btn.onClick.AddListener(() => SwitchTab(captured));
            go.AddComponent<LayoutElement>().preferredWidth = 160f;

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text = label;
            lbl.fontSize = 18;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _tabButtons.Add((tab, img, lbl));
        }

        private void BuildScrollArea()
        {
            float topOffset = TopBarH + TabBarH + FilterBarH;

            _scrollRectGO = NewGO("ScrollRect", _root.transform);
            _viewportGO = _scrollRectGO;
            var srRT = _scrollRectGO.GetComponent<RectTransform>();
            srRT.anchorMin = Vector2.zero;
            srRT.anchorMax = Vector2.one;
            srRT.offsetMin = new Vector2(0f, 0f);
            srRT.offsetMax = new Vector2(0f, -topOffset);

            var srImg = _scrollRectGO.AddComponent<Image>();
            UITheme.ApplySurface(srImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.18f), UIShapePreset.Panel);

            var scroll = _scrollRectGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewGO("Viewport", _scrollRectGO.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(0f, 0f);
            vpRT.offsetMax = new Vector2(-12f, 0f);
            var viewportImage = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportImage, UITheme.WithAlpha(UITheme.PrimarySurface, 0.28f), UIShapePreset.Inset);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = NewGO("Content", viewport.transform);
            _contentRT = content.GetComponent<RectTransform>();
            _contentRT.anchorMin = new Vector2(0f, 1f);
            _contentRT.anchorMax = new Vector2(1f, 1f);
            _contentRT.pivot = new Vector2(0.5f, 1f);
            _contentRT.anchoredPosition = Vector2.zero;
            _contentRT.sizeDelta = Vector2.zero;

            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(0f, 0f, UITheme.Spacing.Xs, UITheme.Spacing.Lg);
            vl.spacing = UITheme.Spacing.Xxs;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _contentRT;
            _contentParent = content.transform;

            var scrollbarGO = NewGO("Scrollbar", _scrollRectGO.transform);
            var sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 0.5f);
            sbRT.anchoredPosition = Vector2.zero;
            sbRT.sizeDelta = new Vector2(10f, 0f);
            var scrollbarImage = scrollbarGO.AddComponent<Image>();
            UITheme.ApplySurface(scrollbarImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.8f), UIShapePreset.Pill);

            var sbSliding = NewGO("SlidingArea", scrollbarGO.transform);
            var ssRT = sbSliding.GetComponent<RectTransform>();
            ssRT.anchorMin = Vector2.zero;
            ssRT.anchorMax = Vector2.one;
            ssRT.offsetMin = new Vector2(1f, 1f);
            ssRT.offsetMax = new Vector2(-1f, -1f);

            var sbHandle = NewGO("Handle", sbSliding.transform);
            var shRT = sbHandle.GetComponent<RectTransform>();
            shRT.anchorMin = Vector2.zero;
            shRT.anchorMax = Vector2.one;
            shRT.offsetMin = Vector2.zero;
            shRT.offsetMax = Vector2.zero;
            var handleImage = sbHandle.AddComponent<Image>();
            UITheme.ApplySurface(handleImage, UITheme.WithAlpha(UITheme.Border, 0.95f), UIShapePreset.Pill);

            var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = shRT;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;

            _emptyLbl = MakeTMP("EmptyLbl", _viewportGO.transform);
            _emptyLbl.text = "";
            _emptyLbl.fontSize = 22;
            _emptyLbl.color = TextMuted;
            _emptyLbl.alignment = TextAlignmentOptions.Center;
            _emptyLbl.raycastTarget = false;
            var emptyRT = _emptyLbl.GetComponent<RectTransform>();
            emptyRT.anchorMin = new Vector2(0.1f, 0.3f);
            emptyRT.anchorMax = new Vector2(0.9f, 0.7f);
            emptyRT.offsetMin = Vector2.zero;
            emptyRT.offsetMax = Vector2.zero;
            _emptyLbl.gameObject.SetActive(false);
        }
    }
}
