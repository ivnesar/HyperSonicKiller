# GenTwo Animator Setup Guide

## Animator Parameters

| Parameter        | Type    | Zweck                                              |
|------------------|---------|-----------------------------------------------------|
| `IsOnWall`       | Bool    | Schaltet zwischen Boden/Wand-Varianten              |
| `IsCharging`     | Bool    | Idle → Charge (Loop)                                |
| `DashStart`      | Trigger | Charge → StartDash                                  |
| `IsDashing`      | Bool    | StartDash → Dash (Loop)                             |
| `DashAttack`     | Trigger | Dash ⇄ DashAttack (Blending)                       |
| `Land`           | Trigger | Dash → Landing                                      |
| `Stunned`        | Trigger | AnyState → Stunned                                  |
| `RecoveryDone`   | Trigger | Landing/Stunned → Idle                              |

---

## Animation Clips (12 Total)

| Clip Name              | Variante | Typ  |
|------------------------|----------|------|
| `IdleGround`           | Boden    | Loop |
| `IdleWall`             | Wand     | Loop |
| `ChargeGround`         | Boden    | Loop |
| `ChargeWall`           | Wand     | Loop |
| `StartDashGround`      | Boden    | Once |
| `StartDashWall`        | Wand     | Once |
| `Dash`                 | Shared   | Loop |
| `DashAttack`           | Shared   | Once |
| `LandingGround`        | Boden    | Once |
| `LandingWall`          | Wand     | Once |
| `StunnedGround`        | Boden    | Loop |
| `StunnedWall`          | Wand     | Loop |

---

## Animator State Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│                          ANIMATOR LAYER 0                           │
│                                                                     │
│   ┌──────────┐    ┌──────────┐    ┌──────────┐                     │
│   │IdleGround│───▶│ChargeGrnd│───▶│StartDash │                     │
│   └──────────┘    └──────────┘    │  Ground  │──┐                  │
│                                    └──────────┘  │                  │
│   ┌──────────┐    ┌──────────┐    ┌──────────┐  │                  │
│   │ IdleWall │───▶│ChargeWall│───▶│StartDash │  │                  │
│   └──────────┘    └──────────┘    │   Wall   │──┤                  │
│                                    └──────────┘  │                  │
│                                                   ▼                  │
│                                    ┌────────────────┐               │
│                                    │      Dash      │⇄ DashAttack  │
│                                    └───────┬────────┘               │
│                                     Land   │                        │
│                              ┌─────────────┼──────────┐             │
│                              ▼             ▼          ▼             │
│                       ┌──────────┐  ┌──────────┐                    │
│                       │ Landing  │  │ Landing  │                    │
│                       │ Ground   │  │   Wall   │                    │
│                       └────┬─────┘  └────┬─────┘                    │
│                            │             │                          │
│                            ▼             ▼                          │
│                       IdleGround     IdleWall                       │
│                                                                     │
│   AnyState ──Stunned──▶ StunnedGround (IsOnWall = false)           │
│   AnyState ──Stunned──▶ StunnedWall   (IsOnWall = true)            │
│   StunnedGround ──RecoveryDone──▶ IdleGround                       │
│   StunnedWall   ──RecoveryDone──▶ IdleWall                         │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Alle Transitions (Step-by-Step Unity Setup)

### Default State
`IdleGround` ist der **Default State** (orangener Knoten).

---

### 1. IdleGround → ChargeGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.1s                        |
| Conditions         | `IsCharging` = true, `IsOnWall` = false |

### 2. IdleWall → ChargeWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.1s                        |
| Conditions         | `IsCharging` = true, `IsOnWall` = true |

### 3. ChargeGround → StartDashGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.05s                       |
| Conditions         | `DashStart`                 |
| Zusatz             | `IsOnWall` = false          |

### 4. ChargeWall → StartDashWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.05s                       |
| Conditions         | `DashStart`                 |
| Zusatz             | `IsOnWall` = true           |

