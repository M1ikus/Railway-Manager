using System.Collections.Generic;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — M-Windows P2: detal POSIADANEGO pojazdu w pływającym oknie
    /// (<see cref="FloatingWindow"/>) zamiast modala z dim-overlay. Okno jest przeciągalne,
    /// odpięte od panelu Tabor (zamknięcie Tabora go nie ubija) i wiele instancji naraz
    /// (różne pojazdy = osobne okna, klucz <c>vehicle:{id}</c>).
    ///
    /// <para>Treść = JEDNA kolumna w ScrollRect (naprawia overflow prawej kolumny z audytu TD-043)
    /// + stały footer „Sprzedaj". Reużywa istniejące section-buildery z FleetPanelUI.DetailPopup.*
    /// (PopupSectionTitle/PopupInfoLine/BuildComponentsSection/BuildPaintSection/
    /// BuildSendActionsSection/BuildMaintenanceHistorySection/BuildInspectionCollapsible/
    /// BuildSeatBreakdownSection) — zero duplikacji logiki.</para>
    ///
    /// <para>Refresh po akcji (mycie/tankowanie/przegląd/malowanie) idzie przez
    /// <c>OnPopupRefreshNeeded</c> → <see cref="RefreshAllOwnedDetailWindows"/>: odświeża WSZYSTKIE
    /// otwarte okna ze świeżych danych, więc poprawne niezależnie które okno wywołało akcję.</para>
    /// </summary>
    public partial class FleetPanelUI
    {
        private const float DetailWindowFooterH = 52f;
        private static readonly Vector2 DetailWindowSize = new Vector2(470f, 640f);

        // Otwarte okna detalu posiadanych pojazdów, klucz = vehicleId (multi-window).
        private readonly Dictionary<int, FloatingWindow> _ownedDetailWindows = new Dictionary<int, FloatingWindow>();

        /// <summary>Static ref ustawiany w Awake — dostęp do otwierania okna pojazdu zewsząd (też z Mapy).</summary>
        public static FleetPanelUI Instance { get; private set; }

        /// <summary>
        /// M-Windows P3: otwiera pływające okno detalu pojazdu po id (cross-scene entry — drill-down
        /// ze składu, klik z Mapy/Depot). Działa nawet gdy panel Tabor jest nieaktywny (okno żyje
        /// w persistentnej warstwie <see cref="WindowManager"/>, nie jako dziecko panelu).
        /// </summary>
        public static void OpenVehicleWindow(int vehicleId)
        {
            if (Instance == null)
            {
                RailwayManager.Core.Log.Warn("[FleetPanelUI] OpenVehicleWindow: brak instancji (scena Depot nie załadowana?)");
                return;
            }
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null)
            {
                RailwayManager.Core.Log.Warn("[FleetPanelUI] OpenVehicleWindow: pojazd #" + vehicleId + " nie znaleziony");
                return;
            }
            Instance.ShowOwnedDetailWindow(v);
        }

        /// <summary>Otwiera (lub fokusuje + odświeża) pływające okno detalu posiadanego pojazdu.</summary>
        private void ShowOwnedDetailWindow(FleetVehicleData v)
        {
            if (v == null) return;
            _currentOwnedDetailVehicle = v;
            _currentMarketDetailVehicle = null;

            bool isNew = !_ownedDetailWindows.ContainsKey(v.id);
            string key = "vehicle:" + v.id;
            string title = !string.IsNullOrEmpty(v.number) ? v.number : v.series;

            var win = WindowManager.Instance.OpenWindow(key, title, DetailWindowSize);
            _ownedDetailWindows[v.id] = win;

            if (isNew)
            {
                int vid = v.id;
                win.OnClosed += () =>
                {
                    _ownedDetailWindows.Remove(vid);
                    if (_currentOwnedDetailVehicle != null && _currentOwnedDetailVehicle.id == vid)
                        _currentOwnedDetailVehicle = null;
                };
            }

            BuildOwnedDetailWindowContent(win, v);
        }

        /// <summary>Odświeża treść wszystkich otwartych okien detalu (po akcji zmieniającej stan pojazdu).</summary>
        private void RefreshAllOwnedDetailWindows()
        {
            if (_ownedDetailWindows.Count == 0) return;
            var snapshot = new List<KeyValuePair<int, FloatingWindow>>(_ownedDetailWindows);
            foreach (var kv in snapshot)
            {
                var win = kv.Value;
                if (win == null) { _ownedDetailWindows.Remove(kv.Key); continue; }
                var fresh = FleetService.GetOwnedById(kv.Key);
                if (fresh == null) { win.Close(); continue; } // pojazd zniknął (sprzedany) → zamknij okno
                BuildOwnedDetailWindowContent(win, fresh);
            }
        }

        private void BuildOwnedDetailWindowContent(FloatingWindow win, FleetVehicleData v)
        {
            var root = win.ContentRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);

            bool isPassenger = PassengerTypes.Contains(v.type);

            // ── Scroll (wypełnia ContentRoot nad footerem) ──
            Transform list = BuildDetailScroll(root, DetailWindowFooterH);

            // ── Sekcje (jedna kolumna; union lewej+prawej kolumny ze starego modala) ──
            PopupSectionTitle(list, "Dane pojazdu");
            PopupInfoLine(list, "V max", $"{v.maxSpeedKmh} km/h");
            if (v.powerKw > 0)
                PopupInfoLine(list, "Moc", $"{v.powerKw} kW");
            PopupInfoLine(list, "Układ osi", v.wheelbase);
            PopupInfoLine(list, "Przebieg", $"{v.mileageKm:N0} km");
            PopupInfoLine(list, "Stan", $"{v.conditionPercent:F0}%", GetConditionColor(v.conditionPercent));
            if (isPassenger)
                BuildSeatBreakdownSection(list, v.passengerSeats, v.seatBreakdown);
            BuildInspectionCollapsible(list, v);

            PopupSectionTitle(list, "Wyposażenie techniczne");
            if (v.voltages != null && v.voltages.Count > 0)
                PopupInfoLine(list, "Napięcia", string.Join(", ", v.voltages));
            else if (v.type == FleetVehicleType.DMU)
                PopupInfoLine(list, "Napęd", "Spalinowy");
            string safety = v.safetySystemsInstalled != null && v.safetySystemsInstalled.Count > 0
                ? string.Join(", ", v.safetySystemsInstalled) : "Brak";
            PopupInfoLine(list, "Bezpieczeństwo", safety, safety == "Brak" ? InspUrgent : TextPrimary);

            PopupSectionTitle(list, "Status operacyjny");
            PopupInfoLine(list, "Status", GetStatusText(v.status), GetStatusColor(v.status));
            PopupInfoLine(list, "Zadanie", v.currentTask ?? "—");
            PopupInfoLine(list, "Skład", v.assignedConsist ?? "—",
                v.assignedConsist != null ? TextAccent : TextMuted);

            BuildComponentsSection(list, v);
            BuildPaintSection(list, v);
            BuildSendActionsSection(list, v);
            BuildMaintenanceHistorySection(list, v);

            if (isPassenger)
            {
                PopupSectionTitle(list, LocalizationService.Get("fleet.detail.seat_preview.title"));
                var seatPrev = NewGO("SeatPreview", list);
                var seatImg = seatPrev.AddComponent<Image>();
                UITheme.ApplySurface(seatImg, InputBg, UIShapePreset.Inset);
                seatPrev.AddComponent<LayoutElement>().preferredHeight = 60f;
                var spLbl = MakeTMP("Lbl", seatPrev.transform);
                spLbl.text = LocalizationService.Get("fleet.detail.seat_preview.placeholder");
                spLbl.fontSize = 12; spLbl.color = TextMuted;
                spLbl.alignment = TextAlignmentOptions.Center;
                spLbl.raycastTarget = false; FillRT(spLbl.gameObject);
            }

            // ── Footer: Sprzedaj (2-step confirm) ──
            BuildDetailSellFooter(win, root, v);
        }

        /// <summary>Pionowy ScrollRect treści okna — deleguje do wspólnego <see cref="WindowScroll"/>
        /// (ten sam idiom co główna lista Tabora).</summary>
        private Transform BuildDetailScroll(Transform parent, float bottomInset)
            => WindowScroll.BuildVertical(parent, 0f, bottomInset);

        /// <summary>Stały footer ze sprzedażą (2-step confirm) — kopia logiki ze starego modala,
        /// ale zamyka OKNO (nie modal) po sukcesie.</summary>
        private void BuildDetailSellFooter(FloatingWindow win, Transform parent, FleetVehicleData v)
        {
            var footer = NewGO("SellFooter", parent);
            var fRT = footer.GetComponent<RectTransform>();
            fRT.anchorMin = new Vector2(0f, 0f); fRT.anchorMax = new Vector2(1f, 0f);
            fRT.pivot = new Vector2(0.5f, 0f);
            fRT.anchoredPosition = Vector2.zero;
            fRT.sizeDelta = new Vector2(0f, DetailWindowFooterH);
            var footerImg = footer.AddComponent<Image>();
            UITheme.ApplySurface(footerImg, TopBarBg, UIShapePreset.Inset);

            var hl = footer.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = UITheme.Spacing.Sm;
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            bool canSell = FleetService.CanSellVehicle(v.id, out string sellBlockReason);
            long resaleZl = FleetResaleMath.ResaleValueGroszy(v) / 100L;

            var sellHint = MakeTMP("SellHint", footer.transform);
            sellHint.fontSize = 12; sellHint.raycastTarget = false;
            sellHint.alignment = TextAlignmentOptions.Right;
            sellHint.color = UITheme.Danger;
            sellHint.text = canSell ? "" : sellBlockReason;
            sellHint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var sellGO = NewGO("SellBtn", footer.transform);
            var sellImage = sellGO.AddComponent<Image>();
            UITheme.ApplySurface(sellImage, UITheme.Danger, UIShapePreset.Pill);
            var sellLE = sellGO.AddComponent<LayoutElement>();
            sellLE.preferredWidth = 180f; sellLE.preferredHeight = 38f;
            var sellBtn = sellGO.AddComponent<Button>();
            sellBtn.targetGraphic = sellImage;
            sellBtn.colors = UITheme.CreateColorBlock(
                UITheme.Danger,
                UITheme.Darken(UITheme.Danger, 0.08f),
                UITheme.Darken(UITheme.Danger, 0.18f),
                UITheme.Danger,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            var sellLbl = MakeTMP("Lbl", sellGO.transform);
            sellLbl.fontSize = 14; sellLbl.fontStyle = FontStyles.Bold; sellLbl.color = TextPrimary;
            sellLbl.alignment = TextAlignmentOptions.Center; sellLbl.raycastTarget = false;
            sellLbl.text = canSell ? $"Sprzedaj (+{resaleZl:N0} zł)" : "Sprzedaj";
            FillRT(sellLbl.gameObject);

            int sellVehId = v.id;
            bool sellArmed = false;
            sellBtn.onClick.AddListener(() =>
            {
                if (!FleetService.CanSellVehicle(sellVehId, out string reCheckReason))
                {
                    sellHint.text = reCheckReason; sellHint.color = UITheme.Danger;
                    sellArmed = false;
                    sellLbl.text = $"Sprzedaj (+{resaleZl:N0} zł)";
                    return;
                }
                if (!sellArmed)
                {
                    sellArmed = true;
                    sellLbl.text = $"Potwierdź: +{resaleZl:N0} zł";
                    sellHint.text = "Kliknij ponownie, aby sprzedać";
                    sellHint.color = UITheme.Warning;
                    return;
                }
                long got = FleetService.SellVehicle(sellVehId, out string saleReason);
                if (got < 0)
                {
                    sellHint.text = saleReason; sellHint.color = UITheme.Danger;
                    sellArmed = false;
                    sellLbl.text = $"Sprzedaj (+{resaleZl:N0} zł)";
                    return;
                }
                win.Close();
                PopulateMyFleet();
            });
        }
    }
}
