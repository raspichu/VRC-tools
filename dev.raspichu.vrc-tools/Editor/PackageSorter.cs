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
                // Si ya están dentro de carpetas __Category__, ignorar
                if (IsAlreadySorted(lastImportedAssets, PackageSorterCategories.Categories))
                {
                    lastImportedAssets = null;
                    return;
                }

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

        private static bool IsAlreadySorted(string[] assets, string[] categories)
        {
            foreach (var asset in assets)
            {
                if (!asset.StartsWith("Assets/"))
                    continue;

                // comprobar si el path contiene alguna categoría ya ordenada
                bool inCategory = categories.Any(cat => asset.Contains($"__{cat}__"));
                if (!inCategory)
                    return false; // si al menos uno no está en carpeta de destino, no está completamente ordenado
            }
            return true;
        }
    }

    public class PackageSorterWindow : EditorWindow
    {
        private string packageName;
        private string selectedCategory;
        private string customFolderInput;

        private string[] importedAssets;

        private Vector2 scrollPos;

        // Colors and styles
        private GUIStyle headerStyle;
        private GUIStyle assetStyle;
        private GUIStyle buttonStyle;

        private void OnEnable()
        {
            // Load the last selected category and previously entered custom folder (if any)
            selectedCategory = PackageSorterToggle.GetLastSelectedCategory();
            customFolderInput = PackageSorterToggle.GetCustomFolder(); // NEW
        }

        private void OnDestroy()
        {
            // Save the category and custom folder when the window is closed
            PackageSorterToggle.SetLastSelectedCategory(selectedCategory);
            PackageSorterToggle.SetCustomFolder(customFolderInput); // NEW
        }

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

            // Target Folder popup
            var categories = PackageSorterCategories.Categories;
            int selectedIndex = System.Array.IndexOf(categories, selectedCategory);
            if (selectedIndex < 0)
                selectedIndex = 0;

            int newSelectedIndex = EditorGUILayout.Popup(
                "Target Folder",
                selectedIndex,
                categories
            );

            if (newSelectedIndex != selectedIndex)
            {
                selectedCategory = categories[newSelectedIndex];
                // Persist the selection immediately
                PackageSorterToggle.SetLastSelectedCategory(selectedCategory);
            }

            // If user chose the "Custom" option, show an editable input for the folder name.
            if (selectedCategory == "Custom")
            {
                customFolderInput = EditorGUILayout.TextField("Custom Folder", customFolderInput);
            }

            // Determine the folder marker: for normal categories we keep the __{Category}__ wrapper,
            // for custom we use the raw customFolderInput (no __).
            string selectedCategoryParsed =
                selectedCategory == "Custom" ? customFolderInput : $"__{selectedCategory}__";

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

            // Buttons
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

            // Determine finalRoute as before...
            string commonRoute = GetCommonRoute();
            string finalRoute = commonRoute ?? packageName;

            // Decide parsed category/folder name
            string selectedCategoryParsed =
                selectedCategory == "Custom" ? customFolderInput : $"__{selectedCategory}__";
            string rootFolder = Path.Combine("Assets", selectedCategoryParsed, finalRoute);

            // Ensure root folder exists
            if (!AssetDatabase.IsValidFolder(rootFolder))
            {
                Directory.CreateDirectory(rootFolder);
                AssetDatabase.Refresh();
            }

            // Create the new folder structure and move assets (unchanged logic, but uses selectedCategoryParsed)
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
        private const string CategoryPrefKey = "Pichu_SortPackage_LastCategory";
        private const string CustomFolderPrefKey = "Pichu_SortPackage_CustomFolder"; // new

        [MenuItem("Tools/Pichu/Options/Enable Sort Imported Package")]
        private static void ToggleSortPackage()
        {
            bool current = IsEnabled();
            SetEnabled(!current);
        }

        [MenuItem("Tools/Pichu/Options/Enable Sort Imported Package", true)]
        private static bool ToggleSortPackageValidate()
        {
            // Always enable the menu
            Menu.SetChecked("Tools/Pichu/Options/Enable Sort Imported Package", IsEnabled());
            return true;
        }

        public static bool IsEnabled()
        {
            return EditorUserSettings.GetConfigValue(PrefKey) == "1";
        }

        public static void SetEnabled(bool value)
        {
            EditorUserSettings.SetConfigValue(PrefKey, value ? "1" : "0");
        }

        public static string GetLastSelectedCategory()
        {
            string lastCategory = EditorUserSettings.GetConfigValue(CategoryPrefKey);

            if (string.IsNullOrEmpty(lastCategory))
            {
                // Return the first category by default if nothing is saved
                return PackageSorterCategories.Categories.First();
            }

            return lastCategory;
        }

        public static void SetLastSelectedCategory(string category)
        {
            if (!string.IsNullOrEmpty(category))
            {
                EditorUserSettings.SetConfigValue(CategoryPrefKey, category);
            }
        }

        // Persist custom folder name for the "Custom" category
        public static string GetCustomFolder()
        {
            string custom = EditorUserSettings.GetConfigValue(CustomFolderPrefKey);
            if (string.IsNullOrEmpty(custom))
                return "CustomFolder";
            return custom;
        }

        public static void SetCustomFolder(string folder)
        {
            if (!string.IsNullOrEmpty(folder))
            {
                EditorUserSettings.SetConfigValue(CustomFolderPrefKey, folder);
            }
        }
    }

    public static class PackageSorterCategories
    {
        public static readonly string[] Categories = new string[]
        {
            "Clothes",
            "Models",
            "Shaders",
            "Scripts",
            "Hair",
            "Other",
            "Custom",
        };
    }
}
