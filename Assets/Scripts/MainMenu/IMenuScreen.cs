namespace MainMenu
{
    /// <summary>
    /// Wspólny kontrakt 6 pełnoekranowych ekranów menu (LoadGame/Credits/Help/Settings/Mods/Multiplayer).
    /// Pozwala MainMenuUI iterować po liście zamiast hardkodować ukrycia/refresh per-screen.
    /// </summary>
    public interface IMenuScreen
    {
        bool IsVisible { get; }
        void Show();
        void Hide();
        void RefreshLanguage();
    }
}
