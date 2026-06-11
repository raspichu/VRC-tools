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
        private static List<string> accumulatedAssets = new List<string>();
        private static bool isImportingPackage = false;
        private static HashSet<string> preImportFolders = new HashSet<string>();
        private static Queue<(string name, string[] rootItems)> importQueue =
            new Queue<(string, string[])>();

        // Sequential batch state. ProcessNextBatch fires each import one at a time;
        // importPackageCompleted drives the next one, preventing shared-state interleaving.
        private static readonly Queue<(string path, string folder)> pendingBatch =
            new Queue<(string, string)>();
        private static string currentBatchFolder;
        private static HashSet<string> currentBatchSnapshot;
        private static bool isBatchActive;

        static AutoPackageSorter()
        {
            AssetDatabase.importPackageStarted += OnPackageImportStarted;
            AssetDatabase.importPackageCompleted += OnPackageImported;
        }

        private static void OnPackageImportStarted(string packageName)
        {
            accumulatedAssets.Clear();
            isImportingPackage = true;
            preImportFolders = new HashSet<string>(
                Directory
                    .GetDirectories(Application.dataPath, "*", SearchOption.AllDirectories)
                    .Select(d =>
                        "Assets" + d.Substring(Application.dataPath.Length).Replace("\\", "/")
                    )
            );
        }

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            if (!isImportingPackage || !PackageSorterToggle.IsEnabled())
                return;

            foreach (var asset in importedAssets)
                accumulatedAssets.Add(asset);
        }

        private static void OnPackageImported(string packageName)
        {
            isImportingPackage = false;
            Debug.Log($"[PackageSorter] importPackageCompleted: '{packageName}' | isBatchActive={isBatchActive}");

            if (isBatchActive)
            {
                var snapshot = currentBatchSnapshot;
                var folder = currentBatchFolder;
                currentBatchSnapshot = null;
                currentBatchFolder = null;
                accumulatedAssets.Clear();

                if (PackageSorterToggle.IsEnabled() && folder != null)
                {
                    var newItems = FindNewRootItemsFromSnapshot(snapshot);
                    Debug.Log($"[PackageSorter] Batch '{packageName}' → folder='{folder}' | newRoots=[{string.Join(", ", newItems)}]");
                    if (newItems.Length > 0 && !newItems.All(IsInsideCategory))
                        PerformSort(newItems, folder);
                }
                else
                {
                    Debug.Log($"[PackageSorter] Batch '{packageName}' → folder=null (None), skipping sort");
                }

                ProcessNextBatch();
                return;
            }

            if (!PackageSorterToggle.IsEnabled())
            {
                accumulatedAssets.Clear();
                return;
            }

            var allAssets = accumulatedAssets.ToArray();
            accumulatedAssets.Clear();

            if (allAssets.Length == 0)
            {
                Debug.Log($"[PackageSorter] Manual '{packageName}' → no accumulated assets, skipping");
                return;
            }

            var items = GetItemsToSort(allAssets);
            Debug.Log($"[PackageSorter] Manual '{packageName}' → rootItems=[{string.Join(", ", items)}]");

            if (items.Length == 0)
                return;

            if (items.All(IsInsideCategory))
            {
                Debug.Log($"[PackageSorter] Manual '{packageName}' → all items already inside category, skipping");
                return;
            }

            importQueue.Enqueue((packageName, items));

            if (!EditorWindow.HasOpenInstances<PackageSorterWindow>())
                ShowNextInQueue();
        }

        public static void StartBatch(IEnumerable<(string path, string folder)> items)
        {
            foreach (var item in items)
                pendingBatch.Enqueue(item);
            Debug.Log($"[PackageSorter] StartBatch: {pendingBatch.Count} item(s) queued");
            if (!isBatchActive)
                ProcessNextBatch();
        }

        private static void ProcessNextBatch()
        {
            if (pendingBatch.Count == 0)
            {
                isBatchActive = false;
                Debug.Log("[PackageSorter] Batch complete");
                return;
            }

            var (path, folder) = pendingBatch.Dequeue();
            currentBatchSnapshot = TakeFolderSnapshot();
            currentBatchFolder = folder;
            isBatchActive = true;
            Debug.Log($"[PackageSorter] ProcessNextBatch: importing '{Path.GetFileName(path)}' → folder='{folder ?? "None"}'");
            AssetDatabase.ImportPackage(path, false);
        }

        private static HashSet<string> TakeFolderSnapshot() =>
            new HashSet<string>(
                Directory.GetDirectories(Application.dataPath, "*", SearchOption.AllDirectories)
                    .Select(d => "Assets" + d.Substring(Application.dataPath.Length).Replace("\\", "/"))
            );

        private static string[] FindNewRootItemsFromSnapshot(HashSet<string> snapshot)
        {
            var current = TakeFolderSnapshot();
            var newFolders = new HashSet<string>(current.Except(snapshot));
            return newFolders
                .Where(f =>
                {
                    var parent = Path.GetDirectoryName(f)?.Replace("\\", "/");
                    return string.IsNullOrEmpty(parent) || !newFolders.Contains(parent);
                })
                .ToArray();
        }

        public static void ShowNextInQueue()
        {
            if (importQueue.Count == 0)
                return;

            var data = importQueue.Dequeue();

            // Filter out items that no longer exist (already moved by a previous sort)
            var validItems = data.rootItems.Where(item => AssetExists(item)).ToArray();

            if (validItems.Length == 0)
            {
                Debug.Log($"[PackageSorter] Skipping '{data.name}' — all items already moved.");
                ShowNextInQueue();
                return;
            }

            var window = EditorWindow.GetWindow<PackageSorterWindow>();
            window.titleContent = new GUIContent("Package Sorter");
            window.SetPackage(data.name, validItems);
            window.Show();
        }

        private static string[] GetItemsToSort(string[] assets)
        {
            var items = new HashSet<string>();
            foreach (var asset in assets)
            {
                if (!asset.StartsWith("Assets/"))
                    continue;
                var item = FindNewRoot(asset);
                if (item != null)
                    items.Add(item);
            }
            return items.ToArray();
        }

        // Walks up the path to find the highest folder that didn't exist before the import.
        // e.g. if Assets/A/B existed and Assets/A/B/C/file.mat was imported, returns Assets/A/B/C
        private static string FindNewRoot(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string newRoot = null;

            while (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets/"))
            {
                if (!preImportFolders.Contains(dir))
                    newRoot = dir;
                else
                    break;
                dir = Path.GetDirectoryName(dir)?.Replace("\\", "/");
            }

            // File placed directly inside a pre-existing folder — include the file itself
            if (newRoot == null && !AssetDatabase.IsValidFolder(assetPath))
                return assetPath;

            return newRoot;
        }

        public static bool IsInsideCategory(string assetPath)
        {
            return PackageSorterCategories
                .Categories.Where(c => c != "Custom")
                .Any(cat =>
                    assetPath == $"Assets/__{cat}__" || assetPath.StartsWith($"Assets/__{cat}__/")
                );
        }

        private static bool AssetExists(string assetPath)
        {
            var fullPath = Path.GetFullPath(assetPath);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

        public static void PerformSort(string[] items, string categoryFolder)
        {
            string destRoot = $"Assets/{categoryFolder}";
            if (!AssetDatabase.IsValidFolder(destRoot))
                AssetDatabase.CreateFolder("Assets", categoryFolder);

            var movedPaths = new List<string>();
            foreach (var item in items)
            {
                if (!AssetDatabase.IsValidFolder(item) && !File.Exists(Path.GetFullPath(item)))
                {
                    Debug.LogWarning($"[PackageSorter] Skipping missing item: {item}");
                    continue;
                }

                string relativePath = item.Substring("Assets/".Length);
                string destPath = $"{destRoot}/{relativePath}";

                if (item == destPath)
                    continue;

                EnsureFolderExists(Path.GetDirectoryName(destPath).Replace("\\", "/"));

                if (AssetDatabase.IsValidFolder(item) && AssetDatabase.IsValidFolder(destPath))
                {
                    // Destination folder already exists — merge contents rather than failing
                    MergeIntoFolder(item, destPath, movedPaths);
                }
                else
                {
                    string error = AssetDatabase.MoveAsset(item, destPath);
                    if (!string.IsNullOrEmpty(error))
                        Debug.LogError($"[PackageSorter] Failed to move {item} → {destPath}: {error}");
                    else
                    {
                        Debug.Log($"[PackageSorter] Moved {item} → {destPath}");
                        movedPaths.Add(destPath);
                    }
                }
            }

            AssetDatabase.Refresh();

            if (movedPaths.Count > 0)
            {
                var objects = movedPaths
                    .Select(p => AssetDatabase.LoadAssetAtPath<Object>(p))
                    .Where(o => o != null)
                    .ToArray();
                if (objects.Length > 0)
                {
                    Selection.objects = objects;
                    EditorGUIUtility.PingObject(objects[objects.Length - 1]);
                }
            }
        }

        public static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;
            var parent = Path.GetDirectoryName(folderPath).Replace("\\", "/");
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
        }

        // Moves all contents of source into dest recursively, merging where dest already exists.
        private static void MergeIntoFolder(string source, string dest, List<string> movedPaths)
        {
            string srcFull = Path.GetFullPath(Application.dataPath + source.Substring("Assets".Length));

            foreach (var subdirFull in Directory.GetDirectories(srcFull))
            {
                string name = Path.GetFileName(subdirFull);
                string srcSub = $"{source}/{name}";
                string dstSub = $"{dest}/{name}";
                if (AssetDatabase.IsValidFolder(dstSub))
                    MergeIntoFolder(srcSub, dstSub, movedPaths);
                else
                {
                    string err = AssetDatabase.MoveAsset(srcSub, dstSub);
                    if (string.IsNullOrEmpty(err)) { Debug.Log($"[PackageSorter] Moved {srcSub} → {dstSub}"); movedPaths.Add(dstSub); }
                    else Debug.LogError($"[PackageSorter] Failed to move {srcSub} → {dstSub}: {err}");
                }
            }

            foreach (var fileFull in Directory.GetFiles(srcFull).Where(f => !f.EndsWith(".meta")))
            {
                string name = Path.GetFileName(fileFull);
                string srcFile = $"{source}/{name}";
                string dstFile = $"{dest}/{name}";
                string err = AssetDatabase.MoveAsset(srcFile, dstFile);
                if (string.IsNullOrEmpty(err)) { Debug.Log($"[PackageSorter] Moved {srcFile} → {dstFile}"); movedPaths.Add(dstFile); }
                else Debug.LogError($"[PackageSorter] Failed to move {srcFile} → {dstFile}: {err}");
            }

            // Remove source folder if now empty (no non-meta entries)
            if (!Directory.GetFileSystemEntries(srcFull).Where(e => !e.EndsWith(".meta")).Any())
                AssetDatabase.DeleteAsset(source);
        }
    }

    public class PackageSorterWindow : EditorWindow
    {
        private string packageName;
        private string[] rootItems;
        private string selectedCategory;
        private string customFolderInput;
        private Vector2 scrollPos;

        private GUIStyle headerStyle;
        private GUIStyle itemStyle;
        private GUIStyle buttonStyle;

        private void OnEnable()
        {
            selectedCategory = PackageSorterToggle.GetLastSelectedCategory();
            customFolderInput = PackageSorterToggle.GetCustomFolder();
        }

        private void OnDestroy()
        {
            PackageSorterToggle.SetLastSelectedCategory(selectedCategory);
            PackageSorterToggle.SetCustomFolder(customFolderInput);
        }

        private void EnsureStyles()
        {
            if (headerStyle == null)
                headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            if (itemStyle == null)
                itemStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            if (buttonStyle == null)
                buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
        }

        public void SetPackage(string name, string[] items)
        {
            packageName = Path.GetFileNameWithoutExtension(name);
            rootItems = items;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(packageName))
            {
                EditorGUILayout.LabelField("No package detected.");
                return;
            }

            EnsureStyles();

            EditorGUILayout.LabelField("Package Imported:", packageName, headerStyle);
            EditorGUILayout.Space();

            var categories = PackageSorterCategories.Categories;
            int selectedIndex = System.Array.IndexOf(categories, selectedCategory);
            if (selectedIndex < 0)
                selectedIndex = 0;

            int newIndex = EditorGUILayout.Popup("Target Folder", selectedIndex, categories);
            if (newIndex != selectedIndex)
            {
                selectedCategory = categories[newIndex];
                PackageSorterToggle.SetLastSelectedCategory(selectedCategory);
            }

            if (selectedCategory == "Custom")
                customFolderInput = EditorGUILayout.TextField("Custom Folder", customFolderInput);

            EditorGUILayout.Space();

            string categoryFolder =
                selectedCategory == "Custom" ? customFolderInput : $"__{selectedCategory}__";

            EditorGUILayout.LabelField("Items to move:", headerStyle);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
            foreach (var item in rootItems)
            {
                string relativePath = item.Substring("Assets/".Length);
                EditorGUILayout.LabelField(
                    $"{item}  →  Assets/{categoryFolder}/{relativePath}",
                    itemStyle
                );
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sort Assets", buttonStyle, GUILayout.Height(30)))
                SortImportedAssets();
            if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Height(30)))
                CloseAndContinue();
            EditorGUILayout.EndHorizontal();
        }

        void SortImportedAssets()
        {
            if (rootItems == null || rootItems.Length == 0)
                return;

            string categoryFolder =
                selectedCategory == "Custom" ? customFolderInput : $"__{selectedCategory}__";
            AutoPackageSorter.PerformSort(rootItems, categoryFolder);
            CloseAndContinue();
        }

        private void CloseAndContinue()
        {
            packageName = null;
            AutoPackageSorter.ShowNextInQueue();
            if (string.IsNullOrEmpty(packageName))
                Close();
        }
    }

    public static class PackageSorterToggle
    {
        private const string PrefKey = "Pichu_SortPackage_Enabled";
        private const string CategoryPrefKey = "Pichu_SortPackage_LastCategory";
        private const string CustomFolderPrefKey = "Pichu_SortPackage_CustomFolder";

        [MenuItem("Tools/Pichu/Options/Enable Sort Imported Package")]
        private static void ToggleSortPackage()
        {
            bool current = IsEnabled();
            SetEnabled(!current);
        }

        [MenuItem("Tools/Pichu/Options/Enable Sort Imported Package", true)]
        private static bool ToggleSortPackageValidate()
        {
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
                return PackageSorterCategories.Categories.First();
            return lastCategory;
        }

        public static void SetLastSelectedCategory(string category)
        {
            if (!string.IsNullOrEmpty(category))
                EditorUserSettings.SetConfigValue(CategoryPrefKey, category);
        }

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
                EditorUserSettings.SetConfigValue(CustomFolderPrefKey, folder);
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
