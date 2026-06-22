using System.Diagnostics;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Centralna klasa logowania dla projektu. Owija UnityEngine.Debug.Log
    /// z czterema poziomami: Info / Warn / Error / Debug.
    ///
    /// Log.Debug jest opakowane w [Conditional("DEBUG_LOG_VERBOSE")] — w buildach
    /// release jest kompletnie usuwane przez kompilator (zero kosztu, zero alokacji).
    /// Żeby włączyć w Editorze: Player Settings → Scripting Define Symbols → DEBUG_LOG_VERBOSE.
    ///
    /// Log.Info / Warn / Error zawsze działają (i w Editorze, i w buildzie).
    ///
    /// Konwencja: zaczynaj wiadomość od prefiksu [ClassName], np.:
    ///     Log.Info("[DepotManager] Initialized");
    ///     Log.Error("[MapLoader] Failed to load tile: " + path);
    /// </summary>
    public static class Log
    {
        // === Info / Warn / Error: zawsze aktywne ===

        public static void Info(string message)
            => UnityEngine.Debug.Log(message);

        public static void Info(string message, Object context)
            => UnityEngine.Debug.Log(message, context);

        public static void Warn(string message)
            => UnityEngine.Debug.LogWarning(message);

        public static void Warn(string message, Object context)
            => UnityEngine.Debug.LogWarning(message, context);

        public static void Error(string message)
            => UnityEngine.Debug.LogError(message);

        public static void Error(string message, Object context)
            => UnityEngine.Debug.LogError(message, context);

        // === Debug: tylko gdy DEBUG_LOG_VERBOSE jest zdefiniowane ===
        // [Conditional] sprawia że wywołanie i argumenty są usuwane przez kompilator
        // gdy symbol nie jest zdefiniowany — dosłownie zero kosztu w buildzie release.

        [Conditional("DEBUG_LOG_VERBOSE")]
        public static void Debug(string message)
            => UnityEngine.Debug.Log(message);

        [Conditional("DEBUG_LOG_VERBOSE")]
        public static void Debug(string message, Object context)
            => UnityEngine.Debug.Log(message, context);
    }
}
