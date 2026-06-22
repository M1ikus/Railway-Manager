using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P1: host warstwy pływających okien detalu. Własny Canvas (ScreenSpaceOverlay,
    /// sortingOrder ponad panelami Depot), wiele instancji <see cref="FloatingWindow"/>, z-order
    /// przez sibling index, dedup po kluczu. Lazy singleton — działa z każdej płaszczyzny
    /// (Tabor / Obiegi / Mapa / Depot / popup pracownika), bo wszystkie referują asmdef Depot.
    ///
    /// <para>Wzór singletona własno-canvasowego: <c>TrainPopupUI</c> / <c>ConsistPopupUI</c>.
    /// Brak <c>DontDestroyOnLoad</c> — scope sceny (cross-scene handling = P3/P4).</para>
    /// </summary>
    public class WindowManager : MonoBehaviour
    {
        /// <summary>Ponad DepotCanvas (10). Popupy Timetable (=200) zostaną zastąpione w P3 → wtedy rewizja.</summary>
        public const int WindowLayerSortingOrder = 150;
        const float CascadeStepPx = 28f;
        const int CascadeWrap = 8;

        static WindowManager _instance;
        public static WindowManager Instance => _instance != null ? _instance : Create();
        public static bool Exists => _instance != null;

        Canvas _canvas;
        RectTransform _layerRoot;
        readonly List<FloatingWindow> _windows = new List<FloatingWindow>();
        readonly Dictionary<string, FloatingWindow> _keyed = new Dictionary<string, FloatingWindow>();
        int _cascadeIndex;

        public RectTransform LayerRoot => _layerRoot;
        public IReadOnlyList<FloatingWindow> OpenWindows => _windows;

        static WindowManager Create()
        {
            _instance = FindAnyObjectByType<WindowManager>();
            if (_instance != null) return _instance;
            // RectTransform przed Canvas → gwarancja że root warstwy ma RectTransform.
            var go = new GameObject("WindowLayer", typeof(RectTransform));
            _instance = go.AddComponent<WindowManager>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            // Persistent: sceny są additive (Depot+Map żyją razem) — warstwa okien musi przetrwać
            // przełączenia scen i renderować się nad obiema (P3 cross-scene).
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            BuildLayer();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void BuildLayer()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = WindowLayerSortingOrder;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler); // obowiązkowe — inaczej UI 2.4× za duże na 1080p

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            _layerRoot = transform as RectTransform; // root overlay canvasu ma RectTransform
            if (_layerRoot == null)
                Log.Error("[WindowManager] Canvas root nie ma RectTransform — okna nie będą pozycjonowane");

            Log.Info("[WindowManager] Warstwa okien zbudowana (sortingOrder " + WindowLayerSortingOrder + ")");
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>(); // New Input System Only
        }

        /// <summary>
        /// Otwiera (lub fokusuje istniejące o tym samym kluczu) pływające okno. Treść wypełnia
        /// caller przez <see cref="FloatingWindow.ContentRoot"/> (P2+). <paramref name="key"/> == null
        /// → zawsze nowa instancja; klucz (np. "vehicle:42") → re-open fokusuje istniejące zamiast duplikatu.
        /// </summary>
        public FloatingWindow OpenWindow(string key, string title, Vector2 size)
        {
            if (!string.IsNullOrEmpty(key) && _keyed.TryGetValue(key, out var existing) && existing != null)
            {
                existing.SetTitle(title);
                existing.Focus();
                return existing;
            }

            var win = FloatingWindow.Create(this, title, size);
            win.Key = key;
            _windows.Add(win);
            if (!string.IsNullOrEmpty(key)) _keyed[key] = win;

            var rt = win.RootRect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = WindowLayoutMath.CascadeOffset(_cascadeIndex++, CascadeStepPx, CascadeWrap);

            win.Focus();
            return win;
        }

        /// <summary>Podnosi okno na wierzch (sibling index = render + raycast order).</summary>
        public void Focus(FloatingWindow win)
        {
            if (win != null) win.transform.SetAsLastSibling();
        }

        /// <summary>Wywoływane przez <see cref="FloatingWindow.Close"/> — wyrejestrowanie z list.</summary>
        public void NotifyClosed(FloatingWindow win)
        {
            if (win == null) return;
            _windows.Remove(win);
            if (!string.IsNullOrEmpty(win.Key) && _keyed.TryGetValue(win.Key, out var w) && w == win)
                _keyed.Remove(win.Key);
        }

        public void CloseAll()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (_windows[i] != null) _windows[i].Close();
            _windows.Clear();
            _keyed.Clear();
        }
    }
}
