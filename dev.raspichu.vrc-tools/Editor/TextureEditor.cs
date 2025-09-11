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

        [MenuItem("Assets/Pichu/Poiyomi Bake texture", true)]
        private static bool ValidateBake()
        {
            foreach (Object obj in Selection.objects)
            {
                if (obj is Material mat && mat.shader != null)
                {
                    string shaderName = mat.shader.name.ToLower();
                    if (shaderName.Contains("poiyomi"))
                        return true;
                }
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
        [MenuItem("Assets/Pichu/Grayscale Texture", false, 102)]
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

        [MenuItem("Assets/Pichu/Poiyomi Bake texture", false, 200)]
        private static void BakePoiyomiTexture()
        {
            foreach (Object obj in Selection.objects)
            {
                Material mat = obj as Material;
                if (mat == null)
                    continue;

                Texture2D bakedTex = BakePoiyomiMaterial(mat);
                if (bakedTex != null)
                {
                    string newPath = SaveProcessedTexture(mat, bakedTex, "_baked.png");
                    Debug.Log($"Baked texture saved at: {newPath}");
                    ResetPoiValues(mat);
                    // Assign to material
                    if (mat.HasProperty("_MainTex"))
                    {
                        // Get relative path
                        string assetPath = newPath.Replace("\\", "/"); // Change slashes

                        Debug.Log($"Assigning new texture to material: {assetPath}");

                        Texture2D newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                        if (newTex != null)
                            mat.SetTexture("_MainTex", newTex);
                    }
                }
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

        private static Texture2D BakePoiyomiMaterial(Material mat)
        {
            if (mat == null)
            {
                Debug.LogError("No material provided");
                return null;
            }

            // Obtain main texture
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            if (mainTex == null)
            {
                Debug.LogError("Material has no _MainTex");
                return null;
            }

            int width = mainTex.width;
            int height = mainTex.height;

            // Create temporary RenderTexture
            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32
            );
            RenderTexture.active = rt;

            // Blit: render the complete material
            Graphics.Blit(null, rt, mat);

            // Read pixels from RenderTexture to Texture2D
            Texture2D baked = new Texture2D(width, height, TextureFormat.RGBA32, false);
            baked.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            baked.Apply();

            // Clean up
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return baked;
        }

        private static void ResetPoiValues(Material mat)
        {
            if (mat == null)
            {
                Debug.LogError("Material nulo");
                return;
            }
            // Reset Poiyomi properties to defaults
            // Textura principal y color
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);

            // Color adjust
            if (mat.HasProperty("_MainColorAdjustToggle"))
            {
                if (mat.HasProperty("_Saturation"))
                    mat.SetFloat("_Saturation", 0f);
                if (mat.HasProperty("_MainBrightness"))
                    mat.SetFloat("_MainBrightness", 0f);
                if (mat.HasProperty("_MainGamma"))
                    mat.SetFloat("_MainGamma", 1f);

                // Hue shift
                if (mat.HasProperty("_MainHueShiftToggle"))
                {
                    if (mat.HasProperty("_MainHueShift"))
                        mat.SetFloat("_MainHueShift", 0f);
                }
            }
        }

        // Common method to save processed texture
        private static string SaveProcessedTexture(
            Object obj,
            Texture2D processed,
            string suffix = "_edit.png"
        )
        {
            if (processed == null)
                return null;

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
            return newPath;
        }
    }
}
