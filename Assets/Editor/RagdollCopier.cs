using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Editor-Tool zum Kopieren von Ragdoll-Komponenten (Rigidbody, Collider, Joints)
/// von einem Source-Prefab/GameObject auf ein Target-Prefab/GameObject.
/// Unterstützt Prefab-Assets direkt (ohne sie in die Szene ziehen zu müssen).
/// </summary>
public class RagdollCopier : EditorWindow
{
    private GameObject source;
    private GameObject target;
    private Vector2 scrollPosition;
    private List<string> logMessages = new List<string>();

    // Körperteil-Toggles
    private bool copyHead = true;
    private bool copyTorso = true;
    private bool copyArmL = true;
    private bool copyArmR = true;
    private bool copyLegL = true;
    private bool copyLegR = true;

    // Zuordnung: welche Bones gehören zu welchem Körperteil
    private static readonly Dictionary<string, string[]> BodyPartBones = new Dictionary<string, string[]>
    {
        { "Head",   new[] { "head", "neck" } },
        { "Torso",  new[] { "pelvis", "stomach", "chest" } },
        { "ArmL",   new[] { "clavicle_l", "arm_l", "forarm_l", "hand_l" } },
        { "ArmR",   new[] { "clavicle_r", "arm_r", "forarm_r", "hand_r" } },
        { "LegL",   new[] { "thigh_l", "calf_l", "foot_l", "toe_l" } },
        { "LegR",   new[] { "thigh_r", "calf_r", "foot_r", "toe_r" } },
    };

