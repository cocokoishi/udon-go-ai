#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PureUdonGo
{
    /// <summary>
    /// Executes the production RenderTexture graph on a real Unity graphics
    /// device and exports every graph checkpoint in the small PUGNN01 format
    /// consumed by Tools/NNReference/compare_pugnn.py. This is a developer
    /// verifier only; it never runs in the generated world.
    /// </summary>
    public static class GoGpuShaderNumericalVerifier
    {
        private const string FixtureFileName = "go-udon-feature-fixtures.json";
        private const string OutputFolderName = "NNCorpus";

        [MenuItem("Tools/Pure Udon Go/Verify Fused Versus Reference GPU Graph")]
        public static void VerifyFusedAgainstReference()
        {
            GoGpuNeuralRuntime runtime=null;
            bool previousFusion=false;
            bool passed=false;
            try
            {
                if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Real graphics device required");
                EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
                runtime=UnityEngine.Object.FindObjectOfType<GoGpuNeuralRuntime>();
                if(runtime==null)throw new InvalidOperationException("Missing production runtime");
                previousFusion=runtime.useEquivalentFusion;
                string project=Directory.GetParent(Application.dataPath).FullName;
                FixtureSet set=JsonUtility.FromJson<FixtureSet>(File.ReadAllText(Path.Combine(project,FixtureFileName)));
                if(set==null||set.fixtures==null||set.fixtures.Length!=16)
                    throw new InvalidOperationException("Full 16-case V7 export required");
                foreach(Fixture fixture in set.fixtures)
                {
                    if(fixture==null||fixture.spatial==null||fixture.global==null||
                        fixture.spatial.Length!=GoFeatureEncoder.SPATIAL_COUNT||
                        fixture.global.Length!=GoFeatureEncoder.GLOBAL_CHANNELS)
                        throw new InvalidOperationException("Invalid feature fixture");
                    runtime.ReleaseResources();runtime.useEquivalentFusion=false;
                    if(!runtime.EvaluateEncoded(fixture.spatial,fixture.global))
                        throw new InvalidOperationException("Reference graph: "+runtime.lastError);
                    if(runtime.executedPasses!=GoGpuNeuralRuntime.REFERENCE_PASSES_WITH_OWNERSHIP)
                        throw new InvalidOperationException("Reference graph pass count changed");
                    Dictionary<string,float[]> reference=CaptureCheckpoints(runtime);
                    runtime.ReleaseResources();runtime.useEquivalentFusion=true;
                    if(!runtime.EvaluateEncoded(fixture.spatial,fixture.global))
                        throw new InvalidOperationException("Fused graph: "+runtime.lastError);
                    if(runtime.executedPasses!=GoGpuNeuralRuntime.FUSED_PASSES_WITH_OWNERSHIP)
                        throw new InvalidOperationException("Fused graph pass count changed");
                    Dictionary<string,float[]> actual=CaptureCheckpoints(runtime);
                    double maxError=0;
                    foreach(KeyValuePair<string,float[]> item in reference)
                    {
                        float[] observed=actual[item.Key];
                        if(observed.Length!=item.Value.Length)throw new InvalidOperationException("Checkpoint shape mismatch");
                        for(int i=0;i<observed.Length;i++)
                        {
                            double expected=item.Value[i],value=observed[i];
                            double error=Math.Abs(value-expected);
                            // Same tolerances as Tools/NNReference/compare_pugnn.py.
                            if(double.IsNaN(expected)||double.IsInfinity(expected)||
                                double.IsNaN(value)||double.IsInfinity(value)||
                                error>1e-4+1e-5*Math.Abs(expected))
                                throw new InvalidOperationException("Fusion mismatch "+fixture.name+" "+item.Key+" index="+i+" error="+error);
                            maxError=Math.Max(maxError,error);
                        }
                    }
                    Debug.Log("PURE_UDON_GO_FUSION_CASE name="+fixture.name+" checkpoints=17 maxAbs="+maxError+" passes=84->66");
                }
                passed=true;
                Debug.Log("PURE_UDON_GO_FUSION_NUMERICAL_PASS cases=16 checkpoints=17 atol=0.0001 rtol=0.00001 scope=GPU-reference-vs-fused searchCalibration=NOT_RUN oracle=NOT_RUN");
            }
            catch(Exception e){Debug.LogError("PURE_UDON_GO_FUSION_NUMERICAL_FAIL "+e);}
            finally
            {
                if(runtime!=null){runtime.ReleaseAllResources();runtime.useEquivalentFusion=previousFusion;}
                EditorApplication.Exit(passed?0:1);
            }
        }

        private static Dictionary<string,float[]> CaptureCheckpoints(GoGpuNeuralRuntime runtime)
        {
            var values=new Dictionary<string,float[]>();
            values.Add("initial_trunk",ReadActivation(runtime.initialTrunkOutput,128));
            for(int i=0;i<10;i++)values.Add("rconv"+(i+1),ReadActivation(runtime.blockOutputs[i],128));
            values.Add("trunk_tip",ReadActivation(runtime.trunkTipOutput,128));
            values.Add("policy_spatial",ReadActivation(runtime.policySpatialLogits,1));
            values.Add("policy_pass",ReadVector(runtime.policyPassLogit,1));
            values.Add("value",ReadVector(runtime.valueLogits,3));
            values.Add("score",ReadVector(runtime.scoreLogits,4));
            values.Add("ownership",ReadActivation(runtime.ownershipLogits,1));
            return values;
        }

        [MenuItem("Tools/Pure Udon Go/Verify GPU Shader Numerical Equivalence")]
        public static void VerifyGeneratedRuntime()
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("A graphics device is required; do not run with -nographics.");

                Scene scene = EditorSceneManager.OpenScene(
                    Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("Generated production scene could not be loaded.");

                GoFeatureEncoder encoder = UnityEngine.Object.FindObjectOfType<GoFeatureEncoder>();
                GoGpuNeuralRuntime runtime = UnityEngine.Object.FindObjectOfType<GoGpuNeuralRuntime>();
                if (encoder == null || runtime == null)
                    throw new InvalidOperationException("Generated encoder/runtime references were not found.");

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string fixturePath = Path.Combine(projectRoot, FixtureFileName);
                if (!File.Exists(fixturePath))
                    throw new InvalidOperationException("Real Udon feature fixture is missing: " + fixturePath);
                FixtureSet fixtures = JsonUtility.FromJson<FixtureSet>(File.ReadAllText(fixturePath));
                if (fixtures == null || fixtures.fixtures == null || fixtures.fixtures.Length == 0)
                    throw new InvalidOperationException("Feature fixture set is empty: " + fixturePath);

                string outputFolder = Path.Combine(projectRoot, "Assets", "PureUdonGo", "Generated", OutputFolderName);
                Directory.CreateDirectory(outputFolder);
                for (int i = 0; i < fixtures.fixtures.Length; i++)
                {
                    Fixture fixture = fixtures.fixtures[i];
                    if (fixture == null || string.IsNullOrEmpty(fixture.name) ||
                        fixture.spatial == null || fixture.spatial.Length != GoFeatureEncoder.SPATIAL_COUNT ||
                        fixture.global == null || fixture.global.Length != GoFeatureEncoder.GLOBAL_CHANNELS)
                        throw new InvalidOperationException("Feature fixture has invalid arrays at index " + i + ".");
                    System.Diagnostics.Stopwatch caseTimer = System.Diagnostics.Stopwatch.StartNew();
                    if (!runtime.EvaluateEncoded(fixture.spatial, fixture.global))
                        throw new InvalidOperationException("GPU graph evaluation failed for " + fixture.name + ": " + runtime.lastError);
                    string outputPath = Path.Combine(outputFolder, fixture.name + ".pugnn");
                    WriteOutput(outputPath, runtime);
                    Debug.Log("PURE_UDON_GO_GPU_SHADER_CASE name=" + fixture.name +
                        " passes=" + runtime.executedPasses + " milliseconds=" +
                        caseTimer.Elapsed.TotalMilliseconds.ToString("F3"));
                    runtime.ReleaseResources();
                }
                Debug.Log("PURE_UDON_GO_GPU_SHADER_RESULT device=" +
                    SystemInfo.graphicsDeviceName + " cases=" + fixtures.fixtures.Length +
                    " checkpointsPerCase=17 outputFolder=" + outputFolder);
                Debug.Log("PURE_UDON_GO_GPU_SHADER_PASS cases=" + fixtures.fixtures.Length +
                    " checkpointsPerCase=17");
                runtime.ReleaseAllResources();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("PURE_UDON_GO_GPU_SHADER_FAIL " + exception);
                EditorApplication.Exit(1);
            }
        }

        [Serializable]
        private sealed class FixtureSet
        {
            public Fixture[] fixtures;
        }

        [Serializable]
        private sealed class Fixture
        {
            public string name;
            public float[] spatial;
            public float[] global;
        }

        private static void WriteOutput(string path, GoGpuNeuralRuntime runtime)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("PUGNN01\0"));
                writer.Write(17);
                WriteRecord(writer, "initial_trunk", ReadActivation(runtime.initialTrunkOutput, 128));
                for (int i = 0; i < 10; i++)
                    WriteRecord(writer, "rconv" + (i + 1).ToString(), ReadActivation(runtime.blockOutputs[i], 128));
                WriteRecord(writer, "trunk_tip", ReadActivation(runtime.trunkTipOutput, 128));
                WriteRecord(writer, "policy_spatial", ReadActivation(runtime.policySpatialLogits, 1));
                WriteRecord(writer, "policy_pass", ReadVector(runtime.policyPassLogit, 1));
                WriteRecord(writer, "value", ReadVector(runtime.valueLogits, 3));
                WriteRecord(writer, "score", ReadVector(runtime.scoreLogits, 4));
                WriteRecord(writer, "ownership", ReadActivation(runtime.ownershipLogits, 1));
            }
        }

        private static void WriteRecord(BinaryWriter writer, string name, float[] values)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            writer.Write(nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
                writer.Write(values[i]);
        }

        private static float[] ReadActivation(RenderTexture source, int channels)
        {
            if (source == null)
                throw new InvalidOperationException("Activation output is null for " + channels + " channels.");
            int groups = (channels + 3) / 4;
            if (source.width != GoGame.SIZE || source.height != GoGame.SIZE * groups)
                throw new InvalidOperationException("Activation output dimensions do not match " + channels + " channels.");
            Texture2D capture = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] pixels = ReadPixels(source, capture);
                float[] values = new float[channels * GoGame.AREA];
                for (int channel = 0; channel < channels; channel++)
                {
                    int group = channel / 4;
                    int lane = channel & 3;
                    for (int y = 0; y < GoGame.SIZE; y++)
                        for (int x = 0; x < GoGame.SIZE; x++)
                        {
                            Color pixel = pixels[(group * GoGame.SIZE + y) * GoGame.SIZE + x];
                            values[channel * GoGame.AREA + y * GoGame.SIZE + x] =
                                lane == 0 ? pixel.r : lane == 1 ? pixel.g : lane == 2 ? pixel.b : pixel.a;
                        }
                }
                return values;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        private static float[] ReadVector(RenderTexture source, int channels)
        {
            if (source == null)
                throw new InvalidOperationException("Vector output is null for " + channels + " channels.");
            int width = (channels + 3) / 4;
            if (source.width != width || source.height != 1)
                throw new InvalidOperationException("Vector output dimensions do not match " + channels + " channels.");
            Texture2D capture = new Texture2D(source.width, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] pixels = ReadPixels(source, capture);
                float[] values = new float[channels];
                for (int channel = 0; channel < channels; channel++)
                {
                    Color pixel = pixels[channel / 4];
                    int lane = channel & 3;
                    values[channel] = lane == 0 ? pixel.r : lane == 1 ? pixel.g : lane == 2 ? pixel.b : pixel.a;
                }
                return values;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        private static Color[] ReadPixels(RenderTexture source, Texture2D capture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            capture.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            capture.Apply(false, false);
            RenderTexture.active = previous;
            return capture.GetPixels();
        }
    }
}
#endif
