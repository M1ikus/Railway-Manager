using System;
using System.Collections.Generic;
using RailwayManager.Core.Rendering;
using RailwayManager.SharedUI.Localization;
using UnityEngine;

namespace DepotSystem
{
    public partial class TrackBuildStateMachine
    {
        // ═══════════════════════════════════════════
        //  PREVIEW — UpdatePreview (geometria + walidacja) + visual objects
        // ═══════════════════════════════════════════

        private void UpdatePreview(Vector3 start, Vector3 end, int snapA, int snapB)
        {
            if (previewObj == null)
                CreatePreviewObject();

            previewObj.SetActive(true);

            if (Vector3.Distance(start, end) < TrackGeometry.MIN_TRACK_LENGTH)
            {
                ShowErrorPreview(start, end, TrackBuildValidationReason.TooShort, snapA, snapB);
                return;
            }

            bool bothSnapped = snapA >= 0 && snapB >= 0;

            List<Vector3> polyline;
            bool isValid;
            float displayRadius = 0f;
            bool isStraight = false;
            TrackBuildValidationReason validationReason = TrackBuildValidationReason.Ready;

            if (bothSnapped)
            {
                // Oba snappowane: CSC z surowych kierunków (U-turny, łączenie torów)
                Vector3 dirA = trackGraph.GetNodeDirection(snapA);
                Vector3 dirB = trackGraph.GetNodeDirection(snapB);

                // Blokuj gdy oba outward wskazują OD SIEBIE wzdłuż linii A→B
                // (= endpointy tego samego toru, budowa duplikowałaby istniejący segment)
                Vector3 abDir = (end - start).normalized;
                if (Vector3.Dot(dirA, abDir) < -0.3f && Vector3.Dot(dirB, -abDir) < -0.3f)
                {
                    ShowErrorPreview(start, end, TrackBuildValidationReason.DuplicateExistingTrack, snapA, snapB);
                    return;
                }

                polyline = TrackGeometry.CalculateRouteAutoFit(start, dirA, end, dirB, minRadius, true);

                if (polyline == null || polyline.Count < 2)
                {
                    ShowErrorPreview(start, end, TrackBuildValidationReason.NoValidRoute, snapA, snapB);
                    return;
                }

                float polylineMinR = TrackGeometry.GetMinimumRadius(polyline);
                displayRadius = polylineMinR;
                isValid = polylineMinR >= minRadius * 0.9f;
                isStraight = false;
                if (!isValid)
                    validationReason = TrackBuildValidationReason.RadiusTooTight;
            }
            else
            {
                // Jeden łuk do myszki: pokaż prawdziwy promień
                // Spłaszcz do XZ
                Vector3 s = start; s.y = 0;
                Vector3 e = end; e.y = 0;

                // Jeśli tylko koniec jest snappowany — zamień perspektywę:
                // oblicz łuk od snap pointa do wolnego punktu, potem odwróć polyline
                bool reverseResult = false;
                int activeSnap = snapA;

                if (snapA < 0 && snapB >= 0)
                {
                    Vector3 tmp = s; s = e; e = tmp;
                    activeSnap = snapB;
                    reverseResult = true;
                }

                Vector3 dirA = GetDirectionForPoint(s, activeSnap, e);
                Vector3 dA = dirA; dA.y = 0; dA.Normalize();
                Vector3 dB = (e - s).normalized;

                // Sprawdź prostą
                Vector3 toEnd = (e - s).normalized;
                float angle = Vector3.Angle(dA, toEnd);

                float distance = Vector3.Distance(s, e);

                bool isSnapped = activeSnap >= 0;

                // Kursor "za plecami" snap pointa — nie pozwól na prostą, wymuś łuk
                bool cursorBehind = isSnapped && Vector3.Dot(dA, toEnd) < 0f;

                // Oblicz promień
                float actualRadius = TrackGeometry.CalculateRequiredRadius(s, dA, e, dB);

                if (advancedMode && manualRadius > 0)
                    actualRadius = manualRadius;

                // Promień → ∞ = kursor prawie idealnie na linii dA
                bool radiusInfinite = float.IsInfinity(actualRadius) || float.IsNaN(actualRadius)
                    || actualRadius > 100000f;

                // Kursor za plecami + promień → ∞ = brak rozwiązania
                if (cursorBehind && radiusInfinite)
                {
                    ShowErrorPreview(start, end, TrackBuildValidationReason.NoValidRoute, snapA, snapB);
                    return;
                }

                // Prosta: próg kątowy + max odchylenie boczne 2m od linii kierunku
                float lateralOffset = Vector3.Cross(dA, (e - s)).magnitude; // odl. prostopadła
                const float maxLateralSnap = 2f;

                bool canBeStraight = !cursorBehind && lateralOffset < maxLateralSnap
                    && ((isSnapped && (radiusInfinite || angle < 1f))
                        || (!isSnapped && (angle < TrackGeometry.STRAIGHT_THRESHOLD_DEG)));

                if (canBeStraight)
                {
                    if (isSnapped)
                    {
                        // Rzutuj punkt końcowy na linię kierunku snap pointa → idealny kąt 0°
                        float projLen = Vector3.Dot(e - s, dA);
                        e = s + dA * projLen;
                    }
                    polyline = TrackGeometry.GenerateStraightLine(s, e);
                    isValid = true;
                    isStraight = true;
                    displayRadius = float.PositiveInfinity;
                }
                else
                {
                    // 1. Czysty pojedynczy łuk — okrąg styczny do dA w S przechodzący przez E
                    polyline = null;
                    isValid = false;
                    validationReason = TrackBuildValidationReason.RadiusTooTight;

                    float pureR = CalculatePureArcRadius(s, dA, e);
                    if (pureR >= minRadius)
                    {
                        polyline = GeneratePureArc(s, dA, e, pureR);
                        isValid = polyline != null && polyline.Count >= 2;
                        displayRadius = pureR;
                    }

                    // 2. Czysty łuk za ciasny → CSC z max promieniem i wstawką ~6m
                    if ((polyline == null || !isValid) && isSnapped && distance > 5f)
                    {
                        Vector3 travelEndDir = (s - e).normalized;
                        var csc = TrackGeometry.CalculateCSCWithTargetStraight(
                            s, dA, e, travelEndDir, minRadius, 6f);
                        if (csc != null && csc.Count >= 2)
                        {
                            polyline = csc;
                            displayRadius = TrackGeometry.GetMinimumRadius(csc);
                            isValid = displayRadius >= minRadius * 0.9f;
                        }
                    }

                    // 3. Fallback: stary łuk + styczna
                    if (polyline == null || polyline.Count < 2)
                    {
                        polyline = TrackGeometry.CalculateArcRoute(s, dA, e, dB, actualRadius);
                        displayRadius = actualRadius;
                        isValid = polyline != null && polyline.Count >= 2 && actualRadius >= minRadius;
                    }

                    if (polyline == null || polyline.Count < 2)
                    {
                        ShowErrorPreview(start, end, TrackBuildValidationReason.NoValidRoute, snapA, snapB);
                        return;
                    }

                    isStraight = false;
                }

                // Odwróć polyline jeśli zamieniliśmy perspektywę (snap na końcu)
                if (reverseResult && polyline != null)
                    polyline.Reverse();
            }

            // Walidacja bounds: jakikolwiek punkt trasy poza obszarem budowlanym → nieważne
            if (isValid && polyline != null && polyline.Count >= 2)
            {
                for (int i = 0; i < polyline.Count; i++)
                {
                    if (!IsInsideBuildableArea(polyline[i]))
                    {
                        isValid = false;
                        validationReason = TrackBuildValidationReason.OutsideBuildableArea;
                        break;
                    }
                }
            }

            // Walidacja kolizji: nowy tor nie może nakładać się na istniejące tory
            if (isValid && polyline != null && IsPolylineOverlapping(polyline))
            {
                isValid = false;
                validationReason = TrackBuildValidationReason.OverlapExistingTrack;
            }

            // Walidacja kolizji: nowy tor nie może przechodzić przez budynki
            if (isValid && polyline != null && IsPolylineOverlappingBuildings(polyline))
            {
                isValid = false;
                validationReason = TrackBuildValidationReason.OverlapBuilding;
            }

            previewIsValid = isValid;
            previewValidationReason = isValid ? TrackBuildValidationReason.Ready : validationReason;
            SetPreviewMetrics(polyline, snapA, snapB, displayRadius, isStraight);

            Color color = isValid ? previewColor : previewErrorColor;
            previewLine.startColor = color;
            previewLine.endColor = color;

            Vector3[] positions = new Vector3[polyline.Count];
            for (int i = 0; i < polyline.Count; i++)
                positions[i] = polyline[i] + Vector3.up * 0.2f;

            previewLine.positionCount = positions.Length;
            previewLine.SetPositions(positions);

            // Wyświetl kąt wyjściowy przy endpoincie (azymut względem mapy)
            if (angleTextMesh != null)
            {
                Vector3 exitDir = TrackGeometry.GetEndTangent(polyline).normalized;
                float bearing = Mathf.Atan2(exitDir.x, exitDir.z) * Mathf.Rad2Deg;
                bearing = (bearing - mapNorthAngle % 360f + 360f) % 360f;
                angleTextMesh.text = $"{bearing:F1}°";
                angleTextMesh.transform.position = polyline[polyline.Count - 1] + Vector3.up * 2.5f;
                angleTextMesh.gameObject.SetActive(true);
            }

            UpdatePreviewOverlay(polyline);
        }

