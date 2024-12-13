using UnityEngine;
using VRC.SDKBase;

namespace Lereldarion.DJ {
    /// <summary>
    /// Root of a DJ controller system (mesh + controls).
    /// Will generate a midi output mesh from controller definitions.
    /// </summary>
    [DisallowMultipleComponent]
    public class GenerateMidiOutput : MonoBehaviour, IEditorOnly {
        // TODO later [Range(1, 16)] public int channel;
        public Material controller_to_midi;
    }

    /// <summary>
    /// A graphical item that drives a Midi Controller value with the given id.
    /// </summary>
    abstract public class MidiController : MonoBehaviour, IEditorOnly {
        [Range(0, 127)]
        public int id;

        public float screen_x => id;
    }

    /// <summary>
    /// A graphical item that drives a Midi Note value with the given id.
    /// </summary>
    abstract public class MidiNote : MonoBehaviour, IEditorOnly {
        [Range(0, 127)]
        public int id;

        public float screen_x => id + 128;
    }
}