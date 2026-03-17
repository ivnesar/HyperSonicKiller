# TimeManager — Migrationsleitfaden

## Was ist der TimeManager?
Ein Singleton der `Time.timeScale` zentral über Prioritäts-Layer verwaltet.
Stellt außerdem `GameDeltaTime` bereit — eine Alternative zu `Time.unscaledDeltaTime`,
die bei Pause und HitStop auf 0 geht.

## Setup
1. `TimeManager.cs` ins Projekt kopieren (z.B. `Assets/Systems/TimeManager.cs`)
2. Ein leeres GameObject in die Szene legen und `TimeManager` dranhängen
   — ODER: nichts tun, er erstellt sich automatisch beim ersten Zugriff

## Nötige Änderungen an bestehenden Dateien

### 1. PlayerDash.cs — Time.timeScale ersetzen

**StartAttackDash()** (Zeile ~406):
```csharp
// ALT:
Time.timeScale = dashTimeScale;

// NEU:
TimeManager.Instance.StartDashSlowMo(dashTimeScale);
```

**CompleteDash()** (Zeile ~532):
```csharp
// ALT:
Time.timeScale = 1f;

// NEU:
TimeManager.Instance.StopDashSlowMo();
```

**CancelDash()** (Zeile ~564):
```csharp
// ALT:
Time.timeScale = 1f;

// NEU:
TimeManager.Instance.StopDashSlowMo();
```

**StartSwordDash()** (Zeile ~640):
```csharp
// ALT:
Time.timeScale = dashTimeScale;

// NEU:
TimeManager.Instance.StartDashSlowMo(dashTimeScale);
```

**CompleteSwordDash()** (Zeile ~684):
```csharp
// ALT:
Time.timeScale = 1f;

// NEU:
TimeManager.Instance.StopDashSlowMo();
```

**ForceCancelSwordDash()** (Zeile ~706):
```csharp
// ALT:
Time.timeScale = 1f;

// NEU:
TimeManager.Instance.StopDashSlowMo();
```

**OnDisable()** (Zeile ~889):
```csharp
// ALT:
if (Time.timeScale != 1f)
{
    Time.timeScale = 1f;
}

// NEU:
TimeManager.Instance.StopDashSlowMo();
```

**ProcessAttackDashMovement()** — Hitstop-Freeze beachten:
```csharp
// ALT:
float moveDistance = dashSpeed * Time.unscaledDeltaTime;

// NEU:  (Damit der Dash beim HitStop auch pausiert)
if (TimeManager.Instance.IsGameTimeFrozen) return;
float moveDistance = dashSpeed * Time.unscaledDeltaTime;
```

Gleiche Änderung in **ProcessSwordDashMovement()**.

---

### 2. PlayerCore.cs — Safety-Net anpassen

**EnterState(Dead)** (Zeile ~264):
```csharp
// ALT:
Dash?.ForceCancelDash();
Time.timeScale = 1f;

// NEU:
Dash?.ForceCancelDash();
TimeManager.Instance.ClearAllLayers(); // Alles zurücksetzen bei Tod
```

---

### 3. NpcBase.cs — RotateTowardTargetUnscaled

**RotateTowardTargetUnscaled()** (Zeile ~274-286):
```csharp
// ALT:
float maxAngle = maxRotationSpeed * Time.unscaledDeltaTime;

// NEU:
float maxAngle = maxRotationSpeed * TimeManager.Instance.GameDeltaTime;
```

---

### 4. GenTwoNpc.cs — Dash Movement

**ProcessDashMovement()** (Zeile ~430):
```csharp
// ALT:
float totalMoveDistance = currentSpeed * Time.unscaledDeltaTime;

// NEU:
float totalMoveDistance = currentSpeed * TimeManager.Instance.GameDeltaTime;
```

**SetUnscaledTimer / UpdateUnscaledTimer** — falls vorhanden:
```csharp
// ALT:
unscaledTimer -= Time.unscaledDeltaTime;

// NEU:
unscaledTimer -= TimeManager.Instance.GameDeltaTime;
```

---

### 5. CameraMotionFx.cs — Punch Timer

**Update()** (Zeile ~80):
```csharp
// ALT:
punchTimer += Time.unscaledDeltaTime;

// NEU:
punchTimer += TimeManager.Instance.GameDeltaTime;
```

---

### 6. CameraShakeFx.cs — Shake Timer

**Update()** (Zeile ~76):
```csharp
// ALT:
shakeTimer -= Time.unscaledDeltaTime;

// NEU:
shakeTimer -= TimeManager.Instance.GameDeltaTime;
```

---

### 7. PlayerDashFOV.cs — FOV Smoothing

**ApplyFOV()** (Zeile ~83):
```csharp
// ALT:
Time.unscaledDeltaTime

// NEU:
TimeManager.Instance.GameDeltaTime
```

---

### 8. PlayerLook.cs — Mouse Look

**HandleLook()**: Hier KEINE Änderung nötig!
PlayerLook nutzt aktuell Time.deltaTime implizit über Input.GetAxis.
Aber: Wenn du willst dass das Umsehen bei Pause stoppt, 
musst du im Pause-Zustand das Look-Script deaktivieren 
oder einen Early-Return einbauen:

```csharp
private void Update()
{
    // Bei Pause kein Umsehen
    if (TimeManager.Instance.IsPaused) return;

    if (core.IsDead)
    {
        HandleDeathCamera();
        return;
    }
    HandleLook();
}
```

---

## Zusammenfassung: Wann welche DeltaTime?

| Situation                      | Verwende                              |
|-------------------------------|---------------------------------------|
| Gameplay-Code der bei SlowMo  | `TimeManager.Instance.GameDeltaTime`  |
| normal laufen soll, aber bei  |                                       |
| Pause/HitStop stoppen soll    |                                       |
|                               |                                       |
| UI die auch bei Pause läuft   | `Time.unscaledDeltaTime`              |
|                               |                                       |
| Physics, Partikel, NavMesh    | `Time.deltaTime` (automatisch)        |
|                               |                                       |
| Dash-Bewegung (Sonderfall)    | `Time.unscaledDeltaTime` + Guard:     |
|                               | `if (TimeManager.IsGameTimeFrozen)`   |
|                               | `    return;`                         |
