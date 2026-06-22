namespace RailwayManager.Core.Difficulty
{
    /// <summary>
    /// M13-13 / D35: Preset poziomu trudności wybierany w GameCreator.
    /// Per-save, niemodyfikowalny mid-game (D33).
    ///
    /// 4 stałe + 1 Custom — Custom pozwala graczowi tweakować każdy modifier
    /// indywidualnie sliderami. Tuning konkretnych wartości presetów → M-Balance.
    /// </summary>
    public enum DifficultyPreset
    {
        /// <summary>Łatwy — wszystkie modyfikatory na korzyść gracza (więcej kasy, mniej awarii, więcej pasażerów).</summary>
        Easy = 0,

        /// <summary>Normalny — modyfikatory neutralne (1.0). Domyślny.</summary>
        Normal = 1,

        /// <summary>Trudny — modyfikatory utrudniające (mniej kasy, więcej awarii, ostrzejsze koszty).</summary>
        Hard = 2,

        /// <summary>Realistyczny — najtrudniejszy preset, modyfikatory zbliżone do realnych warunków rynkowych.</summary>
        Realistic = 3,

        /// <summary>Custom — gracz wybiera modyfikatory ręcznie sliderami.</summary>
        Custom = 99
    }
}
