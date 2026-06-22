using System.Collections.Generic;
using System.Linq;
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
    /// Partial FleetPanelUI — koszyk zamowien (cart).
    /// Zawiera: przycisk koszyka w prawym gornym rogu, popup z lista pozycji,
    /// dodawanie nowych/uzywanych pojazdow, zmiane ilosci, tryb dostawy, usuwanie.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── CART BUTTON (top-right corner) ────────────

        /// <summary>
        /// Cart button pozycjonowany absolutnie w prawym górnym rogu
        /// na wysokości sub-taba (Używane/Nowe).
        /// </summary>
        private void BuildCartButton()
        {
            var cartGO = NewGO("CartBtn", _root.transform);
            _cartButtonGO = cartGO;
            var rt = cartGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            // Pozycja: prawa krawędź, na wysokości sub-taba (po TopBar + TabBar)
            rt.anchoredPosition = new Vector2(-24f, -(TopBarH + TabBarH + 4f));
            rt.sizeDelta = new Vector2(130f, 32f);

            var cartImage = cartGO.AddComponent<Image>();
            UITheme.ApplySurface(cartImage, BtnSecondary, UIShapePreset.Pill);
            var cartBtn = cartGO.AddComponent<Button>();
            cartBtn.targetGraphic = cartImage;
            cartBtn.colors = UITheme.CreateColorBlock(
                BtnSecondary,
                RowHover,
                UITheme.Darken(RowHover, 0.08f),
                RowHover,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            cartBtn.onClick.AddListener(ShowCartPopup);

            var cartLabel = MakeTMP("Lbl", cartGO.transform);
            cartLabel.text = LocalizationService.Get("fleet.cart.button_label"); cartLabel.fontSize = 14;
            cartLabel.fontStyle = FontStyles.Bold; cartLabel.color = TextPrimary;
            cartLabel.alignment = TextAlignmentOptions.Center;
            cartLabel.raycastTarget = false; FillRT(cartLabel.gameObject);

            var badge = NewGO("Badge", cartGO.transform);
            var badgeRT = badge.GetComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(0.82f, 0.55f);
            badgeRT.anchorMax = new Vector2(1.10f, 1.15f);
            badgeRT.offsetMin = Vector2.zero; badgeRT.offsetMax = Vector2.zero;
            var badgeImage = badge.AddComponent<Image>();
            UITheme.ApplySurface(badgeImage, CartBadgeColor, UIShapePreset.Pill);

            _cartCountLbl = MakeTMP("Count", badge.transform);
            _cartCountLbl.text = ""; _cartCountLbl.fontSize = 11;
            _cartCountLbl.fontStyle = FontStyles.Bold; _cartCountLbl.color = TextPrimary;
            _cartCountLbl.alignment = TextAlignmentOptions.Center;
            _cartCountLbl.raycastTarget = false; FillRT(_cartCountLbl.gameObject);

            cartGO.SetActive(false);
        }

        // ── ADD TO CART ───────────────────────────────

        private void OnAddToCart()
        {
            // M-FC-2: wagon configurator path
            if (_isWagonConfiguratorActive)
            {
                OnWagonAddToCart();
                return;
            }

            // M-FC-3: family configurator path
            if (_isFamilyConfiguratorActive)
            {
                OnFamilyAddToCart();
                return;
            }

            // Zaden konfigurator nieaktywny \u2014 nie ma czego dodac (przycisk zwykle ukryty
            // dopoki gracz nie wybierze wagonu lub rodziny po lewej stronie).
            Log.Info("[Fleet] Nie wybrano pojazdu do konfiguracji!");
        }

        private void AddMarketVehicleToCart(FleetMarketVehicle v)
        {
            var item = new CartItem
            {
                cartId = FleetService.NextCartId++,
                isNewVehicle = false,
                marketVehicle = v,
                quantity = 1,
                unitPrice = v.price,
                deliveryMode = CartDeliveryMode.DeliverToDepot,
                deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE,
                deliveryDepotName = "Zajezdnia Mokotow"
            };
            FleetService.AddToCart(item);
            // Remove from market immediately to prevent duplicates
            FleetService.RemoveMarketVehicle(v);
            UpdateCartBadge();
            // Refresh market list if currently shown
            if (_activeTab == FleetTab.BuyFleet && _marketSubTab == MarketSubTab.Used)
                PopulateContent();
            Log.Info($"[Cart] Dodano: {v.number}, cena: {item.TotalPrice:N0} z\u0142 (koszyk: {_cart.Count} pozycji)");
        }

        private void UpdateCartBadge()
        {
            if (_cartCountLbl != null)
                _cartCountLbl.text = _cart.Count > 0 ? _cart.Count.ToString() : "";
        }

        // ═══════════════════════════════════════════════
        //  CART POPUP
        // ═══════════════════════════════════════════════

        private void ShowCartPopup()
        {
            if (_cartPopupGO != null) Destroy(_cartPopupGO);

            _cartPopupGO = NewGO("CartPopup", _root.transform);
            var rt = _cartPopupGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var dimImg = _cartPopupGO.AddComponent<Image>();
            dimImg.color = UITheme.WithAlpha(UITheme.AppBackground, 0.78f);
            var dimBtn = _cartPopupGO.AddComponent<Button>();
            dimBtn.targetGraphic = dimImg;
            dimBtn.onClick.AddListener(CloseCartPopup);

            // Card
            var card = NewGO("Card", _cartPopupGO.transform);
            var cRT = card.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.15f, 0.08f);
            cRT.anchorMax = new Vector2(0.85f, 0.92f);
            cRT.offsetMin = Vector2.zero; cRT.offsetMax = Vector2.zero;
            var cardImage = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImage, PanelBg, UIShapePreset.PanelLarge);
            card.AddComponent<Button>().onClick.AddListener(() => { });

            // Close X (created last and moved to top of hierarchy so nothing blocks it)
            BuildPopupCloseButton(card.transform);
            var closeBtnTr = card.transform.Find("CloseBtn");
            if (closeBtnTr != null)
            {
                var closeBtn = closeBtnTr.GetComponent<Button>();
                closeBtn.onClick.RemoveAllListeners();
                closeBtn.onClick.AddListener(CloseCartPopup);
                closeBtnTr.SetAsLastSibling();
            }

            // Header
            var header = NewGO("Header", card.transform);
            var hRT = header.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1); hRT.anchorMax = Vector2.one;
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.offsetMin = new Vector2(24, -50); hRT.offsetMax = new Vector2(-24, 0);
            var headerImage = header.AddComponent<Image>();
            UITheme.ApplySurface(headerImage, UITheme.WithAlpha(TopBarBg, 0.92f), UIShapePreset.Inset);

            var titleLbl = MakeTMP("Title", header.transform);
            titleLbl.text = LocalizationService.Get("fleet.cart.title_format", _cart.Count); titleLbl.fontSize = 24;
            titleLbl.fontStyle = FontStyles.Bold; titleLbl.color = TextPrimary;
            titleLbl.raycastTarget = false;
            FillRT(titleLbl.gameObject);

            // Bottom bar
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
            botHL.childForceExpandWidth = false;

            long totalSum = 0;
            foreach (var item in _cart) totalSum += item.TotalPrice;
            bool canAfford = GameState.Money >= totalSum; // M-Economy Faza 3: polityka „nie stać → nie kupuj"

            var sumLbl = MakeTMP("Sum", bottom.transform);
            sumLbl.text = LocalizationService.Get("fleet.cart.total_format", NumberFormatService.FormatCurrency(totalSum)); sumLbl.fontSize = 20;
            sumLbl.fontStyle = FontStyles.Bold; sumLbl.color = canAfford ? PriceColor : UITheme.Danger;
            sumLbl.alignment = TextAlignmentOptions.Right;
            sumLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bool canOrder = _cart.Count > 0 && canAfford; // M-Economy Faza 3: nie sta\u0107 \u2192 przycisk nieaktywny
            var orderAllGO = NewGO("OrderAll", bottom.transform);
            Color orderAllColor = canOrder ? BtnBuy : UITheme.WithAlpha(BtnSecondary, 0.75f);
            var orderAllImage = orderAllGO.AddComponent<Image>();
            UITheme.ApplySurface(orderAllImage, orderAllColor, UIShapePreset.Pill);
            var oaLE = orderAllGO.AddComponent<LayoutElement>();
            oaLE.preferredWidth = 180f; oaLE.preferredHeight = 44f;
            var orderAllBtn = orderAllGO.AddComponent<Button>();
            orderAllBtn.targetGraphic = orderAllImage;
            orderAllBtn.colors = UITheme.CreateColorBlock(
                orderAllColor,
                canOrder ? BtnBuyHover : UITheme.WithAlpha(BtnSecondary, 0.75f),
                canOrder ? UITheme.Darken(BtnBuyHover, 0.18f) : UITheme.WithAlpha(BtnSecondary, 0.75f),
                orderAllColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            orderAllBtn.interactable = canOrder;
            orderAllBtn.onClick.AddListener(() => {
                // M-Economy Faza 3: zakup POBIERA kas\u0119 (TryCheckout) + blokuje gdy nie sta\u0107.
                var result = CartProcessor.TryCheckout(_cart);
                if (result.InsufficientFunds)
                {
                    Log.Info($"[Cart] Brak srodkow na zakup: {result.TotalZl:N0} zl (mamy {GameState.Money:N0} zl)");
                    return; // polityka \u201enie sta\u0107 \u2192 nie kupuj"
                }
                Log.Info($"[Cart] Zamowienie: {result.Added} pojazdow, razem: {result.TotalZl:N0} zl (pobrano z kasy)");
                FleetService.ClearCart();
                UpdateCartBadge();
                CloseCartPopup();
                UpdateCounter();
                // Refresh current tab (My Fleet for new owned vehicles, Used market to remove sold ones)
                PopulateContent();
            });
            var oaLbl = MakeTMP("Lbl", orderAllGO.transform);
            oaLbl.text = LocalizationService.Get("fleet.cart.order_all"); oaLbl.fontSize = 15;
            oaLbl.fontStyle = FontStyles.Bold; oaLbl.color = TextPrimary;
            oaLbl.alignment = TextAlignmentOptions.Center;
            oaLbl.raycastTarget = false; FillRT(oaLbl.gameObject);

            // Scroll area for cart items
            var content = NewGO("Content", card.transform);
            var coRT = content.GetComponent<RectTransform>();
            coRT.anchorMin = Vector2.zero; coRT.anchorMax = Vector2.one;
            coRT.offsetMin = new Vector2(0, 60); coRT.offsetMax = new Vector2(0, -50);

            var viewport = NewGO("Viewport", content.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            var viewportImage = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportImage, UITheme.WithAlpha(UITheme.PrimarySurface, 0.24f), UIShapePreset.Inset);
            viewport.AddComponent<RectMask2D>();

            var scrollGO = NewGO("Scroll", content.transform);
            var srRT = scrollGO.GetComponent<RectTransform>();
            srRT.anchorMin = Vector2.zero; srRT.anchorMax = Vector2.one;
            srRT.offsetMin = Vector2.zero; srRT.offsetMax = Vector2.zero;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.scrollSensitivity = 30f;
            scroll.viewport = vpRT;

            var listContent = NewGO("ListContent", viewport.transform);
            var lcRT = listContent.GetComponent<RectTransform>();
            lcRT.anchorMin = new Vector2(0, 1); lcRT.anchorMax = Vector2.one;
            lcRT.pivot = new Vector2(0.5f, 1); lcRT.anchoredPosition = Vector2.zero;
            lcRT.sizeDelta = Vector2.zero;
            var lcVLG = listContent.AddComponent<VerticalLayoutGroup>();
            lcVLG.spacing = UITheme.Spacing.Xs; lcVLG.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            lcVLG.childAlignment = TextAnchor.UpperCenter;
            lcVLG.childControlWidth = true; lcVLG.childControlHeight = true;
            lcVLG.childForceExpandWidth = true; lcVLG.childForceExpandHeight = false;
            listContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = lcRT;

            if (_cart.Count == 0)
            {
                var emptyLbl = MakeTMP("Empty", listContent.transform);
                emptyLbl.text = LocalizationService.Get("fleet.cart.empty"); emptyLbl.fontSize = 20;
                emptyLbl.color = TextMuted; emptyLbl.alignment = TextAlignmentOptions.Center;
                emptyLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
            }
            else
            {
                foreach (var item in _cart)
                    BuildCartItemRow(listContent.transform, item);
            }

            // Ensure close X is on top of everything
            var closeOnTop = card.transform.Find("CloseBtn");
            if (closeOnTop != null) closeOnTop.SetAsLastSibling();
        }

        private void BuildCartItemRow(Transform parent, CartItem item)
        {
            var row = NewGO($"Cart_{item.cartId}", parent);
            var rowImage = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImage, RowBg, UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = 80f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Md; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            // Thumbnail
            var thumbGO = NewGO("Thumb", row.transform);
            FleetVehicleType vType = ResolveCartItemType(item);
            var thumbImage = thumbGO.AddComponent<Image>();
            UITheme.ApplySurface(thumbImage, GetThumbnailColor(vType), UIShapePreset.Button);
            var tLE = thumbGO.AddComponent<LayoutElement>();
            tLE.preferredWidth = 60f; tLE.preferredHeight = 40f;
            var tLbl = MakeTMP("Lbl", thumbGO.transform);
            tLbl.text = GetTypeShortLabel(vType); tLbl.fontSize = 10;
            tLbl.color = TextPrimary; tLbl.alignment = TextAlignmentOptions.Center;
            tLbl.raycastTarget = false; FillRT(tLbl.gameObject);

            // Name + details
            var nameCol = NewGO("Name", row.transform);
            var ncVLG = nameCol.AddComponent<VerticalLayoutGroup>();
            ncVLG.spacing = UITheme.Spacing.Xxs; ncVLG.childAlignment = TextAnchor.MiddleLeft;
            ncVLG.childControlWidth = true; ncVLG.childControlHeight = true;
            ncVLG.childForceExpandWidth = true;
            nameCol.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var nameLbl = MakeTMP("Name", nameCol.transform);
            nameLbl.text = item.DisplayName; nameLbl.fontSize = 16;
            nameLbl.fontStyle = FontStyles.Bold; nameLbl.color = TextPrimary;
            nameLbl.raycastTarget = false;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            string details = ResolveCartItemDetails(item);
            var detLbl = MakeTMP("Det", nameCol.transform);
            detLbl.text = details; detLbl.fontSize = 12;
            detLbl.color = TextMuted; detLbl.raycastTarget = false;
            detLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            // Quantity controls (only for new vehicles)
            if (item.isNewVehicle)
            {
                var qtyMinGO = NewGO("QMin", row.transform);
                var qtyMinImage = qtyMinGO.AddComponent<Image>();
                UITheme.ApplySurface(qtyMinImage, TabNormal, UIShapePreset.Button);
                var qmLE = qtyMinGO.AddComponent<LayoutElement>();
                qmLE.preferredWidth = 26f; qmLE.preferredHeight = 26f;
                var qmBtn = qtyMinGO.AddComponent<Button>();
                qmBtn.targetGraphic = qtyMinImage;
                qmBtn.colors = UITheme.CreateColorBlock(
                    TabNormal,
                    RowHover,
                    UITheme.Darken(RowHover, 0.08f),
                    RowHover,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                int capQidMin = item.cartId;
                qmBtn.onClick.AddListener(() => {
                    var ci = FleetService.GetCartItemById(capQidMin);
                    if (ci != null && ci.quantity > 1)
                    {
                        ci.quantity--;
                        if (ci.deliveryMode == CartDeliveryMode.DeliverToDepot)
                            ci.deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE * ci.quantity;
                        ShowCartPopup();
                    }
                });
                var qmLbl = MakeTMP("L", qtyMinGO.transform);
                qmLbl.text = "-"; qmLbl.fontSize = 14; qmLbl.fontStyle = FontStyles.Bold;
                qmLbl.color = TextPrimary; qmLbl.alignment = TextAlignmentOptions.Center;
                qmLbl.raycastTarget = false; FillRT(qmLbl.gameObject);

                var qtyLbl = MakeTMP("Q", row.transform);
                qtyLbl.text = $"x{item.quantity}"; qtyLbl.fontSize = 15;
                qtyLbl.fontStyle = FontStyles.Bold; qtyLbl.color = TextPrimary;
                qtyLbl.alignment = TextAlignmentOptions.Center;
                qtyLbl.raycastTarget = false;
                qtyLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;

                var qtyPlusGO = NewGO("QPlus", row.transform);
                var qtyPlusImage = qtyPlusGO.AddComponent<Image>();
                UITheme.ApplySurface(qtyPlusImage, TabNormal, UIShapePreset.Button);
                var qpLE = qtyPlusGO.AddComponent<LayoutElement>();
                qpLE.preferredWidth = 26f; qpLE.preferredHeight = 26f;
                var qpBtn = qtyPlusGO.AddComponent<Button>();
                qpBtn.targetGraphic = qtyPlusImage;
                qpBtn.colors = UITheme.CreateColorBlock(
                    TabNormal,
                    RowHover,
                    UITheme.Darken(RowHover, 0.08f),
                    RowHover,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                int capQidPlus = item.cartId;
                qpBtn.onClick.AddListener(() => {
                    var ci = FleetService.GetCartItemById(capQidPlus);
                    if (ci != null)
                    {
                        ci.quantity++;
                        if (ci.deliveryMode == CartDeliveryMode.DeliverToDepot)
                            ci.deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE * ci.quantity;
                        ShowCartPopup();
                    }
                });
                var qpLbl = MakeTMP("L", qtyPlusGO.transform);
                qpLbl.text = "+"; qpLbl.fontSize = 14; qpLbl.fontStyle = FontStyles.Bold;
                qpLbl.color = TextPrimary; qpLbl.alignment = TextAlignmentOptions.Center;
                qpLbl.raycastTarget = false; FillRT(qpLbl.gameObject);
            }

            // Delivery mode toggle
            var delGO = NewGO("Del", row.transform);
            bool deliverToDepot = item.deliveryMode == CartDeliveryMode.DeliverToDepot;
            var delImage = delGO.AddComponent<Image>();
            UITheme.ApplySurface(delImage, deliverToDepot ? TabActive : TabNormal, UIShapePreset.Pill);
            var delLE = delGO.AddComponent<LayoutElement>();
            delLE.preferredWidth = 90f; delLE.preferredHeight = 36f;
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delImage;
            delBtn.colors = UITheme.CreateColorBlock(
                deliverToDepot ? TabActive : TabNormal,
                deliverToDepot ? BtnBuyHover : RowHover,
                deliverToDepot ? UITheme.Darken(BtnBuyHover, 0.18f) : UITheme.Darken(RowHover, 0.08f),
                deliverToDepot ? TabActive : TabNormal,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            int capturedId = item.cartId;
            delBtn.onClick.AddListener(() => {
                var ci = FleetService.GetCartItemById(capturedId);
                if (ci != null)
                {
                    if (ci.deliveryMode == CartDeliveryMode.SelfPickup)
                    {
                        ci.deliveryMode = CartDeliveryMode.DeliverToDepot;
                        ci.deliveryCost = FleetConstants.DELIVERY_COST_PER_VEHICLE * ci.quantity;
                        ci.deliveryDepotName = "Zajezdnia Mokotow";
                    }
                    else
                    {
                        ci.deliveryMode = CartDeliveryMode.SelfPickup;
                        ci.deliveryCost = 0;
                        ci.deliveryDepotName = null;
                    }
                }
                ShowCartPopup();
            });
            var delLbl = MakeTMP("Lbl", delGO.transform);
            delLbl.text = LocalizationService.Get(item.deliveryMode == CartDeliveryMode.SelfPickup ? "fleet.cart.delivery.self_pickup" : "fleet.cart.delivery.delivery");
            delLbl.fontSize = 11; delLbl.color = TextPrimary;
            delLbl.alignment = TextAlignmentOptions.Center;
            delLbl.raycastTarget = false; FillRT(delLbl.gameObject);

            // Price
            var priceLbl = MakeTMP("Price", row.transform);
            priceLbl.text = NumberFormatService.FormatCurrency(item.TotalPrice); priceLbl.fontSize = 16;
            priceLbl.fontStyle = FontStyles.Bold; priceLbl.color = PriceColor;
            priceLbl.alignment = TextAlignmentOptions.Right;
            priceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

            // Delete button
            var delItemGO = NewGO("DelItem", row.transform);
            var delItemImage = delItemGO.AddComponent<Image>();
            UITheme.ApplySurface(delItemImage, UITheme.Danger, UIShapePreset.Button);
            var diLE = delItemGO.AddComponent<LayoutElement>();
            diLE.preferredWidth = 30f; diLE.preferredHeight = 30f;
            var diBtn = delItemGO.AddComponent<Button>();
            diBtn.targetGraphic = delItemImage;
            diBtn.colors = UITheme.CreateColorBlock(
                UITheme.Danger,
                UITheme.Darken(UITheme.Danger, -0.05f),
                UITheme.Darken(UITheme.Danger, 0.12f),
                UITheme.Danger,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            int capturedDelId = item.cartId;
            diBtn.onClick.AddListener(() => {
                // If used vehicle — return it to the market
                var removed = FleetService.GetCartItemById(capturedDelId);
                if (removed != null && !removed.isNewVehicle && removed.marketVehicle != null
                    && !_marketVehicles.Contains(removed.marketVehicle))
                {
                    FleetService.AddMarketVehicle(removed.marketVehicle);
                }
                FleetService.RemoveFromCart(c => c.cartId == capturedDelId);
                UpdateCartBadge();
                ShowCartPopup();
            });
            var diLbl = MakeTMP("X", delItemGO.transform);
            diLbl.text = "X"; diLbl.fontSize = 14; diLbl.fontStyle = FontStyles.Bold;
            diLbl.color = TextPrimary; diLbl.alignment = TextAlignmentOptions.Center;
            diLbl.raycastTarget = false; FillRT(diLbl.gameObject);

            row.AddComponent<HoverImageColor>().Init(rowImage, RowBg, RowHover);
        }

        /// <summary>
        /// Typ pojazdu dla wiersza koszyka. Nowy tabor: z vehicleConfiguration
        /// (wagon -> PassengerCar, rodzina -> typ rodziny); uzywany: z marketVehicle.
        /// Zastapilo bezposrednie item.model.type (legacy NewVehicleModel usuniety M-UIPolish 2026-06-18).
        /// </summary>
        private static FleetVehicleType ResolveCartItemType(CartItem item)
        {
            if (!item.isNewVehicle)
                return item.marketVehicle != null ? item.marketVehicle.type : FleetVehicleType.PassengerCar;

            var cfg = item.vehicleConfiguration;
            if (cfg != null && string.IsNullOrEmpty(cfg.bodyTypeId))
            {
                var fam = FleetCatalog.FindFamily(cfg.familyId);
                if (fam != null) return fam.type;
            }
            return FleetVehicleType.PassengerCar; // wagon konfigurowalny / fallback
        }

        /// <summary>Opis (producent, lokalizacja) dla wiersza koszyka.</summary>
        private static string ResolveCartItemDetails(CartItem item)
        {
            if (!item.isNewVehicle)
                return item.marketVehicle != null
                    ? $"{item.marketVehicle.series} | {item.marketVehicle.location}"
                    : "?";

            var fam = item.vehicleConfiguration != null
                ? FleetCatalog.FindFamily(item.vehicleConfiguration.familyId) : null;
            return fam != null ? $"{fam.manufacturer}, {fam.factoryLocation}" : "Pojazd konfigurowalny";
        }

        private void CloseCartPopup()
        {
            if (_cartPopupGO != null) { Destroy(_cartPopupGO); _cartPopupGO = null; }
        }
    }
}
