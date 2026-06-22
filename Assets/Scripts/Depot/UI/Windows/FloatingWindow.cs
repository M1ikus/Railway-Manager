using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P1: pojedyncze pływające okno detalu — ramka draggable + pasek tytułu + ✕ +
    /// obszar treści (<see cref="ContentRoot"/>, wypełnia caller w P2+). Niezależny lifecycle:
    /// zamknięcie panelu-wywołującego (np. Tabor) NIE ubija okna — żyje w warstwie
    /// <see cref="WindowManager"/>, nie jako dziecko panelu.
    ///
    /// <para>Pływające = zaokrąglone rogi (konwencja rogów / TD-043 / ui-corner-system).</para>
    /// </summary>
    public class FloatingWindow : MonoBehaviour, IPointerDownHandler
    {
        public static readonly Vector2 DefaultSize = new Vector2(440f, 540f);
        public static readonly Vector2 MinSize = new Vector2(300f, 220f);
        public static readonly Vector2 MaxSize = new Vector2(1100f, 900f);
        const float TitleBarH = 40f;
        const float ResizeEdge = 6f;
        const float ResizeCorner = 14f;

        public string Key;
        public event Action OnClosed;

        WindowManager _manager;
        RectTransform _rootRect;
        RectTransform _contentRoot;
        TextMeshProUGUI _titleLabel;

        public RectTransform RootRect => _rootRect;
        /// <summary>Pusty RectTransform pod treść (P2 wstawia VehicleView / ConsistView).</summary>
        public RectTransform ContentRoot => _contentRoot;
        public WindowManager Manager => _manager;
        public static float TitleBarHeight => TitleBarH;

        public static FloatingWindow Create(WindowManager manager, string title, Vector2 size)
        {
            var go = new GameObject("FloatingWindow", typeof(RectTransform));
            go.transform.SetParent(manager.LayerRoot, false);
            var win = go.AddComponent<FloatingWindow>();
            win.Build(manager, title, size);
            return win;
        }

        void Build(WindowManager manager, string title, Vector2 size)
        {
            _manager = manager;

            _rootRect = (RectTransform)transform;
            _rootRect.sizeDelta = new Vector2(
                Mathf.Clamp(size.x, MinSize.x, MaxSize.x),
                Mathf.Clamp(size.y, MinSize.y, MaxSize.y));

            var bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.PrimarySurface, UIShapePreset.PanelLarge);
            bg.raycastTarget = true; // łapie klik (focus) + blokuje przebicie pod okno

            BuildTitleBar(title);
            BuildContentArea();
            BuildResizeHandles();
        }

        void BuildTitleBar(string title)
        {
            var barGo = new GameObject("TitleBar", typeof(RectTransform));
            barGo.transform.SetParent(transform, false);
            var barRt = (RectTransform)barGo.transform;
            barRt.anchorMin = new Vector2(0f, 1f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta = new Vector2(0f, TitleBarH);

            var barImg = barGo.AddComponent<Image>();
            UITheme.ApplySurface(barImg, UITheme.RaisedSurface, UIShapePreset.PanelLarge);
            barImg.raycastTarget = true;

            var drag = barGo.AddComponent<WindowDragHandle>();
            drag.Init(this);

            _titleLabel = UIPrimitives.MakeTMP("Title", barGo.transform, UITheme.Typography.Body,
                UIThemeTextRole.Primary, TextAlignmentOptions.Left, FontStyles.Bold);
            var tRt = _titleLabel.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.offsetMin = new Vector2(UITheme.Spacing.Md, 0f);
            tRt.offsetMax = new Vector2(-(TitleBarH + UITheme.Spacing.Xs), 0f);
            _titleLabel.text = title;
            _titleLabel.raycastTarget = false; // klik na tytuł = drag (przejmuje barImg)

            var closeBtn = UIBuilders.MakeButton(barGo.transform, "✕", UIButtonTone.Ghost);
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(1f, 0.5f);
            cRt.anchorMax = new Vector2(1f, 0.5f);
            cRt.pivot = new Vector2(1f, 0.5f);
            cRt.anchoredPosition = new Vector2(-UITheme.Spacing.Xs, 0f);
            float btn = TitleBarH - UITheme.Spacing.Xs * 2f;
            cRt.sizeDelta = new Vector2(btn, btn);
            closeBtn.onClick.AddListener(Close);
        }

        void BuildContentArea()
        {
            var go = new GameObject("Content", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _contentRoot = (RectTransform)go.transform;
            _contentRoot.anchorMin = new Vector2(0f, 0f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.offsetMin = new Vector2(UITheme.Spacing.Sm, UITheme.Spacing.Sm);
            _contentRoot.offsetMax = new Vector2(-UITheme.Spacing.Sm, -(TitleBarH + UITheme.Spacing.Xs));
            // brak Image → klik w pustą treść spada na tło okna (focus). Treść = P2.
        }

        // ── P5: uchwyty skalowania (4 krawędzie + 2 dolne rogi; górne pomijamy by nie kolidować z ✕) ──
        void BuildResizeHandles()
        {
            AddHandle("ResizeRight",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(ResizeEdge, 0f),  1,  0);
            AddHandle("ResizeLeft",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(ResizeEdge, 0f), -1,  0);
            AddHandle("ResizeTop",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, ResizeEdge),  0,  1);
            AddHandle("ResizeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, ResizeEdge),  0, -1);
            AddHandle("ResizeBR", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(ResizeCorner, ResizeCorner),  1, -1);
            AddHandle("ResizeBL", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ResizeCorner, ResizeCorner), -1, -1);
        }

        void AddHandle(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 sizeDelta, int hx, int hy)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // przezroczysty; raycastTarget łapie drag
            img.raycastTarget = true;
            go.AddComponent<WindowResizeHandle>().Init(this, hx, hy);
        }

        public void OnPointerDown(PointerEventData eventData) => Focus();

        public void Focus()
        {
            if (_manager != null) _manager.Focus(this);
        }

        public void SetTitle(string title)
        {
            if (_titleLabel != null) _titleLabel.text = title;
        }

        /// <summary>Zamyka okno (niezależnie od panelu-wywołującego).</summary>
        public void Close()
        {
            OnClosed?.Invoke();
            if (_manager != null) _manager.NotifyClosed(this);
            Destroy(gameObject);
        }
    }
}
