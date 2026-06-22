namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Interface generatora geometrii dla schematu generative.
    /// Per kategoria (<see cref="TurnoutSchemaCategory"/>) jedna implementacja:
    /// <see cref="LadderSchemaGenerator"/>, <see cref="ThroatSchemaGenerator"/>, <see cref="ScissorsSchemaGenerator"/>.
    ///
    /// Generator NIE stawia rozjazdów ani torów — produkuje tylko opis geometrii (<see cref="SchemaGeometry"/>)
    /// w lokalnych współrzędnych. Konkretne wywołanie <c>TurnoutPlacer.Place...</c>
    /// + <c>PrefabTrackBuilder.PlaceTrackWithPolyline</c> dzieje się dopiero w <c>TurnoutSchemaPlacer</c>
    /// (MD-3+) po snap'ie + rotacji do global coords.
    /// </summary>
    public interface ITurnoutSchemaGenerator
    {
        /// <summary>Kategoria którą obsługuje ten generator.</summary>
        TurnoutSchemaCategory Category { get; }

        /// <summary>
        /// Generuje pełną geometrię schematu z parametrów. Parameters muszą być znormalizowane
        /// przez <see cref="SchemaParameters.Normalize"/> przed wywołaniem.
        /// </summary>
        SchemaGeometry Generate(SchemaParameters parameters);

        /// <summary>
        /// Waliduje parametry przed wywołaniem Generate. Zwraca false + error message
        /// gdy parametry są poza dozwolonym zakresem (np. trackCount poza limitem per kategoria).
        /// </summary>
        bool ValidateParameters(SchemaParameters parameters, out string error);

        /// <summary>
        /// Domyślne parametry dla tej kategorii (używane gdy gracz wybiera "nowy schemat" bez presetu).
        /// </summary>
        SchemaParameters DefaultParameters();

        /// <summary>
        /// Ile rozjazdów wygeneruje schemat dla danego trackCount. Używane przez
        /// <see cref="SchemaParameters.Normalize"/> żeby właściwie expand'ować shorthand → array.
        /// Np. Ladder N-track = N-1 rozjazdów. Scissors = zawsze 2.
        /// </summary>
        int ComputeTurnoutCount(int trackCount);
    }
}
