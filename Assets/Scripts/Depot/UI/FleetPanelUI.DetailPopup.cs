using System.Collections.Generic;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using RailwayManager.Core;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI â€” popupy szczegolow pojazdu (market + owned).
    /// Zawiera: ShowMarketDetailPopup, BuildPopupCloseButton (owned detal -> FleetPanelUI.DetailWindow),
    /// CloseMarketDetailPopup, RefreshCurrentDetailPopup, helpery PopupSectionTitle/PopupInfoLine,
    /// BuildSeatBreakdownSection oraz rozsuwana sekcja przegladow (BuildInspectionCollapsible).
    /// </summary>
    public partial class FleetPanelUI
    {
        // â”€â”€ ROZSUWANA SEKCJA PRZEGLADOW W POPUPIE â€” stan â”€â”€

        // â”€â”€ MARKET DETAIL POPUP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void ShowMarketDetailPopup(FleetMarketVehicle v)
        {
            if (_marketDetailPopupGO != null)
                Destroy(_marketDetailPopupGO);

            _currentMarketDetailVehicle = v;
            _currentOwnedDetailVehicle = null;

            bool isPassenger = PassengerTypes.Contains(v.type);

            // â”€â”€ Overlay â”€â”€
            _marketDetailPopupGO = NewGO("MarketDetailPopup", _root.transform);
            var rt = _marketDetailPopupGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var dimImg = _marketDetailPopupGO.AddComponent<Image>();
            dimImg.color = UITheme.WithAlpha(UITheme.AppBackground, 0.78f);
            var dimBtn = _marketDetailPopupGO.AddComponent<Button>();
            dimBtn.targetGraphic = dimImg;
            dimBtn.onClick.AddListener(CloseMarketDetailPopup);

            // â”€â”€ Card (centered, fixed proportions) â”€â”€
            var card = NewGO("Card", _marketDetailPopupGO.transform);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.1f, 0.05f);
            cardRT.anchorMax = new Vector2(0.9f, 0.95f);
            cardRT.offsetMin = Vector2.zero; cardRT.offsetMax = Vector2.zero;
            var cardImage = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImage, PanelBg, UIShapePreset.PanelLarge);
            card.AddComponent<Button>().onClick.AddListener(() => { }); // block dim click

            // â”€â”€ Card layout: Header (top) | Content (middle, stretch) | Bottom (buy bar) â”€â”€
            // Using anchored children instead of VLG to avoid scroll

            // === HEADER (top 60px) ===
            var header = NewGO("Header", card.transform);
            var hRT = header.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1); hRT.anchorMax = Vector2.one;
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.offsetMin = new Vector2(24, -60); hRT.offsetMax = new Vector2(-24, 0);

            var headerHL = header.AddComponent<HorizontalLayoutGroup>();
            headerHL.spacing = UITheme.Spacing.Lg; headerHL.padding = UITheme.Padding(0f, UITheme.Spacing.Md, 0f, 0f);
            headerHL.childAlignment = TextAnchor.MiddleLeft;
            headerHL.childControlWidth = false; headerHL.childControlHeight = false;

            // Thumbnail in header
            var thumbGO = NewGO("Thumb", header.transform);
            var thumbImage = thumbGO.AddComponent<Image>();
            UITheme.ApplySurface(thumbImage, GetThumbnailColor(v.type), UIShapePreset.Button);
            var tLE = thumbGO.AddComponent<LayoutElement>();
            tLE.preferredWidth = 80f; tLE.preferredHeight = 40f;
            var thumbTxt = MakeTMP("Lbl", thumbGO.transform);
            thumbTxt.text = GetTypeShortLabel(v.type); thumbTxt.fontSize = 13;
            thumbTxt.color = TextPrimary; thumbTxt.alignment = TextAlignmentOptions.Center;
            thumbTxt.raycastTarget = false; FillRT(thumbTxt.gameObject);

            // Name
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            var nameLbl = MakeTMP("Name", header.transform);
            nameLbl.text = $"<b><size=26>{v.series}</size></b>  <size=16><color=#{mutedHex}>{v.number}</color></size>";
            nameLbl.fontSize = 26; nameLbl.richText = true; nameLbl.color = TextPrimary;
            nameLbl.raycastTarget = false;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 300f;

            // Spacer
            var hSpacer = NewGO("Sp", header.transform);
            hSpacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Paint
            var paintLbl = MakeTMP("Paint", header.transform);
            paintLbl.text = v.paintScheme; paintLbl.fontSize = 14;
            paintLbl.color = TextMuted; paintLbl.alignment = TextAlignmentOptions.Right;
            paintLbl.raycastTarget = false;
            paintLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 250f;

            // Close X button (top-right corner of card)
            BuildPopupCloseButton(card.transform);

            // === BOTTOM BAR (bottom 60px) ===
            var bottom = NewGO("Bottom", card.transform);
            var bRT = bottom.GetComponent<RectTransform>();
            bRT.anchorMin = Vector2.zero; bRT.anchorMax = new Vector2(1, 0);
            bRT.pivot = new Vector2(0.5f, 0);
            bRT.offsetMin = new Vector2(24, 0); bRT.offsetMax = new Vector2(-24, 60);
            var bottomImage = bottom.AddComponent<Image>();
            UITheme.ApplySurface(bottomImage, TopBarBg, UIShapePreset.Inset);

            var botHL = bottom.AddComponent<HorizontalLayoutGroup>();
            botHL.spacing = UITheme.Spacing.Lg; botHL.padding = UITheme.Padding(UITheme.Spacing.Lg, 0f);
            botHL.childAlignment = TextAnchor.MiddleCenter;
            botHL.childControlWidth = true; botHL.childControlHeight = true;
            botHL.childForceExpandWidth = false; botHL.childForceExpandHeight = false;

            var paintInfo = MakeTMP("PInfo", bottom.transform);
            paintInfo.text = "Malowanie w cenie"; paintInfo.fontSize = 13;
            paintInfo.color = TextMuted; paintInfo.raycastTarget = false;
            paintInfo.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var priceLbl = MakeTMP("Price", bottom.transform);
            priceLbl.text = $"{v.price:N0} z\u0142"; priceLbl.fontSize = 22;
            priceLbl.fontStyle = FontStyles.Bold; priceLbl.color = PriceColor;
            priceLbl.alignment = TextAlignmentOptions.Right; priceLbl.raycastTarget = false;
            priceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;

            var buyGO = NewGO("BuyBtn", bottom.transform);
            var buyImage = buyGO.AddComponent<Image>();
            UITheme.ApplySurface(buyImage, BtnBuy, UIShapePreset.Pill);
            var buyLE = buyGO.AddComponent<LayoutElement>();
            buyLE.preferredWidth = 140f; buyLE.preferredHeight = 44f;
            var buyBtn = buyGO.AddComponent<Button>();
            buyBtn.targetGraphic = buyImage;
            buyBtn.colors = UITheme.CreateColorBlock(
                BtnBuy,
                BtnBuyHover,
                UITheme.Darken(BtnBuyHover, 0.18f),
                BtnBuy,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            var capturedV = v;
            buyBtn.onClick.AddListener(() => {
                AddMarketVehicleToCart(capturedV);
                CloseMarketDetailPopup();
            });
            var buyLbl = MakeTMP("BuyLbl", buyGO.transform);
            buyLbl.text = "Do koszyka"; buyLbl.fontSize = 14; buyLbl.fontStyle = FontStyles.Bold;
            buyLbl.color = TextPrimary; buyLbl.alignment = TextAlignmentOptions.Center;
            buyLbl.raycastTarget = false; FillRT(buyLbl.gameObject);

            // === CONTENT AREA (between header and bottom, two columns) ===
            var content = NewGO("Content", card.transform);
            var cRT = content.GetComponent<RectTransform>();
            cRT.anchorMin = Vector2.zero; cRT.anchorMax = Vector2.one;
            cRT.offsetMin = new Vector2(0, 60);   // above bottom bar
            cRT.offsetMax = new Vector2(0, -60);   // below header

            // Left column (stats + technical) â€” 50% width
            var leftCol = NewGO("Left", content.transform);
            var lRT = leftCol.GetComponent<RectTransform>();
            lRT.anchorMin = Vector2.zero; lRT.anchorMax = new Vector2(0.5f, 1f);
            lRT.offsetMin = new Vector2(24, 16); lRT.offsetMax = new Vector2(-8, -8);

            var leftVLG = leftCol.AddComponent<VerticalLayoutGroup>();
            leftVLG.spacing = UITheme.Spacing.Xxs; leftVLG.childAlignment = TextAnchor.UpperLeft;
            leftVLG.childControlWidth = true; leftVLG.childControlHeight = true;
            leftVLG.childForceExpandWidth = true; leftVLG.childForceExpandHeight = false;

            // Stats
            PopupSectionTitle(leftCol.transform, "Dane pojazdu");
            PopupInfoLine(leftCol.transform, "V max", $"{v.maxSpeedKmh} km/h");
            if (v.powerKw > 0)
                PopupInfoLine(leftCol.transform, "Moc", $"{v.powerKw} kW");
            PopupInfoLine(leftCol.transform, "Uk\u0142ad osi", v.wheelbase);
            PopupInfoLine(leftCol.transform, "Przebieg", $"{v.mileageKm:N0} km");
            PopupInfoLine(leftCol.transform, "Stan", $"{v.conditionPercent:F0}%",
                GetConditionColor(v.conditionPercent));
            if (isPassenger)
                BuildSeatBreakdownSection(leftCol.transform, v.passengerSeats, v.seatBreakdown);
            var inspStatusMarket = v.inspections.GetMostUrgent(NowGameTime, v.mileageKm);
            Color inspCol = GetInspectionColorFromProgress(inspStatusMarket.progress);
            PopupInfoLine(leftCol.transform, "Przegl\u0105d",
                FormatInspectionCompact(inspStatusMarket), inspCol);
            PopupInfoLine(leftCol.transform, "Lokalizacja", v.location);

            // Technical â€” no spacer, compact
            PopupSectionTitle(leftCol.transform, "Wyposa\u017cenie techniczne");
            if (v.voltages != null && v.voltages.Count > 0)
                PopupInfoLine(leftCol.transform, "Napi\u0119cia", string.Join(", ", v.voltages));
            else if (v.type == FleetVehicleType.DMU)
                PopupInfoLine(leftCol.transform, "Nap\u0119d", "Spalinowy");

            string safety = v.safetySystemsInstalled != null && v.safetySystemsInstalled.Count > 0
                ? string.Join(", ", v.safetySystemsInstalled) : "Brak";
            PopupInfoLine(leftCol.transform, "Bezpiecze\u0144stwo", safety,
                safety == "Brak" ? InspUrgent : TextPrimary);

            // Right column (comfort + seat preview) â€” 50% width
            var rightCol = NewGO("Right", content.transform);
            var rRT = rightCol.GetComponent<RectTransform>();
            rRT.anchorMin = new Vector2(0.5f, 0); rRT.anchorMax = Vector2.one;
            rRT.offsetMin = new Vector2(8, 16); rRT.offsetMax = new Vector2(-24, -8);

            var rightVLG = rightCol.AddComponent<VerticalLayoutGroup>();
            rightVLG.spacing = UITheme.Spacing.Xxs; rightVLG.childAlignment = TextAnchor.UpperLeft;
            rightVLG.childControlWidth = true; rightVLG.childControlHeight = true;
            rightVLG.childForceExpandWidth = true; rightVLG.childForceExpandHeight = false;

            if (isPassenger)
            {
                PopupSectionTitle(rightCol.transform, "Komfort pasa\u017cer\u00f3w");
                if (v.comfortFeatures != null && v.comfortFeatures.Count > 0)
                {
                    foreach (string feat in v.comfortFeatures)
                        PopupInfoLine(rightCol.transform, "+", feat, UITheme.Success);
                }
                else
                {
                    var noFeat = MakeTMP("NoFeat", rightCol.transform);
                    noFeat.text = "Brak udogodnie\u0144"; noFeat.fontSize = 14;
                    noFeat.color = TextMuted; noFeat.raycastTarget = false;
                    noFeat.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                }

                // Seat preview placeholder
                PopupSectionTitle(rightCol.transform, "Podgl\u0105d miejsc");
                var seatPrev = NewGO("SeatPreview", rightCol.transform);
                var seatPreviewImage = seatPrev.AddComponent<Image>();
                UITheme.ApplySurface(seatPreviewImage, InputBg, UIShapePreset.Inset);
                seatPrev.AddComponent<LayoutElement>().flexibleHeight = 1f;
                var spLbl = MakeTMP("Lbl", seatPrev.transform);
                spLbl.text = "Tutaj b\u0119dzie podgl\u0105d\nmiejsc pasa\u017cerskich";
                spLbl.fontSize = 16; spLbl.color = TextMuted;
                spLbl.alignment = TextAlignmentOptions.Center;
                spLbl.raycastTarget = false; FillRT(spLbl.gameObject);
            }
            else
            {
                // For locomotives â€” right column empty or with extra info
                PopupSectionTitle(rightCol.transform, "Informacje dodatkowe");
                PopupInfoLine(rightCol.transform, "Typ", GetTypeShortLabel(v.type));
                PopupInfoLine(rightCol.transform, "Malowanie", v.paintScheme);
            }
        }






        // â”€â”€ POPUP HELPERS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void BuildPopupCloseButton(Transform cardTransform)
        {
            var closeGO = NewGO("CloseBtn", cardTransform);
            var closeRT = closeGO.GetComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1, 1); closeRT.anchorMax = new Vector2(1, 1);
            closeRT.pivot = new Vector2(1, 1);
            closeRT.anchoredPosition = new Vector2(-8, -8);
            closeRT.sizeDelta = new Vector2(36, 36);

            var closeImg = closeGO.AddComponent<Image>();
            UITheme.ApplySurface(closeImg, BtnSecondary, UIShapePreset.Pill);

            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.colors = UITheme.CreateColorBlock(
                BtnSecondary,
                UITheme.RaisedSurface,
                UITheme.Border,
                BtnSecondary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            closeBtn.onClick.AddListener(CloseMarketDetailPopup);

            var closeLbl = MakeTMP("X", closeGO.transform);
            closeLbl.text = "X"; closeLbl.fontSize = 18;
            closeLbl.color = TextPrimary; closeLbl.alignment = TextAlignmentOptions.Center;
            closeLbl.raycastTarget = false; FillRT(closeLbl.gameObject);
        }

        private void CloseMarketDetailPopup()
        {
            if (_marketDetailPopupGO != null)
            {
                Destroy(_marketDetailPopupGO);
                _marketDetailPopupGO = null;
            }
            _currentOwnedDetailVehicle = null;
            _currentMarketDetailVehicle = null;
        }

        /// <summary>Zamyka i ponownie otwiera aktualnie wyswietlany popup szczegolow (np. po wykonaniu przegladu).</summary>
        private void RefreshCurrentDetailPopup()
        {
            // M-Windows P2: owned przeniesiony do pływających okien (RefreshAllOwnedDetailWindows).
            // Tu zostaje tylko market (wciąż modal singleton).
            if (_currentMarketDetailVehicle != null)
            {
                var v = _currentMarketDetailVehicle;
                ShowMarketDetailPopup(v);
            }
        }

        // â”€â”€ SEAT BREAKDOWN SECTION (popup) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€


        // â”€â”€ ROZSUWANA SEKCJA PRZEGLADOW W POPUPIE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€



    }
}
