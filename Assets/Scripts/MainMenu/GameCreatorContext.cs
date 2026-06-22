namespace MainMenu
{
    /// <summary>
    /// Statyczny kontekst przekazywany między sceną menu a sceną kreatora gry.
    /// Nie potrzebuje MonoBehaviour ani DontDestroyOnLoad.
    /// </summary>
    public static class GameCreatorContext
    {
        public enum GameMode { SinglePlayer, Multiplayer }

        /// <summary>Tryb ustawiony przed załadowaniem sceny kreatora.</summary>
        public static GameMode Mode { get; set; } = GameMode.SinglePlayer;

        /// <summary>Czy wejście nastąpiło z trybu multiplayer.</summary>
        public static bool IsMultiplayer => Mode == GameMode.Multiplayer;

        // TD-022: MP server config zebrane przez GameCreatorUI.PopulateSerwer().
        // Konsumenci: M10 Mirror integration (jeszcze nie ready). Pre-EA placeholder bo
        // server name/password/visibility będą używane w lobby flow.
        public static string ServerName { get; set; } = "";
        public static int ServerMaxPlayers { get; set; } = 4;
        public static string ServerPassword { get; set; } = "";
        public enum ServerVisibility { Public, Private, Hidden }
        public static ServerVisibility ServerVisibilityValue { get; set; } = ServerVisibility.Public;

        public static void ResetServerConfig()
        {
            ServerName = "";
            ServerMaxPlayers = 4;
            ServerPassword = "";
            ServerVisibilityValue = ServerVisibility.Public;
        }
    }
}