        private void ShowErrorPreview(Vector3 start, Vector3 end, TrackBuildValidationReason reason, int snapA = -1, int snapB = -1)
        {
            previewIsValid = false;
            previewValidationReason = reason;
            previewPolyline = new List<Vector3> { start, end };
            previewLengthMeters = Vector3.Distance(start, end);
            previewDisplayRadiusMeters = 0f;
            previewEstimatedCostPln = EstimateCost(previewLengthMeters);
            previewIsStraight = true;
            previewUsesManualRadius = false;
            previewAnchorModeLabel = GetAnchorModeLabel(snapA, snapB);
            previewLine.positionCount = 2;
            previewLine.SetPosition(0, start + Vector3.up * 0.2f);
            previewLine.SetPosition(1, end + Vector3.up * 0.2f);
            previewLine.startColor = previewErrorColor;
            previewLine.endColor = previewErrorColor;

            if (angleTextMesh != null)
                angleTextMesh.gameObject.SetActive(false);

            UpdatePreviewOverlay(previewPolyline);
        }

        private void SetPreviewMetrics(List<Vector3> polyline, int snapA, int snapB, float displayRadius, bool isStraight)
        {
            previewPolyline = polyline != null ? new List<Vector3>(polyline) : null;
            previewLengthMeters = polyline != null ? TrackGeometry.CalculatePolylineLength(polyline) : 0f;
            previewDisplayRadiusMeters = displayRadius;
            previewEstimatedCostPln = EstimateCost(previewLengthMeters);
            previewIsStraight = isStraight;
            previewUsesManualRadius = advancedMode && manualRadius > 0f && !isStraight;
            previewAnchorModeLabel = GetAnchorModeLabel(snapA, snapB);
        }

