using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Cosmetic pseudo-code feed for the left and right screen edges.
/// Left side reacts to relevant keyboard gameplay input.
/// Right side reacts to mouse gameplay input (LMB/RMB actions only, not mouse movement).
/// </summary>
public class PseudoCodeHUD : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector References
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player Reference")]
    [SerializeField] private PlayerCore player;

    [Header("UI References")]
    [SerializeField] private Transform keyboardCodeContainer;
    [SerializeField] private Transform mouseCodeContainer;
    [SerializeField] private TextMeshProUGUI codeLinePrefab;

    [Header("Behaviour")]
    [SerializeField] private bool spawnWhileHeld = true;
    [SerializeField] private float keyboardSpawnInterval = 0.10f;
    [SerializeField] private float mouseSpawnInterval = 0.10f;
    [SerializeField] private float lineLifetime = 2.5f;
    [SerializeField] private int maxLinesPerSide = 14;

    [Header("Keyboard Code Lines")]
    [SerializeField]
    private string[] keyboardCodeLines =
    {
        "input.keyboard.scan(WASD);",
        "move.vector = normalize(axis.raw);",
        "controller.Move(delta.unscaled);",
        "jump.buffer.write(SPACE);",
        "sprint.vector.inject(SHIFT);",
        "gravity.override(player.airborne);",
        "surface.query(StickySurface);",
        "dash.charge.sync();",
        "hp.regen.tick();",
        "state.machine.resolve();"
    };

    [Header("Mouse Code Lines")]
    [SerializeField]
    private string[] mouseCodeLines =
    {
        "input.mouse.lmb.hold(DashAim);",
        "input.mouse.rmb.hold(SwordAim);",
        "reticle.cast(ray.center);",
        "dash.target = sticky.hitPoint;",
        "trajectory.solve(camera.forward);",
        "fov.override(zoomFactor);",
        "slowmo.layer.set(0.10);",
        "katana.throw(ray.forward);",
        "katana.recall(stuckTarget);",
        "snap.target.acquire();"
    };

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private readonly List<TextMeshProUGUI> keyboardLines = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> mouseLines = new List<TextMeshProUGUI>();

    private float nextKeyboardSpawnTime;
    private float nextMouseSpawnTime;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerCore>();
        }

        if (player == null || player.Input == null)
        {
            Debug.LogError("[PseudoCodeHUD] No PlayerCore/PlayerInputHandler found!");
            enabled = false;
            return;
        }

        if (codeLinePrefab == null)
        {
            Debug.LogError("[PseudoCodeHUD] No code line prefab assigned!");
            enabled = false;
            return;
        }
    }

    private void Update()
    {
        if (player == null || player.Input == null || player.IsDead)
        {
            return;
        }

        bool keyboardInput = player.Input.HasKeyboardGameplayInput(spawnWhileHeld);
        bool mouseInput = player.Input.HasMouseGameplayInput(spawnWhileHeld);

        if (keyboardInput && Time.unscaledTime >= nextKeyboardSpawnTime)
        {
            SpawnLine(keyboardCodeContainer, keyboardLines, keyboardCodeLines);
            nextKeyboardSpawnTime = Time.unscaledTime + Mathf.Max(0.01f, keyboardSpawnInterval);
        }

        if (mouseInput && Time.unscaledTime >= nextMouseSpawnTime)
        {
            SpawnLine(mouseCodeContainer, mouseLines, mouseCodeLines);
            nextMouseSpawnTime = Time.unscaledTime + Mathf.Max(0.01f, mouseSpawnInterval);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Line Management
    // ════════════════════════════════════════════════════════════════════════

    private void SpawnLine(Transform container, List<TextMeshProUGUI> list, string[] sourceLines)
    {
        if (container == null || codeLinePrefab == null)
        {
            return;
        }

        RemoveMissingEntries(list);

        while (list.Count >= Mathf.Max(1, maxLinesPerSide))
        {
            DestroyLine(list[0], list);
        }

        TextMeshProUGUI line = Instantiate(codeLinePrefab, container);
        line.gameObject.SetActive(true);
        line.text = PickLine(sourceLines);
        list.Add(line);

        StartCoroutine(FadeAndDestroy(line, list));
    }

    private string PickLine(string[] sourceLines)
    {
        if (sourceLines == null || sourceLines.Length == 0)
        {
            return "system.feed.write(input);";
        }

        int index = Random.Range(0, sourceLines.Length);
        return sourceLines[index];
    }

    private IEnumerator FadeAndDestroy(TextMeshProUGUI line, List<TextMeshProUGUI> list)
    {
        if (line == null)
        {
            yield break;
        }

        Color originalColor = line.color;
        float lifetime = Mathf.Max(0.01f, lineLifetime);
        float fadeStart = lifetime * 0.55f;
        float timer = 0f;

        while (timer < lifetime && line != null)
        {
            timer += Time.unscaledDeltaTime;

            if (timer >= fadeStart)
            {
                float fadeDuration = Mathf.Max(0.01f, lifetime - fadeStart);
                float fadeProgress = Mathf.Clamp01((timer - fadeStart) / fadeDuration);
                Color fadedColor = originalColor;
                fadedColor.a = Mathf.Lerp(originalColor.a, 0f, fadeProgress);
                line.color = fadedColor;
            }

            yield return null;
        }

        DestroyLine(line, list);
    }

    private void DestroyLine(TextMeshProUGUI line, List<TextMeshProUGUI> list)
    {
        if (line != null)
        {
            list.Remove(line);
            Destroy(line.gameObject);
        }
    }

    private void RemoveMissingEntries(List<TextMeshProUGUI> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
            {
                list.RemoveAt(i);
            }
        }
    }

    #endregion
}
