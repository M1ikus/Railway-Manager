using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// System torów wyjściowych z zajezdni.
    /// Buduje tory z prefabów (szyna_podklad.fbx) prowadzące w lewo do krawędzi mapy.
    /// Pociągi jadące tymi torami znikają po dotarciu do końca.
    /// </summary>
    public class ExitTrackController : MonoBehaviour
    {
        [Header("Track Prefab")]
        [Tooltip("Prefab segmentu toru (szyna_podklad.fbx z Models/Depot/Track/Test2)")]
        public GameObject trackSegmentPrefab;

        [Tooltip("Długość jednego segmentu toru (m). szyna_podklad = 0.65m")]
        public float segmentLength = 0.65f;

        [Header("Materiał toru")]
        [Tooltip("Gotowy materiał do użycia (np. z folderu Materials). Jeśli pusty — tworzy z tekstur poniżej.")]
        public Material trackMaterial;

        [Tooltip("Albedo texture (szyna_podklad_AlbedoTransparency)")]
        public Texture2D texAlbedo;
        [Tooltip("Normal map (szyna_podklad_Normal)")]
        public Texture2D texNormal;
        [Tooltip("AO map (szyna_podklad_AO)")]
        public Texture2D texAO;
        [Tooltip("Metallic+Smoothness map (szyna_podkladl_MetallicSmoothness)")]
        public Texture2D texMetallic;

        [Header("Exit Track Layout")]
        [Tooltip("Liczba torów wyjściowych")]
        public int numberOfExitTracks = 3;

        [Tooltip("Odstęp między torami wyjściowymi (m)")]
        public float exitTrackSpacing = 6.0f;

        [Tooltip("Długość torów wyjściowych (m)")]
        public float exitTrackLength = 200f;

        [Tooltip("Pozycja X lewej krawędzi mapy (tu zaczynają się tory)")]
        public float exitEdgeX = -2000f;

        [Tooltip("Pozycja Z pierwszego toru (0 = środek mapy)")]
        public float firstTrackZ = 0f;

        [Header("Track Segment Settings")]
        [Tooltip("Skala prefabu segmentu")]
        public Vector3 segmentScale = Vector3.one;

        [Tooltip("Rotacja segmentu (Euler X,Y,Z w stopniach).")]
        public Vector3 segmentRotationEuler = new Vector3(0, 90, 0);

        [Tooltip("Czy użyć auto-detekcji rotacji (testuje 6 wariantów i wybiera najlepszy)")]
        public bool autoDetectRotation = false;

        [Header("Despawn Settings")]
        [Tooltip("Margines za krawędzią mapy do usunięcia pociągu (m)")]
        public float despawnMargin = 50f;

        [Tooltip("Prędkość pociągu na torze wyjściowym (m/s). 25 km/h = 6.944 m/s")]
        public float exitSpeed = 6.944f;

        [Header("Debug")]
        public bool showGizmos = true;
        public Color gizmosColor = Color.magenta;

        // Wewnętrzne dane
        private Transform tracksParent;
        private List<ExitTrack> exitTracks = new();
        private List<ExitingTrain> exitingTrains = new();

        private Quaternion segmentRotation = Quaternion.identity;
        private Material runtimeTrackMat;

        public List<ExitTrack> ExitTracks => exitTracks;

        void Start()
        {
            tracksParent = new GameObject("ExitTracks").transform;
            tracksParent.SetParent(transform);

            PrepareTrackMaterial();
            DetermineRotation();
            BuildExitTracks();
        }

        void Update()
        {
            UpdateExitingTrains();
        }

        // ═══════════════════════════════════════════════════════════
        //  MATERIAŁ
        // ═══════════════════════════════════════════════════════════

        private void PrepareTrackMaterial()
        {
            // Jeśli podany gotowy materiał — użyj go
            if (trackMaterial != null)
            {
                runtimeTrackMat = trackMaterial;
                Log.Info("[ExitTrackController] Using assigned material: " + trackMaterial.name);
                return;
            }

            // Jeśli podane tekstury — zbuduj materiał
            if (texAlbedo != null)
            {
                runtimeTrackMat = MaterialFactory.CreateLit();
                runtimeTrackMat.name = "TrackSegment_RuntimeMat";

                runtimeTrackMat.mainTexture = texAlbedo;

                MaterialFactory.SetPbrMaps(runtimeTrackMat, texNormal, texMetallic, texAO, 1f);

                Log.Info($"[ExitTrackController] Built runtime material from textures");
                return;
            }

            runtimeTrackMat = null;
            Log.Warn("[ExitTrackController] No material and no textures assigned — segments will keep FBX materials");
        }

        private void ApplyMaterialToSegment(GameObject segment)
        {
            if (runtimeTrackMat == null) return;

            MeshRenderer[] renderers = segment.GetComponentsInChildren<MeshRenderer>();
            foreach (var rend in renderers)
            {
                Material[] mats = new Material[rend.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++)
                    mats[m] = runtimeTrackMat;
                rend.sharedMaterials = mats;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  ROTACJA SEGMENTU
        // ═══════════════════════════════════════════════════════════

        private void DetermineRotation()
        {
            if (!autoDetectRotation)
            {
                segmentRotation = Quaternion.Euler(segmentRotationEuler);
                Log.Info($"[ExitTrackController] Manual rotation: {segmentRotationEuler}");
                return;
            }

            if (trackSegmentPrefab == null) return;

            // Sprawdź bounds po różnych rotacjach i wybierz tę która daje:
            // - najdłuższy X (tor wzdłuż osi X)
            // - najniższy Y (tor leży płasko)
            // - szyny po bokach (Z umiarkowane)

            Quaternion[] candidates = new Quaternion[]
            {
                Quaternion.identity,                    // (0,0,0)
                Quaternion.Euler(0, 90, 0),            // obrót Y+90
                Quaternion.Euler(-90, 0, 0),           // Blender Z-up fix
                Quaternion.Euler(-90, 90, 0),          // Blender Z-up + obrót Y
                Quaternion.Euler(90, 0, 0),            // odwrócony Blender fix
                Quaternion.Euler(90, 90, 0),           // odwrócony + Y
            };

            float bestScore = float.MinValue;
            Quaternion bestRotation = Quaternion.identity;

            foreach (var candidateRot in candidates)
            {
                GameObject temp = Instantiate(trackSegmentPrefab, Vector3.zero, candidateRot);
                temp.transform.localScale = segmentScale;

                MeshRenderer[] renderers = temp.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        b.Encapsulate(renderers[i].bounds);

                    // Scoring:
                    // Dobry tor: długi X, mały Y (leży płasko), umiarkowane Z (rozstaw szyn)
                    // Score = X_size - 5 * Y_size (penalizuj wysoki Y bo to znaczy że stoi)
                    float score = b.size.x - 5f * b.size.y;

                    Log.Info($"[ExitTrackController] Candidate {candidateRot.eulerAngles}: X={b.size.x:F3} Y={b.size.y:F3} Z={b.size.z:F3} score={score:F3}");

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRotation = candidateRot;
                    }
                }

                DestroyImmediate(temp);
            }

            segmentRotation = bestRotation;
            Log.Info($"[ExitTrackController] Best rotation: {segmentRotation.eulerAngles} (score={bestScore:F3})");
        }

        // ═══════════════════════════════════════════════════════════
        //  BUDOWANIE TORÓW WYJŚCIOWYCH
        // ═══════════════════════════════════════════════════════════

        [ContextMenu("Build Exit Tracks")]
        public void BuildExitTracks()
        {
            ClearExitTracks();

            float exitEndX = exitEdgeX + exitTrackLength;

            for (int i = 0; i < numberOfExitTracks; i++)
            {
                float trackZ;
                if (i == 0)
                {
                    trackZ = firstTrackZ;
                }
                else
                {
                    int pair = (i + 1) / 2;
                    float sign = (i % 2 == 1) ? 1f : -1f;
                    trackZ = firstTrackZ + sign * pair * exitTrackSpacing;
                }

                Vector3 start = new Vector3(exitEdgeX, 0, trackZ);
                Vector3 end = new Vector3(exitEndX, 0, trackZ);

                ExitTrack track = BuildSingleExitTrack(start, end, i);
                exitTracks.Add(track);
            }

            Log.Info($"[ExitTrackController] Built {exitTracks.Count} exit tracks, {exitTrackLength}m each, from X={exitEdgeX} to X={exitEndX}");
        }

        private ExitTrack BuildSingleExitTrack(Vector3 start, Vector3 end, int index)
        {
            GameObject trackParent = new GameObject($"ExitTrack_{index}");
            trackParent.transform.SetParent(tracksParent);
            trackParent.transform.position = start;

            float totalLength = Vector3.Distance(start, end);

            if (trackSegmentPrefab != null && segmentLength > 0.01f)
            {
                int segmentCount = Mathf.CeilToInt(totalLength / segmentLength);

                for (int i = 0; i < segmentCount; i++)
                {
                    Vector3 segmentPos = new Vector3(
                        start.x + i * segmentLength,
                        start.y,
                        start.z
                    );

                    GameObject segment = Instantiate(trackSegmentPrefab, segmentPos, segmentRotation);
                    segment.name = $"Segment_{i}";
                    segment.transform.SetParent(trackParent.transform);
                    segment.transform.localScale = segmentScale;

                    // Nadaj materiał (runtime lub z inspektora)
                    ApplyMaterialToSegment(segment);
                }

                Log.Info($"[ExitTrackController] Track {index}: {segmentCount} × {segmentLength:F2}m");
            }
            else
            {
                BuildFallbackTrack(trackParent, start, end, totalLength);
            }

            return new ExitTrack
            {
                Id = index,
                TrackObject = trackParent,
                StartPosition = start,
                EndPosition = end,
                Length = totalLength,
                IsOccupied = false
            };
        }

        private void BuildFallbackTrack(GameObject parent, Vector3 start, Vector3 end, float length)
        {
            Vector3 direction = (end - start).normalized;
            Vector3 perp = Vector3.Cross(direction, Vector3.up).normalized;

            CreateFallbackRail(parent, start, end, direction, length, 0.7175f);
            CreateFallbackRail(parent, start, end, direction, length, -0.7175f);

            int sleeperCount = Mathf.CeilToInt(length / 0.6f);
            for (int i = 0; i <= sleeperCount; i++)
            {
                float t = i / (float)sleeperCount;
                Vector3 pos = Vector3.Lerp(start, end, t);

                GameObject sleeper = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sleeper.name = $"Sleeper_{i}";
                sleeper.transform.SetParent(parent.transform);
                sleeper.transform.position = pos - new Vector3(0, 0.1f, 0);
                sleeper.transform.localScale = new Vector3(2.5f, 0.2f, 0.25f);
                sleeper.transform.rotation = Quaternion.LookRotation(perp);

                var col = sleeper.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
        }

        private void CreateFallbackRail(GameObject parent, Vector3 start, Vector3 end, Vector3 direction, float length, float offset)
        {
            Vector3 perp = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 mid = (start + end) / 2 + perp * offset;

            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = offset > 0 ? "LeftRail" : "RightRail";
            rail.transform.SetParent(parent.transform);
            rail.transform.position = mid + new Vector3(0, 0.1f, 0);
            rail.transform.localScale = new Vector3(length, 0.2f, 0.15f);

            Material railMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(railMat, new Color(0.3f, 0.3f, 0.35f));
            railMat.SetFloat("_Metallic", 0.8f);
            rail.GetComponent<MeshRenderer>().material = railMat;

            var col = rail.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // ═══════════════════════════════════════════════════════════
        //  WYSYŁANIE POCIĄGÓW NA TOR WYJŚCIOWY
        // ═══════════════════════════════════════════════════════════

        public void SendTrainToExit(GameObject trainRoot, int exitTrackIndex = -1, float speed = 0f)
        {
            if (trainRoot == null)
            {
                Log.Warn("[ExitTrackController] Cannot send null train to exit!");
                return;
            }

            if (exitTrackIndex == -1)
                exitTrackIndex = FindAvailableExitTrack();

            if (exitTrackIndex < 0 || exitTrackIndex >= exitTracks.Count)
            {
                Log.Warn("[ExitTrackController] No available exit tracks!");
                return;
            }

            ExitTrack track = exitTracks[exitTrackIndex];
            track.IsOccupied = true;

            float useSpeed = speed > 0 ? speed : exitSpeed;

            trainRoot.transform.position = track.EndPosition;
            trainRoot.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

            exitingTrains.Add(new ExitingTrain
            {
                TrainObject = trainRoot,
                Track = track,
                Speed = useSpeed,
                DespawnX = exitEdgeX - despawnMargin
            });

            Log.Info($"[ExitTrackController] Train '{trainRoot.name}' → exit track {exitTrackIndex} at {useSpeed * 3.6f:F0} km/h");
        }

        public void SendVehicleToExit(VehicleUnit vehicle, int exitTrackIndex = -1, float speed = 0f)
        {
            if (vehicle?.GameObject != null)
                SendTrainToExit(vehicle.GameObject, exitTrackIndex, speed);
        }

        private int FindAvailableExitTrack()
        {
            for (int i = 0; i < exitTracks.Count; i++)
                if (!exitTracks[i].IsOccupied) return i;
            return -1;
        }

        // ═══════════════════════════════════════════════════════════
        //  RUCH I ZNIKANIE
        // ═══════════════════════════════════════════════════════════

        private void UpdateExitingTrains()
        {
            for (int i = exitingTrains.Count - 1; i >= 0; i--)
            {
                ExitingTrain train = exitingTrains[i];

                if (train.TrainObject == null)
                {
                    if (train.Track != null) train.Track.IsOccupied = false;
                    exitingTrains.RemoveAt(i);
                    continue;
                }

                train.TrainObject.transform.position += Vector3.left * train.Speed * Time.deltaTime;

                if (train.TrainObject.transform.position.x <= train.DespawnX)
                {
                    Log.Info($"[ExitTrackController] Train '{train.TrainObject.name}' despawned");
                    train.Track.IsOccupied = false;
                    Destroy(train.TrainObject);
                    exitingTrains.RemoveAt(i);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  CZYSZCZENIE
        // ═══════════════════════════════════════════════════════════

        [ContextMenu("Clear Exit Tracks")]
        public void ClearExitTracks()
        {
            foreach (var t in exitingTrains)
                if (t.TrainObject != null) Destroy(t.TrainObject);
            exitingTrains.Clear();

            foreach (var t in exitTracks)
                if (t.TrackObject != null) Destroy(t.TrackObject);
            exitTracks.Clear();

            if (tracksParent != null)
                foreach (Transform child in tracksParent)
                    Destroy(child.gameObject);
        }

        [ContextMenu("Rebuild Exit Tracks")]
        public void RebuildExitTracks()
        {
            ClearExitTracks();
            PrepareTrackMaterial();
            DetermineRotation();
            BuildExitTracks();
        }

        // ═══════════════════════════════════════════════════════════
        //  GIZMOS
        // ═══════════════════════════════════════════════════════════

        void OnDrawGizmos()
        {
            if (!showGizmos) return;

            Gizmos.color = gizmosColor;
            float exitEndX = exitEdgeX + exitTrackLength;

            for (int i = 0; i < numberOfExitTracks; i++)
            {
                float trackZ;
                if (i == 0)
                    trackZ = firstTrackZ;
                else
                {
                    int pair = (i + 1) / 2;
                    float sign = (i % 2 == 1) ? 1f : -1f;
                    trackZ = firstTrackZ + sign * pair * exitTrackSpacing;
                }

                Vector3 start = new Vector3(exitEdgeX, 0.5f, trackZ);
                Vector3 end = new Vector3(exitEndX, 0.5f, trackZ);

                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(end, 2f);

                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(
                    new Vector3(exitEdgeX - despawnMargin, 0.5f, trackZ),
                    new Vector3(despawnMargin * 2, 5, exitTrackSpacing * 0.8f)
                );
                Gizmos.color = gizmosColor;
            }

            float maxSpread = numberOfExitTracks * exitTrackSpacing;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(
                new Vector3(exitEdgeX, 0, firstTrackZ - maxSpread),
                new Vector3(exitEdgeX, 10, firstTrackZ + maxSpread)
            );
        }
    }

    [System.Serializable]
    public class ExitTrack
    {
        public int Id;
        public GameObject TrackObject;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public float Length;
        public bool IsOccupied;
    }

    public class ExitingTrain
    {
        public GameObject TrainObject;
        public ExitTrack Track;
        public float Speed;
        public float DespawnX;
    }
}
