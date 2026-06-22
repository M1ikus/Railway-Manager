using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  HELPERS — find/color/coords/preview objects
        // ═══════════════════════════════════════════

        private WallSegment FindWallByGameObject(GameObject obj)
        {
            return allWalls.Find(w => w.wallObject == obj);
        }

        private WallSegment FindWallByHit(RaycastHit hit)
        {
            var go = hit.collider.gameObject;

            // Bezpośrednie trafienie
            var wall = FindWallByGameObject(go);
            if (wall != null) return wall;

            // Trafienie w dziecko
            foreach (var w in allWalls)
            {
                if (w.wallObject != null && go.transform.IsChildOf(w.wallObject.transform))
                    return w;
            }
            return null;
        }

        private void SetWallColor(WallSegment wall, Color color)
        {
            if (wall.wallObject == null) return;

            var renderers = wall.wallObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r.material != null)
                    r.material.color = color;
            }
        }

        private Vector3 GetMouseWorldPosition()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return Vector3.zero;
            if (Mouse.current == null) return Vector3.zero;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);

            return Vector3.zero;
        }

        private Vector3 SnapToGrid(Vector3 position)
        {
            if (gridSize <= 0) return new Vector3(position.x, 0f, position.z);

            return new Vector3(
                Mathf.Round(position.x / gridSize) * gridSize,
                0f,
                Mathf.Round(position.z / gridSize) * gridSize
            );
        }

        private bool IsInsideBuildableArea(Vector3 point)
        {
            if (!buildableArea.HasValue) return true;
            var ba = buildableArea.Value;
            return point.x >= ba.min.x && point.x <= ba.max.x
                && point.z >= ba.min.z && point.z <= ba.max.z;
        }

        // ─── Preview objects ───

        private void CreatePreviewObjects()
        {
            previewParent = new GameObject("WallPreview");
            previewParent.SetActive(false);

            for (int i = 0; i < 4; i++)
            {
                var lineObj = new GameObject($"PreviewLine_{i}");
                lineObj.transform.SetParent(previewParent.transform, false);
                var lr = lineObj.AddComponent<LineRenderer>();
                lr.material = MaterialFactory.CreateLine();
                lr.startWidth = 0.3f;
                lr.endWidth = 0.3f;
                lr.positionCount = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                previewLines[i] = lr;
            }

            // Wypełnienie (Quad)
            previewFill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            previewFill.name = "PreviewFill";
            previewFill.transform.SetParent(previewParent.transform, false);
            previewFill.transform.rotation = Quaternion.Euler(90, 0, 0);
            var col = previewFill.GetComponent<MeshCollider>();
            if (col != null) Destroy(col);
            var fillRenderer = previewFill.GetComponent<MeshRenderer>();
            var fillMat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(fillMat, new Color(1f, 0.9f, 0.3f, 0.15f));
            fillRenderer.material = fillMat;
            fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var infoObj = new GameObject("PreviewInfoText");
            infoObj.transform.SetParent(previewParent.transform, false);
            previewInfoTextMesh = infoObj.AddComponent<TextMesh>();
            previewInfoTextMesh.fontSize = 42;
            previewInfoTextMesh.characterSize = 0.10f;
            previewInfoTextMesh.anchor = TextAnchor.MiddleCenter;
            previewInfoTextMesh.alignment = TextAlignment.Center;
            previewInfoTextMesh.color = Color.white;
            previewInfoTextMesh.fontStyle = FontStyle.Bold;
            previewInfoTextMesh.gameObject.SetActive(false);

            var statusObj = new GameObject("PreviewStatusText");
            statusObj.transform.SetParent(previewParent.transform, false);
            previewStatusTextMesh = statusObj.AddComponent<TextMesh>();
            previewStatusTextMesh.fontSize = 34;
            previewStatusTextMesh.characterSize = 0.08f;
            previewStatusTextMesh.anchor = TextAnchor.LowerCenter;
            previewStatusTextMesh.alignment = TextAlignment.Center;
            previewStatusTextMesh.color = previewValid;
            previewStatusTextMesh.fontStyle = FontStyle.Bold;
            previewStatusTextMesh.gameObject.SetActive(false);

            // Opening preview
            openingPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            openingPreview.name = "OpeningPreview";
            openingPreview.SetActive(false);
            var opCol = openingPreview.GetComponent<BoxCollider>();
            if (opCol != null) Destroy(opCol);
            var opRenderer = openingPreview.GetComponent<MeshRenderer>();
            var opMat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(opMat, new Color(0.3f, 1f, 0.3f, 0.5f));
            opRenderer.material = opMat;
            opRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void SetLinePositions(LineRenderer lr, Vector3 a, Vector3 b, Color color)
        {
            lr.gameObject.SetActive(true);
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startColor = color;
            lr.endColor = color;
        }

        private void HideWallBuildHud()
        {
            if (previewInfoTextMesh != null) previewInfoTextMesh.gameObject.SetActive(false);
            if (previewStatusTextMesh != null) previewStatusTextMesh.gameObject.SetActive(false);
        }

        private void UpdateWallBuildHud(Vector3 anchor, string infoText, string statusText, Color statusColor)
        {
            if (previewParent == null)
                CreatePreviewObjects();

            previewInfoTextMesh.text = infoText;
            previewInfoTextMesh.transform.position = anchor + Vector3.up * previewInfoHeight;
            previewInfoTextMesh.gameObject.SetActive(true);

            previewStatusTextMesh.text = statusText;
            previewStatusTextMesh.color = statusColor;
            previewStatusTextMesh.transform.position = anchor + Vector3.up * previewStatusHeight;
            previewStatusTextMesh.gameObject.SetActive(true);
        }

        private static string GetRoomBuildModeLabel(RoomBuildSubMode subMode)
        {
            return subMode switch
            {
                RoomBuildSubMode.Hall => "Hala",
                RoomBuildSubMode.Storage => "Magazyn",
                RoomBuildSubMode.Dispatcher => "Dyspozytornia",
                RoomBuildSubMode.Office => "Biuro",
                RoomBuildSubMode.Social => "Socjal",
                RoomBuildSubMode.Supervisor => "Kierownik",
                RoomBuildSubMode.Bathroom => "Lazienka",
                RoomBuildSubMode.Locker => "Szatnia",
                RoomBuildSubMode.Corridor => "Korytarz",
                RoomBuildSubMode.TrafficController => "Nastawnia",
                _ => "Pomieszczenie"
            };
        }

        private static string GetWallValidationLabel(WallBuildValidationReason reason)
        {
            return reason switch
            {
                WallBuildValidationReason.Ready => "Gotowe • LMB buduj • ESC anuluj • PPM cofnij",
                WallBuildValidationReason.TooSmall => "Pomieszczenie jest zbyt male",
                WallBuildValidationReason.RoomTypeTooSmall => "Wybrany typ wymaga wiekszego pomieszczenia",
                WallBuildValidationReason.OutsideBuildableArea => "Pomieszczenie wychodzi poza obszar budowy",
                WallBuildValidationReason.OverlapWalls => "Nowy obrys nachodzi na istniejace sciany",
                WallBuildValidationReason.OverlapTracks => "Pomieszczenie koliduje z torami",
                _ => "Wskaz pierwszy rog pomieszczenia"
            };
        }
    }
}
