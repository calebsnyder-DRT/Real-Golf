using Puttshack.Runtime.Course;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Puttshack.Runtime.Rendering
{
    /// <summary>
    /// Exponential height fog with sun in-scattering, drawn full screen from the depth texture before post-processing
    /// (Shaders/HeightFog.shader). Parameters are shader globals pushed by <see cref="CourseAtmosphere"/>; the pass only
    /// runs while one is active, and only for the gameplay camera and the Scene view, so portrait and UI cameras on the
    /// same renderer stay clear.
    /// </summary>
    public sealed class HeightFogFeature : ScriptableRendererFeature
    {
        public Material Material;
        public RenderPassEvent Event = RenderPassEvent.BeforeRenderingPostProcessing;

        private HeightFogPass _pass;

        public override void Create()
        {
            _pass = new HeightFogPass { renderPassEvent = Event };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Material == null || !CourseAtmosphere.FogActive) return;
            Camera camera = renderingData.cameraData.camera;
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Game ? !camera.CompareTag("MainCamera") : type != CameraType.SceneView) return;
            _pass.Material = Material;
            renderer.EnqueuePass(_pass);
        }

        private sealed class HeightFogPass : ScriptableRenderPass
        {
            public Material Material;

            private sealed class PassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_HeightFogColor";
                desc.clearBuffer = false;
                TextureHandle target = renderGraph.CreateTexture(desc);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Height Fog", out PassData data))
                {
                    data.Source = source;
                    data.Material = Material;
                    builder.UseTexture(source);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(target, 0);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
                }
                resources.cameraColor = target;
            }
        }
    }
}
