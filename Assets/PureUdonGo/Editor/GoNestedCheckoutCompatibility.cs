#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace PureUdonGo.Editor
{
    /// <summary>
    /// Makes the committed Pure Udon Go package work when this repository is
    /// checked out below a Unity project's Assets folder (for example
    /// Assets/udon-go-ai) instead of being the Unity project root itself.
    ///
    /// The production generator intentionally writes its generated world to the
    /// project-level Assets/PureUdonGo namespace. Its committed immutable model
    /// inputs therefore also need to be visible at that project-level path.
    /// This compatibility preflight stages only non-code committed artifacts;
    /// it never duplicates C# sources or Udon programs.
    /// </summary>
    public static class GoNestedCheckoutCompatibility
    {
        // Stable GUID from GoWorldGenerator.cs.meta. Using the GUID makes this
        // independent of the repository folder name chosen by the user.
        private const string GeneratorScriptGuid = "e84ec22f64c8c944e8159f1cc2d399fa";
        private const string GeneratorRelativePath =
            "Assets/PureUdonGo/Editor/GoWorldGenerator.cs";

        private static readonly string[] ProjectLevelArtifacts =
        {
            "Assets/PureUdonGo/Model/Generated/ModelManifest.json",
            "Assets/PureUdonGo/Model/Generated/packing_manifest.json",
            "Assets/PureUdonGo/Model/Generated/tensor_manifest.json",
            "Assets/PureUdonGo/Model/Generated/tensor_reconstruction.json",
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.asset",
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.exr",
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.raw",
            "Assets/PureUdonGo/Editor/Resources/TMP Essential Resources.unitypackage"
        };

        private static bool staging;
        private static bool loggedReady;

        /// <summary>
        /// Unity calls a validation method for the existing Generate menu before
        /// invoking its execution method. This guarantees the committed model is
        /// staged even when the user clicks Generate immediately after import.
        /// </summary>
        [MenuItem(GoWorldGenerator.MenuPath, true)]
        private static bool ValidateGenerateFinalProductionScene()
        {
            EnsureProjectLevelArtifacts();
            return true;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Repair Nested Checkout Assets", false, 1201)]
        public static void RepairNestedCheckoutAssets()
        {
            int staged = EnsureProjectLevelArtifacts();
            Debug.Log("PURE_UDON_GO_NESTED_CHECKOUT_REPAIR_PASS staged=" + staged +
                " weight=" + GoWorldGenerator.WeightPath);
        }

        /// <summary>
        /// Safe to call repeatedly. Returns the number of artifacts copied on
        /// this invocation. A repository already located at the Unity project
        /// root needs no staging and returns zero.
        /// </summary>
        public static int EnsureProjectLevelArtifacts()
        {
            if (staging)
                return 0;

            staging = true;
            try
            {
                string generatorPath = Normalize(
                    AssetDatabase.GUIDToAssetPath(GeneratorScriptGuid));
                if (string.IsNullOrEmpty(generatorPath))
                {
                    Debug.LogError(
                        "PURE_UDON_GO_NESTED_CHECKOUT_FAIL generator GUID could not be resolved");
                    return 0;
                }

                string repositoryAssetPrefix = GetRepositoryAssetPrefix(generatorPath);
                if (repositoryAssetPrefix == null)
                {
                    Debug.LogError("PURE_UDON_GO_NESTED_CHECKOUT_FAIL unexpected generator path=" +
                        generatorPath);
                    return 0;
                }

                // If the repository itself is the Unity project root, the source
                // and destination paths are already identical.
                if (repositoryAssetPrefix.Length == 0)
                    return 0;

                int copied = 0;
                for (int i = 0; i < ProjectLevelArtifacts.Length; i++)
                {
                    string destination = ProjectLevelArtifacts[i];
                    string source = repositoryAssetPrefix + "/" + destination;

                    if (AssetDatabase.LoadMainAssetAtPath(source) == null)
                    {
                        Debug.LogError("PURE_UDON_GO_NESTED_CHECKOUT_FAIL missing committed source=" +
                            source);
                        continue;
                    }

                    if (AssetDatabase.LoadMainAssetAtPath(destination) != null)
                        continue;

                    EnsureAssetFolder(Parent(destination));
                    if (!AssetDatabase.CopyAsset(source, destination))
                    {
                        Debug.LogError("PURE_UDON_GO_NESTED_CHECKOUT_FAIL copy source=" + source +
                            " destination=" + destination);
                        continue;
                    }
                    copied++;
                }

                if (copied > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                Texture2D weight = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    GoWorldGenerator.WeightPath);
                if (weight == null)
                {
                    Debug.LogError("PURE_UDON_GO_NESTED_CHECKOUT_FAIL staged production weight is not loadable: " +
                        GoWorldGenerator.WeightPath + " repositoryPrefix=" + repositoryAssetPrefix);
                    return copied;
                }

                if (copied > 0 || !loggedReady)
                {
                    loggedReady = true;
                    Debug.Log("PURE_UDON_GO_NESTED_CHECKOUT_STAGE_PASS repositoryPrefix=" +
                        repositoryAssetPrefix + " staged=" + copied + " weight=" +
                        GoWorldGenerator.WeightPath + " size=" + weight.width + "x" + weight.height);
                }
                return copied;
            }
            catch (Exception exception)
            {
                Debug.LogError("PURE_UDON_GO_NESTED_CHECKOUT_FAIL " + exception);
                return 0;
            }
            finally
            {
                staging = false;
            }
        }

        private static string GetRepositoryAssetPrefix(string generatorPath)
        {
            if (string.Equals(generatorPath, GeneratorRelativePath,
                StringComparison.Ordinal))
                return string.Empty;

            string suffix = "/" + GeneratorRelativePath;
            if (!generatorPath.EndsWith(suffix, StringComparison.Ordinal))
                return null;
            return generatorPath.Substring(0, generatorPath.Length - suffix.Length);
        }

        private static void EnsureAssetFolder(string folder)
        {
            folder = Normalize(folder);
            if (string.IsNullOrEmpty(folder) || folder == "Assets" ||
                AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Parent(folder);
            EnsureAssetFolder(parent);
            string name = folder.Substring(parent.Length + 1);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static string Parent(string path)
        {
            path = Normalize(path);
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "Assets" : path.Substring(0, slash);
        }

        private static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }
    }

    /// <summary>
    /// Runs the same idempotent staging after a script/domain reload, when
    /// AssetDatabase mutations are safe. This also covers batchmode
    /// -executeMethod calls that invoke the generator directly rather than via
    /// its menu item.
    /// </summary>
    public sealed class GoNestedCheckoutPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (didDomainReload)
                GoNestedCheckoutCompatibility.EnsureProjectLevelArtifacts();
        }
    }
}
#endif
