using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace raspichu.vrc_tools.editor
{
    public static class DisableAndEditor
    {
        // Dictionary to store the original state of GameObjects
        private static readonly Dictionary<GameObject, string> originalTags = new Dictionary<GameObject, string>();

        [MenuItem("Tools/Pichu/Disable and Set Editor-Only #%A")]
        private static void PerformDisableAndEditorOnly()
        {
            // Get the selected GameObject(s)
            var selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                Debug.LogWarning("No GameObject selected to toggle disable and editor-only state.");
                return;
            }

            foreach (var obj in selectedObjects)
            {
                if (obj != null)
                {
                    Undo.RecordObject(obj, "Toggle Disable and Editor-Only State"); // Record action for undo
                    // Toggle state or revert to original state
                    if (!obj.activeSelf)
                    {
                        // Revert to original tag and active state
                        obj.SetActive(true); // Set to false
                        if (originalTags.TryGetValue(obj, out var originalTag))
                        {
                            obj.tag = originalTag; // Restore original tag
                            originalTags.Remove(obj); // Remove from dictionary
                        }
                    }
                    else
                    {
                        // Save the original tag if not already saved
                        if (!originalTags.ContainsKey(obj))
                        {
                            originalTags[obj] = obj.tag;
                        }

                        // Change to disabled and set to EditorOnly
                        obj.SetActive(false);
                        obj.tag = "EditorOnly";
                    }

                    EditorUtility.SetDirty(obj); // Mark the object as dirty to save changes
                }
            }
        }
    }
}
