using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace raspichu.vrc_tools.editor
{
    [InitializeOnLoad]
    public class AutoPackageSorter : AssetPostprocessor
    {
        // Temporary list to store the recently imported assets
        private static string[] lastImportedAssets;

        static AutoPackageSorter()
        {
            // This is called when a package import is completed
            AssetDatabase.importPackageCompleted += OnPackageImported;
        }

        private static void OnPackageImported(string packageName)
        {
            if (!PackageSorterToggle.IsEnabled())
            {
                return;
            }
            
            // We open the window and pass the assets that were imported in the last operation
            if (lastImportedAssets != null && lastImportedAssets.Length > 0)
            {
                PackageSorterWindow window = EditorWindow.GetWindow<PackageSorterWindow>();
                window.titleContent = new GUIContent("Package Sorter");
                window.SetPackage(packageName, lastImportedAssets);
                window.Show();
            }

            // We clear the list for the next import
            lastImportedAssets = null;
        }

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!PackageSorterToggle.IsEnabled())
            {
                return;
            }

            // We save temporarily the imported assets
            if (importedAssets.Length > 0)
            {
                lastImportedAssets = importedAssets;
            }
        }
    }

    public class PackageSorterWindow : EditorWindow
    {
        private string packageName;
        private string selectedCategory = "Models";
        private string[] categories = new string[] { "Clothes", "Models", "Other" };

        private string[] importedAssets;

        private Vector2 scrollPos;

        // Colors and styles
        private GUIStyle headerStyle;
        private GUIStyle assetStyle;
        private GUIStyle buttonStyle;


        private void EnsureStyles()
    {
        if (headerStyle == null)
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        if (assetStyle == null)
            assetStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        if (buttonStyle == null)
            buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
    }

        public void SetPackage(string name, string[] assets)
        {
            packageName = name;
            importedAssets = assets;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(packageName))
            {
                EditorGUILayout.LabelField("No package detected.", headerStyle);
                return;
            }

            EnsureStyles();

            EditorGUILayout.LabelField("Package Imported:", packageName, headerStyle);
            EditorGUILayout.Space();

            // Target Folder
            int selectedIndex = System.Array.IndexOf(categories, selectedCategory);
            selectedIndex = EditorGUILayout.Popup("Target Folder", selectedIndex, categories);
            selectedCategory = categories[selectedIndex];

            string previewPath = Path.Combine("Assets", selectedCategory, packageName);
            EditorGUILayout.LabelField("Destination:", previewPath, EditorStyles.helpBox);

            EditorGUILayout.Space();

            // List of imported assets
            if (importedAssets != null && importedAssets.Length > 0)
            {
                EditorGUILayout.LabelField("Assets to move:", headerStyle);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                foreach (var asset in importedAssets)
                {
                    EditorGUILayout.LabelField(asset, assetStyle);
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.LabelField("No assets detected.", assetStyle);
            }

            EditorGUILayout.Space();

            // Botones
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sort Assets", buttonStyle, GUILayout.Height(30)))
            {
                SortImportedAssets();
            }

            if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Height(30)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        void SortImportedAssets()
        {
            if (importedAssets == null || importedAssets.Length == 0) return;

            string rootFolder = Path.Combine("Assets", selectedCategory, packageName);

            // Create folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(rootFolder))
            {
                Directory.CreateDirectory(rootFolder);
                AssetDatabase.Refresh();
            }


            // We check if there is a common root folder, so we don't end up duplicating the folder with the same name
            string commonRoot = null;
            if (importedAssets.Length > 0)
            {
                foreach (var assetPath in importedAssets)
                {
                    if (!assetPath.StartsWith("Assets/")) continue;

                    string[] segments = assetPath.Substring("Assets/".Length).Split('/');
                    if (segments.Length == 0) continue;

                    string root = segments[0];
                    if (commonRoot == null)
                    {
                        commonRoot = root;
                    }
                    else if (commonRoot != root)
                    {
                        commonRoot = null;
                        break;
                    }
                }
            }

            // We create the new folder structure
            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.StartsWith("Assets/")) continue;

                string relativePath = assetPath.Substring("Assets/".Length);

                // We delete the first segment if it matches packageName and is the only root folder
                if (commonRoot != null && commonRoot == packageName)
                {
                    int firstSlash = relativePath.IndexOf('/');
                    if (firstSlash >= 0)
                        relativePath = relativePath.Substring(firstSlash + 1);
                    else
                        relativePath = ""; // Only root folder
                }

                string newPath = Path.Combine("Assets", selectedCategory, packageName, relativePath)
                                    .Replace("\\", "/");

                // Create necessary folders
                string newFolder = Path.GetDirectoryName(newPath);
                if (!AssetDatabase.IsValidFolder(newFolder))
                {
                    string[] folders = newFolder.Split('/');
                    string current = folders[0];
                    for (int i = 1; i < folders.Length; i++)
                    {
                        string next = current + "/" + folders[i];
                        if (!AssetDatabase.IsValidFolder(next))
                            AssetDatabase.CreateFolder(current, folders[i]);
                        current = next;
                    }
                }

                AssetDatabase.MoveAsset(assetPath, newPath);
            }

            // After moving all assets
            if (!string.IsNullOrEmpty(commonRoot))
            {
                string originalRoot = Path.Combine("Assets", commonRoot).Replace("\\", "/");
                if (AssetDatabase.IsValidFolder(originalRoot))
                {
                    // Check if empty
                    string[] contents = AssetDatabase.FindAssets("", new[] { originalRoot });
                    if (contents.Length == 0)
                    {
                        AssetDatabase.DeleteAsset(originalRoot);
                    }
                }
            }

            AssetDatabase.Refresh();
            Close();
        }
    }


    public static class PackageSorterToggle
    {
        private const string PrefKey = "Pichu_SortPackage_Enabled";

        [MenuItem("Tools/Pichu/Enable Sort Imported Package")]
        private static void ToggleSortPackage()
        {
            bool current = EditorPrefs.GetBool(PrefKey, false);
            EditorPrefs.SetBool(PrefKey, !current);
        }

        [MenuItem("Tools/Pichu/Enable Sort Imported Package", true)]
        private static bool ToggleSortPackageValidate()
        {
            bool enabled = EditorPrefs.GetBool(PrefKey, false);
            Menu.SetChecked("Tools/Pichu/Enable Sort Imported Package", enabled);
            return true;
        }

        public static bool IsEnabled()
        {
            return EditorPrefs.GetBool(PrefKey, true);
        }
    }
}