#if MA_EXISTS
using UnityEditor;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Component Remover Play Mode")]
    public class ComponentRemoverPlayMode : MonoBehaviour, IEditorOnly
    {
        [Tooltip("List of components or GameObjects to destroy during Play Mode.")]
        public List<Object> objectsToDestroy = new List<Object>();

        public void RemoveObjects()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying){
                foreach (var obj in objectsToDestroy)
                {
                    DestroyImmediate(obj);
                    Debug.Log($"[ComponentRemoverPlayMode] Destroyed {obj.GetType().Name} on {gameObject.name} during Play Mode.");
                }
            }
#endif
            // Destroy self component
            DestroyImmediate(this);
        }
    }
}
#endif


