using UnityEngine;
using IEditorOnly = VRC.SDKBase.IEditorOnly;

namespace Lereldarion.DJ
{
    /// <summary>
    /// Root of a DJ controller system (mesh + controls).
    /// Will generate a midi output mesh from controller definitions.
    /// </summary>
    [DisallowMultipleComponent]
    public class GenerateMidiOutput : MonoBehaviour, IEditorOnly
    {
        // TODO later [Range(1, 16)] public int channel;
        public Material MidiOutputMaterial;

        [Header("Hand transforms")]
        public Transform LeftHand;
        public Transform RightHand;
        [Header("Slider / Potentiometer fingers")]
        public Transform LeftFinger;
        public Transform RightFinger;
    }

    /// <summary>
    /// A graphical item that drives a Midi Controller value with the given id.
    /// </summary>
    abstract public class MidiController : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Midi controller id [0-127]")]
        [Range(0, 127)]
        public int Id;

        public float ScreenX => Id + 1;
    }

    /// <summary>
    /// A graphical item that drives a Midi Note value with the given id.
    /// </summary>
    abstract public class MidiNote : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Midi note id [0-127]")]
        [Range(0, 127)]
        public int Id;

        public float ScreenX => Id + 128 + 1;
    }

    /// <summary>
    /// Select which hand is tracked. Each Hand needs its contact.
    /// </summary>
    public enum HandTrackingMode { Both, OnlyLeft, OnlyRight }
    
    public enum Axis { X, Y, Z }
}