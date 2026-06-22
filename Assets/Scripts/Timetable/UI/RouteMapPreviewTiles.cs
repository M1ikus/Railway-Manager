using System.Collections.Generic;
using UnityEngine;
using MapSystem;
using formap;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// Renderuje WŁASNE kafle OSM dla mini-mapy podglądu (RouteMapPreview) we WŁASNYM LOD,
    /// niezależnie od głównej mapy. Buduje meshe warstw tła (Water/Forests/Waterways/Highways/
    /// Railways) na warstwie MapPreview (29), którą widzi tylko kamera podglądu. LOD liczony
    /// z WŁASNEGO zoomu podglądu (<see cref="MapLod"/>), więc przy oddaleniu używa grubych,
    /// tanich kafli zamiast pełnego detalu globalnego LOD.
    ///
    /// Reuse: <see cref="MapLoader.ParseTileRenderLayers"/> (parse per LOD, bez side-effectów) +
    /// <see cref="MapMeshBuilder.BuildMesh"/> (czysty mesh) + gettery materiałów/wysokości z MapRenderer.
    /// Budowa batchowana (I/O per kafel) — sterowana z RouteMapPreview przez <see cref="BuildStep"/>.
    /// </summary>
    public class RouteMapPreviewTiles
    {
        // Warstwy w kolejności rysowania = render queue głównej mapy (fille pod spodem, linie/koleje
        // na wierzchu). TD-041: rozszerzone z 5 do 9 (dodane Industrial/Military/Buildings/Platforms),
        // żeby podgląd pokazywał pełniejszy obraz (nie tylko tory/woda/drogi). POIs/Places (punkty,
        // markery) i AdminBoundaries pominięte — nie pasują do mesh-only podglądu geometrii.
        private static readonly BinaryFormat.LayerType[] PreviewLayers =
        {
            BinaryFormat.LayerType.Water,
            BinaryFormat.LayerType.Forests,
            BinaryFormat.LayerType.Industrial,
            BinaryFormat.LayerType.Military,
            BinaryFormat.LayerType.Buildings,
            BinaryFormat.LayerType.Platforms,
            BinaryFormat.LayerType.Waterways,
            BinaryFormat.LayerType.Highways,
            BinaryFormat.LayerType.Railways,
        };

        private struct Built { public GameObject go; public int lod; }

        private readonly GameObject _root;
        private readonly int _layer;
        private readonly MapLoader _loader;
        private readonly MapRenderer _renderer;

        private readonly Dictionary<long, Built> _built = new();
        private readonly Queue<long> _toBuild = new();
        private int _wantedLod;

        public bool Available => _loader != null && _renderer != null;
        public int PendingCount => _toBuild.Count;

        public RouteMapPreviewTiles(Transform parent, int layer)
        {
            _layer = layer;
            _root = new GameObject("RouteMapPreviewTiles");
            _root.transform.SetParent(parent, false);
            _loader = Object.FindAnyObjectByType<MapLoader>();
            _renderer = Object.FindAnyObjectByType<MapRenderer>();
        }

        /// <summary>Ustaw zbiór kafli widoku + LOD. Usuwa zbędne/wrong-LOD, kolejkuje brakujące.</summary>
        public void RequestViewport(IReadOnlyList<long> tileIds, int lod)
        {
            if (_loader == null || _renderer == null || tileIds == null) return;
            _wantedLod = lod;

            var wanted = new HashSet<long>(tileIds);

            // Usuń zbudowane spoza viewportu albo w innym LOD (rebuild).
            List<long> toRemove = null;
            foreach (var kv in _built)
                if (!wanted.Contains(kv.Key) || kv.Value.lod != lod)
                    (toRemove ??= new List<long>()).Add(kv.Key);
            if (toRemove != null)
                foreach (var id in toRemove)
                {
                    DestroyTileGo(_built[id].go);
                    _built.Remove(id);
                }

            // Kolejka: chciane a niezbudowane.
            _toBuild.Clear();
            for (int i = 0; i < tileIds.Count; i++)
                if (!_built.ContainsKey(tileIds[i]))
                    _toBuild.Enqueue(tileIds[i]);
        }

        /// <summary>Zbuduj do <paramref name="maxTiles"/> kafli z kolejki. Zwraca liczbę pozostałych.</summary>
        public int BuildStep(int maxTiles)
        {
            if (_loader == null || _renderer == null) return 0;
            int n = 0;
            while (n < maxTiles && _toBuild.Count > 0)
            {
                long id = _toBuild.Dequeue();
                if (_built.ContainsKey(id)) continue;
                BuildTile(id, _wantedLod);
                n++;
            }
            return _toBuild.Count;
        }

        private void BuildTile(long tileID, int lod)
        {
            var tileGo = new GameObject($"PreviewTile_{tileID}");
            tileGo.layer = _layer;
            tileGo.transform.SetParent(_root.transform, false);

            var layers = _loader.ParseTileRenderLayers(tileID, lod);
            if (layers != null)
            {
                foreach (var lt in PreviewLayers)
                {
                    if (!layers.TryGetValue(lt, out var feats) || feats == null || feats.Count == 0) continue;
                    var mat = _renderer.GetMaterialForLayer(lt);
                    if (mat == null) continue; // brak materiału → pomiń (zamiast magenty)
                    bool isLine = _renderer.IsLineLayer(lt);
                    // Mirror MapRenderer: reverseWinding dla fillów (wszystko poza liniami + Highways/Waterways),
                    // inaczej fille nawinięte odwrotnie niż główna mapa → znikają przy single-sided materiale.
                    bool reverseWinding = !isLine
                        && lt != BinaryFormat.LayerType.Highways
                        && lt != BinaryFormat.LayerType.Waterways;
                    var mesh = MapMeshBuilder.BuildMesh(feats, _renderer.GetLayerHeight(lt), isLine, reverseWinding);
                    if (mesh == null) continue;

                    var go = new GameObject(lt.ToString());
                    go.layer = _layer;
                    go.transform.SetParent(tileGo.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }

            _built[tileID] = new Built { go = tileGo, lod = lod };
        }

        public void Clear()
        {
            foreach (var kv in _built) DestroyTileGo(kv.Value.go);
            _built.Clear();
            _toBuild.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (_root != null) Object.Destroy(_root);
        }

        // Mesh nie jest GC'owany — przy niszczeniu kafla trzeba zniszczyć też meshe (inaczej leak).
        private static void DestroyTileGo(GameObject go)
        {
            if (go == null) return;
            var filters = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i].sharedMesh != null) Object.Destroy(filters[i].sharedMesh);
            Object.Destroy(go);
        }
    }
}
