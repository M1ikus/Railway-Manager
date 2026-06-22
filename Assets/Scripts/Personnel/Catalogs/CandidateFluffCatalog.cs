using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-3: Pula fluff text'ow (resumeNotes) dla kandydatow generowanych przez
    /// <see cref="Runtime.PersonnelMarketGenerator"/>.
    ///
    /// 3-5 opcji per rola, dopasowane do realow polskiej kolei.
    /// Skill zmienia flavor: 1★ "juniorzy/swiezo po szkoleniu", 5★ "weterani/instruktorzy".
    ///
    /// Placeholder — post-EA mozna rozszerzyc do JSON w StreamingAssets (modding-friendly).
    /// </summary>
    public static class CandidateFluffCatalog
    {
        // Per rola — pierwsze 2 = juniorskie, srodkowe = standard, ostatnie 2 = senior
        static readonly Dictionary<EmployeeRole, string[]> NotesByRole = new()
        {
            [EmployeeRole.Driver] = new[]
            {
                "Swiezo po szkoleniu w Osrodku Szkolenia Maszynistow, bez praktyki",
                "6 miesiecy stazu na EZT w Kolejach Mazowieckich",
                "3 lata w Przewozy Regionalne, linie lokalne",
                "Doswiadczenie na EN57, EN71 i EP09 (5 lat)",
                "Uprawnienia na ETCS L2, staz za granica",
                "Instruktor CSK Warszawa, licencja od 2001"
            },
            [EmployeeRole.Conductor] = new[]
            {
                "Absolwentka technikum kolejowego, staz 3 miesiace",
                "Kelner w WARS, chce zmiany",
                "5 lat w PKP Intercity na ekspresach",
                "Rewizor pociagow ekspresowych od 2012",
                "Certyfikat PL/EN/DE, trasa miedzynarodowa Berlin-Warszawa",
                "Kierownik pociagu w Przewozy Regionalne, 15 lat"
            },
            [EmployeeRole.Mechanic] = new[]
            {
                "Absolwent ZSK Warszawa, praktyki w ZNTK",
                "2 lata w NEWAG, specjalizacja silniki spalinowe",
                "Mechanik EMU w Przewozy Regionalne, 7 lat",
                "Spawacz klasy A, naprawy wozkow jezdnych",
                "Diagnostyk systemow CA/SHP, szkolenie w Alstom",
                "Brygadzista warsztatu w PESA, 20 lat doswiadczenia"
            },
            [EmployeeRole.Cleaner] = new[]
            {
                "Sprzatanie w Galerii Mokotow, pierwsza praca na kolei",
                "Rok w firmie sprzatajacej dworce",
                "Obsluga techniczna Warszawa Centralna",
                "5 lat w PKP Cargo, sprzatanie taboru towarowego",
                "Koordynator zespolu sprzatajacego na stacji"
            },
            [EmployeeRole.WashBay] = new[]
            {
                "Obsluga myjni samochodowej, chce pracy na kolei",
                "Rok na myjni EZT w Warszawie-Odolanach",
                "3 lata obslugi myjni tunelowej, samodzielny",
                "Specjalista mycia tablic sygnalowych i okien"
            },
            [EmployeeRole.Office] = new[]
            {
                "Absolwent ekonomii UW, szuka pierwszej pracy",
                "Ksiegowa w biurze rachunkowym, 4 lata",
                "Specjalista HR w Przewozy Regionalne, 8 lat",
                "Kierownik biura w firmie transportowej",
                "Analityk finansowy, certyfikat ACCA"
            },
            [EmployeeRole.Research] = new[]
            {
                "Magister inzynier kolejowy Politechniki Warszawskiej",
                "Doktorant w Instytucie Kolejnictwa",
                "5 lat w dziale R&D Alstom",
                "Specjalista od systemow ETCS, publikacje miedzynarodowe",
                "Profesor PWR, konsultant dla NEWAG"
            },
            [EmployeeRole.TicketClerk] = new[]
            {
                "Sprzedawca w Biedronce, chce pracy stacjonarnej",
                "Kasjerka w PKS, 2 lata",
                "5 lat w kasie biletowej Warszawa Centralna",
                "Instruktor kas biletowych, znajomosc SKPL",
                "Kierownik zespolu kasjerow na stacji regionalnej"
            },
            [EmployeeRole.Dispatcher] = new[]
            {
                "Absolwent logistyki, staz w LS Airport Services",
                "Dyspozytor taborowy w Kolejach Mazowieckich, 3 lata",
                "Koordynator zmian w PKP Intercity, 8 lat",
                "Glowny dyspozytor terenowy, zarzadzal 80+ pracownikow",
                "Wieloletni dyspozytor generalny"
            },
            [EmployeeRole.TrafficController] = new[]
            {
                "Absolwent technikum kolejowego, uprawnienia dyzurnego ruchu",
                "Dyzurny ruchu na stacji regionalnej, 2 lata",
                "Nastawniczy w centrali LCS Warszawa, 7 lat",
                "Starszy dyzurny LCS, szkolenia w UTK",
                "Szef nastawni strategicznej, 20 lat stazu"
            }
        };

        /// <summary>
        /// Zwraca fluff note dla kandydata. Skill 1-2 = 2 pierwsze opcje (junior),
        /// 3-4 = srodkowe (standard), 5 = ostatnia (senior).
        /// </summary>
        public static string GetRandomNotes(Random random, EmployeeRole role, int skill)
        {
            if (!NotesByRole.TryGetValue(role, out var pool) || pool.Length == 0)
                return "Doswiadczenie niejasne.";

            int index;
            if (skill <= 2)
            {
                // Junior — pierwsze 2 opcje
                index = random.Next(Math.Min(2, pool.Length));
            }
            else if (skill >= 5)
            {
                // Senior — ostatnie 2 opcje
                int start = Math.Max(0, pool.Length - 2);
                index = start + random.Next(Math.Min(2, pool.Length - start));
            }
            else
            {
                // Mid-level — srodek lub calosc
                index = random.Next(pool.Length);
            }

            return pool[index];
        }
    }
}
