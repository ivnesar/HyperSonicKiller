# Player System Refactoring - Übersicht

## Architektur

```
┌─────────────────────────────────────────────────────────────────┐
│                        PlayerCore                                │
│  • Zentrale Koordination aller Subsysteme                       │
│  • State Machine (Normal, Dashing, StuckToSurface, Airborne, Dead)│
│  • Public API für externe Systeme (TakeDamage, Heal, etc.)      │
│  • Events: OnStateChanged, OnPlayerDeath, OnPlayerRevive        │
└─────────────────────────────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│ PlayerMovement│    │  PlayerDash   │    │  PlayerLook   │
│ • Walk/Run    │    │ • Dash        │    │ • Mouse Look  │
│ • Sprint      │    │ • Wall Stick  │    │ • Sensitivity │
│ • Jump        │    │ • Charges     │    │ • Death Cam   │
│ • Gravity     │    │ • Time Slow   │    └───────────────┘
└───────────────┘    └───────────────┘
        │                     │
        └──────────┬──────────┘
                   ▼
        ┌───────────────────┐        ┌───────────────┐
        │   PlayerCombat    │◄──────►│  PlayerHealth │
        │ • Attack          │        │ • Base HP     │
        │ • Block + BlockHP │        │ • Heal        │
        │ • Sword Throw     │        │ • Death       │
        │ • Guard Break     │        └───────────────┘
        └───────────────────┘
                   │
                   ▼
        ┌───────────────────┐
        │ PlayerInputHandler│
        │ • Action States   │
        │ • Key Rebinding   │
        │ • Move/Look Input │
        └───────────────────┘
```

## Dateien

| Datei | Zeilen | Verantwortung |
|-------|--------|---------------|
| `PlayerCore.cs` | ~220 | Koordination, State Machine, Public API |
| `PlayerInputHandler.cs` | ~140 | Input Abstraktion |
| `PlayerMovement.cs` | ~180 | Walk, Sprint, Jump, Gravity |
| `PlayerDash.cs` | ~280 | Dash, Wall-Stick, Time Slow |
| `PlayerLook.cs` | ~80 | Kamera-Rotation |
| `PlayerCombat.cs` | ~340 | Attack, Block, Throw (EIN BlockHP!) |
| `PlayerHealth.cs` | ~100 | Nur Base HP |
| `PlayerHealthUI.cs` | ~180 | UI Display |

**Gesamt: ~1520 Zeilen** (vorher ~1600, aber viel sauberer)

## Setup in Unity

### 1. Scripts zum Projekt hinzufügen
Kopiere alle `.cs` Dateien in deinen `Assets/Scripts/Player/` Ordner.

### 2. Player GameObject Setup
Füge folgende Components zum Player hinzu (Reihenfolge wichtig!):

```
Player (GameObject)
├── CharacterController (Unity Built-in)
├── PlayerInputHandler
├── PlayerCore         ◄── Muss NACH InputHandler kommen
├── PlayerMovement
├── PlayerDash
├── PlayerLook
├── PlayerCombat
├── PlayerHealth
└── Camera (Child Object)
```

### 3. Inspector Setup

**PlayerCore:**
- Keine Einstellungen nötig (findet alles automatisch)

**PlayerMovement:**
- Walk Speed: 5
- Run Speed: 8
- Sprint Initial Boost: 12
- Jump Force: 8
- Gravity: 20

**PlayerDash:**
- Max Dash Charges: 3
- Dash Speed: 20
- Dash Max Distance: 15
- Dash Time Scale: 0.1
- Dash Surface Layer: Alles außer Player

**PlayerLook:**
- Sensitivity: 2
- Max Vertical Angle: 80

**PlayerCombat:**
- Attack Range: 3
- Attack Angle: 30
- Melee Damage: 50
- Max Block HP: 100
- Guard Break Stun Duration: 2
- Enemy Layer: Deine Enemy Layer
- Held Sword Visual: Dein Schwert-Mesh
- Thrown Sword Prefab: Dein geworfenes Schwert Prefab

**PlayerHealth:**
- Max HP: 100

### 4. UI Setup (Optional)
Erstelle ein Canvas mit:
- Health Slider + Text
- Block Slider + Text
- Status Text
- Dash Charges Text

Füge `PlayerHealthUI` zu einem GameObject und verknüpfe die UI-Elemente.

## Wichtige Änderungen

### Block HP ist jetzt EIN System
- Vorher: `SwordCombatSystem.currentBlockHP` UND `PlayerHealthSystem.currentShieldHP`
- Jetzt: NUR `PlayerCombat.currentBlockHP`

### State Machine in PlayerCore
- Vorher: In FPSPlayerController
- Jetzt: Zentralisiert in PlayerCore
- Subsysteme reagieren auf State, ändern ihn aber über Events/Callbacks

### Damage Flow
```
Enemy ruft: playerCore.TakeDamage(50)
                    │
                    ▼
            Spieler blockt?
           /              \
         JA               NEIN
          │                 │
          ▼                 ▼
PlayerCombat.TakeBlockDamage()   PlayerHealth.TakeDamage()
          │                 │
          ▼                 │
   BlockHP reicht?          │
   /           \            │
 JA            NEIN         │
  │              │          │
  ▼              ▼          │
Damage       Guard Break    │
absorbed     + Overflow ────┴───► Health nimmt Schaden
```

## Interfaces

### IDamageable
Implementiere auf Enemies:
```csharp
public class Enemy : MonoBehaviour, IDamageable
{
    public void TakeDamage(float damage)
    {
        // Handle damage
    }
}
```

### IThrownSword
Implementiere auf deinem geworfenen Schwert:
```csharp
public class ThrownSword : MonoBehaviour, IThrownSword
{
    public event Action OnRecalled;
    
    public void Initialize(Vector3 direction, float force, float maxDistance, LayerMask layers)
    {
        // Setup projectile
    }
    
    public void Recall(Transform target)
    {
        // Return to player
        OnRecalled?.Invoke();
    }
}
```

## Migration von altem Code

### INpcInteraction
Das alte Interface wird noch unterstützt für Backwards-Compatibility:
```csharp
// Alt (funktioniert weiterhin)
public class OldEnemy : MonoBehaviour, INpcInteraction
{
    public void OnMeeleDamage(int damage) { }
}

// Neu (empfohlen)
public class NewEnemy : MonoBehaviour, IDamageable
{
    public void TakeDamage(float damage) { }
}
```

### Externe Referenzen
Wenn andere Scripts auf den alten `FPSPlayerController` verweisen:
```csharp
// Alt
FPSPlayerController player = FindObjectOfType<FPSPlayerController>();
player.TakeDamage(50);

// Neu
PlayerCore player = FindObjectOfType<PlayerCore>();
player.TakeDamage(50);
```

## Erweiterbarkeit

Das System ist so designt, dass du einfach neue Features hinzufügen kannst:

### Neues Subsystem hinzufügen
1. Erstelle `PlayerNewFeature.cs` mit `[RequireComponent(typeof(PlayerCore))]`
2. Füge Property in `PlayerCore` hinzu: `public PlayerNewFeature NewFeature { get; private set; }`
3. Initialisiere in `PlayerCore.Awake()`: `NewFeature = GetComponent<PlayerNewFeature>();`

### Neuen State hinzufügen
1. Erweitere `PlayerCore.PlayerState` enum
2. Füge Handling in `EnterState()` und `ExitState()` hinzu
3. Aktualisiere `CanMove`, `CanDash`, etc. Properties

---

Bei Fragen oder Problemen, frag einfach! 🎮
