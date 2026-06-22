using System.Collections.Generic;
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
    /// M-FC-2: Konfigurator wagonu — partial FleetPanelUI. Wybór: pudło (24.5/26.4m)
    /// + wózki (klockowy/tarczowy/tarczowy+szynowy) + drzwi (typ + liczba par).
    /// Generuje <see cref="VehicleConfiguration"/> i dodaje do koszyka jako CartItem.
    ///
    /// W M-FC-2 wnętrze wagonu (interiorMix) jest placeholder'em — heurystyka 100%
    /// 2kl-otwarty z liczbą miejsc proporcjonalną do długości pudła. M-FC-5 doda
    /// pełną edycję miksu stref siedzeń.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── State ────────────────────────────────────────────

        private class WagonConfigState
        {
            public string bodyTypeId;
            public string bogieTypeId;
            public DoorType doorType = DoorType.SwingFolding;
            public int doorPairs = 2;
            public DoorPlacement doorPlacement = DoorPlacement.AtEnds;
            // M-FC-5: miks stref siedzeń wzdłuż długości pudła
            public List<SeatZoneSlot> interiorMix = new();
            // M-FC-7: paint definition (1 segment dla wagonu)
            public PaintDefinition paint = PaintSerializer.CreateDefault(1, "#FAFAFA");
        }

        private bool _isWagonConfiguratorActive;
        private WagonConfigState _wagonState;
        private GameObject _wagonListItemGO;

        // UI references — labels do live update
        private TextMeshProUGUI _wagonPriceLbl;
        private TextMeshProUGUI _wagonSeatsLbl;
        private TextMeshProUGUI _wagonVmaxLbl;
        private TextMeshProUGUI _wagonMassLbl;

        // ── Lewa lista — pseudo-entry "Wagon konfigurowalny" ──

        /// <summary>Build entry "Wagon konfigurowalny" w lewej liście configurator'a, jako pierwszy item.</summary>
        private void BuildWagonListItem(Transform parent)
        {
            var row = NewGO("WagonConfigurable", parent);
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
            UITheme.ApplySurface(thumbImage, GetThumbnailColor(FleetVehicleType.PassengerCar), UIShapePreset.Button);
            var tLE = thumbGO.AddComponent<LayoutElement>();
            tLE.preferredWidth = 60f; tLE.preferredHeight = 48f;
            var tLbl = MakeTMP("Lbl", thumbGO.transform);
            tLbl.text = GetTypeShortLabel(FleetVehicleType.PassengerCar);
            tLbl.fontSize = 10; tLbl.color = TextPrimary;
            tLbl.alignment = TextAlignmentOptions.Center;
            tLbl.raycastTarget = false; FillRT(tLbl.gameObject);

            var infoLbl = MakeTMP("Info", row.transform);
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            infoLbl.text = $"<b>Wagon konfigurowalny</b>\n<size=11><color=#{mutedHex}>Pudło + wózki + drzwi</color></size>";
            infoLbl.fontSize = 15; infoLbl.richText = true;
            infoLbl.color = TextPrimary; infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnWagonConfiguratorSelected);
            row.AddComponent<HoverImageColor>().Init(rowImg, RowBg, RowHover);

            _wagonListItemGO = row;
        }

        private void OnWagonConfiguratorSelected()
        {
            _isWagonConfiguratorActive = true;
            _isFamilyConfiguratorActive = false;
            _selectedFamily = null;
            _configQuantity = 1;
            SetConfigQuantityWidgetsVisible(true);

            if (_wagonState == null) InitWagonStateDefaults();

            // Clear right panel, build wagon UI
            _configRightContent = _configRightContentRoot;
            foreach (Transform ch in _configRightContent) Destroy(ch.gameObject);
            BuildWagonConfigPanel();
            RecalculateWagonPrice();
        }

        private void InitWagonStateDefaults()
        {
            _wagonState = new WagonConfigState
            {
                bodyTypeId = FleetCatalog.WagonBodies.Count > 0 ? FleetCatalog.WagonBodies[0].id : "",
                bogieTypeId = FleetCatalog.WagonBogies.Count > 0 ? FleetCatalog.WagonBogies[0].id : "",
                doorType = DoorType.SwingFolding,
                doorPairs = 2,
                doorPlacement = DoorPlacement.AtEnds,
                interiorMix = new List<SeatZoneSlot>
                {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.SecondClassOpen }
                },
                paint = PaintSerializer.CreateDefault(1, "#FAFAFA")
            };
        }

        // ── Right panel — wagon configurator UI ──────────────

        private void BuildWagonConfigPanel()
        {
            if (_wagonState == null) InitWagonStateDefaults();

            // Header card
            var headerCard = NewGO("WagonHeader", _configRightContent);
            var headerImg = headerCard.AddComponent<Image>();
            UITheme.ApplySurface(headerImg, TopBarBg, UIShapePreset.Panel);
            headerCard.AddComponent<LayoutElement>().preferredHeight = 96f;
            var headerVL = headerCard.AddComponent<VerticalLayoutGroup>();
            headerVL.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            headerVL.spacing = UITheme.Spacing.Xs;
            headerVL.childForceExpandWidth = true; headerVL.childForceExpandHeight = false;
            headerVL.childControlWidth = true; headerVL.childControlHeight = true;

            var eyebrowLbl = MakeTMP("Eyebrow", headerCard.transform);
            eyebrowLbl.text = "KONFIGURATOR WAGONU";
            eyebrowLbl.fontSize = 9;
            eyebrowLbl.fontStyle = FontStyles.Bold;
            eyebrowLbl.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f);
            eyebrowLbl.raycastTarget = false;

            var titleLbl = MakeTMP("Title", headerCard.transform);
            titleLbl.text = "<b>Wagon konfigurowalny</b>";
            titleLbl.fontSize = 22; titleLbl.color = TextPrimary;
            titleLbl.richText = true; titleLbl.raycastTarget = false;

            var subtitleLbl = MakeTMP("Sub", headerCard.transform);
            subtitleLbl.text = "Wybierz pudło, wózki, drzwi. Wnętrze i malowanie w kolejnych etapach.";
            subtitleLbl.fontSize = 12; subtitleLbl.color = TextMuted;
            subtitleLbl.raycastTarget = false;

            // Section: Pudło
            BuildSectionHeader("Pudło");
            BuildBodyChoice();

            BuildSeparator();

            // Section: Wózki
            BuildSectionHeader("Wózki (para)");
            BuildBogieChoice();

            BuildSeparator();

            // Section: Drzwi
            BuildSectionHeader("Drzwi");
            BuildDoorTypeChoice();
            BuildDoorPairsChoice();

            BuildSeparator();

            // Section: Podsumowanie (live preview)
            BuildSectionHeader("Podsumowanie");
            BuildSummaryRow();

            BuildSeparator();

            // M-FC-5: Wnętrze (miks stref siedzeń wzdłuż długości pudła)
            BuildSectionHeader("Wnętrze");
            var bodyDef = FleetCatalog.FindWagonBody(_wagonState.bodyTypeId);
            float bodyLength = bodyDef?.lengthM ?? 24.5f;
            BuildInteriorMixSection(_wagonState.interiorMix, bodyLength, new List<string>(), OnWagonConfigChanged);

            BuildSeparator();

            // M-FC-7: Paint editor (1 segment dla wagonu)
            BuildSectionHeader("Malowanie");
            BuildPaintEditorSection(_wagonState.paint, segmentCount: 1, totalLengthM: bodyLength, OnWagonConfigChanged);

            // M-FC-2: force layout rebuild żeby ScrollRect widział poprawną wysokość content'a od razu
            var rt = _configRightContent as RectTransform ?? _configRightContent.GetComponent<RectTransform>();
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        private void BuildSectionHeader(string text)
        {
            var card = NewGO($"Hdr_{text}", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, UITheme.WithAlpha(TopBarBg, 0.9f), UIShapePreset.Pill);
            card.AddComponent<LayoutElement>().preferredHeight = 44f;

            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            vl.spacing = 0f;
            vl.childAlignment = TextAnchor.MiddleLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var overline = MakeTMP("Overline", card.transform);
            overline.text = "SEKCJA";
            overline.fontSize = 9;
            overline.fontStyle = FontStyles.Bold;
            overline.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.9f);
            overline.raycastTarget = false;
            overline.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            var hdr = MakeTMP("Title", card.transform);
            hdr.text = $"<b>{text}</b>";
            hdr.fontSize = 15;
            hdr.color = TextPrimary;
            hdr.richText = true;
            hdr.raycastTarget = false;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        private void BuildSeparator()
        {
            var wrap = NewGO("Sep", _configRightContent);
            wrap.AddComponent<LayoutElement>().preferredHeight = 16f;

            var line = NewGO("Line", wrap.transform);
            var lineRT = line.GetComponent<RectTransform>();
            lineRT.anchorMin = new Vector2(0f, 0.5f);
            lineRT.anchorMax = new Vector2(1f, 0.5f);
            lineRT.offsetMin = new Vector2(12f, -0.5f);
            lineRT.offsetMax = new Vector2(-12f, 0.5f);

            var lineImg = line.AddComponent<Image>();
            lineImg.color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.32f);
        }

        private void BuildBodyChoice()
        {
            var card = NewGO("BodyCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = FleetCatalog.WagonBodies.Count * 64f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            foreach (var body in FleetCatalog.WagonBodies)
                BuildOptionRow<WagonBodyDef>(card.transform, body, body.id, body.displayName, body.description, body.basePrice,
                    isSelected: _wagonState.bodyTypeId == body.id,
                    onClick: () => { _wagonState.bodyTypeId = body.id; OnWagonConfigChanged(); });
        }

        private void BuildBogieChoice()
        {
            var card = NewGO("BogieCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = FleetCatalog.WagonBogies.Count * 64f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            foreach (var bogie in FleetCatalog.WagonBogies)
                BuildOptionRow<WagonBogieDef>(card.transform, bogie, bogie.id, bogie.displayName, bogie.description, bogie.basePricePair,
                    isSelected: _wagonState.bogieTypeId == bogie.id,
                    onClick: () => { _wagonState.bogieTypeId = bogie.id; OnWagonConfigChanged(); });
        }

        private void BuildDoorTypeChoice()
        {
            var card = NewGO("DoorTypeCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 2 * 56f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            BuildToggleRow(card.transform, "Skrzydłowo-łamane",
                "Klasyczne PKP — wagony Bdhpumn, B11. Retro look.",
                isSelected: _wagonState.doorType == DoorType.SwingFolding,
                onClick: () => { _wagonState.doorType = DoorType.SwingFolding; OnWagonConfigChanged(); });

            BuildToggleRow(card.transform, "Odskokowo-przesuwne",
                "Nowoczesne — wagony PKP IC, FLIRT. Cicha praca, łatwy dostęp.",
                isSelected: _wagonState.doorType == DoorType.SlidingPlugDoor,
                onClick: () => { _wagonState.doorType = DoorType.SlidingPlugDoor; OnWagonConfigChanged(); });
        }

        private void BuildDoorPairsChoice()
        {
            var card = NewGO("DoorPairsCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 2 * 56f + 12f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            BuildToggleRow(card.transform, "2 pary po końcach (default)",
                "Standard wagonu pasażerskiego — szybkie wsiadanie z obu stron.",
                isSelected: _wagonState.doorPairs == 2 && _wagonState.doorPlacement == DoorPlacement.AtEnds,
                onClick: () => {
                    _wagonState.doorPairs = 2;
                    _wagonState.doorPlacement = DoorPlacement.AtEnds;
                    OnWagonConfigChanged();
                });

            BuildToggleRow(card.transform, "1 para w środku (zaawansowane)",
                "Niskie pojemności, klasyczne wagony międzynarodowe lub bagażowe.",
                isSelected: _wagonState.doorPairs == 1,
                onClick: () => {
                    _wagonState.doorPairs = 1;
                    _wagonState.doorPlacement = DoorPlacement.OneAtMiddle;
                    OnWagonConfigChanged();
                });
        }

        private void BuildSummaryRow()
        {
            var card = NewGO("SummaryCard", _configRightContent);
            var cardImg = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImg, TopBarBg, UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 132f;
            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            vl.spacing = UITheme.Spacing.Sm;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var note = MakeTMP("Note", card.transform);
            note.text = "Biezacy wariant po lewej stronie zbiera najwazniejsze parametry robocze i cene jednostkowa.";
            note.fontSize = 11;
            note.color = TextMuted;
            note.textWrappingMode = TextWrappingModes.Normal;
            note.raycastTarget = false;
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _wagonVmaxLbl = MakeTMP("Vmax", card.transform);
            _wagonVmaxLbl.text = "Vmax: – km/h";
            _wagonVmaxLbl.fontSize = 14; _wagonVmaxLbl.color = TextPrimary;
            _wagonVmaxLbl.raycastTarget = false;

            _wagonSeatsLbl = MakeTMP("Seats", card.transform);
            _wagonSeatsLbl.text = "Miejsca: –";
            _wagonSeatsLbl.fontSize = 14; _wagonSeatsLbl.color = TextPrimary;
            _wagonSeatsLbl.raycastTarget = false;

            _wagonMassLbl = MakeTMP("Mass", card.transform);
            _wagonMassLbl.text = "Masa pusta: – t";
            _wagonMassLbl.fontSize = 14; _wagonMassLbl.color = TextPrimary;
            _wagonMassLbl.raycastTarget = false;

            _wagonPriceLbl = MakeTMP("Price", card.transform);
            _wagonPriceLbl.text = "Cena: – zł";
            _wagonPriceLbl.fontSize = 16; _wagonPriceLbl.fontStyle = FontStyles.Bold;
            _wagonPriceLbl.color = PriceColor;
            _wagonPriceLbl.raycastTarget = false;
        }

        // Generic option row (selected = highlight, click = onClick).
        // T jest unused — pattern dla type-safety, wskazuje że row powiązany z konkretnym typem POCO.
        private void BuildOptionRow<T>(Transform parent, T tag, string id, string title, string description, long price, bool isSelected, System.Action onClick)
        {
            var row = NewGO($"Opt_{id}", parent);
            var rowImg = row.AddComponent<Image>();
            var normalColor = isSelected ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.24f) : RowBg;
            var hoverColor = isSelected ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.34f) : RowHover;
            UITheme.ApplySurface(rowImg, normalColor, UIShapePreset.Button);
            row.AddComponent<LayoutElement>().preferredHeight = 64f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            var info = MakeTMP("Info", row.transform);
            info.text = $"<b>{title}</b>\n<size=11><color=#{mutedHex}>{description}</color></size>";
            info.fontSize = 13; info.richText = true;
            info.color = TextPrimary; info.raycastTarget = false;
            info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (isSelected)
                BuildSelectionBadge(row.transform, "Wybrane");

            var priceLbl = MakeTMP("Price", row.transform);
            priceLbl.text = $"+{price / 1_000_000.0:0.0}M zł";
            priceLbl.fontSize = 13; priceLbl.color = PriceColor;
            priceLbl.alignment = TextAlignmentOptions.Right;
            priceLbl.raycastTarget = false;
            priceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            row.AddComponent<HoverImageColor>().Init(rowImg, normalColor, hoverColor);
        }

        private void BuildToggleRow(Transform parent, string title, string description, bool isSelected, System.Action onClick)
        {
            var row = NewGO($"Toggle_{title}", parent);
            var rowImg = row.AddComponent<Image>();
            var normalColor = isSelected ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.24f) : RowBg;
            var hoverColor = isSelected ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.34f) : RowHover;
            UITheme.ApplySurface(rowImg, normalColor, UIShapePreset.Button);
            row.AddComponent<LayoutElement>().preferredHeight = 56f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            var info = MakeTMP("Info", row.transform);
            info.text = $"<b>{title}</b>\n<size=11><color=#{mutedHex}>{description}</color></size>";
            info.fontSize = 13; info.richText = true;
            info.color = TextPrimary; info.raycastTarget = false;
            info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (isSelected)
                BuildSelectionBadge(row.transform, "Aktywne");

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            row.AddComponent<HoverImageColor>().Init(rowImg, normalColor, hoverColor);
        }

        private void BuildSelectionBadge(Transform parent, string label)
        {
            var badge = NewGO($"Badge_{label}", parent);
            var badgeImg = badge.AddComponent<Image>();
            UITheme.ApplySurface(badgeImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.28f), UIShapePreset.Pill);
            var badgeLE = badge.AddComponent<LayoutElement>();
            badgeLE.preferredWidth = 64f;
            badgeLE.preferredHeight = 24f;

            var badgeLbl = MakeTMP("Lbl", badge.transform);
            badgeLbl.text = label;
            badgeLbl.fontSize = 9;
            badgeLbl.fontStyle = FontStyles.Bold;
            badgeLbl.color = TextPrimary;
            badgeLbl.alignment = TextAlignmentOptions.Center;
            badgeLbl.raycastTarget = false;
            FillRT(badgeLbl.gameObject);
        }

        // ── Recalculation + cart integration ─────────────────

        private void OnWagonConfigChanged()
        {
            // Rebuild right panel (selection state changed → highlights need refresh)
            _configRightContent = _configRightContentRoot;
            foreach (Transform ch in _configRightContent) Destroy(ch.gameObject);
            BuildWagonConfigPanel();
            RecalculateWagonPrice();
        }

        private void RecalculateWagonPrice()
        {
            if (_wagonState == null) return;
            if (_wagonPriceLbl == null) return; // UI not yet built

            var bodyDef = FleetCatalog.FindWagonBody(_wagonState.bodyTypeId);
            var bogieDef = FleetCatalog.FindWagonBogie(_wagonState.bogieTypeId);
            if (bodyDef == null || bogieDef == null) return;

            int vMax = Mathf.Min(bodyDef.maxSpeedKmhCap, bogieDef.maxSpeedKmh);
            int seats = InteriorMixCalculator.CalculateSeats(_wagonState.interiorMix, bodyDef.lengthM);
            int comfortClass = InteriorMixCalculator.CalculateComfortClass(_wagonState.interiorMix, null);
            float emptyMass = bodyDef.emptyMassTons + bogieDef.emptyMassTonsPair;
            long unitPrice = bodyDef.basePrice + bogieDef.basePricePair;

            _wagonVmaxLbl.text = $"Vmax: {vMax} km/h <size=11><color=#{ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText)}>(min z pudła i wózków)</color></size>";
            _wagonSeatsLbl.text = $"Miejsca: {seats} <size=11><color=#{ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText)}>(klasa komfortu: {comfortClass} ★)</color></size>";
            _wagonMassLbl.text = $"Masa pusta: {emptyMass:0.0} t";
            _wagonPriceLbl.text = $"Cena: {unitPrice:N0} zł";

            // Update bottom bar price
            long totalPrice = unitPrice * _configQuantity;
            if (_configTotalPriceLbl != null)
                _configTotalPriceLbl.text = $"{totalPrice:N0} zł";
            if (_configTimeLbl != null)
                _configTimeLbl.text = "Produkcja: ~30 dni";
        }

        /// <summary>M-FC-2: Generuje VehicleConfiguration z bieżącego wagon state i dodaje do koszyka.</summary>
        private void OnWagonAddToCart()
        {
            if (_wagonState == null) return;
            var bodyDef = FleetCatalog.FindWagonBody(_wagonState.bodyTypeId);
            var bogieDef = FleetCatalog.FindWagonBogie(_wagonState.bogieTypeId);
            if (bodyDef == null || bogieDef == null) return;

            // M-FC-5: użyj interiorMix calculator do wyliczenia miejsc i klasy komfortu
            int seats = InteriorMixCalculator.CalculateSeats(_wagonState.interiorMix, bodyDef.lengthM);
            int comfortClass = InteriorMixCalculator.CalculateComfortClass(_wagonState.interiorMix, null);

            var cfg = new VehicleConfiguration
            {
                familyId = "Coach",
                variantKey = $"Coach|{bodyDef.id}|{bogieDef.id}",
                bodyTypeId = bodyDef.id,
                bogieTypeId = bogieDef.id,
                doorConfig = new DoorConfig
                {
                    type = _wagonState.doorType,
                    pairsPerSegment = _wagonState.doorPairs,
                    placement = _wagonState.doorPlacement
                },
                interiorMix = CloneSeatZoneSlots(_wagonState.interiorMix),
                pantographConfig = new PantographConfig(), // unused for wagon
                safetySystemsSelected = new List<string> { "CA" },
                etcsL2 = false,
                gsmR = false,
                comfortFeaturesSelected = new List<string>(),
                // M-FC-7: paint editor — clone state.paint do CartItem
                paint = ClonePaintDefinition(_wagonState.paint),
                calculatedPrice = bodyDef.basePrice + bogieDef.basePricePair,
                calculatedSeats = seats,
                calculatedComfortClass = comfortClass
            };

            // Generate stable configId — deterministic hash z definicji (do dedup'u w koszyku)
            cfg.configId = $"Coach|{bodyDef.id}|{bogieDef.id}|{_wagonState.doorType}|{_wagonState.doorPairs}";

            long unitPrice = cfg.calculatedPrice;

            var item = new CartItem
            {
                cartId = FleetService.NextCartId++,
                isNewVehicle = true,
                vehicleConfiguration = cfg,
                quantity = _configQuantity,
                deliveryMode = CartDeliveryMode.DeliverToDepot,
                deliveryDepotName = "Zajezdnia Mokotow",
                deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE * _configQuantity,
                unitPrice = unitPrice
            };

            FleetService.AddToCart(item);
            UpdateCartBadge();
            Log.Info($"[Cart] Dodano: {item.DisplayName}, cena: {item.TotalPrice:N0} zł (koszyk: {_cart.Count} pozycji)");
        }
    }
}
