using System;
using System.Runtime.InteropServices;
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
                VRCAvatarDescriptor VRCAvatarDescriptors =
                    parent.GetComponent<VRCAvatarDescriptor>();
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

        public static void ShowNotification(string message, bool sound = false)
        {
            // Sound logic is independent: if sound is true, it will play regardless of the message
            if (sound)
            {
                EditorApplication.Beep();
            }
            // Only attempt to show the visual notification if a message is provided
            if (!string.IsNullOrEmpty(message))
            {
                // Try to get the currently focused window
                EditorWindow targetWindow = EditorWindow.focusedWindow;
                targetWindow?.ShowNotification(new GUIContent(message), 3.0f);
            }
        }
    }
}

public class StatusPopup : EditorWindow
{
    private string currentStatus = "Processing...";
    private Color currentStatusColor = Color.white;
    private IntPtr windowHandle = IntPtr.Zero;

    // --- Windows API ---
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags
    );

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // SWP_NOSIZE (0x0001) | SWP_NOMOVE (0x0002) | SWP_NOACTIVATE (0x0010)
    // El flag NOACTIVATE es clave para que no robe el foco cada vez que se ejecuta
    private const uint TOPMOST_FLAGS = 0x0001 | 0x0002 | 0x0010;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public static StatusPopup Open(string title)
    {
        StatusPopup window = GetWindow<StatusPopup>(true, title, true);
        window.minSize = new Vector2(400, 80);
        window.maxSize = new Vector2(400, 80);
        window.ShowUtility();

        // Registering the update event in the editor
        EditorApplication.update += window.KeepOnTop;

        return window;
    }

    private void KeepOnTop()
    {
        // Get handle if we don't have it yet
        if (windowHandle == IntPtr.Zero)
        {
            windowHandle = GetActiveWindow();
        }

        // Only call SetWindowPos if the window is still open
        if (windowHandle != IntPtr.Zero)
        {
            SetWindowPos(windowHandle, HWND_TOPMOST, 0, 0, 0, 0, TOPMOST_FLAGS);
        }
    }

    private void OnDestroy()
    {
        // Important: unregister to avoid memory leaks or errors
        EditorApplication.update -= KeepOnTop;
    }

    public void UpdateStatus(string message, Color color)
    {
        currentStatus = message;
        currentStatusColor = color;
        this.Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(25);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            wordWrap = true,
            fontStyle = FontStyle.Bold,
        };

        GUI.contentColor = currentStatusColor;
        EditorGUILayout.LabelField(currentStatus, labelStyle);
        GUI.contentColor = Color.white;
    }
}
