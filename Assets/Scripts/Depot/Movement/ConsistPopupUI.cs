using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// Popup UI dla consist'u w zajezdni 3D. Screen-space Canvas.
    /// Pokazuje info o wybranym consist'cie + hint "wybierz tor docelowy"
    /// gdy handler jest w stanie ConsistSelected.
    /// </summary>
    public class ConsistPopupUI : MonoBehaviour
    {
        public static ConsistPopupUI Instance { get; private set; }

        static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.97f);
        static readonly Color CardBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.88f);
        static readonly Color HintBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.15f);

        ConsistMarker _shown;
        bool _waitingForTarget;
        int _pendingTargetTrackId = -1;
        Vector3? _pendingTargetWorldPos;

        Canvas _canvas;
        GameObject _panel;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _infoText;
        TextMeshProUGUI _hintText;
        Button _closeButton;
        Button _goButton;
        TextMeshProUGUI _goButtonLabel;
        Button _exitButton;
        Button _decoupleButton;
        Button _coupleAdjacentButton;

        // TD-032 decouple modal (picker miejsca cięcia)
        GameObject _decoupleModal;
        Transform _decoupleStripContent;
        TextMeshProUGUI _decoupleStatus;
        Button _decoupleConfirmBtn;
        int _decoupleCutIndex = -1;
        bool _decoupleInCirc;
        readonly System.Collections.Generic.List<Image> _gapBgs = new();
        readonly System.Collections.Generic.List<TextMeshProUGUI> _gapTxts = new();

        public System.Action OnCloseRequested;
        public System.Action OnGoRequested;
        public System.Action OnExitRequested;
        public System.Action<int> OnDecoupleRequested;   // arg = cutIndex (1..count-1)
        public System.Action OnCoupleAdjacentRequested;  // TD-032 H: ręczny couple z sąsiednim (post-load)

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            Hide();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (_shown == null) return;
            if (_shown.gameObject == null) { Hide(); return; }
            UpdateInfo();
        }

        public void Show(ConsistMarker marker, bool waitingForTarget)
        {
            _shown = marker;
            _waitingForTarget = waitingForTarget;
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            _panel.SetActive(true);
            UpdateInfo();
        }

        public void SetPendingTarget(int trackId, Vector3 worldPos)
        {
            _pendingTargetTrackId = trackId;
            _pendingTargetWorldPos = worldPos;
            UpdateInfo();
        }

        public void ClearPendingTarget()
        {
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            UpdateInfo();
        }

        public int PendingTargetTrackId => _pendingTargetTrackId;
        public Vector3? PendingTargetWorldPos => _pendingTargetWorldPos;

        public void Hide()
        {
            ExitDecoupleMode();
            _shown = null;
            _waitingForTarget = false;
            _pendingTargetTrackId = -1;
            _pendingTargetWorldPos = null;
            if (_panel != null) _panel.SetActive(false);
        }

        // ── TD-032: rozprzęganie (picker miejsca cięcia) ─────────────

        void EnterDecoupleMode()
        {
            if (_shown == null) return;
            var ids = _shown.vehicleIds;
            if (ids == null || ids.Count < 2) return;

            BuildDecoupleStrip(ids);
            _decoupleCutIndex = -1;
            _decoupleConfirmBtn.interactable = false;
            _decoupleInCirc = DepotMovementSimulator.IsConsistInActiveCirculation(ids);
            _decoupleStatus.text = _decoupleInCirc ? WarnPrefix() : LocalizationService.Get("popup_decouple.hint");
            _decoupleModal.SetActive(true);
            RailwayManager.Core.SceneController.FullscreenOverlayOpen = true;
        }

        void ExitDecoupleMode()
        {
            _decoupleCutIndex = -1;
            if (_decoupleModal != null && _decoupleModal.activeSelf)
            {
                _decoupleModal.SetActive(false);
                RailwayManager.Core.SceneController.FullscreenOverlayOpen = false;
            }
        }

        void SelectGap(int cutIndex)
        {
            _decoupleCutIndex = cutIndex;
            for (int i = 0; i < _gapBgs.Count; i++)
            {
                bool sel = (i + 1) == cutIndex;
                if (_gapBgs[i] != null)
                    UITheme.ApplySurface(_gapBgs[i],
                        sel ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.95f) : UITheme.WithAlpha(UITheme.Border, 0.30f),
                        UIShapePreset.Button);
                if (_gapTxts[i] != null)
                    _gapTxts[i].text = sel ? "✂" : "┆";   // ✂ / ┆
            }
            int n = _shown?.vehicleIds?.Count ?? 0;
            string counts = string.Format(LocalizationService.Get("popup_decouple.status_format"), cutIndex, n - cutIndex);
            _decoupleStatus.text = _decoupleInCirc ? WarnPrefix() + "   " + counts : counts;
            _decoupleConfirmBtn.interactable = cutIndex >= 1 && cutIndex <= n - 1;
        }

        void ConfirmDecouple()
        {
            int cut = _decoupleCutIndex;
            int n = _shown?.vehicleIds?.Count ?? 0;
            ExitDecoupleMode();
            if (cut >= 1 && cut <= n - 1)
                OnDecoupleRequested?.Invoke(cut);   // handler → DecoupleConsist + Deselect
        }

        void BuildDecoupleModal()
        {
            // Backdrop (full-screen, klik = anuluj, łapie pointer → blokuje raycast 3D).
            _decoupleModal = new GameObject("DecoupleModal");
            _decoupleModal.transform.SetParent(_canvas.transform, false);
            var mrt = _decoupleModal.AddComponent<RectTransform>();
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            var backdrop = _decoupleModal.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            var backdropBtn = _decoupleModal.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(ExitDecoupleMode);

            // Picker (centered).
            var picker = new GameObject("Picker");
            picker.transform.SetParent(_decoupleModal.transform, false);
            var prt = picker.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(680f, 188f);
            UITheme.ApplySurface(picker.AddComponent<Image>(), PanelBg, UIShapePreset.PanelLarge);
            // Połknij klik na pickerze, żeby nie zamykał przez backdrop.
            picker.AddComponent<Button>().transition = Selectable.Transition.None;

            var title = CreateText(picker.transform, "Title", 16,
                TextAlignmentOptions.Top, new Vector2(16f, -12f), new Vector2(648f, 26f), UIThemeTextRole.Accent);
            title.text = LocalizationService.Get("popup_decouple.title");

            // Strip ze scrollem poziomym (obsługuje dowolnie długie składy).
            var scrollGO = new GameObject("Strip", typeof(RectTransform));
            scrollGO.transform.SetParent(picker.transform, false);
            var srt = scrollGO.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(0f, 1f); srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(16f, -46f);
            srt.sizeDelta = new Vector2(648f, 52f);
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGO.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>(); vpImg.color = new Color(0f, 0f, 0f, 0.001f);
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(0f, 1f); crt.pivot = new Vector2(0f, 0.5f);
            var hlg = content.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 0f; hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = vrt; scroll.content = crt;
            _decoupleStripContent = content.transform;

            _decoupleStatus = CreateText(picker.transform, "Status", 13,
                TextAlignmentOptions.Left, new Vector2(16f, -106f), new Vector2(648f, 22f), UIThemeTextRole.Secondary);

            var cancel = CreateButton(picker.transform, LocalizationService.Get("popup_decouple.btn_cancel"),
                new Vector2(16f, -146f), new Vector2(316f, 34f));
            cancel.onClick.AddListener(ExitDecoupleMode);
            _decoupleConfirmBtn = CreateButton(picker.transform, LocalizationService.Get("popup_decouple.btn_confirm"),
                new Vector2(348f, -146f), new Vector2(316f, 34f), true);
            _decoupleConfirmBtn.onClick.AddListener(ConfirmDecouple);

            _decoupleModal.SetActive(false);
        }

        void BuildDecoupleStrip(System.Collections.Generic.List<int> vehicleIds)
        {
            for (int i = _decoupleStripContent.childCount - 1; i >= 0; i--)
                Destroy(_decoupleStripContent.GetChild(i).gameObject);
            _gapBgs.Clear();
            _gapTxts.Clear();

            for (int i = 0; i < vehicleIds.Count; i++)
            {
                CreateChip(_decoupleStripContent, vehicleIds[i]);
                if (i < vehicleIds.Count - 1)
                    CreateGapButton(_decoupleStripContent, i + 1);   // przerwa po pojeździe i → cutIndex i+1
            }
        }

        void CreateChip(Transform parent, int vehicleId)
        {
            var (label, color) = VehicleChipStyle.ChipForVehicle(vehicleId);
            var go = new GameObject($"Chip_{vehicleId}");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 64f; le.preferredHeight = 44f;
            UITheme.ApplySurface(go.AddComponent<Image>(), color, UIShapePreset.Button);
            var txt = CreateText(go.transform, "Lbl", 12, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, UIThemeTextRole.Inverse);
            FillParent(txt.rectTransform);
            txt.text = label;
        }

        void CreateGapButton(Transform parent, int cutIndex)
        {
            var go = new GameObject($"Gap_{cutIndex}");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 26f; le.preferredHeight = 44f;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(UITheme.Border, 0.30f), UIShapePreset.Button);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            int ci = cutIndex;
            btn.onClick.AddListener(() => SelectGap(ci));
            var txt = CreateText(go.transform, "Cut", 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, UIThemeTextRole.Primary);
            FillParent(txt.rectTransform);
            txt.text = "┆";   // ┆
            _gapBgs.Add(img);
            _gapTxts.Add(txt);
        }

        static string WarnPrefix()
        {
            string hex = ColorUtility.ToHtmlStringRGB(UITheme.Warning);
            return $"<color=#{hex}>{LocalizationService.Get("popup_couple.warn_circulation")}</color>";
        }

        static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        void UpdateInfo()
        {
            if (_shown == null) return;

            _titleText.text = string.Format(LocalizationService.Get("popup_consist.title_format"), _shown.consistId);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.vehicles_format"), _shown.vehicleIds?.Count ?? 0));
            sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.track_format"), _shown.currentTrackId));

            // Stan z simulatora, jesli aktywny task dla tego consistu.
            var sim = DepotMovementSimulator.Instance;
            if (sim != null)
            {
                foreach (var task in sim.ActiveTasks)
                {
                    if (task.consistId != _shown.consistId) continue;
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.state_format"), task.state));
                    if (task.state == DepotMoveState.Moving)
                    {
                        float kmh = Mathf.Abs(task.currentSpeedMps) * 3.6f;
                        string dirLabel = task.currentSpeedMps < -0.05f
                            ? LocalizationService.Get("popup_consist.speed_reverse")
                            : string.Empty;
                        sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.speed_format"),
                            kmh.ToString("F0"), dirLabel));
                        sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.distance_format"),
                            task.currentDistanceM.ToString("F0"), task.totalLengthM.ToString("F0")));
                    }
                    break;
                }
            }

            if (_pendingTargetTrackId >= 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(LocalizationService.Get("popup_consist.target_format"), _pendingTargetTrackId));
            }

            _infoText.text = sb.ToString();

            if (_pendingTargetTrackId >= 0)
            {
                _hintText.text = LocalizationService.Get("popup_consist.hint_go");
                _goButton.gameObject.SetActive(true);
            }
            else
            {
                _hintText.text = _waitingForTarget
                    ? LocalizationService.Get("popup_consist.hint_pick_target")
                    : string.Empty;
                _goButton.gameObject.SetActive(false);
            }

            // TD-032: „Rozprzęgnij" tylko gdy ≥2 pojazdy i skład stoi (brak taska).
            bool canDecouple = (_shown.vehicleIds?.Count ?? 0) >= 2
                && (sim == null || !sim.HasTaskForConsist(_shown.consistId));
            _decoupleButton.gameObject.SetActive(canDecouple);

            // TD-032 H: „Połącz z sąsiednim" gdy stojący sąsiad stykiem na tym samym torze (post-load,
            // brak eventu dojazdu). Selektor → sim.FindAdjacentCouplableConsist.
            bool canCoupleAdjacent = sim != null && sim.FindAdjacentCouplableConsist(_shown.consistId) >= 0;
            _coupleAdjacentButton.gameObject.SetActive(canCoupleAdjacent);
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("ConsistPopupCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 220;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(_canvas.transform, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-20f, -20f);
            prt.sizeDelta = new Vector2(360f, 396f);   // +decouple +couple-adjacent (TD-032)

            var bg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(bg, PanelBg, UIShapePreset.PanelLarge);

            _titleText = CreateText(_panel.transform, "Title", 16,
                TextAlignmentOptions.TopLeft, new Vector2(14f, -12f), new Vector2(280f, 28f), UIThemeTextRole.Accent);
            _titleText.richText = true;

            var infoCard = new GameObject("InfoCard", typeof(RectTransform));
            infoCard.transform.SetParent(_panel.transform, false);
            var infoCardRt = infoCard.GetComponent<RectTransform>();
            infoCardRt.anchorMin = new Vector2(0f, 1f);
            infoCardRt.anchorMax = new Vector2(0f, 1f);
            infoCardRt.pivot = new Vector2(0f, 1f);
            infoCardRt.anchoredPosition = new Vector2(14f, -44f);
            infoCardRt.sizeDelta = new Vector2(332f, 136f);
            UITheme.ApplySurface(infoCard.AddComponent<Image>(), CardBg, UIShapePreset.Panel);

            _infoText = CreateText(infoCard.transform, "Info", 12,
                TextAlignmentOptions.TopLeft, new Vector2(14f, -14f), new Vector2(304f, 112f), UIThemeTextRole.Primary);
            _infoText.richText = true;

            var hintCard = new GameObject("HintCard", typeof(RectTransform));
            hintCard.transform.SetParent(_panel.transform, false);
            var hintCardRt = hintCard.GetComponent<RectTransform>();
            hintCardRt.anchorMin = new Vector2(0f, 1f);
            hintCardRt.anchorMax = new Vector2(0f, 1f);
            hintCardRt.pivot = new Vector2(0f, 1f);
            hintCardRt.anchoredPosition = new Vector2(14f, -188f);
            hintCardRt.sizeDelta = new Vector2(332f, 44f);
            UITheme.ApplySurface(hintCard.AddComponent<Image>(), HintBg, UIShapePreset.Panel);

            _hintText = CreateText(hintCard.transform, "Hint", 12,
                TextAlignmentOptions.MidlineLeft, new Vector2(14f, -10f), new Vector2(304f, 22f), UIThemeTextRole.Secondary);
            _hintText.richText = true;

            _closeButton = CreateButton(_panel.transform, LocalizationService.Get("popup_consist.btn_close"),
                new Vector2(318f, -8f), new Vector2(30f, 30f));
            _closeButton.onClick.AddListener(() => { OnCloseRequested?.Invoke(); });

            _goButton = CreateButton(_panel.transform, LocalizationService.Get("popup_consist.btn_go"),
                new Vector2(14f, -240f), new Vector2(332f, 34f), true);
            _goButton.onClick.AddListener(() => { OnGoRequested?.Invoke(); });
            _goButtonLabel = _goButton.GetComponentInChildren<TextMeshProUGUI>();
            _goButton.gameObject.SetActive(false);

            _exitButton = CreateButton(_panel.transform, LocalizationService.Get("popup_consist.btn_exit"),
                new Vector2(14f, -278f), new Vector2(332f, 34f));
            _exitButton.onClick.AddListener(() => { OnExitRequested?.Invoke(); });

            var exitImg = _exitButton.GetComponent<Image>();
            if (exitImg != null)
            {
                UITheme.ApplySurface(exitImg, UITheme.Danger, UIShapePreset.Button);
                _exitButton.targetGraphic = exitImg;
                _exitButton.colors = UITheme.CreateColorBlock(
                    UITheme.Danger,
                    new Color(0.87f, 0.48f, 0.40f, 1f),
                    UITheme.Darken(UITheme.Danger, 0.10f),
                    UITheme.Danger,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
            }

            _decoupleButton = CreateButton(_panel.transform, LocalizationService.Get("popup_decouple.btn_open"),
                new Vector2(14f, -316f), new Vector2(332f, 34f));
            _decoupleButton.onClick.AddListener(EnterDecoupleMode);
            _decoupleButton.gameObject.SetActive(false);

            _coupleAdjacentButton = CreateButton(_panel.transform, LocalizationService.Get("popup_couple.btn_adjacent"),
                new Vector2(14f, -354f), new Vector2(332f, 34f));
            _coupleAdjacentButton.onClick.AddListener(() => OnCoupleAdjacentRequested?.Invoke());
            _coupleAdjacentButton.gameObject.SetActive(false);

            BuildDecoupleModal();
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, int fontSize, TextAlignmentOptions alignment,
            Vector2 anchoredPos, Vector2 size, UIThemeTextRole role)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var txt = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, role);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.overflowMode = TextOverflowModes.Overflow;
            txt.raycastTarget = false;
            return txt;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, bool primary = false)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, primary ? UIButtonTone.Primary : UIButtonTone.Secondary, UIShapePreset.Button);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            txt.text = label;

            return btn;
        }
    }
}
