using UnityEngine;

public class TextureSequencePlayer : MonoBehaviour
{
    [Header("Frames")]
    [Tooltip("Ziehe hier alle Einzelbilder rein, in der richtigen Reihenfolge.")]
    public Texture2D[] frames;

    [Header("Einstellungen")]
    [Tooltip("Bilder pro Sekunde")]
    public float fps = 12f;

    // Interner Zustand
    private Renderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private int currentFrame = 0;
    private float timer = 0f;

    void Start()
    {
        meshRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        if (frames == null || frames.Length == 0)
        {
            Debug.LogWarning("TextureSequencePlayer: Keine Frames zugewiesen!", this);
            enabled = false;
        }
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Wann ist es Zeit für den nächsten Frame?
        float frameDuration = 1f / fps;

        if (timer >= frameDuration)
        {
            timer -= frameDuration;

            // Nächsten Frame setzen (mit Loop)
            currentFrame = (currentFrame + 1) % frames.Length;

            // Textur auf dem Material wechseln
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture("_BaseMap", frames[currentFrame]);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}