        private void UpdatePreviewOverlay(List<Vector3> polyline)
        {
            if (angleTextMesh == null || statusTextMesh == null || polyline == null || polyline.Count < 2)
                return;

            float anchorDistance = Mathf.Clamp(previewLengthMeters * 0.5f, 0.25f, Mathf.Max(0.25f, previewLengthMeters));
            var (anchorPos, _) = TrackGeometry.GetPointAtDistance(polyline, anchorDistance);
            Vector3 exitDir = TrackGeometry.GetEndTangent(polyline).normalized;
            float bearing = Mathf.Atan2(exitDir.x, exitDir.z) * Mathf.Rad2Deg;
            bearing = (bearing - mapNorthAngle % 360f + 360f) % 360f;

            string lengthText = previewLengthMeters >= 10f
                ? $"{previewLengthMeters:F0} m"
                : $"{previewLengthMeters:F1} m";

            string radiusText = previewIsStraight
                ? "R prosta"
                : previewDisplayRadiusMeters > 0f
                    ? $"R {previewDisplayRadiusMeters:F0} m"
                    : "R auto";

            if (previewUsesManualRadius && !previewIsStraight && previewDisplayRadiusMeters > 0f)
                radiusText += " ręczne";

            string costText = estimatedCostPerMeterPln > 0
                ? $"Szac. {NumberFormatService.FormatCurrency(previewEstimatedCostPln)}"
                : "Szac. koszt —";

            string geometryText = previewIsStraight ? "Prosta" : "Łuk";

            angleTextMesh.text =
                $"{geometryText} • {previewAnchorModeLabel} • Az {bearing:F1}°\n" +
                $"Dł. {lengthText} • {radiusText} • {costText}";
            angleTextMesh.transform.position = anchorPos + Vector3.up * previewInfoHeight;
            angleTextMesh.color = previewIsValid ? Color.white : new Color(1f, 0.88f, 0.88f, 1f);
            angleTextMesh.gameObject.SetActive(true);

            statusTextMesh.text = previewIsValid
                ? "Gotowe • LMB postaw • ESC anuluj"
                : GetValidationReasonLabel(previewValidationReason);
            statusTextMesh.transform.position = anchorPos + Vector3.up * previewStatusHeight;
            statusTextMesh.color = previewIsValid ? new Color(0.60f, 1f, 0.60f, 1f) : previewErrorColor;
            statusTextMesh.gameObject.SetActive(true);
        }

