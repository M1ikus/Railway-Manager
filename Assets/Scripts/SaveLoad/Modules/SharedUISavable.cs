using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Assistant;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// Persists SharedUI runtime state that is scoped to a save slot.
    ///
    /// Module ID: "shared_ui". Schema v1.
    /// - playerProgress: progressive UI mode unlock/preference state.
    /// - suggestionRecords: per-save memory for dismissed/accepted/snoozed suggestions.
    /// </summary>
    public class SharedUISavable : ISavable
    {
        public string ModuleId => "shared_ui";
        public int SchemaVersion => 1;

        public JObject Serialize()
        {
            var progress = PlayerProgressService.Snapshot();

            return new JObject
            {
                ["playerProgress"] = new JObject
                {
                    ["timetablesCreated"] = progress.ttCreated,
                    ["tutorialCompleted"] = progress.tutorialDone,
                    ["selectedMode"] = (int)progress.selected,
                    ["hasExplicitSelection"] = progress.hasExplicit,
                    ["advancedUnlockNotified"] = progress.advancedUnlockNotified,
                    ["expertUnlockNotified"] = progress.expertUnlockNotified
                },
                ["suggestionRecords"] = JArray.FromObject(SuggestionMemoryService.GetAllRecords()),
                // M11 AS-1d: persona asystenta (imię + historia). Proaktywność = GameRule (WorldSavable).
                ["assistantState"] = JObject.FromObject(AssistantState.Snapshot())
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            if (data["playerProgress"] is JObject progress)
            {
                var selectedMode = ReadUIMode(progress, "selectedMode", UIMode.Basic);
                int timetablesCreated = progress.Value<int?>("timetablesCreated") ?? 0;
                bool tutorialCompleted = progress.Value<bool?>("tutorialCompleted") ?? false;
                bool hasExplicitSelection = progress.Value<bool?>("hasExplicitSelection") ?? false;
                bool advancedUnlockNotified =
                    progress.Value<bool?>("advancedUnlockNotified") ??
                    timetablesCreated >= PlayerProgressService.AdvancedUnlockTimetableCount;
                bool expertUnlockNotified =
                    progress.Value<bool?>("expertUnlockNotified") ??
                    tutorialCompleted;

                PlayerProgressService.RestoreFromSave(
                    timetablesCreated,
                    tutorialCompleted,
                    selectedMode,
                    hasExplicitSelection,
                    advancedUnlockNotified,
                    expertUnlockNotified);
            }
            else
            {
                PlayerProgressService.Reset();
            }

            var records = new List<SuggestionRecord>();
            if (data["suggestionRecords"] is JArray suggestions)
            {
                foreach (var item in suggestions)
                {
                    try
                    {
                        records.Add(item.ToObject<SuggestionRecord>());
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"[SharedUISavable] Skipped malformed suggestion record: {e.Message}");
                    }
                }
            }

            SuggestionMemoryService.RestoreFromSave(records);

            // M11 AS-1d: stan persony asystenta. Stare save'y bez sekcji → defaulty (pre-EA fallback).
            if (data["assistantState"] is JObject assistant)
            {
                try
                {
                    AssistantState.RestoreFromSave(assistant.ToObject<AssistantStateSnapshot>());
                }
                catch (Exception e)
                {
                    Log.Warn($"[SharedUISavable] Malformed assistantState — reset to defaults: {e.Message}");
                    AssistantState.ResetForNewGame();
                }
            }
            else
            {
                AssistantState.ResetForNewGame();
            }
        }

        public void InitializeDefault()
        {
            PlayerProgressService.Reset();
            SuggestionMemoryService.Reset();
            AssistantState.ResetForNewGame();
        }

        private static UIMode ReadUIMode(JObject data, string fieldName, UIMode fallback)
        {
            var token = data[fieldName];
            if (token == null) return fallback;

            if (token.Type == JTokenType.String)
            {
                return Enum.TryParse(token.Value<string>(), ignoreCase: true, out UIMode parsed)
                    ? parsed
                    : fallback;
            }

            int rawValue = token.Value<int?>() ?? (int)fallback;
            return Enum.IsDefined(typeof(UIMode), rawValue)
                ? (UIMode)rawValue
                : fallback;
        }
    }

    public static class SharedUISavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new SharedUISavable());
        }
    }
}
