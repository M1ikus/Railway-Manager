using System;
using System.Collections.Generic;
using System.IO;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-6: Katalog presetów stripe'ów dla paint editor (M-FC-7).
    /// Ładowany z <c>StreamingAssets/Fleet/paint_presets.json</c>.
    /// </summary>
    public static class PaintPresetsCatalog
    {
        public static List<PaintPresetDef> Presets { get; private set; } = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class Wrapper { public List<PaintPresetDef> presets = new(); }

        public static void Load()
        {
            if (IsLoaded) return;

            string path = Path.Combine(AppPaths.FleetCatalogDir, "paint_presets.json");
            if (!File.Exists(path))
            {
                Log.Warn($"[PaintPresetsCatalog] Not found: {path}");
                IsLoaded = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var wrapper = JsonUtility.FromJson<Wrapper>(json);
                Presets = wrapper?.presets ?? new List<PaintPresetDef>();
                IsLoaded = true;
                Log.Info($"[PaintPresetsCatalog] Loaded {Presets.Count} stripe presets");
            }
            catch (Exception e)
            {
                Log.Error($"[PaintPresetsCatalog] Load failed: {e.Message}");
                Presets = new List<PaintPresetDef>();
                IsLoaded = true;
            }
        }

        public static PaintPresetDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in Presets) if (p.id == id) return p;
            return null;
        }

        /// <summary>Konwertuje preset (single lub compound) na listę StripeLayer gotowych do dodania do SegmentPaint.</summary>
        public static List<StripeLayer> Apply(string presetId, string colorOverride = null)
        {
            var preset = Find(presetId);
            if (preset == null) return new List<StripeLayer>();

            var result = new List<StripeLayer>();
            if (preset.isCompound && preset.compoundParts != null)
            {
                foreach (var part in preset.compoundParts)
                    result.Add(new StripeLayer
                    {
                        presetId = preset.id,
                        positionY = part.positionY,
                        thickness = part.thickness,
                        color = string.IsNullOrEmpty(colorOverride) ? part.defaultColor : colorOverride,
                        mode = part.mode
                    });
            }
            else
            {
                result.Add(new StripeLayer
                {
                    presetId = preset.id,
                    positionY = preset.positionY,
                    thickness = preset.thickness,
                    color = string.IsNullOrEmpty(colorOverride) ? preset.defaultColor : colorOverride,
                    mode = preset.mode
                });
            }
            return result;
        }
    }
}
