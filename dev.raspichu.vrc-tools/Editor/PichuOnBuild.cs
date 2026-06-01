using System;
using raspichu.vrc_tools.component;
using raspichu.vrc_tools.editor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
#if MA_EXISTS
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(PichuOnBuild))]

public class PichuOnBuild : Plugin<PichuOnBuild>
{
    protected override void Configure()
    {
        // PHASE 1: Generating (Early cleanup and setup)
        InPhase(BuildPhase.Generating)
            .BeforePlugin("nadena.dev.modular-avatar")
            .BeforePlugin("Goorm.SeeThroughHair.NDMFPlugin")
            .BeforePlugin("com.anatawa12.avatar-optimizer")
            .BeforePlugin("ShellProtectorNDMFPlugin")
            .Run(
                "PichuOnBuild_Generating1",
                ctx =>
                {
                    // Run ComponentRemoverPlayMode
                    GameObject avatarRoot = ctx.AvatarRootObject;
                    var removers = avatarRoot.GetComponentsInChildren<ComponentRemoverPlayMode>(
                        true
                    );
                    if (removers.Length > 0)
                    {
                        Debug.Log(
                            $"[PI] Found {removers.Length} ComponentRemoverPlayMode components on the avatar."
                        );
                        foreach (var remover in removers)
                        {
                            remover.RemoveObjects();
                        }
                    }
                }
            );

        InPhase(BuildPhase.Generating)
            .BeforePlugin("nadena.dev.modular-avatar")
            .Run(
                "PichuOnBuild_Generating2",
                ctx =>
                {
                    Debug.Log("Doing something before Modular Avatar");
                    GameObject avatarGameObject = ctx.AvatarRootObject;

                    // Change colliders
                    ChangeColliderReference[] changeColliders =
                        avatarGameObject.GetComponentsInChildren<ChangeColliderReference>();
                    if (changeColliders.Length > 0)
                    {
                        Debug.Log(
                            $"Found {changeColliders.Length} ChangeColliderReference components on the avatar."
                        );
                        foreach (var changeCollider in changeColliders)
                        {
                            changeCollider.ApplyColliderChanges();
                        }
                    }

                    // NOTA: Se ha removido EnforceBlendshape de aquí para evitar conflictos de orden con scripts de terceros.
                }
            );

        // PHASE 2: Transforming (After third-party scripts have processed the mesh layout)
        InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .AfterPlugin("com.anatawa12.avatar-optimizer") // Ensures we run after optimizer features if present
            .Run(
                "PichuOnBuild_Transforming_EnforceBlendshapes",
                ctx =>
                {
                    GameObject avatarGameObject = ctx.AvatarRootObject;

                    // Enforce blendshapes using the final optimized and corrected mesh result from MMDEyeFix/MA
                    EnforceBlendshape[] enforceBlendshapes =
                        avatarGameObject.GetComponentsInChildren<EnforceBlendshape>();
                    if (enforceBlendshapes.Length > 0)
                    {
                        Debug.Log(
                            $"[PI] Found {enforceBlendshapes.Length} EnforceBlendshape components on the avatar. Executing AFTER third-party scripts."
                        );
                        foreach (var enforceBlendshape in enforceBlendshapes)
                        {
                            enforceBlendshape.GenerateSelectedBlendShapes();
                        }
                    }
                }
            );

        InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run(
                "PichuOnBuild_Transforming_PathDeleters",
                ctx =>
                {
                    GameObject avatarGameObject = ctx.AvatarRootObject;
                    PathDeleter[] pathDeleters =
                        avatarGameObject.GetComponentsInChildren<PathDeleter>();
                    if (pathDeleters.Length > 0)
                    {
                        Debug.Log(
                            $"[PI] Found {pathDeleters.Length} PathDeleter components on the avatar."
                        );
                        string fullPath = CommonEditor.GetFullPath(avatarGameObject);
                        foreach (var pathDeleter in pathDeleters)
                        {
                            pathDeleter.DeletePath(fullPath);
                        }
                    }
                }
            );
    }
}
#endif