### 5. ChargeGround → IdleGround (Abbruch)
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `IsCharging` = false, `IsOnWall` = false |

### 6. ChargeWall → IdleWall (Abbruch)
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `IsCharging` = false, `IsOnWall` = true |

### 7. StartDashGround → Dash
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ✅ (Animation spielt komplett) |
| Transition Duration| 0.05s                       |
| Conditions         | `IsDashing` = true          |

### 8. StartDashWall → Dash
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ✅ (Animation spielt komplett) |
| Transition Duration| 0.05s                       |
| Conditions         | `IsDashing` = true          |

### 9. Dash ⇄ DashAttack
**Dash → DashAttack:**

| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.05s                       |
| Conditions         | `DashAttack`                |

**DashAttack → Dash:**

| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ✅ (Animation spielt komplett) |
| Transition Duration| 0.1s                        |
| Conditions         | *(keine — Exit Time reicht)* |

### 10. Dash → LandingGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.0s (sofort)               |
| Conditions         | `Land`, `IsOnWall` = false  |

### 11. Dash → LandingWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.0s (sofort)               |
| Conditions         | `Land`, `IsOnWall` = true   |

### 12. LandingGround → IdleGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `RecoveryDone`, `IsOnWall` = false |

### 13. LandingWall → IdleWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `RecoveryDone`, `IsOnWall` = true |

### 14. AnyState → StunnedGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.0s                        |
| Conditions         | `Stunned`, `IsOnWall` = false |
| Can Transition To Self | ❌                      |

### 15. AnyState → StunnedWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.0s                        |
| Conditions         | `Stunned`, `IsOnWall` = true |
| Can Transition To Self | ❌                      |

### 16. StunnedGround → IdleGround
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `RecoveryDone`, `IsOnWall` = false |

### 17. StunnedWall → IdleWall
| Eigenschaft       | Wert                        |
|--------------------|-----------------------------|
| Has Exit Time      | ❌                          |
| Transition Duration| 0.15s                       |
| Conditions         | `RecoveryDone`, `IsOnWall` = true |

---

## Code-Änderungen

### GenTwoNpc.cs — Neue Properties & Methoden

Folgende Ergänzungen in `GenTwoNpc.cs`:

```csharp
// ═══════════════════════════════════════════════════════════════
// In der Region "Runtime State" hinzufügen:
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// True wenn GenTwo an einer Wand hängt (statt am Boden zu stehen).
/// Wird beim Aufprall gesetzt, basierend auf der Kollisions-Normale.
/// </summary>
private bool isOnWall;

// ═══════════════════════════════════════════════════════════════
// In der Region "Properties" hinzufügen:
// ═══════════════════════════════════════════════════════════════

/// <summary>True wenn GenTwo an einer Wand hängt.</summary>
public bool IsOnWall => isOnWall;

// ═══════════════════════════════════════════════════════════════
// Neue Methode in der Region "Helper Methods":
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Bestimmt anhand der Aufprall-Normale ob GenTwo an einer Wand oder
/// am Boden gelandet ist. Alles über 45° zur Vertikalen = Wand.
/// </summary>
public void DetermineWallOrGround(Vector3 surfaceNormal)
{
    // Winkel zwischen Oberflächen-Normale und Oben-Vektor
    // Boden: Normale zeigt nach oben (Winkel ≈ 0°)
    // Wand: Normale zeigt seitlich (Winkel ≈ 90°)
    float angle = Vector3.Angle(surfaceNormal, Vector3.up);
    isOnWall = angle > 45f;

    if (NpcAnimator != null)
        NpcAnimator.SetBool("IsOnWall", isOnWall);

    Debug.Log($"[GenTwo] {name}: Landed on {(isOnWall ? "WALL" : "GROUND")} (angle: {angle:F1}°)");
}
```

### ProcessDashMovement — Surface Normal speichern

Die bestehende `ProcessDashMovement()` Methode muss angepasst werden, damit beim Aufprall die Normale weitergegeben wird:

