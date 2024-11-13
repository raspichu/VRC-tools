using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace raspichu.vrc_tools.editor
{
    public static class CommonEditor
    {

        public static VRCAvatarDescriptor GetVRCAvatarDescriptors(GameObject gameObject)
        {
            // Go to parent until (Including self) you find the VRCVRCAvatarDescriptor component
            Transform parent = gameObject.transform;
            while (parent != null)
            {
                VRCAvatarDescriptor VRCAvatarDescriptors = parent.GetComponent<VRCAvatarDescriptor>();
                if (VRCAvatarDescriptors != null)
                {
                    return VRCAvatarDescriptors;
                }
                parent = parent.parent;
            }
            return null;
        }

        public static string GetFullPath(this GameObject gameObject)
        {
            string path = gameObject.name;
            Transform current = gameObject.transform;

            // Traverse up the hierarchy
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

    }
}