// Indirect diffuse for the course shaders. With the pipeline's light probe system set to Adaptive Probe Volumes the
// course gets its baked sky and sun bounce from the probe volume; outside the volume (the Pro Shop) and before a bake
// URP falls back to the skybox ambient probe, which is all the legacy path ever sampled.
#ifndef PUTTSHACK_COURSE_GI_INCLUDED
#define PUTTSHACK_COURSE_GI_INCLUDED

half3 CourseBakedGI(float3 positionWS, half3 normalWS, half3 viewDirectionWS, float2 positionSS)
{
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    return SampleProbeVolumePixel(half3(0, 0, 0), GetAbsolutePositionWS(positionWS), normalWS, viewDirectionWS, positionSS);
#else
    return SampleSH(normalWS);
#endif
}

#endif
