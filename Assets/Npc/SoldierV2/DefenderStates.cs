using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DEFENDER STATES - Alle Zustände des Defender NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// Der Defender beschützt Soldiers indem er sich zwischen sie und den Spieler
// stellt. Er kann Angriffe blocken und mit einem Counter antworten.
//
// ════════════════════════════════════════════════════════════════════════════

namespace DefenderStates
{
    // ────────────────────────────────────────────────────────────────────────
    // IDLE - Wartet auf einen Soldier zum Beschützen
    // ────────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.NpcAnimator?.SetBool("IsGuarding", false);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Langsam zum Spieler drehen wenn sichtbar
            if (npc.CanSeePlayer && npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 0.5f);
            }

            // Soldier gefunden? → Beschützen
            if (npc.ProtectedSoldier != null && !npc.ProtectedSoldier.IsDead)
            {
                return new MovingToProtect();
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // MOVING TO PROTECT - Bewegt sich zwischen Spieler und Soldier
    // ────────────────────────────────────────────────────────────────────────
    public class MovingToProtect : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "MovingToProtect";
        public override int StateID => 1;

        public override void Enter(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsMoving", true);
            npc.NpcAnimator?.SetBool("IsGuarding", false);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Soldier verloren?
            if (npc.ProtectedSoldier == null || npc.ProtectedSoldier.IsDead)
            {
                npc.FindSoldierToProtect();
                if (npc.ProtectedSoldier == null)
                {
                    return new Idle();
                }
            }

            Vector3 targetPosition = npc.GetInterceptPosition();

            // Am Ziel angekommen?
            if (npc.HasReachedDestination())
            {
                return new Guarding();
            }

            // Weiter bewegen
            npc.MoveToward(targetPosition);
            
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position);
            }

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsMoving", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // GUARDING - Steht in Position und wartet auf Angriffe
    // ────────────────────────────────────────────────────────────────────────
    public class Guarding : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Guarding";
        public override int StateID => 2;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.NpcAnimator?.SetBool("IsGuarding", true);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Zum Spieler drehen
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 2f);
            }

            // Repositionierung nötig?
            if (npc.ProtectedSoldier != null && !npc.ProtectedSoldier.IsDead)
            {
                Vector3 idealPosition = npc.GetInterceptPosition();
                float distanceToIdeal = Vector3.Distance(npc.transform.position, idealPosition);

                if (distanceToIdeal > npc.RepositionThreshold)
                {
                    return new MovingToProtect();
                }
            }
            else
            {
                // Soldier verloren
                npc.FindSoldierToProtect();
                if (npc.ProtectedSoldier == null)
                {
                    return new Idle();
                }
            }

            // Spieler nah genug für Block?
            if (npc.ShouldStartBlocking())
            {
                return new Blocking();
            }

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsGuarding", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // BLOCKING - Aktiv am Blocken
    // ────────────────────────────────────────────────────────────────────────
    public class Blocking : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Blocking";
        public override int StateID => 3;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.BlockDuration);
            npc.BlockStartTime = Time.time;
            npc.LastBlockTime = Time.time;
            npc.WasAttackBlocked = false;
            npc.WasPerfectBlock = false;
            
            npc.NpcAnimator?.SetBool("IsBlocking", true);
            npc.NpcAnimator?.SetTrigger("Block");
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Zum Spieler drehen (schnell)
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 3f);
            }

            // Block-Zeit vorbei?
            if (npc.UpdateStateTimer())
            {
                if (npc.WasAttackBlocked)
                {
                    return new Countering();
                }
                else
                {
                    return new Guarding();
                }
            }

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            npc.WasAttackBlocked = false;
            npc.WasPerfectBlock = false;
            npc.NpcAnimator?.SetBool("IsBlocking", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // COUNTERING - Führt Gegenangriff aus
    // ────────────────────────────────────────────────────────────────────────
    public class Countering : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Countering";
        public override int StateID => 4;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.CounterDuration);
            
            npc.NpcAnimator?.SetTrigger("Counter");
            npc.TryDealCounterDamage();
            npc.PlayCounterSound();
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Zum Spieler drehen (sehr schnell)
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 4f);
            }

            // Counter vorbei?
            if (npc.UpdateStateTimer())
            {
                return new Guarding();
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // STUNNED - Bewegungsunfähig
    // ────────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.NpcAnimator?.SetBool("IsGuarding", false);
            npc.NpcAnimator?.SetBool("IsBlocking", false);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Stun wird von NpcBase gehandhabt
            return null;
        }
    }
}