```csharp
// In ProcessDashMovement(), ersetze den Surface-Hit Block:

// VORHER:
if (Physics.Raycast(currentPos, dashDirection, out RaycastHit surfaceHit,
    segmentDistance + 0.5f, surfaceLayerMask))
{
    transform.position = surfaceHit.point + surfaceHit.normal * 0.3f;
    PlaySound(impactSound);
    return true;
}

// NACHHER:
if (Physics.Raycast(currentPos, dashDirection, out RaycastHit surfaceHit,
    segmentDistance + 0.5f, surfaceLayerMask))
{
    transform.position = surfaceHit.point + surfaceHit.normal * 0.3f;
    DetermineWallOrGround(surfaceHit.normal);
    PlaySound(impactSound);
    return true;
}
```

### GenTwoStates.cs — Aktualisierte States

```csharp
using UnityEngine;

namespace GenTwoStates
{
    // ─────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(GenTwoNpc npc)
        {
            npc.ClearInterceptData();

            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsCharging", false);
                npc.NpcAnimator.SetBool("IsDashing", false);
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (npc.IsPlayerDashing && npc.IsPlayerInRange && npc.HasLineOfSightToPlayer())
            {
                return new Charging();
            }
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // CHARGING
    // ─────────────────────────────────────────────────────────────
    public class Charging : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Charging";
        public override int StateID => 1;

        public override void Enter(GenTwoNpc npc)
        {
            npc.SetUnscaledTimer(npc.ChargeDuration);
            npc.PlayChargeSound();

            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsCharging", true);
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            npc.RotateTowardTargetUnscaled();

            if (!npc.IsPlayerDashing || !npc.IsPlayerInRange || !npc.HasLineOfSightToPlayer())
            {
                return new Idle();
            }

            if (npc.UpdateUnscaledTimer())
            {
                return new Dashing();
            }
            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsCharging", false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DASHING
    // ─────────────────────────────────────────────────────────────
    public class Dashing : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Dashing";
        public override int StateID => 2;

        private bool abortDash;

        public override void Enter(GenTwoNpc npc)
        {
            Vector3 interceptDir = npc.CalculateInterceptDirection();

            if (interceptDir == Vector3.zero)
            {
                abortDash = true;
                Debug.Log($"[GenTwo] {npc.name}: Dash aborted — no valid intercept path");
                return;
            }

            abortDash = false;
            npc.SetDashDirection(interceptDir);
            npc.FaceDirection(interceptDir);
            npc.PlayDashSound();

            if (npc.NpcAnimator != null)
            {
                // DashStart Trigger löst Charge → StartDash Transition aus
                // StartDash → Dash wird durch IsDashing = true fortgesetzt
                npc.NpcAnimator.SetTrigger("DashStart");
                npc.NpcAnimator.SetBool("IsDashing", true);
            }

            Debug.Log($"[GenTwo] {npc.name}: Dash started! Direction: {interceptDir}");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (abortDash)
                return new Idle();

            bool hitSurface = npc.ProcessDashMovement();

            if (hitSurface)
                return new Recovery();

            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsDashing", false);
                npc.NpcAnimator.SetTrigger("Land");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // RECOVERY
    // ─────────────────────────────────────────────────────────────
    public class Recovery : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Recovery";
        public override int StateID => 3;

        public override void Enter(GenTwoNpc npc)
        {
            npc.SetUnscaledTimer(npc.RecoveryDuration);
            Debug.Log($"[GenTwo] {npc.name}: Recovering for {npc.RecoveryDuration}s (OnWall: {npc.IsOnWall})");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (npc.UpdateUnscaledTimer())
            {
                if (npc.NpcAnimator != null)
                    npc.NpcAnimator.SetTrigger("RecoveryDone");

                return new Idle();
            }
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // STUNNED
    // ─────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 4;

        public override void Enter(GenTwoNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsDashing", false);
                npc.NpcAnimator.SetBool("IsCharging", false);
                npc.NpcAnimator.SetTrigger("Stunned");
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}
```

### GenTwoNpc.cs — OnStunEnd anpassen

