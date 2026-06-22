using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    /// <summary>
    /// MB-1 Phase A: Confirmation modal przed powrotem do MainMenu.
    /// Pomaga uniknąć przypadkowego utraty wybranych ustawień (10 sliderów + 6 toggle'i).
    ///
    /// Trigger: kliknięcie BackBtn (top bar) lub CancelBtn (bottom bar) lub ESC.
    /// Modal: "Anulować nową grę? Stracisz wszystkie ustawienia."
    /// Wybór: [Tak, anuluj] / [Nie, kontynuuj]. ESC = Nie (close modal).
    ///
    /// Modal jest stworzony lazy przy pierwszym wywołaniu Show, hidden domyślnie.
    /// </summary>
    public partial class GameCreatorUI
    {
        private GameObject _cancelModalGO;
        private TextMeshProUGUI _cancelModalTitle;
        private TextMeshProUGUI _cancelModalBody;
        private TextMeshProUGUI _cancelModalYesBtn;
        private TextMeshProUGUI _cancelModalNoBtn;

        private void EnsureCancelModalExists()
        {
            if (_cancelModalGO != null) return;

            // Full-screen overlay (półprzezroczysty)
            _cancelModalGO = NewGO("CancelConfirmModal", _root);
            var overlayRT = _cancelModalGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            var overlayImg = _cancelModalGO.AddComponent<Image>();
            UITheme.ApplySurface(overlayImg, UITheme.WithAlpha(Color.black, 0.7f), UIShapePreset.PanelLarge);

            // Centered modal panel
            var panel = NewGO("Panel", _cancelModalGO.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(500f, 220f);
            UITheme.ApplySurface(panel.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Xl);
            vlg.spacing = UITheme.Spacing.Lg;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;

            // Title
            var title = MakeTMP("Title", panel.transform);
            title.fontSize = 22;
            title.fontStyle = FontStyles.Bold;
            title.color = Accent;
            title.alignment = TextAlignmentOptions.Center;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            _cancelModalTitle = title;

            // Body
            var body = MakeTMP("Body", panel.transform);
            body.fontSize = 16;
            body.color = TextPrimary;
            body.alignment = TextAlignmentOptions.Center;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            _cancelModalBody = body;

            // Buttons row
            var btnRow = NewGO("BtnRow", panel.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 50f;
            var btnHL = btnRow.AddComponent<HorizontalLayoutGroup>();
            btnHL.spacing = UITheme.Spacing.Lg;
            btnHL.childAlignment = TextAnchor.MiddleCenter;
            btnHL.childControlWidth = false;
            btnHL.childControlHeight = false;

            // Yes (anuluj nowa grę)
            var yesGO = NewGO("YesBtn", btnRow.transform);
            yesGO.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 44f);
            var yesImg = yesGO.AddComponent<Image>();
            UITheme.ApplySurface(yesImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Inset);
            var yesBtn = yesGO.AddComponent<Button>();
            yesBtn.targetGraphic = yesImg;
            yesBtn.onClick.AddListener(() =>
            {
                Log.Info("[GameCreatorUI] User confirmed cancel — returning to MainMenu.");
                SceneManager.LoadScene("MainMenu");
            });
            yesGO.AddComponent<LayoutElement>().preferredWidth = 180f;
            _cancelModalYesBtn = MakeTMP("Lbl", yesGO.transform);
            _cancelModalYesBtn.fontSize = 18;
            _cancelModalYesBtn.fontStyle = FontStyles.Bold;
            _cancelModalYesBtn.color = TextPrimary;
            _cancelModalYesBtn.alignment = TextAlignmentOptions.Center;
            _cancelModalYesBtn.raycastTarget = false;
            FillRT(_cancelModalYesBtn.gameObject);

            // No (kontynuuj edycję)
            var noGO = NewGO("NoBtn", btnRow.transform);
            noGO.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 44f);
            var noImg = noGO.AddComponent<Image>();
            UITheme.ApplySurface(noImg, BtnCancel, UIShapePreset.Inset);
            var noBtn = noGO.AddComponent<Button>();
            noBtn.targetGraphic = noImg;
            noBtn.onClick.AddListener(HideCancelConfirmation);
            noGO.AddComponent<LayoutElement>().preferredWidth = 180f;
            _cancelModalNoBtn = MakeTMP("Lbl", noGO.transform);
            _cancelModalNoBtn.fontSize = 18;
            _cancelModalNoBtn.color = TextPrimary;
            _cancelModalNoBtn.alignment = TextAlignmentOptions.Center;
            _cancelModalNoBtn.raycastTarget = false;
            FillRT(_cancelModalNoBtn.gameObject);

            _cancelModalGO.SetActive(false);
        }

        private void ShowCancelConfirmation()
        {
            // Per-folder CLAUDE.md: modal pokazujemy TYLKO gdy gracz coś zmienił.
            // Brak zmian → bezpośredni powrót do MainMenu (nie marnujemy klików na potwierdzenie).
            if (!_isDirty)
            {
                Log.Info("[GameCreatorUI] Cancel without dirty state — direct return to MainMenu.");
                SceneManager.LoadScene("MainMenu");
                return;
            }

            EnsureCancelModalExists();
            // Apply lokalizacje (mogły się zmienić od ostatniego show)
            _cancelModalTitle.text  = LocalizationService.Get("game_creator.cancel_confirm.title");
            _cancelModalBody.text   = LocalizationService.Get("game_creator.cancel_confirm.body");
            _cancelModalYesBtn.text = LocalizationService.Get("game_creator.cancel_confirm.yes");
            _cancelModalNoBtn.text  = LocalizationService.Get("game_creator.cancel_confirm.no");
            _cancelModalGO.transform.SetAsLastSibling();
            _cancelModalGO.SetActive(true);
        }

        private void HideCancelConfirmation()
        {
            if (_cancelModalGO != null)
                _cancelModalGO.SetActive(false);
        }
    }
}
