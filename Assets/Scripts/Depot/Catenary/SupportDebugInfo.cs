using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Komponent debugowy dodawany do wizualizacji bramek/słupów.
    /// Kliknij w edytorze aby zobaczyć przypisane punkty podwieszenia.
    /// Rysuje Gizmos w Scene View gdy obiekt jest zaznaczony.
    /// </summary>
    public class SupportDebugInfo : MonoBehaviour
    {
        [Header("Typ konstrukcji")]
        public SupportType Type;

        [Header("Punkty podwieszenia")]
        public List<PointInfo> Points = new();

        [System.Serializable]
        public class PointInfo
        {
            public int TrackId;
            public Vector3 Position;
            public Vector3 AttachPosition;
            public float DistAlongTrack;
            public float LocalRadius;
        }

        /// <summary>
        /// Wypełnia dane z SupportStructure.
        /// </summary>
        public void Init(SupportStructure support)
        {
            Type = support.Type;
            Points.Clear();
            foreach (var p in support.Points)
            {
                Points.Add(new PointInfo
                {
                    TrackId = p.TrackId,
                    Position = p.Position,
                    AttachPosition = p.AttachPosition,
                    DistAlongTrack = p.DistAlongTrack,
                    LocalRadius = p.LocalRadius
                });
            }
        }

        private void OnDrawGizmosSelected()
        {
            float wireY = SupportOptimizer.ContactWireHeight;
            float beamY = SupportOptimizer.PoleHeight;

            foreach (var pt in Points)
            {
                // Punkt na torze — żółta kula
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(new Vector3(pt.Position.x, wireY, pt.Position.z), 0.15f);

                // AttachPosition — zielona kula
                if (pt.AttachPosition.sqrMagnitude > 0.001f)
                {
                    Gizmos.color = Color.green;
                    Vector3 attachWorld = new Vector3(pt.AttachPosition.x, beamY, pt.AttachPosition.z);
                    Gizmos.DrawSphere(attachWorld, 0.12f);

                    // Linia od attach do pozycji na torze
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(attachWorld,
                        new Vector3(pt.Position.x, wireY, pt.Position.z));
                }

                // Label z TrackId
#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(
                    new Vector3(pt.Position.x, wireY + 0.3f, pt.Position.z),
                    $"T{pt.TrackId} d={pt.DistAlongTrack:F1}m R={pt.LocalRadius:F1}");
#endif
            }

            // Pokaż odległości między punktami
            Gizmos.color = Color.red;
            for (int i = 0; i < Points.Count; i++)
            {
                for (int j = i + 1; j < Points.Count; j++)
                {
                    float dist = Vector3.Distance(Points[i].Position, Points[j].Position);
                    if (dist < 1f)
                    {
                        Vector3 midPoint = (Points[i].Position + Points[j].Position) * 0.5f;
                        midPoint.y = wireY + 0.6f;
#if UNITY_EDITOR
                        UnityEditor.Handles.color = Color.red;
                        UnityEditor.Handles.Label(midPoint, $"{dist:F3}m");
#endif
                        Gizmos.DrawLine(
                            new Vector3(Points[i].Position.x, wireY, Points[i].Position.z),
                            new Vector3(Points[j].Position.x, wireY, Points[j].Position.z));
                    }
                }
            }
        }
    }
}
