using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D7: Kandydat na rynku pracy. Rotacja co 7 dni gry.
    ///
    /// Generator <see cref="Runtime.PersonnelMarketGenerator"/> (M8-3) tworzy kandydatow
    /// z seed-random PL imion/nazwisk (<see cref="PolishNamesCatalog"/>), dystrybucja skill:
    /// 1★=35%, 2★=30%, 3★=20%, 4★=12%, 5★=3%.
    ///
    /// Pensja oczekiwana: <see cref="RoleDefinitions.GetBaseSalary"/> × (0.7 + 0.15×skill) + random ±10%.
    /// Hire bonus: 0 lub 5-25% rocznej pensji (~5% kandydatow go ma).
    ///
    /// Expires po 7 dniach (<see cref="availableUntilDateIso"/>) — kandydat znajduje inna prace.
    /// </summary>
    [Serializable]
    public class EmployeeCandidate
    {
        public int candidateId;

        public string firstName;
        public string lastName;

        /// <summary>Wiek 25-60 (kandydaci mlodsi, emeryci nie szukaja pracy).</summary>
        public int age;

        public EmployeeRole role;

        /// <summary>Skill 1-5.</summary>
        public int skill;

        /// <summary>Oczekiwana pensja miesieczna (gr).</summary>
        public int expectedSalaryGroszy;

        /// <summary>Bonus za zatrudnienie (~5% kandydatow ma, 5-25% rocznej pensji).</summary>
        public int hireBonusGroszy;

        /// <summary>Fluff text (CV): "5 lat w Przewozy Regionalne", "Absolwent UTK 2023".</summary>
        public string resumeNotes;

        /// <summary>Data expiry — po niej auto-usuwany przez <see cref="Runtime.CandidateMarketService"/>.</summary>
        public string availableUntilDateIso;
    }
}
