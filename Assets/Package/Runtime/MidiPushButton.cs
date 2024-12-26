using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lereldarion.DJ
{
    /// <summary>
    /// Declare a push button control, which output either 0 (not pressed) or 127 (pressed sufficiently to reach trigger position).
    /// Self is the movable button position, rest the unpressed position and trigger the trigger point.
    /// This component expects you to provide interactivity : typical use is a squishable collidable physbone from Rest to Self.
    /// </summary>
    [DisallowMultipleComponent]
    public class MidiPushButton : MidiController
    {
        [Tooltip("Trigger point position")]
        public Transform TriggerPoint;
        [Tooltip("Rest position override ; defaults to the editor button position")]
        public Transform RestOverride;

        public string ConfigurationError()
        {
            if (TriggerPoint == null) { return "Trigger point not set"; }
            if (TriggerPoint.IsChildOf(transform)) { return "Trigger point must be static with respect to handle"; }
            if (RestOverride != null && RestOverride.IsChildOf(transform)) { return "Rest position must be static with respect to handle"; }
            return null;
        }

        public void OnDrawGizmosSelected()
        {
            if (TriggerPoint != null)
            {
                Vector3 rest = RestOverride != null ? RestOverride.position : transform.position;

                Gizmos.color = Color.yellow;
                Gizmos.matrix = TriggerPoint.localToWorldMatrix;
                rest = TriggerPoint.InverseTransformPoint(rest);

                Vector3 rest_normalized = rest.normalized;
                float stroke = rest.magnitude;
                float button_radius = stroke; // Estimation

                // Better gizmo in the typical use case with physbone
                var physbone = transform.GetComponentInParent<VRCPhysBone>();
                if (physbone != null)
                {
                    button_radius = TriggerPoint.InverseTransformVector(transform.TransformVector(Vector3.forward * physbone.CalcRadius(1))).z;
                }

                Gizmos.DrawWireCube(0.5f * rest, rest_normalized * stroke + (Vector3.one - rest_normalized) * button_radius * 2f);
            }
        }
    }
}