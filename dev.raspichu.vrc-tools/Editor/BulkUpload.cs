using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// VRC SDK
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;
using VRC.SDKBase.Editor.BuildPipeline;

namespace raspichu.vrc_tools.editor
{
    public enum AvatarUploadStatus
    {
        Ready,
        Waiting,
        Building,
        Uploading,
        Uploaded,
        Failed,
        Cancelled
    }

    public class BulkUpload : EditorWindow
    {
        static CancellationTokenSource GetAvatarCancellationToken = new CancellationTokenSource();
        static CancellationTokenSource BuildAndUploadCancellationToken = new CancellationTokenSource();

        private Dictionary<string, AvatarUploadStatus> avatarStatuses = new Dictionary<string, AvatarUploadStatus>();
        private Dictionary<string, string> avatarErrorMessages = new Dictionary<string, string>();
        private Vector2 scrollPosition; // Scroll position for the scroll view

        private bool isBuilderPresent = true;

        private bool isAvatarUploading = false;
        private bool isAvatarUploadingAll = false;
        private bool isAvatarUploadingCancelled = false;
        private string currentAvatarName = "";

        [MenuItem("Window/Pichu/Bulk Upload")]
        public static void ShowWindow()
        {
            BulkUpload window = GetWindow<BulkUpload>("Bulk Upload");
            window.minSize = new Vector2(400, 300); // Set minimum window size
        }

        private void OnEnable()
        {
            ResetAvatarStatus(); // Initialize or reset avatar statuses
        }

        private async void OnGUI()
        {
            GUIStyle titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };



            VRCAvatarDescriptor[] avatarsDescriptor = GetAvatarDescriptorList();

            GUILayout.Label("Upload Avatars", titleStyle);
            DrawUILine(Color.gray, 2, 10);

            if (!IsBuilderPresent())
            {
                EditorGUILayout.Space(5); // Add spacing between avatars
                EditorGUILayout.HelpBox("Please open the VRChat SDK Control Panel.", MessageType.Error, true);
                EditorGUILayout.Space(5); // Add spacing between avatars

                isBuilderPresent = false;
            }
            else
            {
                isBuilderPresent = true;
            }

            EditorGUI.BeginDisabledGroup(isAvatarUploading || !isBuilderPresent); // Disable group for Upload button
            if (GUILayout.Button($"Upload All ({avatarsDescriptor.Length})", GUILayout.Height(40)))
            {
                UploadAllAvatars();
            }
            EditorGUI.EndDisabledGroup(); // End disable group for Upload button

            EditorGUI.BeginDisabledGroup(!isAvatarUploadingAll || isAvatarUploadingCancelled || !isBuilderPresent); // Disable group for Upload button
            if (GUILayout.Button($"Cancel upload", GUILayout.Height(20)))
            {
                CancelUpload();
            }
            EditorGUI.EndDisabledGroup(); // End disable group for Upload button


            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("Avatars to Upload", EditorStyles.boldLabel);

            // Start scroll view
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            foreach (var avatar in avatarsDescriptor)
            {
                if (avatar == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();

                // Upload button on the right
                EditorGUI.BeginDisabledGroup(isAvatarUploading || !isBuilderPresent); // Disable group for Upload button
                if (GUILayout.Button("Upload", GUILayout.Width(80)))
                {
                    isAvatarUploading = true;
                    await UploadAvatar(avatar);
                    isAvatarUploading = false;
                }
                EditorGUI.EndDisabledGroup(); // End disable group for Upload button

                DrawStatusText(avatar.gameObject.name);

                GUILayout.FlexibleSpace();


                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5); // Add spacing between avatars
            }

            // End scroll view
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }


        private VRCAvatarDescriptor[] GetAvatarDescriptorList()
        {
            VRCAvatarDescriptor[] avatarsDescriptor = FindObjectsOfType<VRCAvatarDescriptor>();

            // Filter the results to only include objects that are active and not have a ignore tag (Is created by the Utils.UnityBuild process)
            avatarsDescriptor = avatarsDescriptor
            .Where(descriptor =>
                descriptor.gameObject.scene.isLoaded &&
                descriptor.gameObject.activeInHierarchy &&
                !descriptor.gameObject.name.Contains("(Clone)") &&
                !descriptor.gameObject.name.Contains("VRCF Test Copy for")
            )
            .ToArray()
            .OrderBy(descriptor => GetHierarchyPath(descriptor.gameObject.transform))
                .ToArray();
            // Sort the results by position in the hierarchy, the more close to the top in the hierarchy the more likely it is the root object
            return avatarsDescriptor;
        }

        private void ResetAvatarStatus()
        {
            avatarStatuses.Clear();
            avatarErrorMessages.Clear();
            foreach (var avatar in GetAvatarDescriptorList())
            {
                avatarStatuses.Add(avatar.gameObject.name, AvatarUploadStatus.Ready);
            }
        }