        private long EstimateCost(float lengthMeters)
            => estimatedCostPerMeterPln <= 0
                ? 0L
                : (long)Math.Round(lengthMeters * estimatedCostPerMeterPln);

        private static string GetAnchorModeLabel(int snapA, int snapB)
        {
            bool startSnapped = snapA >= 0;
            bool endSnapped = snapB >= 0;
            return $"{(startSnapped ? "SNAP" : "GRID")}→{(endSnapped ? "SNAP" : "GRID")}";
        }

        private static string GetValidationReasonLabel(TrackBuildValidationReason reason)
        {
            return reason switch
            {
                TrackBuildValidationReason.Ready => "Gotowe do postawienia",
                TrackBuildValidationReason.TooShort => $"Za krótki odcinek (min {TrackGeometry.MIN_TRACK_LENGTH:F0} m)",
                TrackBuildValidationReason.DuplicateExistingTrack => "Ten odcinek dubluje istniejący tor",
                TrackBuildValidationReason.NoValidRoute => "Brak poprawnej geometrii dla tego połączenia",
                TrackBuildValidationReason.RadiusTooTight => "Promień łuku jest zbyt mały",
                TrackBuildValidationReason.OutsideBuildableArea => "Tor wychodzi poza obszar budowy",
                TrackBuildValidationReason.OverlapExistingTrack => "Nowy tor nachodzi na istniejący tor",
                TrackBuildValidationReason.OverlapBuilding => "Tor koliduje z budynkiem lub ścianą",
                _ => "Wybierz punkt końcowy toru"
            };
        }

        private void CreatePreviewObject()
        {
            previewObj = new GameObject("TrackPreview");
            previewLine = previewObj.AddComponent<LineRenderer>();
            previewLine.material = MaterialFactory.CreateLine();
            previewLine.startColor = previewColor;
            previewLine.endColor = previewColor;
            previewLine.startWidth = previewWidth;
            previewLine.endWidth = previewWidth;
            previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewLine.positionCount = 0;

            // Tekst z kątem wyjściowym
            var textObj = new GameObject("AngleText");
            textObj.transform.SetParent(previewObj.transform, false);
            angleTextMesh = textObj.AddComponent<TextMesh>();
            angleTextMesh.fontSize = 48;
            angleTextMesh.characterSize = 0.12f;
            angleTextMesh.anchor = TextAnchor.MiddleCenter;
            angleTextMesh.alignment = TextAlignment.Center;
            angleTextMesh.color = Color.white;
            angleTextMesh.fontStyle = FontStyle.Bold;
            angleTextMesh.gameObject.SetActive(false);

            var statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(previewObj.transform, false);
            statusTextMesh = statusObj.AddComponent<TextMesh>();
            statusTextMesh.fontSize = 38;
            statusTextMesh.characterSize = 0.085f;
            statusTextMesh.anchor = TextAnchor.LowerCenter;
            statusTextMesh.alignment = TextAlignment.Center;
            statusTextMesh.color = previewColor;
            statusTextMesh.fontStyle = FontStyle.Bold;
            statusTextMesh.gameObject.SetActive(false);
        }

