using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class VehicleAssignmentModal
    {
        // ─────────────────────────────────────────────
        //  Build UI — panel + columns + helpers
        // ─────────────────────────────────────────────

        public void BuildUI(Transform canvas)
        {
            if (canvas == null)
            {
                Log.Warn("[VehicleAssignmentModal] BuildUI called with null canvas");
                return;
            }
            _rootCanvas = canvas.GetComponent<Canvas>();

            _panel = new GameObject("VehicleAssignmentModal", typeof(RectTransform));
            _panel.transform.SetParent(canvas, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = new Vector2(0.05f, 0.05f);
            prt.anchorMax = new Vector2(0.95f, 0.95f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(
                _panel.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Top bar
            var topBar = new GameObject("Top", typeof(RectTransform));
            topBar.transform.SetParent(_panel.transform, false);
            var topLe = topBar.AddComponent<LayoutElement>();
            topLe.preferredHeight = 56; topLe.flexibleHeight = 0;
            UITheme.ApplySurface(topBar.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var topHlg = topBar.AddComponent<HorizontalLayoutGroup>();
            topHlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            topHlg.spacing = UITheme.Spacing.Md;
            topHlg.childForceExpandWidth = false;
            topHlg.childForceExpandHeight = true;
            topHlg.childAlignment = TextAnchor.MiddleLeft;

            var titleStack = new GameObject("TitleStack", typeof(RectTransform));
            titleStack.transform.SetParent(topBar.transform, false);
            var titleStackLE = titleStack.AddComponent<LayoutElement>();
            titleStackLE.flexibleWidth = 1;
            var titleStackVlg = titleStack.AddComponent<VerticalLayoutGroup>();
            titleStackVlg.spacing = UITheme.Spacing.Xxs;
            titleStackVlg.childForceExpandWidth = true;
            titleStackVlg.childForceExpandHeight = false;
            titleStackVlg.childAlignment = TextAnchor.MiddleLeft;
            MakeText(titleStack.transform, "PRZYPISANIE TABORU", 10, UITheme.PrimaryAccent, 0);
            _titleText = MakeText(titleStack.transform, LocalizationService.Get("timetable.vehicle_assign.title"), 16, Color.white, 0);

            var sp = new GameObject("Sp", typeof(RectTransform));
            sp.transform.SetParent(topBar.transform, false);
            sp.AddComponent<LayoutElement>().preferredWidth = 8;
            MakeBtn(topBar.transform, LocalizationService.Get("timetable.vehicle_assign.save_btn"), OnSaveClicked, new Color(0.2f, 0.7f, 0.3f), 116);
            MakeBtn(topBar.transform, LocalizationService.Get("timetable.vehicle_assign.cancel_btn"), Close, new Color(0.5f, 0.3f, 0.3f), 104);

            BuildIntroCard(
                _panel.transform,
                "PLAN PRZYPISANIA",
                "Ukladaj pojazdy dzien po dniu i kontroluj, czy sklad jest kompletny zanim zapiszesz zmiany.");

            // Status line
            var statusCard = new GameObject("StatusCard", typeof(RectTransform));
            statusCard.transform.SetParent(_panel.transform, false);
            statusCard.AddComponent<LayoutElement>().preferredHeight = 40;
            UITheme.ApplySurface(statusCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            var statusHlg = statusCard.AddComponent<HorizontalLayoutGroup>();
            statusHlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            statusHlg.spacing = UITheme.Spacing.Sm;
            statusHlg.childForceExpandWidth = true;
            statusHlg.childForceExpandHeight = true;
            statusHlg.childAlignment = TextAnchor.MiddleLeft;
            MakeText(statusCard.transform, "STATUS", 10, UITheme.PrimaryAccent, 74);
            _statusText = MakeText(statusCard.transform, "", 11, new Color(0.6f, 0.6f, 0.6f), 0);
            _statusText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;

            // Main row: left days + right pool
            var mainRow = new GameObject("MainRow", typeof(RectTransform));
            mainRow.transform.SetParent(_panel.transform, false);
            var mainLe = mainRow.AddComponent<LayoutElement>();
            mainLe.flexibleHeight = 1; mainLe.minHeight = 320;
            var mainHlg = mainRow.AddComponent<HorizontalLayoutGroup>();
            mainHlg.spacing = UITheme.Spacing.Md;
            mainHlg.childForceExpandWidth = false;
            mainHlg.childForceExpandHeight = true;

            _daysContent = BuildScrollColumn(
                mainRow.transform,
                LocalizationService.Get("timetable.vehicle_assign.column.days"),
                "Rozpisz dzienne zestawienia i pilnuj, ktore pojazdy pracuja juz w innych obiegach.",
                2.0f);
            _poolContent = BuildScrollColumn(
                mainRow.transform,
                LocalizationService.Get("timetable.vehicle_assign.column.pool"),
                "Wybieraj z puli i szybko sprawdzaj, co jest jeszcze dostepne do przypisania.",
                1.0f);

            _panel.SetActive(false);
        }

        private Transform BuildScrollColumn(Transform parent, string title, string description, float flex)
        {
            var col = new GameObject("Col", typeof(RectTransform));
            col.transform.SetParent(parent, false);
            UITheme.ApplySurface(col.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            var le = col.AddComponent<LayoutElement>();
            le.flexibleWidth = flex; le.minWidth = 280;
            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = MakeText(col.transform, title, 12, new Color(0.55f, 0.75f, 1f), 0);
            hdr.fontStyle = FontStyles.Bold;
            hdr.gameObject.GetComponent<LayoutElement>().preferredHeight = 22;
            var desc = MakeText(col.transform, description, 11, UITheme.SecondaryText, 0);
            desc.textWrappingMode = TextWrappingModes.Normal;
            var sep = new GameObject("S", typeof(RectTransform));
            sep.transform.SetParent(col.transform, false);
            sep.AddComponent<LayoutElement>().preferredHeight = 1;
            sep.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.Border, 0.5f);

            var scroll = new GameObject("Scroll", typeof(RectTransform));
            scroll.transform.SetParent(col.transform, false);
            var sle = scroll.AddComponent<LayoutElement>();
            sle.flexibleHeight = 1; sle.minHeight = 200;
            var sImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(sImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.38f), UIShapePreset.Panel);
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 25f;

            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(scroll.transform, false);
            var vpRt = (RectTransform)vp.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(vp.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
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

            sr.viewport = vpRt;
            sr.content = cRt;
            return content.transform;
        }

        private void BuildIntroCard(Transform parent, string title, string description)
        {
            var card = new GameObject("IntroCard", typeof(RectTransform));
            card.transform.SetParent(parent, false);
            UITheme.ApplySurface(
                card.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f),
                UIShapePreset.Panel);
            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = 58;
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = MakeText(card.transform, title, 10, UITheme.PrimaryAccent, 0);
            hdr.fontStyle = FontStyles.Bold;
            var body = MakeText(card.transform, description, 11, UITheme.SecondaryText, 0);
            body.textWrappingMode = TextWrappingModes.Normal;
        }

        private TextMeshProUGUI MakeText(Transform p, string t, int sz, Color c, float preferredWidth)
        {
            var o = new GameObject("L", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = sz + 8;
            if (preferredWidth > 0) le.preferredWidth = preferredWidth;
            var tx = o.AddComponent<TextMeshProUGUI>();
            tx.fontSize = sz; tx.color = c; tx.text = t;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.raycastTarget = false;
            tx.overflowMode = TextOverflowModes.Overflow;
            UITheme.ApplyTmpText(tx, c == Color.white ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
            tx.color = c;
            return tx;
        }

        private Button MakeBtn(Transform p, string label, System.Action onClick, Color bg, float w)
        {
            var o = new GameObject(label, typeof(RectTransform));
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = 32;
            le.flexibleWidth = 0; le.flexibleHeight = 0;
            var img = o.AddComponent<Image>();
            var btn = o.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var l = new GameObject("L", typeof(RectTransform));
            l.transform.SetParent(o.transform, false);
            var lrt = (RectTransform)l.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var tx = l.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 12; tx.alignment = TextAlignmentOptions.Center;
            tx.text = label;
            tx.raycastTarget = false;
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);
            return btn;
        }
    }
}
