using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  WYBURZANIE (tryb Demolish)
        //  — ściany / pomieszczenia + tory + sieć trakcyjna
        // ═══════════════════════════════════════════

        private int pendingDemolishBuildingId = -1;
        private GameObject buildingDemolishConfirmPanel = null;
        private int demolishConfirmFrameCreated = -1; // klatka w której popup został stworzony

        // Przechowuje pary (renderer, oryginalny materiał) do odtworzenia
        private List<(MeshRenderer renderer, Material originalMat)> demolishHighlightedRenderers = new();
        private static readonly Color DemolishDialogBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color DemolishDialogInset = UITheme.WithAlpha(UITheme.PrimarySurface, 0.88f);

        private void HandleDemolish()
        {
            // Popup potwierdzenia — czekaj na klik przycisku Tak/Nie lub ESC
            if (pendingDemolishBuildingId >= 0)
            {
                // ESC only - popup nie zamyka sie na RMB
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    CancelBuildingDemolishConfirm();
                return;
            }

            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            // ESC / RMB → powrót do Select (Cancel action)
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                DepotUIManager.Instance.CurrentTool = ToolMode.Select;
                ClearDemolishTarget();
                return;
            }

            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                if (Mouse.current == null) return;
                Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 500f))
                {
                    // --- Pomieszczenia (ściany lub podłoga) ---
                    int buildingId = FindBuildingIdByHit(hit);
                    if (buildingId >= 0)
                    {
                        ClearDemolishTarget();
                        ClearDemolishTrackTarget();
                        pendingDemolishBuildingId = buildingId;
                        demolishConfirmFrameCreated = Time.frameCount;
                        HighlightBuilding(buildingId, demolishColor);
                        ShowBuildingDemolishConfirm(hit.point, buildingId);
                        return;
                    }

                    // --- Tory / Rozjazdy ---
                    if (hit.collider.CompareTag("Track"))
                    {
                        var trackSeg = FindTrackSegmentByHit(hit);
                        if (trackSeg != null)
                        {
                            ClearDemolishTarget();
                            if (demolishTrackTarget == trackSeg)
                            {
                                DemolishTrackSegment(trackSeg);
                                ClearDemolishTrackTarget();
                            }
                            else
                            {
                                ClearDemolishTrackTarget();
                                demolishTrackTarget = trackSeg;
                                HighlightTrackForDemolish(trackSeg);
                            }
                            return;
                        }
                    }
                }
                ClearDemolishTarget();
                ClearDemolishTrackTarget();
            }
        }

        private int FindBuildingIdByHit(RaycastHit hit)
        {
            // Sprawdź ściany
            var wall = FindWallByHit(hit);
            if (wall != null) return wall.buildingId;

            // Sprawdź podłogę pokoju
            var roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (roomSystem != null)
            {
                var room = roomSystem.FindRoomByFloor(hit.collider.gameObject);
                if (room != null) return room.buildingId;
            }
            return -1;
        }

        private void HighlightBuilding(int buildingId, Color color)
        {
            foreach (var wall in allWalls)
            {
                if (wall.buildingId == buildingId)
                    SetWallColor(wall, color);
            }
        }

        private void RestoreBuildingColors(int buildingId)
        {
            foreach (var wall in allWalls)
            {
                if (wall.buildingId == buildingId)
                    SetWallColor(wall, wallColor);
            }
        }

        private void ShowBuildingDemolishConfirm(Vector3 worldPos, int buildingId)
        {
            CancelBuildingDemolishConfirm();
            pendingDemolishBuildingId = buildingId; // przywróć po CancelBuildingDemolishConfirm

            var canvas = DepotUIManager.Instance?.canvas;
            if (canvas == null) return;

            buildingDemolishConfirmPanel = new GameObject("BuildingDemolishConfirm");
            buildingDemolishConfirmPanel.transform.SetParent(canvas.transform, false);

            var rt = buildingDemolishConfirmPanel.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(308, 126);

            Vector2 screenPos = mainCamera.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(), screenPos, null, out Vector2 localPos);
            rt.anchoredPosition = localPos + new Vector2(0, 60);

            var bg = buildingDemolishConfirmPanel.AddComponent<UnityEngine.UI.Image>();
            UITheme.ApplySurface(bg, DemolishDialogBg, UIShapePreset.PanelLarge);

            var vlg = buildingDemolishConfirmPanel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            var textCard = new GameObject("TextCard");
            textCard.transform.SetParent(buildingDemolishConfirmPanel.transform, false);
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
            txt.text = "Wyburzyc pomieszczenie?";
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            // Kontener przycisków
            var btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(buildingDemolishConfirmPanel.transform, false);
            btnRow.AddComponent<RectTransform>();
            var hlg = btnRow.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Md;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            btnRow.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 32;

            int capturedId = pendingDemolishBuildingId;

            var btnTak = CreateBuildingConfirmBtn(btnRow.transform, "Tak", new Color(0.8f, 0.2f, 0.2f));
            var btnNie = CreateBuildingConfirmBtn(btnRow.transform, "Nie", new Color(0.3f, 0.3f, 0.35f));

            btnTak.onClick.AddListener(() =>
            {
                int id = capturedId;
                pendingDemolishBuildingId = -1;
                if (buildingDemolishConfirmPanel != null)
                {
                    Destroy(buildingDemolishConfirmPanel);
                    buildingDemolishConfirmPanel = null;
                }
                RemoveBuilding(id);
            });
            btnNie.onClick.AddListener(() =>
            {
                RestoreBuildingColors(capturedId);
                pendingDemolishBuildingId = -1;
                if (buildingDemolishConfirmPanel != null)
                {
                    Destroy(buildingDemolishConfirmPanel);
                    buildingDemolishConfirmPanel = null;
                }
            });
        }

        private UnityEngine.UI.Button CreateBuildingConfirmBtn(Transform parent, string label, Color bgColor)
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
            var txtCmp = txtObj.AddComponent<TextMeshProUGUI>();
            txtCmp.text = label;
            UITheme.ApplyTmpText(txtCmp, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            txtCmp.fontSize = 12;
            txtCmp.alignment = TextAlignmentOptions.Center;
            txtCmp.raycastTarget = false;

            return btn;
        }

        private void CancelBuildingDemolishConfirm()
        {
            if (pendingDemolishBuildingId >= 0)
                RestoreBuildingColors(pendingDemolishBuildingId);
            pendingDemolishBuildingId = -1;
            if (buildingDemolishConfirmPanel != null)
            {
                Destroy(buildingDemolishConfirmPanel);
                buildingDemolishConfirmPanel = null;
            }
        }

        private PlacedTrackSegment FindTrackSegmentByHit(RaycastHit hit)
        {
            var trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackBuilder == null) return null;

            Transform root = hit.collider.transform;
            while (root.parent != null && !root.CompareTag("Track"))
                root = root.parent;
            // Szukaj root toru (obiekt z tagiem Track na najwyższym poziomie)
            if (root.parent != null && root.parent.CompareTag("Track"))
                root = root.parent;

            foreach (var placed in trackBuilder.PlacedTracks)
            {
                if (placed.TrackObject == root.gameObject)
                    return placed;
            }
            return null;
        }

        private void HighlightTrackForDemolish(PlacedTrackSegment seg)
        {
            if (seg.TrackObject == null) return;

            demolishHighlightedRenderers.Clear();

            // 1. Podświetl tor
            foreach (var r in seg.TrackObject.GetComponentsInChildren<MeshRenderer>())
            {
                demolishHighlightedRenderers.Add((r, r.material));
                var mat = new Material(r.material);
                mat.color = demolishColor;
                r.material = mat;
            }

            // 2. Podświetl sieć trakcyjną nad torem (druty + słupy)
            var catenaryGen = DepotServices.Get<CatenaryGenerator>();
            if (catenaryGen != null && catenaryGen.Network != null)
            {
                int trackId = seg.GraphTrackId;

                // Druty (WireSpan)
                if (catenaryGen.Network.WireSpans != null)
                {
                    foreach (var span in catenaryGen.Network.WireSpans)
                    {
                        if (span.TrackId == trackId && span.Visual != null)
                            HighlightVisualDemolish(span.Visual);
                    }
                }

                // Słupy/bramki — podświetl jeśli obsługują ten tor
                if (catenaryGen.Network.Supports != null)
                {
                    foreach (var support in catenaryGen.Network.Supports)
                    {
                        bool servesTrack = false;
                        foreach (var pt in support.Points)
                        {
                            if (pt.TrackId == trackId) { servesTrack = true; break; }
                        }
                        if (servesTrack && support.Visual != null)
                            HighlightVisualDemolish(support.Visual);
                    }
                }
            }
        }

        private void HighlightVisualDemolish(GameObject obj)
        {
            foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
            {
                demolishHighlightedRenderers.Add((r, r.material));
                var mat = new Material(r.material);
                mat.color = demolishColor;
                r.material = mat;
            }
            foreach (var lr in obj.GetComponentsInChildren<LineRenderer>())
            {
                // LineRenderer nie ma materiał-swap, ale zmień kolor
                lr.startColor = demolishColor;
                lr.endColor = demolishColor;
            }
        }

        private void ClearDemolishTrackTarget()
        {
            // Odtwórz oryginalne materiały
            foreach (var (renderer, originalMat) in demolishHighlightedRenderers)
            {
                if (renderer != null)
                    renderer.material = originalMat;
            }
            demolishHighlightedRenderers.Clear();
            demolishTrackTarget = null;
        }

        private void DemolishTrackSegment(PlacedTrackSegment seg)
        {
            var trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackBuilder == null || seg.GraphTrackId < 0) return;

            // Sprawdź czy tor należy do rozjazdu — jeśli tak, usuń cały rozjazd
            if (trackBuilder.TryGetTurnoutForTrack(seg.GraphTrackId, out var turnoutEntity))
                trackBuilder.RemoveTurnout(turnoutEntity.TurnoutId);
            else
                trackBuilder.RemoveTrack(seg.GraphTrackId);

            // Odśwież snap pointy
            var snapSystem = DepotServices.Get<SnapPointSystem>();
            if (snapSystem != null)
                snapSystem.RefreshAllSnapPoints();
        }

        private void ClearDemolishTarget()
        {
            if (demolishTarget != null)
            {
                SetWallColor(demolishTarget, wallColor);
                demolishTarget = null;
            }
        }
    }
}
