using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1: Pule polskich imion i nazwisk dla generatora kandydatow
    /// (<see cref="Runtime.PersonnelMarketGenerator"/> w M8-3).
    ///
    /// Poczatkowo 25+25 imion (M/K) i 50 nazwisk. Post-EA mozna rozszerzyc
    /// lub przeniesc do StreamingAssets JSON dla modding-friendly.
    ///
    /// Plec losowana 50/50 z seed'a (deterministyczne dla save/load — D17 analog).
    /// </summary>
    public static class PolishNamesCatalog
    {
        private static readonly string[] MaleFirstNames =
        {
            "Jan", "Andrzej", "Krzysztof", "Stanislaw", "Piotr",
            "Tomasz", "Pawel", "Michal", "Marcin", "Jakub",
            "Adam", "Lukasz", "Marek", "Grzegorz", "Jozef",
            "Wojciech", "Mariusz", "Rafal", "Zbigniew", "Dariusz",
            "Kamil", "Karol", "Sebastian", "Mateusz", "Dawid"
        };

        private static readonly string[] FemaleFirstNames =
        {
            "Anna", "Maria", "Katarzyna", "Malgorzata", "Agnieszka",
            "Krystyna", "Barbara", "Ewa", "Elzbieta", "Zofia",
            "Teresa", "Janina", "Aleksandra", "Magdalena", "Monika",
            "Joanna", "Jolanta", "Beata", "Renata", "Marta",
            "Dorota", "Halina", "Urszula", "Jadwiga", "Irena"
        };

        // Nazwiska unisex (w PL tradycyjnie konczace sie -ski/-ska, ale dla uproszczenia
        // w grze uzywamy formy meskiej — placeholder pre-M-Models).
        private static readonly string[] LastNames =
        {
            "Nowak", "Kowalski", "Wisniewski", "Wojcik", "Kowalczyk",
            "Kaminski", "Lewandowski", "Zielinski", "Szymanski", "Wozniak",
            "Dabrowski", "Kozlowski", "Jankowski", "Mazur", "Kwiatkowski",
            "Krawczyk", "Kaczmarek", "Piotrowski", "Grabowski", "Zajac",
            "Pawlak", "Michalski", "Nowakowski", "Krol", "Jablonski",
            "Wrobel", "Wieczorek", "Jaworski", "Malinowski", "Adamczyk",
            "Dudek", "Nowicki", "Pawlowski", "Gorski", "Witkowski",
            "Walczak", "Sikora", "Baran", "Rutkowski", "Michalak",
            "Szewczyk", "Ostrowski", "Tomaszewski", "Pietrzak", "Marciniak",
            "Wroblewski", "Zalewski", "Jakubowski", "Jasinski", "Wysocki"
        };

        /// <summary>Losuj imie — <paramref name="isMale"/>=true: pula meska, false: zenska.</summary>
        public static string GetRandomFirstName(Random random, bool isMale)
        {
            var pool = isMale ? MaleFirstNames : FemaleFirstNames;
            return pool[random.Next(pool.Length)];
        }

        /// <summary>Losuj nazwisko z ogolnej puli (unisex placeholder).</summary>
        public static string GetRandomLastName(Random random)
        {
            return LastNames[random.Next(LastNames.Length)];
        }

        /// <summary>Losuj pare (imie, nazwisko) — losowa plec 50/50.</summary>
        public static (string firstName, string lastName, bool isMale) GetRandomFullName(Random random)
        {
            bool isMale = random.Next(2) == 0;
            string first = GetRandomFirstName(random, isMale);
            string last = GetRandomLastName(random);
            return (first, last, isMale);
        }

        // ── Statystyki (dla debug) ────────────────────

        public static int MaleFirstNamesCount => MaleFirstNames.Length;
        public static int FemaleFirstNamesCount => FemaleFirstNames.Length;
        public static int LastNamesCount => LastNames.Length;
    }
}
