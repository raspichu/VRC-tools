using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

using raspichu.vrc_tools.component;
using nadena.dev.ndmf;


[assembly: ExportsPlugin(typeof(PichuOnBuild))]

public class PichuOnBuild : Plugin<PichuOnBuild>
{
    protected override void Configure()
    {
        InPhase(BuildPhase.Generating)
            .BeforePlugin("nadena.dev.modular-avatar")
            .Run("Do something", ctx =>
            {
                Debug.Log("Doing something before Modular Avatar");
                GameObject avatarGameObject = ctx.AvatarRootObject;

                // Change colliders
                ChangeColliderReference changeCollider = avatarGameObject.GetComponentInChildren<ChangeColliderReference>();
                if (changeCollider != null)
                {
                    Debug.Log("Found ChangeColliderReference component on the avatar.");
                    changeCollider.ApplyColliderChanges();
                }

                // Enforce blendshapes
                EnforceBlendshape enforceBlendshape = avatarGameObject.GetComponentInChildren<EnforceBlendshape>();
                if (enforceBlendshape != null)
                {
                    Debug.Log("[PI] Found EnforceBlendshape component on the avatar.");
                    enforceBlendshape.GenerateSelectedBlendShapes();
                }
            });

        InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run("PichuOnBuild_Transforming", ctx=>
            {
                GameObject avatarGameObject = ctx.AvatarRootObject;
                PathDeleter pathDeleter = avatarGameObject.GetComponentInChildren<PathDeleter>();
                if (pathDeleter != null)
                {
                    Debug.Log("[PI] Found PathDeleter component on the avatar.");
                    pathDeleter.DeletePath(CommonEditor.GetFullPath(avatarGameObject));
                }
            });
    }
}