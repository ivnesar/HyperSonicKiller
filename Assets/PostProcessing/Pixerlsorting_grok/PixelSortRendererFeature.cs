using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelSortRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("Material")]
        public Material material;

        [Header("Pixel Sort Parameters")]
        [Range(0f, 2f)] public float intensity = 1.0f;
        [Range(0f, 1f)] public float threshold = 0.4f;
        [Range(0f, 0.5f)] public float displacement = 0.25f;

        [Header("Center")]
        public Vector2 center = new Vector2(0.5f, 0.5f);

        [Header("Sort Mode")]
        [Tooltip("0 = Luminance\n1 = Hue (best radial bands)\n2 = Saturation")]
        [Range(0, 2)] public int sortMode = 1;
    }

    public Settings settings = new Settings();

    private PixelSortPass m_Pass;

    public override void Create()
    {
        m_Pass = new PixelSortPass(settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null)
        {
            Debug.LogWarning("PixelSortRendererFeature: Material is not assigned!");
            return;
        }
        renderer.EnqueuePass(m_Pass);
    }

    // ====================== NESTED RENDER PASS ======================
    private class PixelSortPass : ScriptableRenderPass
    {
        private readonly Settings m_Settings;
        private RTHandle m_TempRT;

        public PixelSortPass(Settings settings)
        {
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("Pixel Sort Radial");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, desc, FilterMode.Bilinear, name: "_PixelSortTempRT");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Settings.material == null) return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;

                // Update shader properties every frame
                m_Settings.material.SetFloat("_Intensity", m_Settings.intensity);
                m_Settings.material.SetFloat("_Threshold", m_Settings.threshold);
                m_Settings.material.SetFloat("_Displacement", m_Settings.displacement);
                m_Settings.material.SetVector("_Center", m_Settings.center);
                m_Settings.material.SetInt("_SortMode", m_Settings.sortMode);

                Blitter.BlitCameraTexture(cmd, source, m_TempRT, m_Settings.material, 0);
                Blitter.BlitCameraTexture(cmd, m_TempRT, source);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // RTHandle is auto-managed in Unity 6 URP
        }
    }

    protected override void Dispose(bool disposing) { }
}