using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;

namespace DepotSystem.Undo
{
    /// <summary>
    /// Nasłuchuje Ctrl+Z i wywołuje UndoManager.UndoTop dla aktywnej kategorii.
    /// Komponent dodawany przez DepotUIManager.
    /// Uzywa ToolBuild.Undo action (bindowany do Ctrl+Z).
    /// </summary>
    public class UndoInputHandler : MonoBehaviour
    {
        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
        }

        void OnEnable()
        {
            if (_inputActions != null)
                _toolBuild.Enable();
        }

        void OnDisable()
        {
            if (_inputActions != null)
                _toolBuild.Disable();
        }

        void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        void Update()
        {
            // Scena Depot musi być aktywna
            if (SceneController.ActiveScene != SceneController.GameScene.Depot)
                return;

            // Ignoruj gdy user pisze w InputField
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<InputField>() != null)
                return;

            if (_toolBuild.Undo.WasPressedThisFrame())
            {
                var cat = UndoManager.CategoryForCurrentTool();
                if (cat.HasValue)
                    UndoManager.UndoTop(cat.Value);
            }
        }
    }
}
