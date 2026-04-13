// ════════════════════════════════════════════════════════════════════════════
// PATCH-ANLEITUNG FÜR NpcBase.cs
// ════════════════════════════════════════════════════════════════════════════
//
// Füge folgenden Code in NpcBase.cs ein:
//
// 1) Im Inspector Fields-Block (#region Inspector Fields), nach dem
//    bestehenden [Header("Debug")] Block, FÜGE HINZU:
//
//    [Header("Overlay")]
//    [Tooltip("Anzeigename im UI-Overlay. Wenn leer, wird der NpcType verwendet.")]
//    [SerializeField] protected string displayName = "";
//
// 2) Im Properties-Block (#region Properties), z.B. nach SnapTarget, FÜGE HINZU:
//
//    /// <summary>
//    /// Name der im Overlay angezeigt wird.
//    /// Fallback auf NpcType wenn kein displayName gesetzt ist.
//    /// </summary>
//    public string DisplayName => string.IsNullOrEmpty(displayName) 
//        ? GetNpcType().ToString() 
//        : displayName;
//
//    /// <summary>
//    /// Renderer für Bounding-Box-Berechnung.
//    /// Wird beim ersten Zugriff gecacht.
//    /// </summary>
//    public Renderer BoundsRenderer
//    {
//        get
//        {
//            if (cachedRenderer == null)
//                cachedRenderer = GetComponentInChildren<Renderer>();
//            return cachedRenderer;
//        }
//    }
//
// 3) Im Runtime State-Block (#region Runtime State), FÜGE HINZU:
//
//    // Overlay
//    private Renderer cachedRenderer;
//
// ════════════════════════════════════════════════════════════════════════════
// Das wars! Der Rest läuft über NpcBoundingBoxUI und NpcOverlayManager.
// ════════════════════════════════════════════════════════════════════════════
