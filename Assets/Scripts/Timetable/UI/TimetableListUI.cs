using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Fullscreen lista rozkładów jazdy — główny widok po kliknięciu taba "Rozkłady".
    /// Tabela z kolumnami, filtr statusu, button [+ Nowy] otwiera kreator.
    /// </summary>
    public class TimetableListUI : MonoBehaviour
    {
        public static TimetableListUI Instance { get; private set; }

        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.97f);
        private static readonly Color SectionBg = UITheme.WithAlpha(UITheme.TopBarInset, 0.94f);
        private static readonly Color RowBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f);
        private static readonly Color RowBgMuted = UITheme.WithAlpha(UITheme.PrimarySurface, 0.7f);
        private static readonly Color ScrollBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.36f);
        private static readonly Color CategoriesButtonBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.88f);
        private static readonly Color CreateButtonBg = UITheme.Success;
        private static readonly Color CloseButtonBg = UITheme.Danger;
        private static readonly Color MapButtonBg = UITheme.WithAlpha(UITheme.Success, 0.82f);
        private static readonly Color DuplicateButtonBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.84f);

        private GameObject _panel;
        private GraphicRaycaster _raycaster;
        private Transform _tableContent;
        private TMP_Dropdown _filterDropdown;
        private TimetableStatus? _filterStatus = TimetableStatus.Active;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDestroy()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
            if (Instance == this) Instance = null;
        }

        // === i18n hot-reload (M13-4i) ===
        // Lista jest dynamicznie generated w RefreshTable z hardcoded labels w wierszach
        // — najprostsze: rebuild całego panelu (BuildUI tworzy wszystko od nowa).
        // Akceptujemy że gracz musi reopen panel jeśli zmieni język while open
        // (Update() destroy panelu by łapać ESC).
        private void OnLocaleChanged()
        {
            if (_panel != null && _panel.activeSelf)
                RefreshTable();
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;

            if (!SceneController.TimetablePopupOpen) { Hide(); return; }

            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                Close();
            }
        }

        // ─────────────────────────────────────────────
        //  Show / Hide
        // ─────────────────────────────────────────────

        public void Open()
        {
            Show();
            RefreshTable();
        }

        public void Show()
        {
            if (_panel != null) _panel.SetActive(true);
            if (_raycaster != null) _raycaster.enabled = true;
            SceneController.TimetablePopupOpen = true;
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            if (_raycaster != null) _raycaster.enabled = false;
        }

        public void Close()
        {
            Hide();
            SceneController.TimetablePopupOpen = false;
        }

        // ─────────────────────────────────────────────
        //  Actions
        // ─────────────────────────────────────────────

        private void OnNewClicked()
        {
            Hide();
            if (TimetableCreatorUI.Instance != null)
                TimetableCreatorUI.Instance.Open();
            var sm = RouteBuildStateMachine.Instance;
            if (sm != null) sm.Activate();
        }

        private void OnCategoriesClicked()
        {
            Hide();
            if (CategoryEditorUI.Instance != null)
                CategoryEditorUI.Instance.Open();
            else
                Log.Warn("[TimetableList] CategoryEditorUI not initialized");
        }

        /// <summary>
        /// M-TimetableUX F1.19: Otwiera kreator z reversed pre-fill ze źródłowego rozkładu.
        /// Wywołuje TimetableCreatorUI.OpenReverseTimetable(sourceId, turnaroundMinutes=30)
        /// — A↔B swap, waypoints reversed, start time = arrival + turnaround.
        /// Po Confirm auto-trigger F1.12 circulation suggestion.
        /// </summary>
        private void CreateReverseTimetable(int sourceId)
        {
            if (TimetableCreatorUI.Instance == null)
            {
                Log.Warn($"[TimetableList F1.19] TimetableCreatorUI.Instance null — nie można otworzyć kreatora");
                return;
            }

            // Close current list panel — kreator otworzy się nad nią
            Close();
            TimetableCreatorUI.Instance.OpenReverseTimetable(sourceId);
        }

        /// <summary>
        /// Tworzy kopię rozkładu o podanym ID i dodaje ją do listy. Czas startu
        /// przesuwany o +5 min żeby uniknąć kolizji z oryginałem. Numer pociągu
        /// czyszczony — gracz lub auto-gen ustawi nowy. Rezerwacje peronów/bloków
        /// zostają wykonane na nowym TT.
        /// </summary>
        private void DuplicateTimetable(int sourceId)
        {
            var src = TimetableService.GetTimetable(sourceId);
            if (src == null)
            {
                Log.Warn($"[TimetableList] Duplicate: nie znaleziono rozkładu id={sourceId}");
                return;
            }

            // Deep copy stops
            var newStops = new List<TimetableStop>(src.stops.Count);
            foreach (var s in src.stops)
            {
                newStops.Add(new TimetableStop
                {
                    stationNodeId = s.stationNodeId,
                    stationName = s.stationName,
                    stopType = s.stopType,
                    plannedArrivalSec = s.plannedArrivalSec,
                    plannedDepartureSec = s.plannedDepartureSec,
                    platformId = -1 // re-przypisanie peronów dla nowego slotu
                });
            }

            // ── Walidacja slotowości: znajdź pierwszy wolny slot przesunięty od oryginału ──
            // Próbujemy +5, +10, +15, +20, +30, +45, +60, +90, +120 minut.
            // Jeśli wszystkie kolidują → odmawiamy duplikatu (gracz musi ręcznie).
            var routeForCheck = TimetableService.GetRoute(src.routeId);
            var initForCheck = TimetableInitializer.Instance;
            if (routeForCheck == null || initForCheck == null)
            {
                Log.Warn("[TimetableList] Duplicate: brak Route/Initializer — przerywam");
                return;
            }

            int[] candidateOffsetsMin = { 5, 10, 15, 20, 30, 45, 60, 90, 120 };
            int chosenStartMin = -1;
            foreach (int off in candidateOffsetsMin)
            {
                int candidate = (src.StartMinutes + off) % (24 * 60);
                var collisions = ReservationManager.CheckCollisions(
                    newStops, routeForCheck, initForCheck.Graph, candidate, initForCheck);
                if (collisions == null || collisions.Count == 0)
                {
                    chosenStartMin = candidate;
                    Log.Info($"[TimetableList] Duplicate: znaleziono wolny slot przy +{off} min "
                             + $"({candidate / 60:D2}:{candidate % 60:D2})");
                    break;
                }
            }

            if (chosenStartMin < 0)
            {
                Log.Warn($"[TimetableList] Duplicate #{sourceId}: każdy slot +5..+120 min koliduje "
                         + "z innym rozkładem. Zduplikuj ręcznie z większym przesunięciem czasu.");
                return;
            }

            int newStartMin = chosenStartMin;

            var copy = new Timetable
            {
                name = src.name + " (kopia)",
                routeId = src.routeId,
                commercialCategoryId = src.commercialCategoryId,
                irjCategory = src.irjCategory,
                irjCategoryManualOverride = src.irjCategoryManualOverride,
                trainNumber = "", // gracz lub auto-gen ustawi nowy
                stops = newStops,
                composition = new PlannedComposition
                {
                    assignment = src.composition?.assignment ?? CompositionAssignment.Symbolic,
                    mode = src.composition?.mode ?? CompositionMode.MultipleUnit,
                    maxSpeedKmh = src.composition?.maxSpeedKmh ?? 120,
                    brakeRegime = src.composition?.brakeRegime ?? BrakeRegime.R,
                    symbolicNotation = src.composition?.symbolicNotation
                },
                frequency = FrequencySpec.SingleRun(newStartMin),
                calendar = src.calendar,
                validFromGameTime = src.validFromGameTime,
                validToGameTime = src.validToGameTime,
                startDateIso = src.startDateIso,
                weeksValid = src.weeksValid,
                assignedDepotId = src.assignedDepotId,
                status = TimetableStatus.Active,
                viaStationNames = new List<string>(src.viaStationNames),
                notes = src.notes
            };

            TimetableService.AddTimetable(copy);

            // Rezerwacje dla kopii (nowe TT id, nowy slot czasowy — już zwalidowany powyżej)
            ReservationManager.AutoAssignPlatforms(copy, initForCheck);
            ReservationManager.ReserveForTimetable(copy, routeForCheck, initForCheck.Graph);

            Log.Info($"[TimetableList] Duplicated #{sourceId} → #{copy.id} "
                     + $"({copy.name}, start {newStartMin / 60:D2}:{newStartMin % 60:D2})");
            RefreshTable();
        }

        /// <summary>
        /// Pokazuje trasę rozkładu na MapScene za pomocą RoutePreviewOverlay.
        /// Zamyka listę rozkładów i przełącza scenę.
        /// </summary>
        private void ShowRouteOnMap(int timetableId)
        {
            var tt = TimetableService.GetTimetable(timetableId);
            if (tt == null) return;
            var route = TimetableService.GetRoute(tt.routeId);
            if (route == null)
            {
                Log.Warn($"[TimetableList] Mapa: brak Route dla rozkładu #{timetableId}");
                return;
            }
            Hide();
            SceneController.TimetablePopupOpen = false;
            RoutePreviewOverlay.ShowRouteOnMap(route);
        }

        /// <summary>
        /// Zmiana statusu rozkładu z UI dropdown'a wiersza. Suspended/Archived nie usuwa
        /// rezerwacji — to robi się dopiero przy fizycznym usunięciu rozkładu (osobny
        /// przycisk, TODO). Refresh tabeli żeby kolory się zaktualizowały.
        /// </summary>
        private void OnStatusChanged(int timetableId, int dropdownIndex)
        {
            var tt = TimetableService.GetTimetable(timetableId);
            if (tt == null) return;
            var newStatus = dropdownIndex switch
            {
                0 => TimetableStatus.Active,
                1 => TimetableStatus.Suspended,
                2 => TimetableStatus.Archived,
                _ => tt.status
            };
            if (tt.status == newStatus) return;
            tt.status = newStatus;
            Log.Info($"[TimetableList] Rozkład #{timetableId} status → {newStatus}");
            RefreshTable();
        }

        private void OnFilterChanged(int idx)
        {
            _filterStatus = idx switch
            {
                0 => TimetableStatus.Active,
                1 => TimetableStatus.Suspended,
                2 => TimetableStatus.Archived,
                _ => null // Wszystkie
            };
            RefreshTable();
        }

        // ─────────────────────────────────────────────
        //  Table
        // ─────────────────────────────────────────────

        public void RefreshTable()
        {
            if (_tableContent == null) return;
            foreach (Transform ch in _tableContent) Destroy(ch.gameObject);

            var timetables = TimetableService.Timetables;
            foreach (var tt in timetables)
            {
                try
                {
                if (tt == null) continue;
                if (_filterStatus.HasValue && tt.status != _filterStatus.Value) continue;

                var route = TimetableService.GetRoute(tt.routeId);
                var cat = TimetableService.GetCommercialCategory(tt.commercialCategoryId);

                string nr = string.IsNullOrEmpty(tt.trainNumber) ? "—" : tt.trainNumber;
                string trasa = tt.RouteDisplayName ?? tt.name ?? "?";
                string katR = IrjCategoryCatalog.GetCode(tt.irjCategory);
                string katH = cat?.shortCode ?? "?";
                string start = FmtHHMM(tt.StartMinutes);
                string end = FmtHHMM(tt.EndMinutes);
                int jazdaMin = tt.stops != null ? tt.DrivingTimeSec / 60 : 0;
                int postojeMin = tt.stops != null ? tt.TotalStopTimeSec / 60 : 0;
                string tabor = tt.composition != null
                    ? (tt.composition.mode == CompositionMode.MultipleUnit
                        ? LocalizationService.Get("timetable.list.composition.emu")
                        : LocalizationService.Get("timetable.list.composition.loco"))
                    : LocalizationService.Get("timetable.list.composition.unknown");
                int vmax = tt.composition?.maxSpeedKmh ?? 0;

                // Status pokazywany w dropdown'ie po prawej, więc usunięty z linii

                var row = new GameObject("Row");
                row.transform.SetParent(_tableContent, false);
                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 58;
                rowLe.flexibleHeight = 0;
                var rowImg = row.AddComponent<Image>();
                UITheme.ApplySurface(
                    rowImg,
                    tt.status == TimetableStatus.Active ? RowBg : RowBgMuted,
                    UIShapePreset.Inset);

                // HorizontalLayoutGroup: text (flex) + przyciski akcji
                var rowH = row.AddComponent<HorizontalLayoutGroup>();
                rowH.spacing = UITheme.Spacing.Sm;
                rowH.padding = UITheme.Padding(UITheme.Spacing.Sm);
                rowH.childForceExpandWidth = false;
                rowH.childForceExpandHeight = true;
                rowH.childAlignment = TextAnchor.MiddleLeft;

                // Tekst rozkładu (rozciąga się)
                var infoCol = MakeColumn(row.transform, 1f, 4);

                var titleRow = MakeInlineRow(infoCol.transform, UITheme.Spacing.Sm, new RectOffset(0, 0, 0, 0), 20);
                var numberBadge = MakeBadge(titleRow.transform, nr, UITheme.PrimaryAccent, 60, UIThemeTextRole.Inverse);
                numberBadge.fontStyle = FontStyles.Bold;

                var routeText = Lbl(
                    titleRow.transform,
                    trasa,
                    12,
                    tt.status == TimetableStatus.Active ? UITheme.PrimaryText : UITheme.DisabledText);
                routeText.fontStyle = tt.status == TimetableStatus.Active ? FontStyles.Bold : FontStyles.Italic;
                routeText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;

                MakeBadge(titleRow.transform, $"{katR} / {katH}", UITheme.WithAlpha(UITheme.Border, 0.72f), 72, UIThemeTextRole.Primary);

                var metaRow = MakeInlineRow(infoCol.transform, UITheme.Spacing.Md, new RectOffset(0, 0, 0, 0), 16);
                var timeText = Lbl(metaRow.transform, $"{start} - {end}", 10, UITheme.SecondaryText);
                timeText.fontStyle = FontStyles.Bold;
                Lbl(metaRow.transform, $"{jazdaMin} min jazdy", 10, UITheme.SecondaryText);
                Lbl(metaRow.transform, $"{postojeMin} min postoju", 10, UITheme.SecondaryText);
                Lbl(metaRow.transform, $"{tabor} {vmax} km/h", 10, UITheme.SecondaryText);

                var actionCol = MakeColumn(row.transform, 0f, 5);
                actionCol.GetComponent<LayoutElement>().preferredWidth = 186;

                var statusWrap = MakeInlineRow(actionCol.transform, 0, new RectOffset(0, 0, 0, 0), 22);
                statusWrap.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;

                // Status dropdown (Active/Suspended/Archived)
                int captIdStatus = tt.id;
                var statusDd = MakeStatusDropdown(statusWrap.transform, tt.status);
                statusDd.onValueChanged.AddListener(v => OnStatusChanged(captIdStatus, v));

                var actionsRow = MakeInlineRow(actionCol.transform, UITheme.Spacing.Sm, new RectOffset(0, 0, 0, 0), 24);

                // Przycisk Mapa (podglad trasy)
                int captIdMap = tt.id;
                Btn(actionsRow.transform, LocalizationService.Get("timetable.list.row_action.map"), () => ShowRouteOnMap(captIdMap),
                    MapButtonBg, 78);

                // Przycisk Duplikuj
                int captTtId = tt.id;
                Btn(actionsRow.transform, LocalizationService.Get("timetable.list.row_action.duplicate"), () => DuplicateTimetable(captTtId),
                    DuplicateButtonBg, 96);

                // M-TimetableUX F1.19: Przycisk "Stwórz powrotny" — wywołuje
                // TimetableCreatorUI.OpenReverseTimetable z auto-trigger F1.12 circulation suggestion.
                int captTtIdReverse = tt.id;
                Btn(actionsRow.transform, "Powrotny", () => CreateReverseTimetable(captTtIdReverse),
                    UITheme.WithAlpha(UITheme.PrimaryAccent, 0.6f), 78);
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[TimetableListUI] RefreshTable error for tt#{tt?.id}: {ex}");
                }
            }

            // Pusta lista
            if (_tableContent.childCount == 0)
            {
                var empty = new GameObject("Empty");
                empty.transform.SetParent(_tableContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 52;
                var emptyBg = empty.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);

                var emptyTextObj = new GameObject("EmptyText");
                emptyTextObj.transform.SetParent(empty.transform, false);
                var emptyTextRt = emptyTextObj.AddComponent<RectTransform>();
                emptyTextRt.anchorMin = Vector2.zero;
                emptyTextRt.anchorMax = Vector2.one;
                emptyTextRt.offsetMin = Vector2.zero;
                emptyTextRt.offsetMax = Vector2.zero;
                var t = emptyTextObj.AddComponent<TextMeshProUGUI>();
                UITheme.ApplyTmpText(t, UIThemeTextRole.Secondary);
                t.fontSize = 13;
                t.alignment = TextAlignmentOptions.Center;
                t.text = LocalizationService.Get("timetable.list.empty");
            }
        }

        static string FmtHHMM(int minutes)
        {
            int h = (minutes / 60) % 24;
            int m = minutes % 60;
            return $"{h:D2}:{m:D2}";
        }

        // ─────────────────────────────────────────────
        //  Build UI
        // ─────────────────────────────────────────────

        public void BuildUI(Transform canvas)
        {
            _raycaster = canvas.GetComponent<GraphicRaycaster>();

            _panel = new GameObject("TimetableList");
            _panel.transform.SetParent(canvas, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(75, 0);
            prt.offsetMax = new Vector2(0, -42);
            var panelImg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, PanelBg, UIShapePreset.PanelLarge);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Header (stała wysokość, nie rozciąga się)
            var header = MakeRow(_panel.transform, 30);
            var headerImg = header.AddComponent<Image>();
            UITheme.ApplySurface(headerImg, SectionBg, UIShapePreset.Panel);
            header.AddComponent<LayoutElement>().flexibleHeight = 0;
            var titleCol = MakeColumn(header.transform, 1f, 2);
            var titleText = Lbl(titleCol.transform, LocalizationService.Get("timetable.list.title"), 16, UITheme.PrimaryText);
            titleText.fontStyle = FontStyles.Bold;
            Lbl(titleCol.transform, LocalizationService.Get("timetable.list.filter.active"), 10, UITheme.SecondaryText);
            var spacer = new GameObject("Sp");
            spacer.transform.SetParent(header.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            Btn(header.transform, LocalizationService.Get("timetable.list.button.categories"), OnCategoriesClicked, CategoriesButtonBg, 110);
            Btn(header.transform, LocalizationService.Get("timetable.list.button.new"),       OnNewClicked,        CreateButtonBg, 80);
            Btn(header.transform, LocalizationService.Get("timetable.list.button.close"),     Close,               CloseButtonBg, 30);

            Sep(_panel.transform);

            // Filter (stała wysokość)
            var filterRow = MakeRow(_panel.transform, 24);
            var filterImg = filterRow.AddComponent<Image>();
            UITheme.ApplySurface(filterImg, SectionBg, UIShapePreset.Inset);
            filterRow.AddComponent<LayoutElement>().flexibleHeight = 0;
            Lbl(filterRow.transform, LocalizationService.Get("timetable.list.filter_label"), 11, UITheme.SecondaryText);
            _filterDropdown = MakeFilterDropdown(filterRow.transform);

            Sep(_panel.transform);

            // Column header (stała wysokość)
            var colHeader = new GameObject("ColHdr");
            colHeader.transform.SetParent(_panel.transform, false);
            var colLe = colHeader.AddComponent<LayoutElement>();
            colLe.preferredHeight = 24;
            colLe.flexibleHeight = 0;
            var colHeaderBg = colHeader.AddComponent<Image>();
            UITheme.ApplySurface(colHeaderBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.78f), UIShapePreset.Inset);

            var colTextObj = new GameObject("ColText");
            colTextObj.transform.SetParent(colHeader.transform, false);
            var colTextRt = colTextObj.AddComponent<RectTransform>();
            colTextRt.anchorMin = Vector2.zero;
            colTextRt.anchorMax = Vector2.one;
            colTextRt.offsetMin = new Vector2(8, 0);
            colTextRt.offsetMax = new Vector2(-8, 0);
            var colTxt = colTextObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(colTxt, UIThemeTextRole.Secondary);
            colTxt.fontSize = 10;
            colTxt.text = LocalizationService.Get("timetable.list.column_header");
            colTxt.alignment = TextAlignmentOptions.MidlineLeft;

            // Table content — ScrollRect z stałymi rozmiarami wierszy
            var scrollView = new GameObject("TableScrollView", typeof(RectTransform));
            scrollView.transform.SetParent(_panel.transform, false);
            var svLe = scrollView.AddComponent<LayoutElement>();
            svLe.flexibleHeight = 1;
            svLe.minHeight = 100;
            var svImg = scrollView.AddComponent<Image>();
            UITheme.ApplySurface(svImg, ScrollBg, UIShapePreset.Panel);
            svImg.raycastTarget = true;
            var scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 25f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollView.transform, false);
            var vpRt = (RectTransform)viewport.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(6, 6);
            vpRt.offsetMax = new Vector2(-6, -6);
            var vpImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(vpImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = Vector2.zero;
            var cVlg = content.AddComponent<VerticalLayoutGroup>();
            cVlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            cVlg.spacing = UITheme.Spacing.Xs;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;
            cVlg.childAlignment = TextAnchor.UpperLeft;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = vpRt;
            scrollRect.content = cRt;
            _tableContent = content.transform;

            _panel.SetActive(false);
        }

        // ─── UI helpers ──────────────────────────

        TextMeshProUGUI Lbl(Transform p, string t, int sz, Color c)
        {
            var o = new GameObject("L"); o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = sz + 6;
            var tx = o.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);
            tx.fontSize = sz; tx.color = c; tx.text = t;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            return tx;
        }

        GameObject MakeRow(Transform p, float h = 26)
        {
            var o = new GameObject("R"); o.transform.SetParent(p, false);
            o.AddComponent<RectTransform>();
            var hlg = o.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Sm; hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            o.AddComponent<LayoutElement>().preferredHeight = h;
            return o;
        }

        GameObject MakeColumn(Transform p, float flexibleWidth = 0f, int spacing = 3)
        {
            var o = new GameObject("Col");
            o.transform.SetParent(p, false);
            o.AddComponent<RectTransform>();
            var le = o.AddComponent<LayoutElement>();
            le.flexibleWidth = flexibleWidth;
            var vlg = o.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return o;
        }

        GameObject MakeInlineRow(Transform p, float spacing, RectOffset padding, float preferredHeight = 18)
        {
            var o = new GameObject("InlineRow");
            o.transform.SetParent(p, false);
            o.AddComponent<RectTransform>();
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            var hlg = o.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.padding = padding;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            return o;
        }

        TextMeshProUGUI MakeBadge(Transform p, string text, Color bg, float width, UIThemeTextRole role)
        {
            var o = new GameObject("Badge");
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 20;
            var img = o.AddComponent<Image>();
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);

            // Text musi być na osobnym GO — Image i Text obie dziedziczą po Graphic,
            // a [DisallowMultipleComponent] sprawia że drugi AddComponent<Graphic>()
            // zwraca null. Patrz commit 2a3907e.
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(o.transform, false);
            var lblRt = labelObj.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            var label = labelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(label, role);
            label.fontSize = 10;
            label.alignment = TextAlignmentOptions.Center;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        void Sep(Transform p)
        {
            var o = new GameObject("S"); o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = 1;
            o.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.45f);
        }

        Button Btn(Transform p, string label, System.Action onClick, Color bg, float w = -1)
        {
            var o = new GameObject(label); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;
            le.preferredHeight = 28;
            var img = o.AddComponent<Image>();
            UITheme.ApplySurface(img, bg, UITheme.Darken(bg, 0.05f) != bg ? UIShapePreset.Pill : UIShapePreset.Button);
            var btn = o.AddComponent<Button>(); btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                bg,
                UITheme.Darken(bg, 0.05f),
                UITheme.Darken(bg, 0.12f),
                bg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() => onClick?.Invoke());
            var l = new GameObject("L"); l.transform.SetParent(o.transform, false);
            var lrt = l.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var tx = l.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);
            tx.fontSize = 12; tx.alignment = TextAlignmentOptions.Center;
            tx.text = label;
            return btn;
        }

        TMP_Dropdown MakeFilterDropdown(Transform p)
        {
            var o = new GameObject("Filter"); o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredWidth = 120;
            var filterImg = o.AddComponent<Image>();
            UITheme.ApplySurface(filterImg, UITheme.SecondarySurface, UIShapePreset.Inset);

            var cap = new GameObject("Cap"); cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(4, 0); crt.offsetMax = new Vector2(-16, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);
            ctx.fontSize = 12;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;

            var tmpl = new GameObject("Tmpl"); tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f); trt.sizeDelta = new Vector2(0, 80);
            var tmplImg = tmpl.AddComponent<Image>();
            UITheme.ApplySurface(tmplImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.98f), UIShapePreset.Inset);
            tmpl.SetActive(false);

            var item = new GameObject("Item"); item.transform.SetParent(tmpl.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemToggle = item.AddComponent<Toggle>();
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Button);
            itemToggle.targetGraphic = itemBg;

            var ilbl = new GameObject("IL"); ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0); ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);
            iltx.fontSize = 12;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx; dd.itemText = iltx; dd.template = trt;
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new(LocalizationService.Get("timetable.list.filter.active")),
                new(LocalizationService.Get("timetable.list.filter.suspended")),
                new(LocalizationService.Get("timetable.list.filter.archived")),
                new(LocalizationService.Get("timetable.list.filter.all"))
            };
            dd.onValueChanged.AddListener(OnFilterChanged);
            return dd;
        }

        /// <summary>
        /// Mały dropdown statusu w wierszu tabeli (Aktywny / Wstrzymany / Archiwalny).
        /// Inicjalna wartość ustawiona na obecny status rozkładu.
        /// </summary>
        TMP_Dropdown MakeStatusDropdown(Transform p, TimetableStatus initial)
        {
            var o = new GameObject("StatusDD"); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 100;
            le.preferredHeight = 20;
            var statusBg = o.AddComponent<Image>();
            UITheme.ApplySurface(statusBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.9f), UIShapePreset.Inset);

            var cap = new GameObject("Cap"); cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 0); crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);
            ctx.fontSize = 10;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;

            var tmpl = new GameObject("Tmpl"); tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f); trt.sizeDelta = new Vector2(0, 70);
            var statusTmplImg = tmpl.AddComponent<Image>();
            UITheme.ApplySurface(statusTmplImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.98f), UIShapePreset.Inset);
            tmpl.SetActive(false);

            var item = new GameObject("Item"); item.transform.SetParent(tmpl.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemToggle = item.AddComponent<Toggle>();
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Button);
            itemToggle.targetGraphic = itemBg;

            var ilbl = new GameObject("IL"); ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0); ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);
            iltx.fontSize = 10;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx; dd.itemText = iltx; dd.template = trt;
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new(LocalizationService.Get("timetable.list.row_status.active")),
                new(LocalizationService.Get("timetable.list.row_status.suspended")),
                new(LocalizationService.Get("timetable.list.row_status.archived"))
            };
            dd.value = initial switch
            {
                TimetableStatus.Active => 0,
                TimetableStatus.Suspended => 1,
                TimetableStatus.Archived => 2,
                _ => 0
            };
            dd.RefreshShownValue();
            return dd;
        }
    }
}
