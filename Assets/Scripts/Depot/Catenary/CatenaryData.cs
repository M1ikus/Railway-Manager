using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    // =================================================================
    //  SYSTEM SIECI TRAKCYJNEJ — struktury danych
    //  Pipeline: ZoneClassifier → WirePathGenerator → SupportOptimizer
    // =================================================================

    /// <summary>Typ konstrukcji wsporczej.</summary>
    public enum SupportType { Pole, Gantry }

    /// <summary>Typ strefy trakcyjnej.</summary>
    public enum ZoneType
    {
        Straight,         // prosty tor lub grupa prostych
        Curve,            // łuk (gęstsze podpory, odsuw przewodu)
        SwitchHead,       // głowica rozjazdowa (grupa rozjazdów)
        ParallelStation,  // tory równoległe stacyjne
        SpecialArea       // miejsca specjalne (zakończenie, wiadukt itp.)
    }

    // =================================================================
    //  ETAP 1: STREFY (ZoneClassifier)
    // =================================================================

    /// <summary>
    /// Strefa trakcyjna — obszar układu torowego z jednorodną logiką generacji sieci.
    /// </summary>
    public class CatenaryZone
    {
        public int ZoneId;
        public ZoneType Type;
        public List<int> TrackIds = new();                // tory w tej strefie
        public List<int> EdgeIds = new();                 // krawędzie grafu w tej strefie
        public List<int> TurnoutEntityIds = new();        // rozjazdy (dla SwitchHead)
        public float RecommendedSpacing;                  // najciaśniejszy spacing w strefie
        public Bounds BoundingBox;                        // AABB dla szybkich zapytań przestrzennych

        /// <summary>Cache polyline per tor, przycięte do strefy.</summary>
        public Dictionary<int, List<Vector3>> TrackPolylines = new();

        /// <summary>Odcinki torów w strefie: (trackId, startDist, endDist).</summary>
        public List<(int trackId, float startDist, float endDist)> TrackSegments = new();
    }

    // =================================================================
    //  ETAP 2: LINIE PRZEWODÓW (WirePathGenerator)
    // =================================================================

    /// <summary>
    /// Punkt kontrolny linii przewodu — pozycja nad torem z parametrami zawieszenia.
    /// </summary>
    public class WireControlPoint
    {
        public int WirePathId;
        public int TrackId;                   // tor do którego należy punkt
        public float DistAlongTrack;          // pozycja wzdłuż polyline toru
        public Vector3 Position;              // pozycja świata XZ (Y=0)
        public Vector3 Tangent;               // kierunek toru
        public float ZigzagOffset;            // boczne odsunięcie od osi toru (m, ze znakiem)
        public float ContactWireHeight = 5.5f;// wysokość przewodu jezdnego
        public float LocalRadius;             // promień krzywizny
        public bool IsSupportCandidate;       // czy tu może stanąć podpora
    }

    /// <summary>
    /// Logiczna linia przewodu — ciąg punktów kontrolnych nad jednym torem/relacją.
    /// </summary>
    public class WirePath
    {
        public int WirePathId;
        public int TrackId;                                // główny tor
        public List<WireControlPoint> ControlPoints = new();
        public int? SourceZoneId;                          // strefa generująca
        public List<int> ConnectedWirePathIds = new();     // połączenia (rozgałęzienia)
    }

    // =================================================================
    //  ETAP 3: PODPORY (SupportOptimizer)
    // =================================================================

    /// <summary>
    /// Kandydat na konstrukcję wsporczą — generowany w fazie optymalizacji.
    /// </summary>
    public class SupportCandidate
    {
        public Vector3 Position;
        public Vector3 Tangent;
        /// <summary>Punkty które ten kandydat obsługuje: (wirePathId, indeks w ControlPoints).</summary>
        public List<(int wirePathId, int controlPointIndex)> ServedPoints = new();
        public float Cost = 1f;               // waga optymalizacyjna (niżej = lepiej)
        public SupportType PreferredType;
    }

    /// <summary>
    /// Punkt podwieszenia sieci na jednym torze — miejsce gdzie konstrukcja wsporcza
    /// trzyma przewód nad torem.
    /// </summary>
    public class SupportPoint
    {
        public int TrackId;
        public float DistAlongTrack;
        public Vector3 Position;              // pozycja XZ na torze (Y=0)
        public Vector3 Tangent;               // kierunek toru w tym punkcie
        public float LocalRadius;             // promień krzywizny (float.MaxValue = prosta)
        public bool IsLongSide;               // true = wysięgnik długi (zygzak)

        /// <summary>Bramka/słup do którego należy ten punkt.</summary>
        public SupportStructure Support;

        /// <summary>Pozycja podwieszenia drutu (rzut na belkę bramki). Domyślnie = Position.</summary>
        public Vector3 AttachPosition;

        /// <summary>Link do logicznego punktu kontrolnego (opcjonalny).</summary>
        public int WireControlPointIndex = -1;
    }

    /// <summary>
    /// Konstrukcja wsporcza — słup z wysięgnikiem lub bramka.
    /// </summary>
    public class SupportStructure
    {
        public SupportType Type;
        public Vector3 Position;              // pozycja na gruncie (słup) lub środek bramki
        public Vector3 Tangent;               // kierunek wzdłuż toru/osi

        // Geometria słupa
        public float PoleHeight = 8f;
        public float PoleOffset;
        public float PoleSide;                // +1 = prawo, -1 = lewo
        public float CantileverLength;
        public float CantileverAngle;
        public bool HasStayWire;

        // Geometria bramki
        public float GantryWidth;
        public Vector3 LeftLegPosition;
        public Vector3 RightLegPosition;

        /// <summary>Punkty podwieszenia na tej konstrukcji (1 per tor).</summary>
        public List<SupportPoint> Points = new();

        /// <summary>Visual GameObject.</summary>
        public GameObject Visual;
    }

    // =================================================================
    //  PRZESLA DRUTOWE
    // =================================================================

    /// <summary>
    /// Przęsło drutowe między dwoma kolejnymi punktami podwieszenia tego samego toru.
    /// </summary>
    public class WireSpan
    {
        public int TrackId;
        public SupportPoint From;
        public SupportPoint To;
        public float SpanLength;
        public GameObject Visual;
    }

    // =================================================================
    //  SIEC CALOSCIOWA
    // =================================================================

    /// <summary>
    /// Cała wygenerowana sieć trakcyjna.
    /// </summary>
    public class CatenaryNetwork
    {
        public List<CatenaryZone> Zones = new();
        public List<WirePath> WirePaths = new();
        public List<SupportStructure> Supports = new();
        public List<WireSpan> WireSpans = new();
        public GameObject RootGameObject;
    }
}
