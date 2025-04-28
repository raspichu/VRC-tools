#if MA_EXISTS
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDKBase;

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Path Deleter")]
    public class PathDeleter : MonoBehaviour, IEditorOnly
    {

        public List<string> pathStrings = new List<string>();

        private void OnValidate()
        {


        }


        public void DeletePath(string avatarPath){
            // Search inside the avatar for each path on the list
            if (avatarPath == null)
            {
                Debug.LogWarning("[DeletePath] Avatar Path is not assigned.");
                return;
            }

            // Iterate over each path in the list
            for (int i = pathStrings.Count - 1; i >= 0; i--)
            {
                string path = pathStrings[i];
                if (path[0] != '/')
                {
                    path = "/" + path;
                }
                string fullPath = avatarPath + path;
                GameObject obj = GameObject.Find(fullPath);
                if (obj != null)
                {
                    // Destroy the GameObject if found
                    Debug.Log($"[DeletePath] Deleting '{fullPath}' from hierarchy.");
                    DestroyImmediate(obj);
                }
                else
                {
                    Debug.Log($"[DeletePath] Path '{fullPath}' not found in hierarchy.");
                }

                // Remove the path from the list
                pathStrings.RemoveAt(i);
            }
            // Delte self component
            DestroyImmediate(this);
        }
    }
}
#endif