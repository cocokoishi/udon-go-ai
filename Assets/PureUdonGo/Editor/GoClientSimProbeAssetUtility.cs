#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Creates and serializes temporary UdonSharp probes in both supported
    /// package layouts: project-level Assets/PureUdonGo and the production
    /// repository's nested Assets/udon-go-ai/Assets/PureUdonGo checkout.
    /// </summary>
    internal static class GoClientSimProbeAssetUtility
    {
        public static string EnsureProgramAsset(Type probeType, string description)
        {
            if (probeType == null)
                throw new ArgumentNullException(nameof(probeType));

            MonoScript sourceScript = FindSourceScript(probeType);
            if (sourceScript == null)
                throw new InvalidOperationException(
                    description + " probe source is missing under the Unity Assets database");

            string sourcePath = AssetDatabase.GetAssetPath(sourceScript).Replace('\\', '/');
            string programPath = Path.Combine(
                Path.GetDirectoryName(sourcePath),
                Path.GetFileNameWithoutExtension(sourcePath) + ".asset").Replace('\\', '/');
            UdonSharpProgramAsset programAsset =
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);

            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = sourceScript;
                AssetDatabase.CreateAsset(programAsset, programPath);
            }
            else
            {
                programAsset.sourceCsScript = sourceScript;
                EditorUtility.SetDirty(programAsset);
            }

            // A newly-created asset starts as Unknown. The source is already
            // current; only the real compiler may set CompiledVersion.
            programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(programPath, ImportAssetOptions.ForceSynchronousImport);
            ResetUdonSharpCaches();
            return programPath;
        }

        public static void CompileAndCopy(UdonSharpBehaviour probe, string programPath)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions
                {
                    IsEditorBuild = true,
                    ConcurrentBuild = false,
                    DisableLogging = false
                });
                UdonSharpProgramAsset programAsset =
                    AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
                if (programAsset != null)
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PROBE_PROGRAM_VERSIONS script=" +
                        programAsset.ScriptVersion + " compiled=" +
                        programAsset.CompiledVersion + " path=" + programPath);
                try
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(
                        probe, ProxySerializationPolicy.All);
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    last = exception;
                    AssetDatabase.ImportAsset(
                        programPath, ImportAssetOptions.ForceSynchronousImport);
                    ResetUdonSharpCaches();
                }
            }

            throw last ?? new InvalidOperationException(
                "UdonSharp probe proxy serialization failed");
        }

        private static MonoScript FindSourceScript(Type probeType)
        {
            string[] scriptGuids = AssetDatabase.FindAssets(
                "t:MonoScript", new[] { "Assets" });
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                MonoScript candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (candidate != null && candidate.GetClass() == probeType)
                    return candidate;
            }
            return null;
        }

        private static void ResetUdonSharpCaches()
        {
            MethodInfo utilityReset = typeof(UdonSharpEditorUtility).GetMethod(
                "ResetCaches", BindingFlags.NonPublic | BindingFlags.Static);
            if (utilityReset != null)
                utilityReset.Invoke(null, null);
            MethodInfo programReset = typeof(UdonSharpProgramAsset).GetMethod(
                "ClearProgramAssetCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (programReset != null)
                programReset.Invoke(null, null);
        }
    }
}
#endif
