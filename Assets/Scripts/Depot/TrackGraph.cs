using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Graf torów zajezdni z BFS pathfindingiem.
    /// Przechowuje węzły (punkty styku/rozjazdy), krawędzie (segmenty torów z polyline)
    /// i dane o torach (nazwy, typ, sieć trakcyjna, zajętość).
    ///
    /// Klasa rozbita na partial files per temat:
    /// - <c>TrackGraph.cs</c>             — pola, eventy, OnDrawGizmos + typy namespace (ten plik)
    /// - <c>TrackGraph.Nodes.cs</c>       — Add/Find/GetOrCreate/GetNearest, UpdateNodeType,
    ///                                      kolinearność, GetNodeDirection, Endpoint/Junction queries
    /// - <c>TrackGraph.Edges.cs</c>       — AddEdge/AddEdgeWithPolyline, FindEdgeBetween +
    ///                                      krzywizna (CurveData) + wstawki (InsertData) + DetectInserts
    /// - <c>TrackGraph.Tracks.cs</c>      — AddTrack/RemoveTrack, occupancy (legacy + per-consist M9b),
    ///                                      query methods (parking/byType/byNode), GetTrackPolyline
    /// - <c>TrackGraph.Pathfinding.cs</c> — BFS FindPath + FindPathRespectingBlades + PathToWorldPositions
    /// - <c>TrackGraph.Switches.cs</c>    — iglice (Set/Toggle/SetPosition + IsRouteAllowedByBlade)
    /// </summary>
    public partial class TrackGraph : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Promień snap - odległość w której node'y się łączą")]
        public float snapTolerance = 0.5f;

        private Dictionary<int, TrackNode> nodes = new();
        private Dictionary<int, TrackEdge> edges = new();
        private Dictionary<int, DepotTrackData> tracks = new();
        private int nextNodeId = 0;
        private int nextEdgeId = 0;
        private int nextTrackId = 0;

        public IReadOnlyDictionary<int, TrackNode> Nodes => nodes;
        public IReadOnlyDictionary<int, TrackEdge> Edges => edges;
        public IReadOnlyDictionary<int, DepotTrackData> Tracks => tracks;

        /// <summary>Event: wywołany po zmianie topologii (dodanie/usunięcie toru/rozjazdu)</summary>
        public event System.Action OnTopologyChanged;

        /// <summary>Event: wywołany po zmianie elektryfikacji toru</summary>
        public event System.Action OnCatenaryChanged;

        // ═══════════════════════════════════════════
        //  SAVE/LOAD API (zamiast reflection w DepotSavable)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Bulk replace nodes/edges/tracks z save'a. Public API zamiast reflection na
        /// private fieldy (`nodes`, `edges`, `tracks`, `nextNodeId`, `nextEdgeId`,
        /// `nextTrackId`) — rename pola łapany w compile zamiast silently no-op.
        ///
        /// Counter'y &lt;0 są klampowane do max(istniejące id)+1 (BUG-pattern z PersonnelService:
        /// counter zostaje 0/1 i nowe encje kolidują z restored).
        /// </summary>
        public void RestoreFromSave(IEnumerable<TrackNode> nodesIn,
                                    IEnumerable<TrackEdge> edgesIn,
                                    IEnumerable<DepotTrackData> tracksIn,
                                    int nextNodeIdIn, int nextEdgeIdIn, int nextTrackIdIn)
        {
            nodes.Clear();
            int maxNodeId = -1;
            if (nodesIn != null)
            {
                foreach (var n in nodesIn)
                {
                    if (n == null) continue;
                    nodes[n.Id] = n;
                    if (n.Id > maxNodeId) maxNodeId = n.Id;
                }
            }

            edges.Clear();
            int maxEdgeId = -1;
            if (edgesIn != null)
            {
                foreach (var e in edgesIn)
                {
                    if (e == null) continue;
                    edges[e.Id] = e;
                    if (e.Id > maxEdgeId) maxEdgeId = e.Id;
                }
            }

            tracks.Clear();
            int maxTrackId = -1;
            if (tracksIn != null)
            {
                foreach (var t in tracksIn)
                {
                    if (t == null) continue;
                    tracks[t.TrackId] = t;
                    if (t.TrackId > maxTrackId) maxTrackId = t.TrackId;
                }
            }

            // TD-031 backward-compat: stary save binarny (IsOccupied + OccupyingConsistId, brak Occupants)
            // → syntetyzuj footprint = cały tor (placement = środek, jak w modelu binarnym). Nowe save'y
            // mają Occupants → no-op. Pre-EA: bez bump SchemaVersion/migratora (zgodnie z SaveLoad/CLAUDE.md).
            foreach (var t in tracks.Values)
                TrackOccupancyMath.SynthesizeLegacyOccupant(t);

            nextNodeId = nextNodeIdIn > 0 ? nextNodeIdIn : maxNodeId + 1;
            nextEdgeId = nextEdgeIdIn > 0 ? nextEdgeIdIn : maxEdgeId + 1;
            nextTrackId = nextTrackIdIn > 0 ? nextTrackIdIn : maxTrackId + 1;

            OnTopologyChanged?.Invoke();
        }

        /// <summary>Reset graph (jak nowa gra / DepotSavable.InitializeDefault).</summary>
        public void ClearAllForReset()
        {
            nodes.Clear();
            edges.Clear();
            tracks.Clear();
            nextNodeId = 0;
            nextEdgeId = 0;
            nextTrackId = 0;
            OnTopologyChanged?.Invoke();
        }

        // ═══════════════════════════════════════════
        //  GIZMOS
        // ═══════════════════════════════════════════

        void OnDrawGizmos()
        {
            Color insertPairColor = new Color(0.5f, 1f, 0.5f);       // Jasnozielony - wstawka para
            Color insertFacingColor = new Color(0f, 1f, 0.6f);       // Morski - wstawka iglice naprzeciw
            Color turnoutStraightColor = new Color(1f, 0.6f, 0f);    // Pomarańczowy - prosta rozjazdu
            Color turnoutDivergingColor = new Color(1f, 0.3f, 0.3f); // Czerwony - odnoga rozjazdu
            Color curveColor = new Color(0.6f, 0.6f, 1f);            // Jasnoniebieski - łuk

            foreach (var edge in edges.Values)
            {
                // Priorytet kolorów: rozjazd > wstawka > łuk > sieć trakcyjna > domyślny
                if (edge.TurnoutPart == TurnoutPart.DivergingLeg)
                    Gizmos.color = turnoutDivergingColor;
                else if (edge.TurnoutPart == TurnoutPart.StraightLeg || edge.TurnoutPart == TurnoutPart.StraightLeg2)
                    Gizmos.color = turnoutStraightColor;
                else if (edge.Insert != null && edge.Insert.Type == InsertType.BetweenFacingBlades)
                    Gizmos.color = insertFacingColor;
                else if (edge.Insert != null && edge.Insert.Type == InsertType.BetweenPair)
                    Gizmos.color = insertPairColor;
                else if (edge.Curve != null && !edge.Curve.IsStraight)
                    Gizmos.color = curveColor;
                else if (edge.HasCatenary)
                    Gizmos.color = Color.yellow;
                else
                    Gizmos.color = Color.white;

                if (edge.Polyline != null && edge.Polyline.Count >= 2)
                {
                    for (int i = 1; i < edge.Polyline.Count; i++)
                    {
                        Vector3 from = edge.Polyline[i - 1] + Vector3.up * 0.2f;
                        Vector3 to = edge.Polyline[i] + Vector3.up * 0.2f;
                        Gizmos.DrawLine(from, to);
                    }
                }
                else if (nodes.ContainsKey(edge.FromNodeId) && nodes.ContainsKey(edge.ToNodeId))
                {
                    Vector3 from = nodes[edge.FromNodeId].Position + Vector3.up * 0.2f;
                    Vector3 to = nodes[edge.ToNodeId].Position + Vector3.up * 0.2f;
                    Gizmos.DrawLine(from, to);
                }
            }

            foreach (var node in nodes.Values)
            {
                // Iglica = magenta, Junction = żółty, Endpoint = cyan
                if (node.Blade != null)
                    Gizmos.color = Color.magenta;
                else
                    Gizmos.color = node.Type switch
                    {
                        NodeType.Junction => Color.yellow,
                        NodeType.Endpoint => Color.cyan,
                        _ => Color.white
                    };

                float size = node.Type == NodeType.Junction ? 0.5f : 0.3f;
                Gizmos.DrawSphere(node.Position + Vector3.up * 0.2f, size);

                // Rysuj kierunek iglicy (linia od node w kierunku aktywnej krawędzi)
                if (node.Blade != null)
                {
                    int activeEdge = node.Blade.ActiveEdgeId;
                    if (edges.ContainsKey(activeEdge))
                    {
                        var edge = edges[activeEdge];
                        int otherNode = edge.FromNodeId == node.Id ? edge.ToNodeId : edge.FromNodeId;
                        if (nodes.ContainsKey(otherNode))
                        {
                            Vector3 dir = (nodes[otherNode].Position - node.Position).normalized;
                            Gizmos.color = node.Blade.IsDiverging ? Color.red : Color.green;
                            Gizmos.DrawRay(node.Position + Vector3.up * 0.5f, dir * 2f);
                        }
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════
    //  DATA TYPES (poza klasą, w namespace)
    // ═══════════════════════════════════════════

    public enum NodeType
    {
        Endpoint,    // Koniec toru (1 krawędź)
        Throughput,  // Przejściowy (2 krawędzie, ciąg)
        Junction     // Rozjazd (3+ krawędzi)
    }

    /// <summary>Rola krawędzi w rozjeździe</summary>
    public enum TurnoutPart
    {
        None,           // Nie jest częścią rozjazdu
        StraightLeg,    // Prosta noga (prosta 1 w krzyżowym)
        StraightLeg2,   // Prosta 2 w krzyżowym (druga prosta przebiegająca na krzyż)
        DivergingLeg    // Odnoga (łuk odchodzący na bok)
    }

    /// <summary>Typ wstawki między rozjazdami</summary>
    public enum InsertType
    {
        None,               // Nie jest wstawką
        BetweenPair,        // Wstawka między parą rozjazdów (ten sam kierunek, np. odgałęzienie z toru głównego)
        BetweenFacingBlades // Wstawka między rozjazdami z iglicami zwróconymi do siebie
    }

    [System.Serializable]
    public class TrackNode
    {
        public int Id;
        public Vector3 Position;
        public List<int> EdgeIds;
        public NodeType Type;
        public Vector3 Direction;         // Kierunek toru w tym punkcie (tangenta)
        public int SwitchActiveEdgeId;    // Dla rozjazdów: aktywne ramię (-1 = brak)

        // === Iglica (blade) ===
        public SwitchBladeData Blade;     // Dane iglicy (null = brak iglicy, zwykły node)
    }

    /// <summary>
    /// Dane iglicy rozjazdu — definiuje, między którymi krawędziami iglica przełącza.
    /// Iglica siedzi na node Junction i decyduje, którą drogą pociąg jedzie.
    /// </summary>
    [System.Serializable]
    public class SwitchBladeData
    {
        public int TurnoutEntityId;       // Id powiązanego TurnoutEntity (-1 = brak)
        public int StraightEdgeId;        // Krawędź prostej drogi (domyślna)
        public int DivergingEdgeId;       // Krawędź odnogi (po przełożeniu)
        public bool IsDiverging;          // Aktualny stan: false = prosta, true = odnoga

        /// <summary>Zwraca id aktywnej krawędzi (tej, na którą iglica jest nastawiona)</summary>
        public int ActiveEdgeId => IsDiverging ? DivergingEdgeId : StraightEdgeId;

        /// <summary>Zwraca id zablokowanej krawędzi</summary>
        public int InactiveEdgeId => IsDiverging ? StraightEdgeId : DivergingEdgeId;
    }

    /// <summary>Dane krzywizny krawędzi (łuk lub prosta)</summary>
    [System.Serializable]
    public class CurveData
    {
        public float Radius;    // Promień łuku (m). 0 = prosta
        public float Angle;     // Kąt łuku (rad). 0 = prosta
        public float ArcLength; // Długość łuku (m). Dla prostej = Length krawędzi
        public bool IsLeftCurve; // Łuk w lewo (true) czy w prawo (false)

        public bool IsStraight => Radius <= 0f || Mathf.Abs(Angle) < 0.001f;
    }

    /// <summary>Dane wstawki między rozjazdami</summary>
    [System.Serializable]
    public class InsertData
    {
        public InsertType Type;
        public int TurnoutIdA;  // Id rozjazdu po stronie A (-1 = brak)
        public int TurnoutIdB;  // Id rozjazdu po stronie B (-1 = brak)
    }

    [System.Serializable]
    public class TrackEdge
    {
        public int Id;
        public int FromNodeId;
        public int ToNodeId;
        public float Length;
        public bool HasCatenary;
        public List<Vector3> Polyline;    // Punkty geometrii (krzywa lub prosta)

        // === Rozjazd ===
        public TurnoutPart TurnoutPart = TurnoutPart.None;
        public int TurnoutEntityId = -1;  // Id powiązanego TurnoutEntity (-1 = nie w rozjeździe)

        // === Krzywizna ===
        public CurveData Curve;           // Dane łuku (null = nieobliczone)

        // === Wstawka ===
        public InsertData Insert;         // Dane wstawki (null = zwykła krawędź)
    }

    [System.Serializable]
    public class DepotTrackData
    {
        public int TrackId;
        public string Name;
        public DepotTrackType TrackType;
        public List<int> EdgeIds;
        public bool HasCatenary;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Vector3 StartDirection;
        public Vector3 EndDirection;
        public float Length;
        public bool IsOccupied;
        public int OccupyingTrainId;       // LEGACY — zostaje dla kompatybilności ze starym kodem

        // M9b Etap 1: per-vehicle occupancy z consist grouping
        /// <summary>Lista pojazdów stojących na tym torze. Puste = tor wolny.</summary>
        public List<int> OccupyingVehicleIds = new List<int>();
        /// <summary>ID consist'u do którego pojazdy należą (atomiczny skład). -1 = brak.</summary>
        public int OccupyingConsistId = -1;

        /// <summary>
        /// M9b Etap 5: Tor permanentny (wygenerowany przez system, nie przez gracza).
        /// Tory zewnętrzne za bramą zajezdni — nie mogą być usunięte, modyfikowane,
        /// ani połączone z innymi w sposób niszczący. Gracz nie buduje tych torów.
        /// </summary>
        public bool IsPermanent;

        /// <summary>
        /// TD-031: Zajętość POZYCYJNA — lista footprintów [FrontDistM, RearDistM] per consist
        /// wzdłuż osi toru (track-local, metry [0, Length]). Źródło prawdy o tym kto i gdzie
        /// stoi/jedzie na torze. Pola IsOccupied / OccupyingConsistId / OccupyingVehicleIds /
        /// OccupyingTrainId to pochodny legacy-mirror (pierwszy occupant) utrzymywany przez API
        /// w <c>TrackGraph.Tracks.cs</c>. Nowy kod używa Occupants + metod interwałowych.
        /// </summary>
        public List<TrackOccupant> Occupants = new List<TrackOccupant>();
    }

    /// <summary>
    /// TD-031: jeden zajęty interwał (footprint) na osi toru — track-local metry [0, Length].
    /// Front ≤ Rear (dwa końce cielska). DirSign = orientacja nosa (+1 ku EndPosition,
    /// -1 ku StartPosition) — do rotacji wizualu i zapytań kierunkowych "co przede mną".
    /// </summary>
    [System.Serializable]
    public class TrackOccupant
    {
        public int ConsistId = -1;
        public List<int> VehicleIds = new List<int>();
        public float FrontDistM;
        public float RearDistM;
        public int DirSign = 1;

        public float LengthM => RearDistM - FrontDistM;
    }

    public enum DepotTrackType
    {
        Parking,    // Tor postojowy
        Entry,      // Tor wjazdowy
        Exit,       // Tor wyjazdowy
        Washing,    // Tor myjni
        Workshop,   // Tor warsztatowy
        Maneuver    // Tor manewrowy / łącznik
    }
}
