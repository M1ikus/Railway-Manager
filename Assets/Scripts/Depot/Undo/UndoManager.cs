using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Undo
{
    /// <summary>
    /// Statyczny manager historii cofania — 4 niezależne stacki per kategoria.
    /// Rozmiar każdego stacka ograniczony przez UndoSettings.MaxUndos.
    /// Re-entrancy guard IsUndoing zapobiega nagrywaniu commandów podczas cofania.
    /// </summary>
    public static class UndoManager
    {
        private static readonly Dictionary<UndoCategory, LinkedList<IUndoCommand>> stacks = new()
        {
            { UndoCategory.Tory, new LinkedList<IUndoCommand>() },
            { UndoCategory.SiecTrakcyjna, new LinkedList<IUndoCommand>() },
            { UndoCategory.Sciezki, new LinkedList<IUndoCommand>() },
            { UndoCategory.Pomieszczenia, new LinkedList<IUndoCommand>() },
        };

        public static bool IsUndoing { get; private set; }

        /// <summary>
        /// Tymczasowo wycisz nagrywanie bez IsUndoing=true.
        /// Używane gdy metoda wywołuje inne metody których nagrania chcemy pominąć.
        /// </summary>
        public static bool Silenced { get; set; }

        /// <summary>
        /// Dodaje command do stacka. Nic nie robi jeśli IsUndoing (re-entrancy guard).
        /// Jeśli stack przekracza MaxUndos, usuwa najstarszy (z końca).
        /// </summary>
        public static void Record(UndoCategory cat, IUndoCommand cmd)
        {
            if (IsUndoing || Silenced || cmd == null) return;
            var list = stacks[cat];
            list.AddFirst(cmd);

            int max = UndoSettings.MaxUndos;
            while (list.Count > max)
                list.RemoveLast();
        }

        /// <summary>
        /// Cofa najnowszy command z danej kategorii. Zwraca false jeśli stack pusty.
        /// </summary>
        public static bool UndoTop(UndoCategory cat)
        {
            var list = stacks[cat];
            if (list.Count == 0) return false;

            var cmd = list.First.Value;
            list.RemoveFirst();

            IsUndoing = true;
            try
            {
                cmd.Undo();
                Log.Info($"[UndoManager] Undone ({cat}): {cmd.Description}");
            }
            catch (System.Exception e)
            {
                Log.Error($"[UndoManager] Undo failed: {e.Message}");
            }
            finally
            {
                IsUndoing = false;
            }
            return true;
        }

        public static int Count(UndoCategory cat) => stacks[cat].Count;

        public static void ClearCategory(UndoCategory cat) => stacks[cat].Clear();

        public static void ClearAll()
        {
            foreach (var list in stacks.Values) list.Clear();
        }

        /// <summary>
        /// Mapuje aktualny ToolMode na kategorię undo. Null dla Select/Demolish.
        /// </summary>
        public static UndoCategory? CategoryForCurrentTool()
        {
            if (DepotUIManager.Instance == null) return null;
            return DepotUIManager.Instance.CurrentTool switch
            {
                ToolMode.BuildTrack => UndoCategory.Tory,
                ToolMode.BuildCatenary => UndoCategory.SiecTrakcyjna,
                ToolMode.BuildPath => UndoCategory.Sciezki,
                ToolMode.BuildRoom => UndoCategory.Pomieszczenia,
                _ => null
            };
        }
    }
}
