using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    public partial class PathBuildStateMachine
    {
        // ═══════════════════════════════════════════
        //  WALIDACJA KOLIZJI + PREVIEW VISUAL
        // ═══════════════════════════════════════════

        private bool ValidateCellCrossing(Vector3 center, PathBuildSubMode subMode)
        {
            Vector3 boxCenter = new Vector3(center.x, 1f, center.z);
            Vector3 halfExtents = new Vector3(gridSize / 2f, 1.5f, gridSize / 2f);

            Collider[] hits = Physics.OverlapBox(boxCenter, halfExtents, Quaternion.identity);
            foreach (var hit in hits)
            {
                if (!hit.CompareTag("Track")) continue;

                if (subMode == PathBuildSubMode.Road)
                    return false; // Drogi nie mogą przecinać torów

                // Ścieżka: rozjazdy nie, zwykłe tory OK
                if (IsTrackPartOfTurnout(hit.gameObject))
                    return false;
            }
            return true;
        }

        private bool IsTrackPartOfTurnout(GameObject hitObj)
        {
            if (trackBuilder == null) return false;

            Transform root = hitObj.transform;
            while (root.parent != null && !root.CompareTag("Track"))
                root = root.parent;
            if (!root.CompareTag("Track"))
                root = hitObj.transform.parent ?? hitObj.transform;

            foreach (var seg in trackBuilder.PlacedTracks)
            {
                if (seg.TrackObject == root.gameObject && seg.GraphTrackId >= 0)
                    return trackBuilder.TryGetTurnoutForTrack(seg.GraphTrackId, out _);
            }
            return false;
        }

        private bool RectOverlapsTracks(Vector3 a, Vector3 b)
        {
            Vector3 center = new Vector3((a.x + b.x) / 2f, 1f, (a.z + b.z) / 2f);
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(b.x - a.x) / 2f, 1.5f, Mathf.Abs(b.z - a.z) / 2f);

            Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity);
            foreach (var hit in hits)
                if (hit.CompareTag("Track")) return true;
            return false;
        }

        // ═══════════════════════════════════════════
        //  PREVIEW (cell + strip + parking fill)
        // ═══════════════════════════════════════════

        private List<GameObject> previewStripCells = new();

        private void ShowPreviewCell(Vector3 center, bool valid)
        {
            if (previewCell == null)
            {
                previewCell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                previewCell.name = "CellPreview";
                previewCell.transform.rotation = Quaternion.Euler(90, 0, 0);
                Object.Destroy(previewCell.GetComponent<MeshCollider>());
                previewCell.GetComponent<MeshRenderer>().material = MaterialFactory.CreateLine();
            }

            previewCell.SetActive(true);
            previewCell.transform.position = new Vector3(center.x, 0.04f, center.z);
            previewCell.transform.localScale = new Vector3(gridSize * 0.95f, gridSize * 0.95f, 1f);
            previewCell.GetComponent<MeshRenderer>().material.color = valid ? validColor : invalidColor;
        }

        private void ShowPreviewStrip(List<Vector2Int> cells, bool valid)
        {
            HidePreviewCell();

            // Dopasuj liczbę preview obiektów
            while (previewStripCells.Count < cells.Count)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "StripPreview";
                quad.transform.rotation = Quaternion.Euler(90, 0, 0);
                Object.Destroy(quad.GetComponent<MeshCollider>());
                quad.GetComponent<MeshRenderer>().material = MaterialFactory.CreateLine();
                previewStripCells.Add(quad);
            }

            Color col = valid ? validColor : invalidColor;
            for (int i = 0; i < previewStripCells.Count; i++)
            {
                if (i < cells.Count)
                {
                    Vector3 center = CellToWorld(cells[i]);
                    previewStripCells[i].SetActive(true);
                    previewStripCells[i].transform.position = new Vector3(center.x, 0.04f, center.z);
                    previewStripCells[i].transform.localScale = new Vector3(gridSize * 0.95f, gridSize * 0.95f, 1f);
                    previewStripCells[i].GetComponent<MeshRenderer>().material.color = col;
                }
                else
                {
                    previewStripCells[i].SetActive(false);
                }
            }
        }

        private void HidePreviewCell()
        {
            if (previewCell != null) previewCell.SetActive(false);
            foreach (var s in previewStripCells)
                if (s != null) s.SetActive(false);
        }

        private void CreatePreviewFill()
        {
            if (previewFill != null) return;
            previewFill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            previewFill.name = "ParkingPreview";
            previewFill.transform.rotation = Quaternion.Euler(90, 0, 0);
            Object.Destroy(previewFill.GetComponent<MeshCollider>());
            previewFill.GetComponent<MeshRenderer>().material = MaterialFactory.CreateLine();
        }

        private void UpdatePreviewFill(Vector3 a, Vector3 b, bool valid)
        {
            if (previewFill == null) return;
            Vector3 center = new Vector3((a.x + b.x) / 2f, 0.05f, (a.z + b.z) / 2f);
            float w = Mathf.Abs(b.x - a.x);
            float d = Mathf.Abs(b.z - a.z);
            previewFill.transform.position = center;
            previewFill.transform.localScale = new Vector3(w, d, 1f);
            previewFill.GetComponent<MeshRenderer>().material.color = valid ? validColor : invalidColor;
        }

        private void HidePreviewFill()
        {
            if (previewFill != null) { Destroy(previewFill); previewFill = null; }
        }

        private void CreatePreviewHud()
        {
            if (previewHudObj != null) return;

            previewHudObj = new GameObject("PathBuildPreviewHud");

            var infoObj = new GameObject("InfoText");
            infoObj.transform.SetParent(previewHudObj.transform, false);
            previewInfoTextMesh = infoObj.AddComponent<TextMesh>();
            previewInfoTextMesh.fontSize = 42;
            previewInfoTextMesh.characterSize = 0.10f;
            previewInfoTextMesh.anchor = TextAnchor.MiddleCenter;
            previewInfoTextMesh.alignment = TextAlignment.Center;
            previewInfoTextMesh.color = Color.white;
            previewInfoTextMesh.fontStyle = FontStyle.Bold;
            previewInfoTextMesh.gameObject.SetActive(false);

            var statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(previewHudObj.transform, false);
            previewStatusTextMesh = statusObj.AddComponent<TextMesh>();
            previewStatusTextMesh.fontSize = 34;
            previewStatusTextMesh.characterSize = 0.08f;
            previewStatusTextMesh.anchor = TextAnchor.LowerCenter;
            previewStatusTextMesh.alignment = TextAlignment.Center;
            previewStatusTextMesh.color = validColor;
            previewStatusTextMesh.fontStyle = FontStyle.Bold;
            previewStatusTextMesh.gameObject.SetActive(false);
        }

        private void HidePreviewHud()
        {
            if (previewInfoTextMesh != null) previewInfoTextMesh.gameObject.SetActive(false);
            if (previewStatusTextMesh != null) previewStatusTextMesh.gameObject.SetActive(false);
        }

        private void UpdatePreviewHud(Vector3 anchor, string infoText, string statusText, Color statusColor)
        {
            CreatePreviewHud();

            previewInfoTextMesh.text = infoText;
            previewInfoTextMesh.transform.position = anchor + Vector3.up * previewInfoHeight;
            previewInfoTextMesh.gameObject.SetActive(true);

            previewStatusTextMesh.text = statusText;
            previewStatusTextMesh.color = statusColor;
            previewStatusTextMesh.transform.position = anchor + Vector3.up * previewStatusHeight;
            previewStatusTextMesh.gameObject.SetActive(true);
        }

        private void UpdatePaintHoverHud(Vector2Int cell, PathBuildSubMode subMode, int validCount, int totalCount, PathBuildValidationReason reason)
        {
            previewValidationReason = reason;

            string modeLabel = GetPathModeLabel(subMode);
            string infoText = subMode == PathBuildSubMode.Road
                ? $"{modeLabel} 6x6 • gotowe {validCount}/{totalCount} komorek"
                : $"{modeLabel} • pojedyncza komorka";

            string statusText = reason == PathBuildValidationReason.Ready
                ? "LMB rozpocznij malowanie"
                : GetValidationReasonLabel(reason);

            Color statusColor = reason == PathBuildValidationReason.Ready
                ? new Color(0.60f, 1f, 0.60f, 1f)
                : invalidColor;

            UpdatePreviewHud(CellToWorld(cell), infoText, statusText, statusColor);
        }

        private void UpdatePendingPaintHud(PathBuildSubMode subMode, string statusText)
        {
            if (pendingCells.Count == 0)
            {
                HidePreviewHud();
                return;
            }

            Vector3 anchor = GetPendingCellsAnchor();
            string infoText = subMode == PathBuildSubMode.Road
                ? $"Droga 6x6 • {pendingCells.Count} komorek w podgladzie"
                : $"Sciezka • {pendingCells.Count} komorka w podgladzie";

            UpdatePreviewHud(anchor, infoText, statusText, new Color(0.60f, 1f, 0.60f, 1f));
        }

        private void UpdateParkingHud(Vector3 start, Vector3 end, PathBuildValidationReason reason, bool paused)
        {
            previewValidationReason = reason;

            Vector3 anchor = paused
                ? start
                : new Vector3((start.x + end.x) * 0.5f, 0f, (start.z + end.z) * 0.5f);

            string infoText;
            if (paused)
            {
                infoText = $"Parking • start ({start.x:F0}, {start.z:F0})";
            }
            else
            {
                float width = Mathf.Abs(end.x - start.x);
                float depth = Mathf.Abs(end.z - start.z);
                infoText = $"Parking • {width:F0} x {depth:F0} m";
            }

            string statusText = paused
                ? ParkingPausedMessage
                : reason == PathBuildValidationReason.Ready
                    ? "LMB buduj • ESC anuluj • PPM cofnij"
                    : GetValidationReasonLabel(reason);

            Color statusColor = paused
                ? Color.white
                : reason == PathBuildValidationReason.Ready
                    ? new Color(0.60f, 1f, 0.60f, 1f)
                    : invalidColor;

            UpdatePreviewHud(anchor, infoText, statusText, statusColor);
        }

        private Vector3 GetPendingCellsAnchor()
        {
            if (pendingCells.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (var cell in pendingCells)
                sum += CellToWorld(cell);

            return sum / pendingCells.Count;
        }

        private static string GetPathModeLabel(PathBuildSubMode subMode)
        {
            return subMode switch
            {
                PathBuildSubMode.Road => "Droga",
                PathBuildSubMode.Parking => "Parking",
                _ => "Sciezka"
            };
        }

        private static string GetValidationReasonLabel(PathBuildValidationReason reason)
        {
            return reason switch
            {
                PathBuildValidationReason.Ready => "Gotowe do potwierdzenia",
                PathBuildValidationReason.OccupiedCell => "Ta komorka jest juz zajeta",
                PathBuildValidationReason.OutsideBuildableArea => "Poza obszarem budowy",
                PathBuildValidationReason.TrackCrossingBlocked => "Droga nie moze przecinac torow",
                PathBuildValidationReason.TurnoutCrossingBlocked => "Sciezka nie moze wejsc na rozjazd",
                PathBuildValidationReason.BrushPartiallyBlocked => "Czesc pedzla wychodzi poza dozwolony obszar",
                PathBuildValidationReason.RectTooSmall => "Parking jest zbyt maly",
                PathBuildValidationReason.RectOverlapsTracks => "Parking koliduje z torami",
                _ => "Wskaz komorke lub obszar budowy"
            };
        }
    }
}
