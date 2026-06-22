using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Assistant;
using RailwayManager.SharedUI.Localization;
using RailwayManager.Timetable.Assistant;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// M11 AS-5c: planner połączeń asystenta — gracz wybiera relację A→B (autocomplete
    /// po stacjach), asystent liczy: fakty trasy (gatherer) → ranking taboru z powodami
    /// odrzuceń (W1-W3) → warianty częstotliwości → bilans dzienny (estymator) →
    /// [Utwórz] tworzy rozkład tą samą ścieżką co kreator.
    ///
    /// Otwierany przez UIIntent.OpenRelationPlanner (guidance timetable.create / eskalacja
    /// stuck). Preview→akceptuj→utwórz = AS-D3 w bogatszej formie niż generic plan preview.
    /// Bez PauseStack (świat żyje); backdrop-klik = zamknij.
    /// </summary>
    public class RelationPlannerUI : MonoBehaviour
    {
        public static RelationPlannerUI Instance { get; private set; }

        const int SortingOrder = 230;
        const float PanelWidth = 600f;
        const float PanelHeight = 640f;
        const int MaxSuggestions = 8;
        const int MaxShownCandidates = 3;
        const int MaxShownRejected = 4;

        GameObject _canvasRoot;
        RectTransform _content;
        TMP_InputField _fromInput;
        TMP_InputField _toInput;
        RectTransform _fromSuggestions;
        RectTransform _toSuggestions;
        RectTransform _results;

        RailwayStation _from;
        RailwayStation _to;
        readonly List<RailwayStation> _suggestionBuffer = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<RelationPlannerUI>() != null) return;
            var go = new GameObject("RelationPlannerUI");
            DontDestroyOnLoad(go);
            go.AddComponent<RelationPlannerUI>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            UIIntents.OnIntent += HandleIntent;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            UIIntents.OnIntent -= HandleIntent;
        }

        void HandleIntent(UIIntent intent)
        {
            if (intent == UIIntent.OpenRelationPlanner) Show();
        }

        // ────────────────────────── Pure (testowalne) ──────────────────────────

        /// <summary>
        /// Autocomplete stacji: prefiksy najpierw, potem zawieranie; min 2 znaki; cap max.
        /// Pure — EditMode-testowalne na syntetycznej liście.
        /// </summary>
        public static void FilterStations(IReadOnlyList<RailwayStation> stations, string query,
            List<RailwayStation> results, int max)
        {
            results.Clear();
            if (stations == null || string.IsNullOrWhiteSpace(query)) return;
            string q = query.Trim();
            if (q.Length < 2) return;

            foreach (var s in stations)
            {
                if (results.Count >= max) return;
                if (s?.name == null) continue;
                if (s.name.StartsWith(q, System.StringComparison.OrdinalIgnoreCase)) results.Add(s);
            }
            foreach (var s in stations)
            {
                if (results.Count >= max) return;
                if (s?.name == null || results.Contains(s)) continue;
                if (s.name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) results.Add(s);
            }
        }

        // ────────────────────────── Show / Hide ──────────────────────────

        public void Show()
        {
            if (_canvasRoot == null) BuildUI();
            _canvasRoot.SetActive(true);
            ClearResults();
        }

        public void Hide()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        [ContextMenu("Planner: otwórz (debug)")]
        void DebugOpen() => Show();

        // ────────────────────────── Logika panelu ──────────────────────────

        void OnComputeClicked()
        {
            ClearResults();

            if (!RelationFactsGatherer.IsReady)
            {
                AddLine(Loc("assistant.planner.ui.not_ready"), UITheme.Warning);
                return;
            }
            if (_from == null || _to == null || _from == _to)
            {
                AddLine(Loc("assistant.planner.ui.pick_both"), UITheme.Warning);
                return;
            }

            var facts = RelationFactsGatherer.Gather(_from, _to);
            var archetype = RelationPlannerCore.ClassifyArchetype(facts);

            // Fakty trasy.
            string electrified = Loc(facts.fullyElectrified
                ? "assistant.planner.ui.electrified_yes"
                : "assistant.planner.ui.electrified_no");
            AddLine(string.Format(Loc("assistant.planner.ui.facts_format"),
                facts.routeFound ? $"{facts.routeLengthKm:F0}" : "?",
                electrified,
                Mathf.RoundToInt(facts.estimatedDailyDemand),
                Loc("assistant.planner.ui.archetype." + ArchetypeKey(archetype))), null, bold: true);

            // Kandydaci z floty.
            var candidates = new List<VehicleCandidate>();
            foreach (var v in FleetService.OwnedVehicles)
            {
                var c = VehicleCandidate.FromVehicle(v);
                if (c != null) candidates.Add(c);
            }
            var verdicts = RelationPlannerCore.FilterAndScore(facts, candidates);

            var accepted = verdicts.FindAll(x => x.accepted);
            var rejected = verdicts.FindAll(x => !x.accepted);

            if (accepted.Count == 0)
            {
                AddLine(Loc("assistant.planner.ui.no_candidates"), UITheme.Danger);
                ShowRejected(rejected);
                return;
            }

            // Top kandydaci (informacyjnie) — warianty liczone dla najlepszego.
            AddLine(Loc("assistant.planner.ui.candidates_header"), null);
            for (int i = 0; i < accepted.Count && i < MaxShownCandidates; i++)
            {
                var a = accepted[i];
                string match = a.archetypeMatch ? " ★" : "";
                AddLine($"  {i + 1}. {a.candidate.label} — {a.score:F2}{match}", null);
            }
            ShowRejected(rejected);

            var top = accepted[0].candidate;
            AddLine(string.Format(Loc("assistant.planner.ui.variants_header_format"), top.label), null, bold: true);

            var category = TimetableService.GetCommercialCategory(
                TimetableFromRelationService.MapArchetypeToCategoryId(archetype));
            var variants = RelationPlannerCore.ComputeFrequencyVariants(facts, top);
            foreach (var variant in variants)
            {
                AddVariantRow(facts, archetype, top, variant, category);
            }
        }

        void ShowRejected(List<CandidateVerdict> rejected)
        {
            if (rejected.Count == 0) return;
            AddLine(string.Format(Loc("assistant.planner.ui.rejected_header_format"), rejected.Count),
                UITheme.WithAlpha(UITheme.GetTextColor(UIThemeTextRole.Secondary), 0.9f));
            for (int i = 0; i < rejected.Count && i < MaxShownRejected; i++)
            {
                var r = rejected[i];
                AddLine($"  ✕ {r.candidate.label} — {Loc(r.rejectReasonKey)}",
                    UITheme.WithAlpha(UITheme.GetTextColor(UIThemeTextRole.Secondary), 0.9f), small: true);
            }
        }

        void AddVariantRow(RelationFacts facts, RelationArchetype archetype,
            VehicleCandidate top, FrequencyVariant variant, CommercialCategory category)
        {
            var row = UIBuilders.MakeContainer(_results, UIBuilders.ContainerLayout.Horizontal,
                padding: UITheme.Spacing.Xs, spacing: UITheme.Spacing.Sm);
            SetHeight(row.gameObject, 52f);

            string variantName = Loc("assistant.planner.ui.variant." + variant.variantKey);
            string line = $"<b>{variantName}</b>  "
                + string.Format(Loc("assistant.planner.ui.variant_line_format"),
                    variant.runsPerDay, variant.taktMinutes, Mathf.RoundToInt(variant.occupancyPercent));

            if (category != null)
            {
                var estimate = RouteProfitabilityEstimator.Estimate(facts, top, variant.runsPerDay, category);
                string net = NumberFormatService.FormatCurrency(estimate.DailyNetGroszy / 100m);
                line += "\n" + string.Format(Loc("assistant.planner.ui.net_format"), net);
                if (estimate.subsidyEligible) line += "  " + Loc("assistant.planner.ui.subsidy_hint");
            }

            var label = UIBuilders.MakeLabel(row, line, UIBuilders.TypographyRole.Small);
            label.textWrappingMode = TextWrappingModes.Normal;
            SetPreferred(label.gameObject, PanelWidth - 190f, 48f);

            var createBtn = UIBuilders.MakeButton(row, Loc("assistant.planner.ui.create"), UIButtonTone.Primary);
            SetPreferred(createBtn.gameObject, 120f, 36f);
            var capturedVariant = variant;
            createBtn.onClick.AddListener(() => OnCreateClicked(top, capturedVariant, archetype));
        }

        void OnCreateClicked(VehicleCandidate top, FrequencyVariant variant, RelationArchetype archetype)
        {
            var tt = TimetableFromRelationService.Create(_from, _to, top, variant, archetype);
            if (tt == null)
            {
                AddLine(Loc("assistant.planner.ui.create_failed"), UITheme.Danger);
                return;
            }

            AssistantState.AddHistory(string.Format(Loc("assistant.planner.ui.history_format"), tt.name));
            AssistantWhisperUI.Instance?.Show(
                string.Format(Loc("assistant.planner.ui.created_format"), tt.name), null, null);
            Hide();
        }

        // ────────────────────────── Build UI ──────────────────────────

        void BuildUI()
        {
            _canvasRoot = new GameObject("RelationPlannerCanvas");
            _canvasRoot.transform.SetParent(transform);
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Backdrop = zamknij.
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(canvas.transform, false);
            UIPrimitives.Stretch((RectTransform)backdrop.transform);
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            backdrop.GetComponent<Button>().onClick.AddListener(Hide);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            UITheme.ApplySurface(panel.GetComponent<Image>(), UITheme.PrimarySurface, UIShapePreset.Panel);

            _content = UIBuilders.MakeContainer(panel.transform, UIBuilders.ContainerLayout.Vertical,
                padding: UITheme.Spacing.Md, spacing: UITheme.Spacing.Sm);
            UIPrimitives.Stretch(_content);

            // Nagłówek + X.
            var header = UIBuilders.MakeContainer(_content, UIBuilders.ContainerLayout.Horizontal,
                padding: 0f, spacing: UITheme.Spacing.Sm);
            SetHeight(header.gameObject, 32f);
            var title = UIBuilders.MakeLabel(header, Loc("assistant.planner.ui.title"), UIBuilders.TypographyRole.H2);
            SetPreferred(title.gameObject, PanelWidth - 90f, 30f);
            var close = UIBuilders.MakeButton(header, "✕", UIButtonTone.Secondary);
            SetPreferred(close.gameObject, 32f, 28f);
            close.onClick.AddListener(Hide);

            UIBuilders.MakeSeparator(_content);

            // Pola relacji.
            _fromInput = MakeStationInput(Loc("assistant.planner.ui.from_label"),
                out _fromSuggestions, station => { _from = station; });
            _toInput = MakeStationInput(Loc("assistant.planner.ui.to_label"),
                out _toSuggestions, station => { _to = station; });

            var compute = UIBuilders.MakeButton(_content, Loc("assistant.planner.ui.compute"), UIButtonTone.Primary);
            SetPreferred(compute.gameObject, 220f, 36f);
            compute.onClick.AddListener(OnComputeClicked);

            UIBuilders.MakeSeparator(_content);

            // Wyniki.
            _results = UIBuilders.MakeContainer(_content, UIBuilders.ContainerLayout.Vertical,
                padding: 0f, spacing: UITheme.Spacing.Xs);
            var rle = _results.gameObject.AddComponent<LayoutElement>();
            rle.flexibleHeight = 1f;

            _canvasRoot.SetActive(false);
        }

        TMP_InputField MakeStationInput(string labelText, out RectTransform suggestions,
            System.Action<RailwayStation> onPicked)
        {
            var label = UIBuilders.MakeLabel(_content, labelText, UIBuilders.TypographyRole.Small);
            SetHeight(label.gameObject, 18f);

            // Pole tekstowe (TMP_InputField proceduralnie — viewport + text + placeholder).
            var go = new GameObject("StationInput", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_content, false);
            SetHeight(go, 32f);
            var img = go.GetComponent<Image>();
            UITheme.ApplySurface(img, UITheme.RaisedSurface, UIShapePreset.Inset);

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            input.lineType = TMP_InputField.LineType.SingleLine;

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(UITheme.Spacing.Sm, 2f);
            vrt.offsetMax = new Vector2(-UITheme.Spacing.Sm, -2f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(viewport.transform, false);
            UIPrimitives.Stretch((RectTransform)textGo.transform);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            text.fontSize = UITheme.Typography.Small;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(viewport.transform, false);
            UIPrimitives.Stretch((RectTransform)phGo.transform);
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(ph, UIThemeTextRole.Secondary);
            ph.fontSize = UITheme.Typography.Small;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.text = Loc("assistant.planner.ui.station_placeholder");

            input.textViewport = vrt;
            input.textComponent = text;
            input.placeholder = ph;

            // Podpowiedzi pod polem.
            var sugg = UIBuilders.MakeContainer(_content, UIBuilders.ContainerLayout.Vertical,
                padding: 0f, spacing: 2f);
            suggestions = sugg;

            var suggRef = sugg;
            input.onValueChanged.AddListener(query => RebuildSuggestions(query, suggRef, input, onPicked));
            return input;
        }

        void RebuildSuggestions(string query, RectTransform container, TMP_InputField input,
            System.Action<RailwayStation> onPicked)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }

            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady) return;

            FilterStations(init.Stations, query, _suggestionBuffer, MaxSuggestions);
            foreach (var station in _suggestionBuffer)
            {
                var picked = station;
                var btn = UIBuilders.MakeButton(container, picked.name, UIButtonTone.Secondary);
                SetPreferred(btn.gameObject, PanelWidth - 60f, 24f);
                btn.onClick.AddListener(() =>
                {
                    onPicked(picked);
                    input.SetTextWithoutNotify(picked.name);
                    for (int i = container.childCount - 1; i >= 0; i--)
                    {
                        Destroy(container.GetChild(i).gameObject);
                    }
                });
            }
        }

        // ── Helpers ──

        void ClearResults()
        {
            if (_results == null) return;
            for (int i = _results.childCount - 1; i >= 0; i--)
            {
                Destroy(_results.GetChild(i).gameObject);
            }
        }

        void AddLine(string text, Color? color, bool bold = false, bool small = false)
        {
            var tmp = UIBuilders.MakeLabel(_results, text,
                small ? UIBuilders.TypographyRole.Tiny : UIBuilders.TypographyRole.Small, color);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            SetHeight(tmp.gameObject, small ? 16f : 20f);
        }

        static string ArchetypeKey(RelationArchetype a) => a switch
        {
            RelationArchetype.Agglomeration => "agglomeration",
            RelationArchetype.Regional => "regional",
            _ => "interregional"
        };

        static string Loc(string key) => LocalizationService.Get(key);

        static void SetHeight(GameObject go, float height)
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
