namespace DepotSystem.Schemas
{
    /// <summary>
    /// Kategoria schematu głowicy rozjazdowej. Wpływa na:
    /// - który <see cref="ITurnoutSchemaGenerator"/> generuje geometrię (per kategoria)
    /// - jakie parametry są dostępne w panelu (Ladder/Throat = full, Scissors = ograniczone)
    /// - jak schemat jest oznaczony w UI (filter w bibliotece)
    ///
    /// <c>Custom</c> = snapshot literalny (nie używa generatora — geometria zaszyta w JSON).
    /// </summary>
    public enum TurnoutSchemaCategory
    {
        Ladder,     // Drabinka — sekwencja N rozjazdów na torze przewodnim → N torów postojowych
        Throat,     // Wachlarz — głowica z mix R190+R300 do różnych torów
        Scissors,   // Rozjazd nożycowy (X) — 4 rozjazdy w 4 rogach + 2 diagonale krzyżujące się w środku
        Trapez,     // Trapez ("<") — 2 rozjazdy na lewym torze, oba odgałęzienia spotykają się w 1 punkcie na prawym torze
        Custom,     // Snapshot literalny (geometria w JSON, brak parametryzacji)
    }
}
