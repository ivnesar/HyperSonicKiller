#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Editor for MainMenuUI.
/// Adds a Scene drag-and-drop field next to each LevelEntry's sceneName,
/// so you don't have to type scene names manually.
/// 
/// SETUP:
/// Place this file in an "Editor" folder anywhere in your Assets.
/// e.g. Assets/Scripts/Editor/MainMenuUIEditor.cs
/// </summary>
[CustomEditor(typeof(MainMenuUI))]
public class MainMenuUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector first
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Scene Assignment Helper", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag scene files below to auto-fill the scene names in your Level Entries.",
            MessageType.Info);

        // Get the levels array
        SerializedProperty levelsProperty = serializedObject.FindProperty("levels");

        if (levelsProperty == null || !levelsProperty.isArray) return;

        for (int i = 0; i < levelsProperty.arraySize; i++)
        {
            SerializedProperty entry = levelsProperty.GetArrayElementAtIndex(i);
            SerializedProperty sceneNameProp = entry.FindPropertyRelative("sceneName");
            SerializedProperty displayNameProp = entry.FindPropertyRelative("displayName");

            string label = displayNameProp != null ? displayNameProp.stringValue : $"Level {i + 1}";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);

            // Show a SceneAsset field (editor-only)
            SceneAsset currentScene = null;
            if (!string.IsNullOrEmpty(sceneNameProp.stringValue))
            {
                // Try to find the scene asset by name
                string[] guids = AssetDatabase.FindAssets(
                    $"t:SceneAsset {sceneNameProp.stringValue}");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                    if (asset != null && asset.name == sceneNameProp.stringValue)
                    {
                        currentScene = asset;
                        break;
                    }
                }
            }

            SceneAsset newScene = (SceneAsset)EditorGUILayout.ObjectField(
                currentScene, typeof(SceneAsset), false);

            if (newScene != currentScene)
            {
                sceneNameProp.stringValue = newScene != null ? newScene.name : "";
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
