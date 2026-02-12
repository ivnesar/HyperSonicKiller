using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC STUN VFX - Particle-Effekt auf allen Meshes während Stun
// ════════════════════════════════════════════════════════════════════════════
//
// SETUP:
// 1. Komponente auf das NPC-GameObject legen (neben NpcBase)
// 2. stunParticlePrefab zuweisen (dein vorbereitetes Prefab)
// 3. Fertig — der Rest passiert automatisch
//
// WAS PASSIERT:
// - Beim Start werden alle Renderer am NPC gesammelt
//   (SkinnedMeshRenderer + MeshRenderer, inkl. Children wie Waffen/Schilde)
// - Pro Renderer wird ein Klon des Prefabs erstellt
// - Die Shape jedes Klons wird auf den jeweiligen Renderer gesetzt
// - Emission wird per IsStunned an/aus geschaltet
//
// ════════════════════════════════════════════════════════════════════════════

public class NpcStunVfx : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector
    // ════════════════════════════════════════════════════════════════════════

    [Header("Stun Particle")]
    [Tooltip("Dein vorbereitetes Particle-Prefab (Root GO + ParticleSystem)")]
    [SerializeField] private GameObject stunParticlePrefab;

    [Header("Optional")]
    [Tooltip("Renderer auf bestimmten Layern ignorieren (z.B. 'Ignore Raycast')")]
    [SerializeField] private LayerMask ignoreLayers;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private List<ParticleSystem> stunParticles = new List<ParticleSystem>();
    private bool isActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();

        if (npc == null)
        {
            Debug.LogWarning($"[NpcStunVfx] Kein NpcBase auf '{gameObject.name}' gefunden. Komponente wird deaktiviert.", this);
            enabled = false;
            return;
        }

        if (stunParticlePrefab == null)
        {
            Debug.LogWarning($"[NpcStunVfx] Kein stunParticlePrefab zugewiesen auf '{gameObject.name}'.", this);
            enabled = false;
            return;
        }

        CreateParticlesForAllRenderers();
        SetEmission(false);
    }

    private void Update()
    {
        if (npc == null) return;

        bool shouldBeActive = npc.IsStunned && !npc.IsDead;

        if (shouldBeActive != isActive)
        {
            SetEmission(shouldBeActive);
            isActive = shouldBeActive;
        }
    }

    private void OnDestroy()
    {
        // Aufräumen wenn NPC zerstört wird
        foreach (var ps in stunParticles)
        {
            if (ps != null)
                Destroy(ps.gameObject);
        }
        stunParticles.Clear();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Setup
    // ════════════════════════════════════════════════════════════════════════

    private void CreateParticlesForAllRenderers()
    {
        // Alle Renderer im NPC-Hierarchy sammeln
        var skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);

        foreach (var smr in skinnedRenderers)
        {
            if (ShouldIgnore(smr.gameObject)) continue;
            CreateParticleForSkinnedMesh(smr);
        }

        foreach (var mr in meshRenderers)
        {
            // MeshFilter brauchen wir für die Shape
            var meshFilter = mr.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;
            if (ShouldIgnore(mr.gameObject)) continue;

            CreateParticleForStaticMesh(mr, meshFilter);
        }

        if (stunParticles.Count == 0)
        {
            Debug.LogWarning($"[NpcStunVfx] Keine Renderer auf '{gameObject.name}' gefunden. Kein Particle-Effekt erstellt.", this);
        }
    }

    private void CreateParticleForSkinnedMesh(SkinnedMeshRenderer smr)
    {
        var instance = Instantiate(stunParticlePrefab, smr.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var ps = instance.GetComponent<ParticleSystem>();
        if (ps == null)
        {
            Debug.LogWarning($"[NpcStunVfx] Prefab hat kein ParticleSystem!", this);
            Destroy(instance);
            return;
        }

        // Shape auf SkinnedMeshRenderer setzen
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
        shape.skinnedMeshRenderer = smr;

        stunParticles.Add(ps);
    }

    private void CreateParticleForStaticMesh(MeshRenderer mr, MeshFilter meshFilter)
    {
        var instance = Instantiate(stunParticlePrefab, mr.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var ps = instance.GetComponent<ParticleSystem>();
        if (ps == null)
        {
            Debug.LogWarning($"[NpcStunVfx] Prefab hat kein ParticleSystem!", this);
            Destroy(instance);
            return;
        }

        // Shape auf MeshRenderer setzen
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.MeshRenderer;
        shape.meshRenderer = mr;

        stunParticles.Add(ps);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Emission Control
    // ════════════════════════════════════════════════════════════════════════

    private void SetEmission(bool on)
    {
        foreach (var ps in stunParticles)
        {
            if (ps == null) continue;

            var emission = ps.emission;
            emission.enabled = on;

            // Bei Aktivierung: Play starten falls gestoppt
            // Bei Deaktivierung: Nicht stoppen — bestehende Partikel sollen ausfaden
            if (on && !ps.isPlaying)
                ps.Play();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private bool ShouldIgnore(GameObject go)
    {
        // ignoreLayers == 0 bedeutet "nichts ignorieren"
        if (ignoreLayers == 0) return false;
        return (ignoreLayers & (1 << go.layer)) != 0;
    }

    #endregion
}
