using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RadialPixelSortFeature : ScriptableRendererFeature
{
    public enum SortCriteria { Luminance = 0, Hue = 1, Saturation = 2 }

    [System.Serializable]
    public class SortSettings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public Shader shader;
        
        [Header("Sorting Parameters")]
        public SortCriteria criteria = SortCriteria.Luminance;
        [Range(0.001f, 0.1f), Tooltip("How far outward the sorting stretches.")]
        public float spread = 0.05f;
        [Range(0f, 1f), Tooltip("Pixels below this value won't sort.")]
        public float threshold = 0.2f;
        public Vector2 center = new Vector2(0.5f, 0.5f);
    }

    public SortSettings settings = new SortSettings();
    private RadialPixelSortPass customPass;

    public override void Create()
    {
        if (settings.shader == null) return;
        
        Material material = CoreUtils.CreateEngineMaterial(settings.shader);
        customPass = new RadialPixelSortPass(material, settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.shader == null || customPass == null)
        {
            Debug.LogWarning("Radial Pixel Sort: Shader missing.");
            return;
        }

        // Only run for the game/scene cameras
        if (renderingData.cameraData.cameraType == CameraType.Game || renderingData.cameraData.cameraType == CameraType.SceneView)
        {
            renderer.EnqueuePass(customPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        customPass?.Dispose();
    }
    
    // --- The Render Pass ---
    class RadialPixelSortPass : ScriptableRenderPass
    {
        private Material material;
        private SortSettings settings;
        private RTHandle tempTexture;

        private static readonly int CenterID = Shader.PropertyToID("_Center");
        private static readonly int SpreadID = Shader.PropertyToID("_Spread");
        private static readonly int SortCriteriaID = Shader.PropertyToID("_SortCriteria");
        private static readonly int ThresholdID = Shader.PropertyToID("_Threshold");

        public RadialPixelSortPass(Material mat, SortSettings settings)
        {
            this.material = mat;
            this.settings = settings;
            this.renderPassEvent = settings.renderPassEvent;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // Color only
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc, name: "_TempPixelSortTexture");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("Radial Pixel Sort");
            
            // Pass properties to shader
            material.SetVector(CenterID, settings.center);
            material.SetFloat(SpreadID, settings.spread);
            material.SetInt(SortCriteriaID, (int)settings.criteria);
            material.SetFloat(ThresholdID, settings.threshold);

            var cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            // Blit from camera target -> temp texture -> back to camera target
            Blitter.BlitCameraTexture(cmd, cameraTarget, tempTexture, material, 0);
            Blitter.BlitCameraTexture(cmd, tempTexture, cameraTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            tempTexture?.Release();
        }
    }
}