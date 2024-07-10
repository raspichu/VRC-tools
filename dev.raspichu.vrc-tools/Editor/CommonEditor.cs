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



    }
}