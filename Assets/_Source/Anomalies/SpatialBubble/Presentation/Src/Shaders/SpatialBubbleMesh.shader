Shader "RaidIntoDarkness/Anomalies/SpatialBubbleMesh"
{
    Properties
    {
        _VolumeColor("Volume Color", Color) = (0.427451, 0.533333, 0.627451, 1)
        _EnergyColor("Energy Color", Color) = (0.388235, 0.894118, 1, 1)
        _WarmColor("Warm Color", Color) = (0.792157, 0.262745, 0.262745, 1)
        _Density("Volume Density", Range(0, 3)) = 0.55
        _Intensity("Emission", Range(0, 6)) = 1.4
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimOpacity("Rim Opacity", Range(0, 1)) = 0.4
        _Warp("Surface Warp", Range(0, 0.1)) = 0.025
        _NoiseScale("BubbleNoise Scale", Range(0.1, 12)) = 3.2
        _FlowSpeed("Flow Speed", Range(0, 3)) = 0.2
        _DepthFade("Depth Fade", Range(0.1, 10)) = 2.5
        _DistortionPixels("Distortion In Pixels", Range(0, 16)) = 3
        [Header(Outer Transparency)]
        [ToggleUI] _OuterFadeEnabled("Outer Fade", Float) = 1
        _OuterFadeWidth("Outer Fade Width", Range(0, 1)) = 0.2
        _OuterFadePower("Outer Fade Power", Range(0.1, 8)) = 1.25
        _OuterFadeInsideTransition("Inside Transition", Range(0.01, 0.5)) = 0.08
        _OuterFadeShell("Shell Edge Fade", Range(0, 1)) = 1
        _OuterFadeView("Destination Edge Fade", Range(0, 1)) = 1
        _OuterFadeDistortion("Distortion Edge Fade", Range(0, 1)) = 1
        [Header(Layer Opacity)]
        _ShellOpacity("Shell Opacity", Range(0, 1)) = 1
        _DestinationOpacity("Destination Opacity", Range(0, 1)) = 1
        _DistortionOpacity("Distortion Opacity", Range(0, 1)) = 1
        [Header(Center)]
        _CoreRadius("Center Radius", Range(0, 1)) = 0.55
        _CoreSoftness("Center Softness", Range(0.001, 1)) = 0.2
        _CoreEnergyFade("Center Energy Fade", Range(0, 1)) = 1
        _CoreDistortionPixels("Center Distortion In Pixels", Range(0, 32)) = 8
        [Header(Live Edge)]
        [ToggleUI] _LiveEdgeEnabled("Live Edge", Float) = 1
        _LiveEdgeAmount("Edge Deformation", Range(0, 0.2)) = 0.055
        _LiveEdgeScale("Edge Detail", Range(1, 20)) = 6
        _LiveEdgeSpeed("Edge Speed", Range(0, 3)) = 0.45
        _LiveEdgeWidth("Edge Width", Range(0.005, 0.3)) = 0.045
        _LiveEdgeGlow("Edge Emission", Range(0, 3)) = 0.65
        [Header(Reaction)]
        [ToggleUI] _ReactionEnabled("Player And Object Reactions", Float) = 1
        _ProximityBoost("Proximity Activity", Range(0, 3)) = 0.65
        _ChargeBoost("Teleport Charge Activity", Range(0, 6)) = 1.4
        _ChargeContraction("Teleport Core Contraction", Range(0, 0.5)) = 0.2
        _ImpactCollapse("Impact Core Collapse", Range(0, 1)) = 0.8
        _ImpactBoost("Impact Activity", Range(0, 4)) = 1.2
        [Header(Waves)]
        [ToggleUI] _WavesEnabled("Traveling Waves", Float) = 1
        _WaveInterval("Wave Interval In Seconds", Range(0.5, 12)) = 3.4
        _WaveSpeed("Wave Speed", Range(0.1, 3)) = 0.8
        _WaveWidth("Wave Width", Range(0.01, 0.5)) = 0.12
        _WaveStrength("Wave Distortion In Pixels", Range(0, 16)) = 5
        _WaveEmission("Wave Emission", Range(0, 3)) = 0.3
        [Header(Destination View)]
        _DestinationViewBlend("Destination View Blend", Range(0, 1)) = 1
        _DestinationViewHeightOffset("Destination View Height Offset (Meters)", Float) = 0
        [ToggleUI] _DestinationGrainEnabled("Destination Edge Grain", Float) = 1
        _DestinationGrainStrength("Grain Strength", Range(0, 1)) = 0.12
        _DestinationGrainSize("Grain Size In Pixels", Range(1, 4)) = 1
        [ToggleUI] _DestinationChromaticAberrationEnabled("Destination Edge Chromatic Aberration", Float) = 1
        _DestinationChromaticAberrationPixels("Chromatic Aberration In Pixels", Range(0, 12)) = 2
        _DestinationEdgeStart("Destination Effects Edge Start", Range(0, 1)) = 0.55
        [HideInInspector] [NoScaleOffset] _DestinationViewTex("Destination View", 2D) = "black" {}
        [HideInInspector] _DestinationViewAvailable("Destination View Available", Float) = 0
        [HideInInspector] _DestinationViewAspect("Destination View Aspect", Float) = 1
        [HideInInspector] _DestinationViewParallax("Destination View Parallax", Float) = 0
        [HideInInspector] _BubbleProximity("Bubble Proximity", Float) = 0
        [HideInInspector] _BubbleCharge("Bubble Charge", Float) = 0
        [HideInInspector] _BubbleImpact("Bubble Impact", Float) = 0
        [HideInInspector] _BubbleFlowOffset("Bubble Flow Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _VolumeColor;
            float4 _EnergyColor;
            float4 _WarmColor;
            float _Density;
            float _Intensity;
            float _RimPower;
            float _RimOpacity;
            float _Warp;
            float _NoiseScale;
            float _FlowSpeed;
            float _DepthFade;
            float _DistortionPixels;
            float _OuterFadeEnabled;
            float _OuterFadeWidth;
            float _OuterFadePower;
            float _OuterFadeInsideTransition;
            float _OuterFadeShell;
            float _OuterFadeView;
            float _OuterFadeDistortion;
            float _ShellOpacity;
            float _DestinationOpacity;
            float _DistortionOpacity;
            float _CoreRadius;
            float _CoreSoftness;
            float _CoreEnergyFade;
            float _CoreDistortionPixels;
            float _LiveEdgeEnabled;
            float _LiveEdgeAmount;
            float _LiveEdgeScale;
            float _LiveEdgeSpeed;
            float _LiveEdgeWidth;
            float _LiveEdgeGlow;
            float _ReactionEnabled;
            float _ProximityBoost;
            float _ChargeBoost;
            float _ChargeContraction;
            float _ImpactCollapse;
            float _ImpactBoost;
            float _WavesEnabled;
            float _WaveInterval;
            float _WaveSpeed;
            float _WaveWidth;
            float _WaveStrength;
            float _WaveEmission;
            float _BubbleProximity;
            float _BubbleCharge;
            float _BubbleImpact;
            float _BubbleFlowOffset;
            float _DestinationViewBlend;
            float _DestinationViewHeightOffset;
            float _DestinationViewAvailable;
            float _DestinationViewAspect;
            float _DestinationViewParallax;
            float _DestinationGrainEnabled;
            float _DestinationGrainStrength;
            float _DestinationGrainSize;
            float _DestinationChromaticAberrationEnabled;
            float _DestinationChromaticAberrationPixels;
            float _DestinationEdgeStart;
            float4x4 _DestinationViewWorldToClip;
        CBUFFER_END

        TEXTURE2D(_DestinationViewTex);
        SAMPLER(sampler_DestinationViewTex);

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float BubbleHash(float3 position)
        {
            position = frac(position * 0.1031);
            position += dot(position, position.yzx + 33.33);
            return frac((position.x + position.y) * position.z);
        }

        float BubbleNoise(float3 position)
        {
            float3 cell = floor(position);
            float3 fraction = frac(position);
            float3 weight = fraction * fraction * (3.0 - 2.0 * fraction);
            float first = lerp(BubbleHash(cell), BubbleHash(cell + float3(1, 0, 0)), weight.x);
            float second = lerp(BubbleHash(cell + float3(0, 1, 0)), BubbleHash(cell + float3(1, 1, 0)), weight.x);
            float third = lerp(BubbleHash(cell + float3(0, 0, 1)), BubbleHash(cell + float3(1, 0, 1)), weight.x);
            float fourth = lerp(BubbleHash(cell + float3(0, 1, 1)), BubbleHash(cell + float3(1, 1, 1)), weight.x);
            return lerp(lerp(first, second, weight.y), lerp(third, fourth, weight.y), weight.z);
        }

        float BubbleField(float3 position, float time)
        {
            position *= 1.0 + (_ReactionEnabled > 0.5 ? saturate(_BubbleCharge) * 0.2 : 0.0);
            float3 flow = float3(time * 0.37, -time * 0.51, time * 0.23);
            float first = BubbleNoise(position * _NoiseScale + flow);
            float second = BubbleNoise(position.zxy * _NoiseScale * 2.07 - flow * 0.73 + 11.7);
            float third = BubbleNoise(position.yzx * _NoiseScale * 4.13 + flow * 0.41 + 27.3);
            return first * 0.57 + second * 0.29 + third * 0.14;
        }

        float BubbleActivity()
        {
            return _ReactionEnabled > 0.5 ? saturate(_BubbleProximity) * _ProximityBoost + saturate(_BubbleCharge) * _ChargeBoost + min(abs(_BubbleImpact), 1.0) * _ImpactBoost : 0.0;
        }

        float BubbleFlowTime()
        {
            return (_TimeParameters.x + (_ReactionEnabled > 0.5 ? _BubbleFlowOffset : 0.0)) * _FlowSpeed;
        }

        float BubbleCoreRadius()
        {
            float impact = _ReactionEnabled > 0.5 ? clamp(_BubbleImpact, -0.5, 1.0) : 0.0;
            float charge = _ReactionEnabled > 0.5 ? smoothstep(0.2, 1.0, saturate(_BubbleCharge)) * _ChargeContraction : 0.0;
            return max(0.0, _CoreRadius) * clamp(1.0 - impact * _ImpactCollapse - charge, 0.08, 1.25);
        }

        float BubbleCoreSoftness(float radius)
        {
            return max(0.0001, _CoreSoftness * radius / max(0.0001, _CoreRadius));
        }

        float BubbleWarpedCoreRadius(float3 position, float radius)
        {
            if (_LiveEdgeEnabled <= 0.5)
            {
                return radius;
            }
            float time = (_TimeParameters.x + (_ReactionEnabled > 0.5 ? _BubbleFlowOffset : 0.0)) * _LiveEdgeSpeed;
            float3 direction = SafeNormalize(position);
            float3 flow = float3(time * 0.31, -time * 0.19, time * 0.27);
            float noise = BubbleNoise(direction * _LiveEdgeScale + flow) * 2.0 - 1.0;
            float amount = min(0.3, _LiveEdgeAmount * (1.0 + BubbleActivity() * 0.25));
            return radius * max(0.1, 1.0 + noise * amount);
        }

        float BubbleWave(float3 position)
        {
            float radius = BubbleCoreRadius();
            if (_WavesEnabled <= 0.5 || radius <= 0.0)
            {
                return 0.0;
            }
            float interval = max(0.5, _WaveInterval);
            float3 center = GetAbsolutePositionWS(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
            float seed = BubbleHash(floor(center * 0.17)) * interval;
            float age = frac((_TimeParameters.x + seed) / interval) * interval;
            float width = max(0.01, _WaveWidth);
            float travel = age * max(0.1, _WaveSpeed) - width;
            float distance = (length(position) / max(0.0001, radius) - travel) / width;
            float envelope = smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(interval - 0.15, interval, age));
            return exp2(-distance * distance * 4.0) * envelope * (1.0 + BubbleActivity() * 0.25);
        }

        float4 BubbleCoreEmission(float4 color, float edge, float wave, float thickness, float shellVisibility)
        {
            float glow = (edge * _LiveEdgeGlow + wave * _WaveEmission) * (1.0 + BubbleActivity() * 0.35);
            float fade = saturate(thickness * _DepthFade);
            float alpha = saturate(glow * _Intensity * 0.25) * fade * shellVisibility;
            return float4(color.rgb * (1.0 - alpha) + _EnergyColor.rgb * glow * _Intensity * fade * shellVisibility, color.a * (1.0 - alpha) + alpha);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float time = BubbleFlowTime();
            float3 normalOS = normalize(input.normalOS);
            float displacement = (BubbleField(input.positionOS * 0.8, time) * 2.0 - 1.0) * _Warp;
            output.positionOS = input.positionOS + normalOS * displacement;
            output.positionWS = TransformObjectToWorld(output.positionOS);
            output.normalWS = TransformObjectToWorldNormal(normalOS);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            return output;
        }

        float BubbleVolumeRange(Varyings input, out float3 cameraOS, out float3 rayOS, out float entry, out float exit)
        {
            float2 uv = input.positionCS.xy * _ScreenSize.zw;
            float3 nearWS = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
            cameraOS = TransformWorldToObject(nearWS);
            rayOS = normalize(input.positionOS - cameraOS);
            float radius = max(0.001, length(input.positionOS));
            float projection = dot(cameraOS, rayOS);
            float discriminant = projection * projection - dot(cameraOS, cameraOS) + radius * radius;
            clip(discriminant);
            float distance = sqrt(max(0.0, discriminant));
            entry = max(0.0, -projection - distance);
            exit = max(0.0, -projection + distance);
            float depth = LoadCameraDepth(uint2(input.positionCS.xy));
            if (depth != UNITY_RAW_FAR_CLIP_VALUE)
            {
                float3 sceneWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 sceneOS = TransformWorldToObject(sceneWS);
                exit = min(exit, dot(sceneOS - cameraOS, rayOS));
            }
            float volumeLength = exit - entry;
            clip(volumeLength - 0.00001);
            return volumeLength;
        }

        float BubbleOuterFade(Varyings input, float3 cameraOS, float3 rayOS)
        {
            if (_OuterFadeEnabled <= 0.5 || _OuterFadeWidth <= 0.0)
            {
                return 1.0;
            }
            float radius = max(0.001, length(input.positionOS));
            float3 closest = cameraOS - rayOS * dot(cameraOS, rayOS);
            float radial = saturate(length(closest) / radius);
            float fade = 1.0 - smoothstep(max(0.0, 1.0 - saturate(_OuterFadeWidth)), 1.0, radial);
            fade = pow(saturate(fade), max(0.1, _OuterFadePower));
            float3 eyeOS = TransformWorldToObject(GetCurrentViewPosition());
            float inside = 1.0 - smoothstep(1.0, 1.0 + max(0.01, _OuterFadeInsideTransition), length(eyeOS) / radius);
            return lerp(fade, 1.0, inside);
        }

        float BubbleLayerVisibility(float opacity, float edgeFade, float outerFade)
        {
            return saturate(opacity) * lerp(1.0, outerFade, saturate(edgeFade));
        }

        float BubbleCoreMask(float3 cameraOS, float3 rayOS, float entry, float exit, out float3 position, out float edge)
        {
            float distance = clamp(-dot(cameraOS, rayOS), entry, exit);
            position = cameraOS + rayOS * distance;
            edge = 0.0;
            if (_CoreRadius <= 0.0)
            {
                return 0.0;
            }
            float radius = max(0.0001, BubbleCoreRadius());
            float warpedRadius = BubbleWarpedCoreRadius(position, radius);
            float softness = BubbleCoreSoftness(radius);
            float inner = max(0.0, warpedRadius - softness);
            if (_LiveEdgeEnabled > 0.5)
            {
                float width = max(0.001, _LiveEdgeWidth * radius / max(0.0001, _CoreRadius));
                float edgeDistance = abs(length(position) - max(0.0, warpedRadius - softness * 0.5));
                edge = 1.0 - smoothstep(width * 0.35, width, edgeDistance);
            }
            return 1.0 - smoothstep(inner, warpedRadius, length(position));
        }

        float2 BubbleCoreDistortion(float3 position, float core, float thickness, float time)
        {
            if (_CoreRadius <= 0.0)
            {
                return 0.0;
            }
            float3 sourcePosition = position;
            position *= _NoiseScale * 1.8;
            float3 flow = float3(time * 0.37, -time * 0.51, time * 0.23);
            float3 noise = float3(BubbleNoise(position + flow + float3(7.1, 11.3, 3.7)), BubbleNoise(position.yzx - flow + float3(19.7, 5.4, 13.1)), BubbleNoise(position.zxy + flow.zxy + float3(2.9, 23.7, 17.3))) * 2.0 - 1.0;
            float magnitude = saturate(length(noise));
            float3 directionWS = TransformObjectToWorldDir(SafeNormalize(noise));
            float2 directionVS = TransformWorldToViewDir(directionWS).xy;
            float wave = BubbleWave(sourcePosition);
            float3 radialWS = TransformObjectToWorldDir(SafeNormalize(sourcePosition));
            float2 radialVS = TransformWorldToViewDir(radialWS).xy;
            float2 distortion = directionVS * magnitude * _CoreDistortionPixels * core * (1.0 + BubbleActivity() * 0.25);
            distortion += radialVS * wave * _WaveStrength * core;
            #if UNITY_UV_STARTS_AT_TOP
                distortion.y = -distortion.y;
            #endif
            return distortion * saturate(thickness * _DepthFade);
        }

        float4 BubbleDestinationPreview(float3 position, float3 positionWS, float2 pixel, float core, float thickness, float time, float outerFade)
        {
            if (_DestinationViewAvailable <= 0.0 || _DestinationViewBlend <= 0.0 || _CoreRadius <= 0.0)
            {
                return 0.0;
            }
            float3 rightOS = SafeNormalize(TransformWorldToObjectDir(mul((float3x3)UNITY_MATRIX_I_V, float3(1.0, 0.0, 0.0)), false));
            float3 upOS = TransformWorldToObjectDir(mul((float3x3)UNITY_MATRIX_I_V, float3(0.0, 1.0, 0.0)), false);
            upOS = SafeNormalize(upOS - rightOS * dot(upOS, rightOS));
            float radius = max(0.0001, BubbleCoreRadius());
            float2 aperture = float2(dot(position, rightOS), dot(position, upOS)) / radius;
            float3 eyeOS = TransformWorldToObject(GetCurrentViewPosition());
            float inner = max(0.0, radius - BubbleCoreSoftness(radius));
            float inside = 1.0 - smoothstep(inner, radius, length(eyeOS));
            float2 screenUV = ComputeNormalizedDeviceCoordinates(positionWS, UNITY_MATRIX_VP);
            float2 uv;
            if (_DestinationViewParallax > 0.5)
            {
                uv = ComputeNormalizedDeviceCoordinates(GetAbsolutePositionWS(positionWS), _DestinationViewWorldToClip);
            }
            else
            {
                uv = aperture * 0.5 + 0.5;
                float aspect = max(0.0001, _DestinationViewAspect);
                if (aspect >= 1.0)
                {
                    uv.x = (uv.x - 0.5) / aspect + 0.5;
                }
                else
                {
                    uv.y = (uv.y - 0.5) * aspect + 0.5;
                }
                uv = lerp(uv, screenUV, inside);
            }
            float distortionVisibility = BubbleLayerVisibility(_DistortionOpacity, _OuterFadeDistortion, outerFade);
            float2 distortion = BubbleCoreDistortion(position, core, thickness, time) * distortionVisibility;
            uv += ddx(uv) * distortion.x + ddy(uv) * distortion.y;
            float opacity = saturate(core * _DestinationViewBlend * _DestinationViewAvailable) * saturate(thickness * _DepthFade) * BubbleLayerVisibility(_DestinationOpacity, _OuterFadeView, outerFade);
            float3 color = SAMPLE_TEXTURE2D(_DestinationViewTex, sampler_DestinationViewTex, uv).rgb;
            float2 edgeCoordinate = lerp(aperture, screenUV * 2.0 - 1.0, inside);
            float edgeRadius = length(edgeCoordinate);
            float edgeDistance = lerp(length(position) / radius, length(screenUV * 2.0 - 1.0), inside);
            float edgeStart = min(saturate(_DestinationEdgeStart), 0.9999);
            float edge = smoothstep(edgeStart, 1.0, edgeDistance);
            if (_DestinationChromaticAberrationEnabled > 0.5 && _DestinationChromaticAberrationPixels > 0.0)
            {
                float2 separation = edgeCoordinate / max(0.0001, edgeRadius) * _DestinationChromaticAberrationPixels * edge * (1.0 + BubbleActivity() * 0.25) * distortionVisibility;
                #if UNITY_UV_STARTS_AT_TOP
                    separation.y = -separation.y;
                #endif
                float2 offset = ddx(uv) * separation.x + ddy(uv) * separation.y;
                color.r = SAMPLE_TEXTURE2D(_DestinationViewTex, sampler_DestinationViewTex, uv + offset).r;
                color.b = SAMPLE_TEXTURE2D(_DestinationViewTex, sampler_DestinationViewTex, uv - offset).b;
            }
            if (_DestinationGrainEnabled > 0.5 && _DestinationGrainStrength > 0.0)
            {
                float grain = BubbleHash(float3(floor(pixel / max(1.0, _DestinationGrainSize)), floor(_TimeParameters.x * 30.0))) * 2.0 - 1.0;
                color *= max(0.0, 1.0 + grain * _DestinationGrainStrength * edge * (1.0 + BubbleActivity() * 0.25));
            }
            return float4(color * opacity, opacity);
        }

        float4 FragVolume(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 cameraOS;
            float3 rayOS;
            float entry;
            float exit;
            float thickness = BubbleVolumeRange(input, cameraOS, rayOS, entry, exit);
            float outerFade = BubbleOuterFade(input, cameraOS, rayOS);
            float shellVisibility = BubbleLayerVisibility(_ShellOpacity, _OuterFadeShell, outerFade);
            float3 corePosition;
            float coreEdge;
            float core = BubbleCoreMask(cameraOS, rayOS, entry, exit, corePosition, coreEdge);
            float wave = BubbleWave(corePosition) * core;
            float time = BubbleFlowTime();
            float4 destination = BubbleDestinationPreview(corePosition, input.positionWS, input.positionCS.xy, core, thickness, time, outerFade);
            float visibility = saturate(1.0 - core * _CoreEnergyFade);
            if (visibility <= 0.0)
            {
                return BubbleCoreEmission(destination, coreEdge, wave, thickness, shellVisibility);
            }
            float stride = thickness / 12.0;
            float transmission = 1.0;
            float3 radiance = 0.0;
            for (int index = 0; index < 12; index++)
            {
                float3 position = cameraOS + rayOS * (entry + (index + 0.5) * stride);
                float field = BubbleField(position, time);
                float veins = pow(saturate(1.0 - abs(field - 0.52) * 9.0), 3.0);
                float boundary = smoothstep(0.1, 0.95, length(position));
                float density = _Density * (0.08 + veins * 1.9 + field * boundary * 0.18);
                float opacity = 1.0 - exp(-density * stride);
                float warm = smoothstep(0.62, 0.78, BubbleNoise(position * _NoiseScale * 0.63 + 5.4 - time * 0.17));
                float3 color = lerp(_VolumeColor.rgb, _EnergyColor.rgb, veins);
                color = lerp(color, _WarmColor.rgb, warm * 0.65);
                radiance += transmission * color * opacity;
                transmission *= 1.0 - opacity;
            }
            float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            float fresnel = pow(saturate(1.0 - abs(dot(normalize(input.normalWS), view))), _RimPower);
            float surface = BubbleField(input.positionOS * 1.27, time);
            float rim = fresnel * _RimOpacity * (0.5 + surface);
            float fade = saturate(thickness * _DepthFade);
            float alpha = saturate(1.0 - transmission + rim) * fade * visibility * shellVisibility;
            radiance += _EnergyColor.rgb * rim;
            float4 color = float4(radiance * _Intensity * fade * visibility * shellVisibility * (1.0 - destination.a) * (1.0 + BubbleActivity() * 0.35) + destination.rgb, alpha * (1.0 - destination.a) + destination.a);
            return BubbleCoreEmission(color, coreEdge, wave, thickness, shellVisibility);
        }

        float4 FragDistortion(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 cameraOS;
            float3 rayOS;
            float entry;
            float exit;
            float thickness = BubbleVolumeRange(input, cameraOS, rayOS, entry, exit);
            float time = BubbleFlowTime();
            float3 normalVS = TransformWorldToViewDir(normalize(input.normalWS));
            float field = BubbleField(input.positionOS * 1.8, time);
            float mask = saturate(thickness * _DepthFade) * (0.2 + field * 0.8);
            float2 distortion = normalVS.xy * (field * 2.0 - 1.0) * _DistortionPixels * mask;
            #if UNITY_UV_STARTS_AT_TOP
                distortion.y = -distortion.y;
            #endif
            float3 corePosition;
            float coreEdge;
            float core = BubbleCoreMask(cameraOS, rayOS, entry, exit, corePosition, coreEdge);
            distortion += BubbleCoreDistortion(corePosition, core, thickness, time);
            float outerFade = BubbleOuterFade(input, cameraOS, rayOS);
            float distortionVisibility = BubbleLayerVisibility(_DistortionOpacity, _OuterFadeDistortion, outerFade);
            return float4(distortion * distortionVisibility, distortionVisibility, 0.0);
        }
        ENDHLSL

        Pass
        {
            Name "BubbleVolume"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragVolume
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DistortionVectors"
            Tags { "LightMode" = "DistortionVectors" }
            Blend One One, One One
            BlendOp Add, Max
            Cull Front
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref 4
                WriteMask 4
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDistortion
            #pragma shader_feature _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
