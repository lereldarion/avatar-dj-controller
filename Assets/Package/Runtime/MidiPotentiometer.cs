using UnityEngine;

namespace Lereldarion.DJ
{
    /// <summary>
    /// Declare a potentiometer control which output its angle to 0-127.
    /// Movable handle is self. Parent if the base (overrideable).
    /// This component will generate all needed gameobjects, contacts, animator support for interaction.
    /// </summary>
    [DisallowMultipleComponent]
    public class MidiPotentiometer : MidiController
    {
        [Tooltip("Radius of the finger collider")]
        public float ColliderRadius = 0.015f;
        [Tooltip("Potentiometer will rotate around this axis")]
        public Axis RotationAxis = Axis.Z;
        public bool FlipRotationAxis = false;
        [Tooltip("Location of the potentiometer cursor (yellow ray)")]
        [Range(-180f, 180f)]
        public float CursorLocation = 0f;
        [Tooltip("Location of minimum (value = 0) range of the potentiometer (red ray)")]
        [Range(0f, 360f)]
        public float MinimumLocation = 145f;
        [Tooltip("Location of maximum (value = 127) range of the potentiometer (green ray)")]
        [Range(0f, 360f)]
        public float MaximumLocation = 145f;
        [Tooltip("Fixed base transform ; defaults to using self.parent")]
        public Transform BaseOverride;
        [Tooltip("Optionally restrict tracking to one hand only (reduces contact count)")]
        public HandTrackingMode HandTracking = HandTrackingMode.Both;

        public string ConfigurationError()
        {
            if (BaseOverride != null && BaseOverride.IsChildOf(transform)) { return "Base override must be static with respect to handle"; }
            if (ColliderRadius <= 0.0f) { return "Collider Radius must be strictly positive"; }
            if (MinimumLocation + MaximumLocation > 360f) { return "Rotation range exceeds 360 degrees"; }
            return null;
        }

        public struct Directions
        {
            public Vector3 RotationAxis;
            public Vector3 Cursor;
            public Vector3 Minimum;
            public Vector3 Maximum;
        }
        public Directions GetDirections()
        {
            Vector3 rotation_axis = RotationAxis.GetVector(transform);
            if(FlipRotationAxis) { rotation_axis = -rotation_axis; }
            Vector3 reference_axis = (RotationAxis != Axis.Z ? Axis.Z : Axis.Y).GetVector(transform);
            return new Directions
            {
                RotationAxis = rotation_axis,
                Cursor = Quaternion.AngleAxis(CursorLocation, rotation_axis) * reference_axis,
                Minimum = Quaternion.AngleAxis(CursorLocation - MinimumLocation, rotation_axis) * reference_axis,
                Maximum = Quaternion.AngleAxis(CursorLocation + MaximumLocation, rotation_axis) * reference_axis,
            };
        }

        public void OnDrawGizmosSelected()
        {
            if (ColliderRadius > 0)
            {
                float radius = ColliderRadius * transform.lossyScale.x;

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, radius);

                var directions = GetDirections();
                Gizmos.DrawLine(transform.position, transform.position + directions.Cursor * radius);
                void draw_arc_from_cursor(float angle, int segments)
                {
                    var points = new Vector3[segments + 1];
                    Vector3 direction = directions.Cursor * radius * 0.9f;
                    Quaternion step_rotation = Quaternion.AngleAxis(angle / segments, directions.RotationAxis);
                    for(int i = 0; i <= segments; i += 1) {
                        points[i] = transform.position + direction;
                        direction = step_rotation * direction;
                    }
                    Gizmos.DrawLineStrip(points, false);
                }
                draw_arc_from_cursor(-MinimumLocation, 10);
                draw_arc_from_cursor(+MaximumLocation, 10);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, transform.position + directions.Minimum * radius);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, transform.position + directions.Maximum * radius);
            }
        }
    }
}