```csharp
// VORHER:
protected override void OnStunEnd()
{
    ChangeState(new GenTwoStates.Idle());
}

// NACHHER:
protected override void OnStunEnd()
{
    if (NpcAnimator != null)
        NpcAnimator.SetTrigger("RecoveryDone");

    ChangeState(new GenTwoStates.Idle());
}
```

### GenTwoNpc.cs — DashAttack Trigger bei Spieler-Treffer

In `ProcessDashMovement()`, beim Spieler-Treffer den Animator-Trigger setzen:

```csharp
// VORHER:
if (distToPlayer <= playerHitRadius)
{
    hasHitPlayer = true;

    if (IsPlayerDashing)
    {
        playerCore.TakeDirectDamage(collisionDamage);
        Debug.Log(...);
    }
    else
    {
        Debug.Log(...);
    }
}

// NACHHER:
if (distToPlayer <= playerHitRadius)
{
    hasHitPlayer = true;

    // DashAttack Animation triggern (unabhängig ob Schaden gemacht wird)
    if (animator != null)
        animator.SetTrigger("DashAttack");

    if (IsPlayerDashing)
    {
        playerCore.TakeDirectDamage(collisionDamage);
        Debug.Log($"[GenTwo] {name}: INTERCEPTED player during dash! Dealt {collisionDamage} damage!");
    }
    else
    {
        Debug.Log($"[GenTwo] {name}: Passed through player (player not dashing - no damage)");
    }
}
```

### NpcBase.cs — UpdateAnimator anpassen

Der `UpdateAnimator()` in `NpcBase` setzt aktuell `IsStunned` als Bool. Das kollidiert mit dem neuen `Stunned` Trigger. Für GenTwo muss der Bool-Ansatz durch den Trigger ersetzt werden. Am einfachsten: den `IsStunned`-Bool aus `UpdateAnimator` für GenTwo überspringen:

```csharp
// In GenTwoNpc.cs — UpdateAnimator überschreiben:

protected override void UpdateAnimator()
{
    if (animator == null) return;

    // GenTwo nutzt keinen MoveSpeed (kein NavMesh)
    // GenTwo nutzt Stunned-Trigger statt IsStunned-Bool
    animator.SetInteger("StateID", GetStateID());
    animator.SetBool("IsDead", isDead);
}
```

---

## Zusammenfassung: Welcher Code triggert welchen Parameter

| Animator Parameter | Gesetzt in                          | Wert                    |
|--------------------|--------------------------------------|--------------------------|
| `IsOnWall`         | `DetermineWallOrGround()`           | true/false bei Landing   |
| `IsCharging`       | `Charging.Enter()` / `Exit()`       | true → false             |
| `DashStart`        | `Dashing.Enter()`                   | Trigger                  |
| `IsDashing`        | `Dashing.Enter()` / `Exit()`        | true → false             |
| `DashAttack`       | `ProcessDashMovement()` (Spieler-Hit) | Trigger                |
| `Land`             | `Dashing.Exit()`                    | Trigger                  |
| `Stunned`          | `Stunned.Enter()`                   | Trigger                  |
| `RecoveryDone`     | `Recovery.Update()` / `OnStunEnd()` | Trigger                  |

---

## Hinweise

- **IsOnWall bleibt bestehen** bis der nächste Dash endet. Es wird nur bei `DetermineWallOrGround()` geändert, also bei jedem neuen Aufprall. Der GenTwo startet mit `isOnWall = false` (Boden-Init).
- **DashAttack → Dash** nutzt Exit Time, weil die DashAttack-Animation komplett spielen soll bevor der Dash-Loop weitergeht.
- **Land Trigger wird in Dashing.Exit() gesetzt**, nicht in Recovery.Enter(). Das stellt sicher, dass der Trigger gefeuert wird bevor der Animator-State wechselt.
- **Transition Duration 0.0s** bei Landing und Stunned, weil diese Übergänge sofort passieren sollen (Aufprall / Stun-Hit).
