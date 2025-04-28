using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;

using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(ComponentRemoverPlayMode))]
    public class ComponentRemoverPlayModeEditor : Editor
    {
        private SerializedProperty objectsToDestroy;
        private ReorderableList reorderableList;

        private void OnEnable()
        {
            // Link serialized properties
            objectsToDestroy = serializedObject.FindProperty("objectsToDestroy");
            List<float> heights = new List<float>();

            // Initialize ReorderableList for objectsToDestroy
            reorderableList = new ReorderableList(serializedObject, objectsToDestroy, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Objects/Components to Destroy");

                    // Implementing Drag-and-Drop on header
                    if (rect.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.type == EventType.DragUpdated)
                        {
                            // If the drag is over the header, change the cursor to indicate it can be dropped
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            Event.current.Use();
                        }

                        if (Event.current.type == EventType.DragPerform)
                        {
                            // If a valid drag is performed, add the dragged object to the list
                            if (DragAndDrop.objectReferences.Length > 0)
                            {
                                foreach (var draggedObject in DragAndDrop.objectReferences)
                                {
                                    if (draggedObject is GameObject || draggedObject is Component)
                                    {
                                        AddObjectToList(draggedObject);
                                    }
                                }
                            }
                            Event.current.Use();
                        }
                    }
                },
    
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    SerializedProperty element = objectsToDestroy.GetArrayElementAtIndex(index);
                    rect.y += 2;
                    float originalHeight = rect.height;  // Save original height for the item

                    // Draw the object field as a PropertyField (dragging components or GameObjects)
                    EditorGUI.PropertyField(
                        new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                        element, GUIContent.none
                    );

                    if (element.objectReferenceValue is Transform){
                        rect.y += 16;
                        EditorGUI.LabelField(
                            new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                            "W-Why are you trying to destroy a Transform?!",
                            new GUIStyle(EditorStyles.miniLabel){
                                normal = new GUIStyleState() { textColor = Color.red }
                            }
                        );
                        heights.Add(rect.height + 7); // Increase height for the warning message
                    } else {
                        heights.Add(EditorGUIUtility.singleLineHeight); // Keep the original height if no warning
                    }
                },

                elementHeightCallback = (index) => {
                    Repaint ();
                    float height = EditorGUIUtility.singleLineHeight + 4; // Default height for each element

                    // Search index on height list
                    if (index < heights.Count)
                    {
                        height = heights[index];
                    }
                    return height;
                },

                // Add button functionality
                onAddCallback = (ReorderableList list) =>
                {
                    // Optionally, add a default empty item or null reference.
                    objectsToDestroy.InsertArrayElementAtIndex(objectsToDestroy.arraySize);
                },

                // Remove button functionality
                onRemoveCallback = (ReorderableList list) =>
                {
                    if (list.index >= 0)
                    {
                        objectsToDestroy.DeleteArrayElementAtIndex(list.index);
                    }
                }
            };
        }

        public override void OnInspectorGUI()
        {
            // Draw a description box
            EditorGUILayout.HelpBox(
                "Removes specified components or game objects from the avatar during Play Mode, while preserving them in Build Mode.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            // Draw the ReorderableList for objects to remove
            reorderableList.DoLayoutList();

            // Apply any changes to the serialized object
            serializedObject.ApplyModifiedProperties();
        }

        // Helper method to add a GameObject or Component to the list
        private void AddObjectToList(Object obj)
        {
            int currentIndex = objectsToDestroy.arraySize;
            objectsToDestroy.InsertArrayElementAtIndex(currentIndex);
            SerializedProperty newObject = objectsToDestroy.GetArrayElementAtIndex(currentIndex);
            newObject.objectReferenceValue = obj;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
