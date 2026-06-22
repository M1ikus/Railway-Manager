using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — szkielet konfiguratora nowego pojazdu (BuyFleet → Nowe):
    /// lewa lista (wagon konfigurowalny + rodziny), prawy panel (zasilany przez
    /// WagonConfigurator/FamilyConfigurator partials), dolny pasek ilosc/cena/czas/zamow.
    /// Legacy single-model path (NewVehicleModel + checkbox grid + seat layout generator)
    /// usuniety w M-UIPolish 2026-06-18 — byl martwy od M-FC-3.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── CONFIGURATOR (Nowe) ──────────────────────

        private void BuildConfigurator()
        {
            float topOffset = TopBarH + TabBarH + MarketSubTabH;
            const float bottomH = 60f;

            _configuratorGO = NewGO("Configurator", _root.transform);
            var rt = _configuratorGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(0f, -topOffset);

            // ── Left panel (model list, 25%) ──
            var leftPanel = NewGO("LeftPanel", _configuratorGO.transform);
            var lpRT = leftPanel.GetComponent<RectTransform>();
            lpRT.anchorMin = Vector2.zero; lpRT.anchorMax = new Vector2(0.25f, 1f);
            lpRT.offsetMin = new Vector2(12f, bottomH + 12f); lpRT.offsetMax = new Vector2(-8f, -12f);
            var leftPanelImage = leftPanel.AddComponent<Image>();
            UITheme.ApplySurface(leftPanelImage, TopBarBg, UIShapePreset.Panel);

            // Left: scroll area for model list (M-FC-2: ScrollRect jako rodzic Viewportu — naprawia scroll wheel events)
            var lpScrollGO = NewGO("Scroll", leftPanel.transform);
            var lpSrRT = lpScrollGO.GetComponent<RectTransform>();
            lpSrRT.anchorMin = Vector2.zero; lpSrRT.anchorMax = Vector2.one;
            lpSrRT.offsetMin = Vector2.zero; lpSrRT.offsetMax = Vector2.zero;
            var lpScroll = lpScrollGO.AddComponent<ScrollRect>();
            lpScroll.horizontal = false; lpScroll.scrollSensitivity = 30f;

            var lpViewport = NewGO("Viewport", lpScrollGO.transform);
            var lpVpRT = lpViewport.GetComponent<RectTransform>();
            lpVpRT.anchorMin = Vector2.zero; lpVpRT.anchorMax = Vector2.one;
            lpVpRT.offsetMin = Vector2.zero; lpVpRT.offsetMax = Vector2.zero;
            var lpVpImg = lpViewport.AddComponent<Image>(); // transparent raycast target dla scroll wheel
            lpVpImg.color = UITheme.WithAlpha(Color.black, 0.001f);
            lpViewport.AddComponent<RectMask2D>();
            lpScroll.viewport = lpVpRT;

            var lpContent = NewGO("Content", lpViewport.transform);
            var lpCRT = lpContent.GetComponent<RectTransform>();
            lpCRT.anchorMin = new Vector2(0, 1); lpCRT.anchorMax = Vector2.one;
            lpCRT.pivot = new Vector2(0.5f, 1); lpCRT.anchoredPosition = Vector2.zero;
            lpCRT.sizeDelta = Vector2.zero;
            var lpVLG = lpContent.AddComponent<VerticalLayoutGroup>();
            lpVLG.spacing = UITheme.Spacing.Sm; lpVLG.padding = UITheme.Padding(UITheme.Spacing.Md);
            lpVLG.childAlignment = TextAnchor.UpperCenter;
            lpVLG.childControlWidth = true; lpVLG.childControlHeight = true;
            lpVLG.childForceExpandWidth = true; lpVLG.childForceExpandHeight = false;
            lpContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            lpScroll.content = lpCRT;

            var introCard = NewGO("ConfiguratorIntroCard", lpContent.transform);
            var introCardImg = introCard.AddComponent<Image>();
            UITheme.ApplySurface(introCardImg, PanelBg, UIShapePreset.Panel);
            introCard.AddComponent<LayoutElement>().preferredHeight = 100f;
            var introCardVLG = introCard.AddComponent<VerticalLayoutGroup>();
            introCardVLG.padding = UITheme.Padding(UITheme.Spacing.Lg);
            introCardVLG.spacing = UITheme.Spacing.Xs;
            introCardVLG.childAlignment = TextAnchor.UpperLeft;
            introCardVLG.childControlWidth = true;
            introCardVLG.childControlHeight = true;
            introCardVLG.childForceExpandWidth = true;
            introCardVLG.childForceExpandHeight = false;

            var introEyebrow = MakeTMP("Eyebrow", introCard.transform);
            introEyebrow.text = "KONFIGURATOR";
            introEyebrow.fontSize = 9;
            introEyebrow.fontStyle = FontStyles.Bold;
            introEyebrow.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f);
            introEyebrow.raycastTarget = false;

            var introTitle = MakeTMP("Title", introCard.transform);
            introTitle.text = "<b>Wybierz bazę pojazdu</b>";
            introTitle.fontSize = 16;
            introTitle.color = TextPrimary;
            introTitle.richText = true;
            introTitle.raycastTarget = false;

            var introBody = MakeTMP("Body", introCard.transform);
            introBody.text = "Zacznij od rodziny, gotowego modelu albo wagonu konfigurowalnego. Prawa strona pokaże tylko potrzebne opcje.";
            introBody.fontSize = 11;
            introBody.color = TextMuted;
            introBody.textWrappingMode = TextWrappingModes.Normal;
            introBody.raycastTarget = false;

            // Populate model list — wagon konfigurowalny + rodziny pojazdow
            // M-FC-2: pseudo-entry "Wagon konfigurowalny" jako pierwszy item
            BuildWagonListItem(lpContent.transform);

            // M-FC-3: rodziny pojazdów (FLIRT, SA, EU160, ...) zamiast płaskich SKU
            foreach (var family in FleetCatalog.Families)
                BuildFamilyListItem(lpContent.transform, family);

            // ── Right panel (config, 75%) ──
            var rightPanel = NewGO("RightPanel", _configuratorGO.transform);
            var rpRT = rightPanel.GetComponent<RectTransform>();
            rpRT.anchorMin = new Vector2(0.25f, 0); rpRT.anchorMax = Vector2.one;
            rpRT.offsetMin = new Vector2(8f, bottomH + 12f); rpRT.offsetMax = new Vector2(-12f, -12f);
            var rightPanelImage = rightPanel.AddComponent<Image>();
            UITheme.ApplySurface(rightPanelImage, PanelBg, UIShapePreset.PanelLarge);

            // Right: scroll area for config (M-FC-2: ScrollRect jako rodzic Viewportu — naprawia scroll wheel events)
            var rpScrollGO = NewGO("Scroll", rightPanel.transform);
            var rpSrRT = rpScrollGO.GetComponent<RectTransform>();
            rpSrRT.anchorMin = Vector2.zero; rpSrRT.anchorMax = Vector2.one;
            rpSrRT.offsetMin = Vector2.zero; rpSrRT.offsetMax = Vector2.zero;
            var rpScroll = rpScrollGO.AddComponent<ScrollRect>();
            rpScroll.horizontal = false; rpScroll.scrollSensitivity = 30f;

            var rpViewport = NewGO("Viewport", rpScrollGO.transform);
            var rpVpRT = rpViewport.GetComponent<RectTransform>();
            rpVpRT.anchorMin = Vector2.zero; rpVpRT.anchorMax = Vector2.one;
            rpVpRT.offsetMin = Vector2.zero; rpVpRT.offsetMax = Vector2.zero;
            var rpVpImg = rpViewport.AddComponent<Image>(); // transparent raycast target dla scroll wheel
            rpVpImg.color = UITheme.WithAlpha(Color.black, 0.001f);
            rpViewport.AddComponent<RectMask2D>();
            rpScroll.viewport = rpVpRT;

            var rpContent = NewGO("Content", rpViewport.transform);
            var rpCRT = rpContent.GetComponent<RectTransform>();
            rpCRT.anchorMin = new Vector2(0, 1); rpCRT.anchorMax = Vector2.one;
            rpCRT.pivot = new Vector2(0.5f, 1); rpCRT.anchoredPosition = Vector2.zero;
            rpCRT.sizeDelta = Vector2.zero;
            var rpVLG = rpContent.AddComponent<VerticalLayoutGroup>();
            rpVLG.spacing = UITheme.Spacing.Lg; rpVLG.padding = UITheme.Padding(UITheme.Spacing.Xl);
            rpVLG.childAlignment = TextAnchor.UpperLeft;
            rpVLG.childControlWidth = true; rpVLG.childControlHeight = true;
            rpVLG.childForceExpandWidth = true; rpVLG.childForceExpandHeight = false;
            rpContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rpScroll.content = rpCRT;
            _configRightContent = rpContent.transform;
            _configRightContentRoot = rpContent.transform;

            // Initial state: prompt to select
            var selectPromptCard = NewGO("SelectPromptCard", _configRightContent);
            var selectPromptCardImage = selectPromptCard.AddComponent<Image>();
            UITheme.ApplySurface(selectPromptCardImage, TopBarBg, UIShapePreset.Panel);
            var selectPromptCardLE = selectPromptCard.AddComponent<LayoutElement>();
            selectPromptCardLE.preferredHeight = 220f;
            var selectPromptCardVLG = selectPromptCard.AddComponent<VerticalLayoutGroup>();
            selectPromptCardVLG.padding = UITheme.Padding(UITheme.Spacing.Xxl);
            selectPromptCardVLG.spacing = UITheme.Spacing.Sm;
            selectPromptCardVLG.childAlignment = TextAnchor.UpperLeft;
            selectPromptCardVLG.childControlWidth = true;
            selectPromptCardVLG.childControlHeight = true;
            selectPromptCardVLG.childForceExpandWidth = true;
            selectPromptCardVLG.childForceExpandHeight = false;

            var selectPromptEyebrow = MakeTMP("PromptEyebrow", selectPromptCard.transform);
            selectPromptEyebrow.text = "GOTOWE NA WYBÓR";
            selectPromptEyebrow.fontSize = 9;
            selectPromptEyebrow.fontStyle = FontStyles.Bold;
            selectPromptEyebrow.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f);
            selectPromptEyebrow.raycastTarget = false;
            selectPromptEyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var selectPromptTitle = MakeTMP("SelectPromptTitle", selectPromptCard.transform);
            selectPromptTitle.text = "<b>Najpierw wybierz model po lewej stronie</b>";
            selectPromptTitle.fontSize = 22;
            selectPromptTitle.color = TextPrimary;
            selectPromptTitle.richText = true;
            selectPromptTitle.alignment = TextAlignmentOptions.Left;
            selectPromptTitle.raycastTarget = false;
            selectPromptTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            var selectPrompt = MakeTMP("SelectPrompt", selectPromptCard.transform);
            selectPrompt.text = LocalizationService.Get("fleet.configurator.empty_select_prompt");
            selectPrompt.fontSize = 14;
            selectPrompt.color = TextMuted;
            selectPrompt.textWrappingMode = TextWrappingModes.Normal;
            selectPrompt.alignment = TextAlignmentOptions.Left;
            selectPrompt.raycastTarget = false;
            selectPrompt.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;

            var selectPromptHint = MakeTMP("SelectPromptHint", selectPromptCard.transform);
            selectPromptHint.text = "Po wyborze zobaczysz tylko istotne sekcje: wariant, wyposażenie, wnętrze i malowanie.";
            selectPromptHint.fontSize = 12;
            selectPromptHint.color = TextMuted;
            selectPromptHint.textWrappingMode = TextWrappingModes.Normal;
            selectPromptHint.alignment = TextAlignmentOptions.Left;
            selectPromptHint.raycastTarget = false;
            selectPromptHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            // ── Bottom bar (quantity + time + price + order) ──
            var bottom = NewGO("BottomBar", _configuratorGO.transform);
            var bRT = bottom.GetComponent<RectTransform>();
            bRT.anchorMin = Vector2.zero; bRT.anchorMax = new Vector2(1, 0);
            bRT.pivot = new Vector2(0.5f, 0);
            bRT.offsetMin = new Vector2(12f, 12f); bRT.offsetMax = new Vector2(-12f, bottomH + 12f);
            var bottomImage = bottom.AddComponent<Image>();
            UITheme.ApplySurface(bottomImage, TopBarBg, UIShapePreset.Panel);

            var botHL = bottom.AddComponent<HorizontalLayoutGroup>();
            botHL.spacing = UITheme.Spacing.Md; botHL.padding = UITheme.Padding(UITheme.Spacing.Xl, 0f);
            botHL.childAlignment = TextAnchor.MiddleCenter;
            botHL.childControlWidth = true; botHL.childControlHeight = true;
            botHL.childForceExpandWidth = false; botHL.childForceExpandHeight = false;

            // Quantity: [-] X [+]
            _configQuantityWidgets.Clear();
            var qtyLabel = MakeTMP("QtyLbl", bottom.transform);
            qtyLabel.text = LocalizationService.Get("fleet.configurator.quantity_label"); qtyLabel.fontSize = 14;
            qtyLabel.color = TextMuted; qtyLabel.raycastTarget = false;
            qtyLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 50f;
            _configQuantityWidgets.Add(qtyLabel.gameObject);

            var minusGO = NewGO("MinusBtn", bottom.transform);
            _configQuantityWidgets.Add(minusGO);
            var minusImage = minusGO.AddComponent<Image>();
            UITheme.ApplySurface(minusImage, TabNormal, UIShapePreset.Button);
            var minusLE = minusGO.AddComponent<LayoutElement>();
            minusLE.preferredWidth = 30f; minusLE.preferredHeight = 30f;
            var minusBtn = minusGO.AddComponent<Button>();
            minusBtn.targetGraphic = minusImage;
            minusBtn.colors = UITheme.CreateColorBlock(
                TabNormal,
                RowHover,
                UITheme.Darken(RowHover, 0.08f),
                RowHover,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            minusBtn.onClick.AddListener(() => { if (_configQuantity > 1) { _configQuantity--; RecalculateConfigPrice(); } });
            var minusLbl = MakeTMP("Lbl", minusGO.transform);
            minusLbl.text = "-"; minusLbl.fontSize = 18; minusLbl.fontStyle = FontStyles.Bold;
            minusLbl.color = TextPrimary; minusLbl.alignment = TextAlignmentOptions.Center;
            minusLbl.raycastTarget = false; FillRT(minusLbl.gameObject);

            _configQuantityLbl = MakeTMP("Qty", bottom.transform);
            _configQuantityLbl.text = "1"; _configQuantityLbl.fontSize = 18;
            _configQuantityLbl.fontStyle = FontStyles.Bold; _configQuantityLbl.color = TextPrimary;
            _configQuantityLbl.alignment = TextAlignmentOptions.Center;
            _configQuantityLbl.raycastTarget = false;
            _configQuantityLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;
            _configQuantityWidgets.Add(_configQuantityLbl.gameObject);

            var plusGO = NewGO("PlusBtn", bottom.transform);
            _configQuantityWidgets.Add(plusGO);
            var plusImage = plusGO.AddComponent<Image>();
            UITheme.ApplySurface(plusImage, TabNormal, UIShapePreset.Button);
            var plusLE = plusGO.AddComponent<LayoutElement>();
            plusLE.preferredWidth = 30f; plusLE.preferredHeight = 30f;
            var plusBtn = plusGO.AddComponent<Button>();
            plusBtn.targetGraphic = plusImage;
            plusBtn.colors = UITheme.CreateColorBlock(
                TabNormal,
                RowHover,
                UITheme.Darken(RowHover, 0.08f),
                RowHover,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            plusBtn.onClick.AddListener(() => { _configQuantity++; RecalculateConfigPrice(); });
            var plusLbl = MakeTMP("Lbl", plusGO.transform);
            plusLbl.text = "+"; plusLbl.fontSize = 18; plusLbl.fontStyle = FontStyles.Bold;
            plusLbl.color = TextPrimary; plusLbl.alignment = TextAlignmentOptions.Center;
            plusLbl.raycastTarget = false; FillRT(plusLbl.gameObject);

            // Separator
            var sep = NewGO("Sep", bottom.transform);
            sep.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.45f);
            var sepLE = sep.AddComponent<LayoutElement>();
            sepLE.preferredWidth = 1f; sepLE.preferredHeight = 36f;
            _configQuantityWidgets.Add(sep);

            // Production time
            _configTimeLbl = MakeTMP("Time", bottom.transform);
            _configTimeLbl.text = ""; _configTimeLbl.fontSize = 12;
            _configTimeLbl.color = TextMuted; _configTimeLbl.raycastTarget = false;
            _configTimeLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 180f;

            var botSpacer = NewGO("Sp", bottom.transform);
            botSpacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Price
            _configTotalPriceLbl = MakeTMP("TotalPrice", bottom.transform);
            _configTotalPriceLbl.text = ""; _configTotalPriceLbl.fontSize = 20;
            _configTotalPriceLbl.fontStyle = FontStyles.Bold; _configTotalPriceLbl.color = PriceColor;
            _configTotalPriceLbl.alignment = TextAlignmentOptions.Right;
            _configTotalPriceLbl.raycastTarget = false;
            _configTotalPriceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 250f;

            // Order button
            var orderGO = NewGO("OrderBtn", bottom.transform);
            var orderImage = orderGO.AddComponent<Image>();
            UITheme.ApplySurface(orderImage, BtnBuy, UIShapePreset.Pill);
            var orderLE = orderGO.AddComponent<LayoutElement>();
            orderLE.preferredWidth = 140f; orderLE.preferredHeight = 44f;
            var orderBtn = orderGO.AddComponent<Button>();
            orderBtn.targetGraphic = orderImage;
            orderBtn.colors = UITheme.CreateColorBlock(
                BtnBuy,
                BtnBuyHover,
                UITheme.Darken(BtnBuyHover, 0.18f),
                BtnBuy,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            orderBtn.onClick.AddListener(OnAddToCart);
            var orderLbl = MakeTMP("Lbl", orderGO.transform);
            orderLbl.text = LocalizationService.Get("fleet.configurator.order_button"); orderLbl.fontSize = 14;
            orderLbl.fontStyle = FontStyles.Bold; orderLbl.color = TextPrimary;
            orderLbl.alignment = TextAlignmentOptions.Center;
            orderLbl.raycastTarget = false; FillRT(orderLbl.gameObject);
            _configQuantityWidgets.Add(orderGO);

            _configuratorGO.SetActive(false);
            // Hide quantity widgets initially (shown when a model is selected)
            SetConfigQuantityWidgetsVisible(false);
        }

        private void SetConfigQuantityWidgetsVisible(bool visible)
        {
            foreach (var go in _configQuantityWidgets)
                if (go != null) go.SetActive(visible);
        }

        private void RecalculateConfigPrice()
        {
            if (_configQuantityLbl != null)
                _configQuantityLbl.text = _configQuantity.ToString();

            // M-FC-2: wagon configurator price calc
            if (_isWagonConfiguratorActive)
            {
                RecalculateWagonPrice();
                return;
            }

            // M-FC-3: family configurator price calc
            if (_isFamilyConfiguratorActive)
            {
                RecalculateFamilyPrice();
                return;
            }

            // Nic nie wybrane (oba konfiguratory nieaktywne) — wyczysc etykiety.
            if (_configTotalPriceLbl != null) _configTotalPriceLbl.text = "";
            if (_configTimeLbl != null) _configTimeLbl.text = "";
        }
    }
}
