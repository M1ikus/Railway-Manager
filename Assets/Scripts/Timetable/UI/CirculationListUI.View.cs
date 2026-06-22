using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        public void BuildUI(Transform canvas)
        {
            _raycaster = canvas.GetComponent<GraphicRaycaster>();
            _rootCanvas = canvas.GetComponent<Canvas>();

            _panel = new GameObject("CirculationList", typeof(RectTransform));
            _panel.transform.SetParent(canvas, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(75, 0);
            prt.offsetMax = new Vector2(0, -42);
            UITheme.ApplySurface(
                _panel.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var topBar = MakeHRow(_panel.transform, 52);
            topBar.GetComponent<LayoutElement>().flexibleHeight = 0;
            UITheme.ApplySurface(topBar.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var topBarLayout = topBar.GetComponent<HorizontalLayoutGroup>();
            topBarLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            topBarLayout.spacing = UITheme.Spacing.Sm;

            var titleStack = new GameObject("TitleStack", typeof(RectTransform));
            titleStack.transform.SetParent(topBar.transform, false);
            titleStack.AddComponent<LayoutElement>().flexibleWidth = 1;
            var titleStackVlg = titleStack.AddComponent<VerticalLayoutGroup>();
            titleStackVlg.spacing = UITheme.Spacing.Xxs;
            titleStackVlg.childForceExpandWidth = true;
            titleStackVlg.childForceExpandHeight = false;
            titleStackVlg.childAlignment = TextAnchor.MiddleLeft;
            MakeText(titleStack.transform, "OBIEGI I TABOR", 10, UITheme.PrimaryAccent, preferredWidth: 0);
            MakeText(titleStack.transform, LocalizationService.Get("timetable.circulations.title"), 15, Color.white, preferredWidth: 0);
            var spacer = new GameObject("Sp", typeof(RectTransform));
            spacer.transform.SetParent(topBar.transform, false);
            spacer.AddComponent<LayoutElement>().preferredWidth = 8;
            MakeBtn(topBar.transform, LocalizationService.Get("timetable.circulations.button.new_empty"), OnNewEmptyClicked, new Color(0.2f, 0.6f, 0.3f), 166);
            MakeBtn(topBar.transform, LocalizationService.Get("timetable.circulations.button.auto_generate"), OnAutoGenClicked, new Color(0.3f, 0.45f, 0.65f), 138);
            MakeBtn(topBar.transform, LocalizationService.Get("timetable.circulations.button.close"), Close, new Color(0.6f, 0.2f, 0.2f), 104);

            Sep(_panel.transform);

            SectionHeader(
                _panel.transform,
                "PRZEGLAD",
                LocalizationService.Get("timetable.circulations.title"),
                "Lewa kolumna sluzy do budowania obiegow, prawa trzyma pule rozkladow gotowych do przypisania.");

            var mainRow = new GameObject("MainRow", typeof(RectTransform));
            mainRow.transform.SetParent(_panel.transform, false);
            var mainLe = mainRow.AddComponent<LayoutElement>();
            mainLe.flexibleHeight = 1;
            mainLe.minHeight = 250;
            var mainHlg = mainRow.AddComponent<HorizontalLayoutGroup>();
            mainHlg.spacing = UITheme.Spacing.Md;
            mainHlg.childForceExpandWidth = false;
            mainHlg.childForceExpandHeight = true;

            var leftCol = BuildColumn(
                mainRow.transform,
                LocalizationService.Get("timetable.circulations.column.circulations"),
                "Buduj kroki obiegu i sprawdzaj, czy wszystkie przejazdy oraz postoje ukladaja sie poprawnie.",
                1.6f);
            _circulationsContent = leftCol;

            var rightCol = BuildColumn(
                mainRow.transform,
                LocalizationService.Get("timetable.circulations.column.schedules_pool"),
                "Wybieraj gotowe rozklady z puli i dopinaj je do obiegu bez gubienia kontekstu.",
                1f);
            _schedulesPoolContent = rightCol;

            Sep(_panel.transform);

            var warn = new GameObject("Warnings", typeof(RectTransform));
            warn.transform.SetParent(_panel.transform, false);
            var wle = warn.AddComponent<LayoutElement>();
            wle.preferredHeight = 104;
            wle.flexibleHeight = 0;
            UITheme.ApplySurface(warn.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            var wvlg = warn.AddComponent<VerticalLayoutGroup>();
            wvlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            wvlg.spacing = UITheme.Spacing.Xs;
            wvlg.childForceExpandWidth = true;
            wvlg.childForceExpandHeight = false;

            var warnHeader = MakeText(warn.transform, "UWAGI I WALIDACJA", 11, UITheme.PrimaryAccent, preferredWidth: 0);
            warnHeader.fontStyle = FontStyles.Bold;
            MakeText(
                warn.transform,
                "Tutaj pojawiaja sie konflikty, braki i komunikaty do poprawienia przed uruchomieniem obiegu.",
                11,
                UITheme.SecondaryText,
                preferredWidth: 0);

            var warnContent = new GameObject("WarningsContent", typeof(RectTransform));
            warnContent.transform.SetParent(warn.transform, false);
            warnContent.AddComponent<LayoutElement>().flexibleHeight = 1;
            var warnContentVlg = warnContent.AddComponent<VerticalLayoutGroup>();
            warnContentVlg.spacing = UITheme.Spacing.Xxs;
            warnContentVlg.childForceExpandWidth = true;
            warnContentVlg.childForceExpandHeight = false;
            _warningsContent = warnContent.transform;

            _panel.SetActive(false);
        }

        private Transform BuildColumn(Transform parent, string title, string description, float flexWidth)
        {
            var col = new GameObject("Col", typeof(RectTransform));
            col.transform.SetParent(parent, false);
            UITheme.ApplySurface(col.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            var le = col.AddComponent<LayoutElement>();
            le.flexibleWidth = flexWidth;
            le.minWidth = 280;
            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var hdr = MakeText(col.transform, title, 12, new Color(0.55f, 0.75f, 1f));
            hdr.fontStyle = FontStyles.Bold;
            hdr.gameObject.GetComponent<LayoutElement>().preferredHeight = 20;
            var desc = MakeText(col.transform, description, 11, UITheme.SecondaryText, preferredWidth: 0);
            desc.textWrappingMode = TextWrappingModes.Normal;

            var sepGo = new GameObject("Sep", typeof(RectTransform));
            sepGo.transform.SetParent(col.transform, false);
            sepGo.AddComponent<LayoutElement>().preferredHeight = 1;
            sepGo.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.Border, 0.5f);

            var scrollView = new GameObject("ScrollView", typeof(RectTransform));
            scrollView.transform.SetParent(col.transform, false);
            var svLe = scrollView.AddComponent<LayoutElement>();
            svLe.flexibleHeight = 1;
            svLe.minHeight = 100;
            var svImg = scrollView.AddComponent<Image>();
            UITheme.ApplySurface(svImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.38f), UIShapePreset.Panel);
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
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
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
            cRt.sizeDelta = new Vector2(0, 0);
            var cVlg = content.AddComponent<VerticalLayoutGroup>();
            cVlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            cVlg.spacing = UITheme.Spacing.Xs;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;
            cVlg.childAlignment = TextAnchor.UpperLeft;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = vpRt;
            scrollRect.content = cRt;

            return content.transform;
        }

        private TMP_InputField MakeInputField(Transform p, string def, float w, string placeholder = null)
        {
            var o = new GameObject("I", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 30;
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;
            var bg = o.AddComponent<Image>();

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children of viewport.
            var viewportObj = new GameObject("Viewport", typeof(RectTransform));
            viewportObj.transform.SetParent(o.transform, false);
            var viewportRt = (RectTransform)viewportObj.transform;
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(4, 0);
            viewportRt.offsetMax = new Vector2(-4, 0);
            viewportObj.AddComponent<RectMask2D>();

            var t = new GameObject("T", typeof(RectTransform));
            t.transform.SetParent(viewportObj.transform, false);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tx = t.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 12;
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
                var ph = new GameObject("PH", typeof(RectTransform));
                ph.transform.SetParent(viewportObj.transform, false);
                var phrt = (RectTransform)ph.transform;
                phrt.anchorMin = Vector2.zero;
                phrt.anchorMax = Vector2.one;
                phrt.offsetMin = Vector2.zero;
                phrt.offsetMax = Vector2.zero;
                var phtx = ph.AddComponent<TextMeshProUGUI>();
                phtx.fontSize = 12;
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

        private GameObject SectionHeader(Transform parent, string eyebrow, string title, string description)
        {
            var card = new GameObject("SectionHeader", typeof(RectTransform));
            card.transform.SetParent(parent, false);
            UITheme.ApplySurface(
                card.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f),
                UIShapePreset.Panel);
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = 58;

            var eye = MakeText(card.transform, eyebrow, 10, UITheme.PrimaryAccent, preferredWidth: 0);
            eye.fontStyle = FontStyles.Bold;
            var head = MakeText(card.transform, title, 13, Color.white, preferredWidth: 0);
            head.fontStyle = FontStyles.Bold;
            var body = MakeText(card.transform, description, 11, UITheme.SecondaryText, preferredWidth: 0);
            body.textWrappingMode = TextWrappingModes.Normal;
            return card;
        }

        private Toggle MakeSimpleToggle(Transform p, string label, bool isOn)
        {
            var row = new GameObject("SimpleTgl", typeof(RectTransform));
            row.transform.SetParent(p, false);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 28;
            rowLe.flexibleHeight = 0;
            var rowHh = row.AddComponent<HorizontalLayoutGroup>();
            rowHh.spacing = UITheme.Spacing.Sm;
            rowHh.childForceExpandWidth = false;
            rowHh.childForceExpandHeight = true;
            rowHh.childAlignment = TextAnchor.MiddleLeft;

            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(row.transform, false);
            var bgLe = bg.AddComponent<LayoutElement>();
            bgLe.preferredWidth = 14;
            bgLe.preferredHeight = 14;
            var bgi = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgi, UITheme.TopBarInset, UIShapePreset.Inset);

            var ch = new GameObject("Ch", typeof(RectTransform));
            ch.transform.SetParent(bg.transform, false);
            var chrt = (RectTransform)ch.transform;
            chrt.anchorMin = new Vector2(0.15f, 0.15f);
            chrt.anchorMax = new Vector2(0.85f, 0.85f);
            chrt.offsetMin = Vector2.zero;
            chrt.offsetMax = Vector2.zero;
            var chi = ch.AddComponent<Image>();
            UITheme.ApplySurface(chi, UITheme.PrimaryAccent, UIShapePreset.Inset);

            var tgl = row.AddComponent<Toggle>();
            tgl.isOn = isOn;
            tgl.targetGraphic = bgi;
            tgl.graphic = chi;
            tgl.colors = UITheme.CreateColorBlock(
                UITheme.TopBarInset,
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var lbl = new GameObject("L", typeof(RectTransform));
            lbl.transform.SetParent(row.transform, false);
            var lblLe = lbl.AddComponent<LayoutElement>();
            lblLe.flexibleWidth = 1;
            lblLe.preferredHeight = 18;
            var lblTx = lbl.AddComponent<TextMeshProUGUI>();
            lblTx.fontSize = 11;
            lblTx.alignment = TextAlignmentOptions.MidlineLeft;
            lblTx.raycastTarget = false;
            lblTx.text = label;
            UITheme.ApplyTmpText(lblTx, UIThemeTextRole.Primary);

            return tgl;
        }

        private Toggle MakeDayToggle(Transform p, string label, bool isOn)
        {
            var o = new GameObject("DayTgl", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 34;
            le.preferredHeight = 26;
            var hh = o.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = UITheme.Spacing.Xxs;
            hh.padding = UITheme.Padding(0f, UITheme.Spacing.Xxs, 0f, 0f);
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = true;
            hh.childAlignment = TextAnchor.MiddleLeft;

            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(o.transform, false);
            bg.AddComponent<LayoutElement>().preferredWidth = 13;
            ((RectTransform)bg.transform).sizeDelta = new Vector2(13, 13);
            var bgImg = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.TopBarInset, UIShapePreset.Inset);

            var ch = new GameObject("Ch", typeof(RectTransform));
            ch.transform.SetParent(bg.transform, false);
            var chRt = (RectTransform)ch.transform;
            chRt.anchorMin = new Vector2(0.15f, 0.15f);
            chRt.anchorMax = new Vector2(0.85f, 0.85f);
            chRt.offsetMin = Vector2.zero;
            chRt.offsetMax = Vector2.zero;
            var chImg = ch.AddComponent<Image>();
            UITheme.ApplySurface(chImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

            var lbObj = new GameObject("L", typeof(RectTransform));
            lbObj.transform.SetParent(o.transform, false);
            lbObj.AddComponent<LayoutElement>().preferredWidth = 18;
            var lx = lbObj.AddComponent<TextMeshProUGUI>();
            lx.fontSize = 10;
            lx.text = label;
            lx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(lx, UIThemeTextRole.Secondary);

            var tgl = o.AddComponent<Toggle>();
            tgl.isOn = isOn;
            tgl.targetGraphic = bgImg;
            tgl.graphic = chImg;
            tgl.colors = UITheme.CreateColorBlock(
                UITheme.TopBarInset,
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.45f));
            return tgl;
        }

        private TextMeshProUGUI MakeText(Transform p, string t, int sz, Color c, float preferredWidth = -1, bool center = false)
        {
            var o = new GameObject("L", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = sz + 6;
            le.flexibleHeight = 0;
            if (preferredWidth > 0)
            {
                le.preferredWidth = preferredWidth;
                le.flexibleWidth = 0;
            }
            else if (preferredWidth == 0)
            {
                le.flexibleWidth = 1;
            }
            else
            {
                int len = t?.Length ?? 0;
                le.preferredWidth = Mathf.Max(16f, len * sz * 0.55f + 8f);
                le.flexibleWidth = 0;
            }
            var tx = o.AddComponent<TextMeshProUGUI>();
            tx.fontSize = sz;
            tx.text = t;
            tx.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            tx.overflowMode = TextOverflowModes.Overflow;
            tx.raycastTarget = false;
            UITheme.ApplyTmpText(tx, c == Color.white ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
            tx.color = c;
            if (center)
            {
                var rt = (RectTransform)o.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            return tx;
        }

        private TextMeshProUGUI MakeTextFlex(Transform p, string t, int sz, Color c)
        {
            var o = new GameObject("LFlex", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = sz + 6;
            var tx = o.AddComponent<TextMeshProUGUI>();
            tx.fontSize = sz;
            tx.text = t;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.raycastTarget = false;
            UITheme.ApplyTmpText(tx, c == Color.white ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
            tx.color = c;
            return tx;
        }

        private GameObject MakeHRow(Transform p, float h = 26)
        {
            var o = new GameObject("HR", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var hlg = o.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Sm;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            o.AddComponent<LayoutElement>().preferredHeight = h;
            return o;
        }

        private void Sep(Transform p)
        {
            var o = new GameObject("S", typeof(RectTransform));
            o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = 1;
            o.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.Border, 0.45f);
        }

        private Button MakeBtn(Transform p, string label, System.Action onClick, Color bg, float w = -1)
        {
            var o = new GameObject(label, typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;
            le.preferredHeight = 30;
            le.flexibleHeight = 0;
            var img = o.AddComponent<Image>();
            var btn = o.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var l = new GameObject("L", typeof(RectTransform));
            l.transform.SetParent(o.transform, false);
            var lrt = (RectTransform)l.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var tx = l.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 12;
            tx.alignment = TextAlignmentOptions.Center;
            tx.text = label;
            tx.raycastTarget = false;
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);
            return btn;
        }

        private Button MakeSmallBtn(Transform p, string label, System.Action onClick, Color bg, float w)
        {
            return MakeBtn(p, label, onClick, bg, w);
        }

        private TMP_Dropdown MakeDropdown(Transform p, float w)
        {
            var o = new GameObject("DD", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 30;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);
            var cap = new GameObject("Cap", typeof(RectTransform));
            cap.transform.SetParent(o.transform, false);
            var crt = (RectTransform)cap.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(4, 0);
            crt.offsetMax = new Vector2(-16, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 11;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);
            var tmpl = new GameObject("Tmpl", typeof(RectTransform));
            tmpl.transform.SetParent(o.transform, false);
            var trt = (RectTransform)tmpl.transform;
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, 180);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            tmpl.SetActive(false);
            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(tmpl.transform, false);
            var irt = (RectTransform)item.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
            var itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBg;
            itemToggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.92f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));
            var ilbl = new GameObject("IL", typeof(RectTransform));
            ilbl.transform.SetParent(item.transform, false);
            var ilrt = (RectTransform)ilbl.transform;
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 11;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);
            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx;
            dd.itemText = iltx;
            dd.template = trt;
            return dd;
        }

        private void MakeStatusDropdown(Transform p, CirculationStatus initial, UnityEngine.Events.UnityAction<int> onChanged)
        {
            var o = new GameObject("StatusDD", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 90;
            le.preferredHeight = 28;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.9f), UIShapePreset.Inset);
            var cap = new GameObject("Cap", typeof(RectTransform));
            cap.transform.SetParent(o.transform, false);
            var crt = (RectTransform)cap.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 0);
            crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 10;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);
            var tmpl = new GameObject("Tmpl", typeof(RectTransform));
            tmpl.transform.SetParent(o.transform, false);
            var trt = (RectTransform)tmpl.transform;
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, 90);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            tmpl.SetActive(false);
            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(tmpl.transform, false);
            var irt = (RectTransform)item.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
            var itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBg;
            itemToggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.92f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));
            var ilbl = new GameObject("IL", typeof(RectTransform));
            ilbl.transform.SetParent(item.transform, false);
            var ilrt = (RectTransform)ilbl.transform;
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 10;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);
            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx;
            dd.itemText = iltx;
            dd.template = trt;
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new(LocalizationService.Get("timetable.circulations.status.draft")),
                new(LocalizationService.Get("timetable.circulations.status.active")),
                new(LocalizationService.Get("timetable.circulations.status.paused")),
                new(LocalizationService.Get("timetable.circulations.status.archived"))
            };
            dd.value = (int)initial;
            dd.RefreshShownValue();
            dd.onValueChanged.AddListener(onChanged);
        }
    }
}
