using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    public partial class TrackPopupUI
    {
        public void ShowPopup(int trackId, Vector3 worldPosition)
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackGraph == null || trackId < 0) return;

            selectedTrackId = trackId;
            var trackData = trackGraph.GetTrack(trackId);
            if (trackData == null) return;

            if (popupPanel == null)
                CreatePopup();

            popupPanel.SetActive(true);

            if (mainCamera != null)
            {
                Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition + Vector3.up * 3f);
                popupPanel.transform.position = screenPos;
            }

            if (trackNameInput != null)
                trackNameInput.text = trackData.Name;

            if (trackTypeText != null)
                trackTypeText.text = string.Format(LocalizationService.Get("popup_depot_track.type_format"),
                    GetTypeName(trackData.TrackType),
                    LocalizationService.Get(trackData.IsOccupied ? "popup_depot_track.occupied_yes" : "popup_depot_track.occupied_no"));

            if (lengthInput != null)
                lengthInput.text = trackData.Length.ToString("F1");

            if (radiusInput != null)
            {
                List<Vector3> polyline = trackGraph.GetTrackPolyline(trackId);
                if (polyline != null && polyline.Count >= 3)
                {
                    float minR = TrackGeometry.GetMinimumRadius(polyline);
                    radiusInput.text = minR < 10000f ? minR.ToString("F0") : "0";
                }
                else
                {
                    radiusInput.text = "0";
                }
            }

            if (angleValueText != null)
            {
                List<Vector3> anglePoly = trackGraph.GetTrackPolyline(trackId);
                Vector3 exitDir = (anglePoly != null && anglePoly.Count >= 2)
                    ? TrackGeometry.GetEndTangent(anglePoly).normalized
                    : Vector3.forward;
                float bearing = Mathf.Atan2(exitDir.x, exitDir.z) * Mathf.Rad2Deg;
                bearing = (bearing - mapNorthAngle % 360f + 360f) % 360f;
                angleValueText.text = bearing.ToString("F1");
            }

            bool isConnected = IsTrackConnected(trackId);
            bool isPermanent = trackData.IsPermanent;
            if (lengthInput != null) lengthInput.interactable = !isConnected && !isPermanent;
            if (radiusInput != null) radiusInput.interactable = !isConnected && !isPermanent;
            if (applyParamsButton != null) applyParamsButton.interactable = !isConnected && !isPermanent;
            if (deleteButton != null) deleteButton.interactable = !isPermanent;
            if (trackNameInput != null) trackNameInput.interactable = !isPermanent;

            UpdateCatenaryIcon(trackData.HasCatenary);
        }

        private string GetTypeName(DepotTrackType type) => type switch
        {
            DepotTrackType.Parking => LocalizationService.Get("popup_depot_track.type.parking"),
            DepotTrackType.Entry => LocalizationService.Get("popup_depot_track.type.entry"),
            DepotTrackType.Exit => LocalizationService.Get("popup_depot_track.type.exit"),
            DepotTrackType.Washing => LocalizationService.Get("popup_depot_track.type.washing"),
            DepotTrackType.Workshop => LocalizationService.Get("popup_depot_track.type.workshop"),
            DepotTrackType.Maneuver => LocalizationService.Get("popup_depot_track.type.maneuver"),
            _ => type.ToString()
        };

        private void UpdateCatenaryIcon(bool hasCatenary)
        {
            if (catenaryIcon != null)
                catenaryIcon.color = hasCatenary ? catenaryOnColor : catenaryOffColor;
        }

        private bool IsTrackConnected(int trackId)
        {
            if (trackGraph == null) return false;
            var trackData = trackGraph.GetTrack(trackId);
            if (trackData == null || trackData.EdgeIds.Count == 0) return false;

            int lastEdgeId = trackData.EdgeIds[trackData.EdgeIds.Count - 1];
            if (!trackGraph.Edges.ContainsKey(lastEdgeId)) return false;
            var lastEdge = trackGraph.Edges[lastEdgeId];

            if (trackGraph.Nodes.ContainsKey(lastEdge.ToNodeId) &&
                trackGraph.Nodes[lastEdge.ToNodeId].EdgeIds.Count > 1)
                return true;

            return false;
        }

        private void OnRenameClicked()
        {
            if (selectedTrackId < 0 || trackGraph == null || trackNameInput == null) return;
            string newName = trackNameInput.text;
            if (!string.IsNullOrEmpty(newName))
                trackGraph.RenameTrack(selectedTrackId, newName);
        }

        private void OnDeleteClicked()
        {
            if (selectedTrackId < 0) return;

            var builder = DepotServices.Get<PrefabTrackBuilder>();
            if (builder != null)
                builder.RemoveTrack(selectedTrackId);

            ClosePopup();
        }

        private void OnToggleCatenaryClicked()
        {
            if (selectedTrackId < 0 || trackGraph == null) return;

            var trackData = trackGraph.GetTrack(selectedTrackId);
            if (trackData == null) return;

            Log.Info("[TrackPopupUI] Catenary toggle disabled - use BuildCatenary tool instead");
        }

        private void OnApplyParams()
        {
            if (selectedTrackId < 0 || trackGraph == null) return;

            float newLength = 0f;
            float newRadius = 0f;

            if (lengthInput != null) float.TryParse(lengthInput.text, out newLength);
            if (radiusInput != null) float.TryParse(radiusInput.text, out newRadius);

            if (newLength < TrackGeometry.MIN_TRACK_LENGTH)
            {
                Log.Warn("[TrackPopupUI] Dlugosc za krotka");
                return;
            }

            if (newRadius != 0 && Mathf.Abs(newRadius) < TrackGeometry.MIN_RADIUS)
            {
                Log.Warn($"[TrackPopupUI] Promien musi byc >= {TrackGeometry.MIN_RADIUS} lub 0 (prosta)");
                return;
            }

            var trackData = trackGraph.GetTrack(selectedTrackId);
            if (trackData == null) return;

            List<Vector3> oldPolyline = trackGraph.GetTrackPolyline(selectedTrackId);
            if (oldPolyline == null || oldPolyline.Count < 2) return;

            Vector3 startPos = oldPolyline[0];
            Vector3 startDir = TrackGeometry.GetStartTangent(oldPolyline);

            List<Vector3> newPolyline;
            if (Mathf.Abs(newRadius) < 1f)
            {
                Vector3 endPos = startPos + startDir * newLength;
                newPolyline = TrackGeometry.GenerateStraightLine(startPos, endPos);
            }
            else
            {
                bool turnLeft = TrackGeometry.DetectTurnLeft(oldPolyline);
                newPolyline = TrackGeometry.GenerateArcFromParams(
                    startPos, startDir, Mathf.Abs(newRadius), newLength, turnLeft);
            }

            var builder = DepotServices.Get<PrefabTrackBuilder>();
            if (builder != null)
            {
                int newTrackId = builder.RebuildTrackWithPolyline(
                    selectedTrackId, newPolyline,
                    trackData.Name, trackData.TrackType, trackData.HasCatenary);

                if (newTrackId >= 0)
                {
                    selectedTrackId = newTrackId;
                    var newTrackData = trackGraph.GetTrack(newTrackId);
                    if (newTrackData != null)
                    {
                        if (trackTypeText != null)
                            trackTypeText.text = string.Format(LocalizationService.Get("popup_depot_track.type_format"),
                                GetTypeName(newTrackData.TrackType),
                                LocalizationService.Get(newTrackData.IsOccupied ? "popup_depot_track.occupied_yes" : "popup_depot_track.occupied_no"));
                        if (lengthInput != null)
                            lengthInput.text = newTrackData.Length.ToString("F1");
                    }
                }
            }
        }

        private void OnParallelClicked()
        {
            if (selectedTrackId < 0) return;
            ShowParallelDialog();
        }

        private void OnGenerateParallel()
        {
            if (parallelGenerator == null)
                parallelGenerator = DepotServices.Get<ParallelTrackGenerator>();

            if (parallelGenerator == null || selectedTrackId < 0) return;

            int leftCount = 0;
            int rightCount = 0;
            float spacing = 5f;

            if (leftCountInput != null) int.TryParse(leftCountInput.text, out leftCount);
            if (rightCountInput != null) int.TryParse(rightCountInput.text, out rightCount);
            if (spacingInput != null) float.TryParse(spacingInput.text, out spacing);

            if (leftCount <= 0 && rightCount <= 0)
            {
                Log.Warn("[TrackPopupUI] No parallel tracks to generate");
                return;
            }

            bool withCatenary = catenaryToggle != null && catenaryToggle.isOn;

            if (spacing < 3f) spacing = 3f;

            parallelGenerator.GenerateParallelTracks(
                selectedTrackId, leftCount, rightCount,
                spacing, TurnoutType.R190, withCatenary);

            CloseParallelDialog();
            ClosePopup();
        }

        public void ClosePopup()
        {
            if (popupPanel != null) popupPanel.SetActive(false);
            if (parallelDialog != null) parallelDialog.SetActive(false);
            selectedTrackId = -1;
        }

        public void ShowTurnoutPopup(TurnoutEntity turnout, Vector3 worldPosition)
        {
            if (popupPanel != null) popupPanel.SetActive(false);

            selectedTurnout = turnout;

            if (turnoutPopupPanel == null)
                CreateTurnoutPopup();

            turnoutPopupPanel.SetActive(true);

            if (mainCamera != null)
            {
                Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition + Vector3.up * 3f);
                turnoutPopupPanel.transform.position = screenPos;
            }

            if (turnoutNameText != null)
                turnoutNameText.text = turnout.DefinitionName;

            string typeName = LocalizationService.Get(turnout.Type == TurnoutEntityType.Crossover
                ? "popup_depot_track.turnout.type_crossover"
                : "popup_depot_track.turnout.type_normal");
            if (turnoutInfoText != null)
                turnoutInfoText.text = string.Format(LocalizationService.Get("popup_depot_track.turnout.info_format"),
                    typeName, turnout.MemberTrackIds.Count);
        }

        private void OnDeleteTurnoutClicked()
        {
            if (selectedTurnout == null) return;

            var builder = DepotServices.Get<PrefabTrackBuilder>();
            if (builder != null)
                builder.RemoveTurnout(selectedTurnout.TurnoutId);

            CloseTurnoutPopup();
        }

        private void CloseTurnoutPopup()
        {
            if (turnoutPopupPanel != null) turnoutPopupPanel.SetActive(false);
            selectedTurnout = null;
        }

        private void ShowParallelDialog()
        {
            if (parallelDialog == null)
                CreateParallelDialog();

            parallelDialog.SetActive(true);

            if (leftCountInput != null) leftCountInput.text = "0";
            if (rightCountInput != null) rightCountInput.text = "3";
            if (spacingInput != null) spacingInput.text = "5";
            if (catenaryToggle != null) catenaryToggle.isOn = false;
        }

        private void CloseParallelDialog()
        {
            if (parallelDialog != null) parallelDialog.SetActive(false);
        }
    }
}
