using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    public class FindMissingScripts : EditorWindow
    {
        private List<GameObject> objectsWithMissingScripts = new List<GameObject>();
        private Vector2 scrollPos;

        [MenuItem("Tools/Pichu/Find Missing Scripts In Scene")]
        public static void ShowWindow()
        {
            GetWindow<FindMissingScripts>("Find Missing Scripts");
        }

        private void OnDisable() => EditorApplication.update -= CleanupNullEntries;

        private void OnEnable()
        {
            EditorApplication.update += CleanupNullEntries;
            FindInCurrentScene();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawFindButton();
            DrawObjectsList();
            DrawActions();
        }

        #region GUI Methods

        private void DrawHeader()
        {
            GUILayout.Space(10);

            GUIStyle subHeaderStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
            };

            // Draw colored background for header
            EditorGUILayout.LabelField(
                "Scan for GameObjects with missing scripts.",
                subHeaderStyle
            );
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void DrawFindButton()
        {
            GUIStyle findButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
            };

            if (
                GUILayout.Button(
                    "🔍 Find Missing Scripts in Scene",
                    findButtonStyle,
                    GUILayout.Height(30)
                )
            )
            {
                FindInCurrentScene();
            }
        }

        private void DrawObjectsList()
        {
            GUILayout.Space(5);

            GUIStyle listHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
            };

            EditorGUILayout.LabelField(
                $"Objects with Missing Scripts ({objectsWithMissingScripts.Count})",
                listHeaderStyle
            );

            // Scrollable list with shaded background
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(5, 5, 5, 5);
            boxStyle.normal.background = Texture2D.grayTexture;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
            EditorGUILayout.BeginVertical(boxStyle);

            if (objectsWithMissingScripts.Count > 0)
            {
                foreach (var go in objectsWithMissingScripts)
                {
                    GUIStyle itemButtonStyle = new GUIStyle(GUI.skin.button)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontStyle = FontStyle.Bold,
                    };

                    if (GUILayout.Button(go.name, itemButtonStyle))
                    {
                        Selection.activeGameObject = go;
                        EditorGUIUtility.PingObject(go);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No objects with missing scripts found.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawActions()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            GUIStyle actionButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 25,
            };

            // "Select All" button: enabled if there are objects with missing scripts
            GUI.enabled = objectsWithMissingScripts.Count > 0;
            if (GUILayout.Button("Select All Objects with Missing Scripts", actionButtonStyle))
            {
                SelectObjectsWithMissingScripts();
            }

            GUILayout.Space(5);

            // "Remove" button: only enabled if selected objects contain missing scripts
            bool selectionHasMissing = false;
            foreach (var go in Selection.gameObjects)
            {
                if (HasMissingScripts(go))
                {
                    selectionHasMissing = true;
                    break;
                }
            }

            GUI.enabled = selectionHasMissing;
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = Color.red;

            if (GUILayout.Button("Remove Missing Scripts from Selected Objects", actionButtonStyle))
            {
                RemoveMissingScriptsFromSelectedObjects();
                FindInCurrentScene();
            }

            GUI.backgroundColor = originalColor;
            GUI.enabled = true; // reset GUI
        }

        #endregion

        #region Logic Methods

        private void CleanupNullEntries()
        {
            for (int i = objectsWithMissingScripts.Count - 1; i >= 0; i--)
            {
                if (objectsWithMissingScripts[i] == null)
                    objectsWithMissingScripts.RemoveAt(i);
            }
        }

        private void FindInCurrentScene()
        {
            objectsWithMissingScripts.Clear();
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject go in allObjects)
            {
                if (
                    !EditorUtility.IsPersistent(go.transform.root.gameObject)
                    && go.hideFlags == HideFlags.None
                )
                {
                    var components = go.GetComponents<Component>();
                    if (HasMissingScripts(go) && !objectsWithMissingScripts.Contains(go))
                    {
                        objectsWithMissingScripts.Add(go);
                    }
                }
            }
        }

        private bool HasMissingScripts(GameObject go)
        {
            return go.GetComponents<Component>().Any(c => c == null);
        }

        private void SelectObjectsWithMissingScripts()
        {
            if (objectsWithMissingScripts.Count > 0)
                Selection.objects = objectsWithMissingScripts.ToArray();
        }

        private void RemoveMissingScriptsFromSelectedObjects()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            foreach (GameObject go in selectedObjects)
            {
                // Register undo for the whole GameObject (all components)
                Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");

                // Remove missing scripts safely
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        #endregion
    }
}
