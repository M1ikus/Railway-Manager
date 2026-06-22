using System;
using System.Threading.Tasks;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Helper do bezpiecznego wywoływania async metod jako handlery przycisków UI.
    ///
    /// Problem: <c>async void</c> w Unity = exception leci do <see cref="System.Threading.SynchronizationContext"/>
    /// → unhandled exception (crash w build, silent w editor). Dodatkowo gdy GameObject
    /// zostanie destroyed w trakcie await, kontynuacja może touchować zniszczone obiekty
    /// → NullReferenceException + zaśmiecenie konsoli przy każdej zmianie sceny.
    ///
    /// Rozwiązanie: opakuj <see cref="Func{Task}"/> w try/catch z owner-check.
    /// Owner check używa Unity overloaded <c>==</c> operator który zwraca true gdy
    /// MonoBehaviour zostal Destroy()'d ale C# reference jest jeszcze nie-null.
    ///
    /// Użycie:
    /// <code>
    /// button.onClick.AddListener(() => AsyncUI.Run(OnClickAsync, this, "MyUI.SomeAction"));
    /// ...
    /// private async Task OnClickAsync() { ... }
    /// </code>
    /// </summary>
    public static class AsyncUI
    {
        /// <summary>
        /// Bezpiecznie wykonuje async akcję jako fire-and-forget. Łapie wszystkie
        /// exceptiony i loguje (Error gdy owner żyje, Debug gdy owner zniszczony —
        /// bo race condition po destroy nie jest błędem programu).
        /// </summary>
        /// <param name="action">Async akcja do wykonania.</param>
        /// <param name="owner">MonoBehaviour właściciel — gdy zostanie destroyed
        /// w trakcie await, exception po continuation jest klasyfikowany jako Debug.</param>
        /// <param name="contextTag">Krótki tag do logu (np. "SaveLoadUI.NewSave").</param>
        public static async void Run(Func<Task> action, MonoBehaviour owner, string contextTag)
        {
            if (action == null) return;
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                // Task anulowany — np. cancellation token przy scene change. Nie loguj.
            }
            catch (Exception e)
            {
                if (owner == null)
                    Log.Debug($"[AsyncUI] {contextTag}: exception po destroy ownera — {e.GetType().Name}: {e.Message}");
                else
                    Log.Error($"[AsyncUI] {contextTag}: async handler threw — {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