        private void DrawStatusText(string avatarName)
        {
            if (!avatarStatuses.ContainsKey(avatarName))
            {
                avatarStatuses.Add(avatarName, AvatarUploadStatus.Ready);
            }
            AvatarUploadStatus status = avatarStatuses[avatarName];
            switch (status)
            {
                case AvatarUploadStatus.Ready:
                    GUI.contentColor = new Color(0.77f, 0.76f, 0.82f, 1); // Lavender
                    GUILayout.Label($"[Ready] {avatarName}");
                    break;
                case AvatarUploadStatus.Waiting:
                    GUI.contentColor = new Color(1, 0.65f, 0, 1); // Orange
                    GUILayout.Label($"[Waiting] {avatarName}");
                    break;
                case AvatarUploadStatus.Building:
                    GUI.contentColor = new Color(0.53f, 0.81f, 0.98f, 1); // Sky Blue
                    GUILayout.Label($"[Building] {avatarName}");
                    break;
                case AvatarUploadStatus.Uploading:
                    GUI.contentColor = new Color(0.53f, 0.81f, 0.98f, 1); // Sky Blue
                    GUILayout.Label($"[Uploading] {avatarName}");
                    break;
                case AvatarUploadStatus.Uploaded:
                    GUI.contentColor = new Color(0.31f, 0.85f, 0.4f, 1); // Greenish
                    GUILayout.Label($"[Uploaded] {avatarName}");
                    break;
                case AvatarUploadStatus.Failed:
                    GUI.contentColor = new Color(1, 0, 0, 1); // Red
                    GUILayout.Label($"[Failed] {avatarName}");
                    break;
                case AvatarUploadStatus.Cancelled:
                    GUI.contentColor = new Color(1, 0, 0, 1); // Red
                    GUILayout.Label($"[Cancelled] {avatarName}");
                    break;
                default:
                    GUI.contentColor = new Color(0.77f, 0.76f, 0.82f, 1); // Lavender
                    GUILayout.Label($"[Unknown] {avatarName}");
                    break;
            }

            GUI.contentColor = Color.white; // Reset content color
        }

        private void CancelUpload()
        {
            isAvatarUploadingCancelled = true;
            bool cancelling = false;
            for (int i = 0; i < avatarStatuses.Count; i++)
            {
                if (currentAvatarName == avatarStatuses.ElementAt(i).Key)
                {
                    cancelling = true;
                    continue;
                }
                if (!cancelling) { continue; }

                SetAvatarStatus(avatarStatuses.ElementAt(i).Key, AvatarUploadStatus.Cancelled);
            }
        }

        private async Task UploadAvatar(VRCAvatarDescriptor avatarDescriptor)
        {
            string avatarName = avatarDescriptor.gameObject.name;
            currentAvatarName = avatarName;
            SetAvatarStatus(avatarName, AvatarUploadStatus.Building);

            Debug.Log($"Uploading avatar: {avatarName}");

            try
            {
                if (!IsBuilderPresent())
                {
                    throw new System.Exception("No builder found");
                }

                GameObject avatarObject = avatarDescriptor.gameObject;
                var avatarId = avatarDescriptor.GetComponent<PipelineManager>().blueprintId;
                var vrcAvatar = await VRCApi.GetAvatar(avatarId, true, cancellationToken: GetAvatarCancellationToken.Token);

                SetAvatarStatus(avatarName, AvatarUploadStatus.Uploading);
                VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder);
                await builder.BuildAndUpload(avatarObject, vrcAvatar, cancellationToken: BuildAndUploadCancellationToken.Token);

                SetAvatarStatus(avatarName, AvatarUploadStatus.Uploaded);
                Debug.Log($"Avatar {avatarName} uploaded successfully");
            }
            catch (System.Exception e)
            {
                SetAvatarStatus(avatarName, AvatarUploadStatus.Failed);
                avatarErrorMessages[avatarName] = e.Message; // Store error message
                Debug.LogError($"Avatar {avatarName} failed to upload: {e.Message}");
            }
        }

        private bool IsBuilderPresent()
        {
            return VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder);
        }

        private async void UploadAllAvatars()
        {
            isAvatarUploading = true;
            isAvatarUploadingAll = true;
            foreach (var avatar in GetAvatarDescriptorList())
            {
                SetAvatarStatus(avatar.gameObject.name, AvatarUploadStatus.Waiting);
            }
            foreach (var avatar in GetAvatarDescriptorList())
            {
                SetAvatarStatus(avatar.gameObject.name, AvatarUploadStatus.Building);
                await UploadAvatar(avatar);
                if (isAvatarUploadingCancelled)
                {
                    break;
                }
            }
            isAvatarUploading = false;
            isAvatarUploadingAll = false;
            Debug.Log("All avatars uploaded");
            isAvatarUploadingCancelled = false;
        }

        private void SetAvatarStatus(string avatarName, AvatarUploadStatus status)
        {
            if (avatarStatuses.ContainsKey(avatarName))
            {
                avatarStatuses[avatarName] = status;
            }
            else
            {
                avatarStatuses.Add(avatarName, status);
            }
        }

        private void DrawUILine(Color color, int thickness = 1, int padding = 10)
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
            r.height = thickness;
            r.y += padding / 2;
            r.x -= 2;
            r.width += 6;
            EditorGUI.DrawRect(r, color);
        }

        public static string GetHierarchyPath(Transform transform)
        {
            // Create a list to store the sibling indices at each level of the hierarchy
            var path = new List<string>();
            // Traverse up the hierarchy from the current transform to the root
            while (transform != null)
            {
                // Get the sibling index of the current transform, which is its position among its siblings.
                // Convert it to a string with leading zeros ("D4" format) to ensure correct string comparison
                // when sorting (e.g., so that "10" is not considered less than "2").
                path.Insert(0, transform.GetSiblingIndex().ToString("D4"));

                // Move up to the parent of the current transform
                transform = transform.parent;
            }
            // Join the sibling indices with slashes to create a string representation of the hierarchy path
            return string.Join("/", path);
        }
    }
}
