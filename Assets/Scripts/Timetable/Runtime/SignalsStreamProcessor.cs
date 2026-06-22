using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Streaming processor — akumuluje railway=signal POIs per tile, deduplikuje po pozycji.
    /// Snap do graph + parse signal function w <see cref="Finalize"/>.
    /// Refactor z <c>TimetableInitializer.LoadSignals</c> na incremental version.
    /// </summary>
    public class SignalsStreamProcessor
    {
        private readonly List<(Vector2 pos, SignalFunction func, SignalDirection dir, string refNum)> _raw = new();
        private readonly HashSet<string> _seenPositions = new();

        private int _total, _withFunction, _duplicates;

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.POIs, out var features)) return;

            foreach (var feature in features)
            {
                if (feature.Vertices == null || feature.Vertices.Count == 0) continue;
                if (!feature.Metadata.TryGetValue("railway", out var railway)) continue;
                if (railway != "signal") continue;

                _total++;

                var func = ParseSignalFunction(feature.Metadata);
                if (func == SignalFunction.Unknown) continue;
                _withFunction++;

                var pos = feature.Vertices[0];
                string posKey = $"{pos.x:F2}|{pos.y:F2}";
                if (!_seenPositions.Add(posKey))
                {
                    _duplicates++;
                    continue;
                }

                var dir = SignalDirection.Both;
                if (feature.Metadata.TryGetValue("railway:signal:direction", out var dirStr))
                {
                    if (dirStr == "forward") dir = SignalDirection.Forward;
                    else if (dirStr == "backward") dir = SignalDirection.Backward;
                }

                feature.Metadata.TryGetValue("ref", out var refNum);
                _raw.Add((pos, func, dir, refNum ?? ""));
            }
        }

        public List<SignalInfo> Finalize(PathfindingGraph graph)
        {
            var result = new List<SignalInfo>(_raw.Count);
            int entry = 0, exit = 0, block = 0, intermediate = 0;
            int snapped = 0;

            foreach (var (pos, func, dir, refNum) in _raw)
            {
                int nodeId = graph.FindNearestNode(pos, 10f);
                if (nodeId < 0) continue;

                switch (func)
                {
                    case SignalFunction.Entry: entry++; break;
                    case SignalFunction.Exit: exit++; break;
                    case SignalFunction.Block: block++; break;
                    case SignalFunction.Intermediate: intermediate++; break;
                }

                result.Add(new SignalInfo
                {
                    nodeId = nodeId,
                    function = func,
                    direction = dir,
                    refNum = refNum
                });
                snapped++;
            }

            Log.Info($"[SignalsStreamProcessor] {_total} total, {_withFunction} with function, "
                     + $"{_duplicates} duplicates skipped, {snapped} snapped — "
                     + $"entry={entry} exit={exit} block={block} intermediate={intermediate}");
            return result;
        }

        private static SignalFunction ParseSignalFunction(Dictionary<string, string> metadata)
        {
            // TYLKO semafory główne (main) i kombinowane (combined) dzielą bloki.
            string[] keys = {
                "railway:signal:main:function",
                "railway:signal:combined:function"
            };
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var func)) continue;
                switch (func)
                {
                    case "entry": return SignalFunction.Entry;
                    case "exit": return SignalFunction.Exit;
                    case "block": return SignalFunction.Block;
                    case "intermediate": return SignalFunction.Intermediate;
                }
            }
            return SignalFunction.Unknown;
        }
    }
}
