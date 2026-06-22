using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P3: pływające okno SKŁADU — nagłówek (kontekst + status + liczba pojazdów) +
    /// lista chipów pojazdów; klik wiersza → drill-down do okna pojazdu
    /// (<see cref="FleetPanelUI.OpenVehicleWindow"/>, czyli to samo okno co z Tabora — P2).
    ///
    /// <para>Zunifikowane: jeden builder dla wszystkich płaszczyzn (zakładka „Składy" / Depot 3D /
    /// Mapa) — każda podaje <see cref="ConsistView"/>. Żyje w asmdef Depot, więc Map/Timetable
    /// (referują Depot) też mogą go otworzyć.</para>
    /// </summary>
    public static class ConsistWindowUI
    {
        static readonly Vector2 Size = new Vector2(380f, 460f);
        const float HeaderH = 50f;

        /// <summary>Otwiera (lub fokusuje + odświeża) okno składu dla danego widoku.</summary>
        public static FloatingWindow Open(ConsistView view)
        {
            if (view == null) return null;
            var win = WindowManager.Instance.OpenWindow(view.Key, view.Title, Size);
            BuildContent(win, view);
            return win;
        }

        static void BuildContent(FloatingWindow win, ConsistView view)
        {
            var root = win.ContentRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.Destroy(root.GetChild(i).gameObject);

            BuildHeader(root, view);

            var list = WindowScroll.BuildVertical(root, HeaderH, 0f);
            if (view.VehicleIds.Count == 0)
            {
                var empty = UIPrimitives.MakeTMP("Empty", list, UITheme.Typography.Small, UIThemeTextRole.Secondary);
                empty.text = "Brak pojazdów w składzie";
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
                return;
            }
            for (int i = 0; i < view.VehicleIds.Count; i++)
                BuildVehicleRow(list, view.VehicleIds[i]);
        }

        static void BuildHeader(Transform root, ConsistView view)
        {
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(root, false);
            var hRT = (RectTransform)header.transform;
            hRT.anchorMin = new Vector2(0f, 1f); hRT.anchorMax = new Vector2(1f, 1f);
            hRT.pivot = new Vector2(0.5f, 1f);
            hRT.anchoredPosition = Vector2.zero;
            hRT.sizeDelta = new Vector2(0f, HeaderH);

            var hvl = header.AddComponent<VerticalLayoutGroup>();
            hvl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            hvl.spacing = UITheme.Spacing.Xxs;
            hvl.childControlWidth = true; hvl.childControlHeight = true;
            hvl.childForceExpandWidth = true; hvl.childForceExpandHeight = false;

            var ctx = UIPrimitives.MakeTMP("Context", header.transform, UITheme.Typography.Small, UIThemeTextRole.Secondary);
            ctx.text = view.Context;

            var st = UIPrimitives.MakeTMP("Status", header.transform, UITheme.Typography.Small, UIThemeTextRole.Accent);
            st.text = view.Status + "  ·  " + view.VehicleIds.Count + " poj.";
        }

        /// <summary>Wiersz pojazdu (chip + numer + „›") klikalny → drill-down do okna pojazdu.
        /// Public bo reużywany cross-asmdef (Timetable mapowe okno pociągu).</summary>
        public static void BuildVehicleRow(Transform parent, int vehicleId)
        {
            var (label, color) = VehicleChipStyle.ChipForVehicle(vehicleId);
            var v = FleetService.GetOwnedById(vehicleId);
            string number = v != null && !string.IsNullOrEmpty(v.number) ? v.number : "#" + vehicleId;

            var row = new GameObject("VRow_" + vehicleId, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.RaisedSurface, 0.6f), UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = 40f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            // chip (kolor typu + label serii)
            var chip = new GameObject("Chip", typeof(RectTransform));
            chip.transform.SetParent(row.transform, false);
            var chipImg = chip.AddComponent<Image>();
            UITheme.ApplySurface(chipImg, color, UIShapePreset.Pill);
            chipImg.raycastTarget = false;
            var chipLE = chip.AddComponent<LayoutElement>();
            chipLE.preferredWidth = 66f; chipLE.preferredHeight = 26f;
            var chipTxt = UIPrimitives.MakeTMP("Lbl", chip.transform, UITheme.Typography.Small,
                UIThemeTextRole.Inverse, TextAlignmentOptions.Center, FontStyles.Bold);
            chipTxt.text = label;
            UIPrimitives.Stretch(chipTxt.rectTransform);

            // numer taborowy
            var numTxt = UIPrimitives.MakeTMP("Num", row.transform, UITheme.Typography.Body, UIThemeTextRole.Primary);
            numTxt.text = number;
            numTxt.raycastTarget = false;
            numTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // strzałka drill-down
            var arrow = UIPrimitives.MakeTMP("Arrow", row.transform, UITheme.Typography.Body, UIThemeTextRole.Secondary);
            arrow.text = "›";
            arrow.raycastTarget = false;
            arrow.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;

            // klik całego wiersza → okno pojazdu (P2)
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            int vid = vehicleId;
            btn.onClick.AddListener(() => FleetPanelUI.OpenVehicleWindow(vid));
        }
    }
}
