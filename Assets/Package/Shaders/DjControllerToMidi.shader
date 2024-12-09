// Extract the Midi values of various DJ controller elements, and display them encoded to pixels.
// Each DJ controller item must define a triangle with specific skinning setup and uv metadata for this shader to properly infer the controller value.
// Each item is output as a line of pixels encoding one bit each ; this is to protect from noise and post-processing.

// Initial idea was to use the corners of the desktop window while in VR.
// Sadly this window is just a resized+cropped version of the raw left eye render in normal mode.
// Thus pixels are blurred, coordinates are unreliable (especially corners), and this would be visible in VR (except if using alpha ?).
// For now try using the stream camera, which should have its own non-resized render. Set it to playerlocal only to avoid performance issues.
Shader "Lereldarion/DjControllerToMidi" {
    Properties {
        [ToggleUI] _DJ_Enable("Enable output pixels", Float) = 1
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "Overlay" // After mostly everything
            "VRCFallback" = "Hidden"
            "IgnoreProjector" = "True"
        }

        Pass {
            Name "ControllerToMidi"

            Cull Off
            ZWrite On
            ZTest Always

            CGPROGRAM
            #pragma require geometry
            #pragma multi_compile_instancing

            #pragma vertex vertex_stage
            #pragma geometry geometry_stage
            #pragma fragment fragment_stage

            #include "UnityCG.cginc"

            struct VertexData {
                float3 position_os : POSITION;
                float3 normal_os_unnormalized : NORMAL; // May be not normalized after skinning
                float4 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GeometryVertexData {
                float3 position_os : POSITION_OS;
                float3 normal_os : NORMAL_OS;
                float4 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragmentInput {
                float4 position_cs : SV_POSITION;
                float pixel : PIXEL; // Pixel position in controller item line
                nointerpolation uint midi_value : MIDI_VALUE; // 7 bits

                UNITY_VERTEX_OUTPUT_STEREO
            };

            uniform float _VRChatMirrorMode;
            uniform float _VRChatCameraMode;
            uniform float _DJ_Enable;

            float4 screen_pixel_to_cs(float2 pixel_coord) {
                float4 pos = float4(pixel_coord / _ScreenParams.xy, UNITY_NEAR_CLIP_VALUE, 1);
                pos.xy = pos.xy * 2.0 - 1.0; // To [-1, 1]
                if (_ProjectionParams.x < 0) { pos.y = -pos.y; } // https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
                return pos;
            }

            void vertex_stage(VertexData input, out GeometryVertexData output) {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position_os = input.position_os;
                output.normal_os = normalize(input.normal_os_unnormalized);
                output.uv0 = input.uv0;
            }

            [maxvertexcount(2)]
            void geometry_stage(triangle GeometryVertexData input[3], inout LineStream<FragmentInput> stream, uint triangle_id : SV_PrimitiveID) {
                UNITY_SETUP_INSTANCE_ID(input[0]);

                // Display in small Desktop vindow when stream camera is used from within VR.
                // Ignore mirrors. Animate DJ_Enable to match IsLocal and do not break streamers.
                if(!(_VRChatCameraMode == 1 && _VRChatMirrorMode == 0 && _DJ_Enable)) { return ; }

                uint midi_value = triangle_id * 127; // TODO compute for various cases

                FragmentInput output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.midi_value = midi_value;

                output.pixel = 0;
                output.position_cs = screen_pixel_to_cs(float2(triangle_id, output.pixel)); stream.Append(output);
                output.pixel = 2 + 7;
                output.position_cs = screen_pixel_to_cs(float2(triangle_id, output.pixel)); stream.Append(output);
            }

            fixed4 fragment_stage(FragmentInput input) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // [7 midi bits]10
                // The 10 is used to calibrate decoder, which works on pixels after post-processing
                uint bit_mask = (input.midi_value << 2) | 2;
                
                uint pixel = input.pixel;
                bool bit = (bit_mask >> pixel) & 1;
                return bit ? fixed4(1, 1, 1, 1) : fixed4(0, 0, 0, 0);
            }
            ENDCG            
        }
    }
}