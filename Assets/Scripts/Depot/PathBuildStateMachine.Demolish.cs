using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    public partial class PathBuildStateMachine
    {
        // ═══════════════════════════════════════════
        //  WYBURZANIE ŚCIEŻEK / DRÓG / PARKINGÓW
        // ═══════════════════════════════════════════

        private PlacedPathSegment pendingDemolishParking = null;
        private GameObject demolishConfirmPanel = null;
        private UnityEngine.UI.Button btnTak;
        private UnityEngine.UI.Button btnNie;

        private static readonly Color DemolishDialogBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color DemolishDialogInset = UITheme.WithAlpha(UITheme.PrimarySurface, 0.88f);

        private void HandleDemolishPaths()
        {
            // Popup potwierdzenia parkingu — czekaj na klik przycisku lub ESC
            if (pendingDemolishParking != null)
            {
                // ESC only (nie chcemy zeby RMB zamykal popup potwierdzenia)
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    pendingDemolishParking = null;
                    HideDemolishConfirm();
                }
                return;
            }

            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            var mousePos = GetMouseWorldPosition();
            if (!mousePos.HasValue) return;

            if (!_toolBuild.Primary.WasPressedThisFrame()) return;

            Vector2 mousePos2D = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = mainCamera.ScreenPointToRay(mousePos2D);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            string tag = hit.collider.gameObject.tag;

            if (tag == "Path" || tag == "Road")
            {
                Vector2Int cell = WorldToCell(hit.point);
                if (tag == "Road")
                {
                    int half = roadBrushSize / 2;
                    for (int dx = -half; dx < roadBrushSize - half; dx++)
                        for (int dy = -half; dy < roadBrushSize - half; dy++)
                        {
                            var c = cell + new Vector2Int(dx, dy);
                            if (placedCells.Contains(c)) RemoveCell(c);
                        }
                }
                else
                {
                    if (placedCells.Contains(cell)) RemoveCell(cell);
                }
            }
            else if (tag == "Parking")
            {
                var seg = FindParkingSegmentByHit(hit.collider.gameObject);
                if (seg != null)
                {
                    pendingDemolishParking = seg;
                    ShowDemolishConfirm(hit.point);
                }
            }
        }

        private void DemolishParking(PlacedPathSegment parking)
        {
            if (pathGraph != null && parking.GraphEdgeId >= 0)
            {
                var edge = pathGraph.GetEdge(parking.GraphEdgeId);
                if (edge != null)
                {
                    // Zbierz wszystkie krawędzie parkingu (BFS po Parking edges)
                    var edgesToRemove = new HashSet<int>();
                    var visited = new HashSet<int>();
                    var queue = new Queue<int>();
                    queue.Enqueue(edge.FromNodeId);
                    visited.Add(edge.FromNodeId);
                    while (queue.Count > 0)
                    {
                        int cur = queue.Dequeue();
                        var curNode = pathGraph.GetNode(cur);
                        if (curNode == null) continue;
                        foreach (int eid in curNode.EdgeIds)
                        {
                            var e = pathGraph.GetEdge(eid);
                            if (e != null && e.EdgeType == PathEdgeType.Parking)
                            {
                                edgesToRemove.Add(eid);
                                int other = e.FromNodeId == cur ? e.ToNodeId : e.FromNodeId;
                                if (visited.Add(other)) queue.Enqueue(other);
                            }
                        }
                    }
                    foreach (int eid in edgesToRemove)
                        pathGraph.RemoveEdge(eid);
                }
            }
            visualBuilder.RemoveSegment(parking.GraphEdgeId);
        }

        /// <summary>Public wrapper dla undo — usuwa parking</summary>
        public void UndoDemolishParking(PlacedPathSegment parking)
        {
            if (parking != null)
                DemolishParking(parking);
        }

        private PlacedPathSegment FindParkingSegmentByHit(GameObject hitObj)
        {
            if (visualBuilder == null) return null;
            // Szukaj segmentu parkingu po obiekcie lub jego rodzicu
            Transform t = hitObj.transform;
            while (t != null)
            {
                foreach (var seg in visualBuilder.PlacedSegments)
                {
                    if (seg.SegmentObject == t.gameObject && seg.Type == PathEdgeType.Parking)
                        return seg;
                }
                t = t.parent;
            }
            return null;
        }

        private void ShowDemolishConfirm(Vector3 worldPos)
        {
            HideDemolishConfirm();

            var canvas = DepotUIManager.Instance?.canvas;
            if (canvas == null) return;

            demolishConfirmPanel = new GameObject("DemolishConfirm");
            demolishConfirmPanel.transform.SetParent(canvas.transform, false);

            var rt = demolishConfirmPanel.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(296, 126);

            Vector2 screenPos = mainCamera.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(), screenPos, null, out Vector2 localPos);
            rt.anchoredPosition = localPos + new Vector2(0, 60);

            var bg = demolishConfirmPanel.AddComponent<UnityEngine.UI.Image>();
            UITheme.ApplySurface(bg, DemolishDialogBg, UIShapePreset.PanelLarge);

            var vlg = demolishConfirmPanel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            var textCard = new GameObject("TextCard");
            textCard.transform.SetParent(demolishConfirmPanel.transform, false);
            textCard.AddComponent<RectTransform>();
            var textCardBg = textCard.AddComponent<UnityEngine.UI.Image>();
            UITheme.ApplySurface(textCardBg, DemolishDialogInset, UIShapePreset.Panel);
            textCard.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 50;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(textCard.transform, false);
            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 8f);
            textRt.offsetMax = new Vector2(-12f, -8f);
            var txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = "Usunac ten parking?";
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            // Kontener przycisków
            var btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(demolishConfirmPanel.transform, false);
            btnRow.AddComponent<RectTransform>();
            var hlg = btnRow.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Md;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            btnRow.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 32;

            btnTak = CreateConfirmButton(btnRow.transform, "Tak", new Color(0.8f, 0.2f, 0.2f));
            btnNie = CreateConfirmButton(btnRow.transform, "Nie", new Color(0.3f, 0.3f, 0.35f));

            btnTak.onClick.AddListener(() =>
            {
                if (pendingDemolishParking != null)
                    DemolishParking(pendingDemolishParking);
                pendingDemolishParking = null;
                HideDemolishConfirm();
            });
            btnNie.onClick.AddListener(() =>
            {
                pendingDemolishParking = null;
                HideDemolishConfirm();
            });
        }

        private UnityEngine.UI.Button CreateConfirmButton(Transform parent, string label, Color bgColor)
        {
            var obj = new GameObject($"Btn_{label}");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<UnityEngine.UI.Image>();
            img.raycastTarget = true;
            var btn = obj.AddComponent<UnityEngine.UI.Button>();
            bool primary = bgColor.r > 0.5f;
            if (primary)
            {
                UITheme.ApplySurface(img, UITheme.Danger, UIShapePreset.Button);
                btn.targetGraphic = img;
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.Danger,
                    new Color(0.87f, 0.48f, 0.40f, 1f),
                    UITheme.Darken(UITheme.Danger, 0.10f),
                    UITheme.Danger,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
            }
            else
            {
                UITheme.ApplySurface(img, UITheme.SecondarySurface, UIShapePreset.Button);
                btn.targetGraphic = img;
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.SecondarySurface,
                    UITheme.RaisedSurface,
                    UITheme.Border,
                    UITheme.SecondarySurface,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
            }

            var txtObj = new GameObject("Label");
            txtObj.transform.SetParent(obj.transform, false);
            var txtRt = txtObj.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            UITheme.ApplyTmpText(txt, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            txt.fontSize = 12;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            return btn;
        }

        private void HideDemolishConfirm()
        {
            btnTak = null;
            btnNie = null;
            if (demolishConfirmPanel != null)
            {
                Destroy(demolishConfirmPanel);
                demolishConfirmPanel = null;
            }
        }
    }
}