        private void CreateSnapDirectionPreview()
        {
            snapDirectionPreviewObj = new GameObject("SnapDirectionPreview");
            snapDirectionPreviewObj.transform.SetParent(transform, false);
            snapDirectionPreviewLine = snapDirectionPreviewObj.AddComponent<LineRenderer>();
            snapDirectionPreviewLine.material = MaterialFactory.CreateLine();
            snapDirectionPreviewLine.startColor = snapDirectionColor;
            snapDirectionPreviewLine.endColor = snapDirectionColor;
            snapDirectionPreviewLine.startWidth = 0.16f;
            snapDirectionPreviewLine.endWidth = 0.10f;
            snapDirectionPreviewLine.positionCount = 0;
            snapDirectionPreviewLine.numCapVertices = 6;
            snapDirectionPreviewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            snapDirectionPreviewObj.SetActive(false);
        }

        private void UpdateSnapDirectionPreview(int snapNodeId)
        {
            if (snapNodeId < 0 || trackGraph == null)
            {
                HideSnapDirectionPreview();
                return;
            }

            if (snapDirectionPreviewObj == null)
                CreateSnapDirectionPreview();

            var node = trackGraph.GetNode(snapNodeId);
            if (node == null)
            {
                HideSnapDirectionPreview();
                return;
            }

            Vector3 direction = trackGraph.GetNodeDirection(snapNodeId);
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                HideSnapDirectionPreview();
                return;
            }

            direction.Normalize();

            Vector3 start = node.Position + Vector3.up * snapDirectionHeight;
            Vector3 end = start + direction * snapDirectionLength;
            Vector3 backDirection = -direction;
            Vector3 leftHead = end + Quaternion.AngleAxis(snapDirectionArrowHeadAngle, Vector3.up) * backDirection * snapDirectionArrowHeadLength;
            Vector3 rightHead = end + Quaternion.AngleAxis(-snapDirectionArrowHeadAngle, Vector3.up) * backDirection * snapDirectionArrowHeadLength;

            snapDirectionPreviewLine.startColor = snapDirectionColor;
            snapDirectionPreviewLine.endColor = snapDirectionColor;
            snapDirectionPreviewLine.positionCount = 5;
            snapDirectionPreviewLine.SetPosition(0, start);
            snapDirectionPreviewLine.SetPosition(1, end);
            snapDirectionPreviewLine.SetPosition(2, leftHead);
            snapDirectionPreviewLine.SetPosition(3, end);
            snapDirectionPreviewLine.SetPosition(4, rightHead);
            snapDirectionPreviewObj.SetActive(true);
        }

        private void HideSnapDirectionPreview()
        {
            if (snapDirectionPreviewLine != null)
                snapDirectionPreviewLine.positionCount = 0;

            if (snapDirectionPreviewObj != null)
                snapDirectionPreviewObj.SetActive(false);
        }

        private void HidePreviewVisuals()
        {
            if (previewLine != null)
                previewLine.positionCount = 0;

            if (angleTextMesh != null)
                angleTextMesh.gameObject.SetActive(false);

            if (statusTextMesh != null)
                statusTextMesh.gameObject.SetActive(false);
        }

