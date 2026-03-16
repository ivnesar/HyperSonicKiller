using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class PixelSortingRenderPass : ScriptableRenderPass
{
    // ── Shader Property IDs ────────────────────────────────────
    private static readonly int ID_SortDirection = Shader.PropertyToID("_SortDirection");
    private static readonly int ID_ThresholdMin  = Shader.PropertyToID("_ThresholdMin");
    private static readonly int ID_ThresholdMax  = Shader.PropertyToID("_ThresholdMax");
    private static readonly int ID_Intensity     = Shader.PropertyToID("_Intensity");
    private static readonly int ID_SortCriterion = Shader.PropertyToID("_SortCriterion");
    private static readonly int ID_StepSize      = Shader.PropertyToID("_StepSize");
    private static readonly int ID_PassCount     = Shader.PropertyToID("_PassCount");
    private static readonly int ID_DebugMode     = Shader.PropertyToID("_DebugMode");

    private readonly Material _material;

    // ── PassData for Render Graph ──────────────────────────────
    private class PassData
    {
        internal TextureHandle source;
        internal Material material;
    }

    public PixelSortingRenderPass(Material material)
    {
        _material = material;
    }

    private void UpdateMaterial()
    {
        var s = PixelSortingSettings.Instance;
        if (s == null || _material == null) return;

        float angleRad = s.sortAngle * Mathf.Deg2Rad;
        _material.SetVector(ID_SortDirection,
            new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)));
        _material.SetFloat(ID_ThresholdMin, s.thresholdMin);
        _material.SetFloat(ID_ThresholdMax, s.thresholdMax);
        _material.SetFloat(ID_Intensity, s.intensity);
        _material.SetFloat(ID_SortCriterion, (float)s.sortCriterion);
        _material.SetFloat(ID_StepSize, s.sortStepSize);
        _material.SetFloat(ID_PassCount, s.sortPasses);
        _material.SetFloat(ID_DebugMode, (float)s.debugMode);
    }

    // ── Execute callback – actual rendering commands ───────────
    private static void ExecutePass(PassData data, RasterGraphContext context)
    {
        // Blitter.BlitTexture draws a fullscreen triangle using the material.
        // The source TextureHandle is set as _BlitTexture by the Blitter API.
        Blitter.BlitTexture(
            context.cmd,
            data.source,
            new Vector4(1f, 1f, 0f, 0f),  // scaleBias
            data.material,
            0                               // shader pass index
        );
    }

    public override void RecordRenderGraph(RenderGraph renderGraph,
        ContextContainer frameData)
    {
        if (PixelSortingSettings.Instance == null || _material == null) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        if (resourceData.isActiveTargetBackBuffer)
            return;

        TextureHandle source = resourceData.cameraColor;
        if (!source.IsValid()) return;

        // ── Create destination texture matching source ─────
        var desc = renderGraph.GetTextureDesc(source);
        desc.name = "_PixelSortDest";
        desc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(desc);

        // ── Push settings to material ──────────────────────
        UpdateMaterial();

        // ── Add a raster render pass ───────────────────────
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(
            "PixelSortPass", out var passData))
        {
            passData.source = source;
            passData.material = _material;

            // Declare source as read input
            builder.UseTexture(source);

            // Set destination as the render target (color attachment 0)
            builder.SetRenderAttachment(destination, 0);

            // Keep the pass even if nothing reads destination yet
            builder.AllowPassCulling(false);

            // Set the execute function
            builder.SetRenderFunc(
                static (PassData data, RasterGraphContext ctx) => ExecutePass(data, ctx)
            );
        }

        // ── Swap camera color to our destination ───────────
        resourceData.cameraColor = destination;
    }
}
