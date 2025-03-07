using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

using raspichu.vrc_tools.component;
using raspichu.vrc_tools.editor;
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
                ChangeColliderReference[] changeColliders = avatarGameObject.GetComponentsInChildren<ChangeColliderReference>();
                if (changeColliders.Length > 0)
                {
                    Debug.Log($"Found {changeColliders.Length} ChangeColliderReference components on the avatar.");
                    foreach (var changeCollider in changeColliders)
                    {
                        changeCollider.ApplyColliderChanges();
                    }
                }

                // Enforce blendshapes
                EnforceBlendshape[] enforceBlendshapes = avatarGameObject.GetComponentsInChildren<EnforceBlendshape>();
                if (enforceBlendshapes.Length > 0)
                {
                    Debug.Log($"[PI] Found {enforceBlendshapes.Length} EnforceBlendshape components on the avatar.");
                    foreach (var enforceBlendshape in enforceBlendshapes)
                    {
                        enforceBlendshape.GenerateSelectedBlendShapes();
                    }
                }
            });

        InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run("PichuOnBuild_Transforming", ctx=>
            {
                GameObject avatarGameObject = ctx.AvatarRootObject;
                PathDeleter[] pathDeleters = avatarGameObject.GetComponentsInChildren<PathDeleter>();
                if (pathDeleters.Length > 0)
                {
                    Debug.Log($"[PI] Found {pathDeleters.Length} PathDeleter components on the avatar.");
                    string fullPath = CommonEditor.GetFullPath(avatarGameObject);
                    foreach (var pathDeleter in pathDeleters)
                    {
                        pathDeleter.DeletePath(fullPath);
                    }
                }
            });
    }
}