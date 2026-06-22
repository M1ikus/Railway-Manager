using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using RailwayManager.Core;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
// 2026-05-10: usunięte `using RailwayManager.SaveLoad;` — cyclic dependency fix.
// MainMenu nie referuje SaveLoad asmdef. Hook pattern via SaveActionsHook (Core) z DTO mapping.

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "Załaduj grę" z listą zapisów.
    /// Tworzony proceduralnie jako dziecko istniejącego Canvas.
    ///
    /// TD-021 (2026-05-08): integracja z SaveOrchestrator. Wcześniej hardkodowana mock data
    /// (6 fake save'ów) + klik tylko Log.Info. Teraz: query SaveActionsHook.EnumerateSavesAsync (Core hook)
    /// przy Show, klik → SaveActionsHook.LoadSaveByIdAsync + scene transition do Depot. 2026-05-10:
    /// hook pattern (cyclic dep fix) — MainMenu nie może referować SaveLoad bo Depot → MainMenu existing.
    /// </summary>
    public class LoadGameScreenUI : MonoBehaviour, IMenuScreen
    {
        private struct SaveEntryData
        {
            public string slotId;       // TD-021: real slot identifier dla LoadAsync
            public string saveName;
            public string dateTime;
            public string playTime;
            public bool isAutosave;
        }

        private GameObject root;
        private RectTransform contentRT;
        private Transform contentParent;
        private bool showAutosaves;
        private Toggle autosaveToggle;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI toggleLabel;
        private TextMeshProUGUI sectionLastLabel;
        private TextMeshProUGUI sectionOtherLabel;
        private TextMeshProUGUI backButtonText;

        private readonly List<GameObject> entryRows = new();
        private readonly List<SaveEntryData> allSaves = new();
        private bool isLoading;            // TD-021: blokuje multi-click podczas LoadAsync
        private bool isRefreshing;         // TD-021: ListAsync w toku
        private GameObject statusLabelGO;  // TD-021: "Wczytywanie listy..." / "Brak zapisów"

        // Colors
        private static readonly Color TopBarBg = UITheme.OverlayPanelStrong;
        private static readonly Color RowBg = UITheme.OverlayPanel;
        private static readonly Color RowHoverBg = UITheme.OverlayPanelStrong;
        private static readonly Color TextPrimary = UITheme.PrimaryText;
        private static readonly Color TextSecondary = UITheme.SecondaryText;
        private static readonly Color AccentColor = UITheme.PrimaryAccent;
        private static readonly Color AutosaveTag = UITheme.Focus;

        private const int EntryNameSize = 24;
        private const int EntryDetailSize = 18;
        private const int SectionHeaderSize = 20;

        public System.Action OnBack;

        public void Build(Transform canvasTransform)
        {
            // TD-021: brak PopulateMockData — RefreshSavesAsync() przy Show
            BuildRoot(canvasTransform);
            BuildTopBar();
            BuildScrollArea();
            BuildBottomBar();
            root.SetActive(false);
        }

        public void Show()
        {
            root.SetActive(true);
            RefreshLanguage();
            // TD-021: fire-and-forget async refresh — UI zostaje responsive, status label pokazuje progres
            _ = RefreshSavesAsync();
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        public bool IsVisible => root != null && root.activeSelf;

        public void RefreshLanguage()
        {
            if (titleLabel != null)        titleLabel.text        = LocalizationService.Get("load_game.title");
            if (toggleLabel != null)       toggleLabel.text       = LocalizationService.Get("load_game.toggle.show_autosaves");
            if (sectionLastLabel != null)  sectionLastLabel.text  = LocalizationService.Get("load_game.section.last_game");
            if (sectionOtherLabel != null) sectionOtherLabel.text = LocalizationService.Get("load_game.section.other_saves");
            if (backButtonText != null)    backButtonText.text    = "\u2190";
        }

        // === i18n hot-reload (M13-4b) ===

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += RefreshLanguage;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= RefreshLanguage;
        }

        // ─────────────────────────────────────────────
        //  TD-021: SAVE ENUMERATION via SaveOrchestrator
        // ─────────────────────────────────────────────

        /// <summary>
        /// Query SaveActionsHook.EnumerateSavesAsync() i mapuje SaveSlotSummary → SaveEntryData.
        /// Sortowanie po SavedAt desc (storage gwarantuje per ISaveStorage.ListAsync contract).
        /// Auto-save detection: SaveType ∈ {"auto", "quick", "exit"}.
        /// 2026-05-10: hook pattern (cyclic dep fix) — SaveSlotSummary z Core, NIE SaveSlotInfo z SaveLoad.
        /// </summary>
        private async Task RefreshSavesAsync()
        {
            if (isRefreshing) return;
            isRefreshing = true;
            try
            {
                allSaves.Clear();
                ClearEntries();
                ShowStatusLabel("load_game.status.loading");

                if (SaveActionsHook.EnumerateSavesAsync == null)
                {
                    Log.Warn("[LoadGameScreenUI] SaveActionsHook.EnumerateSavesAsync = null — SaveLoad bootstrap niezarejestrowany");
                    ShowStatusLabel("load_game.status.error");
                    return;
                }

                List<SaveSlotSummary> list = null;
                try
                {
                    list = await SaveActionsHook.EnumerateSavesAsync.Invoke();
                }
                catch (Exception e)
                {
                    Log.Error($"[LoadGameScreenUI] EnumerateSavesAsync hook threw: {e.Message}");
                    ShowStatusLabel("load_game.status.error");
                    return;
                }

                if (this == null || root == null) return; // panel destroyed during await

                if (list == null || list.Count == 0)
                {
                    ShowStatusLabel("load_game.status.empty");
                    return;
                }

                foreach (var slot in list)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.SlotId)) continue;
                    allSaves.Add(new SaveEntryData
                    {
                        slotId     = slot.SlotId,
                        saveName   = string.IsNullOrEmpty(slot.SlotName) ? slot.SlotId : slot.SlotName,
                        dateTime   = FormatSavedAt(slot.SavedAt),
                        playTime   = FormatPlaytime(slot.Playtime),
                        isAutosave = IsAutoSaveType(slot.SaveType)
                    });
                }

                HideStatusLabel();
                PopulateEntries();
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
            }
            finally
            {
                isRefreshing = false;
            }
        }

        /// <summary>UTC ISO "2026-03-28T14:30:00Z" → local "2026-03-28 14:30". Fallback raw on parse failure.</summary>
        private static string FormatSavedAt(string isoUtc)
        {
            if (string.IsNullOrEmpty(isoUtc)) return "?";
            if (DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                  out var utc))
            {
                return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            return isoUtc;
        }

        /// <summary>Playtime sekundy → "2h 15min" lub "45min" gdy &lt; 1h.</summary>
        private static string FormatPlaytime(double playtimeSec)
        {
            if (playtimeSec < 0) playtimeSec = 0;
            int totalMin = (int)(playtimeSec / 60.0);
            int hours = totalMin / 60;
            int mins = totalMin % 60;
            if (hours <= 0) return $"{mins}min";
            return $"{hours}h {mins:D2}min";
        }

        /// <summary>SaveType ∈ {"auto", "quick", "exit"} → autosave. "manual" → false.</summary>
        private static bool IsAutoSaveType(string saveType)
        {
            if (string.IsNullOrEmpty(saveType)) return false;
            return saveType == "auto" || saveType == "quick" || saveType == "exit";
        }

        /// <summary>Status label (Loading / Empty / Error) jako single TMPText w content area.</summary>
        private void ShowStatusLabel(string i18nKey)
        {
            HideStatusLabel();
            statusLabelGO = new GameObject("StatusLabel");
            statusLabelGO.transform.SetParent(contentParent, false);
            var rt = statusLabelGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 100f);
            statusLabelGO.AddComponent<LayoutElement>().preferredHeight = 100f;
            var tmp = MenuScreenPrimitives.CreateTMP("Label", statusLabelGO.transform).GetComponent<TextMeshProUGUI>();
            tmp.text = LocalizationService.Get(i18nKey);
            tmp.fontSize = EntryNameSize;
            tmp.color = TextSecondary;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            FillRect(tmp.gameObject);
            entryRows.Add(statusLabelGO);
        }

        private void HideStatusLabel()
        {
            if (statusLabelGO != null)
            {
                Destroy(statusLabelGO);
                statusLabelGO = null;
            }
        }

        // ─────────────────────────────────────────────
        //  TD-021: LOAD HANDLER
        // ─────────────────────────────────────────────

        /// <summary>Klik save → LoadAsync hook + transition do Depot. Toast on failure.
        /// 2026-05-10: hook pattern (cyclic dep fix) — LoadOutcome z Core, NIE LoadStatus z SaveLoad.</summary>
        private async Task OnLoadSaveAsync(string slotId)
        {
            if (isLoading) return;
            if (string.IsNullOrEmpty(slotId))
            {
                Log.Warn("[LoadGameScreenUI] OnLoadSaveAsync: slotId empty");
                return;
            }
            if (SaveActionsHook.LoadSaveByIdAsync == null)
            {
                Log.Error("[LoadGameScreenUI] SaveActionsHook.LoadSaveByIdAsync = null — bootstrap problem");
                return;
            }

            isLoading = true;
            SaveActionsHook.ShowLoadingScreen?.Invoke(LocalizationService.Get("save_load.busy_load"));

            LoadOutcome outcome = LoadOutcome.Failed;
            try
            {
                outcome = await SaveActionsHook.LoadSaveByIdAsync.Invoke(slotId, false);
            }
            catch (Exception e)
            {
                Log.Error($"[LoadGameScreenUI] LoadSaveByIdAsync hook threw: {e.Message}");
            }
            finally
            {
                SaveActionsHook.HideLoadingScreen?.Invoke();
                isLoading = false;
            }

            switch (outcome)
            {
                case LoadOutcome.Success:
                case LoadOutcome.PartialLoad:
                    // Stan zrestorowany do statycznych services (Fleet/Timetable/Personnel/...).
                    // Transition do Depot — scene Awake/Start nie reset'uje state bo SaveLoad
                    // singletony są DontDestroyOnLoad.
                    Log.Info($"[LoadGameScreenUI] Load '{slotId}' OK ({outcome}). Loading Depot scene...");
                    SceneManager.LoadScene("Depot");
                    break;
                case LoadOutcome.NotFound:
                    Log.Warn($"[LoadGameScreenUI] Save '{slotId}' nie istnieje (zniknął między enumerate a load?)");
                    _ = RefreshSavesAsync(); // refresh listy
                    break;
                case LoadOutcome.ModifiedSave:
                    Log.Warn($"[LoadGameScreenUI] Save '{slotId}' ModifiedSave (HMAC mismatch). " +
                             "MainMenu UI nie ma confirm modal'a — dla teraz ignorujemy. " +
                             "TODO: spinner confirm popup '/' load mimo to.");
                    break;
                case LoadOutcome.NewerVersion:
                    Log.Warn($"[LoadGameScreenUI] Save '{slotId}' NewerVersion (save z nowszej wersji gry)");
                    break;
                case LoadOutcome.Failed:
                default:
                    Log.Error($"[LoadGameScreenUI] Save '{slotId}' Load failed");
                    break;
            }
        }

        // ─────────────────────────────────────────────
        //  ROOT PANEL (fullscreen)
        // ─────────────────────────────────────────────

        private void BuildRoot(Transform parent)
        {
            root = MenuScreenPrimitives.CreateFullscreenRoot("LoadGameScreen", parent);
        }

        // ─────────────────────────────────────────────
        //  TOP BAR
        // ─────────────────────────────────────────────

        private void BuildTopBar()
        {
            MenuScreenPrimitives.CreateTopBar("TopBar", root.transform, () => OnBack?.Invoke(), out backButtonText, out titleLabel);
        }

        // ─────────────────────────────────────────────
        //  SCROLL AREA
        // ─────────────────────────────────────────────

        private void BuildScrollArea()
        {
            MenuScreenPrimitives.BuildVerticalScrollArea(
                root.transform,
                offsetMin: new Vector2(40f, 60f),
                offsetMax: new Vector2(-40f, -80f),
                contentPadding: UITheme.Padding(0f, UITheme.Spacing.Md),
                contentSpacing: UITheme.Spacing.Sm,
                contentAlignment: TextAnchor.UpperCenter,
                out contentRT);
            contentParent = contentRT.transform;
        }

        // ─────────────────────────────────────────────
        //  BOTTOM BAR (autosave toggle)
        // ─────────────────────────────────────────────

        private void BuildBottomBar()
        {
            var bar = new GameObject("BottomBar");
            bar.transform.SetParent(root.transform, false);
            UITheme.ApplySurface(bar.AddComponent<Image>(), TopBarBg, UIShapePreset.PanelLarge);

            var barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0f);
            barRT.anchorMax = new Vector2(1f, 0f);
            barRT.pivot = new Vector2(0.5f, 0f);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0f, 50f);

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(50, 20, 5, 5);
            layout.spacing = UITheme.Spacing.Md;
            layout.childAlignment = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern).
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // Toggle
            var toggleObj = CreateToggle("AutosaveToggle", bar.transform);
            autosaveToggle = toggleObj.GetComponent<Toggle>();
            autosaveToggle.isOn = false;
            autosaveToggle.onValueChanged.AddListener(OnAutosaveToggleChanged);

            // Label
            var labelShell = new GameObject("ToggleShell");
            labelShell.transform.SetParent(bar.transform, false);
            labelShell.AddComponent<RectTransform>().sizeDelta = new Vector2(320f, 34f);
            UITheme.ApplySurface(labelShell.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
            var labelShellLE = labelShell.AddComponent<LayoutElement>();
            labelShellLE.preferredWidth = 320f;
            labelShellLE.preferredHeight = 34f;
            var labelLayout = labelShell.AddComponent<HorizontalLayoutGroup>();
            labelLayout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            labelLayout.childAlignment = TextAnchor.MiddleLeft;
            labelLayout.childControlWidth = true;
            labelLayout.childControlHeight = true;

            var labelObj = CreateTMPText("ToggleLabel", labelShell.transform);
            toggleLabel = labelObj.GetComponent<TextMeshProUGUI>();
            toggleLabel.fontSize = EntryDetailSize;
            toggleLabel.color = TextSecondary;

            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.preferredWidth = 280f;
            labelLE.preferredHeight = 22f;
        }

        // ─────────────────────────────────────────────
        //  POPULATE ENTRIES
        // ─────────────────────────────────────────────

        private void PopulateEntries()
        {
            ClearEntries();

            // Section: Last game (first entry)
            var lastHeader = CreateSectionHeader("LastGameHeader");
            sectionLastLabel = lastHeader.GetComponentInChildren<TextMeshProUGUI>();
            entryRows.Add(lastHeader);

            if (allSaves.Count > 0)
            {
                var firstSave = allSaves[0];
                if (!firstSave.isAutosave || showAutosaves)
                {
                    var row = CreateSaveRow(firstSave, 0);
                    entryRows.Add(row);
                }
            }

            // Spacer
            var spacer = CreateSpacer(20f);
            entryRows.Add(spacer);

            // Section: Other saves
            var otherHeader = CreateSectionHeader("OtherSavesHeader");
            sectionOtherLabel = otherHeader.GetComponentInChildren<TextMeshProUGUI>();
            entryRows.Add(otherHeader);

            for (int i = 1; i < allSaves.Count; i++)
            {
                var save = allSaves[i];
                if (save.isAutosave && !showAutosaves) continue;
                var row = CreateSaveRow(save, i);
                entryRows.Add(row);
            }
        }

        private void ClearEntries()
        {
            foreach (var row in entryRows)
            {
                if (row != null)
                    Destroy(row);
            }
            entryRows.Clear();
        }

        private void OnAutosaveToggleChanged(bool value)
        {
            showAutosaves = value;
            PopulateEntries();
            RefreshLanguage();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
        }

        // ─────────────────────────────────────────────
        //  SECTION HEADER
        // ─────────────────────────────────────────────

        private GameObject CreateSectionHeader(string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(contentParent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 40f);
            UITheme.ApplySurface(obj.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.8f), UIShapePreset.Panel);
            obj.AddComponent<LayoutElement>().preferredHeight = 40f;
            var layout = obj.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var textObj = CreateTMPText("Label", obj.transform);
            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = SectionHeaderSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = AccentColor;
            tmp.alignment = TextAlignmentOptions.Left;

            textObj.AddComponent<LayoutElement>().preferredHeight = 22f;

            return obj;
        }

        // ─────────────────────────────────────────────
        //  SAVE ROW
        // ─────────────────────────────────────────────

        private GameObject CreateSaveRow(SaveEntryData data, int index)
        {
            var row = new GameObject($"SaveRow_{index}");
            row.transform.SetParent(contentParent, false);

            var rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0f, 88f);

            // Background
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Panel);

            // Button
            var btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = rowImg;
            // TD-021: capture slot data dla LoadAsync handler
            var capturedSlotId = data.slotId;
            var capturedSaveName = data.saveName;
            btn.onClick.AddListener(() =>
            {
                Log.Info($"[LoadGame] Wybrano save '{capturedSaveName}' (slot '{capturedSlotId}')");
                _ = OnLoadSaveAsync(capturedSlotId);
            });

            // Hover
            var hover = row.AddComponent<HoverImageColor>();
            hover.Init(rowImg, RowBg, RowHoverBg);

            // Layout
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            layout.spacing = UITheme.Spacing.Lg;
            layout.childAlignment = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern). Bez tego infoCol+actions
            // ignorowali LayoutElement.preferredWidth (470px) → save row layout pomieszany.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            // Info column
            var infoCol = new GameObject("Info");
            infoCol.transform.SetParent(row.transform, false);
            var infoLayout = infoCol.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = UITheme.Spacing.Sm;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = false;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            var infoLE = infoCol.AddComponent<LayoutElement>();
            infoLE.preferredWidth = 470f;
            infoLE.preferredHeight = 62f;

            // Name
            var nameObj = CreateTMPText("Name", infoCol.transform);
            var nameTmp = nameObj.GetComponent<TextMeshProUGUI>();
            nameTmp.text = data.saveName;
            nameTmp.fontSize = EntryNameSize;
            nameTmp.color = TextPrimary;
            nameTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            nameTmp.overflowMode = TextOverflowModes.Ellipsis;
            nameTmp.raycastTarget = false;
            var nameLE = nameObj.AddComponent<LayoutElement>();
            nameLE.preferredWidth = 470f;
            nameLE.preferredHeight = 28f;

            var metaRow = new GameObject("Meta");
            metaRow.transform.SetParent(infoCol.transform, false);
            var metaLayout = metaRow.AddComponent<HorizontalLayoutGroup>();
            metaLayout.spacing = UITheme.Spacing.Md;
            metaLayout.childAlignment = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth/Height true (TD-029 pattern).
            metaLayout.childControlWidth = true;
            metaLayout.childControlHeight = true;
            metaLayout.childForceExpandWidth = false;
            metaLayout.childForceExpandHeight = false;
            metaRow.AddComponent<LayoutElement>().preferredHeight = 28f;

            // Date
            var dateShell = CreateMetaChip("DateChip", metaRow.transform, 180f);
            var dateObj = CreateTMPText("Date", dateShell.transform);
            var dateTmp = dateObj.GetComponent<TextMeshProUGUI>();
            dateTmp.text = data.dateTime;
            dateTmp.fontSize = EntryDetailSize;
            dateTmp.color = TextSecondary;
            dateTmp.alignment = TextAlignmentOptions.Center;
            dateTmp.raycastTarget = false;
            FillRect(dateObj);

            // Play time
            var timeShell = CreateMetaChip("TimeChip", metaRow.transform, 120f);
            var timeObj = CreateTMPText("PlayTime", timeShell.transform);
            var timeTmp = timeObj.GetComponent<TextMeshProUGUI>();
            timeTmp.text = data.playTime;
            timeTmp.fontSize = EntryDetailSize;
            timeTmp.color = TextSecondary;
            timeTmp.alignment = TextAlignmentOptions.Center;
            timeTmp.raycastTarget = false;
            FillRect(timeObj);

            // Autosave tag
            if (data.isAutosave)
            {
                var tagShell = CreateMetaChip("TagChip", metaRow.transform, 80f);
                var tagObj = CreateTMPText("Tag", tagShell.transform);
                var tagTmp = tagObj.GetComponent<TextMeshProUGUI>();
                tagTmp.text = LocalizationService.Get("load_game.tag.auto");
                tagTmp.fontSize = 16;
                tagTmp.color = AutosaveTag;
                tagTmp.fontStyle = FontStyles.Bold;
                tagTmp.alignment = TextAlignmentOptions.Center;
                tagTmp.raycastTarget = false;
                FillRect(tagObj);
            }

            var actionHintShell = CreateMetaChip("ActionHint", row.transform, 150f);
            var actionHint = CreateTMPText("ActionHintLabel", actionHintShell.transform).GetComponent<TextMeshProUGUI>();
            actionHint.text = LocalizationService.Get("load_game.action.open_save");
            actionHint.fontSize = 16;
            actionHint.color = AccentColor;
            actionHint.fontStyle = FontStyles.Bold;
            actionHint.alignment = TextAlignmentOptions.Center;
            actionHint.raycastTarget = false;
            FillRect(actionHint.gameObject);

            return row;
        }

        // ─────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────

        private GameObject CreateTMPText(string name, Transform parent) => MenuScreenPrimitives.CreateTMP(name, parent);

        private GameObject CreateToggle(string name, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(30f, 30f);

            // Background
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(obj.transform, false);
            var bgImg = bgObj.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.SecondarySurface, UIShapePreset.Inset);
            var bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Checkmark — filled Image (no font dependency)
            var checkObj = new GameObject("Checkmark");
            checkObj.transform.SetParent(bgObj.transform, false);
            var checkImg = checkObj.AddComponent<Image>();
            checkImg.color = AccentColor;
            var checkRT = checkObj.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.2f, 0.2f);
            checkRT.anchorMax = new Vector2(0.8f, 0.8f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;

            var toggle = obj.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = false;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = 30f;
            le.preferredHeight = 30f;

            return obj;
        }

        private GameObject CreateSpacer(float height)
        {
            var obj = new GameObject("Spacer");
            obj.transform.SetParent(contentParent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            return obj;
        }

        private GameObject CreateMetaChip(string name, Transform parent, float width)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(width, 28f);
            UITheme.ApplySurface(obj.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Pill);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 28f;
            return obj;
        }

        private static void FillRect(GameObject obj) => MenuScreenPrimitives.Fill(obj);
    }

}
