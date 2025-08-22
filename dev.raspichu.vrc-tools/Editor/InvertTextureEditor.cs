using UnityEngine;
using UnityEditor;
using System.IO;

using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    public class InvertTextureEditor
    {
        // Validador: only enable if at least one texture is selected
        [MenuItem("Assets/Pichu/Invert Texture", true)]
        private static bool ValidateInvertTexture()
        {
            foreach (Object obj in Selection.objects)
            {
                if (obj is Texture2D)
                    return true; // at least one texture is selected
            }
            return false; // no texture selected
        }

        // Context Menu
        [MenuItem("Assets/Pichu/Invert Texture", false, 100)]
        private static void InvertSelectedTextures()
        {
            Object[] selectedObjects = Selection.objects;

            foreach (Object obj in selectedObjects)
            {
                Texture2D texture = obj as Texture2D;
                if (texture == null) continue;

                InvertTexture(texture);
            }
        }

        private static void InvertTexture(Texture2D texture)
        {
            if (texture == null) return;

            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Texture2D invertedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            Color[] pixels = texture.GetPixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                c.r = 1f - c.r;
                c.g = 1f - c.g;
                c.b = 1f - c.b;
                pixels[i] = c;
            }

            invertedTexture.SetPixels(pixels);
            invertedTexture.Apply();

            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            string newPath = Path.Combine(directory, fileName + "_inverted.png");

            File.WriteAllBytes(newPath, invertedTexture.EncodeToPNG());
            AssetDatabase.Refresh();

            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            Debug.Log($"Inverted texture saved at: {newPath}");
        }


    }
}
