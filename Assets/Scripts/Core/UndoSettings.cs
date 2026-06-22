using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Ustawienia systemu undo — max liczba cofnięć, persystowana w PlayerPrefs.
    /// </summary>
    public static class UndoSettings
    {
        private const string KEY = "depot.undo.maxCount";
        public const int DEFAULT = 3;
        public const int MIN = 1;
        public const int MAX = 20;

        public static event System.Action OnChanged;

        public static int MaxUndos
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KEY, DEFAULT), MIN, MAX);
            set
            {
                int v = Mathf.Clamp(value, MIN, MAX);
                PlayerPrefs.SetInt(KEY, v);
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }
    }
}
