using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Full-screen depth/normal edge detection. Add this to
    /// <c>WastelandURP_Renderer</c> after the URP assets exist.
    /// The 5k cost is the DepthNormals prepass (VAT sampled again), not the blit.
    /// </summary>
    public sealed class ToonOutlineRendererFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "Wasteland/ToonOutline";

        /// <summary>Full-screen blit. Independent of the DepthNormals prepass.</summary>
        public static bool BlitEnabled = true;

        /// <summary>
        /// Requests URP's DepthNormals prepass via <see cref="ScriptableRenderPass.ConfigureInput"/>.
        /// That geometry pass is the expensive line at 5k VAT units.
        /// </summary>
        public static bool DepthNormalsPrepassEnabled = true;

        [SerializeField] private Shader _shader;
        [SerializeField] private RenderPassEvent _injection = RenderPassEvent.BeforeRenderingPostProcessing;
        [SerializeField] [Range(0.1f, 1f)] private float _outlineMultiply = 0.35f;
        [SerializeField] private Color _fallbackColor = new Color(0.12f, 0.11f, 0.10f, 1f);
        [SerializeField] private float _depthThreshold = 0.4f;
        [SerializeField] private float _normalThreshold = 0.35f;
        [SerializeField] private float _outlineWidth = 1.5f;

        private Material _material;
        private ToonOutlinePass _pass;

        public override void Create()
        {
            if (_shader == null)
            {
                _shader = Shader.Find(ShaderName);
            }

            if (_shader != null && _material == null)
            {
                _material = CoreUtils.CreateEngineMaterial(_shader);
            }

            _pass = new ToonOutlinePass
            {
                renderPassEvent = _injection
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || renderingData.cameraData.cameraType != CameraType.Game)
            {
                return;
            }

            if (!BlitEnabled && !DepthNormalsPrepassEnabled)
            {
                return;
            }

            _material.SetColor("_OutlineColor", _fallbackColor);
            _material.SetFloat("_OutlineMultiply", _outlineMultiply);
            _material.SetFloat("_DepthThreshold", _depthThreshold);
            _material.SetFloat("_NormalThreshold", _normalThreshold);
            _material.SetFloat("_OutlineWidth", _outlineWidth);

            Camera camera = renderingData.cameraData.camera;
            if (camera != null)
            {
                _material.SetFloat("_CameraHeight", camera.transform.position.y);
            }

            ScriptableRenderPassInput inputs = ScriptableRenderPassInput.Color;
            if (DepthNormalsPrepassEnabled)
            {
                inputs |= ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            }

            _pass.ConfigureInput(inputs);
            _pass.Setup(_material, BlitEnabled);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (_material != null)
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }
        }

        private sealed class PassData
        {
            public TextureHandle Source;
            public Material Material;
        }

        private sealed class ToonOutlinePass : ScriptableRenderPass
        {
            private Material _material;
            private bool _blitEnabled;

            public void Setup(Material material, bool blitEnabled)
            {
                _material = material;
                _blitEnabled = blitEnabled;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || !_blitEnabled)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                TextureHandle source = resources.activeColorTexture;
                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    descriptor,
                    "_WastelandToonOutline",
                    false,
                    FilterMode.Bilinear);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "Wasteland Toon Outline",
                           out PassData passData))
                {
                    passData.Source = source;
                    passData.Material = _material;
                    builder.UseTexture(source, AccessFlags.Read);

                    TextureHandle depth = resources.cameraDepthTexture;
                    TextureHandle normals = resources.cameraNormalsTexture;
                    if (depth.IsValid())
                    {
                        builder.UseTexture(depth, AccessFlags.Read);
                    }

                    if (normals.IsValid())
                    {
                        builder.UseTexture(normals, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1f, 1f, 0f, 0f), data.Material, 0);
                    });
                }

                resources.cameraColor = destination;
            }

            [System.Obsolete]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || !_blitEnabled)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get("Wasteland Toon Outline");
                RTHandle color = renderingData.cameraData.renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, color, color, _material, 0);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
