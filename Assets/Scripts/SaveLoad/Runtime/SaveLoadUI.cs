using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-11: Fullscreen panel save/load slot management.
    ///
    /// Layout (master-detail):
    /// - Lewa kolumna (40%): lista slot'ów scrollable, sortowane po SavedAt malejąco
    /// - Prawa kolumna (60%): szczegóły wybranego (nazwa/typ/playtime/data + akcje)
    /// - Dół: button "+ Nowy save", "Wczytaj", "Usuń"
    ///
    /// Tryby:
    /// - <see cref="ShowForSave"/> — panel save (gracz w grze, save manualny)
    /// - <see cref="ShowForLoad"/> — panel load (z MainMenu lub Pause Menu)
    ///
    /// ESC zamyka. Slot click = select. Double-click slot = load.
    ///
    /// **TODO M-UIPolish (MUI-4):** UI używa Legacy `Text`/`InputField` + LegacyRuntime.ttf.
    /// Migracja na TMP_Text/TMP_InputField razem z 50 plików × 213 wystąpień Text w innych UI.
    /// Kolory już są derived z UITheme (RootOverlayBg/PanelBg/SectionBg/...).
    /// </summary>
    public class SaveLoadUI : MonoBehaviour
    {
        public static SaveLoadUI Instance { get; private set; }

        private static readonly Color RootOverlayBg = UITheme.WithAlpha(Color.black, 0.82f);
        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.985f);
        private static readonly Color SectionBg = UITheme.WithAlpha(UITheme.TopBarInset, 0.94f);
        private static readonly Color RowBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.86f);
        private static readonly Color RowSelectedBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.82f);
        private static readonly Color ModalOverlayBg = UITheme.WithAlpha(Color.black, 0.72f);
        private static readonly Color ModalBoxBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color InputBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f);
        private static readonly Color CloseButtonBg = UITheme.WithAlpha(UITheme.Border, 0.82f);

        // ── UI references ────────────────────────────────────────────

        Canvas _canvas;
        GameObject _root;
        GameObject _newSaveModal;
        TMP_InputField _newSaveNameInput;
        RectTransform _slotsContent;
        TextMeshProUGUI _detailHeaderText;
        TextMeshProUGUI _detailInfoText;
        Button _loadBtn;
        Button _deleteBtn;
        Button _newSaveBtn;
        TextMeshProUGUI _modeTitleText;

        // ModifiedSave confirm modal (HMAC mismatch — gracz potwierdza retry z ignoreHmac=true)
        GameObject _modifiedSaveModal;
        TextMeshProUGUI _modifiedSaveMsgText;

        // Toast (krótki komunikat — PartialLoad / Failed / NewerVersion)
        GameObject _toastGo;
        TextMeshProUGUI _toastText;
        float _toastHideAt;

        // ── State ────────────────────────────────────────────────────

        public enum Mode { Save, Load }
        Mode _mode = Mode.Load;
        readonly List<SaveSlotInfo> _slots = new List<SaveSlotInfo>();
        readonly List<GameObject> _slotRows = new List<GameObject>();
        string _selectedSlotId;
        bool _isVisible;
        bool _isBusy;

        public System.Action OnClosed;

        // ── Bootstrap ────────────────────────────────────────────────

        public static SaveLoadUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SaveLoadUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SaveLoadUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // Toast auto-hide (timer-based, niezależny od _isVisible — toast może
            // przeżyć Hide() panelu na chwilę żeby gracz zobaczył komunikat).
            if (_toastGo != null && _toastGo.activeSelf && Time.unscaledTime >= _toastHideAt)
                _toastGo.SetActive(false);

            if (!_isVisible) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_modifiedSaveModal != null && _modifiedSaveModal.activeSelf)
                    _modifiedSaveModal.SetActive(false);
                else if (_newSaveModal != null && _newSaveModal.activeSelf)
                    _newSaveModal.SetActive(false);
                else
                    Hide();
            }
        }

        // ── Show / Hide ──────────────────────────────────────────────

        public void ShowForSave() => Show(Mode.Save);
        public void ShowForLoad() => Show(Mode.Load);

        public void Show(Mode mode)
        {
            EnsureExists();
            _mode = mode;
            _root.SetActive(true);
            _isVisible = true;
            UpdateModeTitle();
            UpdateButtonsVisibility();
            AsyncUI.Run(RefreshSlotsAsync, this, "SaveLoadUI.Show");
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            _selectedSlotId = null;
            OnClosed?.Invoke();
        }

        // ── Refresh slots ────────────────────────────────────────────

        private async Task RefreshSlotsAsync()
        {
            var storage = SaveLoadServiceBootstrap.Storage;
            if (storage == null)
            {
                Log.Warn("[SaveLoadUI] Storage null — bootstrap problem");
                return;
            }

            _slots.Clear();
            try
            {
                var list = await storage.ListAsync();
                _slots.AddRange(list);
            }
            catch (System.Exception e)
            {
                Log.Error($"[SaveLoadUI] ListAsync threw: {e.Message}");
            }
            RefreshSlotRows();
            RefreshDetailPanel();
        }

        private void RefreshSlotRows()
        {
            foreach (var r in _slotRows) if (r != null) Destroy(r);
            _slotRows.Clear();

            if (_slots.Count == 0)
            {
                var empty = CreateText(_slotsContent, "Empty",
                    LocalizationService.Get("save_load.empty"),
                    14, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 60);
                _slotRows.Add(empty.gameObject);
                return;
            }

            foreach (var slot in _slots)
                _slotRows.Add(BuildSlotRow(slot));
        }

        private GameObject BuildSlotRow(SaveSlotInfo slot)
        {
            var row = new GameObject($"Slot_{slot.SlotId}", typeof(RectTransform));
            row.transform.SetParent(_slotsContent, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 56;
            row.AddComponent<RectTransform>();

            bool selected = slot.SlotId == _selectedSlotId;
            var bg = row.AddComponent<Image>();
            Color rowColor = selected ? RowSelectedBg : RowBg;
            UITheme.ApplySurface(bg, rowColor, selected ? UIShapePreset.Panel : UIShapePreset.Inset);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.colors = UITheme.CreateColorBlock(
                rowColor,
                UITheme.Darken(rowColor, 0.04f),
                UITheme.Darken(rowColor, 0.1f),
                rowColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() =>
            {
                _selectedSlotId = slot.SlotId;
                RefreshSlotRows();
                RefreshDetailPanel();
            });

            // Info text — name + type + when
            string saveTypeKey = "save_load.type_" + (slot.SaveType ?? SaveTypes.Manual);
            string saveTypeLabel = LocalizationService.Get(saveTypeKey);
            string when = !string.IsNullOrEmpty(slot.SavedAt) ? slot.SavedAt : "?";
            string display = !string.IsNullOrEmpty(slot.SlotName) ? slot.SlotName : slot.SlotId;
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);

            var info = CreateText(row.transform, "Info",
                $"<b>{display}</b>  <color=#{mutedHex}>[{saveTypeLabel}]</color>\n" +
                $"<size=11><color=#9CA3AF>{when}  ·  v{slot.GameVersion}  ·  {FormatBytes(slot.FileSizeBytes)}</color></size>",
                12, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            info.raycastTarget = false;
            var irt = (RectTransform)info.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(10, 4); irt.offsetMax = new Vector2(-10, -4);

            return row;
        }

        private void RefreshDetailPanel()
        {
            SaveSlotInfo selected = null;
            if (_selectedSlotId != null)
            {
                foreach (var s in _slots)
                    if (s.SlotId == _selectedSlotId) { selected = s; break; }
            }

            if (selected == null)
            {
                _detailHeaderText.text = LocalizationService.Get("save_load.detail_select");
                _detailInfoText.text = "";
                if (_loadBtn != null) _loadBtn.interactable = false;
                if (_deleteBtn != null) _deleteBtn.interactable = false;
                return;
            }

            string display = !string.IsNullOrEmpty(selected.SlotName) ? selected.SlotName : selected.SlotId;
            _detailHeaderText.text = $"<b><size=20>{display}</size></b>";

            string saveTypeKey = "save_load.type_" + (selected.SaveType ?? "manual");
            int playtimeMin = (int)(selected.Playtime / 60.0);
            _detailInfoText.text =
                string.Format(LocalizationService.Get("save_load.detail_format"),
                    LocalizationService.Get(saveTypeKey),
                    selected.GameVersion,
                    selected.GameTimeIso,
                    selected.SavedAt,
                    playtimeMin,
                    FormatBytes(selected.FileSizeBytes),
                    selected.SlotId);

            if (_loadBtn != null) _loadBtn.interactable = !_isBusy;
            if (_deleteBtn != null) _deleteBtn.interactable = !_isBusy;
        }

        // ── Actions ──────────────────────────────────────────────────

        private void UpdateModeTitle()
        {
            if (_modeTitleText == null) return;
            _modeTitleText.text = LocalizationService.Get(_mode == Mode.Save
                ? "save_load.title_save" : "save_load.title_load");
        }

        private void UpdateButtonsVisibility()
        {
            if (_newSaveBtn != null)
                _newSaveBtn.gameObject.SetActive(_mode == Mode.Save);
            if (_loadBtn != null)
                _loadBtn.gameObject.SetActive(_mode == Mode.Load);
        }

        private void OnNewSaveClicked()
        {
            if (_newSaveModal == null) return;
            _newSaveNameInput.text = $"Save {System.DateTime.Now:yyyy-MM-dd HH:mm}";
            _newSaveModal.SetActive(true);
        }

        private async Task OnNewSaveConfirmAsync()
        {
            if (_isBusy) return;

            string name = _newSaveNameInput?.text?.Trim();
            if (string.IsNullOrEmpty(name))
                name = $"Save {System.DateTime.Now:HHmmss}";
            string slotId = "save_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var orch = SaveLoadServiceBootstrap.Orchestrator;
            if (orch == null)
            {
                Log.Warn("[SaveLoadUI] Orchestrator null");
                return;
            }

            _isBusy = true;
            if (_newSaveModal != null) _newSaveModal.SetActive(false);

            LoadingScreenManager.EnsureExists().Show(LocalizationService.Get("save_load.busy_save"));
            bool ok = false;
            try
            {
                ok = await orch.SaveAsync(slotId, name,
                    saveType: SaveTypes.Manual,
                    playtime: RailwayManager.Core.GameState.GetTotalPlaytimeSec(),
                    gameTimeIso: RailwayManager.Core.GameState.CurrentDateIso);
            }
            finally
            {
                _isBusy = false;
                LoadingScreenManager.Instance?.Hide();
            }

            // Object destroyed during await → bail out before touching Unity API.
            if (this == null) return;

            Log.Info($"[SaveLoadUI] New save '{slotId}': {(ok ? "OK" : "FAIL")}");
            if (!ok) ShowToast(LocalizationService.Get("save_load.save_failed"));
            await RefreshSlotsAsync();
        }

        /// <summary>
        /// Load handler. <paramref name="ignoreHmac"/>=true tylko z confirm modal'a
        /// (gracz potwierdził load zmodyfikowanego save'u).
        /// </summary>
        private async Task OnLoadClickedAsync(bool ignoreHmac = false)
        {
            if (_isBusy) return;
            if (_selectedSlotId == null) return;
            var orch = SaveLoadServiceBootstrap.Orchestrator;
            if (orch == null) return;

            string slotId = _selectedSlotId;
            _isBusy = true;
            LoadingScreenManager.EnsureExists().Show(LocalizationService.Get("save_load.busy_load"));
            LoadResult result = null;
            try
            {
                result = await orch.LoadAsync(slotId, ignoreHmac);
            }
            finally
            {
                _isBusy = false;
                LoadingScreenManager.Instance?.Hide();
            }

            if (this == null || result == null) return;
            Log.Info($"[SaveLoadUI] Load '{slotId}' (ignoreHmac={ignoreHmac}): {result.Status}");

            switch (result.Status)
            {
                case LoadStatus.Success:
                    Hide();
                    break;

                case LoadStatus.PartialLoad:
                    // Niektóre moduły failed → InitializeDefault. Reszta OK, gracz wszedł do gry.
                    string modules = result.FailedModules.Count > 0
                        ? string.Join(", ", result.FailedModules)
                        : "?";
                    ShowToast(string.Format(
                        LocalizationService.Get("save_load.partial_load_msg"), modules));
                    Hide();
                    break;

                case LoadStatus.ModifiedSave:
                    // HMAC mismatch — wymagana zgoda gracza na retry z ignoreHmac=true.
                    ShowModifiedSaveConfirm();
                    break;

                case LoadStatus.NewerVersion:
                    ShowToast(result.ErrorMessage ?? LocalizationService.Get("save_load.newer_version"));
                    break;

                case LoadStatus.NotFound:
                    ShowToast(LocalizationService.Get("save_load.not_found"));
                    await RefreshSlotsAsync();
                    break;

                case LoadStatus.Failed:
                default:
                    ShowToast(LocalizationService.Get("save_load.load_failed")
                              + (string.IsNullOrEmpty(result.ErrorMessage) ? "" : ": " + result.ErrorMessage));
                    break;
            }
        }

        private async Task OnDeleteClickedAsync()
        {
            if (_isBusy) return;
            if (_selectedSlotId == null) return;
            var storage = SaveLoadServiceBootstrap.Storage;
            if (storage == null) return;

            _isBusy = true;
            try
            {
                await storage.DeleteAsync(_selectedSlotId);
            }
            finally
            {
                _isBusy = false;
            }

            if (this == null) return;
            _selectedSlotId = null;
            await RefreshSlotsAsync();
        }

        // ── ModifiedSave confirm modal ───────────────────────────────

        private void ShowModifiedSaveConfirm()
        {
            if (_modifiedSaveModal == null) return;
            if (_modifiedSaveMsgText != null)
                _modifiedSaveMsgText.text = LocalizationService.Get("save_load.modified_save_msg");
            _modifiedSaveModal.SetActive(true);
        }

        // ── Toast (krótki komunikat) ─────────────────────────────────

        private void ShowToast(string msg, float durationSec = 4.5f)
        {
            if (_toastGo == null || _toastText == null) return;
            _toastText.text = msg;
            _toastGo.SetActive(true);
            _toastHideAt = Time.unscaledTime + durationSec;
        }

        // ── UI build ────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("SaveLoadCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 250;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            var rrt = (RectTransform)_root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(_root.AddComponent<Image>(), RootOverlayBg, UIShapePreset.PanelLarge);

            // Centered panel
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_root.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1100, 720);
            prt.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(panel.AddComponent<Image>(), PanelBg, UIShapePreset.PanelLarge);

            // Title
            _modeTitleText = CreateText(panel.transform, "Title",
                LocalizationService.Get("save_load.title_load"), 22, TextAlignmentOptions.MidlineLeft);
            var trt = (RectTransform)_modeTitleText.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0, 1);
            trt.anchoredPosition = new Vector2(20, -15);
            trt.sizeDelta = new Vector2(-180, 40);

            // Close
            var closeBtn = CreateButton(panel.transform, "Close",
                LocalizationService.Get("save_load.close_btn"), Hide, CloseButtonBg);
            var cbr = (RectTransform)closeBtn.transform;
            cbr.anchorMin = new Vector2(1, 1); cbr.anchorMax = new Vector2(1, 1);
            cbr.pivot = new Vector2(1, 1);
            cbr.anchoredPosition = new Vector2(-15, -15);
            cbr.sizeDelta = new Vector2(140, 30);

            // Slots scroll (left)
            BuildSlotsScroll(panel.transform);

            // Detail panel (right)
            BuildDetailPanel(panel.transform);

            // Bottom buttons
            BuildBottomButtons(panel.transform);

            // New save modal
            BuildNewSaveModal(panel.transform);

            // ModifiedSave confirm modal (HMAC mismatch)
            BuildModifiedSaveModal(panel.transform);

            // Toast (PartialLoad / NewerVersion / Failed messages) — w canvas root
            // żeby był nad wszystkim, włącznie z modal'ami.
            BuildToast(canvasGo.transform);
        }

        private void BuildSlotsScroll(Transform parent)
        {
            var scroll = new GameObject("Scroll", typeof(RectTransform));
            scroll.transform.SetParent(parent, false);
            var sr = (RectTransform)scroll.transform;
            sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(0.42f, 1);
            sr.pivot = new Vector2(0.5f, 0.5f);
            sr.offsetMin = new Vector2(20, 70); sr.offsetMax = new Vector2(-10, -65);
            UITheme.ApplySurface(scroll.AddComponent<Image>(), SectionBg, UIShapePreset.Panel);

            var srComp = scroll.AddComponent<ScrollRect>();
            srComp.horizontal = false; srComp.vertical = true;

            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(scroll.transform, false);
            var vrt = (RectTransform)vp.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(vp.AddComponent<Image>(), UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            srComp.viewport = vrt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            _slotsContent = content.GetComponent<RectTransform>();
            _slotsContent.anchorMin = new Vector2(0, 1);
            _slotsContent.anchorMax = new Vector2(1, 1);
            _slotsContent.pivot = new Vector2(0.5f, 1);
            // 2026-05-17: explicit sizeDelta zero (identyczny pattern bug co w DepotLocationPicker.eef9f7d).
            // Bez tego Unity default (100, 100) z anchor stretch X dawał content.width = viewport.width + 100,
            // plus pivot (0.5, 1) → przesunięty 50px na lewo poza viewport. Mask clipował → "save_xxx"
            // wyglądało jak "av_xxx" (pierwsze znaki ucięte).
            _slotsContent.sizeDelta = Vector2.zero;
            _slotsContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            srComp.content = _slotsContent;
        }

        private void BuildDetailPanel(Transform parent)
        {
            var detail = new GameObject("Detail", typeof(RectTransform));
            detail.transform.SetParent(parent, false);
            var drt = (RectTransform)detail.transform;
            drt.anchorMin = new Vector2(0.42f, 0); drt.anchorMax = new Vector2(1, 1);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.offsetMin = new Vector2(10, 70); drt.offsetMax = new Vector2(-20, -65);
            UITheme.ApplySurface(detail.AddComponent<Image>(), SectionBg, UIShapePreset.Panel);

            _detailHeaderText = CreateText(detail.transform, "Hdr",
                LocalizationService.Get("save_load.detail_select"), 16, TextAlignmentOptions.TopLeft);
            _detailHeaderText.richText = true;
            var hrt = (RectTransform)_detailHeaderText.transform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
            hrt.pivot = new Vector2(0.5f, 1);
            hrt.anchoredPosition = new Vector2(0, -10);
            hrt.sizeDelta = new Vector2(-20, 50);

            _detailInfoText = CreateText(detail.transform, "Info", "", 12, TextAlignmentOptions.TopLeft);
            _detailInfoText.richText = true;
            var irt = (RectTransform)_detailInfoText.transform;
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 1);
            irt.offsetMin = new Vector2(15, 15); irt.offsetMax = new Vector2(-15, -65);
        }

        private void BuildBottomButtons(Transform parent)
        {
            _newSaveBtn = CreateButton(parent, "NewSave",
                LocalizationService.Get("save_load.new_save_btn"), OnNewSaveClicked, UITheme.Success);
            var nr = (RectTransform)_newSaveBtn.transform;
            nr.anchorMin = new Vector2(0, 0); nr.anchorMax = new Vector2(0, 0);
            nr.pivot = new Vector2(0, 0);
            nr.anchoredPosition = new Vector2(20, 15);
            nr.sizeDelta = new Vector2(180, 42);
            _loadBtn = CreateButton(parent, "Load",
                LocalizationService.Get("save_load.load_btn"),
                () => AsyncUI.Run(() => OnLoadClickedAsync(false), this, "SaveLoadUI.Load"),
                UITheme.PrimaryAccent);
            var lr = (RectTransform)_loadBtn.transform;
            lr.anchorMin = new Vector2(0.42f, 0); lr.anchorMax = new Vector2(0.42f, 0);
            lr.pivot = new Vector2(0, 0);
            lr.anchoredPosition = new Vector2(20, 15);
            lr.sizeDelta = new Vector2(160, 42);
            _deleteBtn = CreateButton(parent, "Delete",
                LocalizationService.Get("save_load.delete_btn"),
                () => AsyncUI.Run(OnDeleteClickedAsync, this, "SaveLoadUI.Delete"),
                UITheme.Danger);
            var dr = (RectTransform)_deleteBtn.transform;
            dr.anchorMin = new Vector2(1, 0); dr.anchorMax = new Vector2(1, 0);
            dr.pivot = new Vector2(1, 0);
            dr.anchoredPosition = new Vector2(-20, 15);
            dr.sizeDelta = new Vector2(140, 42);
        }

        private void BuildNewSaveModal(Transform parent)
        {
            _newSaveModal = new GameObject("NewSaveModal", typeof(RectTransform));
            _newSaveModal.transform.SetParent(parent, false);
            var mrt = (RectTransform)_newSaveModal.transform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(_newSaveModal.AddComponent<Image>(), ModalOverlayBg, UIShapePreset.PanelLarge);

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(_newSaveModal.transform, false);
            var brt = (RectTransform)box.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(500, 200);
            brt.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(box.AddComponent<Image>(), ModalBoxBg, UIShapePreset.PanelLarge);

            var title = CreateText(box.transform, "Title",
                LocalizationService.Get("save_load.new_save_title"), 16, TextAlignmentOptions.Center);
            var trt = (RectTransform)title.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -15);
            trt.sizeDelta = new Vector2(-20, 30);

            // Input field
            var input = new GameObject("NameInput", typeof(RectTransform));
            input.transform.SetParent(box.transform, false);
            var ir = (RectTransform)input.transform;
            ir.anchorMin = new Vector2(0.5f, 0.5f); ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(420, 32);
            ir.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(input.AddComponent<Image>(), InputBg, UIShapePreset.Inset);

            var inputTextGo = new GameObject("Text", typeof(RectTransform));
            inputTextGo.transform.SetParent(input.transform, false);
            var itr = (RectTransform)inputTextGo.transform;
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = new Vector2(8, 4); itr.offsetMax = new Vector2(-8, -4);
            // TMP_InputField requires Viewport (RectMask2D); existing layout uses single GO for textComponent — adapt.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(input.transform, false);
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8, 4); viewportRt.offsetMax = new Vector2(-8, -4);
            viewportGo.AddComponent<RectMask2D>();

            inputTextGo.transform.SetParent(viewportGo.transform, false);
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = Vector2.zero; itr.offsetMax = Vector2.zero;

            var inputText = inputTextGo.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 14;
            inputText.color = UITheme.PrimaryText;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            inputText.textWrappingMode = TextWrappingModes.NoWrap;
            inputText.raycastTarget = false;
            UITheme.ApplyTmpText(inputText, UIThemeTextRole.Primary);

            _newSaveNameInput = input.AddComponent<TMP_InputField>();
            _newSaveNameInput.textViewport = viewportRt;
            _newSaveNameInput.textComponent = inputText;
            _newSaveNameInput.lineType = TMP_InputField.LineType.SingleLine;

            // Confirm + cancel
            var confirmBtn = CreateButton(box.transform, "Confirm",
                LocalizationService.Get("save_load.confirm_btn"),
                () => AsyncUI.Run(OnNewSaveConfirmAsync, this, "SaveLoadUI.NewSaveConfirm"),
                UITheme.Success);
            var cr = (RectTransform)confirmBtn.transform;
            cr.anchorMin = new Vector2(0.5f, 0); cr.anchorMax = new Vector2(0.5f, 0);
            cr.pivot = new Vector2(1, 0);
            cr.anchoredPosition = new Vector2(-10, 20);
            cr.sizeDelta = new Vector2(180, 36);
            var cancelBtn = CreateButton(box.transform, "Cancel",
                LocalizationService.Get("save_load.cancel_btn"),
                () => _newSaveModal.SetActive(false),
                UITheme.WithAlpha(UITheme.Border, 0.82f));
            var canr = (RectTransform)cancelBtn.transform;
            canr.anchorMin = new Vector2(0.5f, 0); canr.anchorMax = new Vector2(0.5f, 0);
            canr.pivot = new Vector2(0, 0);
            canr.anchoredPosition = new Vector2(10, 20);
            canr.sizeDelta = new Vector2(180, 36);

            _newSaveModal.SetActive(false);
        }

        private void BuildModifiedSaveModal(Transform parent)
        {
            _modifiedSaveModal = new GameObject("ModifiedSaveModal", typeof(RectTransform));
            _modifiedSaveModal.transform.SetParent(parent, false);
            var mrt = (RectTransform)_modifiedSaveModal.transform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(_modifiedSaveModal.AddComponent<Image>(), ModalOverlayBg, UIShapePreset.PanelLarge);

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(_modifiedSaveModal.transform, false);
            var brt = (RectTransform)box.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(560, 240);
            brt.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(box.AddComponent<Image>(), ModalBoxBg, UIShapePreset.PanelLarge);

            var title = CreateText(box.transform, "Title",
                LocalizationService.Get("save_load.modified_save_title"), 16, TextAlignmentOptions.Center);
            var trt = (RectTransform)title.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -15);
            trt.sizeDelta = new Vector2(-20, 30);

            _modifiedSaveMsgText = CreateText(box.transform, "Msg",
                LocalizationService.Get("save_load.modified_save_msg"), 13, TextAlignmentOptions.Center);
            _modifiedSaveMsgText.color = UITheme.SecondaryText;
            var mtrt = (RectTransform)_modifiedSaveMsgText.transform;
            mtrt.anchorMin = new Vector2(0, 0.5f); mtrt.anchorMax = new Vector2(1, 0.5f);
            mtrt.pivot = new Vector2(0.5f, 0.5f);
            mtrt.anchoredPosition = new Vector2(0, 5);
            mtrt.sizeDelta = new Vector2(-30, 90);

            // Load anyway (Danger) — gracz świadomie ignoruje HMAC
            var loadAnywayBtn = CreateButton(box.transform, "LoadAnyway",
                LocalizationService.Get("save_load.modified_save_load_anyway"),
                () =>
                {
                    if (_modifiedSaveModal != null) _modifiedSaveModal.SetActive(false);
                    AsyncUI.Run(() => OnLoadClickedAsync(true), this, "SaveLoadUI.LoadIgnoreHmac");
                },
                UITheme.Danger);
            var lar = (RectTransform)loadAnywayBtn.transform;
            lar.anchorMin = new Vector2(0.5f, 0); lar.anchorMax = new Vector2(0.5f, 0);
            lar.pivot = new Vector2(1, 0);
            lar.anchoredPosition = new Vector2(-10, 20);
            lar.sizeDelta = new Vector2(220, 36);

            var cancelBtn = CreateButton(box.transform, "Cancel",
                LocalizationService.Get("save_load.cancel_btn"),
                () => { if (_modifiedSaveModal != null) _modifiedSaveModal.SetActive(false); },
                UITheme.WithAlpha(UITheme.Border, 0.82f));
            var canr = (RectTransform)cancelBtn.transform;
            canr.anchorMin = new Vector2(0.5f, 0); canr.anchorMax = new Vector2(0.5f, 0);
            canr.pivot = new Vector2(0, 0);
            canr.anchoredPosition = new Vector2(10, 20);
            canr.sizeDelta = new Vector2(180, 36);

            _modifiedSaveModal.SetActive(false);
        }

        private void BuildToast(Transform canvasRoot)
        {
            _toastGo = new GameObject("Toast", typeof(RectTransform));
            _toastGo.transform.SetParent(canvasRoot, false);
            var trt = (RectTransform)_toastGo.transform;
            trt.anchorMin = new Vector2(0.5f, 0); trt.anchorMax = new Vector2(0.5f, 0);
            trt.pivot = new Vector2(0.5f, 0);
            trt.anchoredPosition = new Vector2(0, 80);
            trt.sizeDelta = new Vector2(720, 64);
            UITheme.ApplySurface(_toastGo.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.95f), UIShapePreset.Pill);

            _toastText = CreateText(_toastGo.transform, "Text", "", 13, TextAlignmentOptions.Center);
            _toastText.richText = true;
            var ttr = (RectTransform)_toastText.transform;
            ttr.anchorMin = Vector2.zero; ttr.anchorMax = Vector2.one;
            ttr.offsetMin = new Vector2(20, 6); ttr.offsetMax = new Vector2(-20, -6);

            _toastGo.SetActive(false);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static TextMeshProUGUI CreateText(Transform parent, string name, string text,
                                        int fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = UITheme.PrimaryText;
            t.richText = true;
            UITheme.ApplyTmpText(t, UIThemeTextRole.Primary);
            return t;
        }

        private static Button CreateButton(Transform parent, string name, string label,
                                            System.Action onClick, Color? bgOverride = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            Color bg = bgOverride ?? UITheme.WithAlpha(UITheme.Border, 0.82f);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                bg,
                UITheme.Darken(bg, 0.05f),
                UITheme.Darken(bg, 0.12f),
                bg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text = label;
            lt.fontSize = 14;
            lt.alignment = TextAlignmentOptions.Center;
            lt.color = UITheme.InverseText;
            UITheme.ApplyTmpText(lt, UIThemeTextRole.Inverse);
            return btn;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}
