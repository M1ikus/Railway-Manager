using System;
using System.Collections.Generic;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Parametry generative schematu (Ladder/Throat/Scissors). Deserializowalne przez JsonUtility.
    ///
    /// Format JSON wspiera dwie formy:
    /// - <b>Shorthand</b>: <c>trackSpacing: 5.0, turnoutType: "R190"</c> — wszystkie międzytorza i
    ///   typy rozjazdów takie same. Używane w built-in i prostych user save.
    /// - <b>Per-element</b>: <c>trackSpacings: [5.0, 5.0, 6.0], turnoutTypes: ["R190", "R190", "R300"]</c>
    ///   — różne wartości per pair / per rozjazd. Używane w advanced mode.
    ///
    /// Po deserializacji wywołaj <see cref="Normalize"/> żeby expand'ować shorthand do array form.
    /// Wewnętrznie zawsze pracujemy na array — to upraszcza generators.
    /// </summary>
    [Serializable]
    public class SchemaParameters
    {
        // ── Liczba torów ──────────────────────────────────
        public int trackCount = 4;

        // ── Międzytorza (jeden z dwóch — patrz Normalize) ─
        public float trackSpacing = 0f;        // shorthand (== 0 oznacza "użyj trackSpacings")
        public float[] trackSpacings;          // per-pair (długość = trackCount-1)

        // ── Typy rozjazdów (jeden z dwóch — patrz Normalize) ─
        public string turnoutType = "";        // shorthand ("" oznacza "użyj turnoutTypes")
        public string[] turnoutTypes;          // per-rozjazd (długość = trackCount-1 dla Ladder/Throat)

        // ── Mirror ────────────────────────────────────────
        public bool mirror = false;

        /// <summary>
        /// Domyślne międzytorze. Spec decyzja: 5.0m sztywno na EA. Clamp 4.0-6.0m.
        /// </summary>
        public const float DefaultSpacing = 5.0f;
        public const float MinSpacing = 4.0f;
        public const float MaxSpacing = 6.0f;

        /// <summary>
        /// Expanduje shorthand do array form. Po wywołaniu zawsze:
        /// - <see cref="trackSpacings"/> ma długość <see cref="trackCount"/>-1 (lub 1 dla Scissors)
        /// - <see cref="turnoutTypes"/> ma tę samą długość
        ///
        /// <paramref name="expectedTurnoutCount"/> = ile rozjazdów ma być w schemacie
        /// (Ladder/Throat = trackCount-1, Scissors = 2).
        /// </summary>
        public void Normalize(int expectedTurnoutCount)
        {
            // Spacings: jeśli shorthand > 0, expand do array. Jeśli array istnieje i pasuje, zostaw.
            int expectedSpacingsCount = System.Math.Max(1, expectedTurnoutCount); // min 1 (np. Scissors)
            if (trackSpacings == null || trackSpacings.Length == 0)
            {
                float val = trackSpacing > 0f ? trackSpacing : DefaultSpacing;
                trackSpacings = new float[expectedSpacingsCount];
                for (int i = 0; i < expectedSpacingsCount; i++) trackSpacings[i] = val;
            }
            else if (trackSpacings.Length < expectedSpacingsCount)
            {
                // Pad — jeśli array krótszy niż trzeba, dopełnij ostatnią wartością
                float fill = trackSpacings[trackSpacings.Length - 1];
                var padded = new float[expectedSpacingsCount];
                for (int i = 0; i < expectedSpacingsCount; i++)
                    padded[i] = i < trackSpacings.Length ? trackSpacings[i] : fill;
                trackSpacings = padded;
            }

            // Clamp spacings
            for (int i = 0; i < trackSpacings.Length; i++)
                trackSpacings[i] = ClampSpacing(trackSpacings[i]);

            // TurnoutTypes: analogicznie
            if (turnoutTypes == null || turnoutTypes.Length == 0)
            {
                string val = !string.IsNullOrEmpty(turnoutType) ? turnoutType : SchemaTurnoutType.R190;
                turnoutTypes = new string[expectedTurnoutCount];
                for (int i = 0; i < expectedTurnoutCount; i++) turnoutTypes[i] = val;
            }
            else if (turnoutTypes.Length < expectedTurnoutCount)
            {
                string fill = turnoutTypes[turnoutTypes.Length - 1];
                var padded = new string[expectedTurnoutCount];
                for (int i = 0; i < expectedTurnoutCount; i++)
                    padded[i] = i < turnoutTypes.Length ? turnoutTypes[i] : fill;
                turnoutTypes = padded;
            }
        }

        /// <summary>Clamp międzytorza do dozwolonego zakresu (4.0-6.0m).</summary>
        public static float ClampSpacing(float value)
        {
            if (value < MinSpacing) return MinSpacing;
            if (value > MaxSpacing) return MaxSpacing;
            return value;
        }

        /// <summary>
        /// Zwraca i-te międzytorze (po normalizacji). i = 0..N-2 dla Ladder/Throat.
        /// </summary>
        public float GetSpacingAt(int index)
        {
            if (trackSpacings == null || trackSpacings.Length == 0) return DefaultSpacing;
            if (index < 0) return trackSpacings[0];
            if (index >= trackSpacings.Length) return trackSpacings[trackSpacings.Length - 1];
            return trackSpacings[index];
        }

        /// <summary>
        /// Zwraca i-ty typ rozjazdu (po normalizacji). i = 0..N-2 dla Ladder/Throat.
        /// </summary>
        public string GetTurnoutTypeAt(int index)
        {
            if (turnoutTypes == null || turnoutTypes.Length == 0) return SchemaTurnoutType.R190;
            if (index < 0) return turnoutTypes[0];
            if (index >= turnoutTypes.Length) return turnoutTypes[turnoutTypes.Length - 1];
            return turnoutTypes[index];
        }

        /// <summary>
        /// Cumulative spacing — odległość Y od toru przewodniego (i=0) do i-tego toru postojowego.
        /// Dla i=1: trackSpacings[0]. Dla i=2: trackSpacings[0]+trackSpacings[1]. Itd.
        /// </summary>
        public float GetCumulativeOffsetTo(int trackIndex)
        {
            if (trackIndex <= 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < trackIndex && i < trackSpacings.Length; i++)
                sum += trackSpacings[i];
            return sum;
        }
    }
}
