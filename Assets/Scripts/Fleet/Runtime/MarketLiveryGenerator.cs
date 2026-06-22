using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-8: Deterministic generator livery dla pojazdów z rynku wtórnego.
    /// Z paintSeed → identyczny PaintDefinition (bottom color + 0-2 stripes
    /// + 1-2 decals). Generic — bez nazw fikcyjnych przewoźników (E4 z spec'a).
    ///
    /// Używana w 2 miejscach:
    /// - FleetMarketVehicle.GetOrResolvePaint (lazy resolve)
    /// - MarketGenerator.PerturbClone (przy każdym refresh ustawiany seed)
    /// </summary>
    public static class MarketLiveryGenerator
    {
        /// <summary>
        /// Paleta typowych kolorów kolejowych w PL — z minimalnym hue offset
        /// dla diversity. Kolory bazowe (jasne pudła) + akcent (paski).
        /// </summary>
        private static readonly string[] BaseColors = new[]
        {
            "#FAFAFA", // biały
            "#E8E8E8", // jasny szary
            "#103090", // PKP IC granat
            "#DC0000", // PKP IC red
            "#FFCC00", // Mazowieckie żółty
            "#008844", // Koleje Śląskie zielony
            "#FF6600", // ŁKA pomarańczowy
            "#003366", // ciemny granat
            "#7A1F2D", // bordowy
            "#D5D5D5"  // szary plat
        };

        /// <summary>Akcent / kolor pasków (kontrastujący z bottom).</summary>
        private static readonly string[] AccentColors = new[]
        {
            "#FFFFFF", "#000000", "#DC0000", "#103090", "#FFCC00",
            "#008844", "#FF6600", "#1A1A4A", "#7A1F2D"
        };

        /// <summary>Info icons które mogą się pojawić jako "fabryczne dekale" (1 random).</summary>
        private static readonly string[] CommonInfoDecals = new[]
        {
            "icon-wheelchair", "icon-bicycle", "icon-wifi",
            "icon-toilet", "icon-first-class", "icon-quiet"
        };

        /// <summary>
        /// Generuje deterministic PaintDefinition dla pojazdu z rynku wtórnego.
        /// Identyczny seed → identyczny output.
        /// </summary>
        public static PaintDefinition Generate(int seed, FleetVehicleType type, int segmentCount)
        {
            var rng = new System.Random(seed);
            int segs = Mathf.Max(1, segmentCount);

            string baseColor = BaseColors[rng.Next(BaseColors.Length)];

            // Decyzja: ile pasków (0-2)
            int stripeCount = rng.Next(0, 3); // 0, 1, lub 2
            var stripes = new List<StripeLayer>();
            for (int i = 0; i < stripeCount; i++)
            {
                string accent = AccentColors[rng.Next(AccentColors.Length)];
                // Avoid same color as bottom (małe prawdopodobieństwo, ale safety)
                if (accent.Equals(baseColor, System.StringComparison.OrdinalIgnoreCase))
                    accent = AccentColors[(rng.Next(AccentColors.Length) + 1) % AccentColors.Length];

                // Random preset — albo z palety stałych pozycji, albo dynamiczny
                float positionY = i == 0 ? 0.5f : (i == 1 ? 0.85f : 0.15f); // środek / dół / góra
                float thickness = 0.02f + (float)rng.NextDouble() * 0.10f;  // 0.02-0.12

                stripes.Add(new StripeLayer
                {
                    presetId = "generated",
                    positionY = positionY,
                    thickness = thickness,
                    color = accent,
                    mode = StripeMode.Solid
                });
            }

            // Decals: numer pojazdu (jakaś losowa cyfra) + opcjonalnie info icon
            var decals = new List<DecalLayer>();
            int randNumber = rng.Next(1, 99);
            string digitId = $"digit-{randNumber % 10}";
            decals.Add(new DecalLayer
            {
                symbolId = digitId,
                positionX = 0.1f,
                positionY = 0.5f,
                scale = 0.8f,
                rotation = 0f,
                color = ContrastWith(baseColor)
            });

            // Pasażerskie pojazdy (EZT, DMU, wagon) mogą dostać info icon (50% szans)
            if (type != FleetVehicleType.ElectricLocomotive && type != FleetVehicleType.DieselLocomotive
                && rng.Next(2) == 0)
            {
                string infoId = CommonInfoDecals[rng.Next(CommonInfoDecals.Length)];
                decals.Add(new DecalLayer
                {
                    symbolId = infoId,
                    positionX = 0.85f,
                    positionY = 0.6f,
                    scale = 0.6f,
                    rotation = 0f,
                    color = ContrastWith(baseColor)
                });
            }

            // Build PaintDefinition (AllSegments mode — generic livery jednolity dla całego pojazdu)
            var def = new PaintDefinition
            {
                schemaVersion = PaintSerializer.CURRENT_SCHEMA_VERSION,
                applyMode = "AllSegments"
            };

            for (int i = 0; i < segs; i++)
            {
                var segPaint = new SegmentPaint
                {
                    segmentIndex = i,
                    baseColor = baseColor
                };
                // Stripes/decals są takie same dla każdego segmentu (AllSegments)
                foreach (var s in stripes)
                    segPaint.stripes.Add(new StripeLayer
                    {
                        presetId = s.presetId,
                        positionY = s.positionY,
                        thickness = s.thickness,
                        color = s.color,
                        mode = s.mode
                    });
                foreach (var d in decals)
                    segPaint.decals.Add(new DecalLayer
                    {
                        symbolId = d.symbolId,
                        positionX = d.positionX,
                        positionY = d.positionY,
                        scale = d.scale,
                        rotation = d.rotation,
                        color = d.color,
                        customText = d.customText
                    });
                def.segments.Add(segPaint);
            }

            return def;
        }

        /// <summary>Zwraca biały lub czarny w zależności od jasności tła (kontrast).</summary>
        private static string ContrastWith(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) return "#000000";
            float brightness = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return brightness > 0.5f ? "#1A1A1A" : "#FAFAFA";
        }

        /// <summary>
        /// Helper: dla seed=0 wygeneruj deterministic non-zero seed z fields pojazdu.
        /// Używane gdy initial_market.json nie ma paintSeed.
        /// </summary>
        public static int FallbackSeedForVehicle(FleetMarketVehicle mv)
        {
            if (mv == null) return 12345;
            int hash = mv.id;
            if (!string.IsNullOrEmpty(mv.seriesId)) hash = hash * 31 + mv.seriesId.GetHashCode();
            if (!string.IsNullOrEmpty(mv.number)) hash = hash * 31 + mv.number.GetHashCode();
            return hash != 0 ? hash : 12345;
        }
    }
}
