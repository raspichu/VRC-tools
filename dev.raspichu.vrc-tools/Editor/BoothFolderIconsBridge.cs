using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace raspichu.vrc_tools.editor
{
    // Optional integration with the third-party "BoothFolderIcons" package (Mame2an), if the user
    // has it installed. We never reference its assembly directly (the project may not have it at
    // all) and never reimplement its scraping logic - we only look up its own public API via
    // reflection and call it, mirroring what its "Save Link & Fetch Thumbnail" button does.
    internal static class BoothFolderIconsBridge
    {
        private const string TypeName = "Mame2an.BoothFolderIcons.BoothFolderIconsWindow";

        private static bool _resolved;
        private static Type _type;
        private static MethodInfo _registrySave;
        private static MethodInfo _saveLinkFile;
        private static MethodInfo _tryFetch;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return _type != null;
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved)
                return;
            _resolved = true;

            _type = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(TypeName))
                .FirstOrDefault(t => t != null);
            if (_type == null)
                return;

            _registrySave = _type.GetMethod("Registry_Save", BindingFlags.Public | BindingFlags.Static);
            _saveLinkFile = _type.GetMethod("SaveLinkFile", BindingFlags.Public | BindingFlags.Static);
            _tryFetch = _type.GetMethod(
                "TryFetchAndSaveThumbnail",
                BindingFlags.Public | BindingFlags.Static
            );

            // API shape changed in an incompatible way (different plugin version) - treat as absent
            // rather than risk an exception on Invoke.
            if (_registrySave == null || _saveLinkFile == null || _tryFetch == null)
                _type = null;
        }

        // For every freshly-imported root item, resolves the one folder BoothFolderIcons should
        // run on and links it to boothUrl (overwriting any existing link). sortFolder is the
        // category items were just moved into ("__Clothes__" etc.), or null if nothing was sorted.
        public static void RunOnImportedItems(string[] newRootItems, string sortFolder, string boothUrl)
        {
            if (newRootItems == null || newRootItems.Length == 0 || string.IsNullOrEmpty(boothUrl))
                return;
            EnsureResolved();
            if (_type == null)
                return;

            foreach (var item in CollapseToCommonAncestor(newRootItems))
            {
                string finalPath =
                    sortFolder != null ? $"Assets/{sortFolder}/{item.Substring("Assets/".Length)}" : item;

                if (!AssetDatabase.IsValidFolder(finalPath))
                    continue; // a loose root file, not a folder - nothing to link

                Run(ResolveTargetFolder(finalPath), boothUrl);
            }
        }

        // Re-importing into an already-existing folder (e.g. a follow-up package for the same
        // product) makes each new file/subfolder its own separate "root item" instead of one -
        // if they all sit under one common ancestor folder, that ancestor is really the single
        // folder the import touched, so run once there instead of once per scattered leaf.
        private static string[] CollapseToCommonAncestor(string[] items)
        {
            if (items.Length <= 1)
                return items;

            string[] common = items[0].Split('/');
            int length = common.Length;
            foreach (var item in items.Skip(1))
            {
                string[] parts = item.Split('/');
                int max = Math.Min(length, parts.Length);
                int i = 0;
                while (i < max && common[i] == parts[i])
                    i++;
                length = i;
                if (length <= 1)
                    break;
            }

            // length <= 1 means the only shared ancestor is "Assets" itself (or nothing) - not a
            // meaningful common folder, so leave the items as separate, unrelated roots.
            if (length <= 1)
                return items;

            string ancestor = string.Join("/", common.Take(length));
            return AssetDatabase.IsValidFolder(ancestor) ? new[] { ancestor } : items;
        }

        // A package that just wraps its real content one level deep (e.g. "Item/Item_v1.0/...")
        // would otherwise get linked on the ambiguous outer wrapper every time it's re-imported -
        // if the imported folder holds exactly one subfolder and nothing else, that subfolder is
        // the real target instead.
        private static string ResolveTargetFolder(string folder)
        {
            string[] subfolders = AssetDatabase.GetSubFolders(folder);
            string fullPath = Path.GetFullPath(folder);
            bool hasRootFiles = Directory.GetFiles(fullPath).Any(f => !f.EndsWith(".meta"));
            return subfolders.Length == 1 && !hasRootFiles ? subfolders[0] : folder;
        }

        private static bool Run(string assetFolderPath, string boothUrl)
        {
            try
            {
                string guid = AssetDatabase.AssetPathToGUID(assetFolderPath);
                _registrySave.Invoke(null, new object[] { guid, assetFolderPath, boothUrl });
                _saveLinkFile.Invoke(null, new object[] { assetFolderPath, boothUrl });
                return (bool)_tryFetch.Invoke(null, new object[] { assetFolderPath, boothUrl, true });
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[BoothFolderIconsBridge] Run failed for '{assetFolderPath}': {e.Message}");
                return false;
            }
        }
    }
}
