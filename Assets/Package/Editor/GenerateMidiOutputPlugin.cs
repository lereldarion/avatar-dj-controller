using System.Linq;
using System.Collections.Generic;
using AnimatorAsCode.V1;
using AnimatorAsCode.V1.VRC;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

[assembly: ExportsPlugin(typeof(Lereldarion.DJ.GenerateMidiOutputPlugin))]

namespace Lereldarion.DJ
{
    public class GenerateMidiOutputPlugin : Plugin<GenerateMidiOutputPlugin>
    {
        public override string DisplayName => "Lereldarion DJ Controller: Generate Midi Output Mesh";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating).Run(DisplayName, Generate);
        }

        private void Generate(BuildContext ctx)
        {
            var aac = AacV1.Create(new AacConfiguration
            {
                SystemName = "DJ",
                AnimatorRoot = ctx.AvatarRootTransform,
                DefaultValueRoot = ctx.AvatarRootTransform,
                AssetKey = UnityEditor.GUID.Generate().ToString(),
                AssetContainer = ctx.AssetContainer,
                ContainerMode = AacConfiguration.Container.OnlyWhenPersistenceRequired,
                DefaultsProvider = new AacDefaultsProvider()
            });
            var animator_controller = aac.NewAnimatorController();
            var animator_context = new AnimatorContext
            {
                Aac = aac,
                Left = new HandAnimatorLayer(animator_controller.NewLayer("DJ/LeftHand")),
                Right = new HandAnimatorLayer(animator_controller.NewLayer("DJ/RightHand")),
            };

            foreach (var dj_controller in ctx.AvatarRootTransform.GetComponentsInChildren<GenerateMidiOutput>(true))
            {
                var mesh = SetupController(dj_controller, animator_context);
                ctx.AssetSaver.SaveAsset(mesh); // Required for proper upload
            }

            var ma_object = new GameObject("DJ_Animator") { transform = { parent = ctx.AvatarRootTransform } };
            var ma = AnimatorAsCode.V1.ModularAvatar.MaAc.Create(ma_object);
            ma.NewMergeAnimator(animator_controller, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
        }

        /// <summary>
        /// Generated mesh vertex data.
        /// Using float2 uv because avatar optimizer tools do not like float4 uvs...
        /// </summary>
        private class Vertex
        {
            /// <summary>Position and bone assignment</summary>
            public Transform transform;
            /// <summary>
            /// uv0 = (type, screen_x).
            /// type is the type of element (ShaderElementType), and controls what the shader code should compute.
            /// screen_x is the channel layout : [auto_offset_tag:1][controllers:127][note:127]
            /// </summary>
            public Vector2 uv0;
            /// <summary>uv1 type-dependent data</summary>
            public Vector2 uv1;
            /// <summary>A direction vector stored in normal slot. Will be rotated. Value here in world space.</summary>
            public Vector3 direction = Vector3.forward;
        };
        internal enum ShaderElementType
        {
            /// <summary>Unused, and ignored by shader</summary>
            Undefined = 0,
            /// <summary>Fixed pattern used to guide screen capture</summary>
            AutoOffsetTag = 1,
            /// <summary>Track a linear motion in a range, continuous</summary>
            Slider = 2,
            /// <summary>Track a linear motion in a range, but apply step() to have a binary value output</summary>
            Button = 3,
            /// <summary>Track a rotation in a range, continuous</summary>
            RotationRange = 4,
        }

        /// <summary>
        /// Create controller mesh renderer, animator layers, gameobjects from descriptor components.
        /// Remove descriptors from the ndmf copy, to allow d4rkAvatarOptimizer to see no reference to gameobjects and merge properly.
        /// </summary>
        /// <param name="dj_controller">Controller root component : start of search for descriptors, and location where renderer is added</param>
        /// <returns>Reference to the created mesh, to be saved as asset by ndmf</returns>
        private Mesh SetupController(GenerateMidiOutput dj_controller, AnimatorContext animator)
        {
            Transform root = dj_controller.transform;
            Mesh mesh = new Mesh();
            var vertices = new List<Vertex>();
            var context = new Context { Animator = animator, Controller = dj_controller, Vertices = vertices };

            {
                // Auto offset tag
                var uv0 = new Vector2((float)ShaderElementType.AutoOffsetTag, 0.0f);
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
            }

            foreach (var slider in root.GetComponentsInChildren<MidiSlider>(true)) { SetupSlider(slider, context); }
            foreach (var button in root.GetComponentsInChildren<MidiPushButton>(true)) { SetupPushButton(button, context); }
            foreach (var potentiometer in root.GetComponentsInChildren<MidiPotentiometer>(true)) { SetupPotentiometer(potentiometer, context); }

            mesh.vertices = vertices.Select(vertex => root.InverseTransformPoint(vertex.transform.position)).ToArray();
            mesh.SetUVs(0, vertices.Select(vertex => vertex.uv0).ToArray());
            mesh.SetUVs(1, vertices.Select(vertex => vertex.uv1).ToArray());
            mesh.SetNormals(vertices.Select(vertex => root.InverseTransformDirection(vertex.direction)).ToArray());
            mesh.triangles = Enumerable.Range(0, vertices.Count()).ToArray();

            Transform[] bones = vertices.Select(vertex => vertex.transform).ToArray();
            mesh.boneWeights = Enumerable.Range(0, vertices.Count()).Select(i =>
            {
                var bw = new BoneWeight();
                bw.boneIndex0 = i;
                bw.weight0 = 1;
                return bw;
            }).ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * root.localToWorldMatrix).ToArray();

            var renderer = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.material = dj_controller.MidiOutputMaterial;

            Object.DestroyImmediate(dj_controller); // Cleanup components
            return mesh;
        }

        private class Context
        {
            public AnimatorContext Animator;
            public GenerateMidiOutput Controller;
            public List<Vertex> Vertices;
            // TODO map of controller / note ids to detect collisions
        }
        private class AnimatorContext
        {
            public AacFlBase Aac;
            public HandAnimatorLayer Left;
            public HandAnimatorLayer Right;

            private int id = 0;
            public int UniqueId() { return id++; }
        }
        private class HandAnimatorLayer
        {
            public AacFlLayer Layer;
            public AacFlState Standby;
            public HandAnimatorLayer(AacFlLayer layer)
            {
                Layer = layer;
                Standby = Layer.NewState("Standby", 0, 0);
            }
        }

        /// <summary>
        /// Setup slider objects, animator, contacts.
        /// Overall setup : a linearly position constrained object tracks fingers if the finger contact is active.
        /// This is followed by a hinge physbone attracted to an inside collider on it. This physbone smooths movement and enforce range limit;
        /// The actual handle (with geometry and tracking triangle) is linearly position constrained on the physbone end.
        /// The physbone arc is aligned to the range, with a angle of 30 degrees on each side, its size and position constrained by the stroke length.
        /// </summary>
        /// <param name="slider">Slider descriptor</param>
        /// <param name="context">Data of the current DJ controller being built</param>
        /// <exception cref="System.ArgumentException"></exception>
        private void SetupSlider(MidiSlider slider, Context context)
        {
            var error = slider.ConfigurationError();
            if (error != null) { throw new System.ArgumentException($"Slider '{slider.gameObject.name}': {error}"); }

            Vector3 stroke = slider.Maximum.position - slider.Minimum.position;
            // Center of range but displaced to be on handle path.
            Vector3 stroke_center = slider.transform.position + Vector3.Project(0.5f * (slider.Minimum.position + slider.Maximum.position) - slider.transform.position, stroke);

            Axis parent_stroke_axis = slider.transform.AlignParentAxisTo(stroke);

            GameObject finger_tracker = new GameObject("Finger Tracker")
            {
                transform = {
                    parent = slider.transform.parent,
                    position = slider.transform.position,
                }
            };

            VRCPhysBoneCollider physbone_attractor = finger_tracker.AddComponent<VRCPhysBoneCollider>();
            physbone_attractor.insideBounds = true;
            physbone_attractor.shapeType = VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Sphere;
            physbone_attractor.radius = stroke.magnitude * 0.1f; // Does not matter I think

            VRCPositionConstraint finger_constraint = finger_tracker.AddComponent<VRCPositionConstraint>();
            finger_constraint.Locked = true;
            finger_constraint.AffectsPositionX = parent_stroke_axis == Axis.X;
            finger_constraint.AffectsPositionY = parent_stroke_axis == Axis.Y;
            finger_constraint.AffectsPositionZ = parent_stroke_axis == Axis.Z;
            finger_constraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource(context.Controller.LeftFinger.transform, 0f));
            finger_constraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource(context.Controller.RightFinger.transform, 0f));

            // Physbone : hinge with symmetric ranges, len = stroke, init vector is within the median plane of the stroke
            // Thus triangle of /|\, base = stroke/2 on each side, hypothenuses = stroke, height = sqrt(3)/2, total angle on hinge (top) = 30 deg 
            Vector3 physbone_axis = slider.transform.parent.AxisVector(parent_stroke_axis == Axis.Z ? Axis.Y : Axis.Z); // Physbone seems to default to hinge on X axis
            GameObject physbone_pivot = new GameObject("Physbone Pivot")
            {
                transform = {
                    parent = slider.transform.parent,
                    rotation = slider.transform.parent.rotation,
                    position = stroke_center - physbone_axis * (stroke.magnitude * 0.5f * Mathf.Sqrt(3f)),
                }
            };
            GameObject physbone_end = new GameObject("Physbone End")
            {
                transform = {
                    parent = physbone_pivot.transform,
                    rotation = slider.transform.parent.rotation,
                    position = stroke_center + physbone_axis * (stroke.magnitude * (1f - 0.5f * Mathf.Sqrt(3f))),
                }
            };
            VRCPhysBone physbone = physbone_pivot.AddComponent<VRCPhysBone>();
            physbone.allowGrabbing = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.False;
            physbone.allowPosing = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.False;
            physbone.allowCollision = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.False;
            physbone.radius = physbone_attractor.radius;
            physbone.immobile = 1f;
            physbone.limitType = VRC.Dynamics.VRCPhysBoneBase.LimitType.Hinge;
            physbone.maxAngleX = 30f;
            physbone.colliders.Add(physbone_attractor);

            VRCPositionConstraint handle_constraint = slider.gameObject.AddComponent<VRCPositionConstraint>();
            handle_constraint.Locked = true;
            handle_constraint.AffectsPositionX = parent_stroke_axis == Axis.X;
            handle_constraint.AffectsPositionY = parent_stroke_axis == Axis.Y;
            handle_constraint.AffectsPositionZ = parent_stroke_axis == Axis.Z;
            handle_constraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource(physbone_end.transform, 1f));
            handle_constraint.ActivateConstraint();

            // One contact and animator setting per hand.
            // Animator layers are one per hand, as one hand may use only one controller at a time.
            // Controllers are locked to one hand at a time using a local driven parameter.
            int id = context.Animator.UniqueId();
            void setup_hand(string side_letter, int constraint_source, HandAnimatorLayer animator, AacFlEnumIntParameter<AacAv3.Av3Gesture> gesture)
            {
                VRCContactReceiver contact = slider.gameObject.AddComponent<VRCContactReceiver>();
                contact.allowOthers = false;
                contact.allowSelf = true;
                contact.localOnly = false;
                contact.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
                contact.radius = slider.ColliderRadius;
                contact.collisionTags.Add($"FingerIndex{side_letter}");
                contact.collisionTags.Add($"FingerMiddle{side_letter}"); // More flexible ? TODO config point
                contact.parameter = $"DJ/Slider{id}/Contact{side_letter}";

                var contact_parameter = animator.Layer.BoolParameter(contact.parameter);
                var is_used_parameter = animator.Layer.BoolParameter($"DJ/Slider{id}/Used");

                var active_state = animator.Layer.NewState($"Slider{id}", 1, id);
                active_state.Drives(is_used_parameter, true);
                active_state.WithAnimation(context.Animator.Aac.NewClip()
                    .Animating(SetConstraintActive(finger_constraint, true))
                    .Animating(SetConstraintActiveSource(finger_constraint, constraint_source))
                );

                var reset_state = animator.Layer.NewState($"Slider{id} Reset", 2, id);
                reset_state.Drives(is_used_parameter, false);
                reset_state.WithAnimation(context.Animator.Aac.NewClip()
                    .Animating(SetConstraintActive(finger_constraint, false))
                );

                animator.Standby.TransitionsTo(active_state).When(contact_parameter.IsTrue()).And(is_used_parameter.IsFalse());
                active_state.TransitionsTo(reset_state).When(contact_parameter.IsFalse()).And(gesture.IsNotEqualTo(AacAv3.Av3Gesture.Fist)); // Allow keeping hold by grabbing
                reset_state.TransitionsTo(animator.Standby).Automatically();
            }
            if (slider.HandTracking != HandTrackingMode.OnlyRight) { setup_hand("L", 0, context.Animator.Left, context.Animator.Left.Layer.Av3().GestureLeft); }
            if (slider.HandTracking != HandTrackingMode.OnlyLeft) { setup_hand("R", 1, context.Animator.Right, context.Animator.Right.Layer.Av3().GestureRight); }

            // Slider triangle uv1 : coefficients to compute |handle-minimum| / |maximum-minimum| 
            var uv0 = new Vector2((float)ShaderElementType.Slider, slider.ScreenX);
            context.Vertices.Add(new Vertex { transform = slider.Minimum, uv0 = uv0, uv1 = new Vector2(-1f, -1f) });
            context.Vertices.Add(new Vertex { transform = slider.Maximum, uv0 = uv0, uv1 = new Vector2(0f, 1f) });
            context.Vertices.Add(new Vertex { transform = slider.transform, uv0 = uv0, uv1 = new Vector2(1f, 0f) });

            Object.DestroyImmediate(slider);
        }

        /// <summary>
        /// Setup push button.
        /// </summary>
        /// <param name="button">Button descriptor</param>
        /// <param name="context">Data of the current DJ controller being built</param>
        /// <exception cref="System.ArgumentException"></exception>
        private void SetupPushButton(MidiPushButton button, Context context)
        {
            var error = button.ConfigurationError();
            if (error != null) { throw new System.ArgumentException($"PushButton '{button.gameObject.name}': {error}"); }

            Transform rest = button.RestOverride;
            if (rest == null)
            {
                var rest_object = new GameObject("Rest Position")
                {
                    transform = {
                        parent = button.TriggerPoint,
                        position = button.transform.position,
                    }
                };
                rest = rest_object.transform;
            }

            // Button triangle uv1 : coefficients to compute |handle-rest| / |trigger-rest| 
            var uv0 = new Vector2((float)ShaderElementType.Button, button.ScreenX);
            context.Vertices.Add(new Vertex { transform = rest, uv0 = uv0, uv1 = new Vector2(-1f, -1f) });
            context.Vertices.Add(new Vertex { transform = button.TriggerPoint, uv0 = uv0, uv1 = new Vector2(0f, 1f) });
            context.Vertices.Add(new Vertex { transform = button.transform, uv0 = uv0, uv1 = new Vector2(1f, 0f) });

            Object.DestroyImmediate(button);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="potentiometer">Potentiometer descriptor</param>
        /// <param name="context">Data of the current DJ controller being built</param>
        /// <exception cref="System.ArgumentException"></exception>
        private void SetupPotentiometer(MidiPotentiometer potentiometer, Context context)
        {
            var error = potentiometer.ConfigurationError();
            if (error != null) { throw new System.ArgumentException($"Potentiometer '{potentiometer.gameObject.name}': {error}"); }

            var directions = potentiometer.GetDirections();
            // FIXME use when building the animator 
            // Axis parent_rotation_axis = potentiometer.transform.AlignParentAxisTo(directions.RotationAxis);
            // TODO if we want to mutualise the hand rotation tracker objects, we need to defer creating the animations to the end...

            // Potentiometer triangle : using vectors to encode base plane (x, y) and cursor
            var uv0 = new Vector2((float)ShaderElementType.RotationRange, potentiometer.ScreenX);
            float range_rad = Mathf.Deg2Rad * (potentiometer.MinimumLocation + potentiometer.MaximumLocation);
            Vector3 base_plane_x = directions.Minimum;
            Vector3 base_plane_y = Quaternion.AngleAxis(90f, directions.RotationAxis) * directions.Minimum;
            context.Vertices.Add(new Vertex { transform = potentiometer.transform.parent, uv0 = uv0, uv1 = new Vector2(0f, range_rad), direction = base_plane_x });
            context.Vertices.Add(new Vertex { transform = potentiometer.transform.parent, uv0 = uv0, uv1 = new Vector2(1f, range_rad), direction = base_plane_y });
            context.Vertices.Add(new Vertex { transform = potentiometer.transform, uv0 = uv0, uv1 = new Vector2(2f, range_rad), direction = directions.Cursor });

            Object.DestroyImmediate(potentiometer);
        }

        static private System.Action<AacFlEditClip> SetConstraintActive(VRC.Dynamics.VRCConstraintBase constraint, bool active)
        {
            return clip => { clip.Animates(constraint, "IsActive").WithOneFrame(active ? 1 : 0); };
        }

        static private System.Action<AacFlEditClip> SetConstraintActiveSource(VRC.Dynamics.VRCConstraintBase constraint, int active_source)
        {
            return clip =>
            {
                for (int i = 0; i < constraint.Sources.Count; i += 1)
                {
                    clip.Animates(constraint, $"Sources.source{i}.Weight").WithOneFrame(i == active_source ? 1 : 0);
                }
            };
        }
    }

    public static class Extensions
    {
        public static Vector3 AxisVector(this Transform transform, Axis Axis)
        {
            return Axis.GetVector(transform);
        }

        public static Axis? AxisMatchingDirection(this Transform transform, Vector3 direction)
        {
            direction.Normalize();
            if (System.Math.Abs(Vector3.Dot(direction, transform.right)) > 0.999) { return Axis.X; }
            if (System.Math.Abs(Vector3.Dot(direction, transform.up)) > 0.999) { return Axis.Y; }
            if (System.Math.Abs(Vector3.Dot(direction, transform.forward)) > 0.999) { return Axis.Z; }
            return null;
        }

        /// <summary>
        /// Force parent transform to have one axis aligned to direction.
        /// Insert a parent if needed.
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="direction"></param>
        /// <returns>Ths aligned axis</returns>
        public static Axis AlignParentAxisTo(this Transform transform, Vector3 direction)
        {
            var axis = transform.parent.AxisMatchingDirection(direction);
            if (axis.HasValue)
            {
                return axis.Value;
            }
            else
            {
                // Parent does not fit, insert aligned object.
                var aligned_parent = new GameObject("Axis Aligned Parent")
                {
                    transform = {
                        parent = transform.parent,
                        position = transform.position
                    }
                };
                aligned_parent.transform.LookAt(transform.position + direction);
                transform.SetParent(aligned_parent.transform, true);
                return Axis.Z; // LookAt aligns Z
            }
        }
    }
}