        private void ShowPausedEndSelectionOverlay()
        {
            if (previewObj == null)
                CreatePreviewObject();

            previewObj.SetActive(true);
            HidePreviewVisuals();

            if (statusTextMesh == null)
                return;

            statusTextMesh.text = pausedEndSelectionMessage;
            statusTextMesh.transform.position = pointA + Vector3.up * previewStatusHeight;
            statusTextMesh.color = new Color(1f, 0.95f, 0.60f, 1f);
            statusTextMesh.gameObject.SetActive(true);
        }

        private void ShowDeletePreview(Vector3 cursorWorldPos, DeletePreviewTarget? target)
        {
            if (previewObj == null)
                CreatePreviewObject();

            previewObj.SetActive(true);
            HidePreviewVisuals();

            if (target == null || target.Value.Segment == null)
            {
                if (statusTextMesh != null)
                {
                    statusTextMesh.text = $"Brak toru w zasiegu Delete ({deletePreviewMaxDistance:F0} m) • ESC wroc";
                    statusTextMesh.transform.position = cursorWorldPos + Vector3.up * previewStatusHeight;
                    statusTextMesh.color = new Color(1f, 0.92f, 0.60f, 1f);
                    statusTextMesh.gameObject.SetActive(true);
                }
                return;
            }

            var previewTarget = target.Value;
            List<Vector3> polyline = previewTarget.Segment.Polyline;
            if ((polyline == null || polyline.Count < 2) && trackGraph != null)
                polyline = trackGraph.GetTrackPolyline(previewTarget.Segment.GraphTrackId);
            if (polyline == null || polyline.Count < 2)
                return;

            if (previewLine != null)
            {
                Color lineColor = previewTarget.CanRemove ? deletePreviewColor : deletePreviewBlockedColor;
                previewLine.startColor = lineColor;
                previewLine.endColor = lineColor;

                Vector3[] positions = new Vector3[polyline.Count];
                for (int i = 0; i < polyline.Count; i++)
                    positions[i] = polyline[i] + Vector3.up * 0.22f;

                previewLine.positionCount = positions.Length;
                previewLine.SetPositions(positions);
            }

            float polylineLength = TrackGeometry.CalculatePolylineLength(polyline);
            float anchorDistance = Mathf.Clamp(polylineLength * 0.5f, 0.25f, Mathf.Max(0.25f, polylineLength));
            var (anchorPos, _) = TrackGeometry.GetPointAtDistance(polyline, anchorDistance);

            string trackName = previewTarget.TrackData?.Name;
            if (string.IsNullOrWhiteSpace(trackName))
                trackName = $"Tor #{previewTarget.Segment.GraphTrackId}";

            string trackType = previewTarget.TrackData != null
                ? previewTarget.TrackData.TrackType.ToString()
                : "Tor";

            string actionHint = previewTarget.IsPermanent
                ? "Tor permanentny • nie mozna usunac"
                : previewTarget.RemovesEntireTurnout
                    ? "Usuniesz caly rozjazd"
                    : "Usuniesz pojedynczy tor";

            if (angleTextMesh != null)
            {
                angleTextMesh.text =
                    $"{trackName} • {trackType}\n" +
                    $"Dł. {polylineLength:F0} m • kursor {previewTarget.DistanceMeters:F1} m • {actionHint}";
                angleTextMesh.transform.position = anchorPos + Vector3.up * previewInfoHeight;
                angleTextMesh.color = previewTarget.CanRemove
                    ? new Color(1f, 0.97f, 0.92f, 1f)
                    : new Color(1f, 0.94f, 0.82f, 1f);
                angleTextMesh.gameObject.SetActive(true);
            }

            if (statusTextMesh != null)
            {
                statusTextMesh.text = previewTarget.CanRemove
                    ? "Tryb usuwania • LMB lub DEL usuń • ESC wróć"
                    : "Tryb usuwania • ten tor jest chroniony • ESC wróć";
                statusTextMesh.transform.position = anchorPos + Vector3.up * previewStatusHeight;
                statusTextMesh.color = previewTarget.CanRemove
                    ? new Color(1f, 0.62f, 0.42f, 1f)
                    : deletePreviewBlockedColor;
                statusTextMesh.gameObject.SetActive(true);
            }
        }

