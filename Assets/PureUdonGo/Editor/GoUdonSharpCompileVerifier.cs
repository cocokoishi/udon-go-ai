#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Developer-only gate for the real VRChat/UdonSharp toolchain. The
    /// command deliberately uses the package compiler rather than a proxy C#
    /// harness, then checks that each discovered U# program has a serialized
    /// Udon program asset.
    /// </summary>
    public static class GoUdonSharpCompileVerifier
    {
        private const string TmpEssentialsPackagePath =
            "Assets/PureUdonGo/Editor/Resources/TMP Essential Resources.unitypackage";

[MenuItem("Tools/Pure Udon Go/Tests/Import TMP Essentials For Validation")]
        public static void ImportTmpEssentialsForValidation()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
            {
                Debug.Log("PURE_UDON_GO_TMP_VALIDATION_PASS shader=present");
                EditorApplication.Exit(0);
                return;
            }

            AssetDatabase.importPackageCompleted += OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed += OnTmpEssentialsImportFailed;
            AssetDatabase.ImportPackage(TmpEssentialsPackagePath, false);
            Debug.Log("PURE_UDON_GO_TMP_VALIDATION_IMPORT_STARTED path=" + TmpEssentialsPackagePath);
        }

        private static void OnTmpEssentialsImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed -= OnTmpEssentialsImportFailed;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            bool present = Shader.Find("TextMeshPro/Mobile/Distance Field") != null;
            Debug.Log("PURE_UDON_GO_TMP_VALIDATION_RESULT package=" + packageName + " shader=" + present);
            EditorApplication.Exit(present ? 0 : 1);
        }

        private static void OnTmpEssentialsImportFailed(string packageName, string error)
        {
            AssetDatabase.importPackageCompleted -= OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed -= OnTmpEssentialsImportFailed;
            Debug.LogError("PURE_UDON_GO_TMP_VALIDATION_FAIL package=" + packageName + " error=" + error);
            EditorApplication.Exit(1);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Real UdonSharp Compile")]
        public static void VerifyRealUdonSharpCompile()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions
            {
                IsEditorBuild = true,
                ConcurrentBuild = false,
                DisableLogging = false
            });

            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset[] programs = UdonSharpProgramAsset.GetAllUdonSharpPrograms();
            bool hasCompilerError = UdonSharpProgramAsset.AnyUdonSharpScriptHasError();
            int missingSource = 0;
            int missingSerializedProgram = 0;
            int compiled = 0;

            for (int i = 0; i < programs.Length; i++)
            {
                UdonSharpProgramAsset program = programs[i];
                if (program == null) continue;
                if (program.sourceCsScript == null)
                {
                    missingSource++;
                    continue;
                }

                if (program.GetSerializedUdonProgramAsset() == null)
                    missingSerializedProgram++;
                else
                    compiled++;
            }

            string result = string.Format(
                "PURE_UDON_GO_UDONSHARP_COMPILE_RESULT programs={0} compiled={1} missingSource={2} missingSerialized={3} compilerError={4}",
                programs.Length, compiled, missingSource, missingSerializedProgram, hasCompilerError);

            if (hasCompilerError || missingSource > 0 || missingSerializedProgram > 0)
                throw new InvalidOperationException(result);

            Debug.Log(result);
            Debug.Log("PURE_UDON_GO_UDONSHARP_COMPILE_PASS");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Generate And Verify Real Udon World")]
        public static void GenerateAndVerifyRealUdonWorld()
        {
            PureUdonGo.Editor.GoWorldGenerator.GenerateFinalProductionScene();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Scene scene = EditorSceneManager.GetSceneByPath(
                PureUdonGo.Editor.GoWorldGenerator.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");

            GameObject[] roots = scene.GetRootGameObjects();
            int udonBehaviours = 0;
            int generatedRoots = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                if (roots[i].GetComponent<GoGeneratedWorld>() != null)
                    generatedRoots++;
                udonBehaviours += roots[i].GetComponentsInChildren<UdonBehaviour>(true).Length;
            }

            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions
            {
                IsEditorBuild = true,
                ConcurrentBuild = false,
                DisableLogging = false
            });
            AssetDatabase.SaveAssets();

            UdonSharpProgramAsset[] programs = UdonSharpProgramAsset.GetAllUdonSharpPrograms();
            bool hasCompilerError = UdonSharpProgramAsset.AnyUdonSharpScriptHasError();
            int compiled = 0;
            for (int i = 0; i < programs.Length; i++)
            {
                if (programs[i] != null && programs[i].sourceCsScript != null &&
                    programs[i].GetSerializedUdonProgramAsset() != null)
                    compiled++;
            }

            string result = string.Format(
                "PURE_UDON_GO_UDON_WORLD_RESULT scene={0} generatedRoots={1} udonBehaviours={2} programs={3} compiled={4} compilerError={5}",
                scene.path, generatedRoots, udonBehaviours, programs.Length, compiled, hasCompilerError);

            if (generatedRoots != 1 || udonBehaviours == 0 || hasCompilerError || compiled == 0)
                throw new InvalidOperationException(result);

            Debug.Log(result);
            Debug.Log("PURE_UDON_GO_UDON_WORLD_PASS");
        }
    }
}
#endif
