using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PixelSortingRendererFeature : ScriptableRendererFeature
{
    [Header("Shader Reference")]
    public Shader pixelSortingShader;

    private Material _material;
    private PixelSortingRenderPass _pass;

    public override void Create()
    {
        if (pixelSortingShader == null) return;

        _material = new Material(pixelSortingShader);
        _pass = new PixelSortingRenderPass(_material);
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (_material == null || _pass == null) return;
        if (PixelSortingSettings.Instance == null) return;
        if (PixelSortingSettings.Instance.intensity <= 0f) return;

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (_material != null)
        {
            #if UNITY_EDITOR
            if (EditorApplication.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
            #else
            Destroy(_material);
            #endif
        }
    }
}
