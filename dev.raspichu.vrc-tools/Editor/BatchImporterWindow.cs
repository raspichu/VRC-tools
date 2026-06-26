using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    public class BatchImporterWindow : EditorWindow
    {
        private class ImportItem
        {
            public string SourcePath;
            public bool IsZip;
            public Dictionary<string, bool> InternalPackages = new Dictionary<string, bool>();
            public bool IsExpanded = true;
            public string SortCategory = "None";
            public string SortCustomFolder = "CustomFolder";
        }

        private List<ImportItem> importQueue = new List<ImportItem>();
        private Vector2 scrollPos;
        private bool isProcessing = false;
        private string bulkSortCategory = "None";

        private GUIStyle itemLabelStyle;
        private GUIStyle foldoutStyle;
        private GUIStyle warningStyle;

        [MenuItem("Tools/Pichu/Batch Package Importer", false, 41)]
        public static void ShowWindow()
        {
            BatchImporterWindow window = GetWindow<BatchImporterWindow>("Batch Importer");
            window.titleContent = new GUIContent(
                "Batch Importer",
                EditorGUIUtility.IconContent("d_Package Manager").image
            );
            window.minSize = new Vector2(450, 500);
        }

        private void OnGUI()
        {
            InitStyles();
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Pichu Tools - Batch Importer", EditorStyles.boldLabel);
            // EditorGUILayout.HelpBox(
            //     "Individual packages will always be imported. Use checkboxes inside ZIPs to filter content.",
            //     MessageType.None
            // );

            EditorGUILayout.Space(5);

            Rect dropArea = GUILayoutUtility.GetRect(0f, 80f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "FILE DROP AREA", GetDropAreaStyle());
            HandleDragAndDrop(dropArea);

            EditorGUILayout.Space(10);

            if (
                GUILayout.Button(
                    new GUIContent(
                        "  Browse Files...",
                        EditorGUIUtility.IconContent("FolderOpened Icon").image
                    ),
                    GUILayout.Height(28)
                )
            )
            {
                string path = EditorUtility.OpenFilePanelWithFilters(
                    "Select Packages",
                    "",
                    new string[] { "Package/Zip files", "unitypackage,zip" }
                );
                if (!string.IsNullOrEmpty(path))
                    AddFileToQueue(path);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Queue ({importQueue.Count} items)",
                EditorStyles.boldLabel
            );
            if (PackageSorterToggle.IsEnabled() && importQueue.Count > 0)
            {
                var sortOptions = new[] { "None" }.Concat(PackageSorterCategories.Categories).ToArray();
                int bulkIdx = System.Array.IndexOf(sortOptions, bulkSortCategory);
                if (bulkIdx < 0) bulkIdx = 0;
                EditorGUILayout.LabelField("Set all:", GUILayout.Width(45));
                int newBulkIdx = EditorGUILayout.Popup(bulkIdx, sortOptions, GUILayout.Width(90));
                if (newBulkIdx != bulkIdx)
                {
                    bulkSortCategory = sortOptions[newBulkIdx];
                    foreach (var item in importQueue)
                        item.SortCategory = bulkSortCategory;
                }
            }
            EditorGUILayout.EndHorizontal();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            for (int i = 0; i < importQueue.Count; i++)
            {
                DrawImportItem(importQueue[i], i);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5);
            DrawBottomButtons();
            EditorGUILayout.Space(10);
        }

        private void InitStyles()
        {
            if (itemLabelStyle == null)
            {
                itemLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                };
            }
            if (foldoutStyle == null)
            {
                foldoutStyle = new GUIStyle(EditorStyles.label)
                {
                    fixedWidth = 15,
                    alignment = TextAnchor.MiddleCenter,
                };
            }
            if (warningStyle == null)
            {
                warningStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                    fontStyle = FontStyle.Italic,
                };
            }
        }

        private void DrawImportItem(ImportItem item, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            if (item.IsZip)
            {
                string arrow = item.IsExpanded ? "▼" : "▶";
                if (GUILayout.Button(arrow, foldoutStyle))
                    item.IsExpanded = !item.IsExpanded;

                var content = new GUIContent(
                    Path.GetFileName(item.SourcePath),
                    EditorGUIUtility.IconContent("d_Folder Icon").image
                );
                // Usamos itemLabelStyle también aquí para consistencia
                EditorGUILayout.LabelField(content, EditorStyles.boldLabel, GUILayout.Height(20));
            }
            else
            {
                var content = new GUIContent(
                    Path.GetFileName(item.SourcePath),
                    EditorGUIUtility.IconContent("d_BuildSettings.Editor.Small").image
                );

                // --- CAMBIO AQUÍ: Limitamos el ancho para que no pise el botón ---
                // Usamos un ancho máximo basado en el ancho de la ventana actual menos un margen para el botón
                float labelWidth = position.width - 80f;
                EditorGUILayout.LabelField(
                    content,
                    itemLabelStyle,
                    GUILayout.Height(20),
                    GUILayout.Width(labelWidth)
                );
            }

            // El FlexibleSpace ahora sí empujará el botón correctamente al final
            GUILayout.FlexibleSpace();

            GUI.color = new Color(1f, 0.3f, 0.3f);
            if (
                GUILayout.Button(
                    "X",
                    EditorStyles.miniButton,
                    GUILayout.Width(22),
                    GUILayout.Height(18)
                )
            )
            {
                importQueue.RemoveAt(index);
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            // --- ZIP CONTENTS ---
            if (item.IsZip && item.IsExpanded)
            {
                if (item.InternalPackages.Count == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(25);
                    EditorGUILayout.LabelField(
                        "No .unitypackage files found inside.",
                        warningStyle
                    );
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    var keys = new List<string>(item.InternalPackages.Keys);
                    foreach (var pkgName in keys)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(20);
                        item.InternalPackages[pkgName] = EditorGUILayout.Toggle(
                            item.InternalPackages[pkgName],
                            GUILayout.Width(18)
                        );

                        GUIContent pkgContent = new GUIContent(
                            pkgName,
                            EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image
                        );
                        EditorGUILayout.LabelField(
                            pkgContent,
                            itemLabelStyle,
                            GUILayout.Height(18)
                        );
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.Space(2);
            }
            if (PackageSorterToggle.IsEnabled())
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(item.IsZip ? 20 : 4);
                EditorGUILayout.LabelField("Move to:", GUILayout.Width(50));
                var sortOptions = new[] { "None" }
                    .Concat(PackageSorterCategories.Categories)
                    .ToArray();
                int sortIdx = System.Array.IndexOf(sortOptions, item.SortCategory);
                if (sortIdx < 0)
                    sortIdx = 0;
                item.SortCategory = sortOptions[
                    EditorGUILayout.Popup(sortIdx, sortOptions, GUILayout.Width(90))
                ];
                if (item.SortCategory != "None")
                {
                    string destFolder =
                        item.SortCategory == "Custom"
                            ? item.SortCustomFolder
                            : $"__{item.SortCategory}__";
                    EditorGUILayout.LabelField($"→ Assets/{destFolder}/", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                if (item.SortCategory == "Custom")
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(item.IsZip ? 20 : 4);
                    item.SortCustomFolder = EditorGUILayout.TextField(
                        "Custom:",
                        item.SortCustomFolder
                    );
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBottomButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All", GUILayout.Width(80), GUILayout.Height(30)))
                importQueue.Clear();

            // Total is: sum of all standalones + sum of selected items in Zips
            int total =
                importQueue.Count(i => !i.IsZip)
                + importQueue.Where(i => i.IsZip).Sum(i => i.InternalPackages.Values.Count(v => v));

            GUI.enabled = total > 0 && !isProcessing;
            GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            if (
                GUILayout.Button(
                    $"IMPORT ({total})",
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(30)
                )
            )
            {
                ProcessAllFiles();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void AddFileToQueue(string path)
        {
            if (importQueue.Any(x => x.SourcePath == path))
                return;
            string ext = Path.GetExtension(path).ToLower();
            ImportItem newItem = new ImportItem { SourcePath = path, IsZip = (ext == ".zip") };

            if (newItem.IsZip)
            {
                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(path))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (
                                entry.FullName.EndsWith(
                                    ".unitypackage",
                                    System.StringComparison.OrdinalIgnoreCase
                                )
                            )
                                newItem.InternalPackages.Add(entry.Name, true);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[BatchImporter] ZIP Error: {e.Message}");
                }
            }
            newItem.SortCategory = bulkSortCategory;
            importQueue.Add(newItem);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event evt = Event.current;
            if (!dropArea.Contains(evt.mousePosition))
                return;
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (string path in DragAndDrop.paths)
                    {
                        string ext = Path.GetExtension(path).ToLower();
                        if (ext == ".unitypackage" || ext == ".zip")
                            AddFileToQueue(path);
                    }
                }
                evt.Use();
            }
        }

        private void ProcessAllFiles()
        {
            isProcessing = true;
            string tempPath = Path.Combine(Application.temporaryCachePath, "VRCBatchImport");
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
            Directory.CreateDirectory(tempPath);

            try
            {
                var batchItems = new List<(string path, string folder)>();

                foreach (var item in importQueue)
                {
                    string resolvedFolder =
                        item.SortCategory == "None" ? null :
                        item.SortCategory == "Custom" ? item.SortCustomFolder :
                        $"__{item.SortCategory}__";

                    if (item.IsZip)
                    {
                        using (ZipArchive archive = ZipFile.OpenRead(item.SourcePath))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (
                                    item.InternalPackages.TryGetValue(entry.Name, out bool isSelected)
                                    && isSelected
                                )
                                {
                                    string dest = Path.Combine(tempPath, entry.Name);
                                    entry.ExtractToFile(dest, true);
                                    batchItems.Add((dest, resolvedFolder));
                                }
                            }
                        }
                    }
                    else
                    {
                        batchItems.Add((item.SourcePath, resolvedFolder));
                    }
                }

                importQueue.Clear();
                // StartBatch drives imports one at a time via the importPackageCompleted callback chain,
                // so each snapshot is taken after the previous package has been fully processed and sorted.
                AutoPackageSorter.StartBatch(batchItems);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private GUIStyle GetDropAreaStyle()
        {
            return new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.7f, 0.7f, 0.7f)
                        : Color.gray,
                },
            };
        }
    }
}
