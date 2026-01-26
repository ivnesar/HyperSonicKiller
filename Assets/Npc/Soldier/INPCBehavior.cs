// ===== SIMPLIFIED BEHAVIOR INTERFACE =====
public interface INPCBehavior
{
    // Check if NPC can attack (in range and has LOS)
    bool CanAttack(float distanceToPlayer, bool hasLOS);
    
    // Called when entering attack state
    void OnStartAttack();
    
    // Called every frame while attacking
    void UpdateAttack();
    
    // Called when exiting attack state
    void OnStopAttack();

    // Check if should transition to reload state
    bool ShouldReload();

    // Called when entering reload state
    void OnStartReload();

    // Called every frame while reloading
    void UpdateReload();

    // Called when exiting reload state
    void OnStopReload();

    // Check if reload is complete
    bool IsReloadComplete();
}