using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Data-integrity (NIE kontrakt serwisu — to robi LocalizationServiceTests): parytet
    /// kluczy miedzy locale shipowanymi w EA (PL/EN/DE/CZ). Te 4 pliki sa utrzymywane
    /// w lockstep — test pilnuje ze maja IDENTYCZNY zestaw kluczy. FAIL = ktos dodal/
    /// usunal klucz w jednym jezyku a zapomnial w pozostalych -> gracz widzi "[key]"
    /// placeholder w UI (fallback chain current->EN->[key]).
    ///
    /// JP/RU/UK celowo wykluczone — to post-EA stuby (8 linii), nie kompletne tlumaczenia.
    /// </summary>
    public class LocalizationParityTests
    {
        // Locale shipowane jako kompletne w EA. JP/RU/UK = stuby (pominiete celowo).
        static readonly LocaleCode[] EaLocales = { LocaleCode.PL, LocaleCode.EN, LocaleCode.DE, LocaleCode.CZ };

        static HashSet<string> LoadKeys(LocaleCode locale)
        {
            string folder = LocaleResolver.ToFolderName(locale);
            var ta = Resources.Load<TextAsset>($"Locale/{folder}/strings");
            Assert.That(ta, Is.Not.Null, $"Brak pliku Resources/Locale/{folder}/strings.json dla {locale}.");
            var keys = new HashSet<string>();
            Flatten(JObject.Parse(ta.text), "", keys);
            return keys;
        }

        static void Flatten(JToken token, string prefix, HashSet<string> keys)
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties())
                    Flatten(p.Value, string.IsNullOrEmpty(prefix) ? p.Name : $"{prefix}.{p.Name}", keys);
            }
            else if (token.Type == JTokenType.String)
            {
                keys.Add(prefix);
            }
        }

        [Test]
        public void EaLocales_HaveIdenticalKeySets()
        {
            var perLocale = EaLocales.ToDictionary(l => l, LoadKeys);

            var union = new HashSet<string>();
            foreach (var set in perLocale.Values) union.UnionWith(set);

            var report = new List<string>();
            foreach (var kvp in perLocale)
            {
                var missing = union.Except(kvp.Value).ToList();
                if (missing.Count > 0)
                    report.Add($"{kvp.Key} brakuje {missing.Count} kluczy: " +
                               $"{string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? " ..." : "")}");
            }

            Assert.That(report, Is.Empty,
                "Locale EA nie maja identycznego zestawu kluczy (brakujace tlumaczenia -> [key] w UI):\n"
                + string.Join("\n", report));
        }

        [Test]
        public void EaLocales_AreNonEmpty()
        {
            foreach (var locale in EaLocales)
                Assert.That(LoadKeys(locale).Count, Is.GreaterThan(100),
                    $"{locale} ma podejrzanie malo kluczy — plik niezaladowany lub okrojony.");
        }
    }
}
