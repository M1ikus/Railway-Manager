using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// Component attached to hidden signal points for railway logic.
    /// Signals are not visible but store position and metadata for train simulation.
    /// </summary>
    public class SignalMarker : MonoBehaviour
    {
        [Header("Signal Data")]
        public Dictionary<string, string> metadata = new();

        /// <summary>
        /// Gets the signal type from metadata
        /// </summary>
        public string SignalType
        {
            get
            {
                metadata.TryGetValue("railway", out var type);
                return type ?? "signal";
            }
        }

        /// <summary>
        /// Gets the signal name if available
        /// </summary>
        public string SignalName
        {
            get
            {
                metadata.TryGetValue("name", out var name);
                return name ?? $"Signal_{GetEntityId()}";
            }
        }

        /// <summary>
        /// Gets the signal reference number if available
        /// </summary>
        public string SignalRef
        {
            get
            {
                metadata.TryGetValue("ref", out var refNum);
                return refNum;
            }
        }

        /// <summary>
        /// Gets the world position of this signal
        /// </summary>
        public Vector3 Position => transform.position;
    }
}
