using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    internal class PichuOnBuild : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -2048;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            ChangeColliderReference changeCollider = avatarGameObject.GetComponentInChildren<ChangeColliderReference>();

            if (changeCollider != null)
            {
                Debug.Log("Found ChangeColliderReference component on the avatar.");
                // Perform any necessary actions with changeCollider here

                changeCollider.ApplyColliderChanges();
            }

            else
            {
                Debug.LogWarning("ChangeColliderReference component not found on the avatar.");
            }

            return true;
        }
    }
}