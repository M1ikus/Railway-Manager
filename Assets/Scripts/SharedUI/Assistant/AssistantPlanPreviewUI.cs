using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-1c: generyczny preview planu — warstwa [3] UX (Plan→akceptuj→Apply, AS-D3).
    /// Jedyny „głośny" element asystenta — zawsze ŚWIADOMIE wywołany przez gracza
    /// (klik w propozycję), więc panel z backdropem jest OK. Bez PauseStack (świat żyje).
    /// Renderuje AssistantPlan niezależnie od domeny: tytuł, kroki preview, koszt, efekt.
    /// Backdrop-klik = Anuluj („akceptacja = undo" — odrzucenie nic nie zmienia).
    /// </summary>
    public class AssistantPlanPreviewUI : MonoBehaviour
    {
        public static AssistantPlanPreviewUI Instance { get; private set; }

        /// <summary>Pasmo fullscreen-paneli (Finance/Workshops = 230) — preview to świadoma akcja.</summary>
        public const int SortingOrder = 230;

        const float PanelWidth = 480f;
        const int MaxPreviewLines = 14;

        GameObject _root;
        IAssistantCapability _capability;
        AssistantPlan _plan;

        public static void Show(IAssistantCapability capability, AssistantPlan plan)
        {
            if (capability == null || plan == null)
            {
                Log.Warn("[AssistantPlanPreview] Show z null capability/plan — pomijam");
                return;
            }
            EnsureExists().ShowInternal(capability, plan);
        }

        static AssistantPlanPreviewUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AssistantPlanPreviewUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AssistantPlanPreviewUI>();
            return Instance;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void ShowInternal(IAssistantCapability capability, AssistantPlan plan)
        {
            HideAndClear();
            _capability = capability;
            _plan = plan;
            BuildUI();
        }

        void HideAndClear()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _capability = null;
            _plan = null;
        }

        void OnAccept()
        {
            var cap = _capability;
            var plan = _plan;
            HideAndClear();
            if (cap == null || plan == null) return;

            bool ok = cap.Apply(plan);
            if (ok)
            {
                AssistantState.AddHistory(plan.title);
                Log.Info($"[AssistantPlanPreview] Zastosowano plan '{plan.title}' ({cap.Id})");
            }
            else
            {
                // Plan przeterminowany/nieważny — capability nic nie zmieniła (kontrakt AS-D3).
                Log.Warn($"[AssistantPlanPreview] Apply odrzucone dla '{cap.Id}' — plan nieaktualny");
            }
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("PlanPreviewCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();
            _root = canvasGo;

            // Backdrop — klik poza panelem = Anuluj.
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(canvas.transform, false);
            var brt = (RectTransform)backdrop.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            backdrop.GetComponent<Button>().onClick.AddListener(HideAndClear);

            // Panel centralny.
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            int lineCount = Mathf.Min(_plan.previewLines.Count, MaxPreviewLines);
            float height = 150f + lineCount * 20f
                + (_plan.costGroszy > 0 ? 24f : 0f)
                + (!string.IsNullOrEmpty(_plan.effectSummary) ? 42f : 0f);
            prt.sizeDelta = new Vector2(PanelWidth, height);
            var pimg = panelGo.AddComponent<Image>();
            UITheme.ApplySurface(pimg, UITheme.PrimarySurface, UIShapePreset.Panel);

            var content = UIBuilders.MakeContainer(panelGo.transform, UIBuilders.ContainerLayout.Vertical,
                padding: UITheme.Spacing.Md, spacing: UITheme.Spacing.Xs);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            // Tytuł.
            string title = string.IsNullOrEmpty(_plan.title)
                ? LocalizationService.Get("assistant.preview.title_fallback")
                : _plan.title;
            var titleTmp = UIBuilders.MakeLabel(content, title, UIBuilders.TypographyRole.H2);
            SetRowHeight(titleTmp.gameObject, 30f);

            // Efekt (opcjonalny).
            if (!string.IsNullOrEmpty(_plan.effectSummary))
            {
                var effTmp = UIBuilders.MakeLabel(content, _plan.effectSummary, UIBuilders.TypographyRole.Body);
                effTmp.textWrappingMode = TextWrappingModes.Normal;
                SetRowHeight(effTmp.gameObject, 38f);
            }

            UIBuilders.MakeSeparator(content);

            // Kroki preview (cap + licznik pominiętych).
            for (int i = 0; i < lineCount; i++)
            {
                var lineTmp = UIBuilders.MakeLabel(content, "• " + _plan.previewLines[i], UIBuilders.TypographyRole.Small);
                lineTmp.overflowMode = TextOverflowModes.Ellipsis;
                SetRowHeight(lineTmp.gameObject, 18f);
            }
            int omitted = _plan.previewLines.Count - lineCount;
            if (omitted > 0)
            {
                var moreTmp = UIBuilders.MakeLabel(content,
                    string.Format(LocalizationService.Get("assistant.preview.more_lines_format"), omitted),
                    UIBuilders.TypographyRole.Small);
                SetRowHeight(moreTmp.gameObject, 18f);
            }

            // Koszt (AS-D6: plan z kosztem ZAWSZE wymaga akceptacji — nigdy auto).
            if (_plan.costGroszy > 0)
            {
                var costTmp = UIBuilders.MakeLabel(content,
                    string.Format(LocalizationService.Get("assistant.preview.cost_format"),
                        NumberFormatService.FormatCurrency(_plan.costGroszy / 100m)),
                    UIBuilders.TypographyRole.Body, UITheme.Warning);
                SetRowHeight(costTmp.gameObject, 22f);
            }

            // Przyciski.
            var buttons = UIBuilders.MakeContainer(content, UIBuilders.ContainerLayout.Horizontal,
                padding: 0f, spacing: UITheme.Spacing.Sm);
            SetRowHeight(buttons.gameObject, 38f);
            var cancel = UIBuilders.MakeButton(buttons,
                LocalizationService.Get("assistant.preview.cancel"), UIButtonTone.Secondary);
            SetPreferred(cancel.gameObject, 140f, 34f);
            cancel.onClick.AddListener(HideAndClear);
            var accept = UIBuilders.MakeButton(buttons,
                LocalizationService.Get("assistant.preview.accept"), UIButtonTone.Primary);
            SetPreferred(accept.gameObject, 160f, 34f);
            accept.onClick.AddListener(OnAccept);
        }

        static void SetRowHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

        static void SetPreferred(GameObject go, float width, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
        }
    }
}
