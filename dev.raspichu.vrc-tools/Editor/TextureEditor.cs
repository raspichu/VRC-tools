using System.IO;
using raspichu.vrc_tools.component;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    public class TextureEditor
    {
        // Validators
        [MenuItem("Assets/Pichu/Invert Texture", true)]
        [MenuItem("Assets/Pichu/Grayscale Texture", true)]
        private static bool ValidateSelection()
        {
            foreach (Object obj in Selection.objects)
            {
                if (obj is Texture2D)
                    return true;
            }
            return false;
        }

        // Invert Texture
        [MenuItem("Assets/Pichu/Invert Texture", false, 100)]
        private static void InvertSelectedTextures()
        {
            foreach (Object obj in Selection.objects)
            {
                Texture2D texture = obj as Texture2D;
                if (texture == null)
                    continue;

                Texture2D inverted = ProcessTexture(texture, InvertPixels);
                SaveProcessedTexture(texture, inverted, "_inverted.png");
            }
        }

        // Grayscale texture
        [MenuItem("Assets/Pichu/Grayscale Texture", false, 101)]
        private static void GrayscaleSelectedTextures()
        {
            foreach (Object obj in Selection.objects)
            {
                Texture2D texture = obj as Texture2D;
                if (texture == null)
                    continue;

                Texture2D gray = ProcessTexture(texture, GrayscalePixels);
                SaveProcessedTexture(texture, gray, "_grayscale.png");
            }
        }

        private static Texture2D ProcessTexture(
            Texture2D texture,
            System.Func<Color[], Color[]> pixelOperation
        )
        {
            if (texture == null || pixelOperation == null)
                return null;

            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return null;

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            // Process pixels
            Color[] pixels = texture.GetPixels();
            Color[] newPixels = pixelOperation(pixels);

            Texture2D newTex = new Texture2D(
                texture.width,
                texture.height,
                TextureFormat.RGBA32,
                false
            );
            newTex.SetPixels(newPixels);
            newTex.Apply();

            // Restore original settings
            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return newTex;
        }

        private static Color[] InvertPixels(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                c.r = 1f - c.r;
                c.g = 1f - c.g;
                c.b = 1f - c.b;
                pixels[i] = c;
            }
            return pixels;
        }

        private static Color[] GrayscalePixels(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                c.r = c.g = c.b = luminance;
                pixels[i] = c;
            }
            return pixels;
        }

        // Common method to save processed texture
        private static void SaveProcessedTexture(
            Object obj,
            Texture2D processed,
            string suffix = "_edit.png"
        )
        {
            if (processed == null)
                return;

            // Obtener ruta de carpeta
            string directory = Application.dataPath; // default
            string fileName = ""; // default

            if (obj != null)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                    directory = Path.GetDirectoryName(path);
                fileName = obj.name;
            }

            string newPath = Path.Combine(directory, fileName + suffix);

            // Numeración automática
            int counter = 1;
            while (File.Exists(newPath))
            {
                string numberedSuffix =
                    Path.GetFileNameWithoutExtension(suffix)
                    + "_"
                    + counter
                    + Path.GetExtension(suffix);
                newPath = Path.Combine(directory, fileName + numberedSuffix);
                counter++;
            }

            File.WriteAllBytes(newPath, processed.EncodeToPNG());
            AssetDatabase.Refresh();

            Debug.Log($"Texture saved at: {newPath}");
        }
    }
}
