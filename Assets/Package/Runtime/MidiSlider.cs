using UnityEngine;

namespace Lereldarion.DJ
{
    /// <summary>
    /// Declare a slider control, which outputs a continuous value from minimum (0) to maximum (127).
    /// Movable handle is self. Parent is the base.
    /// This component will generate all needed gameobjects, contacts, animator support for interaction.
    /// </summary>
    [DisallowMultipleComponent]
    public class MidiSlider : MidiController
    {
        [Tooltip("Radius of the finger collider")]
        public float ColliderRadius = 0.015f;

        [Tooltip("Minimum position (value = 0)")]
        public Transform Minimum;
        [Tooltip("Maximum position (value = 127)")]
        public Transform Maximum;

        [Tooltip("Optionally restrict tracking to one hand only (reduces contact count)")]
        public HandTrackingMode HandTracking = HandTrackingMode.Both;

        public string ConfigurationError()
        {
            if (Minimum == null) { return "Minimum not set"; }
            if (Maximum == null) { return "Maximum not set"; }
            if (Minimum.IsChildOf(transform)) { return "Minimum must be static with respect to handle"; }
            if (Maximum.IsChildOf(transform)) { return "Maximum must be static with respect to handle"; }
            if (ColliderRadius <= 0.0f) { return "Collider Radius must be strictly positive"; }
            return null;
        }

        public void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (ColliderRadius > 0)
            {
                Gizmos.DrawWireSphere(transform.position, ColliderRadius * transform.lossyScale.x);
            }

            if (Minimum != null && Maximum != null)
            {
                Vector3 stroke = Maximum.position - Minimum.position;
                Gizmos.DrawLine(
                    transform.position + Vector3.Project(Minimum.position - transform.position, stroke),
                    transform.position + Vector3.Project(Maximum.position - transform.position, stroke)
                );
            }
        }
    }
}