        private void CreatePointAMarker(Vector3 position)
        {
            if (pointAMarker != null) Destroy(pointAMarker);

            pointAMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointAMarker.name = "PointA_Marker";
            pointAMarker.transform.position = position + Vector3.up * 0.3f;
            pointAMarker.transform.localScale = Vector3.one * 0.6f;

            Material mat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(mat, Color.green);
            MaterialFactory.SetEmission(mat, Color.green * 0.5f);
            pointAMarker.GetComponent<MeshRenderer>().material = mat;
            Destroy(pointAMarker.GetComponent<SphereCollider>());
        }

        private void ClearPreview()
        {
            if (previewObj != null)
            {
                Destroy(previewObj);
                previewObj = null;
                previewLine = null;
                angleTextMesh = null;
                statusTextMesh = null;
            }
            if (snapDirectionPreviewObj != null)
            {
                Destroy(snapDirectionPreviewObj);
                snapDirectionPreviewObj = null;
                snapDirectionPreviewLine = null;
            }
            if (pointAMarker != null)
            {
                Destroy(pointAMarker);
                pointAMarker = null;
            }

            previewPolyline = null;
        }

        // ═══════════════════════════════════════════
        //  PURE ARC GEOMETRY — single tangent arc through E
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oblicza promień jedynego okręgu stycznego do dA w punkcie S, przechodzącego przez E.
        /// R = |d|² / (2 * |d · n|), gdzie d = E-S, n = prostopadły do dA.
        /// Zwraca float.MaxValue jeśli S i E są współliniowe z dA.
        /// </summary>
        private static float CalculatePureArcRadius(Vector3 s, Vector3 dA, Vector3 e)
        {
            Vector3 d = e - s;
            d.y = 0;
            Vector3 n = Vector3.Cross(Vector3.up, dA).normalized;
            float dotDN = Vector3.Dot(d, n);
            if (Mathf.Abs(dotDN) < 0.01f) return float.MaxValue; // współliniowe → prosta
            return d.sqrMagnitude / (2f * Mathf.Abs(dotDN));
        }

        /// <summary>
        /// Generuje polyline dla czystego łuku (okrąg styczny do dA w S przechodzący przez E).
        /// </summary>
        private static List<Vector3> GeneratePureArc(Vector3 s, Vector3 dA, Vector3 e, float radius)
        {
            Vector3 n = Vector3.Cross(Vector3.up, dA).normalized;
            Vector3 d = e - s; d.y = 0;
            float dotDN = Vector3.Dot(d, n);

            // Centrum po tej stronie gdzie leży E
            Vector3 center = dotDN > 0
                ? s + n * radius
                : s - n * radius;
            bool ccw = dotDN < 0; // CCW jeśli centrum po lewej

            // Kąty
            Vector3 toS = s - center;
            Vector3 toE = e - center;
            float startAngle = Mathf.Atan2(toS.z, toS.x);
            float endAngle = Mathf.Atan2(toE.z, toE.x);

            float arcAngle = endAngle - startAngle;
            if (ccw)
            {
                while (arcAngle <= 0) arcAngle += 2f * Mathf.PI;
                if (arcAngle > 2f * Mathf.PI) arcAngle -= 2f * Mathf.PI;
            }
            else
            {
                while (arcAngle >= 0) arcAngle -= 2f * Mathf.PI;
                if (arcAngle < -2f * Mathf.PI) arcAngle += 2f * Mathf.PI;
            }

            // Odrzuć łuki > 180° (idą okrężnie, nienaturalne)
            if (Mathf.Abs(arcAngle) > Mathf.PI)
                return null;

            float arcLength = Mathf.Abs(arcAngle) * radius;
            int samples = Mathf.Max(3, Mathf.CeilToInt(arcLength / TrackGeometry.ARC_SAMPLE_STEP));

            var result = new List<Vector3>(samples + 1);
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float angle = startAngle + arcAngle * t;
                result.Add(new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0f,
                    center.z + Mathf.Sin(angle) * radius));
            }
            return result;
        }
    }
}
