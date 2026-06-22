using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    /// <summary>
    /// Represents a railway network as a graph for pathfinding and train simulation
    /// Processes railway features with segment IDs and junction information
    /// ✅ FIXED: Now uses tolerance-based junction matching to handle float precision issues
    /// </summary>
    public class RailwayGraph : MonoBehaviour
    {
        [Header("Graph Data")]
        public bool buildOnStart = false;
        public bool showDebugInfo = false;
        
        [Header("Junction Matching")]
        [Tooltip("Tolerance for matching junction positions (meters)")]
        public float junctionTolerance = 0.5f;
        
        // Graph structures
        private Dictionary<int, RailwaySegment> segments = new();
        private List<RailwayJunction> junctions = new();  // ✅ CHANGED: List instead of Dictionary for tolerance-based matching

        // M-PL: spatial hash dla junctions — O(N²) GetJunctionAt linear search
        // blokował BuildGraph dla 100k+ junctions na pełnej Polsce (~9 BILION ops).
        // Cell size 1m, tolerance typowo 0.5m → check 3x3 sąsiadujących cells.
        private const float JunctionHashCellSizeM = 1f;
        private readonly Dictionary<long, List<int>> _junctionSpatialHash = new();

        // Public accessors
        public Dictionary<int, RailwaySegment> Segments => segments;
        public List<RailwayJunction> Junctions => junctions;  // ✅ CHANGED
        public int SegmentCount => segments.Count;
        public int JunctionCount => junctions.Count;

        void Start()
        {
            if (buildOnStart)
            {
                var mapLoader = FindAnyObjectByType<MapLoader>();
                if (mapLoader != null)
                {
                    BuildGraph(mapLoader.GetLayer(BinaryFormat.LayerType.Railways));
                }
            }
        }
        
        /// <summary>
        /// Builds the railway graph from railway features
        /// ✅ FIXED: Now correctly matches junctions with tolerance for float precision
        /// </summary>
        public void BuildGraph(List<MeshGeometry> railways)
        {
            Log.Info("[RailwayGraph] Building railway graph...");
            float startTime = Time.realtimeSinceStartup;

            segments.Clear();
            junctions.Clear();
            _junctionSpatialHash.Clear(); // M-PL: reset spatial index
            
            if (railways.Count == 0)
            {
                Log.Warn("[RailwayGraph] No railway features to process!");
                return;
            }
            
            // ✅ VERIFICATION: Check if SegmentIds are valid
            int featuresWithSegments = 0;
            int featuresWithJunctions = 0;
            
            foreach (var feature in railways)
            {
                if (feature.SegmentIds.Count > 0)
                    featuresWithSegments++;
                if (feature.JunctionIndices.Count > 0)
                    featuresWithJunctions++;
            }
            
            if (showDebugInfo)
            {
                Log.Info($"[RailwayGraph] Processing {railways.Count} railway features");
                Log.Info($"[RailwayGraph]   Features with SegmentIds: {featuresWithSegments}");
                Log.Info($"[RailwayGraph]   Features with JunctionIndices: {featuresWithJunctions}");
            }
            
            // Pass 1: Create segments
            foreach (var feature in railways)
            {
                if (feature.SegmentIds.Count == 0)
                {
                    if (showDebugInfo)
                        Log.Warn("[RailwayGraph] Railway feature has no segment IDs!");
                    continue;
                }
                
                // Each polyline can have multiple segments
                // SegmentIds.Count should be Vertices.Count - 1
                for (int i = 0; i < feature.SegmentIds.Count; i++)
                {
                    int segmentId = feature.SegmentIds[i];
                    
                    // Get segment vertices (from i to i+1)
                    if (i + 1 < feature.Vertices.Count)
                    {
                        var segment = new RailwaySegment
                        {
                            Id = segmentId,
                            Start = feature.Vertices[i],
                            End = feature.Vertices[i + 1],
                            Metadata = feature.Metadata
                        };
                        
                        // ✅ VERIFICATION: Check for reasonable segment IDs
                        if (segmentId <= 0)
                        {
                            Log.Warn($"[RailwayGraph] Invalid segment ID: {segmentId}");
                        }
                        
                        segments[segmentId] = segment;
                    }
                }
            }
            
            // Pass 2: Identify junctions from JunctionIndices
            foreach (var feature in railways)
            {
                foreach (int junctionVertexIndex in feature.JunctionIndices)
                {
                    if (junctionVertexIndex < feature.Vertices.Count)
                    {
                        Vector2 pos = feature.Vertices[junctionVertexIndex];
                        
                        // ✅ FIXED: Use tolerance-based matching instead of exact Dictionary lookup
                        var existingJunction = GetJunctionAt(pos, junctionTolerance);
                        
                        if (existingJunction == null)
                        {
                            // Create new junction
                            var newJunction = new RailwayJunction
                            {
                                Position = pos,
                                ConnectedSegments = new List<int>()
                            };
                            junctions.Add(newJunction);
                            AddJunctionToHash(junctions.Count - 1); // M-PL: index w spatial hash
                        }
                    }
                    else
                    {
                        Log.Warn($"[RailwayGraph] Junction index {junctionVertexIndex} out of bounds (vertices: {feature.Vertices.Count})");
                    }
                }
            }
            
            // Pass 3: Connect segments to junctions (using tolerance-based lookup)
            // ✅ FIXED: This is the critical fix - no longer uses exact Vector2 comparison
            int connectionsFound = 0;
            
            foreach (var segment in segments.Values)
            {
                // Check if start point is near a junction (tolerance-based)
                var startJunction = GetJunctionAt(segment.Start, junctionTolerance);
                if (startJunction != null)
                {
                    if (!startJunction.ConnectedSegments.Contains(segment.Id))
                    {
                        startJunction.ConnectedSegments.Add(segment.Id);
                        connectionsFound++;
                    }
                }
                
                // Check if end point is near a junction (tolerance-based)
                var endJunction = GetJunctionAt(segment.End, junctionTolerance);
                if (endJunction != null)
                {
                    if (!endJunction.ConnectedSegments.Contains(segment.Id))
                    {
                        endJunction.ConnectedSegments.Add(segment.Id);
                        connectionsFound++;
                    }
                }
            }
            
            float buildTime = Time.realtimeSinceStartup - startTime;
            
            if (showDebugInfo)
            {
                Log.Info($"[RailwayGraph] ✓ Graph built in {buildTime:F2}s");
                Log.Info($"[RailwayGraph]   Segments: {segments.Count}");
                Log.Info($"[RailwayGraph]   Junctions: {junctions.Count}");
                Log.Info($"[RailwayGraph]   Junction-Segment connections: {connectionsFound}");
                
                // Stats about junctions
                int twoWay = 0, threeWay = 0, fourWayPlus = 0, disconnected = 0;
                foreach (var junction in junctions)
                {
                    int connections = junction.ConnectedSegments.Count;
                    if (connections == 0) disconnected++;
                    else if (connections == 2) twoWay++;
                    else if (connections == 3) threeWay++;
                    else if (connections >= 4) fourWayPlus++;
                }
                
                Log.Info($"[RailwayGraph]   Junction types: {disconnected} disconnected, {twoWay} simple, " +
                         $"{threeWay} switches, {fourWayPlus} complex");
                
                // ✅ WARNING: If many disconnected junctions, there's a problem
                if (disconnected > junctions.Count * 0.5f && junctions.Count > 0)
                {
                    Log.Warn($"[RailwayGraph] ⚠️ {disconnected}/{junctions.Count} junctions are disconnected!");
                    Log.Warn($"[RailwayGraph] This may indicate incorrect junction matching.");
                    Log.Warn($"[RailwayGraph] Try increasing junctionTolerance (current: {junctionTolerance}m)");
                }
                
                // ✅ VERIFICATION: Warn if data looks suspicious
                if (segments.Count == 0 && featuresWithSegments > 0)
                {
                    Log.Error("[RailwayGraph] ⚠️ CRITICAL: Features have SegmentIds but no segments created!");
                    Log.Error("[RailwayGraph] This may indicate that SegmentIds contain incorrect data.");
                    Log.Error("[RailwayGraph] Please verify that MeshGeometry.Read() correctly reads HoleStarts BEFORE SegmentIds.");
                }
            }
        }
        
        /// <summary>
        /// Finds a segment by ID
        /// </summary>
        public RailwaySegment GetSegment(int segmentId)
        {
            return segments.TryGetValue(segmentId, out var segment) ? segment : null;
        }
        
        /// <summary>
        /// Finds junction at a specific position (with tolerance)
        /// ✅ FIXED: Now the primary method for finding junctions
        /// </summary>
        public RailwayJunction GetJunctionAt(Vector2 position, float tolerance = 0.1f)
        {
            // M-PL: spatial hash O(1) lookup — sprawdź 3x3 sąsiadujących cells
            // (tolerance ≤ cellSize, więc match może być w sąsiedniej komórce).
            int cx = Mathf.FloorToInt(position.x / JunctionHashCellSizeM);
            int cy = Mathf.FloorToInt(position.y / JunctionHashCellSizeM);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                if (!_junctionSpatialHash.TryGetValue(key, out var indices)) continue;
                foreach (int idx in indices)
                {
                    if (Vector2.Distance(junctions[idx].Position, position) < tolerance)
                        return junctions[idx];
                }
            }
            return null;
        }

        /// <summary>Dodaje junction do spatial hash. Wywoływane po każdym junctions.Add().</summary>
        private void AddJunctionToHash(int junctionIndex)
        {
            var pos = junctions[junctionIndex].Position;
            int cx = Mathf.FloorToInt(pos.x / JunctionHashCellSizeM);
            int cy = Mathf.FloorToInt(pos.y / JunctionHashCellSizeM);
            long key = ((long)(uint)cx << 32) | (uint)cy;
            if (!_junctionSpatialHash.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _junctionSpatialHash[key] = list;
            }
            list.Add(junctionIndex);
        }
        
        /// <summary>
        /// Finds all segments connected to a junction
        /// </summary>
        public List<RailwaySegment> GetConnectedSegments(RailwayJunction junction)
        {
            var result = new List<RailwaySegment>();
            foreach (int segmentId in junction.ConnectedSegments)
            {
                if (segments.TryGetValue(segmentId, out var segment))
                    result.Add(segment);
            }
            return result;
        }
        
        /// <summary>
        /// ✅ UPDATED: Diagnostic method to verify graph integrity
        /// </summary>
        [ContextMenu("Verify Graph Integrity")]
        public void VerifyGraphIntegrity()
        {
            Log.Info("═══════════════════════════════════════════");
            Log.Info("[RailwayGraph] VERIFYING GRAPH INTEGRITY");
            Log.Info("═══════════════════════════════════════════");
            
            bool allOk = true;
            
            // Check 1: Segments exist
            if (segments.Count == 0)
            {
                Log.Error("❌ No segments in graph!");
                allOk = false;
            }
            else
            {
                Log.Info($"✅ {segments.Count} segments found");
            }
            
            // Check 2: Segment IDs are reasonable
            int invalidIds = 0;
            foreach (var seg in segments.Values)
            {
                if (seg.Id <= 0 || seg.Id > 1000000)
                {
                    invalidIds++;
                }
            }
            
            if (invalidIds > 0)
            {
                Log.Warn($"⚠️ {invalidIds} segments have suspicious IDs");
                allOk = false;
            }
            else
            {
                Log.Info($"✅ All segment IDs are reasonable");
            }
            
            // Check 3: Junctions
            if (junctions.Count == 0)
            {
                Log.Warn("⚠️ No junctions found (unusual but possible)");
            }
            else
            {
                Log.Info($"✅ {junctions.Count} junctions found");
            }
            
            // Check 4: Junction connectivity
            int disconnectedJunctions = 0;
            foreach (var junction in junctions)
            {
                if (junction.ConnectedSegments.Count == 0)
                    disconnectedJunctions++;
            }
            
            if (disconnectedJunctions > 0)
            {
                Log.Warn($"⚠️ {disconnectedJunctions}/{junctions.Count} junctions are disconnected");
                Log.Warn($"⚠️ Try increasing junctionTolerance (current: {junctionTolerance}m)");
            }
            else
            {
                Log.Info($"✅ All junctions are connected");
            }
            
            // Check 5: Segment lengths
            int zeroLengthSegments = 0;
            foreach (var seg in segments.Values)
            {
                if (seg.Length < 0.01f)
                {
                    zeroLengthSegments++;
                }
            }
            
            if (zeroLengthSegments > 0)
            {
                Log.Warn($"⚠️ {zeroLengthSegments} segments have near-zero length");
            }
            else
            {
                Log.Info($"✅ All segments have reasonable lengths");
            }
            
            Log.Info("═══════════════════════════════════════════");
            if (allOk && disconnectedJunctions == 0)
            {
                Log.Info("✅ GRAPH INTEGRITY: PERFECT");
            }
            else if (allOk)
            {
                Log.Warn("⚠️ GRAPH INTEGRITY: OK (with warnings)");
            }
            else
            {
                Log.Error("❌ GRAPH INTEGRITY: ISSUES DETECTED");
                Log.Error("This may indicate incorrect reading of SegmentIds/JunctionIndices.");
                Log.Error("Verify that MeshGeometry.Read() includes HoleStarts!");
            }
            Log.Info("═══════════════════════════════════════════");
        }
        
        /// <summary>
        /// Draws debug gizmos for the railway graph.
        /// Pełna Polska ma ~100k segmentów + ~50k junction — naiwny render zabija editor.
        /// Frustum clip + hard cap żeby dev mógł włączyć showDebugInfo bez crash'a.
        /// </summary>
        void OnDrawGizmos()
        {
            if (!showDebugInfo)
                return;

            // Hard cap niezależny od frustum clip — fallback gdy kamera non-ortho / null.
            const int MaxGizmosToDraw = 5000;

            // Frustum clip (orthographic camera = mapa). Bez tego rysujemy całość Polski.
            Camera cam = Camera.current;
            bool useFrustumClip = cam != null && cam.orthographic;
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
            if (useFrustumClip)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                Vector3 p = cam.transform.position;
                minX = p.x - halfW; maxX = p.x + halfW;
                minZ = p.z - halfH; maxZ = p.z + halfH;
            }

            // Draw segments — clip out-of-view + cap.
            Gizmos.color = Color.yellow;
            int drawnSegments = 0;
            foreach (var segment in segments.Values)
            {
                if (drawnSegments >= MaxGizmosToDraw) break;
                if (useFrustumClip)
                {
                    bool startIn = segment.Start.x >= minX && segment.Start.x <= maxX
                                && segment.Start.y >= minZ && segment.Start.y <= maxZ;
                    bool endIn = segment.End.x >= minX && segment.End.x <= maxX
                              && segment.End.y >= minZ && segment.End.y <= maxZ;
                    if (!startIn && !endIn) continue;
                }
                Vector3 start = new Vector3(segment.Start.x, 0.5f, segment.Start.y);
                Vector3 end = new Vector3(segment.End.x, 0.5f, segment.End.y);
                Gizmos.DrawLine(start, end);
                drawnSegments++;
            }

            // Draw junctions with different colors based on connectivity — clip out-of-view + cap.
            int drawnJunctions = 0;
            foreach (var junction in junctions)
            {
                if (drawnJunctions >= MaxGizmosToDraw) break;
                if (useFrustumClip)
                {
                    if (junction.Position.x < minX || junction.Position.x > maxX
                     || junction.Position.y < minZ || junction.Position.y > maxZ) continue;
                }
                Vector3 pos = new Vector3(junction.Position.x, 0.5f, junction.Position.y);

                if (junction.ConnectedSegments.Count == 0)
                    Gizmos.color = Color.red;       // Disconnected
                else if (junction.ConnectedSegments.Count == 2)
                    Gizmos.color = Color.green;     // Simple
                else if (junction.ConnectedSegments.Count == 3)
                    Gizmos.color = Color.cyan;      // Switch
                else
                    Gizmos.color = Color.magenta;   // Complex

                Gizmos.DrawSphere(pos, 10f); // 10 meters radius
                drawnJunctions++;
            }
        }
    }
    
    /// <summary>
    /// Represents a single railway track segment
    /// </summary>
    [System.Serializable]
    public class RailwaySegment
    {
        public int Id;
        public Vector2 Start;
        public Vector2 End;
        public Dictionary<string, string> Metadata;
        
        public float Length => Vector2.Distance(Start, End);
        
        public Vector2 Direction => (End - Start).normalized;
        
        public Vector2 GetPointAt(float t)
        {
            return Vector2.Lerp(Start, End, Mathf.Clamp01(t));
        }
    }
    
    /// <summary>
    /// Represents a railway junction (where tracks meet)
    /// </summary>
    [System.Serializable]
    public class RailwayJunction
    {
        public Vector2 Position;
        public List<int> ConnectedSegments;
        
        public bool IsSimple => ConnectedSegments.Count == 2;
        public bool IsSwitch => ConnectedSegments.Count == 3;
        public bool IsComplex => ConnectedSegments.Count >= 4;
        public bool IsDisconnected => ConnectedSegments.Count == 0;
    }
}