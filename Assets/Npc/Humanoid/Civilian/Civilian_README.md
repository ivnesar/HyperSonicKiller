# Civilian NPC - Setup Instructions

## Dateien

| Datei | Zielordner | Beschreibung |
|-------|-----------|--------------|
| `CivilianNpc.cs` | `Assets/Npc/Humanoid/Civilian/` | Haupt-NPC-Klasse |
| `CivilianStates.cs` | `Assets/Npc/Humanoid/Civilian/` | State Machine |
| `CivilianAnimationManager.cs` | `Assets/Npc/Humanoid/Civilian/` | Animancer-basierter Anim-Manager |

## Nötige Änderung in NpcBase.cs

`Civilian` zum `NpcType`-Enum hinzufügen:

```csharp
public enum NpcType
{
    Soldier,
    Defender,
    GenOne,
    GenTwo,
    AntiDashDrone,
    ProxyMine,
    Scientist,
    Grenadier,
    Sniper,
    Civilian       // ← NEU
}
```

---

## Unity Scene Setup

### 1. Civilian Prefab-Struktur

```
Npc_Civilian (Empty GameObject)
├── Model (3D-Modell mit Animator + AnimancerComponent)
│   └── [Bones, Mesh, etc.]
```

**Auf dem Root "Npc_Civilian" GameObject:**
- `CivilianNpc` Komponente
- `NavMeshAgent`
- `NpcImpactTracker` (für Ragdoll-Impact bei Tod)
- `NpcRagdollSwapper` (für Ragdoll-Tod)
- `AudioSource`
- `CapsuleCollider` (für Gameplay-Hits)

**Auf dem Model-Child (wo der Animator sitzt):**
- `AnimancerComponent`
- `CivilianAnimationManager`
- Alle ClipTransitions zuweisen (idle, walk, panicRun, cower, cowerAtWall, stunned)

### 2. NavMeshAgent Konfiguration

| Feld | Empfohlener Wert | Warum |
|------|-----------------|-------|
| Speed | 6 | Schneller als Soldier-Walk, langsamer als Player |
| Angular Speed | 360 | Schnelles Umdrehen in Panik |
| Acceleration | 12 | Schnelles Beschleunigen beim Losrennen |
| Stopping Distance | 0.5 | Standard |
| Avoidance Priority | 40 | Weicht Soldiers aus (Default = 50, niedriger = wichtiger) |
| Obstacle Avoidance | High Quality | Vermeidet Überlappungen mit anderen NPCs |

### 3. CivilianNpc Inspector-Werte

#### Panik-Auslöser
| Feld | Default | Beschreibung |
|------|---------|-------------|
| Panic Trigger Distance | 15 | Spieler-Nähe die Panik auslöst |
| Combat Detection Radius | 20 | Radius für Kampf-Erkennung |

#### Fluchtverhalten
| Feld | Default | Beschreibung |
|------|---------|-------------|
| Panic Run Speed | 6 | Renn-Geschwindigkeit bei Panik |
| Calm Walk Speed | 2 | Geh-Geschwindigkeit im Calm-Zustand |
| Flee Search Radius | 15 | Suchradius für Fluchtpunkte |
| Min Flee Distance From Player | 10 | Fluchtpunkte müssen mind. so weit vom Spieler weg sein |
| Cower Trigger Distance | 5 | Kauer-Freeze wenn Spieler näher als dies |
| Cower Duration | 1.5 | Dauer des Kauer-Moments |
| Wall Detection Distance | 2 | Raycast-Distanz für Wand-Erkennung |
| Wall Cower Duration | 2 | Kauer-Dauer an der Wand |
| Wall Layer Mask | Solid | Layer für Wand-Erkennung |

#### Richtungswechsel
| Feld | Default | Beschreibung |
|------|---------|-------------|
| Min Direction Change Interval | 1.5 | Minimale Zeit zwischen Richtungswechseln |
| Max Direction Change Interval | 3.5 | Maximale Zeit zwischen Richtungswechseln |

### 4. Benötigte Animationen

| Clip | Typ | Beschreibung |
|------|-----|-------------|
| Idle | Loop | Ruhiges Stehen |
| Walk | Loop | Ruhiges Gehen (Calm-Patrol) |
| PanicRun | Loop | Panisches Rennen (Arme hoch, chaotisch) |
| Cower | Loop | Kauern, Hände über Kopf |
| CowerAtWall | Loop (optional) | Kauern an der Wand — fällt auf Cower zurück wenn leer |
| Stunned | Loop | Stunned-Pose (z.B. benommen taumeln) |

### 5. Ragdoll-Setup (wie Soldier)

Der Civilian nutzt das gleiche Ragdoll-System:
- `NpcRagdollSwapper` auf dem Root
- `NpcImpactTracker` auf dem Root
- Fullbody-Ragdoll-Prefab zuweisen (mit `SpawnedRagdoll` Komponente)
- Optional: Sliced-Ragdoll-Paare (wenn Melee-Kill möglich sein soll)

---

## State Machine Übersicht

```
CALM-PHASE:
  [Idle] ←──(wait)──→ [Patrolling]
    │                      │
    └───── ShouldPanic() ──┘
              │
              ▼
PANICKING-PHASE:
  [Fleeing] ←──────────────────────────────┐
    │              │              │         │
    │ (Spieler     │ (Wand        │ (Ziel   │
    │  sehr nah)   │  erkannt)    │ erreicht/│
    ▼              ▼              │ Timer)   │
  [Cowering]  [CoweringAtWall]   │         │
    │              │              │         │
    └──(Timer)─────┴──(Timer)─────┘─────────┘

STUN (jederzeit):
  Any State ──(ApplyStun)──→ [Stunned] ──(StunEnd)──→ [Fleeing]
```

**Hinweis:** In V1 gibt es keinen Rück-Übergang von Panicking zu Calm.
Einmal in Panik = bleibt in Panik bis zum Tod.

---

## Unterschiede zum Scientist NPC

| Eigenschaft | Scientist | Civilian |
|------------|-----------|----------|
| Flucht-Trigger | Permanent fliehend | Calm → Panik bei Auslöser |
| Fluchtpunkt-Wahl | Bevorzugt Soldier-Nähe | Bevorzugt weg vom Spieler |
| Richtungswechsel | Nein (gerader Weg) | Ja, chaotisch (Timer-basiert) |
| Kauern | Nein | Ja (Freeze + Wand) |
| Animation | Walk-Loop | PanicRun, Cower, CowerAtWall |
| Audio | Keine Panik-Sounds | Schreie, Schluchzen |
| NPC-Ausweichen | Mindestabstand | Mindestabstand |

---

## Spätere Iterationen (nicht in V1)

- [ ] Stolpern/Fallen während der Flucht
- [ ] Aktives Versteck-Suchen (hinter Kisten etc.)
- [ ] Blickfeld-Erkennung: nicht ins Soldier-Sichtfeld laufen
- [ ] Eskalationsstufe "Terrified" (Nervous → Panicking → Terrified)
- [ ] Rück-Übergang zu Calm wenn Kampf vorbei ist
- [ ] Gruppen-Panik (Civilians in der Nähe stecken sich gegenseitig an)
