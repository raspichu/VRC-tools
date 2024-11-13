using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(PathDeleter))]
    public class PathDeleterEditor : Editor
    {
        private SerializedProperty pathStrings;
        private ReorderableList reorderableList;

        private void OnEnable()
        {
            // Link serialized properties
            pathStrings = serializedObject.FindProperty("pathStrings");

            // Initialize ReorderableList
            reorderableList = new ReorderableList(serializedObject, pathStrings, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Paths to Delete"),
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    SerializedProperty element = pathStrings.GetArrayElementAtIndex(index);
                    rect.y += 2;
                    EditorGUI.PropertyField(
                        new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                        element,
                        GUIContent.none
                    );
                }
            };
        }

        public override void OnInspectorGUI()
        {
            // Draw a description box
            EditorGUILayout.HelpBox(
                "Add the relative path of the GameObjects you want to delete from the avatar.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            // Draw the ReorderableList
            reorderableList.DoLayoutList();

            // Apply any changes to the serialized object
            serializedObject.ApplyModifiedProperties();
        }
    }
}