    [MenuItem("Tools/Ragdoll Copier")]
    public static void ShowWindow()
    {
        var window = GetWindow<RagdollCopier>("Ragdoll Copier");
        window.minSize = new Vector2(420, 600);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Ragdoll Component Copier", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Kopiert Ragdoll-Komponenten (Rigidbody, Collider, Joints) vom Source " +
            "auf das Target. Die Zuordnung erfolgt über übereinstimmende Bone-Namen.\n" +
            "Funktioniert direkt mit Prefab-Assets aus dem Project-Fenster.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Source & Target Felder
        source = (GameObject)EditorGUILayout.ObjectField("Source (hat Ragdoll)", source, typeof(GameObject), true);
        target = (GameObject)EditorGUILayout.ObjectField("Target (bekommt Ragdoll)", target, typeof(GameObject), true);

        EditorGUILayout.Space(10);

        // Körperteil-Toggles
        EditorGUILayout.LabelField("Körperteile", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Alle an", GUILayout.Width(80)))
        {
            copyHead = copyTorso = copyArmL = copyArmR = copyLegL = copyLegR = true;
        }
        if (GUILayout.Button("Alle aus", GUILayout.Width(80)))
        {
            copyHead = copyTorso = copyArmL = copyArmR = copyLegL = copyLegR = false;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        copyHead  = EditorGUILayout.Toggle("Kopf (head, neck)", copyHead);
        copyTorso = EditorGUILayout.Toggle("Torso (pelvis, stomach, chest)", copyTorso);
        copyArmL  = EditorGUILayout.Toggle("Arm Links (clavicle_l .. hand_l)", copyArmL);
        copyArmR  = EditorGUILayout.Toggle("Arm Rechts (clavicle_r .. hand_r)", copyArmR);
        copyLegL  = EditorGUILayout.Toggle("Bein Links (thigh_l .. toe_l)", copyLegL);
        copyLegR  = EditorGUILayout.Toggle("Bein Rechts (thigh_r .. toe_r)", copyLegR);

        EditorGUILayout.Space(10);

        // Buttons
        EditorGUI.BeginDisabledGroup(source == null || target == null);

        if (GUILayout.Button("Vorschau (Dry Run)", GUILayout.Height(30)))
        {
            logMessages.Clear();
            DoCopy(dryRun: true);
        }

        EditorGUILayout.Space(5);

        GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
        if (GUILayout.Button("Ragdoll kopieren!", GUILayout.Height(35)))
        {
            logMessages.Clear();
            DoCopy(dryRun: false);
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(5);

        EditorGUI.BeginDisabledGroup(target == null);
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Ragdoll vom Target entfernen", GUILayout.Height(25)))
        {
            logMessages.Clear();
            RemoveRagdoll();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        // Log-Bereich
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        foreach (string msg in logMessages)
        {
            EditorGUILayout.LabelField(msg, EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void Log(string message)
    {
        logMessages.Add(message);
        Debug.Log($"[RagdollCopier] {message}");
    }

    /// <summary>
    /// Prüft ob ein Bone-Name zu den aktuell aktiven Körperteilen gehört.
    /// </summary>
    private bool IsBoneEnabled(string boneName)
    {
        string lower = boneName.ToLower();

        if (copyHead && BodyPartBones["Head"].Any(b => lower == b)) return true;
        if (copyTorso && BodyPartBones["Torso"].Any(b => lower == b)) return true;
        if (copyArmL && BodyPartBones["ArmL"].Any(b => lower == b)) return true;
        if (copyArmR && BodyPartBones["ArmR"].Any(b => lower == b)) return true;
        if (copyLegL && BodyPartBones["LegL"].Any(b => lower == b)) return true;
        if (copyLegR && BodyPartBones["LegR"].Any(b => lower == b)) return true;

        return false;
    }

    /// <summary>
    /// Prüft ob ein Child-Objekt (z.B. "Foot Collider") zu einem aktiven Körperteil gehört,
    /// basierend auf dem Parent-Bone-Namen.
    /// </summary>
    private bool IsChildOfEnabledBone(Transform child)
    {
        if (child.parent == null) return false;
        return IsBoneEnabled(child.parent.name);
    }

    /// <summary>
    /// Prüft ob ein GameObject ein Prefab-Asset auf der Festplatte ist.
    /// </summary>
    private bool IsPrefabAsset(GameObject obj)
    {
        return PrefabUtility.IsPartOfPrefabAsset(obj) && !PrefabUtility.IsPartOfPrefabInstance(obj);
    }

    #region Bone Matching

    /// <summary>
    /// Baut ein Dictionary: Bone-Name -> Transform.
    /// Bei doppelten Namen wird der relative Pfad als zusätzlicher Key gespeichert.
    /// </summary>
    private Dictionary<string, Transform> BuildBoneMap(GameObject root)
    {
        var map = new Dictionary<string, Transform>();
        var allTransforms = root.GetComponentsInChildren<Transform>(true);

        // Zähle wie oft jeder Name vorkommt
        var nameCount = new Dictionary<string, int>();
        foreach (var t in allTransforms)
        {
            if (!nameCount.ContainsKey(t.name))
                nameCount[t.name] = 0;
            nameCount[t.name]++;
        }

        foreach (var t in allTransforms)
        {
            // Einfachen Namen immer als Key wenn er eindeutig ist
            if (nameCount[t.name] == 1)
            {
                map[t.name] = t;
            }
            else
            {
                // Bei Duplikaten: relativen Pfad verwenden
                string path = GetRelativePath(root.transform, t);
                map[path] = t;
            }
        }

        return map;
    }

    private string GetRelativePath(Transform root, Transform child)
    {
        var parts = new List<string>();
        var current = child;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>
    /// Findet ein passendes Transform im Target für ein Source-Transform.
    /// Prüft erst einfachen Namen, dann relativen Pfad.
    /// </summary>
    private Transform FindMatchingBone(Transform sourceBone, Dictionary<string, Transform> targetMap, GameObject sourceRoot)
    {
        // Einfachen Namen probieren
        if (targetMap.TryGetValue(sourceBone.name, out Transform match))
            return match;

        // Relativen Pfad probieren
        string path = GetRelativePath(sourceRoot.transform, sourceBone);
        if (targetMap.TryGetValue(path, out match))
            return match;

        return null;
    }

    #endregion

    #region Main Copy Logic

    private void DoCopy(bool dryRun)
    {
        if (source == target)
        {
            Log("FEHLER: Source und Target sind das gleiche Objekt!");
            return;
        }

        if (!copyHead && !copyTorso && !copyArmL && !copyArmR && !copyLegL && !copyLegR)
        {
            Log("FEHLER: Kein Körperteil ausgewählt!");
            return;
        }

        string action = dryRun ? "VORSCHAU" : "KOPIERE";
        Log($"=== {action} Ragdoll von '{source.name}' -> '{target.name}' ===");

        // Aktive Körperteile anzeigen
        var activeBodyParts = new List<string>();
        if (copyHead) activeBodyParts.Add("Kopf");
        if (copyTorso) activeBodyParts.Add("Torso");
        if (copyArmL) activeBodyParts.Add("Arm L");
        if (copyArmR) activeBodyParts.Add("Arm R");
        if (copyLegL) activeBodyParts.Add("Bein L");
        if (copyLegR) activeBodyParts.Add("Bein R");
        Log($"  Aktiv: {string.Join(", ", activeBodyParts)}");

        // Prefab-Asset Handling
        bool targetIsPrefabAsset = IsPrefabAsset(target);
        GameObject targetRoot = null;
        string targetAssetPath = null;

        if (!dryRun && targetIsPrefabAsset)
        {
            targetAssetPath = AssetDatabase.GetAssetPath(target);
            targetRoot = PrefabUtility.LoadPrefabContents(targetAssetPath);
            Log($"  Prefab-Asset geöffnet: {targetAssetPath}");
        }
        else if (!dryRun)
        {
            targetRoot = target;
            Undo.SetCurrentGroupName("Ragdoll kopieren");
        }
        else
        {
            targetRoot = target;
        }

        var targetMap = BuildBoneMap(targetRoot);

        int copiedRigidbodies = 0;
        int copiedColliders = 0;
        int copiedJoints = 0;
        int copiedChildObjects = 0;
        int skippedFiltered = 0;
        int skippedNoMatch = 0;

        // ============================================================
        // Schritt 1: Rigidbodies (müssen VOR Joints existieren)
        // ============================================================
        var sourceRigidbodies = source.GetComponentsInChildren<Rigidbody>(true);
        foreach (var srcRb in sourceRigidbodies)
        {
            if (!IsBoneEnabled(srcRb.transform.name))
            {
                skippedFiltered++;
                continue;
            }

            Transform targetBone = FindMatchingBone(srcRb.transform, targetMap, source);
            if (targetBone == null)
            {
                Log($"  KEIN MATCH: Rigidbody auf '{srcRb.transform.name}'");
                skippedNoMatch++;
                continue;
            }

            if (dryRun)
            {
                Log($"  Rigidbody -> '{targetBone.name}' (mass={srcRb.mass}, kinematic={srcRb.isKinematic})");
            }
            else
            {
                var targetRb = targetBone.GetComponent<Rigidbody>();
                if (targetRb == null)
                {
                    targetRb = AddComponent<Rigidbody>(targetBone.gameObject, targetIsPrefabAsset);
                }
                CopyRigidbody(srcRb, targetRb);
            }
            copiedRigidbodies++;
        }

        // ============================================================
        // Schritt 2: Colliders auf Bones UND Child-Collider-Objekte
        // ============================================================
        var allSourceTransforms = source.GetComponentsInChildren<Transform>(true);
        foreach (var srcTransform in allSourceTransforms)
        {
            var srcColliders = srcTransform.GetComponents<Collider>();
            if (srcColliders.Length == 0) continue;

            // Ist dieses Objekt ein bekannter Bone?
            bool isBone = IsBoneEnabled(srcTransform.name);

            // Oder ein Extra-Child unter einem aktiven Bone? (z.B. "Foot Collider" unter "foot_r")
            bool isChildOfBone = !isBone && IsChildOfEnabledBone(srcTransform);

            if (!isBone && !isChildOfBone)
            {
                skippedFiltered++;
                continue;
            }

            if (isBone)
            {
                // Normaler Bone: Collider direkt auf das passende Target-Bone kopieren
                Transform targetBone = FindMatchingBone(srcTransform, targetMap, source);
                if (targetBone == null)
                {
                    Log($"  KEIN MATCH: Collider auf '{srcTransform.name}'");
                    skippedNoMatch++;
                    continue;
                }

                foreach (var srcCol in srcColliders)
                {
                    if (dryRun)
                    {
                        Log($"  {srcCol.GetType().Name} -> '{targetBone.name}'");
                    }
                    else
                    {
                        CopyCollider(srcCol, targetBone.gameObject, targetIsPrefabAsset);
                    }
                    copiedColliders++;
                }
            }
            else
            {
                // Extra-Child (z.B. "Foot Collider"): neues Child-GameObject im Target erstellen
                Transform targetParent = FindMatchingBone(srcTransform.parent, targetMap, source);
                if (targetParent == null)
                {
                    Log($"  KEIN MATCH: Parent '{srcTransform.parent.name}' für Child '{srcTransform.name}'");
                    skippedNoMatch++;
                    continue;
                }

                // Prüfen ob gleichnamiges Child schon existiert
                Transform existingChild = targetParent.Find(srcTransform.name);
                if (existingChild != null && existingChild.GetComponent<Collider>() != null)
                    continue;

                if (dryRun)
                {
                    string colTypes = string.Join(", ", srcColliders.Select(c => c.GetType().Name));
                    Log($"  Child '{srcTransform.name}' -> unter '{targetParent.name}' ({colTypes})");
                }
                else
                {
                    var newChild = new GameObject(srcTransform.name);
                    if (!targetIsPrefabAsset)
                        Undo.RegisterCreatedObjectUndo(newChild, "Child-Collider erstellen");

                    newChild.transform.SetParent(targetParent);
                    newChild.transform.localPosition = srcTransform.localPosition;
                    newChild.transform.localRotation = srcTransform.localRotation;
                    newChild.transform.localScale = srcTransform.localScale;

                    foreach (var srcCol in srcColliders)
                    {
                        CopyCollider(srcCol, newChild, targetIsPrefabAsset);
                    }

                    // Falls das Child auch einen Rigidbody hat
                    var srcChildRb = srcTransform.GetComponent<Rigidbody>();
                    if (srcChildRb != null)
                    {
                        var newRb = AddComponent<Rigidbody>(newChild, targetIsPrefabAsset);
                        CopyRigidbody(srcChildRb, newRb);
                    }
                }
                copiedChildObjects++;
                copiedColliders += srcColliders.Length;
            }
        }

        // ============================================================
        // Schritt 3: Joints (nachdem alle Rigidbodies existieren)
        // ============================================================
        // Target-Map neu aufbauen (neue Child-Objekte sind jetzt drin)
        targetMap = BuildBoneMap(targetRoot);

        var sourceJoints = source.GetComponentsInChildren<Joint>(true);
        foreach (var srcJoint in sourceJoints)
        {
            if (!IsBoneEnabled(srcJoint.transform.name))
            {
                skippedFiltered++;
                continue;
            }

            Transform targetBone = FindMatchingBone(srcJoint.transform, targetMap, source);
            if (targetBone == null)
            {
                Log($"  KEIN MATCH: {srcJoint.GetType().Name} auf '{srcJoint.transform.name}'");
                skippedNoMatch++;
                continue;
            }

            // Connected Body im Target finden
            Rigidbody targetConnectedBody = null;
            if (srcJoint.connectedBody != null)
            {
                Transform connectedTarget = FindMatchingBone(srcJoint.connectedBody.transform, targetMap, source);
                if (connectedTarget != null)
                {
                    targetConnectedBody = connectedTarget.GetComponent<Rigidbody>();
                }

                if (targetConnectedBody == null)
                {
                    Log($"  WARNUNG: Connected Body '{srcJoint.connectedBody.name}' nicht gefunden für Joint auf '{srcJoint.transform.name}'");
                }
            }

            if (dryRun)
            {
                string connectedName = srcJoint.connectedBody != null ? srcJoint.connectedBody.name : "none";
                Log($"  {srcJoint.GetType().Name} -> '{targetBone.name}' (connected: {connectedName})");
            }
            else
            {
                CopyJoint(srcJoint, targetBone.gameObject, targetConnectedBody, targetIsPrefabAsset);
            }
            copiedJoints++;
        }

        // ============================================================
        // Zusammenfassung
        // ============================================================
        Log("");
        Log("=== Zusammenfassung ===");
        Log($"  Rigidbodies:   {copiedRigidbodies}");
        Log($"  Colliders:     {copiedColliders}");
        Log($"  Joints:        {copiedJoints}");
        Log($"  Child-Objekte: {copiedChildObjects}");
        if (skippedFiltered > 0)
            Log($"  Gefiltert (Körperteil aus): {skippedFiltered}");
        if (skippedNoMatch > 0)
            Log($"  Kein Match gefunden: {skippedNoMatch}");

        if (!dryRun)
        {
            if (targetIsPrefabAsset && targetAssetPath != null)
            {
                PrefabUtility.SaveAsPrefabAsset(targetRoot, targetAssetPath);
                PrefabUtility.UnloadPrefabContents(targetRoot);
                Log($"  Prefab gespeichert: {targetAssetPath}");
            }
            else
            {
                EditorUtility.SetDirty(target);
                Log("  FERTIG! (Undo mit Strg+Z bei Szenen-Objekten)");
            }
        }

        Repaint();
    }

    #endregion

    #region Component Copy Helpers

    private T AddComponent<T>(GameObject obj, bool isPrefabEdit) where T : Component
    {
        if (isPrefabEdit)
            return obj.AddComponent<T>();
        else
            return Undo.AddComponent<T>(obj);
    }

    private void CopyRigidbody(Rigidbody src, Rigidbody dst)
    {
        dst.mass = src.mass;
        dst.linearDamping = src.linearDamping;
        dst.angularDamping = src.angularDamping;
        dst.useGravity = src.useGravity;
        dst.isKinematic = src.isKinematic;
        dst.interpolation = src.interpolation;
        dst.collisionDetectionMode = src.collisionDetectionMode;
        dst.constraints = src.constraints;
    }

    private void CopyCollider(Collider src, GameObject targetObj, bool isPrefabEdit)
    {
        if (src is BoxCollider srcBox)
        {
            var dst = AddComponent<BoxCollider>(targetObj, isPrefabEdit);
            dst.center = srcBox.center;
            dst.size = srcBox.size;
            dst.isTrigger = srcBox.isTrigger;
            dst.sharedMaterial = srcBox.sharedMaterial;
        }
        else if (src is CapsuleCollider srcCapsule)
        {
            var dst = AddComponent<CapsuleCollider>(targetObj, isPrefabEdit);
            dst.center = srcCapsule.center;
            dst.radius = srcCapsule.radius;
            dst.height = srcCapsule.height;
            dst.direction = srcCapsule.direction;
            dst.isTrigger = srcCapsule.isTrigger;
            dst.sharedMaterial = srcCapsule.sharedMaterial;
        }
        else if (src is SphereCollider srcSphere)
        {
            var dst = AddComponent<SphereCollider>(targetObj, isPrefabEdit);
            dst.center = srcSphere.center;
            dst.radius = srcSphere.radius;
            dst.isTrigger = srcSphere.isTrigger;
            dst.sharedMaterial = srcSphere.sharedMaterial;
        }
        else if (src is MeshCollider srcMesh)
        {
            var dst = AddComponent<MeshCollider>(targetObj, isPrefabEdit);
            dst.sharedMesh = srcMesh.sharedMesh;
            dst.convex = srcMesh.convex;
            dst.isTrigger = srcMesh.isTrigger;
            dst.sharedMaterial = srcMesh.sharedMaterial;
        }
    }

    private void CopyJoint(Joint src, GameObject targetObj, Rigidbody connectedBody, bool isPrefabEdit)
    {
        if (src is ConfigurableJoint srcCJ)
        {
            var dst = AddComponent<ConfigurableJoint>(targetObj, isPrefabEdit);
            dst.connectedBody = connectedBody;
            dst.anchor = srcCJ.anchor;
            dst.axis = srcCJ.axis;
            dst.autoConfigureConnectedAnchor = srcCJ.autoConfigureConnectedAnchor;
            dst.connectedAnchor = srcCJ.connectedAnchor;
            dst.secondaryAxis = srcCJ.secondaryAxis;

            dst.xMotion = srcCJ.xMotion;
            dst.yMotion = srcCJ.yMotion;
            dst.zMotion = srcCJ.zMotion;
            dst.angularXMotion = srcCJ.angularXMotion;
            dst.angularYMotion = srcCJ.angularYMotion;
            dst.angularZMotion = srcCJ.angularZMotion;

            dst.linearLimit = srcCJ.linearLimit;
            dst.linearLimitSpring = srcCJ.linearLimitSpring;
            dst.lowAngularXLimit = srcCJ.lowAngularXLimit;
            dst.highAngularXLimit = srcCJ.highAngularXLimit;
            dst.angularXLimitSpring = srcCJ.angularXLimitSpring;
            dst.angularYLimit = srcCJ.angularYLimit;
            dst.angularZLimit = srcCJ.angularZLimit;
            dst.angularYZLimitSpring = srcCJ.angularYZLimitSpring;

            dst.xDrive = srcCJ.xDrive;
            dst.yDrive = srcCJ.yDrive;
            dst.zDrive = srcCJ.zDrive;
            dst.angularXDrive = srcCJ.angularXDrive;
            dst.angularYZDrive = srcCJ.angularYZDrive;
            dst.slerpDrive = srcCJ.slerpDrive;

            dst.targetPosition = srcCJ.targetPosition;
            dst.targetVelocity = srcCJ.targetVelocity;
            dst.targetRotation = srcCJ.targetRotation;
            dst.targetAngularVelocity = srcCJ.targetAngularVelocity;

            dst.rotationDriveMode = srcCJ.rotationDriveMode;
            dst.projectionMode = srcCJ.projectionMode;
            dst.projectionDistance = srcCJ.projectionDistance;
            dst.projectionAngle = srcCJ.projectionAngle;
            dst.configuredInWorldSpace = srcCJ.configuredInWorldSpace;
            dst.swapBodies = srcCJ.swapBodies;
            dst.breakForce = srcCJ.breakForce;
            dst.breakTorque = srcCJ.breakTorque;
            dst.enableCollision = srcCJ.enableCollision;
            dst.enablePreprocessing = srcCJ.enablePreprocessing;
            dst.massScale = srcCJ.massScale;
            dst.connectedMassScale = srcCJ.connectedMassScale;
        }
        else if (src is CharacterJoint srcCharJ)
        {
            var dst = AddComponent<CharacterJoint>(targetObj, isPrefabEdit);
            dst.connectedBody = connectedBody;
            dst.anchor = srcCharJ.anchor;
            dst.axis = srcCharJ.axis;
            dst.autoConfigureConnectedAnchor = srcCharJ.autoConfigureConnectedAnchor;
            dst.connectedAnchor = srcCharJ.connectedAnchor;
            dst.swingAxis = srcCharJ.swingAxis;
            dst.twistLimitSpring = srcCharJ.twistLimitSpring;
            dst.lowTwistLimit = srcCharJ.lowTwistLimit;
            dst.highTwistLimit = srcCharJ.highTwistLimit;
            dst.swingLimitSpring = srcCharJ.swingLimitSpring;
            dst.swing1Limit = srcCharJ.swing1Limit;
            dst.swing2Limit = srcCharJ.swing2Limit;
            dst.enableProjection = srcCharJ.enableProjection;
            dst.projectionDistance = srcCharJ.projectionDistance;
            dst.projectionAngle = srcCharJ.projectionAngle;
            dst.breakForce = srcCharJ.breakForce;
            dst.breakTorque = srcCharJ.breakTorque;
            dst.enableCollision = srcCharJ.enableCollision;
            dst.enablePreprocessing = srcCharJ.enablePreprocessing;
        }
        else if (src is HingeJoint srcHinge)
        {
            var dst = AddComponent<HingeJoint>(targetObj, isPrefabEdit);
            dst.connectedBody = connectedBody;
            dst.anchor = srcHinge.anchor;
            dst.axis = srcHinge.axis;
            dst.autoConfigureConnectedAnchor = srcHinge.autoConfigureConnectedAnchor;
            dst.connectedAnchor = srcHinge.connectedAnchor;
            dst.useSpring = srcHinge.useSpring;
            dst.spring = srcHinge.spring;
            dst.useMotor = srcHinge.useMotor;
            dst.motor = srcHinge.motor;
            dst.useLimits = srcHinge.useLimits;
            dst.limits = srcHinge.limits;
            dst.breakForce = srcHinge.breakForce;
            dst.breakTorque = srcHinge.breakTorque;
            dst.enableCollision = srcHinge.enableCollision;
            dst.enablePreprocessing = srcHinge.enablePreprocessing;
        }
        else
        {
            Log($"  WARNUNG: Joint-Typ '{src.GetType().Name}' nicht unterstützt!");
        }
    }

    #endregion

    #region Remove Ragdoll

    private void RemoveRagdoll()
    {
        Log($"=== Entferne Ragdoll von '{target.name}' ===");

        bool targetIsPrefabAsset = IsPrefabAsset(target);
        GameObject targetRoot;
        string targetAssetPath = null;

        if (targetIsPrefabAsset)
        {
            targetAssetPath = AssetDatabase.GetAssetPath(target);
            targetRoot = PrefabUtility.LoadPrefabContents(targetAssetPath);
        }
        else
        {
            targetRoot = target;
            Undo.SetCurrentGroupName("Ragdoll entfernen");
        }

        int removed = 0;

        // Reihenfolge wichtig: Joints -> Rigidbodies -> Colliders (wegen Abhängigkeiten)
        foreach (var joint in targetRoot.GetComponentsInChildren<Joint>(true))
        {
            if (!IsBoneEnabled(joint.transform.name)) continue;
            Log($"  Entferne {joint.GetType().Name} von '{joint.gameObject.name}'");
            if (targetIsPrefabAsset)
                Object.DestroyImmediate(joint);
            else
                Undo.DestroyObjectImmediate(joint);
            removed++;
        }

        foreach (var rb in targetRoot.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!IsBoneEnabled(rb.transform.name) && !IsChildOfEnabledBone(rb.transform)) continue;
            Log($"  Entferne Rigidbody von '{rb.gameObject.name}'");
            if (targetIsPrefabAsset)
                Object.DestroyImmediate(rb);
            else
                Undo.DestroyObjectImmediate(rb);
            removed++;
        }

        foreach (var col in targetRoot.GetComponentsInChildren<Collider>(true))
        {
            if (!IsBoneEnabled(col.transform.name) && !IsChildOfEnabledBone(col.transform)) continue;
            Log($"  Entferne {col.GetType().Name} von '{col.gameObject.name}'");
            if (targetIsPrefabAsset)
                Object.DestroyImmediate(col);
            else
                Undo.DestroyObjectImmediate(col);
            removed++;
        }

        if (targetIsPrefabAsset && targetAssetPath != null)
        {
            PrefabUtility.SaveAsPrefabAsset(targetRoot, targetAssetPath);
            PrefabUtility.UnloadPrefabContents(targetRoot);
            Log($"  Prefab gespeichert: {targetAssetPath}");
        }
        else
        {
            EditorUtility.SetDirty(target);
        }

        Log($"  {removed} Komponenten entfernt.");
        Repaint();
    }

    #endregion
}
