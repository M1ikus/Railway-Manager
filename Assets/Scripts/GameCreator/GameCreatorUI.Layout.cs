using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═══════════════════════════════════════════
        //  CANVAS / TOP BAR / SIDEBAR / CONTENT AREA / BOTTOM BAR
        // ═══════════════════════════════════════════

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // MUI-10 (2026-05-06): wszystkie CanvasScaler przez helper żeby trzymać single source
            // of truth dla reference resolution + match. Wcześniej ręcznie ustawione 1920×1080 + 0.5
            // (akurat zgodne z helper'em, ale niezgodne z konwencją).
            UITheme.ApplyCanvasScaler(canvasGO.AddComponent<CanvasScaler>());

            canvasGO.AddComponent<GraphicRaycaster>();

            // Root panel
            var rootGO = new GameObject("Root");
            rootGO.transform.SetParent(canvasGO.transform, false);
            rootGO.AddComponent<RectTransform>();
            UITheme.ApplySurface(rootGO.AddComponent<Image>(), PanelBg, UIShapePreset.PanelLarge);
            FillRT(rootGO);
            _root = rootGO.transform;
        }

        // ─── TOP BAR ────────────────────────────────

        private void BuildTopBar()
        {
            var bar = NewGO("TopBar", _root);
            UITheme.ApplySurface(bar.AddComponent<Image>(), TopBarBg, UIShapePreset.Panel);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, TopBarHeight);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Xl;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Back button
            var backGO = NewGO("BackBtn", bar.transform);
            backGO.GetComponent<RectTransform>().sizeDelta = new Vector2(50f, 50f);
            var backImg = backGO.AddComponent<Image>();
            UITheme.ApplySurface(backImg, BtnCancel, UIShapePreset.Pill);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.colors = UITheme.CreateColorBlock(
                BtnCancel,
                UITheme.RaisedSurface,
                UITheme.Darken(BtnCancel, 0.08f),
                BtnCancel,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            backBtn.onClick.AddListener(ShowCancelConfirmation); // MB-1: confirmation
            backGO.AddComponent<LayoutElement>().preferredWidth = 50f;

            var backLbl = MakeTMP("Lbl", backGO.transform);
            backLbl.text      = "←";
            backLbl.fontSize  = 28;
            backLbl.color     = TextPrimary;
            backLbl.alignment = TextAlignmentOptions.Center;
            backLbl.raycastTarget = false;
            FillRT(backLbl.gameObject);

            // Title
            _titleLbl = MakeTMP("Title", bar.transform);
            _titleLbl.text      = LocalizationService.Get(_isMP ? "game_creator.title.multiplayer" : "game_creator.title.single_player");
            _titleLbl.fontSize  = 32;
            _titleLbl.fontStyle = FontStyles.Bold;
            _titleLbl.color     = TextPrimary;
            _titleLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 600f;
        }

        // ─── SIDEBAR ────────────────────────────────

        private void BuildSidebar()
        {
            var sidebar = NewGO("Sidebar", _root);
            UITheme.ApplySurface(sidebar.AddComponent<Image>(), SidebarBg, UIShapePreset.PanelLarge);
            var rt = sidebar.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(SidebarWidth, -TopBarHeight);

            var vl = sidebar.AddComponent<VerticalLayoutGroup>();
            vl.padding  = UITheme.Padding(0f, UITheme.Spacing.Lg);
            vl.spacing  = UITheme.Spacing.Xxs;
            vl.childAlignment      = TextAnchor.UpperLeft;
            vl.childControlWidth   = true;
            vl.childControlHeight  = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            for (int i = 0; i < 2; i++)
            {
                int idx = i;

                var btn = NewGO($"Sec{i}", sidebar.transform);
                btn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);

                var bgImg = btn.AddComponent<Image>();
                UITheme.ApplySurface(bgImg, BtnNormal, UIShapePreset.Button);
                _secBgs[i] = bgImg;

                var ub = btn.AddComponent<Button>();
                ub.targetGraphic = bgImg;
                ub.colors = UITheme.CreateColorBlock(
                    BtnNormal,
                    UITheme.RaisedSurface,
                    UITheme.Border,
                    BtnActive,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                ub.onClick.AddListener(() =>
                {
                    _activeSection = idx;
                    ApplySidebarState();
                    PopulateSection(idx);
                });

                // Accent stripe
                var acGO = NewGO("Accent", btn.transform);
                var acImg = acGO.AddComponent<Image>();
                acImg.color = Accent;
                var acRT = acGO.GetComponent<RectTransform>();
                acRT.anchorMin = new Vector2(0f, 0f);
                acRT.anchorMax = new Vector2(0f, 1f);
                acRT.pivot     = new Vector2(0f, 0.5f);
                acRT.anchoredPosition = Vector2.zero;
                acRT.sizeDelta = new Vector2(4f, 0f);
                _secAccents[i] = acImg;

                // Label
                var lbl = MakeTMP("Lbl", btn.transform);
                lbl.text      = LocalizationService.Get(_secKeys[i]);
                lbl.fontSize  = 22;
                lbl.alignment = TextAlignmentOptions.Left;
                lbl.raycastTarget = false;
                var lblRT = lbl.GetComponent<RectTransform>();
                lblRT.anchorMin = Vector2.zero;
                lblRT.anchorMax = Vector2.one;
                lblRT.offsetMin = new Vector2(20f, 0f);
                lblRT.offsetMax = Vector2.zero;
                _secLbls[i] = lbl;
            }
        }

        private void ApplySidebarState()
        {
            for (int i = 0; i < 2; i++)
            {
                bool on = i == _activeSection;
                _secBgs[i].color      = on ? BtnActive : BtnNormal;
                _secLbls[i].color     = on ? Accent    : TextMuted;
                _secLbls[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                _secAccents[i].gameObject.SetActive(on);
            }
        }

        // ─── CONTENT AREA ───────────────────────────

        private void BuildContentArea()
        {
            var vp = NewGO("Viewport", _root);
            var vpRT = vp.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(SidebarWidth, BotBarHeight);
            vpRT.offsetMax = new Vector2(0f, -TopBarHeight);
            vp.AddComponent<RectMask2D>();

            var sr = NewGO("ScrollRect", _root);
            var srRT = sr.GetComponent<RectTransform>();
            srRT.anchorMin = Vector2.zero;
            srRT.anchorMax = Vector2.one;
            srRT.offsetMin = new Vector2(SidebarWidth, BotBarHeight);
            srRT.offsetMax = new Vector2(0f, -TopBarHeight);

            var scroll = sr.AddComponent<ScrollRect>();
            scroll.horizontal        = false;
            scroll.scrollSensitivity = 30f;
            scroll.viewport          = vpRT;

            var content = NewGO("Content", vp.transform);
            _contentRT = content.GetComponent<RectTransform>();
            _contentRT.anchorMin = new Vector2(0f, 1f);
            _contentRT.anchorMax = new Vector2(1f, 1f);
            _contentRT.pivot     = new Vector2(0.5f, 1f);
            _contentRT.anchoredPosition = Vector2.zero;
            _contentRT.sizeDelta = Vector2.zero;

            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.padding  = new RectOffset(50, 50, 30, 30);
            vl.spacing  = UITheme.Spacing.Sm;
            vl.childAlignment      = TextAnchor.UpperCenter;
            vl.childControlWidth   = true;
            vl.childControlHeight  = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _contentRT;
            _contentParent = content.transform;
        }

        // ─── BOTTOM BAR ─────────────────────────────

        private void BuildBottomBar()
        {
            var bar = NewGO("BottomBar", _root);
            UITheme.ApplySurface(bar.AddComponent<Image>(), BotBarBg, UIShapePreset.Panel);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, BotBarHeight);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Lg;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Cancel button
            var cancelGO = NewGO("CancelBtn", bar.transform);
            cancelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 44f);
            var cancelImg = cancelGO.AddComponent<Image>();
            UITheme.ApplySurface(cancelImg, BtnCancel, UIShapePreset.Pill);
            var cancelBtn = cancelGO.AddComponent<Button>();
            cancelBtn.targetGraphic = cancelImg;
            cancelBtn.colors = UITheme.CreateColorBlock(
                BtnCancel,
                UITheme.RaisedSurface,
                UITheme.Border,
                BtnCancel,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            cancelBtn.onClick.AddListener(ShowCancelConfirmation); // MB-1: confirmation przed powrotem
            cancelGO.AddComponent<LayoutElement>().preferredWidth = 150f;

            var cancelLbl = MakeTMP("Lbl", cancelGO.transform);
            _lblCancelBtn = cancelLbl;
            cancelLbl.text      = LocalizationService.Get("game_creator.bottom.cancel");
            cancelLbl.fontSize  = 22;
            cancelLbl.color     = TextPrimary;
            cancelLbl.alignment = TextAlignmentOptions.Center;
            cancelLbl.raycastTarget = false;
            FillRT(cancelLbl.gameObject);

            // Spacer
            var spacer = NewGO("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Start / Create button — wpięty: onClick → ApplyOnStart() + LoadScene
            string startLabel = LocalizationService.Get(_isMP ? "game_creator.bottom.create_server" : "game_creator.bottom.start_game");
            var startGO = NewGO("StartBtn", bar.transform);
            startGO.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 44f);
            var startImg = startGO.AddComponent<Image>();
            UITheme.ApplySurface(startImg, BtnPrimary, UIShapePreset.Pill);
            var startBtn = startGO.AddComponent<Button>();
            startBtn.targetGraphic = startImg;
            startBtn.colors = UITheme.CreateColorBlock(
                BtnPrimary,
                UITheme.PrimaryAccentHover,
                UITheme.Darken(UITheme.PrimaryAccentHover, 0.12f),
                BtnPrimary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            startBtn.onClick.AddListener(() =>
            {
                ApplyOnStart(); // TD-022: agreguje GameName + Speed + Pause + Autosave + Difficulty + Server
                SceneManager.LoadScene("Depot");
            });
            startGO.AddComponent<LayoutElement>().preferredWidth = 220f;

            var startLbl = MakeTMP("Lbl", startGO.transform);
            _lblStartBtn = startLbl;
            startLbl.text      = startLabel;
            startLbl.fontSize  = 22;
            startLbl.fontStyle = FontStyles.Bold;
            startLbl.color     = TextPrimary;
            startLbl.alignment = TextAlignmentOptions.Center;
            startLbl.raycastTarget = false;
            FillRT(startLbl.gameObject);
        }
    }
}
