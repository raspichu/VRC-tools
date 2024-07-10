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
            // #### Change colliders ####
            ChangeColliderReference changeCollider = avatarGameObject.GetComponentInChildren<ChangeColliderReference>();

            if (changeCollider != null)
            {
                Debug.Log("Found ChangeColliderReference component on the avatar.");
                changeCollider.ApplyColliderChanges();
            }

            // #### Enforce blendshapes ####
            EnforceBlendshape enforceBlendshape = avatarGameObject.GetComponentInChildren<EnforceBlendshape>();
            if (enforceBlendshape != null)
            {
                Debug.Log("Found EnforceBlendshape component on the avatar.");
                enforceBlendshape.GenerateSelectedBlendShapes();
            }


            return true;
        }
    }
}