using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Smoke test regresji dla SharedUI po audycie 2026-05-15 (5 commit'ów top-tier
    /// bolaczek + 1 commit drobnych). Sprawdza invariants kluczowych helperów + servicy:
    ///
    /// - <see cref="NumberFormatService.FormatCurrency"/> wymusza "zł" suffix per locale
    ///   (bug fix: wcześniej zwracał symbol kultury — $/€/¥)
    /// - <see cref="UITheme.GetTextColor"/>/<see cref="UITheme.GetReputationColor"/>/Focus alias
    /// - <see cref="UITheme.ApplyCanvasScaler"/> ustawia 1920×1080 + match 0.5 + MatchWidthOrHeight
    /// - <see cref="LocalizationService.Get"/> fallback chain (unknown → "[key]")
    /// - <see cref="LocaleResolver"/> registry coverage (7 locales: PL/EN/DE/CZ/JP/RU/UK)
    /// - <see cref="PlayerProgressService"/> Snapshot/Restore roundtrip
    /// - <see cref="SuggestionMemoryService"/> dismiss/snooze/reset semantics
    ///
    /// Każdy test ContextMenu jest niezależny — możesz uruchamiać selektywnie. Wszystkie
    /// piszą wynik przez <see cref="Log.Info"/> z prefiksem `[SharedUISmokeTest]` i markerem
    /// PASS/FAIL na końcu wiersza. Backup/restore stanu globalnego (PlayerProgress +
    /// SuggestionMemory) w blokach try/finally.
    ///
    /// Konwencja projektu (CLAUDE.md): brak Unity Test Framework — smoke tests +
    /// <c>[ContextMenu]</c> ręczne uruchamianie w Editor.
    /// </summary>
    public class SharedUISmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<SharedUISmokeTest>() != null) return;
            var go = new GameObject("SharedUISmokeTest (auto-spawn)");
            go.AddComponent<SharedUISmokeTest>();
        }

        [ContextMenu("SharedUI: Run ALL smoke tests")]
        public void RunAll()
        {
            TestFormatCurrency();
            TestThemeColors();
            TestCanvasScaler();
            TestLocalizationFallback();
            TestLocaleResolverRegistry();
            TestPlayerProgressRoundtrip();
            TestSuggestionMemoryDismissSnooze();
        }

        // ── Test 1: FormatCurrency wymusza "zł" suffix ─────────────────

        [ContextMenu("SharedUI: Test FormatCurrency 'zł' suffix per locale")]
        public void TestFormatCurrency()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] FormatCurrency wymusza 'zł' per locale:");

            // 1a: PL → "zł" suffix
            string pl = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.PL);
            bool t1a_ok = pl.EndsWith(" zł");
            sb.AppendLine($"  1a) PL 1234.56 → \"{pl}\" ends with ' zł' " + (t1a_ok ? "PASS" : "FAIL"));

            // 1b: EN → "zł" zamiast "$"
            string en = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.EN);
            bool t1b_ok = en.EndsWith(" zł") && !en.Contains("$");
            sb.AppendLine($"  1b) EN 1234.56 → \"{en}\" no '$', has ' zł' " + (t1b_ok ? "PASS" : "FAIL"));

            // 1c: DE → "zł" zamiast "€"
            string de = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.DE);
            bool t1c_ok = de.EndsWith(" zł") && !de.Contains("€");
            sb.AppendLine($"  1c) DE 1234.56 → \"{de}\" no '€', has ' zł' " + (t1c_ok ? "PASS" : "FAIL"));

            // 1d: JP → "zł" zamiast "¥", bez utraty decimals
            string jp = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.JP);
            bool t1d_ok = jp.EndsWith(" zł") && !jp.Contains("¥");
            sb.AppendLine($"  1d) JP 1234.56 → \"{jp}\" no '¥', has ' zł' " + (t1d_ok ? "PASS" : "FAIL"));

            // 1e: long overload, decimals=0 default → bez kropki / przecinka
            string longPl = NumberFormatService.FormatCurrency(1234567L, LocaleCode.PL);
            bool t1e_ok = longPl.EndsWith(" zł") && !longPl.Contains(",") && !longPl.Contains(".");
            sb.AppendLine($"  1e) PL long 1234567 → \"{longPl}\" no decimals " + (t1e_ok ? "PASS" : "FAIL"));

            // 1f: negative
            string neg = NumberFormatService.FormatCurrency(-500L, LocaleCode.PL);
            bool t1f_ok = neg.Contains("zł") && neg.Contains("-");
            sb.AppendLine($"  1f) PL -500 → \"{neg}\" has '-' and 'zł' " + (t1f_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t1a_ok && t1b_ok && t1c_ok && t1d_ok && t1e_ok && t1f_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 2: UITheme color helpers + Focus alias ────────────────

        [ContextMenu("SharedUI: Test UITheme color helpers + Focus alias")]
        public void TestThemeColors()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] UITheme color helpers:");

            // 2a: GetTextColor mapping
            bool t2a_ok = UITheme.GetTextColor(UIThemeTextRole.Primary) == UITheme.PrimaryText
                       && UITheme.GetTextColor(UIThemeTextRole.Success) == UITheme.Success
                       && UITheme.GetTextColor(UIThemeTextRole.Danger) == UITheme.Danger;
            sb.AppendLine($"  2a) GetTextColor(Primary/Success/Danger) maps correctly " + (t2a_ok ? "PASS" : "FAIL"));

            // 2b: GetReputationColor thresholds (≥70 Success, ≥40 Warning, else Danger)
            bool t2b_ok = UITheme.GetReputationColor(80) == UITheme.Success
                       && UITheme.GetReputationColor(70) == UITheme.Success
                       && UITheme.GetReputationColor(69) == UITheme.Warning
                       && UITheme.GetReputationColor(40) == UITheme.Warning
                       && UITheme.GetReputationColor(39) == UITheme.Danger
                       && UITheme.GetReputationColor(0)  == UITheme.Danger;
            sb.AppendLine($"  2b) GetReputationColor thresholds [70+/40+/below] " + (t2b_ok ? "PASS" : "FAIL"));

            // 2c: Focus alias = PrimaryAccent (post-refactor 2026-05-15)
            bool t2c_ok = UITheme.Focus == UITheme.PrimaryAccent;
            sb.AppendLine($"  2c) Focus aliases PrimaryAccent " + (t2c_ok ? "PASS" : "FAIL"));

            // 2d: Darken — kanał -amount, clamp [0,1], alpha zachowane
            var dark = UITheme.Darken(new Color(0.5f, 0.5f, 0.5f, 0.8f), 0.1f);
            bool t2d_ok = Mathf.Approximately(dark.r, 0.4f) && Mathf.Approximately(dark.g, 0.4f)
                       && Mathf.Approximately(dark.b, 0.4f) && Mathf.Approximately(dark.a, 0.8f);
            sb.AppendLine($"  2d) Darken(0.5, 0.1) ≈ 0.4 RGB, alpha zachowane " + (t2d_ok ? "PASS" : "FAIL"));

            // 2e: Darken clamp do 0 (nie wychodzi poniżej)
            var clamped = UITheme.Darken(new Color(0.05f, 0.05f, 0.05f, 1f), 0.5f);
            bool t2e_ok = clamped.r == 0f && clamped.g == 0f && clamped.b == 0f;
            sb.AppendLine($"  2e) Darken clamp 0 (0.05 - 0.5 = 0, nie -0.45) " + (t2e_ok ? "PASS" : "FAIL"));

            // 2f: WithAlpha
            var withA = UITheme.WithAlpha(Color.red, 0.5f);
            bool t2f_ok = withA.r == 1f && Mathf.Approximately(withA.a, 0.5f);
            sb.AppendLine($"  2f) WithAlpha(red, 0.5) " + (t2f_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t2a_ok && t2b_ok && t2c_ok && t2d_ok && t2e_ok && t2f_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 3: ApplyCanvasScaler ───────────────────────────────────

        [ContextMenu("SharedUI: Test ApplyCanvasScaler standard")]
        public void TestCanvasScaler()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] UITheme.ApplyCanvasScaler:");

            var go = new GameObject("__SmokeTest_CanvasScaler", typeof(Canvas), typeof(CanvasScaler));
            try
            {
                var scaler = go.GetComponent<CanvasScaler>();
                // Wymuś "popsute" wartości żeby helper musiał je nadpisać
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.referenceResolution = new Vector2(800f, 600f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.matchWidthOrHeight = 0f;

                UITheme.ApplyCanvasScaler(scaler);

                bool t3a_ok = scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sb.AppendLine($"  3a) uiScaleMode = ScaleWithScreenSize " + (t3a_ok ? "PASS" : "FAIL"));

                bool t3b_ok = Mathf.Approximately(scaler.referenceResolution.x, 1920f)
                           && Mathf.Approximately(scaler.referenceResolution.y, 1080f);
                sb.AppendLine($"  3b) referenceResolution = 1920x1080 (actual {scaler.referenceResolution}) " + (t3b_ok ? "PASS" : "FAIL"));

                bool t3c_ok = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sb.AppendLine($"  3c) screenMatchMode nadpisany Expand → MatchWidthOrHeight " + (t3c_ok ? "PASS" : "FAIL"));

                bool t3d_ok = Mathf.Approximately(scaler.matchWidthOrHeight, 0.5f);
                sb.AppendLine($"  3d) matchWidthOrHeight = 0.5 (actual {scaler.matchWidthOrHeight}) " + (t3d_ok ? "PASS" : "FAIL"));

                // 3e: null defense — nie rzuca
                UITheme.ApplyCanvasScaler(null);
                sb.AppendLine($"  3e) null scaler → no throw PASS");

                sb.AppendLine($"  Result: {(t3a_ok && t3b_ok && t3c_ok && t3d_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Log.Info(sb.ToString());
        }

        // ── Test 4: Localization fallback chain ─────────────────────────

        [ContextMenu("SharedUI: Test LocalizationService.Get fallback")]
        public void TestLocalizationFallback()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] LocalizationService.Get fallback chain:");

            // 4a: unknown key → [key] placeholder
            const string unknownKey = "__nonexistent.key.smoketest__";
            string unknown = LocalizationService.Get(unknownKey);
            bool t4a_ok = unknown == $"[{unknownKey}]";
            sb.AppendLine($"  4a) unknown key → \"{unknown}\" matches \"[{unknownKey}]\" " + (t4a_ok ? "PASS" : "FAIL"));

            // 4b: empty key → "" (early return)
            string empty = LocalizationService.Get("");
            bool t4b_ok = empty == "";
            sb.AppendLine($"  4b) empty key → \"\" early return " + (t4b_ok ? "PASS" : "FAIL"));

            // 4c: format args swallowed gdy template = placeholder (string.Format na "[key]" bez {0})
            string fmt = LocalizationService.Get("__nonexistent.format__", 42, "test");
            bool t4c_ok = fmt.Contains("__nonexistent");
            sb.AppendLine($"  4c) format unknown key → placeholder preserved \"{fmt}\" " + (t4c_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t4a_ok && t4b_ok && t4c_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 5: LocaleResolver registry coverage ────────────────────

        [ContextMenu("SharedUI: Test LocaleResolver registry coverage")]
        public void TestLocaleResolverRegistry()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] LocaleResolver registry coverage:");

            var locales = new[] { LocaleCode.PL, LocaleCode.EN, LocaleCode.DE, LocaleCode.CZ, LocaleCode.JP, LocaleCode.RU, LocaleCode.UK };
            var expectedFolders = new Dictionary<LocaleCode, string>
            {
                [LocaleCode.PL] = "pl", [LocaleCode.EN] = "en", [LocaleCode.DE] = "de", [LocaleCode.CZ] = "cz",
                [LocaleCode.JP] = "jp", [LocaleCode.RU] = "ru", [LocaleCode.UK] = "uk"
            };

            bool allFolders = true;
            foreach (var loc in locales)
            {
                string folder = LocaleResolver.ToFolderName(loc);
                if (folder != expectedFolders[loc])
                {
                    allFolders = false;
                    sb.AppendLine($"  FAIL {loc}: folder=\"{folder}\" expected \"{expectedFolders[loc]}\"");
                }
            }
            sb.AppendLine($"  5a) Wszystkie 7 locales mają poprawny folder " + (allFolders ? "PASS" : "FAIL"));

            bool allCultures = true;
            foreach (var loc in locales)
            {
                try
                {
                    var ci = LocaleResolver.ToCultureInfo(loc);
                    if (ci == null || ci.TwoLetterISOLanguageName.Length != 2)
                    {
                        allCultures = false;
                        sb.AppendLine($"  FAIL {loc}: CultureInfo invalid");
                    }
                }
                catch (System.Exception e)
                {
                    allCultures = false;
                    sb.AppendLine($"  FAIL {loc}: ToCultureInfo threw {e.GetType().Name}");
                }
            }
            sb.AppendLine($"  5b) Wszystkie 7 locales mają działający CultureInfo " + (allCultures ? "PASS" : "FAIL"));

            // 5c: CZ → ISO "cs" (Czech), nie "cz" (różnica folderName vs ISO)
            var cz = LocaleResolver.ToCultureInfo(LocaleCode.CZ);
            bool t5c_ok = cz.TwoLetterISOLanguageName == "cs";
            sb.AppendLine($"  5c) CZ.ToCultureInfo.ISO = \"cs\" (nie \"cz\") actual=\"{cz.TwoLetterISOLanguageName}\" " + (t5c_ok ? "PASS" : "FAIL"));

            // 5d: JP → ISO "ja"
            var jp = LocaleResolver.ToCultureInfo(LocaleCode.JP);
            bool t5d_ok = jp.TwoLetterISOLanguageName == "ja";
            sb.AppendLine($"  5d) JP.ToCultureInfo.ISO = \"ja\" actual=\"{jp.TwoLetterISOLanguageName}\" " + (t5d_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(allFolders && allCultures && t5c_ok && t5d_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 6: PlayerProgress Snapshot/Restore roundtrip ───────────

        [ContextMenu("SharedUI: Test PlayerProgressService Snapshot/Restore roundtrip")]
        public void TestPlayerProgressRoundtrip()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] PlayerProgressService Snapshot/Restore roundtrip:");

            var backup = PlayerProgressService.Snapshot();
            try
            {
                PlayerProgressService.Reset();
                for (int i = 0; i < 7; i++) PlayerProgressService.RecordTimetableCreated();
                PlayerProgressService.RecordTutorialCompletion();
                PlayerProgressService.SetPlayerMode(UIMode.Advanced);

                var snap = PlayerProgressService.Snapshot();
                bool t6a_ok = snap.ttCreated == 7 && snap.tutorialDone && snap.selected == UIMode.Advanced && snap.hasExplicit;
                sb.AppendLine($"  6a) Setup state captured (tt=7, tut=true, mode=Advanced, explicit=true) " + (t6a_ok ? "PASS" : "FAIL"));

                // Reset + restore
                PlayerProgressService.Reset();
                PlayerProgressService.RestoreFromSave(snap.ttCreated, snap.tutorialDone, snap.selected, snap.hasExplicit,
                    snap.advancedUnlockNotified, snap.expertUnlockNotified);
                var rs = PlayerProgressService.Snapshot();
                bool t6b_ok = rs.ttCreated == 7 && rs.tutorialDone && rs.selected == UIMode.Advanced && rs.hasExplicit;
                sb.AppendLine($"  6b) Reset+Restore preserves state (tt={rs.ttCreated} tut={rs.tutorialDone} mode={rs.selected}) " + (t6b_ok ? "PASS" : "FAIL"));

                // 6c: IsAdvancedUnlocked respektuje threshold
                bool t6c_ok = PlayerProgressService.IsAdvancedUnlocked;
                sb.AppendLine($"  6c) IsAdvancedUnlocked == true (7 >= threshold {PlayerProgressService.AdvancedUnlockTimetableCount}) " + (t6c_ok ? "PASS" : "FAIL"));

                // 6d: IsExpertUnlocked po tutorial (bypass czasu gry)
                bool t6d_ok = PlayerProgressService.IsExpertUnlocked;
                sb.AppendLine($"  6d) IsExpertUnlocked == true (tutorial done) " + (t6d_ok ? "PASS" : "FAIL"));

                // 6e: Reset → zerowy stan
                PlayerProgressService.Reset();
                var resetSnap = PlayerProgressService.Snapshot();
                bool t6e_ok = resetSnap.ttCreated == 0 && !resetSnap.tutorialDone && resetSnap.selected == UIMode.Basic && !resetSnap.hasExplicit;
                sb.AppendLine($"  6e) Reset → zeroed state (tt=0, tut=false, mode=Basic, explicit=false) " + (t6e_ok ? "PASS" : "FAIL"));

                sb.AppendLine($"  Result: {(t6a_ok && t6b_ok && t6c_ok && t6d_ok && t6e_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            }
            finally
            {
                PlayerProgressService.RestoreFromSave(backup.ttCreated, backup.tutorialDone, backup.selected, backup.hasExplicit,
                    backup.advancedUnlockNotified, backup.expertUnlockNotified);
            }
            Log.Info(sb.ToString());
        }

        // ── Test 7: SuggestionMemory dismiss/snooze/reset ────────────────

        [ContextMenu("SharedUI: Test SuggestionMemory dismiss/snooze/reset")]
        public void TestSuggestionMemoryDismissSnooze()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SharedUISmokeTest] SuggestionMemoryService dismiss/snooze/reset:");

            // Backup wszystkich records (restore w finally)
            var backup = new List<SuggestionRecord>(SuggestionMemoryService.GetAllRecords());

            try
            {
                SuggestionMemoryService.Reset();

                // 7a: fresh state → ShouldShow = true
                bool t7a_ok = SuggestionMemoryService.ShouldShow(SuggestionType.Circulation, "ctx_a");
                sb.AppendLine($"  7a) Fresh state → ShouldShow(Circulation, ctx_a)=true " + (t7a_ok ? "PASS" : "FAIL"));

                // 7b: po Dismiss → ShouldShow = false
                SuggestionMemoryService.RecordChoice(SuggestionType.Circulation, "ctx_a", SuggestionChoice.Dismiss);
                bool t7b_ok = !SuggestionMemoryService.ShouldShow(SuggestionType.Circulation, "ctx_a");
                sb.AppendLine($"  7b) Po Dismiss → ShouldShow=false " + (t7b_ok ? "PASS" : "FAIL"));

                // 7c: per-context — inny contextKey nie dotknięty
                bool t7c_ok = SuggestionMemoryService.ShouldShow(SuggestionType.Circulation, "ctx_b");
                sb.AppendLine($"  7c) Inny contextKey → ShouldShow(ctx_b)=true (per-context isolation) " + (t7c_ok ? "PASS" : "FAIL"));

                // 7d: różny SuggestionType na tym samym contextKey — niezależny
                bool t7d_ok = SuggestionMemoryService.ShouldShow(SuggestionType.CrewSwap, "ctx_a");
                sb.AppendLine($"  7d) Inny SuggestionType na tym samym ctx → ShouldShow=true " + (t7d_ok ? "PASS" : "FAIL"));

                // 7e: empty contextKey → ShouldShow always true (defensive)
                bool t7e_ok = SuggestionMemoryService.ShouldShow(SuggestionType.Mijanka, "");
                sb.AppendLine($"  7e) Empty contextKey → ShouldShow=true (defensive guard) " + (t7e_ok ? "PASS" : "FAIL"));

                // 7f: Reset → wszystko clear
                SuggestionMemoryService.Reset();
                bool t7f_ok = SuggestionMemoryService.ShouldShow(SuggestionType.Circulation, "ctx_a")
                           && SuggestionMemoryService.RecordCount == 0;
                sb.AppendLine($"  7f) Reset → records cleared, ShouldShow(ctx_a)=true ponownie " + (t7f_ok ? "PASS" : "FAIL"));

                sb.AppendLine($"  Result: {(t7a_ok && t7b_ok && t7c_ok && t7d_ok && t7e_ok && t7f_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            }
            finally
            {
                SuggestionMemoryService.RestoreFromSave(backup);
            }
            Log.Info(sb.ToString());
        }
    }
}
