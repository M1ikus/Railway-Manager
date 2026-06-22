using System.Collections.Generic;
using System.Linq;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// M-FC-3: Konfigurator rodziny pojazdów (FLIRT, SA, EU160 Griffin, ...) — partial FleetPanelUI.
    /// Lewa lista zawiera Family entries (zamiast płaskich SKU); klik → prawy panel
    /// pokazuje matrix wariantów (członów × voltage) + przeznaczenie + drzwi + pantografy
    /// + comfort features. Generuje VehicleConfiguration z familyId+variantKey.
    ///
    /// W M-FC-3 obsługuje EMU/DMU (FLIRT/SA) i loco (EU160 — single variant).
    /// Multi-system + ETCS dla loco rozwijane w M-FC-4. Wnętrze zespołów (interiorMix)
    /// na razie default'owe — pełna edycja w M-FC-5.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── State ────────────────────────────────────────────

        private class FamilyConfigState
        {
            public int memberCount;
            public string voltageConfigId;
            public string purpose;             // "longDistance" / "regional" / "agglomeration"
            public DoorType doorType;
            public int doorPairsPerSegment;
            public DoorPlacement doorPlacement;
            public PantographPlacement pantographPlacement = PantographPlacement.CabSegments;
            public List<string> safetySystemsSelected = new();
            public List<string> comfortFeaturesSelected = new();
            // M-FC-5: miks stref siedzeń (per człon) — używany dla EZT/SZT
            public List<SeatZoneSlot> interiorMix = new();
            // M-FC-7c: paint definition — N segmentów (memberCount) dla EZT/SZT, 1 dla loko
            public PaintDefinition paint = PaintSerializer.CreateDefault(1, "#FAFAFA");
        }

        private bool _isFamilyConfiguratorActive;
        private FleetFamily _selectedFamily;
        private FamilyConfigState _familyState;

        private TextMeshProUGUI _familyPriceLbl;
        private TextMeshProUGUI _familyVmaxLbl;
        private TextMeshProUGUI _familySeatsLbl;
        private TextMeshProUGUI _familyMassLbl;
        private TextMeshProUGUI _familyPantoLbl;

        // ── Lewa lista — Family entry ────────────────────────

        private void BuildFamilyListItem(Transform parent, FleetFamily family)
        {
            var row = NewGO($"Family_{family.familyId}", parent);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = 70f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            var thumbGO = NewGO("Thumb", row.transform);
            var thumbImage = thumbGO.AddComponent<Image>();
            UITheme.ApplySurface(thumbImage, GetThumbnailColor(family.type), UIShapePreset.Button);
            var tLE = thumbGO.AddComponent<LayoutElement>();
            tLE.preferredWidth = 60f; tLE.preferredHeight = 48f;
            var tLbl = MakeTMP("Lbl", thumbGO.transform);
            tLbl.text = GetTypeShortLabel(family.type); tLbl.fontSize = 10;
            tLbl.color = TextPrimary; tLbl.alignment = TextAlignmentOptions.Center;
            tLbl.raycastTarget = false; FillRT(tLbl.gameObject);

            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            int variantCount = family.variants?.Count ?? 0;
            var infoLbl = MakeTMP("Info", row.transform);
            infoLbl.text = $"<b>{family.displayName}</b>\n<size=11><color=#{mutedHex}>{family.manufacturer} | {variantCount} wariant{(variantCount == 1 ? "" : variantCount < 5 ? "y" : "ów")}</color></size>";
            infoLbl.fontSize = 15; infoLbl.richText = true;
            infoLbl.color = TextPrimary; infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Min price label (cheapest variant)
            long minPrice = family.variants != null && family.variants.Count > 0
                ? family.variants.Min(v => v.basePrice) : 0;
            var priceLbl = MakeTMP("Price", row.transform);
            priceLbl.text = LocalizationService.Get("fleet.currency.from_million_format",
                (minPrice / 1_000_000.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            priceLbl.fontSize = 13; priceLbl.color = PriceColor;
            priceLbl.alignment = TextAlignmentOptions.Right;
            priceLbl.raycastTarget = false;
            priceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.transition = Selectable.Transition.None;
            var capturedFamily = family;
            btn.onClick.AddListener(() => OnFamilySelected(capturedFamily));
            row.AddComponent<HoverImageColor>().Init(rowImg, RowBg, RowHover);
        }

        private void OnFamilySelected(FleetFamily family)
        {
            _isFamilyConfiguratorActive = true;
            _isWagonConfiguratorActive = false;
            _selectedFamily = family;
            _configQuantity = 1;
            SetConfigQuantityWidgetsVisible(true);

            InitFamilyStateDefaults(family);

            _configRightContent = _configRightContentRoot;
            foreach (Transform ch in _configRightContent) Destroy(ch.gameObject);
            BuildFamilyConfigPanel();
            RecalculateFamilyPrice();
        }

        private void InitFamilyStateDefaults(FleetFamily family)
        {
            // Pierwszy wariant = default
            var firstVariant = family.variants != null && family.variants.Count > 0 ? family.variants[0] : null;

            _familyState = new FamilyConfigState
            {
                memberCount = firstVariant?.memberCount ?? 1,
                voltageConfigId = firstVariant?.voltageConfigId ?? "",
                purpose = firstVariant?.defaultPurpose ?? "regional",
                doorType = DoorType.SlidingPlugDoor, // nowoczesne EZT zwykle wsuwane
                doorPairsPerSegment = DefaultDoorPairsForPurpose(firstVariant?.defaultPurpose ?? "regional"),
                doorPlacement = DoorPlacement.AtEnds,
                pantographPlacement = PantographPlacement.CabSegments,
                safetySystemsSelected = firstVariant?.defaultSafetySystems != null
                    ? new List<string>(firstVariant.defaultSafetySystems)
                    : new List<string>(),
                comfortFeaturesSelected = firstVariant?.defaultComfortFeatures != null
                    ? new List<string>(firstVariant.defaultComfortFeatures)
                    : new List<string>(),
                interiorMix = new List<SeatZoneSlot>
                {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.SecondClassOpen }
                },
                paint = PaintSerializer.CreateDefault(firstVariant?.memberCount ?? 1, "#FAFAFA")
            };
        }

        private static int DefaultDoorPairsForPurpose(string purpose) => purpose switch
        {
            "longDistance" => 1,
            "regional" => 2,
            "agglomeration" => 3,
            _ => 2
        };

        // ── Right panel UI ───────────────────────────────────

        private void BuildFamilyConfigPanel()
        {
            if (_selectedFamily == null) return;

            // Header
            BuildFamilyHeaderCard();

            // Variant selection (members × voltage)
            BuildSectionHeader("Wariant");
            BuildVariantSelectionSection();

            BuildSeparator();

            // Purpose (EMU/DMU only — wpływa na default drzwi)
            if (_selectedFamily.type == FleetVehicleType.EMU || _selectedFamily.type == FleetVehicleType.DMU)
            {
                BuildSectionHeader("Przeznaczenie");
                BuildPurposeSection();
                BuildSeparator();
            }

            // Doors (everyone except locos)
            if (_selectedFamily.type != FleetVehicleType.ElectricLocomotive
                && _selectedFamily.type != FleetVehicleType.DieselLocomotive)
            {
                BuildSectionHeader("Drzwi");
                BuildDoorsSection();
                BuildSeparator();
            }

            // Pantographs (electric only — EMU + ElectricLocomotive)
            if (_selectedFamily.type == FleetVehicleType.EMU
                || _selectedFamily.type == FleetVehicleType.ElectricLocomotive)
            {
                BuildSectionHeader("Pantografy");
                BuildPantographSection();
                BuildSeparator();
            }

            // Safety systems
            BuildSectionHeader("Systemy bezpieczeństwa");
            BuildSafetySystemsSection();

            BuildSeparator();

            // Comfort features
            if (_selectedFamily.type != FleetVehicleType.ElectricLocomotive
                && _selectedFamily.type != FleetVehicleType.DieselLocomotive)
            {
                BuildSectionHeader("Wyposażenie komfortu");
                BuildComfortFeaturesSection();
                BuildSeparator();
            }

            // M-FC-5: Wnętrze (per człon) — dla EZT/SZT, nie dla loko
            if (_selectedFamily.type == FleetVehicleType.EMU || _selectedFamily.type == FleetVehicleType.DMU)
            {
                BuildSectionHeader("Wnętrze (per człon)");
                var interiorVariant = ResolveCurrentVariant();
                float perSegmentLength = interiorVariant != null && interiorVariant.memberCount > 0
                    ? interiorVariant.lengthM / interiorVariant.memberCount
                    : 25f;
                BuildInteriorMixSection(_familyState.interiorMix, perSegmentLength,
                    _familyState.comfortFeaturesSelected, OnFamilyConfigChanged);
                BuildSeparator();
            }

            // M-FC-7c: Paint editor — N segmentów dla EZT/SZT, 1 dla loko
            BuildSectionHeader("Malowanie");
            var paintVariant = ResolveCurrentVariant();
            int paintSegmentCount = paintVariant?.memberCount ?? 1;
            float paintTotalLength = paintVariant?.lengthM ?? 20f;
            BuildPaintEditorSection(_familyState.paint, paintSegmentCount, paintTotalLength, OnFamilyConfigChanged);

            BuildSeparator();

            // Summary
            BuildSectionHeader("Podsumowanie");
            BuildFamilySummarySection();

            // Force layout rebuild
            var rt = _configRightContent as RectTransform ?? _configRightContent.GetComponent<RectTransform>();
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        private void BuildFamilyHeaderCard()
        {
            var card = NewGO("FamilyHeader", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Panel);
            card.AddComponent<LayoutElement>().preferredHeight = 118f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var eyebrowLbl = MakeTMP("Eyebrow", card.transform);
            eyebrowLbl.text = "RODZINA POJAZDOW";
            eyebrowLbl.fontSize = 9;
            eyebrowLbl.fontStyle = FontStyles.Bold;
            eyebrowLbl.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f);
            eyebrowLbl.raycastTarget = false;

            var titleLbl = MakeTMP("Title", card.transform);
            titleLbl.text = $"<b>{_selectedFamily.displayName}</b>";
            titleLbl.fontSize = 22; titleLbl.color = TextPrimary;
            titleLbl.richText = true; titleLbl.raycastTarget = false;

            var manuLbl = MakeTMP("Manu", card.transform);
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            manuLbl.text = $"<color=#{mutedHex}>{_selectedFamily.manufacturer}, {_selectedFamily.factoryLocation} | produkcja od {_selectedFamily.inProductionFromYear}</color>";
            manuLbl.fontSize = 12; manuLbl.color = TextMuted;
            manuLbl.richText = true; manuLbl.raycastTarget = false;

            if (!string.IsNullOrEmpty(_selectedFamily.description))
            {
                var descLbl = MakeTMP("Desc", card.transform);
                descLbl.text = _selectedFamily.description;
                descLbl.fontSize = 11; descLbl.color = TextMuted;
                descLbl.raycastTarget = false;
            }
        }

        private void BuildVariantSelectionSection()
        {
            // Member count choice (rows for each unique memberCount)
            var memberCounts = _selectedFamily.variants.Select(v => v.memberCount).Distinct().OrderBy(c => c).ToList();
            if (memberCounts.Count > 1)
            {
                var memCard = NewGO("MemberCard", _configRightContent);
                var memImg = memCard.AddComponent<Image>();
                UITheme.ApplySurface(memImg, TopBarBg, UIShapePreset.Inset);
                memCard.AddComponent<LayoutElement>().preferredHeight = memberCounts.Count * 56f + 12f;
                var vl = memCard.AddComponent<VerticalLayoutGroup>();
                vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
                vl.spacing = UITheme.Spacing.Xs;
                vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
                vl.childControlWidth = true; vl.childControlHeight = true;

                var memLbl = MakeTMP("Hdr", _configRightContent);
                memLbl.text = "<i>Liczba członów</i>";
                memLbl.fontSize = 12; memLbl.color = TextMuted;
                memLbl.richText = true; memLbl.raycastTarget = false;
                memLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

                foreach (var mc in memberCounts)
                {
                    var anyVariant = _selectedFamily.variants.First(v => v.memberCount == mc);
                    string title = $"{mc} człon{(mc == 1 ? "" : mc < 5 ? "y" : "ów")}";
                    string desc = $"{anyVariant.lengthM:0.0}m, {anyVariant.passengerSeatsBase} miejsc, {anyVariant.powerKw} kW";
                    int capturedMc = mc;
                    BuildToggleRow(memCard.transform, title, desc,
                        isSelected: _familyState.memberCount == mc,
                        onClick: () => {
                            _familyState.memberCount = capturedMc;
                            // Snap voltage to first available combination for this member count
                            var v = _selectedFamily.variants.FirstOrDefault(x => x.memberCount == capturedMc);
                            if (v != null) _familyState.voltageConfigId = v.voltageConfigId;
                            OnFamilyConfigChanged();
                        });
                }
            }

            // Voltage choice — opcje dostępne dla wybranego memberCount
            var voltagesForMember = _selectedFamily.variants
                .Where(v => v.memberCount == _familyState.memberCount)
                .Select(v => v.voltageConfigId).Distinct().ToList();
            if (voltagesForMember.Count > 1)
            {
                var voltLbl = MakeTMP("VoltHdr", _configRightContent);
                voltLbl.text = "<i>System napięcia</i>";
                voltLbl.fontSize = 12; voltLbl.color = TextMuted;
                voltLbl.richText = true; voltLbl.raycastTarget = false;
                voltLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

                var voltCard = NewGO("VoltCard", _configRightContent);
                var voltImg = voltCard.AddComponent<Image>();
                UITheme.ApplySurface(voltImg, TopBarBg, UIShapePreset.Inset);
                voltCard.AddComponent<LayoutElement>().preferredHeight = voltagesForMember.Count * 56f + 12f;
                var vl = voltCard.AddComponent<VerticalLayoutGroup>();
                vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
                vl.spacing = UITheme.Spacing.Xs;
                vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
                vl.childControlWidth = true; vl.childControlHeight = true;

                foreach (var voltId in voltagesForMember)
                {
                    var v = _selectedFamily.variants.First(x => x.memberCount == _familyState.memberCount && x.voltageConfigId == voltId);
                    string title = HumanReadableVoltage(voltId);
                    string desc = LocalizationService.Get("fleet.currency.million_format",
                        (v.basePrice / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    string capturedVolt = voltId;
                    BuildToggleRow(voltCard.transform, title, desc,
                        isSelected: _familyState.voltageConfigId == voltId,
                        onClick: () => { _familyState.voltageConfigId = capturedVolt; OnFamilyConfigChanged(); });
                }
            }
            else if (voltagesForMember.Count == 1)
            {
                _familyState.voltageConfigId = voltagesForMember[0];
            }
        }

        private void BuildPurposeSection()
        {
            var card = NewGO("PurposeCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 3 * 56f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            BuildToggleRow(card.transform, "Aglomeracyjny", "3 pary drzwi/człon — szybkie wsiadanie, krótkie trasy.",
                isSelected: _familyState.purpose == "agglomeration",
                onClick: () => {
                    _familyState.purpose = "agglomeration";
                    _familyState.doorPairsPerSegment = 3;
                    OnFamilyConfigChanged();
                });
            BuildToggleRow(card.transform, "Regionalny", "2 pary drzwi/człon — standard regionalny.",
                isSelected: _familyState.purpose == "regional",
                onClick: () => {
                    _familyState.purpose = "regional";
                    _familyState.doorPairsPerSegment = 2;
                    OnFamilyConfigChanged();
                });
            BuildToggleRow(card.transform, "Dalekobieżny", "1 para drzwi/człon — komfort, długie trasy.",
                isSelected: _familyState.purpose == "longDistance",
                onClick: () => {
                    _familyState.purpose = "longDistance";
                    _familyState.doorPairsPerSegment = 1;
                    OnFamilyConfigChanged();
                });
        }

        private void BuildDoorsSection()
        {
            var card = NewGO("DoorsCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 2 * 56f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            BuildToggleRow(card.transform, "Odskokowo-przesuwne",
                "Nowoczesne — wagony PKP IC, FLIRT. Cicha praca, łatwy dostęp.",
                isSelected: _familyState.doorType == DoorType.SlidingPlugDoor,
                onClick: () => { _familyState.doorType = DoorType.SlidingPlugDoor; OnFamilyConfigChanged(); });
            BuildToggleRow(card.transform, "Skrzydłowo-łamane",
                "Klasyczne PKP — retro look, wagony Bdhpumn, B11.",
                isSelected: _familyState.doorType == DoorType.SwingFolding,
                onClick: () => { _familyState.doorType = DoorType.SwingFolding; OnFamilyConfigChanged(); });

            // Doors-per-segment label (informational; controlled by purpose section)
            var infoLbl = MakeTMP("DoorPairsInfo", _configRightContent);
            infoLbl.text = $"<i>Liczba par drzwi/człon: {_familyState.doorPairsPerSegment} (z przeznaczenia)</i>";
            infoLbl.fontSize = 11; infoLbl.color = TextMuted;
            infoLbl.richText = true; infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        private void BuildPantographSection()
        {
            int autoCount = _familyState.memberCount >= 4 ? 2 : 1;

            var card = NewGO("PantoCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 2 * 56f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            BuildToggleRow(card.transform, "Standardowo (pudła kabinowe)",
                $"Auto: {autoCount} pantograf{(autoCount == 1 ? "" : "y")} na skrajnych członach.",
                isSelected: _familyState.pantographPlacement == PantographPlacement.CabSegments,
                onClick: () => { _familyState.pantographPlacement = PantographPlacement.CabSegments; OnFamilyConfigChanged(); });
            BuildToggleRow(card.transform, "Środkowe człony (zaawansowane)",
                "Mniej kołysania sieci przy v=200, mniej awarii pantografu.",
                isSelected: _familyState.pantographPlacement == PantographPlacement.MiddleSegments,
                onClick: () => { _familyState.pantographPlacement = PantographPlacement.MiddleSegments; OnFamilyConfigChanged(); });
        }

        private void BuildSafetySystemsSection()
        {
            var variant = ResolveCurrentVariant();
            if (variant == null || variant.defaultSafetySystems == null || variant.defaultSafetySystems.Count == 0) return;

            // M-FC-4: hint dla multi-system bez ETCS L2 (informacyjny, nie blokujący)
            bool isMultiSystem = !string.IsNullOrEmpty(_familyState.voltageConfigId)
                && _familyState.voltageConfigId.Contains("+");
            bool hasEtcsL2 = _familyState.safetySystemsSelected != null
                && _familyState.safetySystemsSelected.Contains("ETCS L2");
            if (isMultiSystem && !hasEtcsL2)
            {
                var hintGO = NewGO("EtcsHint", _configRightContent);
                var hintImg = hintGO.AddComponent<Image>();
                UITheme.ApplySurface(hintImg, UITheme.WithAlpha(UITheme.Warning, 0.18f), UIShapePreset.Inset);
                hintGO.AddComponent<LayoutElement>().preferredHeight = 42f;
                var hintHL = hintGO.AddComponent<HorizontalLayoutGroup>();
                hintHL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
                hintHL.spacing = UITheme.Spacing.Sm;
                hintHL.childAlignment = TextAnchor.MiddleLeft;
                hintHL.childControlWidth = true; hintHL.childControlHeight = true;
                hintHL.childForceExpandWidth = false; hintHL.childForceExpandHeight = false;

                var hintLbl = MakeTMP("HintLbl", hintGO.transform);
                hintLbl.text = "<i>ⓘ Wielosystemowa lokomotywa zwykle wymaga ETCS L2 dla ruchu po korytarzu TEN-T (Niemcy, Czechy, Słowacja).</i>";
                hintLbl.fontSize = 11; hintLbl.color = TextPrimary;
                hintLbl.richText = true; hintLbl.raycastTarget = false;
                hintLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            // Use union of all default safety systems across family variants — gives full pick list
            var allSafety = _selectedFamily.variants
                .SelectMany(v => v.defaultSafetySystems ?? new List<string>())
                .Distinct().ToList();

            var card = NewGO("SafetyCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = allSafety.Count * 36f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xxs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            foreach (var sys in allSafety)
            {
                string capturedSys = sys;
                BuildCheckboxRow(card.transform, sys,
                    isChecked: _familyState.safetySystemsSelected.Contains(sys),
                    onToggle: (newVal) => {
                        if (newVal && !_familyState.safetySystemsSelected.Contains(capturedSys))
                            _familyState.safetySystemsSelected.Add(capturedSys);
                        else if (!newVal)
                            _familyState.safetySystemsSelected.Remove(capturedSys);
                        OnFamilyConfigChanged();
                    });
            }
        }

        private void BuildComfortFeaturesSection()
        {
            var allComfort = _selectedFamily.variants
                .SelectMany(v => v.defaultComfortFeatures ?? new List<string>())
                .Distinct().ToList();

            if (allComfort.Count == 0) return;

            var card = NewGO("ComfortCard", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = allComfort.Count * 30f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xxs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            foreach (var feat in allComfort)
            {
                string captured = feat;
                BuildCheckboxRow(card.transform, feat,
                    isChecked: _familyState.comfortFeaturesSelected.Contains(feat),
                    onToggle: (newVal) => {
                        if (newVal && !_familyState.comfortFeaturesSelected.Contains(captured))
                            _familyState.comfortFeaturesSelected.Add(captured);
                        else if (!newVal)
                            _familyState.comfortFeaturesSelected.Remove(captured);
                        OnFamilyConfigChanged();
                    });
            }
        }

        private void BuildFamilySummarySection()
        {
            var card = NewGO("FamilySummary", _configRightContent);
            var img = card.AddComponent<Image>();
            UITheme.ApplySurface(img, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 158f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var note = MakeTMP("Note", card.transform);
            note.text = "To podsumowanie pokazuje aktywny wariant po wszystkich zmianach: wyborze czlonow, napiecia, wyposazenia i wnetrza.";
            note.fontSize = 11;
            note.color = TextMuted;
            note.textWrappingMode = TextWrappingModes.Normal;
            note.raycastTarget = false;
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            _familyVmaxLbl = MakeTMP("Vmax", card.transform);
            _familyVmaxLbl.text = "Vmax: – km/h";
            _familyVmaxLbl.fontSize = 14; _familyVmaxLbl.color = TextPrimary;
            _familyVmaxLbl.raycastTarget = false;

            _familySeatsLbl = MakeTMP("Seats", card.transform);
            _familySeatsLbl.text = "Miejsca: –";
            _familySeatsLbl.fontSize = 14; _familySeatsLbl.color = TextPrimary;
            _familySeatsLbl.raycastTarget = false;

            _familyMassLbl = MakeTMP("Mass", card.transform);
            _familyMassLbl.text = "Masa pusta: – t, moc: – kW";
            _familyMassLbl.fontSize = 14; _familyMassLbl.color = TextPrimary;
            _familyMassLbl.raycastTarget = false;

            _familyPantoLbl = MakeTMP("Panto", card.transform);
            _familyPantoLbl.text = "";
            _familyPantoLbl.fontSize = 11; _familyPantoLbl.color = TextMuted;
            _familyPantoLbl.raycastTarget = false;

            _familyPriceLbl = MakeTMP("Price", card.transform);
            _familyPriceLbl.text = "Cena: – zł";
            _familyPriceLbl.fontSize = 16; _familyPriceLbl.fontStyle = FontStyles.Bold;
            _familyPriceLbl.color = PriceColor;
            _familyPriceLbl.raycastTarget = false;
        }

        private void BuildCheckboxRow(Transform parent, string label, bool isChecked, System.Action<bool> onToggle)
        {
            var row = NewGO($"CB_{label}", parent);
            var rowImg = row.AddComponent<Image>();
            var normalColor = isChecked ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f) : UITheme.WithAlpha(RowBg, 0.78f);
            var hoverColor = isChecked ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.26f) : RowHover;
            UITheme.ApplySurface(rowImg, normalColor, UIShapePreset.Button);
            row.AddComponent<LayoutElement>().preferredHeight = 32f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            var box = NewGO("Box", row.transform);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = isChecked ? UITheme.PrimaryAccent : UITheme.WithAlpha(UITheme.Border, 0.6f);
            var boxLE = box.AddComponent<LayoutElement>();
            boxLE.preferredWidth = 18f; boxLE.preferredHeight = 18f;
            if (isChecked)
            {
                var check = MakeTMP("Check", box.transform);
                check.text = "✓"; check.fontSize = 14; check.color = TextPrimary;
                check.alignment = TextAlignmentOptions.Center;
                check.raycastTarget = false; FillRT(check.gameObject);
            }

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text = label;
            lbl.fontSize = 12; lbl.color = TextPrimary;
            lbl.raycastTarget = false;
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onToggle(!isChecked));
            row.AddComponent<HoverImageColor>().Init(rowImg, normalColor, hoverColor);
        }

        // ── Recalc + cart ────────────────────────────────────

        private void OnFamilyConfigChanged()
        {
            _configRightContent = _configRightContentRoot;
            foreach (Transform ch in _configRightContent) Destroy(ch.gameObject);
            BuildFamilyConfigPanel();
            RecalculateFamilyPrice();
        }

        private FleetVariantSpec ResolveCurrentVariant()
        {
            if (_selectedFamily == null || _familyState == null) return null;
            return _selectedFamily.variants?.FirstOrDefault(v =>
                v.memberCount == _familyState.memberCount && v.voltageConfigId == _familyState.voltageConfigId);
        }

        private void RecalculateFamilyPrice()
        {
            if (_familyPriceLbl == null) return;

            var v = ResolveCurrentVariant();
            if (v == null) return;

            int autoPantos = _familyState.memberCount >= 4 ? 2 : 1;

            _familyVmaxLbl.text = $"Vmax: {v.maxSpeedKmh} km/h";

            // M-FC-5: dla EZT/SZT seats z miksu × członów; dla loko/wagonu fall-back na variant
            if (_selectedFamily.type == FleetVehicleType.EMU || _selectedFamily.type == FleetVehicleType.DMU)
            {
                float perSegmentLength = v.memberCount > 0 ? v.lengthM / v.memberCount : 25f;
                int perSegmentSeats = InteriorMixCalculator.CalculateSeats(_familyState.interiorMix, perSegmentLength);
                int totalSeats = perSegmentSeats * v.memberCount;
                int comfortClass = InteriorMixCalculator.CalculateComfortClass(
                    _familyState.interiorMix, _familyState.comfortFeaturesSelected);
                _familySeatsLbl.text = $"Miejsca: {totalSeats} ({perSegmentSeats}/człon, klasa komfortu {comfortClass} ★)";
            }
            else
            {
                _familySeatsLbl.text = v.passengerSeatsBase > 0
                    ? $"Miejsca: {v.passengerSeatsBase}"
                    : "Miejsca: – (lokomotywa)";
            }

            _familyMassLbl.text = $"Masa pusta: {v.emptyMassTons:0.0} t, moc: {v.powerKw} kW";

            if (_selectedFamily.type == FleetVehicleType.EMU || _selectedFamily.type == FleetVehicleType.ElectricLocomotive)
                _familyPantoLbl.text = $"Pantografy: {autoPantos} ({(_familyState.pantographPlacement == PantographPlacement.CabSegments ? "kabinowe" : "środkowe")})";
            else
                _familyPantoLbl.text = "";

            long unitPrice = v.basePrice;
            _familyPriceLbl.text = $"Cena: {unitPrice:N0} zł";

            long totalPrice = unitPrice * _configQuantity;
            if (_configTotalPriceLbl != null)
                _configTotalPriceLbl.text = $"{totalPrice:N0} zł";
            if (_configTimeLbl != null)
            {
                int monthsBase = _selectedFamily.type switch
                {
                    FleetVehicleType.ElectricLocomotive => 6,
                    FleetVehicleType.DieselLocomotive => 6,
                    FleetVehicleType.EMU => 8,
                    FleetVehicleType.DMU => 4,
                    _ => 4
                };
                int totalMonths = monthsBase + (int)((_configQuantity - 1) * monthsBase * 0.6f);
                _configTimeLbl.text = $"Produkcja: ~{totalMonths} mies";
            }
        }

        private void OnFamilyAddToCart()
        {
            var variant = ResolveCurrentVariant();
            if (variant == null || _selectedFamily == null) return;

            // M-FC-5: dla EZT/SZT wylicz seats z miksu × członów; dla loko zostawiamy variant base
            int calcSeats = variant.passengerSeatsBase;
            int calcComfort = variant.comfortClassBase;
            if (_selectedFamily.type == FleetVehicleType.EMU || _selectedFamily.type == FleetVehicleType.DMU)
            {
                float perSegLen = variant.memberCount > 0 ? variant.lengthM / variant.memberCount : 25f;
                calcSeats = InteriorMixCalculator.CalculateSeats(_familyState.interiorMix, perSegLen) * variant.memberCount;
                calcComfort = InteriorMixCalculator.CalculateComfortClass(
                    _familyState.interiorMix, _familyState.comfortFeaturesSelected);
            }

            var cfg = new VehicleConfiguration
            {
                familyId = _selectedFamily.familyId,
                variantKey = $"{_selectedFamily.familyId}|{variant.memberCount}|{variant.voltageConfigId}",
                bodyTypeId = "",
                bogieTypeId = "",
                doorConfig = new DoorConfig
                {
                    type = _familyState.doorType,
                    pairsPerSegment = _familyState.doorPairsPerSegment,
                    placement = _familyState.doorPlacement
                },
                interiorMix = CloneSeatZoneSlots(_familyState.interiorMix),
                pantographConfig = new PantographConfig
                {
                    count = _familyState.memberCount >= 4 ? 2 : 1,
                    placement = _familyState.pantographPlacement
                },
                safetySystemsSelected = new List<string>(_familyState.safetySystemsSelected),
                etcsL2 = _familyState.safetySystemsSelected.Contains("ETCS L2"),
                gsmR = _familyState.safetySystemsSelected.Contains("GSM-R"),
                comfortFeaturesSelected = new List<string>(_familyState.comfortFeaturesSelected),
                paint = ClonePaintDefinition(_familyState.paint),
                calculatedPrice = variant.basePrice,
                calculatedSeats = calcSeats,
                calculatedComfortClass = calcComfort
            };
            cfg.configId = $"{_selectedFamily.familyId}|{variant.memberCount}|{variant.voltageConfigId}|{_familyState.purpose}|{_familyState.doorType}";

            var item = new CartItem
            {
                cartId = FleetService.NextCartId++,
                isNewVehicle = true,
                vehicleConfiguration = cfg,
                quantity = _configQuantity,
                deliveryMode = CartDeliveryMode.DeliverToDepot,
                deliveryDepotName = "Zajezdnia Mokotow",
                deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE * _configQuantity,
                unitPrice = variant.basePrice
            };

            FleetService.AddToCart(item);
            UpdateCartBadge();
            Log.Info($"[Cart] Dodano: {_selectedFamily.displayName} ({variant.variantLabel}) x{_configQuantity}, cena: {item.TotalPrice:N0} zł");
        }

        // ── Helpers ──────────────────────────────────────────

        private static string HumanReadableVoltage(string voltageConfigId) => voltageConfigId switch
        {
            "diesel" => "Spalinowy",
            "passive" => "Pasywny (wagon)",
            "3kV" => "3kV DC (Polska)",
            "3kV+25kV" => "3kV DC + 25kV AC (PL + transgraniczny)",
            "3kV+15kV+25kV" => "3kV + 15kV + 25kV (multi-system)",
            "3kV+15kV+25kV+1.5kV" => "4-systemowy (wszystkie europejskie)",
            _ => voltageConfigId
        };
    }
}
