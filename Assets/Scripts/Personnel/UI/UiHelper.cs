using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-4: Prymitywy UI do programatycznego budowania paneli Personnel
    /// (Text/Button/Dropdown/InputField) — unika duplikacji miedzy
    /// <see cref="RecruitmentUI"/>, <see cref="PersonnelMainTabUI"/>,
    /// <see cref="EmployeeDetailsUI"/>, <see cref="EmployeeScheduleEditorUI"/>.
    ///
    /// W uzyciu: <c>UiHelper.CreateText(parent, ...)</c>.
    ///
    /// Font: Unity LegacyRuntime.ttf (bez TMP dla uproszczenia).
    /// Post-EA mozna zastapic TMP + prefab templates dla spojnego stylu.
    /// </summary>
    public static class UiHelper
    {
        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.richText = true;
            UITheme.ApplyTmpText(t, UIThemeTextRole.Primary);
            return t;
        }

        public static Button CreateButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var lt = labelGo.AddComponent<TextMeshProUGUI>();
            lt.text = label;
            lt.alignment = TextAlignmentOptions.Center;
            lt.fontSize = 13;
            UITheme.ApplyTmpText(lt, UIThemeTextRole.Inverse);
            return btn;
        }

        public static TMP_Dropdown CreateDropdown(Transform parent, string name, List<string> options)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>().color = UITheme.SecondarySurface;
            var dd = go.AddComponent<TMP_Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(options);
            return dd;
        }

        public static TMP_InputField CreateInputField(Transform parent, string name, string placeholder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var bg = go.AddComponent<Image>();

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children of viewport.
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(go.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8, 4); viewportRt.offsetMax = new Vector2(-8, -4);
            viewportGo.AddComponent<RectMask2D>();

            var input = go.AddComponent<TMP_InputField>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(viewportGo.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.fontSize = 13;
            t.richText = false;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            input.textViewport = viewportRt;
            input.textComponent = t;
            input.lineType = TMP_InputField.LineType.SingleLine;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(viewportGo.transform, false);
            var prt = phGo.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.fontSize = 13;
            ph.text = placeholder;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.fontStyle = FontStyles.Italic;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            ph.raycastTarget = false;
            input.placeholder = ph;
            UITheme.ApplyTmpInputField(input, bg, t, ph);

            return input;
        }

        /// <summary>Prosty panel tla bez lol-bagiennych layoutów. Dodaj VerticalLayoutGroup recznie.</summary>
        public static GameObject CreatePanel(Transform parent, string name, Color color, UIShapePreset shape = UIShapePreset.Panel)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, color, shape);
            return go;
        }
    }
}
