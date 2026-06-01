using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC HUD ICON — Per-Prefab-Datenkomponente für das Tracking-HUD-Icon
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
// - Diese Komponente ZEICHNET NICHTS. Sie hält nur die Icons dieses NPCs und
//   liefert auf Anfrage (GetCurrentIcon) das passende Icon zum aktuellen State.
// - Das globale TrackingHUD liest dieses Icon aus und zeichnet es.
// - So liegt die Icon-KONFIGURATION pro Prefab (designerfreundlich), während
//   das RENDERING zentral im HUD bleibt.
//
// STATE-ABFRAGE:
// - Der aktuelle State kommt aus NpcBase.GetStateID() (ein int, den jede NPC-
//   State-Machine selbst vergibt). Die StateIDs sind pro NPC-Typ eigenständig —
//   deshalb wird das Mapping hier am Prefab definiert, wo die IDs bekannt sind.
//
// PREFAB SETUP:
// 1. Diese Komponente aufs NPC-Root legen (neben NpcBase).
// 2. 'Default Icon' = Standard-Icon dieses NPC-Typs.
// 3. Optional pro State einen Eintrag unter 'State Icons':
//    - State ID  = Wert den GetStateID() in diesem State zurückgibt
//    - Icon      = Sprite das in diesem State gezeigt werden soll
// 4. NPCs OHNE diese Komponente bekommen einfach kein Icon (Box + Label bleiben).
//
// ════════════════════════════════════════════════════════════════════════════

public class NpcHudIcon : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector
    // ════════════════════════════════════════════════════════════════════════

    [System.Serializable]
    public struct StateIcon
    {
        [Tooltip("StateID wie von GetStateID() in genau diesem State zurückgegeben.")]
        public int stateID;

        [Tooltip("Sprite das in diesem State angezeigt wird.")]
        public Sprite icon;
    }

    [Header("Icons")]
    [Tooltip("Standard-Icon des NPC-Typs. Wird gezeigt, wenn der aktuelle State " +
             "kein eigenes Icon in der Liste hat. Leer = kein Icon.")]
    [SerializeField] private Sprite defaultIcon;

    [Tooltip("Optionale State-spezifische Icons. Überschreiben das Default-Icon, " +
             "wenn der NPC sich gerade in diesem State befindet.")]
    [SerializeField] private StateIcon[] stateIcons;

    [Header("Position")]
    [Tooltip("Feinjustierung der Icon-Position in Pixeln, relativ zur Standard-" +
             "position (mittig über der Box). +X = nach rechts, +Y = nach oben.")]
    [SerializeField] private Vector2 positionOffset = Vector2.zero;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private Dictionary<int, Sprite> iconLookup;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        if (npc == null) npc = GetComponentInParent<NpcBase>();

        if (npc == null)
            Debug.LogWarning($"[NpcHudIcon] Kein NpcBase auf {name} gefunden — " +
                             "State-Icons funktionieren nicht.", this);

        // Lookup einmalig aufbauen (StateID -> Sprite)
        iconLookup = new Dictionary<int, Sprite>();
        if (stateIcons != null)
        {
            foreach (var entry in stateIcons)
            {
                if (entry.icon != null)
                    iconLookup[entry.stateID] = entry.icon;
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Liefert das Icon für den aktuellen State, sonst das Default-Icon.
    /// Kann null sein (dann zeichnet das HUD einfach kein Icon).
    /// </summary>
    public Sprite GetCurrentIcon()
    {
        if (npc != null && iconLookup != null &&
            iconLookup.TryGetValue(npc.GetStateID(), out var stateIcon))
        {
            return stateIcon;
        }
        return defaultIcon;
    }

    /// <summary>
    /// Feinjustierung der Icon-Position in Pixeln (+X rechts, +Y oben),
    /// vom TrackingHUD beim Zeichnen angewendet.
    /// </summary>
    public Vector2 PositionOffset => positionOffset;

    #endregion
}
