using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// Menu pauzy wywoływane klawiszem ESC w scenie Depot.
    /// Budowane proceduralnie jako dziecko DepotCanvas.
    /// Zawiera wbudowany dialog potwierdzenia dla destruktywnych akcji.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        /// <summary>
        /// Popupy ustawiaja ten frame gdy zamykaja sie na ESC.
        /// PauseMenuUI.LateUpdate sprawdza to przed otwarciem menu pauzy,
        /// zeby nie otwierac menu w tej samej klatce ktora zamknela popup.
        /// Wartosc -1 = ESC nie bylo consumed w zadnej klatce.
        /// </summary>
        public static int LastEscConsumedFrame = -1;

        // ── colours ───────────────────────────────────
        private static readonly Color Overlay        = UITheme.WithAlpha(Color.black, 0.76f);
        private static readonly Color ConfirmOverlay = UITheme.WithAlpha(Color.black, 0.56f);
        private static readonly Color CardBg         = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.99f);
        private static readonly Color TitleBg        = UITheme.TopBarInset;
        private static readonly Color BtnBg          = UITheme.SecondarySurface;
        private static readonly Color BtnHover       = UITheme.RaisedSurface;
        private static readonly Color BtnRed         = UITheme.Danger;
        private static readonly Color BtnRedHover    = UITheme.Darken(UITheme.Danger, 0.08f);
        private static readonly Color SepColor       = UITheme.TopBarDivider;
        private static readonly Color TextPrimary    = UITheme.PrimaryText;
        private static readonly Color TextMuted      = UITheme.SecondaryText;
        private static readonly Color Accent         = UITheme.PrimaryAccent;

        // ── refs ──────────────────────────────────────
        private GameObject       _pauseRoot;
        private GameObject       _confirmRoot;
        private TextMeshProUGUI  _confirmMsg;
        private Button           _btnSaveGo, _btnGoNoSave, _btnCancel;

        // ── confirm callbacks ─────────────────────────
        private System.Action _onSaveAndProceed;
        private System.Action _onProceedNoSave;

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.UIPauseMenuActions _pauseMenuActions;

        // ─────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            _pauseMenuActions.Enable();
        }

        private void OnDisable()
        {
            _pauseMenuActions.Disable();
        }

        private void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        private void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _pauseMenuActions = _inputActions.UIPauseMenu;

            // Fill the canvas — RT must already exist on this GO (DepotUIManager creates it with typeof(RectTransform))
            var rt = GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            BuildPauseMenu();
            BuildConfirmDialog();

            _pauseRoot.SetActive(false);
            _confirmRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            // ESC: direct keyboard check w LateUpdate, po wszystkich Update().
            // Dzieki temu jesli popup zamknal sie w swoim Update i ustawil
            // LastEscConsumedFrame, my tutaj mozemy to wykryc i nie otwierac
            // menu pauzy w tej samej klatce co zamkniecie popupu.
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

            // Popup juz obsluzyl ESC w tej klatce - nie otwieraj menu pauzy
            // Sprawdza zarówno lokalną flagę (popupy Depot) jak i SceneController (Timetable)
            if (LastEscConsumedFrame == Time.frameCount) return;
            if (RailwayManager.Core.SceneController.LastEscConsumedFrame == Time.frameCount) return;

            // Każda scena ma własny PauseMenuUI — reaguj tylko gdy ta scena jest aktywna
            string myScene = gameObject.scene.name;
            var activeScene = RailwayManager.Core.SceneController.ActiveScene;
            bool isDepotMenu = myScene == "Depot";
            bool isMapMenu = myScene == "MapScene";
            if (isDepotMenu && activeScene != RailwayManager.Core.SceneController.GameScene.Depot) return;
            if (isMapMenu && activeScene != RailwayManager.Core.SceneController.GameScene.Map) return;

            if (_confirmRoot.activeSelf)   { HideConfirm(); return; }
            if (_pauseRoot.activeSelf)     { Hide();        return; }

            // If a build tool is active, ESC resets to Select instead of opening pause menu
            if (DepotUIManager.Instance != null &&
                DepotUIManager.Instance.CurrentTool != ToolMode.Select)
            {
                DepotUIManager.Instance.CurrentTool = ToolMode.Select;
                return;
            }

            // If fleet panel is open, ESC closes it (or its popup) instead of opening pause menu
            var fleetPanel = DepotUIManager.Instance?.fleetPanel;
            if (fleetPanel != null && fleetPanel.IsVisible)
            {
                if (!fleetPanel.HandleEscape())
                    fleetPanel.Hide();
                return;
            }

            // Detail popups (track/train/building) consume ESC themselves and set
            // LastEscConsumedFrame static flag, ktory sprawdzamy na wejsciu LateUpdate.
            // Wiec jesli tutaj dotarlo - zaden popup nie wziął ESC, mozna otworzyc menu.

            Show();
        }

        // ─────────────────────────────────────────────
        //  PUBLIC API
        // ─────────────────────────────────────────────

        public void Show() => _pauseRoot.SetActive(true);
        public void Hide() => _pauseRoot.SetActive(false);
        public bool IsVisible => _pauseRoot != null && _pauseRoot.activeSelf;

        // ─────────────────────────────────────────────
        //  BUILD — PAUSE MENU
        // ─────────────────────────────────────────────

        private void BuildPauseMenu()
        {
            // Dark overlay (full screen)
            _pauseRoot = NewGO("PauseRoot", transform);
            _pauseRoot.AddComponent<Image>().color = Overlay;
            FillRT(_pauseRoot);

            // Card (centered)
            var card = NewGO("Card", _pauseRoot.transform);
            var cardImage = card.AddComponent<Image>();
            UITheme.ApplySurface(cardImage, CardBg, UIShapePreset.PanelLarge);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.5f, 0.5f);
            cardRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardRT.pivot     = new Vector2(0.5f, 0.5f);
            cardRT.anchoredPosition = Vector2.zero;
            cardRT.sizeDelta = new Vector2(420f, 472f);

            // Card's Image already blocks raycasts — no second Image needed

            var cardVL = card.AddComponent<VerticalLayoutGroup>();
            cardVL.padding  = new RectOffset(0, 0, 0, 0);
            cardVL.spacing  = 0f;
            cardVL.childAlignment      = TextAnchor.UpperCenter;
            cardVL.childControlWidth   = true;
            cardVL.childControlHeight  = false;
            cardVL.childForceExpandWidth  = true;
            cardVL.childForceExpandHeight = false;

            // ── Title bar ─────────────────────────────
            var titleBar = NewGO("TitleBar", card.transform);
            var titleBarImage = titleBar.AddComponent<Image>();
            UITheme.ApplySurface(titleBarImage, TitleBg, UIShapePreset.Inset);
            titleBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
            titleBar.AddComponent<LayoutElement>().preferredHeight = 64f;

            var titleHL = titleBar.AddComponent<HorizontalLayoutGroup>();
            titleHL.padding  = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            titleHL.spacing  = 0f;
            titleHL.childAlignment      = TextAnchor.MiddleLeft;
            titleHL.childControlWidth   = false;
            titleHL.childControlHeight  = false;
            titleHL.childForceExpandWidth  = false;
            titleHL.childForceExpandHeight = false;

            // ← Wróć do gry
            var backGO = NewGO("BackBtn", titleBar.transform);
            backGO.GetComponent<RectTransform>().sizeDelta = new Vector2(132f, 44f);
            var backImg = backGO.AddComponent<Image>();
            UITheme.ApplySurface(backImg, BtnBg, UIShapePreset.Pill);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.transition    = Selectable.Transition.None;
            backBtn.onClick.AddListener(Hide);
            backGO.AddComponent<LayoutElement>().preferredWidth = 132f;
            backGO.AddComponent<PauseHover>().Init(backImg, BtnBg, BtnHover);

            var backLbl = MakeTMP("Lbl", backGO.transform);
            backLbl.text      = LocalizationService.Get("pause_menu.back_btn");
            backLbl.fontSize  = 18;
            backLbl.color     = TextPrimary;
            backLbl.alignment = TextAlignmentOptions.Center;
            backLbl.raycastTarget = false;
            FillRT(backLbl.gameObject);

            // Title (fills remaining space)
            var titleLbl = MakeTMP("Title", titleBar.transform);
            titleLbl.text      = LocalizationService.Get("pause_menu.title");
            titleLbl.fontSize  = 26;
            titleLbl.fontStyle = FontStyles.Bold;
            titleLbl.color     = Accent;
            titleLbl.alignment = TextAlignmentOptions.Center;
            titleLbl.raycastTarget = false;
            var titleLE = titleLbl.gameObject.AddComponent<LayoutElement>();
            titleLE.preferredWidth  = 160f;
            titleLE.flexibleWidth   = 1f;

            // ── Buttons area ──────────────────────────
            var btnArea = NewGO("BtnArea", card.transform);
            btnArea.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 408f);
            var areaLE = btnArea.AddComponent<LayoutElement>();
            areaLE.preferredHeight = 408f;

            var areaVL = btnArea.AddComponent<VerticalLayoutGroup>();
            areaVL.padding  = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Xl);
            areaVL.spacing  = UITheme.Spacing.Md;
            areaVL.childAlignment      = TextAnchor.UpperCenter;
            areaVL.childControlWidth   = true;
            areaVL.childControlHeight  = false;
            areaVL.childForceExpandWidth  = true;
            areaVL.childForceExpandHeight = false;

            // Buttons
            MakeMenuBtn(btnArea.transform, LocalizationService.Get("pause_menu.buttons.save_state"),  false, OnSaveState);
            MakeMenuBtn(btnArea.transform, LocalizationService.Get("pause_menu.buttons.load_state"),  false, OnLoadState);
            MakeMenuBtn(btnArea.transform, LocalizationService.Get("pause_menu.buttons.settings"),    false, OnSettings);

            // Separator
            var sep = NewGO("Sep", btnArea.transform);
            sep.AddComponent<Image>().color = SepColor;
            sep.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 1f);
            sep.AddComponent<LayoutElement>().preferredHeight = 1f;

            MakeMenuBtn(btnArea.transform, LocalizationService.Get("pause_menu.buttons.main_menu"), false, OnGoMainMenu);
            MakeMenuBtn(btnArea.transform, LocalizationService.Get("pause_menu.buttons.exit"),         true,  OnExitGame);
        }

        private void MakeMenuBtn(Transform parent, string label, bool danger, System.Action action)
        {
            var go = NewGO(label, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 50f);
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, danger ? BtnRed : BtnBg, danger ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => action?.Invoke());

            go.AddComponent<LayoutElement>().preferredHeight = 50f;
            go.AddComponent<PauseHover>().Init(img,
                danger ? BtnRed      : BtnBg,
                danger ? BtnRedHover : BtnHover);

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text      = label;
            lbl.fontSize  = 22;
            lbl.color     = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);
        }

        // ─────────────────────────────────────────────
        //  BUILD — CONFIRM DIALOG
        // ─────────────────────────────────────────────

        private void BuildConfirmDialog()
        {
            _confirmRoot = NewGO("ConfirmRoot", transform);
            _confirmRoot.AddComponent<Image>().color = ConfirmOverlay;
            FillRT(_confirmRoot);

            var dlg = NewGO("DlgCard", _confirmRoot.transform);
            var dlgImage = dlg.AddComponent<Image>();
            UITheme.ApplySurface(dlgImage, CardBg, UIShapePreset.PanelLarge);
            var dlgRT = dlg.GetComponent<RectTransform>();
            dlgRT.anchorMin = new Vector2(0.5f, 0.5f);
            dlgRT.anchorMax = new Vector2(0.5f, 0.5f);
            dlgRT.pivot     = new Vector2(0.5f, 0.5f);
            dlgRT.anchoredPosition = Vector2.zero;
            dlgRT.sizeDelta = new Vector2(480f, 212f);

            var dlgVL = dlg.AddComponent<VerticalLayoutGroup>();
            dlgVL.padding  = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Xl);
            dlgVL.spacing  = UITheme.Spacing.Lg;
            dlgVL.childAlignment      = TextAnchor.UpperCenter;
            dlgVL.childControlWidth   = true;
            dlgVL.childControlHeight  = false;
            dlgVL.childForceExpandWidth  = true;
            dlgVL.childForceExpandHeight = false;

            // Message
            _confirmMsg = MakeTMP("Msg", dlg.transform);
            _confirmMsg.fontSize  = 20;
            _confirmMsg.color     = TextPrimary;
            _confirmMsg.alignment = TextAlignmentOptions.Center;
            _confirmMsg.textWrappingMode = TMPro.TextWrappingModes.Normal;
            _confirmMsg.raycastTarget = false;
            _confirmMsg.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;

            // Button row
            var btnRow = NewGO("BtnRow", dlg.transform);
            btnRow.AddComponent<Image>().color = Color.clear;
            btnRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 44f;

            var hl = btnRow.AddComponent<HorizontalLayoutGroup>();
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleCenter;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            _btnSaveGo  = MakeConfirmBtn(btnRow.transform, LocalizationService.Get("pause_menu.confirm.save_and_go"), false);
            _btnGoNoSave = MakeConfirmBtn(btnRow.transform, LocalizationService.Get("pause_menu.confirm.no_save"),          true);
            _btnCancel  = MakeConfirmBtn(btnRow.transform, LocalizationService.Get("pause_menu.confirm.cancel"),              false);

            _btnSaveGo.onClick.AddListener(() => { HideConfirm(); _onSaveAndProceed?.Invoke(); });
            _btnGoNoSave.onClick.AddListener(() => { HideConfirm(); _onProceedNoSave?.Invoke(); });
            _btnCancel.onClick.AddListener(HideConfirm);
        }

        private Button MakeConfirmBtn(Transform parent, string label, bool danger)
        {
            var go = NewGO(label, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(155f, 46f);
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, danger ? BtnRed : BtnBg, danger ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None;

            go.AddComponent<LayoutElement>().preferredWidth = 155f;
            go.AddComponent<PauseHover>().Init(img,
                danger ? BtnRed      : BtnBg,
                danger ? BtnRedHover : BtnHover);

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text      = label;
            lbl.fontSize  = 19;
            lbl.color     = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            return btn;
        }

        private void ShowConfirm(string message, System.Action saveAndGo, System.Action goNoSave)
        {
            _confirmMsg.text    = message;
            _onSaveAndProceed   = saveAndGo;
            _onProceedNoSave    = goNoSave;
            _confirmRoot.SetActive(true);
        }

        private void HideConfirm() => _confirmRoot.SetActive(false);

        // ─────────────────────────────────────────────
        //  BUTTON ACTIONS
        // ─────────────────────────────────────────────

        private void OnSaveState()
        {
            // TD-006: button "Zapisz stan" \u2192 otwiera SaveLoadUI master-detail (gracz wybiera slot).
            // F5 quicksave hotkey nadal dzia\u0142a przez SaveActionsHook.QuickSave (bez UI, instant).
            // Fallback na QuickSave gdy slot picker hook nie zarejestrowany (legacy / smoke test).
            Log.Info("[PauseMenu] Zapisz stan \u2192 slot picker");
            if (RailwayManager.Core.SaveActionsHook.ShowSaveSlotPicker != null)
            {
                RailwayManager.Core.SaveActionsHook.ShowSaveSlotPicker.Invoke();
            }
            else if (RailwayManager.Core.SaveActionsHook.QuickSave != null)
            {
                Log.Warn("[PauseMenu] ShowSaveSlotPicker null \u2014 fallback na QuickSave (quick-save bez UI)");
                RailwayManager.Core.SaveActionsHook.QuickSave.Invoke();
            }
            else
            {
                Log.Warn("[PauseMenu] Save hooks null \u2014 SaveLoad bootstrap niezarejestrowany");
            }
        }

        private void OnLoadState()
        {
            // TD-006: button "Za\u0142aduj stan" \u2192 otwiera SaveLoadUI master-detail (gracz wybiera save).
            // F9 quickload hotkey nadal dzia\u0142a przez SaveActionsHook.QuickLoad (bez UI, instant).
            Log.Info("[PauseMenu] Za\u0142aduj stan \u2192 slot picker");
            if (RailwayManager.Core.SaveActionsHook.ShowLoadSlotPicker != null)
            {
                RailwayManager.Core.SaveActionsHook.ShowLoadSlotPicker.Invoke();
            }
            else if (RailwayManager.Core.SaveActionsHook.QuickLoad != null)
            {
                Log.Warn("[PauseMenu] ShowLoadSlotPicker null \u2014 fallback na QuickLoad (quick-load bez UI)");
                RailwayManager.Core.SaveActionsHook.QuickLoad.Invoke();
            }
            else
            {
                Log.Warn("[PauseMenu] Load hooks null \u2014 SaveLoad bootstrap niezarejestrowany");
            }
        }

        private MainMenu.SettingsScreenUI settingsScreen;

        private void OnSettings()
        {
            if (settingsScreen == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) return;
                var go = new GameObject("SettingsScreen");
                go.transform.SetParent(canvas.transform, false);
                settingsScreen = go.AddComponent<MainMenu.SettingsScreenUI>();
                settingsScreen.Build(canvas.transform);
                settingsScreen.OnBack = () => settingsScreen.Hide();
            }
            settingsScreen.Show();
        }

        private void OnGoMainMenu()
        {
            ShowConfirm(
                LocalizationService.Get("pause_menu.confirm.main_menu_msg"),
                () => {
                    // TAK — zapisz i wyjdź do menu (best-effort save z 5s timeout)
                    if (RailwayManager.Core.SaveActionsHook.SaveAndExitToMainMenu != null)
                        RailwayManager.Core.SaveActionsHook.SaveAndExitToMainMenu.Invoke();
                    else
                        SceneManager.LoadScene("MainMenu");
                },
                () => SceneManager.LoadScene("MainMenu") // NIE — wyjdź bez zapisu
            );
        }

        private void OnExitGame()
        {
            ShowConfirm(
                LocalizationService.Get("pause_menu.confirm.exit_msg"),
                () => {
                    // TAK — zapisz i wyjdź z gry
                    if (RailwayManager.Core.SaveActionsHook.SaveAndQuitApplication != null)
                        RailwayManager.Core.SaveActionsHook.SaveAndQuitApplication.Invoke();
                    else
                        Quit();
                },
                () => Quit() // NIE — wyjdź bez zapisu
            );
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────

        private static GameObject NewGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI MakeTMP(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var text = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            return text;
        }

        private static void FillRT(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    // ─────────────────────────────────────────────────
    //  HOVER HELPER
    // ─────────────────────────────────────────────────

    internal class PauseHover : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler
    {
        private Image _img;
        private Color _normal, _hover;

        public void Init(Image img, Color normal, Color hover)
        { _img = img; _normal = normal; _hover = hover; }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => _img.color = _hover;
        public void OnPointerExit (UnityEngine.EventSystems.PointerEventData _) => _img.color = _normal;
    }
}
