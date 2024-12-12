#if UNITY_EDITOR
using UnityEngine;

namespace Lereldarion.DJ {
/// <summary>
/// A slider, with range from bottom (0) to top (127), and current value at handle.
/// </summary>
[DisallowMultipleComponent]
public class MidiSlider : MidiController {
    public Transform bottom;
    public Transform top;
    public Transform handle;
}
}
#endif