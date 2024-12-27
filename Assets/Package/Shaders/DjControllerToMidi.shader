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
        [ToggleUI] _DJ_Debug("Force output on", Float) = 0
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
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 direction_os : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GeometryVertexData {
                float3 position_os : POSITION_OS;
                float3 direction_os : DIRECTION_OS;
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
            uniform float _DJ_Debug;

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
                output.direction_os = normalize(input.direction_os); // May be not normalized after skinning, or never normalized in the generated mesh at all.
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
            }

            void draw_bit_pattern_as_line(inout LineStream<FragmentInput> stream, uint bits, uint bit_count, float2 pixel_position) {
                FragmentInput output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.bits = bits;

                output.bit_index = 0;
                output.position_cs = screen_pixel_to_cs(pixel_position);
                stream.Append(output);
                output.bit_index = bit_count;
                output.position_cs = screen_pixel_to_cs(pixel_position + float2(0, bit_count));
                stream.Append(output);
            }
            void draw_midi_value_01_as_bit_line(inout LineStream<FragmentInput> stream, float value_01, float2 pixel_position) {
                uint bit_count = 7;
                uint mask = (1 << bit_count) - 1; // 2^count - 1 = 001...1 (count)
                uint bits = round(saturate(value_01) * float(mask));

                // Data : [~value_bits:7][value_bits:7]10 ; 10 for calibration with post processing
                bits = ((~bits) << (bit_count + 2)) | (bits << 2) | 2;
                bit_count = 2 * bit_count + 2;
                draw_bit_pattern_as_line(stream, bits, bit_count, pixel_position);
            }

            float3 select_row(float3x3 m, float3 row_keys, float key) {
                bool3 selected = row_keys == key;
                if(selected[0]) { return m[0]; }
                if(selected[1]) { return m[1]; }
                return m[2];
            }

            static const float pi = 3.14159265358;

            [maxvertexcount(2)]
            void geometry_stage(triangle GeometryVertexData input[3], inout LineStream<FragmentInput> stream, uint triangle_id : SV_PrimitiveID) {
                UNITY_SETUP_INSTANCE_ID(input[0]);

                // Display in small Desktop vindow when stream camera is used from within VR.
                // Ignore mirrors. Animate DJ_Enable to match IsLocal and do not break streamers.
                if(!((_VRChatCameraMode == 1 || _DJ_Debug > 0.5) && _VRChatMirrorMode == 0 && _DJ_Enable > 0.5)) { return; }

                float type = round(input[0].uv0.x);
                float screen_x = input[0].uv0.y;
                float output_01 = 0;

                float3x3 positions = float3x3(input[0].position_os, input[1].position_os, input[2].position_os);
                float3x3 directions = float3x3(input[0].direction_os, input[1].direction_os, input[2].direction_os);

                switch(type) {
                    case 1: {
                        // Auto offset tag
                        draw_bit_pattern_as_line(stream, 0xAAAA, 16, float2(0, 0));
                        return;
                    }
                    case 2: {
                        // Slider
                        float3 min_handle = mul(float3(input[0].uv1.x, input[1].uv1.x, input[2].uv1.x), positions);
                        float3 min_max    = mul(float3(input[0].uv1.y, input[1].uv1.y, input[2].uv1.y), positions);
                        output_01 = dot(min_handle, min_max) / dot(min_max, min_max);
                        break;
                    }
                    case 3: {
                        // Button
                        float3 rest_handle  = mul(float3(input[0].uv1.x, input[1].uv1.x, input[2].uv1.x), positions);
                        float3 rest_trigger = mul(float3(input[0].uv1.y, input[1].uv1.y, input[2].uv1.y), positions);
                        float ratio_to_trigger = dot(rest_handle, rest_trigger) / dot(rest_trigger, rest_trigger);
                        output_01 = step(1, ratio_to_trigger);
                        break;
                    }
                    case 4: {
                        // Rotation range
                        float angle_range_rad = input[0].uv1.y; // Same value in all 3 slots
                        float3 ids = float3(input[0].uv1.x, input[1].uv1.x, input[2].uv1.x);
                        float3 plane_x = select_row(directions, ids, 0);
                        float3 plane_y = select_row(directions, ids, 1);
                        float3 cursor = select_row(directions, ids, 2);
                        // [-pi, pi], 0 is minimum direction
                        float angle = atan2(dot(plane_y, cursor), dot(plane_x, cursor));
                        // Translate to [0, range]. Place discontinuity at middle of unused range.
                        if(angle < angle_range_rad / 2 - pi) { angle += 2 * pi; }
                        output_01 = angle / angle_range_rad;
                        break;
                    }
                    default: return;
                }
                draw_midi_value_01_as_bit_line(stream, output_01, float2(screen_x, 0));
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