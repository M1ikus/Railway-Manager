using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// M-FC-7: Paint editor — partial FleetPanelUI. Sekcja "Malowanie" w prawym panelu
    /// konfiguratora wagon/family. Phase A (M-FC-7a):
    /// - 3D preview cuboid z drag-orbit + scroll-zoom
    /// - Bottom paint color picker (paleta predefined)
    /// - Stripes lista wierszy (add via preset, edit color/position, delete)
    /// Phase B (M-FC-7b): decals + custom text + export/import shareable string.
    /// Phase C (M-FC-7c): per-segment mode + lista członów.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── State ────────────────────────────────────────────

        private PaintDefinition _currentPaint;       // edytowany przez UI; clone przy add to cart
        private PaintPreview3D _paintPreview;
        private RawImage _paintPreviewImage;
        private GameObject _paintPreviewGO;          // root w hidden world (5000,5000,5000)

        // Phase B: decals state
        private bool _decalPickerOpen;
        private bool _importPanelOpen;
        private string _importBuffer = "";

        // Phase C: per-segment mode state
        private int _activeSegment;

        // Predefined kolory PL — paleta dla bottom paint i stripes
        private static readonly (string name, string hex)[] PaintPalette = new[]
        {
            ("PKP IC granat", "#103090"),
            ("PKP IC biały", "#FAFAFA"),
            ("PKP IC red", "#DC0000"),
            ("Mazowieckie żółty", "#FFCC00"),
            ("Koleje Śląskie zielony", "#008844"),
            ("ŁKA pomarańczowy", "#FF6600"),
            ("Szary", "#808080"),
            ("Granatowy", "#1A1A4A"),
            ("Bordowy", "#7A1F2D"),
            ("Czarny", "#1A1A1A")
        };

        // ── Public API ───────────────────────────────────────

        /// <summary>
        /// Buduje sekcję "Malowanie" w prawym panelu. Init'uje paint preview 3D + color picker
        /// + stripes manager. Mutuje przekazaną PaintDefinition; po zmianach wywołuje onChanged.
        /// </summary>
        private void BuildPaintEditorSection(
            PaintDefinition paint,
            int segmentCount,
            float totalLengthM,
            System.Action onChanged)
        {
            _currentPaint = paint;
            EnsurePaintSegmentCount(_currentPaint, segmentCount);
            if (_activeSegment >= segmentCount) _activeSegment = 0;
            EnsurePaintPreviewExists(segmentCount, totalLengthM);

            // 3D preview area
            BuildPaintPreviewArea();

            BuildSeparator();

            // Phase C: Mode toolbar (All / Per Segment) — pokazany tylko gdy >1 segment
            if (segmentCount > 1)
            {
                BuildModeToolbar(onChanged);
                if (_currentPaint.applyMode == "PerSegment")
                {
                    BuildSegmentSelector(segmentCount, onChanged);
                }
                BuildSeparator();
            }

            // Bottom paint section
            BuildBottomPaintSection(onChanged);

            BuildSeparator();

            // Stripes section
            BuildStripesSection(onChanged);

            BuildSeparator();

            // Phase B: Decals section
            BuildDecalsSection(onChanged);

            BuildSeparator();

            // Phase B: Export/Import shareable string
            BuildExportImportSection(onChanged);

            // Refresh 3D preview po build (initial state)
            RefreshPaintPreview();
        }

        // ── Phase C: Per-segment mode ────────────────────────

        private void BuildModeToolbar(System.Action onChanged)
        {
            BuildSectionHeader("Tryb edycji");

            var card = NewGO("ModeToolbar", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 52f;
            var hl = card.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = false;
            hl.childControlWidth = true; hl.childControlHeight = true;

            BuildModeToggle(card.transform, "Cały zespół", _currentPaint.applyMode == "AllSegments", () => {
                if (_currentPaint.applyMode != "AllSegments")
                {
                    _currentPaint.applyMode = "AllSegments";
                    // Synchronize: skopiuj segment[0] na resztę żeby wszystkie były takie same
                    PropagateSegment0ToAll();
                    OnPaintEdited(onChanged);
                }
            });
            BuildModeToggle(card.transform, "Per człon (zaawansowane)", _currentPaint.applyMode == "PerSegment", () => {
                if (_currentPaint.applyMode != "PerSegment")
                {
                    _currentPaint.applyMode = "PerSegment";
                    onChanged?.Invoke();
                }
            });
        }

        private void BuildModeToggle(Transform parent, string label, bool isSelected, System.Action onClick)
        {
            var go = NewGO($"Mode_{label}", parent);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.preferredHeight = 36f;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img,
                isSelected ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.40f)
                           : UITheme.WithAlpha(UITheme.Border, 0.22f),
                UIShapePreset.Button);
            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text = (isSelected ? "● " : "○ ") + label;
            lbl.fontSize = 12; lbl.color = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false; FillRT(lbl.gameObject);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        private void BuildSegmentSelector(int segmentCount, System.Action onChanged)
        {
            BuildSectionHeader("Wybierz człon");

            var card = NewGO("SegmentSelector", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 56f;
            var hl = card.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Xs;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            for (int i = 0; i < segmentCount; i++)
            {
                int capturedIdx = i;
                var btnGO = NewGO($"Seg_{i}", card.transform);
                var btnLE = btnGO.AddComponent<LayoutElement>();
                btnLE.preferredWidth = 56f; btnLE.preferredHeight = 40f;
                var btnImg = btnGO.AddComponent<Image>();
                UITheme.ApplySurface(btnImg,
                    _activeSegment == i ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.50f) : RowBg,
                    UIShapePreset.Button);
                var btnLbl = MakeTMP("Lbl", btnGO.transform);
                btnLbl.text = (i + 1).ToString();
                btnLbl.fontSize = 14; btnLbl.fontStyle = FontStyles.Bold;
                btnLbl.color = TextPrimary;
                btnLbl.alignment = TextAlignmentOptions.Center;
                btnLbl.raycastTarget = false; FillRT(btnLbl.gameObject);
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => {
                    _activeSegment = capturedIdx;
                    onChanged?.Invoke();
                });
            }
        }

        /// <summary>Zwraca segments które aktualnie podlegają edycji (zgodnie z applyMode).</summary>
        private List<SegmentPaint> GetEditableSegments()
        {
            if (_currentPaint.applyMode == "PerSegment"
                && _activeSegment >= 0
                && _activeSegment < _currentPaint.segments.Count)
            {
                return new List<SegmentPaint> { _currentPaint.segments[_activeSegment] };
            }
            return _currentPaint.segments;
        }

        /// <summary>Zwraca segment z którego pokazujemy stripes/decals w UI (do edycji + display).</summary>
        private SegmentPaint GetDisplaySegment()
        {
            if (_currentPaint.segments.Count == 0) return null;
            int idx = _currentPaint.applyMode == "PerSegment" ? _activeSegment : 0;
            if (idx < 0 || idx >= _currentPaint.segments.Count) idx = 0;
            return _currentPaint.segments[idx];
        }

        /// <summary>Po zmianie z PerSegment → AllSegments: ujednolicić wszystkie segmenty na podstawie segment[0].</summary>
        private void PropagateSegment0ToAll()
        {
            if (_currentPaint.segments.Count <= 1) return;
            var src = _currentPaint.segments[0];
            for (int i = 1; i < _currentPaint.segments.Count; i++)
            {
                var seg = _currentPaint.segments[i];
                seg.baseColor = src.baseColor;
                seg.stripes.Clear();
                foreach (var s in src.stripes)
                    seg.stripes.Add(new StripeLayer { presetId = s.presetId, positionY = s.positionY,
                        thickness = s.thickness, color = s.color, mode = s.mode });
                seg.decals.Clear();
                foreach (var d in src.decals)
                    seg.decals.Add(new DecalLayer { symbolId = d.symbolId, positionX = d.positionX,
                        positionY = d.positionY, scale = d.scale, rotation = d.rotation,
                        color = d.color, customText = d.customText });
            }
        }

        /// <summary>Resize PaintDefinition.segments do target count. Dodaje defaults gdy brakuje, trim gdy nadmiar.</summary>
        private static void EnsurePaintSegmentCount(PaintDefinition paint, int targetCount)
        {
            if (paint == null) return;
            while (paint.segments.Count < targetCount)
            {
                paint.segments.Add(new SegmentPaint
                {
                    segmentIndex = paint.segments.Count,
                    baseColor = paint.segments.Count > 0 ? paint.segments[0].baseColor : "#FAFAFA"
                });
            }
            while (paint.segments.Count > targetCount)
            {
                paint.segments.RemoveAt(paint.segments.Count - 1);
            }
        }

        /// <summary>Wywołać po destroy paint editor (np. na zmianę modelu) by zwolnić zasoby 3D.</summary>
        private void DestroyPaintPreview()
        {
            if (_paintPreviewGO != null)
            {
                Destroy(_paintPreviewGO);
                _paintPreviewGO = null;
                _paintPreview = null;
            }
        }

        // Cleanup paint preview GO przy destroy panelu (parent paint preview NIE w UI hierarchy
        // bo używa world space, więc trzeba ręcznie sprzątać).
        private void OnDestroy()
        {
            DestroyPaintPreview();
        }

        /// <summary>M-FC-7: deep clone PaintDefinition (do kopiowania state'u → CartItem.paint).</summary>
        private static PaintDefinition ClonePaintDefinition(PaintDefinition src)
        {
            if (src == null) return new PaintDefinition();
            var clone = new PaintDefinition
            {
                schemaVersion = src.schemaVersion,
                applyMode = src.applyMode
            };
            if (src.segments != null)
            {
                foreach (var seg in src.segments)
                {
                    var segClone = new SegmentPaint
                    {
                        segmentIndex = seg.segmentIndex,
                        baseColor = seg.baseColor
                    };
                    if (seg.stripes != null)
                    {
                        foreach (var s in seg.stripes)
                            segClone.stripes.Add(new StripeLayer
                            {
                                presetId = s.presetId,
                                positionY = s.positionY,
                                thickness = s.thickness,
                                color = s.color,
                                mode = s.mode
                            });
                    }
                    if (seg.decals != null)
                    {
                        foreach (var d in seg.decals)
                            segClone.decals.Add(new DecalLayer
                            {
                                symbolId = d.symbolId,
                                positionX = d.positionX,
                                positionY = d.positionY,
                                scale = d.scale,
                                rotation = d.rotation,
                                color = d.color,
                                customText = d.customText
                            });
                    }
                    clone.segments.Add(segClone);
                }
            }
            return clone;
        }

        // ── 3D preview ───────────────────────────────────────

        private void EnsurePaintPreviewExists(int segmentCount, float totalLengthM)
        {
            if (_paintPreviewGO == null)
            {
                _paintPreviewGO = new GameObject("PaintPreview3D");
                _paintPreview = _paintPreviewGO.AddComponent<PaintPreview3D>();
            }
            _paintPreview.Init(segmentCount, totalLengthM);
        }

        private void BuildPaintPreviewArea()
        {
            var card = NewGO("PaintPreviewCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 252f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            // Header
            var hdr = MakeTMP("Hdr", card.transform);
            hdr.text = "<i>Podgląd 3D — przeciągnij myszką by obracać, scroll by zoom</i>";
            hdr.fontSize = 11; hdr.color = TextMuted;
            hdr.richText = true; hdr.raycastTarget = false;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            // RawImage container (centered, fixed aspect)
            var imageHolder = NewGO("RawImageHolder", card.transform);
            imageHolder.AddComponent<LayoutElement>().preferredHeight = 180f;
            var rawImg = imageHolder.AddComponent<RawImage>();
            rawImg.texture = _paintPreview != null ? _paintPreview.RT : null;
            rawImg.color = Color.white;
            _paintPreviewImage = rawImg;

            var orbitHandler = imageHolder.AddComponent<PaintPreviewOrbitHandler>();
            orbitHandler.preview = _paintPreview;
        }

        // ── Bottom paint ─────────────────────────────────────

        private void BuildBottomPaintSection(System.Action onChanged)
        {
            BuildSectionHeader("Kolor podstawowy");

            var card = NewGO("BottomPaintCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 100f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            // Current color label (z aktywnego segment'u)
            var displaySeg = GetDisplaySegment();
            string currentColor = displaySeg?.baseColor ?? "#FFFFFF";
            var curLbl = MakeTMP("CurLbl", card.transform);
            curLbl.text = $"Kolor: {currentColor}" +
                (_currentPaint.applyMode == "PerSegment" ? $" (człon {_activeSegment + 1})" : "");
            curLbl.fontSize = 11; curLbl.color = TextMuted;
            curLbl.raycastTarget = false;
            curLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            // Palette grid
            var paletteGO = NewGO("Palette", card.transform);
            paletteGO.AddComponent<LayoutElement>().preferredHeight = 58f;
            var hl = paletteGO.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = UITheme.Spacing.Sm;
            hl.padding = new RectOffset(0, 0, 0, 0);
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            foreach (var (name, hex) in PaintPalette)
            {
                BuildPaletteSwatch(paletteGO.transform, name, hex, () => {
                    foreach (var seg in GetEditableSegments()) seg.baseColor = hex;
                    OnPaintEdited(onChanged);
                });
            }
        }

        private void BuildPaletteSwatch(Transform parent, string name, string hex, System.Action onClick)
        {
            var go = NewGO($"Swatch_{name}", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 36f; le.preferredHeight = 36f;
            var img = go.AddComponent<Image>();
            ColorUtility.TryParseHtmlString(hex, out var color);
            UITheme.ApplySurface(img, color, UIShapePreset.Button);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        // ── Stripes ──────────────────────────────────────────

        private void BuildStripesSection(System.Action onChanged)
        {
            string suffix = _currentPaint.applyMode == "PerSegment" ? $" (człon {_activeSegment + 1})" : "";
            BuildSectionHeader("Paski" + suffix);

            var seg = GetDisplaySegment();
            int stripeCount = seg?.stripes?.Count ?? 0;

            var card = NewGO("StripesCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = stripeCount * 56f + 88f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var summary = MakeTMP("Summary", card.transform);
            summary.text = stripeCount == 0
                ? "Brak paskow. Dodaj preset, aby szybko zbudowac warstwe akcentow."
                : $"{stripeCount} warstw paskow gotowych do edycji.";
            summary.fontSize = 10;
            summary.color = TextMuted;
            summary.raycastTarget = false;
            summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            if (seg != null && seg.stripes != null)
            {
                for (int i = 0; i < seg.stripes.Count; i++)
                {
                    int capturedIdx = i;
                    BuildStripeRow(card.transform, seg.stripes, capturedIdx, onChanged);
                }
            }

            // Preset picker (add new stripe via preset)
            var addRowGO = NewGO("AddRow", card.transform);
            addRowGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var addImg = addRowGO.AddComponent<Image>();
            UITheme.ApplySurface(addImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f), UIShapePreset.Button);
            var addLbl = MakeTMP("Lbl", addRowGO.transform);
            addLbl.text = "+ Dodaj pasek (preset)";
            addLbl.fontSize = 12; addLbl.color = TextPrimary;
            addLbl.alignment = TextAlignmentOptions.Center;
            addLbl.raycastTarget = false; FillRT(addLbl.gameObject);
            var addBtn = addRowGO.AddComponent<Button>();
            addBtn.targetGraphic = addImg;
            addBtn.transition = Selectable.Transition.None;
            addBtn.onClick.AddListener(() => OnAddStripePresetClicked(onChanged));
        }

        private void BuildStripeRow(Transform parent, List<StripeLayer> stripes, int idx, System.Action onChanged)
        {
            var stripe = stripes[idx];

            var row = NewGO($"Stripe_{idx}", parent);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 52f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            var nameLbl = MakeTMP("Name", row.transform);
            nameLbl.text = $"Pasek {idx + 1}";
            nameLbl.fontSize = 11;
            nameLbl.fontStyle = FontStyles.Bold;
            nameLbl.color = TextPrimary;
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            nameLbl.raycastTarget = false;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

            // Color swatch (klik → cycle przez paletę)
            var swatchGO = NewGO("Swatch", row.transform);
            var swatchLE = swatchGO.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = 30f; swatchLE.preferredHeight = 30f;
            var swatchImg = swatchGO.AddComponent<Image>();
            ColorUtility.TryParseHtmlString(stripe.color, out var swColor);
            UITheme.ApplySurface(swatchImg, swColor, UIShapePreset.Button);
            var swatchBtn = swatchGO.AddComponent<Button>();
            swatchBtn.targetGraphic = swatchImg;
            swatchBtn.transition = Selectable.Transition.None;
            swatchBtn.onClick.AddListener(() => CyclePaletteColor(stripe, onChanged));

            // Position Y slider
            var posGO = NewGO("PosSlider", row.transform);
            var posLE = posGO.AddComponent<LayoutElement>();
            posLE.preferredWidth = 100f; posLE.preferredHeight = 24f;
            BuildSimpleSlider(posGO.transform, stripe.positionY, 0, 1, (newVal) => {
                stripe.positionY = newVal;
                OnPaintEdited(onChanged);
            });

            // Thickness slider
            var thickGO = NewGO("ThickSlider", row.transform);
            var thickLE = thickGO.AddComponent<LayoutElement>();
            thickLE.preferredWidth = 80f; thickLE.preferredHeight = 24f;
            BuildSimpleSlider(thickGO.transform, stripe.thickness, 0.01f, 0.4f, (newVal) => {
                stripe.thickness = newVal;
                OnPaintEdited(onChanged);
            });

            // Info label
            var infoLbl = MakeTMP("Info", row.transform);
            infoLbl.text = $"Y:{stripe.positionY:0.00}\nGr:{stripe.thickness:0.00}";
            infoLbl.fontSize = 9; infoLbl.color = TextMuted;
            infoLbl.alignment = TextAlignmentOptions.MidlineLeft;
            infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            // Delete button
            var delGO = NewGO("Del", row.transform);
            var delLE = delGO.AddComponent<LayoutElement>();
            delLE.preferredWidth = 42f; delLE.preferredHeight = 30f;
            var delImg = delGO.AddComponent<Image>();
            UITheme.ApplySurface(delImg, UITheme.WithAlpha(UITheme.Danger, 0.22f), UIShapePreset.Button);
            var delLbl = MakeTMP("Lbl", delGO.transform);
            delLbl.text = "✕"; delLbl.fontSize = 14; delLbl.color = TextPrimary;
            delLbl.alignment = TextAlignmentOptions.Center;
            delLbl.raycastTarget = false; FillRT(delLbl.gameObject);
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            delBtn.transition = Selectable.Transition.None;
            delBtn.onClick.AddListener(() => {
                stripes.RemoveAt(idx);
                OnPaintEdited(onChanged);
            });
        }

        private void OnAddStripePresetClicked(System.Action onChanged)
        {
            if (PaintPresetsCatalog.Presets.Count == 0) return;

            // Cycle przez presety na podstawie liczby stripes w aktywnym segmencie
            var displaySeg = GetDisplaySegment();
            int currentStripes = displaySeg?.stripes?.Count ?? 0;
            int presetIdx = currentStripes % PaintPresetsCatalog.Presets.Count;
            var preset = PaintPresetsCatalog.Presets[presetIdx];

            var newStripes = PaintPresetsCatalog.Apply(preset.id);
            var targets = GetEditableSegments();

            foreach (var seg in targets)
            {
                if (seg.stripes.Count + newStripes.Count > PaintSerializer.MAX_STRIPES_PER_SEGMENT)
                {
                    Log.Warn($"[PaintEditor] Max {PaintSerializer.MAX_STRIPES_PER_SEGMENT} stripes per segment — pomijam");
                    return;
                }
                foreach (var s in newStripes)
                {
                    seg.stripes.Add(new StripeLayer
                    {
                        presetId = s.presetId,
                        positionY = s.positionY,
                        thickness = s.thickness,
                        color = s.color,
                        mode = s.mode
                    });
                }
            }
            OnPaintEdited(onChanged);
        }

        private void CyclePaletteColor(StripeLayer stripe, System.Action onChanged)
        {
            // Find current color in palette and cycle to next
            int currentIdx = -1;
            for (int i = 0; i < PaintPalette.Length; i++)
            {
                if (PaintPalette[i].hex.Equals(stripe.color, System.StringComparison.OrdinalIgnoreCase))
                { currentIdx = i; break; }
            }
            int nextIdx = (currentIdx + 1) % PaintPalette.Length;
            stripe.color = PaintPalette[nextIdx].hex;
            OnPaintEdited(onChanged);
        }

        // ── Decals (Phase B) ─────────────────────────────────

        private void BuildDecalsSection(System.Action onChanged)
        {
            string suffix = _currentPaint.applyMode == "PerSegment" ? $" (człon {_activeSegment + 1})" : "";
            BuildSectionHeader("Symbole / dekoracje" + suffix);

            var seg = GetDisplaySegment();
            int decalCount = seg?.decals?.Count ?? 0;

            float pickerHeight = _decalPickerOpen ? 280f : 0f;
            var card = NewGO("DecalsCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = decalCount * 60f + 84f + pickerHeight;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var summary = MakeTMP("Summary", card.transform);
            summary.text = decalCount == 0
                ? "Dodaj symbole, logotypy albo prosty tekst na pudle pojazdu."
                : $"{decalCount} dekoracji gotowych do ustawienia na modelu.";
            summary.fontSize = 10;
            summary.color = TextMuted;
            summary.raycastTarget = false;
            summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            if (seg != null && seg.decals != null)
            {
                for (int i = 0; i < seg.decals.Count; i++)
                {
                    int capturedIdx = i;
                    BuildDecalRow(card.transform, seg.decals, capturedIdx, onChanged);
                }
            }

            // "+ Dodaj decal" button
            var addRowGO = NewGO("AddDecalBtn", card.transform);
            addRowGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var addImg = addRowGO.AddComponent<Image>();
            UITheme.ApplySurface(addImg,
                _decalPickerOpen ? UITheme.WithAlpha(UITheme.Warning, 0.18f)
                                 : UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f),
                UIShapePreset.Button);
            var addLbl = MakeTMP("Lbl", addRowGO.transform);
            addLbl.text = _decalPickerOpen ? "✕ Anuluj" : "+ Dodaj symbol";
            addLbl.fontSize = 12; addLbl.color = TextPrimary;
            addLbl.alignment = TextAlignmentOptions.Center;
            addLbl.raycastTarget = false; FillRT(addLbl.gameObject);
            var addBtn = addRowGO.AddComponent<Button>();
            addBtn.targetGraphic = addImg;
            addBtn.transition = Selectable.Transition.None;
            addBtn.onClick.AddListener(() => {
                _decalPickerOpen = !_decalPickerOpen;
                onChanged?.Invoke();
            });

            // Symbol picker grid (gdy open)
            if (_decalPickerOpen)
            {
                BuildSymbolPickerGrid(card.transform, onChanged);
            }
        }

        private void BuildDecalRow(Transform parent, List<DecalLayer> decals, int idx, System.Action onChanged)
        {
            var decal = decals[idx];
            var def = DecalCatalog.Find(decal.symbolId);
            string label = def != null ? def.displayName : decal.symbolId;
            bool isText = def != null && def.supportsCustomText;

            var row = NewGO($"Decal_{idx}", parent);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 60f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            var typeLbl = MakeTMP("Type", row.transform);
            typeLbl.text = isText ? "Tekst" : label;
            typeLbl.fontSize = 11;
            typeLbl.fontStyle = FontStyles.Bold;
            typeLbl.color = TextPrimary;
            typeLbl.alignment = TextAlignmentOptions.MidlineLeft;
            typeLbl.raycastTarget = false;
            typeLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

            // Color swatch (cycle)
            var swatchGO = NewGO("Swatch", row.transform);
            var swatchLE = swatchGO.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = 30f; swatchLE.preferredHeight = 30f;
            var swatchImg = swatchGO.AddComponent<Image>();
            ColorUtility.TryParseHtmlString(decal.color, out var swColor);
            UITheme.ApplySurface(swatchImg, swColor, UIShapePreset.Button);
            var swatchBtn = swatchGO.AddComponent<Button>();
            swatchBtn.targetGraphic = swatchImg;
            swatchBtn.transition = Selectable.Transition.None;
            swatchBtn.onClick.AddListener(() => CycleDecalColor(decal, onChanged));

            // Label / text input (gdy text-supported)
            if (isText)
            {
                var inputGO = NewGO("TextInput", row.transform);
                var inputLE = inputGO.AddComponent<LayoutElement>();
                inputLE.preferredWidth = 130f; inputLE.preferredHeight = 30f;
                var inputBg = inputGO.AddComponent<Image>();
                UITheme.ApplySurface(inputBg, UITheme.WithAlpha(UITheme.Border, 0.3f), UIShapePreset.Button);

                var inputField = inputGO.AddComponent<TMP_InputField>();
                inputField.targetGraphic = inputBg;
                var textGO = NewGO("Text", inputGO.transform);
                var textRT = textGO.GetComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(8, 4); textRT.offsetMax = new Vector2(-8, -4);
                var textTMP = textGO.AddComponent<TextMeshProUGUI>();
                textTMP.text = ""; textTMP.fontSize = 12; textTMP.color = TextPrimary;
                inputField.textComponent = textTMP;
                inputField.text = decal.customText ?? "";
                inputField.characterLimit = 30;
                inputField.onValueChanged.AddListener(v => {
                    decal.customText = v;
                    OnPaintEdited(onChanged);
                });
            }
            else
            {
                var infoLbl = MakeTMP("Lbl", row.transform);
                infoLbl.text = label;
                infoLbl.fontSize = 11; infoLbl.color = TextPrimary;
                infoLbl.alignment = TextAlignmentOptions.MidlineLeft;
                infoLbl.raycastTarget = false;
                infoLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
            }

            // Position X slider
            var posXGO = NewGO("PosX", row.transform);
            posXGO.AddComponent<LayoutElement>().preferredWidth = 60f;
            posXGO.GetComponent<LayoutElement>().preferredHeight = 24f;
            BuildSimpleSlider(posXGO.transform, decal.positionX, 0, 1, (v) => {
                decal.positionX = v;
                OnPaintEdited(onChanged);
            });

            // Position Y slider
            var posYGO = NewGO("PosY", row.transform);
            posYGO.AddComponent<LayoutElement>().preferredWidth = 60f;
            posYGO.GetComponent<LayoutElement>().preferredHeight = 24f;
            BuildSimpleSlider(posYGO.transform, decal.positionY, 0, 1, (v) => {
                decal.positionY = v;
                OnPaintEdited(onChanged);
            });

            var posLbl = MakeTMP("PosInfo", row.transform);
            posLbl.text = $"X:{decal.positionX:0.00}\nY:{decal.positionY:0.00}";
            posLbl.fontSize = 9;
            posLbl.color = TextMuted;
            posLbl.alignment = TextAlignmentOptions.MidlineLeft;
            posLbl.raycastTarget = false;
            posLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 54f;

            // Delete
            var delGO = NewGO("Del", row.transform);
            var delLE = delGO.AddComponent<LayoutElement>();
            delLE.preferredWidth = 42f; delLE.preferredHeight = 30f;
            var delImg = delGO.AddComponent<Image>();
            UITheme.ApplySurface(delImg, UITheme.WithAlpha(UITheme.Danger, 0.22f), UIShapePreset.Button);
            var delLbl = MakeTMP("Lbl", delGO.transform);
            delLbl.text = "✕"; delLbl.fontSize = 14; delLbl.color = TextPrimary;
            delLbl.alignment = TextAlignmentOptions.Center;
            delLbl.raycastTarget = false; FillRT(delLbl.gameObject);
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            delBtn.transition = Selectable.Transition.None;
            delBtn.onClick.AddListener(() => {
                decals.RemoveAt(idx);
                OnPaintEdited(onChanged);
            });
        }

        private void BuildSymbolPickerGrid(Transform parent, System.Action onChanged)
        {
            var pickerGO = NewGO("SymbolPicker", parent);
            pickerGO.AddComponent<LayoutElement>().preferredHeight = 268f;
            var pickerImg = pickerGO.AddComponent<Image>();
            UITheme.ApplySurface(pickerImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.08f), UIShapePreset.Inset);
            var pickerVL = pickerGO.AddComponent<VerticalLayoutGroup>();
            pickerVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
            pickerVL.spacing = UITheme.Spacing.Xs;
            pickerVL.childForceExpandWidth = true; pickerVL.childForceExpandHeight = false;
            pickerVL.childControlWidth = true; pickerVL.childControlHeight = true;

            var hdr = MakeTMP("Hdr", pickerGO.transform);
            hdr.text = "<i>Wybierz symbol:</i>";
            hdr.fontSize = 12; hdr.color = TextMuted;
            hdr.richText = true; hdr.raycastTarget = false;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            // Scrollable grid (5 cols × 6 rows = 30 slots)
            var scrollGO = NewGO("Scroll", pickerGO.transform);
            scrollGO.AddComponent<LayoutElement>().preferredHeight = 240f;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.scrollSensitivity = 30f;

            var viewport = NewGO("VP", scrollGO.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = NewGO("Content", viewport.transform);
            var cRT = content.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 1); cRT.anchorMax = Vector2.one;
            cRT.pivot = new Vector2(0.5f, 1);
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(70, 50);
            grid.spacing = new Vector2(4, 4);
            grid.padding = UITheme.Padding(UITheme.Spacing.Xs);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cRT;

            foreach (var d in DecalCatalog.Decals)
            {
                var capturedDecal = d;
                BuildSymbolCell(content.transform, d, () => {
                    OnSymbolPicked(capturedDecal, onChanged);
                });
            }
        }

        private void BuildSymbolCell(Transform parent, DecalDef decal, System.Action onClick)
        {
            var cell = NewGO($"Cell_{decal.id}", parent);
            var cellImg = cell.AddComponent<Image>();
            UITheme.ApplySurface(cellImg, RowBg, UIShapePreset.Button);
            var vl = cell.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Xxs);
            vl.spacing = UITheme.Spacing.Xxs;
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            // Placeholder ikona — kolorowy kwadrat z literą category
            var iconGO = NewGO("Icon", cell.transform);
            iconGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.color = GetDecalCategoryColor(decal.category);
            var iconLbl = MakeTMP("L", iconGO.transform);
            iconLbl.text = GetDecalShortLabel(decal);
            iconLbl.fontSize = 10; iconLbl.fontStyle = FontStyles.Bold;
            iconLbl.color = TextPrimary;
            iconLbl.alignment = TextAlignmentOptions.Center;
            iconLbl.raycastTarget = false; FillRT(iconLbl.gameObject);

            var nameLbl = MakeTMP("Name", cell.transform);
            nameLbl.text = decal.displayName;
            nameLbl.fontSize = 8; nameLbl.color = TextMuted;
            nameLbl.alignment = TextAlignmentOptions.Center;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.raycastTarget = false;

            var btn = cell.AddComponent<Button>();
            btn.targetGraphic = cellImg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            cell.AddComponent<HoverImageColor>().Init(cellImg, RowBg, RowHover);
        }

        private void OnSymbolPicked(DecalDef def, System.Action onChanged)
        {
            var targets = GetEditableSegments();

            // Sprawdź limit decali per segment
            foreach (var seg in targets)
            {
                if (seg.decals.Count >= PaintSerializer.MAX_DECALS_PER_SEGMENT)
                {
                    Log.Warn($"[PaintEditor] Max {PaintSerializer.MAX_DECALS_PER_SEGMENT} decali per segment — pomijam");
                    _decalPickerOpen = false;
                    onChanged?.Invoke();
                    return;
                }
            }

            // Dodaj do edytowalnych segmentów (AllSegments → wszystkie, PerSegment → tylko aktywny)
            foreach (var seg in targets)
            {
                seg.decals.Add(new DecalLayer
                {
                    symbolId = def.id,
                    positionX = 0.5f,
                    positionY = 0.5f,
                    scale = 1.0f,
                    rotation = 0f,
                    color = "#000000",
                    customText = def.supportsCustomText ? def.displayName : ""
                });
            }
            _decalPickerOpen = false;
            OnPaintEdited(onChanged);
        }

        private void CycleDecalColor(DecalLayer decal, System.Action onChanged)
        {
            int currentIdx = -1;
            for (int i = 0; i < PaintPalette.Length; i++)
            {
                if (PaintPalette[i].hex.Equals(decal.color, System.StringComparison.OrdinalIgnoreCase))
                { currentIdx = i; break; }
            }
            int nextIdx = (currentIdx + 1) % PaintPalette.Length;
            decal.color = PaintPalette[nextIdx].hex;
            OnPaintEdited(onChanged);
        }

        private static Color GetDecalCategoryColor(string category) => category switch
        {
            "info" => new Color(0.3f, 0.6f, 1f, 0.7f),       // niebieski
            "warning" => new Color(0.9f, 0.6f, 0.2f, 0.7f),  // pomarańczowy
            "digit" => new Color(0.4f, 0.4f, 0.4f, 0.7f),    // szary
            "letter" => new Color(0.5f, 0.3f, 0.6f, 0.7f),   // fiolet
            "arrow" => new Color(0.4f, 0.7f, 0.4f, 0.7f),    // zielony
            "logo" => new Color(0.7f, 0.4f, 0.4f, 0.7f),     // bordowy
            _ => new Color(0.5f, 0.5f, 0.5f, 0.7f)
        };

        private static string GetDecalShortLabel(DecalDef d)
        {
            if (d.category == "digit") return d.id.Replace("digit-", "");
            if (d.category == "arrow")
            {
                if (d.id.Contains("up")) return "↑";
                if (d.id.Contains("down")) return "↓";
                if (d.id.Contains("left")) return "←";
                if (d.id.Contains("right")) return "→";
            }
            if (d.category == "warning") return "!";
            if (d.id == "custom-text") return "Aa";
            if (d.id == "company-name") return "©";
            // Info icons — pierwsza litera display name
            return d.displayName.Length > 0 ? d.displayName.Substring(0, 1) : "?";
        }

        // ── Export / Import (Phase B) ────────────────────────

        private void BuildExportImportSection(System.Action onChanged)
        {
            BuildSectionHeader("Eksport / import");

            var card = NewGO("ExportImport", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            float cardHeight = _importPanelOpen ? 130f : 50f;
            card.AddComponent<LayoutElement>().preferredHeight = cardHeight;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            // Buttons row
            var btnRow = NewGO("BtnRow", card.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hl = btnRow.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = UITheme.Spacing.Sm;
            hl.padding = new RectOffset(0, 0, 0, 0);
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = false;
            hl.childControlWidth = true; hl.childControlHeight = true;

            BuildActionButton(btnRow.transform, "Eksportuj (do schowka)",
                UITheme.WithAlpha(UITheme.Success, 0.25f), OnExportClicked);
            BuildActionButton(btnRow.transform,
                _importPanelOpen ? "✕ Anuluj import" : "Importuj (ze schowka)",
                _importPanelOpen ? UITheme.WithAlpha(UITheme.Warning, 0.25f) : UITheme.WithAlpha(UITheme.PrimaryAccent, 0.25f),
                () => { _importPanelOpen = !_importPanelOpen; if (_importPanelOpen) _importBuffer = ""; onChanged?.Invoke(); });

            // Import input panel (gdy open)
            if (_importPanelOpen)
            {
                var inputGO = NewGO("ImportInput", card.transform);
                inputGO.AddComponent<LayoutElement>().preferredHeight = 32f;
                var inputBg = inputGO.AddComponent<Image>();
                UITheme.ApplySurface(inputBg, UITheme.WithAlpha(UITheme.Border, 0.3f), UIShapePreset.Button);

                var inputField = inputGO.AddComponent<TMP_InputField>();
                inputField.targetGraphic = inputBg;
                var textGO = NewGO("Text", inputGO.transform);
                var textRT = textGO.GetComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(8, 4); textRT.offsetMax = new Vector2(-8, -4);
                var textTMP = textGO.AddComponent<TextMeshProUGUI>();
                textTMP.fontSize = 10; textTMP.color = TextPrimary;
                inputField.textComponent = textTMP;
                inputField.text = _importBuffer;
                inputField.characterLimit = 2000;
                inputField.onValueChanged.AddListener(v => _importBuffer = v);

                BuildActionButton(card.transform, "Zastosuj importowane malowanie",
                    UITheme.WithAlpha(UITheme.Success, 0.30f), () => OnImportApplyClicked(onChanged));
            }
        }

        private void BuildActionButton(Transform parent, string label, Color tint, System.Action onClick)
        {
            var go = NewGO($"Btn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 32f;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, tint, UIShapePreset.Button);
            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text = label;
            lbl.fontSize = 12; lbl.color = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false; FillRT(lbl.gameObject);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        private void OnExportClicked()
        {
            string serialized = PaintSerializer.Serialize(_currentPaint);
            if (string.IsNullOrEmpty(serialized))
            {
                Log.Warn("[PaintEditor] Eksport nie powiódł się");
                return;
            }
            GUIUtility.systemCopyBuffer = serialized;
            Log.Info($"[PaintEditor] Skopiowano malowanie do schowka ({serialized.Length} znaków)");
        }

        private void OnImportApplyClicked(System.Action onChanged)
        {
            if (string.IsNullOrEmpty(_importBuffer))
            {
                Log.Warn("[PaintEditor] Buffer pusty — wklej shareable string przed Zastosuj");
                return;
            }
            var imported = PaintSerializer.Deserialize(_importBuffer.Trim());
            if (imported == null)
            {
                Log.Warn("[PaintEditor] Nie udało się zdeserializować malowania — niewłaściwy format");
                return;
            }

            // Replace current paint w state'u (zachowując segment count)
            int targetSegCount = _currentPaint.segments.Count;
            _currentPaint.segments.Clear();
            for (int i = 0; i < targetSegCount; i++)
            {
                if (i < imported.segments.Count)
                    _currentPaint.segments.Add(imported.segments[i]);
                else
                    _currentPaint.segments.Add(new SegmentPaint { segmentIndex = i, baseColor = "#FFFFFF" });
            }

            _importPanelOpen = false;
            _importBuffer = "";
            Log.Info($"[PaintEditor] Zastosowano importowane malowanie ({imported.segments.Count} segmentów)");
            OnPaintEdited(onChanged);
        }

        // ── Helpers ──────────────────────────────────────────

        private void RefreshPaintPreview()
        {
            _paintPreview?.ApplyPaint(_currentPaint);
            // RawImage automatycznie pokazuje aktualizowaną RT
        }

        /// <summary>
        /// M-FC-7c: Po edycji paint state — synchronizuj wszystkie segmenty (AllSegments mode),
        /// odśwież 3D preview, wywołaj parent callback.
        /// </summary>
        private void OnPaintEdited(System.Action onChanged)
        {
            if (_currentPaint.applyMode == "AllSegments") PropagateSegment0ToAll();
            RefreshPaintPreview();
            onChanged?.Invoke();
        }

        private void BuildSimpleSlider(Transform parent, float currentVal, float minVal, float maxVal, System.Action<float> onChanged)
        {
            var sliderGO = NewGO("SliderRoot", parent);
            var sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = Vector2.zero; sliderRT.anchorMax = Vector2.one;
            sliderRT.offsetMin = Vector2.zero; sliderRT.offsetMax = Vector2.zero;

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = minVal; slider.maxValue = maxVal;
            slider.value = Mathf.Clamp(currentVal, minVal, maxVal);

            var bg = NewGO("BG", sliderGO.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.4f); bgRT.anchorMax = new Vector2(1, 0.6f);
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = UITheme.WithAlpha(UITheme.Border, 0.5f);

            var fillArea = NewGO("FillArea", sliderGO.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0, 0.4f); faRT.anchorMax = new Vector2(1, 0.6f);
            faRT.offsetMin = Vector2.zero; faRT.offsetMax = Vector2.zero;

            var fill = NewGO("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = UITheme.PrimaryAccent;
            slider.fillRect = fillRT;

            var hsa = NewGO("HandleSlideArea", sliderGO.transform);
            var hsaRT = hsa.GetComponent<RectTransform>();
            hsaRT.anchorMin = Vector2.zero; hsaRT.anchorMax = Vector2.one;
            hsaRT.offsetMin = new Vector2(8, 0); hsaRT.offsetMax = new Vector2(-8, 0);

            var handle = NewGO("Handle", hsa.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(14, 18);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = UITheme.PrimaryAccent;
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        }
    }
}
