# Sniper NPC - Setup Instructions

## Dateien

| Datei | Zielordner | Beschreibung |
|-------|-----------|--------------|
| `SniperNpc.cs` | `Assets/Npc/Sniper/` | Haupt-NPC-Klasse |
| `SniperStates.cs` | `Assets/Npc/Sniper/` | State Machine |
| `SniperAnimationManager.cs` | `Assets/Npc/Sniper/` | Animancer-basierter Anim-Manager |
| `SniperBullet.cs` | `Assets/Npc/Sniper/` | Eigenes Sniper-Projektil |

## Nötige Änderung in NpcBase.cs

`Sniper` zum `NpcType`-Enum hinzufügen:

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
    Sniper       // ← NEU
}
```

---

## Unity Scene Setup

### 1. Sniper NPC Prefab

```
Sniper (Empty GameObject)
├── Model (3D-Modell mit Animator + AnimancerComponent)
│   └── [Bones, Mesh, etc.]
└── MuzzlePoint (Empty — Position der Gewehrmündung)
```

**Auf dem Root "Sniper" GameObject:**
- `SniperNpc` Komponente
- `NavMeshAgent` (Standard-Navigation)
- `NpcRagdollController` (optional, für Ragdoll bei Tod)
- `NpcLaserPointer` (empfohlen — der Warnlaser ist das Hauptsignal für den Spieler)
- `AudioSource`

**Auf dem Model-Child (wo der Animator sitzt):**
- `AnimancerComponent`
- `SniperAnimationManager`
- Alle ClipTransitions zuweisen (idle, walk, aim, aimHold, fire, reload, hit, die, stunned)

**NpcLaserPointer Konfiguration (empfohlen):**
Der Sniper profitiert von einem deutlicheren Laser als der Soldier. Empfohlene Werte:

| Feld | Soldier-Default | Sniper-Empfehlung | Warum |
|------|----------------|-------------------|-------|
| Early Width | 0.01 | 0.02 | Breiter = früher sichtbar |
| Locked Width | 0.06 | 0.10 | Deutlichere Endwarnung |
| Early Color | Gelb 50% | Gelb 70% | Sichtbarer auf Distanz |
| Locked Color | Rot 100% | Rot 100% | Gleich |
| Wiggle Max Angle | 8° | 12° | Größerer Wiggle-Start = mehr Warnung |

### 2. Sniper Bullet Prefab

```
SniperBullet (Empty GameObject)
└── Trail (optional: TrailRenderer für Leuchtspur)
```

**Auf dem Root:**
- `SniperBullet` Komponente
- Optional: Mesh-Child (kleine Kugel/Kapsel für die Kugel selbst)

**TrailRenderer Setup (empfohlen):**
1. TrailRenderer als Child erstellen
2. Material: Unlit/Transparent, helle Farbe (weiß/gelb)
3. Time: 0.15 (kurzer Trail)
4. Start Width: 0.03, End Width: 0.0
5. Diesen TrailRenderer in `SniperBullet.Trail` zuweisen

---

## Inspector-Werte

### SniperNpc

| Feld | Default | Beschreibung |
|------|---------|-------------|
| Min Shooting Range | 20 | Mindestdistanz (Sniper ist schlecht im Nahkampf) |
| Max Shooting Range | 50 | Maximale Schussdistanz |
| Preferred Range | 35 | Bevorzugte Kampfdistanz |
| Aim Duration | 2.0 | Sekunden Zielen — länger als Soldier (0.6) für mehr Warnung |
| Reload Duration | 2.5 | Sekunden Nachladen nach dem Schuss |
| Damage Per Shot | 80 | Hoher Schaden (Soldier: 10 pro Kugel) |
| Base Accuracy | 0.95 | Trefferchance (Soldier: 0.85) |
| Accuracy Spread Angle | 2° | Streuung (Soldier: 5°) |
| Muzzle Aim Assist FOV | 3° | Enger als Soldier (5°), Sniper braucht weniger Assist |
| Max Health | 100 | Von NpcBase |

### SniperBullet

| Feld | Default | Beschreibung |
|------|---------|-------------|
| Speed | 200 | Kugel-Geschwindigkeit in m/s (sehr schnell) |
| Max Lifetime | 3 | Sicherheits-Timeout in Sekunden |
| Trail Linger Time | 0.3 | Wie lange der Trail nach Einschlag sichtbar bleibt |

---

## Wie es funktioniert

```
[Idle] ─── (Spieler in Reichweite + LoS) ──▶ [Aiming]
  ▲                                               │
  │                                          (2.0s Aim-Timer)
  │                                          Laser: Wiggle → Einlocken
  │                                               ▼
  │                                          [Firing]
  │                                          1 Schuss, hoher Schaden
  │                                               │
  │                                               ▼
  └──── (Reload-Timer ab) ◀──────────────── [Reloading]

  Jeder State → [Stunned] → (Stun endet) → [Idle]
```

### Vergleich: Soldier vs Sniper

| Eigenschaft | Soldier | Sniper |
|-------------|---------|--------|
| Reichweite | 6-18m | 20-50m |
| Aim-Dauer | 0.6s | 2.0s |
| Schüsse pro Zyklus | 5 (Salve) | 1 |
| Schaden pro Schuss | 10 | 80 |
| Gesamt-Schaden pro Zyklus | 50 | 80 |
| Accuracy | 85% | 95% |
| Streuung | 5° | 2° |
| Reload | 2.0s | 2.5s |
| Projektil | SoldierBullet | SniperBullet (schneller) |
| Warnlaser | Standard | Breiter, deutlicher |

### Gameplay-Rolle

Der Sniper ist eine **Distanz-Bedrohung** die den Spieler zwingt, aktiv zu werden:
- Die lange Aim-Phase (2s) mit deutlichem Warnlaser gibt genug Reaktionszeit
- Hoher Schaden bestraft Passivität
- Große Mindest-Reichweite (20m) macht den Sniper im Nahkampf verwundbar
- Ideale Kombination mit Soldiers: Soldiers drängen den Spieler, Sniper bestraft Stillstehen

### SniperBullet vs SoldierBullet

| Eigenschaft | SoldierBullet | SniperBullet |
|-------------|--------------|-------------|
| Geschwindigkeit | (aus deinem Code) | 200 m/s |
| Kollision | (aus deinem Code) | Raycast pro Frame |
| Trail | (aus deinem Code) | Optional, eigener TrailRenderer |
| Treffer-Interface | (aus deinem Code) | IDamageable.TakeDamage() |

Die SniperBullet nutzt **Raycast pro Frame** statt Trigger-Kollision. Bei 200 m/s Geschwindigkeit
würde eine normale Collider-basierte Kugel durch dünne Wände fliegen. Der Raycast prüft den
gesamten Weg den die Kugel pro Frame zurücklegt.
