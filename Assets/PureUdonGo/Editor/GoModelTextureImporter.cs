#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Keeps the committed FP32 weight atlas in the exact import mode required
    /// by the shader runtime. This is intentionally an importer rule rather
    /// than a manual inspector step.
    /// </summary>
    public sealed class GoModelTextureImporter : AssetPostprocessor
    {
        public const string WeightTextureAssetPath = "Assets/PureUdonGo/Model/Generated/weights_rgba32f.exr";

        private void OnPreprocessTexture()
        {
            if (assetPath != WeightTextureAssetPath)
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 0;
            importer.npotScale = TextureImporterNPOTScale.None;

            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.name = "DefaultTexturePlatform";
            platform.overridden = true;
            platform.format = TextureImporterFormat.RGBAFloat;
            platform.compressionQuality = 0;
            importer.SetPlatformTextureSettings(platform);
        }

        public static bool IsConfigured(TextureImporter importer, out string failure)
        {
            if (importer == null)
            {
                failure = "weight texture has no TextureImporter";
                return false;
            }

            var platform = importer.GetDefaultPlatformTextureSettings();
            if (importer.sRGBTexture)
            {
                failure = "sRGB must be disabled";
                return false;
            }
            if (importer.mipmapEnabled)
            {
                failure = "mipmaps must be disabled";
                return false;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                failure = "texture compression must be None/Uncompressed";
                return false;
            }
            if (importer.filterMode != FilterMode.Point)
            {
                failure = "filter mode must be Point";
                return false;
            }
            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                failure = "wrap mode must be Clamp";
                return false;
            }
            if (!platform.overridden || platform.format != TextureImporterFormat.RGBAFloat)
            {
                failure = "default platform format must be RGBAFloat";
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}
#endif
