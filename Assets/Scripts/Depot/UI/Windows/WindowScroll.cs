using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows: wspólny builder pionowego ScrollRecta dla treści pływających okien
    /// (ScrollRect + Viewport[RectMask2D] + Content[VLG+ContentSizeFitter] + Scrollbar).
    /// Wzór 1:1 z głównej listy <c>FleetPanelUI.Layout</c>. Zwraca Transform kontenera treści
    /// (top-anchored, pivot góra → ContentSizeFitter rośnie w dół, scroll działa).
    /// </summary>
    public static class WindowScroll
    {
        /// <param name="parent">rodzic (ContentRoot okna); scroll wypełnia go między top/bottom inset</param>
        /// <param name="topInset">ile px zostawić u góry (np. nagłówek); 0 = od samej góry</param>
        /// <param name="bottomInset">ile px zostawić na dole (np. footer); 0 = do samego dołu</param>
        public static Transform BuildVertical(Transform parent, float topInset, float bottomInset)
        {
            var scrollGO = new GameObject("Scroll", typeof(RectTransform));
            scrollGO.transform.SetParent(parent, false);
            var srRT = (RectTransform)scrollGO.transform;
            srRT.anchorMin = Vector2.zero; srRT.anchorMax = Vector2.one;
            srRT.offsetMin = new Vector2(0f, bottomInset); srRT.offsetMax = new Vector2(0f, -topInset);

            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGO.transform, false);
            var vpRT = (RectTransform)viewport.transform;
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = new Vector2(-12f, 0f);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = new GameObject("List", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRT = (RectTransform)content.transform;
            cRT.anchorMin = new Vector2(0f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(0.5f, 1f);
            cRT.anchoredPosition = Vector2.zero; cRT.sizeDelta = Vector2.zero;

            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Sm, UITheme.Spacing.Sm, UITheme.Spacing.Lg);
            vl.spacing = UITheme.Spacing.Xxs;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cRT;

            var scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform));
            scrollbarGO.transform.SetParent(scrollGO.transform, false);
            var sbRT = (RectTransform)scrollbarGO.transform;
            sbRT.anchorMin = new Vector2(1f, 0f); sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 0.5f); sbRT.anchoredPosition = Vector2.zero;
            sbRT.sizeDelta = new Vector2(10f, 0f);
            var sbImg = scrollbarGO.AddComponent<Image>();
            UITheme.ApplySurface(sbImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.8f), UIShapePreset.Pill);

            var sliding = new GameObject("SlidingArea", typeof(RectTransform));
            sliding.transform.SetParent(scrollbarGO.transform, false);
            var slRT = (RectTransform)sliding.transform;
            slRT.anchorMin = Vector2.zero; slRT.anchorMax = Vector2.one;
            slRT.offsetMin = new Vector2(1f, 1f); slRT.offsetMax = new Vector2(-1f, -1f);

            var handle = new GameObject("Handle", typeof(RectTransform));
            handle.transform.SetParent(sliding.transform, false);
            var hRT = (RectTransform)handle.transform;
            hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one;
            hRT.offsetMin = Vector2.zero; hRT.offsetMax = Vector2.zero;
            var handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.WithAlpha(UITheme.Border, 0.95f), UIShapePreset.Pill);

            var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = hRT;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;

            return content.transform;
        }
    }
}
