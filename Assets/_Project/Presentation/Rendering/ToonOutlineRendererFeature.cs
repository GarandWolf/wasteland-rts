using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Full-screen depth/normal edge detection. Add this to
    /// <c>WastelandURP_Renderer</c> after the URP assets exist.
    /// Implements URP 17 render graph; <see cref="Execute"/> is kept for
    /// compatibility mode.
    /// </summary>
    public sealed class ToonOutlineRendererFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "Wasteland/ToonOutline";

        [SerializeField] private Shader _shader;
        [SerializeField] private RenderPassEvent _injection = RenderPassEvent.BeforeRenderingPostProcessing;
        [SerializeField] [Range(0.1f, 1f)] private float _outlineMultiply = 0.35f;
        [SerializeField] private Color _fallbackColor = new Color(0.12f, 0.11f, 0.10f, 1f);
        [SerializeField] private float _depthThreshold = 0.003f;
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
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || renderingData.cameraData.cameraType != CameraType.Game)
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

            _pass.Setup(_material);
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

            public void Setup(Material material)
            {
                _material = material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle source = resources.activeColorTexture;
                TextureDesc destDesc = renderGraph.GetTextureDesc(source);
                destDesc.name = "_WastelandToonOutline";
                destDesc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(destDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "Wasteland Toon Outline",
                           out PassData passData))
                {
                    passData.Source = source;
                    passData.Material = _material;
                    builder.UseTexture(source, AccessFlags.Read);
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
                if (_material == null)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get("Wasteland Toon Outline");
                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, source, source, _material, 0);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
