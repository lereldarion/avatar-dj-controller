using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;

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
            foreach (var dj_controller in ctx.AvatarRootTransform.GetComponentsInChildren<GenerateMidiOutput>(true))
            {
                var mesh = SetupControllerMesh(dj_controller);
                ctx.AssetSaver.SaveAsset(mesh); // Required for proper upload
            }
        }

        /// <summary>
        /// uv0 (type, screen_x)
        /// uv1 type-dependent data
        /// type is the type of element, and controls what the shader code should compute.
        /// screen_x is the channel layout : [auto_offset_tag:1][controllers:127][note:127]
        /// Using float2 uv because d4rkAvatarOptimizer does not expect float4 uvs...
        /// </summary>
        internal enum ShaderElementType
        {
            Undefined = 0,
            AutoOffsetTag = 1,
            Slider = 2,
        }

        private struct Vertex
        {
            public Transform transform;
            public Vector2 uv0;
            public Vector2 uv1;
        };

        /// <summary>
        /// Create controller mesh renderer from descriptor components.
        /// Remove them from the ndmf copy, to allow d4rkAvatarOptimizer to see no reference to gameobjects and merge properly.
        /// </summary>
        /// <param name="dj_controller">Controller root component : start of search for descriptors, and location where renderer is added</param>
        /// <returns>Reference to the created mesh, to be saved as asset by ndmf</returns>
        private Mesh SetupControllerMesh(GenerateMidiOutput dj_controller)
        {
            // This is our object space
            Transform root = dj_controller.transform;

            Mesh mesh = new Mesh();

            // Triangles are consecutive triplets of vertices.
            var vertices = new List<Vertex>();

            {
                // Auto offset tag
                var uv0 = new Vector2((float)ShaderElementType.AutoOffsetTag, 0.0f);
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
                vertices.Add(new Vertex { transform = root, uv0 = uv0 });
            }

            foreach (var slider in root.GetComponentsInChildren<MidiSlider>(true))
            {
                // Slider uv1 : coefficients to compute |handle-bottom| / |top-bottom| 
                var uv0 = new Vector2((float)ShaderElementType.Slider, slider.screen_x);
                vertices.Add(new Vertex { transform = slider.bottom, uv0 = uv0, uv1 = new Vector2(-1.0f, -1.0f) });
                vertices.Add(new Vertex { transform = slider.top, uv0 = uv0, uv1 = new Vector2(0.0f, 1.0f) });
                vertices.Add(new Vertex { transform = slider.handle, uv0 = uv0, uv1 = new Vector2(1.0f, 0.0f) });
                Object.DestroyImmediate(slider);
            }

            mesh.vertices = vertices.Select(vertex => root.InverseTransformPoint(vertex.transform.position)).ToArray();
            mesh.SetUVs(0, vertices.Select(vertex => vertex.uv0).ToArray());
            mesh.SetUVs(1, vertices.Select(vertex => vertex.uv1).ToArray());
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
            renderer.material = dj_controller.controller_to_midi;

            Object.DestroyImmediate(dj_controller); // Cleanup components
            return mesh;
        }
    }
}