using System.Text;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;

namespace RailwayManager.GameCreator
{
    /// <summary>
    /// Smoke test regresji dla GameCreator (post-audit cleanup 2026-05-14).
    ///
    /// Sprawdza invariants:
    /// - Mapping helpers (Autosave/MaxPlayers/Preset dropdown ↔ value) — roundtrip + edge cases
    /// - <see cref="GameCreatorUI.FormatSeedFieldText"/> — 0 = pusty placeholder (UX), inne = liczba
    /// - <see cref="DifficultyPresetCatalog"/>.Get(Custom) = neutralne 1.0 (kontrakt z UI Custom quirk)
    /// - <see cref="DifficultyPresetCatalog"/>.Get(non-Custom) ≠ neutral (presety mają realne wartości)
    /// - <see cref="GameCreatorUI"/> preset loc/desc keys non-null dla wszystkich enum values
    /// - <see cref="SaveActionsHook"/>.IsRegistered po Bootstrap (ApplyOnStart depends on this)
    ///
    /// Każdy test ContextMenu jest niezależny — możesz uruchamiać selektywnie. Wszystkie
    /// piszą wynik przez <see cref="Log.Info"/> z prefiksem `[GameCreatorSmokeTest]` i markerem
    /// PASS/FAIL na końcu wiersza. Testy nie modyfikują state'u trwale.
    ///
    /// Konwencja projektu (CLAUDE.md): brak Unity Test Framework — smoke tests +
    /// `[ContextMenu]` ręczne uruchamianie w Editor. Wzór:
    /// <c>Assets/Scripts/Core/CoreSmokeTest.cs</c>, <c>Assets/Scripts/Depot/DepotSmokeTest.cs</c>.
    /// </summary>
    public class GameCreatorSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<GameCreatorSmokeTest>() != null) return;
            var go = new GameObject("GameCreatorSmokeTest (auto-spawn)");
            go.AddComponent<GameCreatorSmokeTest>();
        }

        [ContextMenu("GameCreator: Run ALL smoke tests")]
        public void RunAll()
        {
            TestAutosaveIntervalLookup();
            TestMaxPlayersLookupRoundtrip();
            TestPresetDropdownIndexRoundtrip();
            TestPresetLocKeysNonEmpty();
            TestDifficultyPresetCatalogCustomNeutral();
            TestDifficultyPresetCatalogNonCustomDiffers();
            TestSeedFieldFormatting();
            TestSaveActionsHookRegistration();
        }

        // ── Test 1: Autosave dropdown idx → minutes mapping ─────────────

        [ContextMenu("GameCreator: Test AutosaveDropdownIndexToMinutes")]
        public void TestAutosaveIntervalLookup()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] AutosaveDropdownIndexToMinutes:");

            bool t1a = GameCreatorUI.AutosaveDropdownIndexToMinutes(0) == 5;
            bool t1b = GameCreatorUI.AutosaveDropdownIndexToMinutes(1) == 10;
            bool t1c = GameCreatorUI.AutosaveDropdownIndexToMinutes(2) == 15;
            bool t1d = GameCreatorUI.AutosaveDropdownIndexToMinutes(3) == 30;
            bool t1e = GameCreatorUI.AutosaveDropdownIndexToMinutes(99) == 5; // default fallback
            bool t1f = GameCreatorUI.AutosaveDropdownIndexToMinutes(-1) == 5; // default fallback

            sb.AppendLine($"  1a) idx 0 → 5 min  " + (t1a ? "PASS" : "FAIL"));
            sb.AppendLine($"  1b) idx 1 → 10 min " + (t1b ? "PASS" : "FAIL"));
            sb.AppendLine($"  1c) idx 2 → 15 min " + (t1c ? "PASS" : "FAIL"));
            sb.AppendLine($"  1d) idx 3 → 30 min " + (t1d ? "PASS" : "FAIL"));
            sb.AppendLine($"  1e) idx 99 → 5 min (default) " + (t1e ? "PASS" : "FAIL"));
            sb.AppendLine($"  1f) idx -1 → 5 min (default) " + (t1f ? "PASS" : "FAIL"));

            bool all = t1a && t1b && t1c && t1d && t1e && t1f;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 2: MaxPlayers dropdown roundtrip ───────────────────────

        [ContextMenu("GameCreator: Test MaxPlayers lookup + roundtrip")]
        public void TestMaxPlayersLookupRoundtrip()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] MaxPlayers idx ↔ count:");

            // Forward mapping
            int[] expected = { 2, 4, 6, 8, 12, 16 };
            bool forwardOk = true;
            for (int i = 0; i < expected.Length; i++)
            {
                int actual = GameCreatorUI.MaxPlayersDropdownIndexToCount(i);
                if (actual != expected[i])
                {
                    sb.AppendLine($"  forward FAIL: idx {i} → expected {expected[i]}, got {actual}");
                    forwardOk = false;
                }
            }
            sb.AppendLine($"  2a) forward 0..5 → 2/4/6/8/12/16 " + (forwardOk ? "PASS" : "FAIL"));

            // Roundtrip: idx → count → idx
            bool roundtripOk = true;
            for (int i = 0; i < expected.Length; i++)
            {
                int count = GameCreatorUI.MaxPlayersDropdownIndexToCount(i);
                int back = GameCreatorUI.CountToMaxPlayersDropdownIndex(count);
                if (back != i)
                {
                    sb.AppendLine($"  roundtrip FAIL: idx {i} → count {count} → back {back}");
                    roundtripOk = false;
                }
            }
            sb.AppendLine($"  2b) roundtrip idx → count → idx " + (roundtripOk ? "PASS" : "FAIL"));

            // Default cases
            bool t2c = GameCreatorUI.MaxPlayersDropdownIndexToCount(99) == 4;
            bool t2d = GameCreatorUI.CountToMaxPlayersDropdownIndex(999) == 1;
            sb.AppendLine($"  2c) idx 99 → 4 (default) " + (t2c ? "PASS" : "FAIL"));
            sb.AppendLine($"  2d) count 999 → idx 1 (default) " + (t2d ? "PASS" : "FAIL"));

            bool all = forwardOk && roundtripOk && t2c && t2d;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 3: DifficultyPreset dropdown roundtrip ─────────────────

        [ContextMenu("GameCreator: Test DifficultyPreset dropdown roundtrip")]
        public void TestPresetDropdownIndexRoundtrip()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] DifficultyPreset idx ↔ enum:");

            DifficultyPreset[] expected =
            {
                DifficultyPreset.Easy,
                DifficultyPreset.Normal,
                DifficultyPreset.Hard,
                DifficultyPreset.Realistic,
                DifficultyPreset.Custom
            };

            bool forwardOk = true;
            for (int i = 0; i < expected.Length; i++)
            {
                var actual = GameCreatorUI.DropdownIndexToPreset(i);
                if (actual != expected[i])
                {
                    sb.AppendLine($"  forward FAIL: idx {i} → expected {expected[i]}, got {actual}");
                    forwardOk = false;
                }
            }
            sb.AppendLine($"  3a) idx 0..4 → Easy/Normal/Hard/Realistic/Custom " + (forwardOk ? "PASS" : "FAIL"));

            bool roundtripOk = true;
            for (int i = 0; i < expected.Length; i++)
            {
                var preset = GameCreatorUI.DropdownIndexToPreset(i);
                int back = GameCreatorUI.PresetToDropdownIndex(preset);
                if (back != i)
                {
                    sb.AppendLine($"  roundtrip FAIL: idx {i} → preset {preset} → back {back}");
                    roundtripOk = false;
                }
            }
            sb.AppendLine($"  3b) roundtrip idx → preset → idx " + (roundtripOk ? "PASS" : "FAIL"));

            // Default: out-of-range idx → Normal (idx 1)
            bool t3c = GameCreatorUI.DropdownIndexToPreset(99) == DifficultyPreset.Normal;
            sb.AppendLine($"  3c) idx 99 → Normal (default) " + (t3c ? "PASS" : "FAIL"));

            bool all = forwardOk && roundtripOk && t3c;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 4: Preset loc/desc keys non-empty ──────────────────────

        [ContextMenu("GameCreator: Test preset i18n keys non-empty")]
        public void TestPresetLocKeysNonEmpty()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] Preset loc/desc keys:");

            bool allOk = true;
            foreach (DifficultyPreset preset in System.Enum.GetValues(typeof(DifficultyPreset)))
            {
                string locKey  = GameCreatorUI.GetPresetLocKey(preset);
                string descKey = GameCreatorUI.GetPresetDescKey(preset);
                bool locOk  = !string.IsNullOrEmpty(locKey)  && locKey.StartsWith("difficulty.preset");
                bool descOk = !string.IsNullOrEmpty(descKey) && descKey.StartsWith("difficulty.preset_desc");
                if (!locOk || !descOk)
                {
                    sb.AppendLine($"  FAIL: {preset} → loc='{locKey}', desc='{descKey}'");
                    allOk = false;
                }
            }
            sb.AppendLine($"  4) wszystkie enum values mają non-empty loc/desc keys " + (allOk ? "PASS" : "FAIL"));
            sb.AppendLine($"  Result: {(allOk ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 5: DifficultyPresetCatalog.Get(Custom) = neutral ──────

        [ContextMenu("GameCreator: Test DifficultyPresetCatalog.Get(Custom) neutralny")]
        public void TestDifficultyPresetCatalogCustomNeutral()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] DifficultyPresetCatalog.Get(Custom) = neutralne 1.0:");

            var custom = DifficultyPresetCatalog.Get(DifficultyPreset.Custom);
            // Custom = `new DifficultyModifiers()` = wszystko 1.0 (Normal-like). Patrz Custom quirk
            // komentarz w Difficulty.Presets.cs:OnDifficultyPresetChanged — kreator celowo NIE
            // resetuje sliderów do tych wartości, slidery zostają z poprzedniego presetu jako UX.
            bool allNeutral =
                Mathf.Approximately(custom.StartBudgetMultiplier, 1f) &&
                Mathf.Approximately(custom.OperationalCostMultiplier, 1f) &&
                Mathf.Approximately(custom.BreakdownChanceMultiplier, 1f) &&
                Mathf.Approximately(custom.PassengerDemandMultiplier, 1f) &&
                Mathf.Approximately(custom.SalaryMultiplier, 1f) &&
                Mathf.Approximately(custom.SubsidyMultiplier, 1f) &&
                Mathf.Approximately(custom.DelayPropagationMultiplier, 1f) &&
                Mathf.Approximately(custom.EventFrequencyMultiplier, 1f) &&
                Mathf.Approximately(custom.HotelCostMultiplier, 1f) &&
                Mathf.Approximately(custom.TicketPriceToleranceMultiplier, 1f);
            sb.AppendLine($"  5) Custom wszystkie 10 mnożników = 1.0 " + (allNeutral ? "PASS" : "FAIL"));
            sb.AppendLine($"  Result: {(allNeutral ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 6: Non-Custom presets ≠ neutral ───────────────────────

        [ContextMenu("GameCreator: Test non-Custom presets differ from neutral")]
        public void TestDifficultyPresetCatalogNonCustomDiffers()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] Non-Custom presets ≠ neutralne:");

            // Normal SAM jest neutralny (= Custom). Easy/Hard/Realistic muszą odbiegać.
            DifficultyPreset[] differs = { DifficultyPreset.Easy, DifficultyPreset.Hard, DifficultyPreset.Realistic };
            bool allOk = true;
            foreach (var preset in differs)
            {
                var m = DifficultyPresetCatalog.Get(preset);
                bool isNeutral =
                    Mathf.Approximately(m.StartBudgetMultiplier, 1f) &&
                    Mathf.Approximately(m.OperationalCostMultiplier, 1f) &&
                    Mathf.Approximately(m.BreakdownChanceMultiplier, 1f);
                if (isNeutral)
                {
                    sb.AppendLine($"  FAIL: {preset} jest neutralny (powinien odbiegać)");
                    allOk = false;
                }
            }
            sb.AppendLine($"  6) Easy/Hard/Realistic odbiegają od 1.0 " + (allOk ? "PASS" : "FAIL"));

            // Normal jest neutralny — kontrakt projektu (1.0 baseline).
            var normal = DifficultyPresetCatalog.Get(DifficultyPreset.Normal);
            bool normalIsNeutral =
                Mathf.Approximately(normal.StartBudgetMultiplier, 1f) &&
                Mathf.Approximately(normal.BreakdownChanceMultiplier, 1f);
            sb.AppendLine($"  6b) Normal IS neutralny (baseline kontrakt) " + (normalIsNeutral ? "PASS" : "FAIL"));

            bool all = allOk && normalIsNeutral;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 7: FormatSeedFieldText ────────────────────────────────

        [ContextMenu("GameCreator: Test FormatSeedFieldText")]
        public void TestSeedFieldFormatting()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] FormatSeedFieldText:");

            // 0 = pusty placeholder (UX: nie pokazujemy "0" wprost gdy gracz nic nie wpisał)
            bool t7a = GameCreatorUI.FormatSeedFieldText(0) == "";
            sb.AppendLine($"  7a) 0 → '' (empty placeholder) " + (t7a ? "PASS" : "FAIL"));

            // Non-zero = liczba jako string
            bool t7b = GameCreatorUI.FormatSeedFieldText(42) == "42";
            sb.AppendLine($"  7b) 42 → '42' " + (t7b ? "PASS" : "FAIL"));

            bool t7c = GameCreatorUI.FormatSeedFieldText(-1) == "-1";
            sb.AppendLine($"  7c) -1 → '-1' (negatives accepted) " + (t7c ? "PASS" : "FAIL"));

            bool t7d = GameCreatorUI.FormatSeedFieldText(int.MaxValue) == int.MaxValue.ToString();
            sb.AppendLine($"  7d) int.MaxValue → '{int.MaxValue}' " + (t7d ? "PASS" : "FAIL"));

            bool all = t7a && t7b && t7c && t7d;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 8: SaveActionsHook.IsRegistered po Bootstrap ──────────

        [ContextMenu("GameCreator: Test SaveActionsHook.IsRegistered")]
        public void TestSaveActionsHookRegistration()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GameCreatorSmokeTest] SaveActionsHook registration:");

            // SaveActionsHook.Register jest wywoływane przez SaveLoadServiceBootstrap (AfterSceneLoad).
            // Smoke test też leci AfterSceneLoad — kolejność niedeterministyczna między
            // RuntimeInitializeOnLoadMethod ale przy ContextMenu run (po pełnym Bootstrap) musi być true.
            // Jeśli auto-spawn run via AutoSpawn → może być race. Ten test przeznaczony do
            // ContextMenu run po starcie Editor'a.
            bool registered = SaveActionsHook.IsRegistered;
            bool quickSaveSet = SaveActionsHook.QuickSave != null;
            bool autoSaveEnabledSet = SaveActionsHook.SetAutoSaveEnabled != null;
            bool resetRuntimeSet = SaveActionsHook.ResetRuntimeForNewGame != null;

            sb.AppendLine($"  8a) IsRegistered " + (registered ? "PASS" : "FAIL (SaveLoadServiceBootstrap nie odpalił)"));
            sb.AppendLine($"  8b) QuickSave hook non-null " + (quickSaveSet ? "PASS" : "FAIL"));
            sb.AppendLine($"  8c) SetAutoSaveEnabled hook non-null " + (autoSaveEnabledSet ? "PASS" : "FAIL"));
            sb.AppendLine($"  8d) ResetRuntimeForNewGame hook non-null " + (resetRuntimeSet ? "PASS" : "FAIL"));

            bool all = registered && quickSaveSet && autoSaveEnabledSet && resetRuntimeSet;
            sb.AppendLine($"  Result: {(all ? "ALL PASS" : "SOMETHING FAILED — Bootstrap order issue?")}");
            Log.Info(sb.ToString());
        }
    }
}
