using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CategoryEditorUI
    {
        // ═══════════════════════════════════════════
        //  TABELA + FORM ACTIONS — refresh listy + new/load/clear/save/delete + parsery
        // ═══════════════════════════════════════════

        public void RefreshTable()
        {
            if (_tableContent == null) return;
            foreach (Transform ch in _tableContent) Destroy(ch.gameObject);

            foreach (var cat in TimetableService.CommercialCategories)
            {
                if (cat == null) continue;
                int usedBy = TimetableService.CountTimetablesUsingCategory(cat.id);

                var row = new GameObject("CatRow");
                row.transform.SetParent(_tableContent, false);
                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 42;
                rowLe.flexibleHeight = 0;
                var rowBg = row.AddComponent<Image>();
                UITheme.ApplySurface(rowBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.38f), UIShapePreset.Inset);
                var hh = row.AddComponent<HorizontalLayoutGroup>();
                hh.spacing = UITheme.Spacing.Xs;
                hh.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
                hh.childForceExpandWidth = false;
                hh.childForceExpandHeight = true;
                hh.childAlignment = TextAnchor.MiddleLeft;

                string line = $"{cat.shortCode,-6} {cat.displayName,-30}  bazowa {cat.basePriceZl,5:F0} zł  "
                              + $"+{cat.pricePerKmZl,5:F2}/km  prio {cat.trafficPriority}  Vmax {cat.suggestedMaxSpeedKmh,3}  "
                              + $"({usedBy} rozkł.)";
                var lbl = new GameObject("L");
                lbl.transform.SetParent(row.transform, false);
                lbl.AddComponent<RectTransform>();
                var lblLe = lbl.AddComponent<LayoutElement>();
                lblLe.flexibleWidth = 1;
                lblLe.preferredHeight = 36;
                var tx = lbl.AddComponent<TextMeshProUGUI>();
                tx.fontSize = 11;
                tx.text = line;
                tx.text = $"{cat.shortCode}  {cat.displayName}\n" +
                          $"bazowa {cat.basePriceZl:F0} zl  |  +{cat.pricePerKmZl:F2}/km  |  prio {cat.trafficPriority}  |  Vmax {cat.suggestedMaxSpeedKmh}  |  {usedBy} rozkl.";
                tx.alignment = TextAlignmentOptions.MidlineLeft;
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);

                var captCat = cat;
                Btn(row.transform, LocalizationService.Get("timetable.category_editor.button.edit"), () => LoadCategoryToForm(captCat),
                    UITheme.PrimaryAccent, 60);

                bool deletable = usedBy == 0;
                var delBtn = Btn(row.transform, LocalizationService.Get("timetable.category_editor.button.delete"),
                    () => DeleteCategory(captCat.id),
                    deletable ? UITheme.Danger : UITheme.WithAlpha(UITheme.Border, 0.65f),
                    50);
                delBtn.interactable = deletable;
            }

            if (_tableContent.childCount == 0)
            {
                var empty = new GameObject("Empty");
                empty.transform.SetParent(_tableContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 30;
                UITheme.ApplySurface(
                    empty.AddComponent<Image>(),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f),
                    UIShapePreset.Inset);

                // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
                var emptyTextObj = new GameObject("Text", typeof(RectTransform));
                emptyTextObj.transform.SetParent(empty.transform, false);
                var emptyRt = (RectTransform)emptyTextObj.transform;
                emptyRt.anchorMin = Vector2.zero;
                emptyRt.anchorMax = Vector2.one;
                emptyRt.offsetMin = Vector2.zero;
                emptyRt.offsetMax = Vector2.zero;
                var t = emptyTextObj.AddComponent<TextMeshProUGUI>();
                t.fontSize = 12;
                t.alignment = TextAlignmentOptions.Center;
                t.raycastTarget = false;
                t.text = LocalizationService.Get("timetable.category_editor.empty");
                UITheme.ApplyTmpText(t, UIThemeTextRole.Secondary);
            }
        }

        // ─── Form actions ─────────────────────────────

        private void OnNewClicked()
        {
            _editingCategory = null;
            _isNewCategory = true;
            ClearForm();
            if (_formHeader != null) _formHeader.text = LocalizationService.Get("timetable.category_editor.form.header_new");
            if (_idInput != null) _idInput.interactable = true;
            SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.fill_form"), UITheme.SecondaryText);
        }

        private void LoadCategoryToForm(CommercialCategory cat)
        {
            _editingCategory = cat;
            _isNewCategory = false;
            if (_formHeader != null) _formHeader.text = LocalizationService.Get("timetable.category_editor.form.header_edit_format", cat.shortCode, cat.displayName);
            if (_idInput != null) { _idInput.text = cat.id; _idInput.interactable = false; }
            if (_shortCodeInput != null) _shortCodeInput.text = cat.shortCode ?? "";
            if (_displayNameInput != null) _displayNameInput.text = cat.displayName ?? "";
            if (_basePriceInput != null) _basePriceInput.text = cat.basePriceZl.ToString("F2");
            if (_pricePerKmInput != null) _pricePerKmInput.text = cat.pricePerKmZl.ToString("F2");
            if (_firstClassMultInput != null) _firstClassMultInput.text = cat.firstClassMultiplier.ToString("F2");
            if (_minStopSecInput != null) _minStopSecInput.text = cat.minStopSeconds.ToString();
            if (_trafficPriorityInput != null) _trafficPriorityInput.text = cat.trafficPriority.ToString();
            if (_maxSpeedInput != null) _maxSpeedInput.text = cat.suggestedMaxSpeedKmh.ToString();
            if (_emuToggle != null) _emuToggle.isOn = cat.defaultCompositionMode == CompositionMode.MultipleUnit;
            if (_stopPolicyDropdown != null) _stopPolicyDropdown.value = (int)cat.defaultStopPolicy;
            if (_airconToggle != null) _airconToggle.isOn = cat.requiresAirConditioning;
            if (_wifiToggle != null) _wifiToggle.isOn = cat.requiresWiFi;
            if (_socketsToggle != null) _socketsToggle.isOn = cat.requiresPowerSockets;
            if (_cateringToggle != null) _cateringToggle.isOn = cat.requiresCatering;
            if (_sleepingToggle != null) _sleepingToggle.isOn = cat.requiresSleepingCar;
            if (_notesInput != null) _notesInput.text = cat.notes ?? "";
            SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.edit_form"), UITheme.SecondaryText);
        }

        private void ClearForm()
        {
            if (_formHeader != null) _formHeader.text = LocalizationService.Get("timetable.category_editor.form.header_select_prompt");
            if (_idInput != null) { _idInput.text = ""; _idInput.interactable = true; }
            if (_shortCodeInput != null) _shortCodeInput.text = "";
            if (_displayNameInput != null) _displayNameInput.text = "";
            if (_basePriceInput != null) _basePriceInput.text = "10";
            if (_pricePerKmInput != null) _pricePerKmInput.text = "0.30";
            if (_firstClassMultInput != null) _firstClassMultInput.text = "1.50";
            if (_minStopSecInput != null) _minStopSecInput.text = "30";
            if (_trafficPriorityInput != null) _trafficPriorityInput.text = "1";
            if (_maxSpeedInput != null) _maxSpeedInput.text = "120";
            if (_emuToggle != null) _emuToggle.isOn = true;
            if (_stopPolicyDropdown != null) _stopPolicyDropdown.value = 0;
            if (_airconToggle != null) _airconToggle.isOn = false;
            if (_wifiToggle != null) _wifiToggle.isOn = false;
            if (_socketsToggle != null) _socketsToggle.isOn = false;
            if (_cateringToggle != null) _cateringToggle.isOn = false;
            if (_sleepingToggle != null) _sleepingToggle.isOn = false;
            if (_notesInput != null) _notesInput.text = "";
            SetFormStatus(string.Empty, UITheme.SecondaryText);
        }

        private void SaveForm()
        {
            // Walidacja
            string id = _idInput?.text?.Trim().ToLowerInvariant();
            string shortCode = _shortCodeInput?.text?.Trim();
            string displayName = _displayNameInput?.text?.Trim();

            if (string.IsNullOrEmpty(id))
            {
                SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.id_required"), UITheme.Danger);
                return;
            }
            if (string.IsNullOrEmpty(shortCode) || string.IsNullOrEmpty(displayName))
            {
                SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.name_required"), UITheme.Danger);
                return;
            }
            // ID może zawierać tylko litery, cyfry i podkreślnik
            foreach (char c in id)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.id_invalid_chars"), UITheme.Danger);
                    return;
                }
            }

            CommercialCategory cat;
            if (_isNewCategory)
            {
                if (TimetableService.GetCommercialCategory(id) != null)
                {
                    SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.id_already_exists_format", id), UITheme.Danger);
                    return;
                }
                cat = new CommercialCategory { id = id };
            }
            else
            {
                if (_editingCategory == null)
                {
                    SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.no_category_selected"), UITheme.Danger);
                    return;
                }
                cat = _editingCategory;
            }

            // Wpisz pola z formularza
            cat.shortCode = shortCode;
            cat.displayName = displayName;
            cat.basePriceZl = ParseFloat(_basePriceInput?.text, 0f);
            cat.pricePerKmZl = ParseFloat(_pricePerKmInput?.text, 0f);
            cat.firstClassMultiplier = ParseFloat(_firstClassMultInput?.text, 1.5f);
            cat.minStopSeconds = ParseInt(_minStopSecInput?.text, 30);
            cat.trafficPriority = ParseInt(_trafficPriorityInput?.text, 1);
            cat.suggestedMaxSpeedKmh = ParseInt(_maxSpeedInput?.text, 120);
            cat.defaultCompositionMode = (_emuToggle != null && _emuToggle.isOn)
                ? CompositionMode.MultipleUnit : CompositionMode.LocoWithCars;
            cat.defaultStopPolicy = (StopPolicy)(_stopPolicyDropdown?.value ?? 0);
            cat.requiresAirConditioning = _airconToggle?.isOn ?? false;
            cat.requiresWiFi = _wifiToggle?.isOn ?? false;
            cat.requiresPowerSockets = _socketsToggle?.isOn ?? false;
            cat.requiresCatering = _cateringToggle?.isOn ?? false;
            cat.requiresSleepingCar = _sleepingToggle?.isOn ?? false;
            cat.notes = _notesInput?.text;

            if (_isNewCategory)
            {
                if (!TimetableService.AddCommercialCategory(cat))
                {
                    SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.save_failed"), UITheme.Danger);
                    return;
                }
                Log.Info($"[CategoryEditor] Dodano kategorię: {cat.shortCode} — {cat.displayName}");
            }
            else
            {
                Log.Info($"[CategoryEditor] Zaktualizowano kategorię: {cat.shortCode} — {cat.displayName}");
            }

            SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.saved_format", cat.shortCode, cat.displayName), UITheme.Success);
            _editingCategory = cat;
            _isNewCategory = false;
            if (_idInput != null) _idInput.interactable = false;
            if (_formHeader != null) _formHeader.text = LocalizationService.Get("timetable.category_editor.form.header_edit_format", cat.shortCode, cat.displayName);
            RefreshTable();
        }

        private void DeleteCategory(string id)
        {
            if (TimetableService.RemoveCommercialCategory(id))
            {
                Log.Info($"[CategoryEditor] Usunięto kategorię: {id}");
                if (_editingCategory != null && _editingCategory.id == id)
                {
                    _editingCategory = null;
                    ClearForm();
                }
                RefreshTable();
            }
            else
            {
                SetFormStatus(LocalizationService.Get("timetable.category_editor.form.status.cant_delete_format", id), UITheme.Danger);
            }
        }

        private void SetFormStatus(string text, Color color)
        {
            if (_formStatus == null) return;
            _formStatus.text = text;
            _formStatus.color = color;
        }

        // ─── Parsery ─────────────────────────────────

        static int ParseInt(string s, int def) => int.TryParse(s, out int v) ? v : def;

        static float ParseFloat(string s, float def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            // Akceptuj zarówno przecinek jak i kropkę dla polskich userów
            s = s.Replace(',', '.');
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
        }
    }
}
