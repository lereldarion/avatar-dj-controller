#if UNITY_EDITOR
using AnimatorAsCode.V1;
using AnimatorAsCode.V1.ModularAvatar;
using nadena.dev.ndmf;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using VRC.SDK3.Dynamics.Constraint.Components;
using AnimatorAsCode.V1.VRC;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;


[assembly: ExportsPlugin(typeof(Lereldarion.LuciferDJPlugin))]

namespace Lereldarion {
    public class LuciferDJ : MonoBehaviour, IEditorOnly {
        public VRCExpressionsMenu menu_target;

        [Header("DJ Controller")]
        public VRCParentConstraint dj_controller;

        public Renderer midi_output;

        public VRCRotationConstraint potentiometer_driver;
        public VRCRotationConstraint potentiometer_hand_tracker;
        public VRCContactReceiver potentiometer_contact;
    }

    public class LuciferDJPlugin : Plugin<LuciferDJPlugin> {
        public override string DisplayName => "Lucifer DJ Animator";

        public string SystemName => "LuciferDJ";

        protected override void Configure() {
            InPhase(BuildPhase.Generating).Run(DisplayName, Generate);
        }

        private void Generate(BuildContext ctx) {
            var config = ctx.AvatarRootTransform.GetComponentInChildren<LuciferDJ>(true);
            if(config == null) { return; }

            var aac = AacV1.Create(new AacConfiguration {
                SystemName = SystemName,
                AnimatorRoot = ctx.AvatarRootTransform,
                DefaultValueRoot = ctx.AvatarRootTransform,
                AssetKey = GUID.Generate().ToString(),
                AssetContainer = ctx.AssetContainer,
                ContainerMode = AacConfiguration.Container.OnlyWhenPersistenceRequired,
                DefaultsProvider = new AacDefaultsProvider()
            });
            
            var ma_object = new GameObject(SystemName) { transform = { parent = ctx.AvatarRootTransform }};
            var ma = MaAc.Create(ma_object);
            MaacMenuItem new_installed_menu_item() {
                var menu = new GameObject { transform = { parent = ma_object.transform }};
                var installer = menu.AddComponent<ModularAvatarMenuInstaller>();
                installer.installTargetMenu = config.menu_target;
                return ma.EditMenuItem(menu);
            }
            var ctrl = aac.NewAnimatorController();

            {
                var layer = ctrl.NewLayer("DJ");

                var dj_enabled = layer.BoolParameter("DJ/Enabled");
                ma.NewParameter(dj_enabled).WithDefaultValue(false).NotSaved();
                new_installed_menu_item().Name("DJ").Toggle(dj_enabled);

                var dj_world = layer.BoolParameter("DJ/World");
                ma.NewParameter(dj_world).WithDefaultValue(false).NotSaved();
                new_installed_menu_item().Name("DJ World").Toggle(dj_world);

                var disabled = layer.NewState("Disabled");
                disabled.Drives(dj_world, false).DrivingLocally(); // Reset, 2 bools but 3 states
                disabled.WithAnimation(aac.NewClip()
                    .Toggling(config.dj_controller.gameObject, false)
                    .Animating(SetConstraintWorldFixed(config.dj_controller, false))
                );

                var enabled = layer.NewState("Enabled");
                enabled.Drives(dj_world, false).DrivingLocally();
                enabled.WithAnimation(aac.NewClip()
                    .Toggling(config.dj_controller.gameObject, true)
                    .Animating(SetConstraintWorldFixed(config.dj_controller, false))
                );

                var world = layer.NewState("World");
                world.WithAnimation(aac.NewClip()
                    .Toggling(config.dj_controller.gameObject, true)
                    .Animating(SetConstraintWorldFixed(config.dj_controller, true))
                );

                layer.AnyTransitionsTo(disabled).When(dj_enabled.IsFalse());

                // ignore Late sync <=> bed enabled+world
                disabled.TransitionsTo(enabled).When(dj_enabled.IsTrue()).And(dj_world.IsFalse());

                enabled.TransitionsTo(world).When(dj_enabled.IsTrue()).And(dj_world.IsTrue());
                world.TransitionsTo(enabled).When(dj_world.IsFalse());
            }
            // Only enable the midi shader locally
            {
                var layer = ctrl.NewLayer("DJ/MidiShader");

                var remote = layer.NewState("Remote");
                remote.WithAnimation(aac.NewClip().Animating(clip => clip.Animates(config.midi_output, "_DJ_Enable").WithOneFrame(0)));

                var local = layer.NewState("Local");
                local.WithAnimation(aac.NewClip().Animating(clip => clip.Animates(config.midi_output, "_DJ_Enable").WithOneFrame(1)));

                remote.TransitionsTo(local).When(layer.Av3().ItIsLocal());
                local.TransitionsTo(remote).When(layer.Av3().ItIsRemote());
            }
            // Potentiometer system
            {
                var layer = ctrl.NewLayer("DJ/Potentiometer");

                var dj_enabled = layer.BoolParameter("DJ/Enabled");
                var contact = layer.BoolParameter(config.potentiometer_contact.parameter);

                var disabled = layer.NewState("Disabled");
                disabled.WithAnimation(aac.NewClip()
                    .Animating(SetConstraintActive(config.potentiometer_driver, false))
                    .Animating(SetConstraintActive(config.potentiometer_hand_tracker, false))
                );

                var standby = layer.NewState("Standby");
                standby.WithAnimation(aac.NewClip()
                    .Animating(SetConstraintActive(config.potentiometer_driver, false))
                    .Animating(SetConstraintActive(config.potentiometer_hand_tracker, true))
                );

                var grabbed = layer.NewState("Grabbed");
                grabbed.WithAnimation(aac.NewClip()
                    .Animating(SetConstraintActive(config.potentiometer_driver, true))
                    .Animating(SetConstraintActive(config.potentiometer_hand_tracker, false))
                );

                disabled.TransitionsTo(standby).When(dj_enabled.IsTrue());
                layer.AnyTransitionsTo(disabled).When(dj_enabled.IsFalse());

                standby.TransitionsTo(grabbed).When(contact.IsTrue());
                grabbed.TransitionsTo(standby).When(contact.IsFalse()).And(layer.Av3().GestureRight.IsNotEqualTo(AacAv3.Av3Gesture.Fist));
            }
            ma.NewMergeAnimator(ctrl.AnimatorController, VRCAvatarDescriptor.AnimLayerType.FX);
        }

        static private System.Action<AacFlEditClip> SetConstraintWorldFixed(VRCConstraintBase constraint, bool fixed_to_world) {
            return clip => {
                clip.Animates(constraint, "FreezeToWorld").WithOneFrame(fixed_to_world ? 1 : 0);
            };
        }

        static private System.Action<AacFlEditClip> SetConstraintActive(VRCConstraintBase constraint, bool active) {
            return clip => {
                clip.Animates(constraint, "IsActive").WithOneFrame(active ? 1 : 0);
            };
        }
    }
}
#endif