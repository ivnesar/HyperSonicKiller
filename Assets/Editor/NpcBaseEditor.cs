using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// NPC BASE EDITOR - Blendet irrelevante geerbte Felder im Inspector aus
// ════════════════════════════════════════════════════════════════════════════
//
// Funktionsweise:
//   1. Gilt automatisch für NpcBase und ALLE Subklassen
//   2. Jede Subklasse kann HiddenBaseFields überschreiben
//   3. Gelistete Felder werden im Inspector nicht gezeichnet
//   4. Die Werte bleiben serialisiert (Default-Werte bleiben erhalten)
//
// Verwendung in einer Subklasse:
//   public override string[] HiddenBaseFields => new[]
//   {
//       "moveSpeed", "stoppingDistance", "maxRotationSpeed"
//   };
//
// ════════════════════════════════════════════════════════════════════════════

[CustomEditor(typeof(NpcBase), true)]  // true = gilt auch für Subklassen
[CanEditMultipleObjects]
public class NpcBaseEditor : Editor
{
    private HashSet<string> hiddenFields;

    private void OnEnable()
    {
        hiddenFields = new HashSet<string>();

        if (target is NpcBase npc)
        {
            string[] hidden = npc.HiddenBaseFields;
            if (hidden != null)
            {
                foreach (string field in hidden)
                    hiddenFields.Add(field);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        if (hiddenFields.Count == 0)
        {
            // Keine Felder versteckt → normales Verhalten
            DrawDefaultInspector();
            return;
        }

        serializedObject.Update();

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            // "m_Script" immer zeichnen (das Script-Feld oben im Inspector)
            if (property.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(property);
                continue;
            }

            // Versteckte Felder überspringen
            if (hiddenFields.Contains(property.name))
                continue;

            EditorGUILayout.PropertyField(property, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
