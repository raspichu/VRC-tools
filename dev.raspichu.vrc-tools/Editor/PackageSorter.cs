using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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
            string[] movedFromAssetPaths
        )
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
        private string selectedCategory = "Clothes";
        private string[] categories = new string[]
        {
            "Clothes",
            "Models",
            "Shaders",
            "Scripts",
            "Hair",
            "Other",
        };

        private string[] importedAssets;

        private Vector2 scrollPos;

        // Colors and styles
        private GUIStyle headerStyle;
        private GUIStyle assetStyle;
        private GUIStyle buttonStyle;

        private void EnsureStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            }
            if (assetStyle == null)
            {
                assetStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            }
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            }
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

            // adds _ at the start and end __{selectedCategory}__

            // Target Folder
            int selectedIndex = System.Array.IndexOf(categories, selectedCategory);
            selectedIndex = EditorGUILayout.Popup("Target Folder", selectedIndex, categories);
            selectedCategory = categories[selectedIndex];

            string selectedCategoryParsed = $"__{selectedCategory}__";
            string finalRoute = GetCommonRoute() ?? packageName;
            string previewPath = Path.Combine("Assets", selectedCategoryParsed, finalRoute);
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

        string GetCommonRoute()
        {
            if (importedAssets == null || importedAssets.Length == 0)
            {
                return null;
            }

            // We check if there is a common root folder
            string commonRoot = null;

            if (importedAssets.Length > 0)
            {
                // Split all paths into segments and store them
                var splitPaths = importedAssets
                    .Where(path => path.StartsWith("Assets/"))
                    .Select(path => path.Substring("Assets/".Length).Split('/'))
                    .ToList();

                if (splitPaths.Count > 0)
                {
                    // Start with the first path segments as a baseline
                    var firstSegments = splitPaths[0];
                    int commonLength = firstSegments.Length;

                    // Compare with each other path
                    foreach (var segments in splitPaths)
                    {
                        int i = 0;
                        // Compare each segment level
                        while (
                            i < commonLength
                            && i < segments.Length
                            && segments[i] == firstSegments[i]
                        )
                        {
                            i++;
                        }
                        commonLength = i;
                        if (commonLength == 0)
                            break; // No common folder at all
                    }

                    if (commonLength > 0)
                    {
                        commonRoot = string.Join("/", firstSegments.Take(commonLength));
                    }
                }
            }

            return commonRoot;
        }

        void DeleteEmptyFolders(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
                return;

            // Get subfolders
            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            foreach (var sub in subFolders)
            {
                DeleteEmptyFolders(sub);
            }

            // If folder is empty, delete it
            string[] assets = AssetDatabase.FindAssets("", new[] { folderPath });
            if (assets.Length == 0)
            {
                AssetDatabase.DeleteAsset(folderPath);
            }
        }

        void SortImportedAssets()
        {
            if (importedAssets == null || importedAssets.Length == 0)
            {
                return;
            }

            // We check if there is a common root folder
            string commonRoute = GetCommonRoute();
            string finalRoute = commonRoute ?? packageName;

            if (importedAssets.Length > 0)
            {
                // Split all paths into segments and store them
                var splitPaths = importedAssets
                    .Where(path => path.StartsWith("Assets/"))
                    .Select(path => path.Substring("Assets/".Length).Split('/'))
                    .ToList();

                if (splitPaths.Count > 0)
                {
                    // Start with the first path segments as a baseline
                    var firstSegments = splitPaths[0];
                    int commonLength = firstSegments.Length;

                    // Compare with each other path
                    foreach (var segments in splitPaths)
                    {
                        int i = 0;
                        // Compare each segment level
                        while (
                            i < commonLength
                            && i < segments.Length
                            && segments[i] == firstSegments[i]
                        )
                        {
                            i++;
                        }
                        commonLength = i;
                        if (commonLength == 0)
                            break; // No common folder at all
                    }

                    if (commonLength > 0)
                    {
                        finalRoute = string.Join("/", firstSegments.Take(commonLength));
                    }
                }
            }

            string selectedCategoryParsed = $"__{selectedCategory}__";
            string rootFolder = Path.Combine("Assets", selectedCategoryParsed, finalRoute);

            // Ensure root folder exists
            if (!AssetDatabase.IsValidFolder(rootFolder))
            {
                Directory.CreateDirectory(rootFolder);
                AssetDatabase.Refresh();
            }

            // Create the new folder structure and move assets
            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.StartsWith("Assets/"))
                    continue;

                string relativePath = assetPath.Substring("Assets/".Length);
                if (relativePath == finalRoute)
                {
                    continue;
                }

                // Remove the commonRoot prefix if present
                if (!string.IsNullOrEmpty(finalRoute) && relativePath.StartsWith(finalRoute + "/"))
                {
                    int firstSlash = relativePath.IndexOf('/');
                    if (firstSlash >= 0)
                        relativePath = relativePath.Substring(firstSlash + 1);
                    else
                        relativePath = ""; // Only root folder
                }

                string newPath = Path.Combine(
                        "Assets",
                        selectedCategoryParsed,
                        finalRoute,
                        relativePath
                    )
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

                Debug.Log(
                    $"[PI] Moving {assetPath} to Assets/{selectedCategoryParsed}/{finalRoute}/{relativePath}"
                );

                AssetDatabase.MoveAsset(assetPath, newPath);
            }
            foreach (var assetPath in importedAssets)
            {
                DeleteEmptyFolders(assetPath);
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
