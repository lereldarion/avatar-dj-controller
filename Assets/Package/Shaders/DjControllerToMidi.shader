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
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GeometryVertexData {
                float3 position_os : POSITION_OS;
                float3 normal_os : NORMAL_OS;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragmentInput {
                float4 position_cs : SV_POSITION;

                float bit_index : BIT_INDEX;
                nointerpolation uint bits : BITS;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            uniform float _VRChatMirrorMode;
            uniform float _VRChatCameraMode;
            uniform float _DJ_Enable;

            float4 screen_pixel_to_cs(float2 pixel_coord) {
                float2 screen = _ScreenParams.xy;
                float2 position_cs = (pixel_coord * 2 - screen + 1) / screen; // +1 = center of pixels
                if (_ProjectionParams.x < 0) { position_cs.y = -position_cs.y; } // https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
                return float4(position_cs, UNITY_NEAR_CLIP_VALUE, 1);
            }

            void vertex_stage(VertexData input, out GeometryVertexData output) {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position_os = input.position_os;
                output.normal_os = normalize(input.normal_os_unnormalized);
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
            }

            void draw_value_bits_as_line(inout LineStream<FragmentInput> stream, uint value, float2 pixel_position) {
                FragmentInput output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                uint bit_count = 16; // max 16 bits FIXME using 16 for debug
                uint mask = (1 << bit_count) - 1; // 2^count - 1 = 001...1 (count)
                uint bits = value & mask;
                uint checksum = countbits(bits); // 4 bits for 16 bits
                // Data : [value:16][checksum:4]10 ; 10 for calibration with post processing
                output.bits = (bits << 6) | (checksum << 2) | 2;
                bit_count = bit_count + 4 + 2;
                // Line
                output.bit_index = 0;
                output.position_cs = screen_pixel_to_cs(pixel_position);
                stream.Append(output);
                output.bit_index = bit_count;
                output.position_cs = screen_pixel_to_cs(pixel_position + float2(0, bit_count));
                stream.Append(output);
            }

            [maxvertexcount(2)]
            void geometry_stage(triangle GeometryVertexData input[3], inout LineStream<FragmentInput> stream, uint triangle_id : SV_PrimitiveID) {
                UNITY_SETUP_INSTANCE_ID(input[0]);

                // Display in small Desktop vindow when stream camera is used from within VR.
                // Ignore mirrors. Animate DJ_Enable to match IsLocal and do not break streamers.
                if(!(_VRChatCameraMode == 1 && _VRChatMirrorMode == 0 && _DJ_Enable)) { return; }

                float type = input[0].uv0.x;
                float screen_x = input[0].uv0.y;
                uint midi_value = 0;

                float3x3 positions = float3x3(input[0].position_os, input[1].position_os, input[2].position_os);

                UNITY_BRANCH
                if(type == 1) {
                    // Slider

                    // Vectors by using UV coefficients
                    float3 bottom_handle = mul(float3(input[0].uv1.x, input[1].uv1.x, input[2].uv1.x), positions);
                    float3 bottom_top =    mul(float3(input[0].uv1.y, input[1].uv1.y, input[2].uv1.y), positions);

                    float ratio = dot(bottom_handle, bottom_top) / dot(bottom_top, bottom_top);
                    midi_value = saturate(ratio) * float((1 << 16) - 1);
                } else {
                    // Discard
                    return;
                }

                draw_value_bits_as_line(stream, midi_value, float2(screen_x, 0));
            }

            fixed4 fragment_stage(FragmentInput input) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                uint bit_index = input.bit_index + 0.5; // +0.5 = compensate pixel position being centered in screen_pixel_to_cs
                bool bit = (input.bits >> bit_index) & 1;
                return bit ? fixed4(1, 1, 1, 1) : fixed4(0, 0, 0, 0);
            }
            ENDCG            
        }
    }
}