# Grenadier NPC - Setup Instructions

## Dateien

| Datei | Zielordner | Beschreibung |
|-------|-----------|--------------|
| `GrenadierNpc.cs` | `Assets/Npc/Grenadier/` | Haupt-NPC-Klasse |
| `GrenadierStates.cs` | `Assets/Npc/Grenadier/` | State Machine |
| `GrenadierAnimationManager.cs` | `Assets/Npc/Grenadier/` | Animancer-basierter Anim-Manager |
| `AntiDashGrenade.cs` | `Assets/Npc/Grenadier/` | Granaten-Projektil |
| `AntiDashZone.cs` | `Assets/Npc/Grenadier/` | Temporäre Anti-Dash Zone |

## Nötige Änderung in NpcBase.cs

`Grenadier` zum `NpcType`-Enum hinzufügen:

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
    Grenadier    // ← NEU
}
```

---

## Unity Scene Setup

### 1. Grenadier NPC Prefab

```
Grenadier (Empty GameObject)
├── Model (dein 3D-Modell mit Animator + AnimancerComponent)
│   └── [Bones, Mesh, etc.]
└── MuzzlePoint (Empty — Position der Granatwerfer-Mündung)
```

**Auf dem Root "Grenadier" GameObject:**
- `GrenadierNpc` Komponente
- `NavMeshAgent` (Standard-Navigation)
- `NpcRagdollController` (optional, für Ragdoll bei Tod)
- `NpcLaserPointer` (optional, zeigt Warnlaser beim Zielen)
- `AudioSource`

**Auf dem Model-Child (wo der Animator sitzt):**
- `AnimancerComponent`
- `GrenadierAnimationManager`
- Alle ClipTransitions zuweisen (idle, walk, aim, aimHold, fire, reload, hit, die, stunned)

### 2. Anti-Dash Granate Prefab

```
AntiDashGrenade (Empty GameObject)
├── Model (optionales Mesh für die Granate)
└── [Trail-Effekt, Partikel, etc.]
```

**Auf dem Root:**
- `AntiDashGrenade` Komponente
- `Zone Prefab` zuweisen (siehe unten)
- Optional: `Impact Effect Prefab`, Audio Clips

### 3. Anti-Dash Zone Prefab

```
AntiDashZone (Empty GameObject)
└── Billboard (Quad)
```

**Auf dem Root "AntiDashZone":**
- `AntiDashZone` Komponente
- `Billboard Transform` zuweisen (das Quad-Child)
- Optional: Audio Clips für Aktivierung/Deaktivierung

**Billboard-Quad Setup:**
1. 3D Object → Quad als Child erstellen
2. Material zuweisen: Transparentes Material mit Warntextur (gleich wie bei der Drone)
3. Shader: `Unlit/Transparent` oder ähnlich
4. Die Skalierung wird automatisch auf `effectRadius * 2` gesetzt

---

## Inspector-Werte

### GrenadierNpc

| Feld | Default | Beschreibung |
|------|---------|-------------|
| Min Shooting Range | 8 | Mindestdistanz zum Schießen |
| Max Shooting Range | 25 | Maximale Schussdistanz |
| Preferred Range | 16 | Bevorzugte Kampfdistanz |
| Aim Duration | 1.2 | Sekunden Zielen vor dem Schuss |
| Magazine Size | 1 | Granaten pro Magazin (1 = Einzelschuss, 6 = MGL-Trommel) |
| Time Between Shots | 0.8 | Sekunden zwischen Schüssen (nur bei magazineSize > 1) |
| Reload Duration | 3.0 | Sekunden Nachladen nach dem letzten Schuss |
| Grenade Zone Radius | 6 | Radius der Anti-Dash Zone |
| Grenade Zone Duration | 5 | Wie lange die Zone aktiv bleibt |
| Max Health | 100 | Von NpcBase |

### AntiDashGrenade

| Feld | Default | Beschreibung |
|------|---------|-------------|
| Flight Duration | 1.2 | Flugzeit in Sekunden |
| Gravity | 20 | Stärke der Schwerkraft auf die Granate |

### AntiDashZone

| Feld | Default | Beschreibung |
|------|---------|-------------|
| Effect Radius | 6 | Radius der Sperrzone |
| Duration | 5 | Wie lange die Zone aktiv bleibt |
| Dash Cancel Delay | 0.1 | Unscaled Sekunden bis aktiver Dash abgebrochen wird |
| Blink Before Expiry | true | Billboard blinkt kurz vor Ablauf |
| Blink Start Time | 1.5 | Sekunden vor Ablauf wenn Blinken beginnt |

---

## Wie es funktioniert

```
[Idle] ─── (Spieler in Reichweite + LoS) ──▶ [Aiming]
  ▲                                               │
  │                                          (Aim-Timer ab)
  │                                               ▼
  │                                          [Firing]
  │                                          Granaten feuern bis
  │                                          Magazin leer
  │                                               │
  │                                               ▼
  └──── (Reload-Timer ab) ◀──────────────── [Reloading]

  Jeder State → [Stunned] → (Stun endet) → [Idle]
```

### Granaten-Flugbahn

```
    Grenadier                              Spieler
       ╲                                    ╱
        ╲    ⌒⌒⌒  Parabelbahn  ⌒⌒⌒      ╱
         ╲ ╱                          ╲  ╱
          ●                            ●
     Muzzle Point               Aufschlagspunkt
                                       │
                                       ▼
                               ┌──────────────┐
                               │ AntiDashZone  │
                               │ (zeitbegrenzt)│
                               └──────────────┘
```

- Beim Abschuss wird die Spielerposition als fester Zielpunkt gespeichert
- Die Granate fliegt auf einer mathematischen Parabelbahn (kein Rigidbody)
- **Keine Kollisionen unterwegs** — die Granate ignoriert alle Objekte auf dem Weg
- Nach `flightDuration` Sekunden detoniert sie am Zielpunkt
- Am Einschlag wird eine `AntiDashZone` gespawnt (zeitbegrenzt)

### Anti-Dash Zone Verhalten

Identisch zur Anti-Dash Drone:
1. Spieler betritt Radius → `PlayerDash.SetDashEnabled(false)` 
2. Spieler dasht bereits → Dash wird nach 0.1s (unscaled) abgebrochen
3. Spieler verlässt Radius → Dash wieder erlaubt
4. **NEU:** Zone blinkt 1.5s vor Ablauf und zerstört sich danach

### Sicherheit

- `AntiDashZone.OnDestroy()` aktiviert Dash immer wieder (verhindert permanentes Sperren)
- Zone prüft in `Start()` ob PlayerCore/PlayerDash vorhanden sind
- Granate hat Fallback-Position wenn kein MuzzlePoint gesetzt ist
