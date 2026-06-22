using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CategoryEditorUI
    {
        // ═══════════════════════════════════════════
        //  BUILD UI — panel layout + UI primitive helpers
        // ═══════════════════════════════════════════

        public void BuildUI(Transform canvas)
        {
            _raycaster = canvas.GetComponent<GraphicRaycaster>();

            _panel = new GameObject("CategoryEditor");
            _panel.transform.SetParent(canvas, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(75, 0);
            prt.offsetMax = new Vector2(0, -42);
            UITheme.ApplySurface(
                _panel.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Header
            var header = MakeRow(_panel.transform, 36);
            header.AddComponent<LayoutElement>().flexibleHeight = 0;
            UITheme.ApplySurface(header.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            Lbl(header.transform, "EDYTOR KATEGORII", 9, UITheme.PrimaryAccent);
            Lbl(header.transform, LocalizationService.Get("timetable.category_editor.title"), 16, Color.white);
            var spacer = new GameObject("Sp");
            spacer.transform.SetParent(header.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            Btn(header.transform, LocalizationService.Get("timetable.category_editor.button.new"),   OnNewClicked, UITheme.Success, 90);
            Btn(header.transform, LocalizationService.Get("timetable.category_editor.button.close"), Close,        UITheme.Danger, 34);

            Sep(_panel.transform);

            // Tabela kategorii — ScrollRect z stałymi rozmiarami
            Lbl(_panel.transform, LocalizationService.Get("timetable.category_editor.list_label"), 11, UITheme.PrimaryAccent);

            var scrollView = new GameObject("TableScrollView", typeof(RectTransform));
            scrollView.transform.SetParent(_panel.transform, false);
            var svLe = scrollView.AddComponent<LayoutElement>();
            svLe.flexibleHeight = 1;
            svLe.minHeight = 100;
            var svImg = scrollView.AddComponent<Image>();
            UITheme.ApplySurface(svImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            svImg.raycastTarget = true;
            var scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 25f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollView.transform, false);
            var vpRt = (RectTransform)viewport.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(vpImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f), UIShapePreset.Inset);
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = Vector2.zero;
            var cVlg = content.AddComponent<VerticalLayoutGroup>();
            cVlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            cVlg.spacing = UITheme.Spacing.Xxs;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;
            cVlg.childAlignment = TextAnchor.UpperLeft;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = vpRt;
            scrollRect.content = cRt;
            _tableContent = content.transform;

            Sep(_panel.transform);

            // Form header
            _formHeader = Lbl(_panel.transform, LocalizationService.Get("timetable.category_editor.form.header_select_prompt"),
                14, UITheme.WithAlpha(UITheme.PrimaryText, 0.98f));
            UITheme.ApplySurface(
                _formHeader.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.92f),
                UIShapePreset.Inset);

            // Form pola — pierwsza linia: ID, skrót, nazwa
            var rId = MakeRow(_panel.transform, 24);
            StyleRow(rId);
            Lbl(rId.transform, LocalizationService.Get("timetable.category_editor.form.field.id"), 11, new Color(0.7f, 0.7f, 0.7f));
            _idInput = Inp(rId.transform, "", 100, LocalizationService.Get("timetable.category_editor.form.placeholder.id"));
            Lbl(rId.transform, LocalizationService.Get("timetable.category_editor.form.field.short_code"), 11, new Color(0.7f, 0.7f, 0.7f));
            _shortCodeInput = Inp(rId.transform, "", 70, LocalizationService.Get("timetable.category_editor.form.placeholder.short_code"));
            Lbl(rId.transform, LocalizationService.Get("timetable.category_editor.form.field.name"), 11, new Color(0.7f, 0.7f, 0.7f));
            _displayNameInput = Inp(rId.transform, "", 240, LocalizationService.Get("timetable.category_editor.form.placeholder.name"));

            // Druga linia: ceny
            var rPrice = MakeRow(_panel.transform, 24);
            StyleRow(rPrice);
            Lbl(rPrice.transform, LocalizationService.Get("timetable.category_editor.form.field.base_price"), 11, new Color(0.7f, 0.7f, 0.7f));
            _basePriceInput = Inp(rPrice.transform, "10", 60);
            Lbl(rPrice.transform, LocalizationService.Get("timetable.category_editor.form.field.price_per_km"), 11, new Color(0.7f, 0.7f, 0.7f));
            _pricePerKmInput = Inp(rPrice.transform, "0.30", 60);
            Lbl(rPrice.transform, LocalizationService.Get("timetable.category_editor.form.field.first_class_mult"), 11, new Color(0.7f, 0.7f, 0.7f));
            _firstClassMultInput = Inp(rPrice.transform, "1.50", 60);

            // Trzecia linia: postoje + priorytet + Vmax
            var rOps = MakeRow(_panel.transform, 24);
            StyleRow(rOps);
            Lbl(rOps.transform, LocalizationService.Get("timetable.category_editor.form.field.min_stop_sec"), 11, new Color(0.7f, 0.7f, 0.7f));
            _minStopSecInput = Inp(rOps.transform, "30", 50);
            Lbl(rOps.transform, LocalizationService.Get("timetable.category_editor.form.field.priority"), 11, new Color(0.7f, 0.7f, 0.7f));
            _trafficPriorityInput = Inp(rOps.transform, "1", 40);
            Lbl(rOps.transform, LocalizationService.Get("timetable.category_editor.form.field.vmax_suggestion"), 11, new Color(0.7f, 0.7f, 0.7f));
            _maxSpeedInput = Inp(rOps.transform, "120", 60);

            // Czwarta linia: tabor + polityka postojów
            var rPol = MakeRow(_panel.transform, 24);
            StyleRow(rPol);
            _emuToggle = MakeToggle(rPol.transform, LocalizationService.Get("timetable.category_editor.form.field.default_emu_dmu"), true);
            Lbl(rPol.transform, LocalizationService.Get("timetable.category_editor.form.field.stops_policy"), 11, new Color(0.7f, 0.7f, 0.7f));
            _stopPolicyDropdown = MakePolicyDropdown(rPol.transform);

            // Piąta linia: wymagania taboru
            var rReq = MakeRow(_panel.transform, 24);
            StyleRow(rReq);
            Lbl(rReq.transform, LocalizationService.Get("timetable.category_editor.form.field.requires"), 11, new Color(0.7f, 0.7f, 0.7f));
            _airconToggle = MakeToggle(rReq.transform, "klima", false);
            _wifiToggle = MakeToggle(rReq.transform, LocalizationService.Get("timetable.category_editor.form.field.wifi"), false);
            _socketsToggle = MakeToggle(rReq.transform, "gniazdka", false);
            _cateringToggle = MakeToggle(rReq.transform, LocalizationService.Get("timetable.category_editor.form.field.restaurant"), false);
            _sleepingToggle = MakeToggle(rReq.transform, LocalizationService.Get("timetable.category_editor.form.field.sleeping"), false);

            // Notatki
            var rNotes = MakeRow(_panel.transform, 24);
            StyleRow(rNotes);
            Lbl(rNotes.transform, LocalizationService.Get("timetable.category_editor.form.field.notes"), 11, new Color(0.7f, 0.7f, 0.7f));
            _notesInput = Inp(rNotes.transform, "", 600, LocalizationService.Get("timetable.category_editor.form.placeholder.notes"));

            // Status + akcje
            _formStatus = Lbl(_panel.transform, "", 11, UITheme.SecondaryText);
            UITheme.ApplySurface(
                _formStatus.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.88f),
                UIShapePreset.Inset);
            var actionRow = MakeRow(_panel.transform, 32);
            StyleRow(actionRow);
            Btn(actionRow.transform, LocalizationService.Get("timetable.category_editor.button.clear_form"), () => { _editingCategory = null; _isNewCategory = false; ClearForm(); },
                new Color(0.3f, 0.3f, 0.4f));
            Btn(actionRow.transform, LocalizationService.Get("timetable.category_editor.button.save"), SaveForm, new Color(0.2f, 0.7f, 0.3f));

            _panel.SetActive(false);
        }

        // ─── UI helpers ──────────────────────────

        TextMeshProUGUI Lbl(Transform p, string t, int sz, Color c)
        {
            var o = new GameObject("L"); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = sz + 6;
            le.flexibleHeight = 0;
            var tx = o.AddComponent<TextMeshProUGUI>();
            tx.fontSize = sz; tx.color = c; tx.text = t;
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            tx.overflowMode = TextOverflowModes.Overflow;
            UITheme.ApplyTmpText(tx, c == Color.white ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
            tx.color = c;
            // Heurystyka preferredWidth: ~0.55 × fontSize per znak (LegacyRuntime font).
            int len = t?.Length ?? 0;
            le.preferredWidth = Mathf.Max(16f, len * sz * 0.55f + 8f);
            return tx;
        }

        GameObject MakeRow(Transform p, float h = 26)
        {
            var o = new GameObject("R"); o.transform.SetParent(p, false);
            o.AddComponent<RectTransform>();
            var hlg = o.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Sm; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = h;
            le.flexibleHeight = 0;
            return o;
        }

        void StyleRow(GameObject row)
        {
            if (row == null) return;

            UITheme.ApplySurface(
                row.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.92f),
                UIShapePreset.Inset);

            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
                hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xxs);

            var le = row.GetComponent<LayoutElement>();
            if (le != null && le.preferredHeight < 30)
                le.preferredHeight = 30;
        }

        void Sep(Transform p)
        {
            var o = new GameObject("S"); o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = 12;
            var line = new GameObject("Line");
            line.transform.SetParent(o.transform, false);
            var rt = line.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(8f, -0.5f);
            rt.offsetMax = new Vector2(-8f, 0.5f);
            line.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.Border, 0.45f);
        }

        Button Btn(Transform p, string label, System.Action onClick, Color bg, float w = -1)
        {
            var o = new GameObject(label); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;
            le.preferredHeight = 24;
            le.flexibleHeight = 0;
            var img = o.AddComponent<Image>();
            var btn = o.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var l = new GameObject("L"); l.transform.SetParent(o.transform, false);
            var lrt = l.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var tx = l.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 11; tx.alignment = TextAlignmentOptions.Center;
            tx.text = label;
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);
            return btn;
        }

        TMP_InputField Inp(Transform p, string def, float w, string placeholder = null)
        {
            var o = new GameObject("I"); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 22;
            var bg = o.AddComponent<Image>();

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children of viewport.
            var viewportObj = new GameObject("Viewport"); viewportObj.transform.SetParent(o.transform, false);
            var viewportRt = viewportObj.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(4, 0); viewportRt.offsetMax = new Vector2(-4, 0);
            viewportObj.AddComponent<RectMask2D>();

            var t = new GameObject("T"); t.transform.SetParent(viewportObj.transform, false);
            var trt = t.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tx = t.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 11;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            tx.raycastTarget = false;

            var inp = o.AddComponent<TMP_InputField>();
            inp.textViewport = viewportRt;
            inp.textComponent = tx;
            inp.lineType = TMP_InputField.LineType.SingleLine;
            inp.text = def;

            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = new GameObject("PH"); ph.transform.SetParent(viewportObj.transform, false);
                var phrt = ph.AddComponent<RectTransform>();
                phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
                phrt.offsetMin = Vector2.zero; phrt.offsetMax = Vector2.zero;
                var phtx = ph.AddComponent<TextMeshProUGUI>();
                phtx.fontSize = 11;
                phtx.fontStyle = FontStyles.Italic;
                phtx.alignment = TextAlignmentOptions.MidlineLeft;
                phtx.textWrappingMode = TextWrappingModes.NoWrap;
                phtx.raycastTarget = false;
                phtx.text = placeholder;
                inp.placeholder = phtx;
                UITheme.ApplyTmpInputField(inp, bg, tx, phtx);
            }
            else
            {
                UITheme.ApplyTmpInputField(inp, bg, tx, null);
            }
            return inp;
        }

        Toggle MakeToggle(Transform p, string label, bool on)
        {
            // Heurystyka: 14 checkbox + 4 spacing + label width
            int labelChars = label?.Length ?? 0;
            float labelWidth = Mathf.Max(20f, labelChars * 6.2f + 4f);
            float totalWidth = 14f + 4f + labelWidth;

            var o = new GameObject("Tgl", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var oLe = o.AddComponent<LayoutElement>();
            oLe.preferredWidth = totalWidth;
            oLe.preferredHeight = 22;
            oLe.flexibleWidth = 0;
            oLe.flexibleHeight = 0;
            var hh = o.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = UITheme.Spacing.Xs;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;
            hh.childAlignment = TextAnchor.MiddleLeft;

            // Checkbox bg 14x14
            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(o.transform, false);
            var bgLe = bg.AddComponent<LayoutElement>();
            bgLe.preferredWidth = 14; bgLe.preferredHeight = 14;
            bgLe.flexibleWidth = 0; bgLe.flexibleHeight = 0;
            var bgi = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgi, UITheme.TopBarInset, UIShapePreset.Inset);

            var ch = new GameObject("Ch", typeof(RectTransform));
            ch.transform.SetParent(bg.transform, false);
            var chrt = (RectTransform)ch.transform;
            chrt.anchorMin = new Vector2(0.2f, 0.2f); chrt.anchorMax = new Vector2(0.8f, 0.8f);
            chrt.offsetMin = Vector2.zero; chrt.offsetMax = Vector2.zero;
            var chi = ch.AddComponent<Image>();
            UITheme.ApplySurface(chi, UITheme.PrimaryAccent, UIShapePreset.Inset);

            var tgl = o.AddComponent<Toggle>(); tgl.isOn = on;
            tgl.targetGraphic = bgi; tgl.graphic = chi;
            tgl.colors = UITheme.CreateColorBlock(
                UITheme.TopBarInset,
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var lb = new GameObject("L", typeof(RectTransform));
            lb.transform.SetParent(o.transform, false);
            var lbLe = lb.AddComponent<LayoutElement>();
            lbLe.preferredWidth = labelWidth;
            lbLe.preferredHeight = 18;
            lbLe.flexibleWidth = 0;
            var lx = lb.AddComponent<TextMeshProUGUI>();
            lx.fontSize = 11; lx.text = label;
            lx.alignment = TextAlignmentOptions.MidlineLeft;
            lx.textWrappingMode = TextWrappingModes.NoWrap;
            UITheme.ApplyTmpText(lx, UIThemeTextRole.Primary);
            return tgl;
        }

        TMP_Dropdown MakePolicyDropdown(Transform p)
        {
            var o = new GameObject("PolicyDD"); o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 180;
            le.preferredHeight = 22;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);

            var cap = new GameObject("Cap"); cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(4, 0); crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 11;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);

            var tmpl = new GameObject("Tmpl"); tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f); trt.sizeDelta = new Vector2(0, 70);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            tmpl.SetActive(false);

            var item = new GameObject("Item"); item.transform.SetParent(tmpl.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBg;
            toggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.92f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var ilbl = new GameObject("IL"); ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0); ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 11;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx; dd.itemText = iltx; dd.template = trt;
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new("wszystkie stacje"),
                new("tylko duże stacje"),
                new("ręcznie per trasa")
            };
            return dd;
        }
    }
}
