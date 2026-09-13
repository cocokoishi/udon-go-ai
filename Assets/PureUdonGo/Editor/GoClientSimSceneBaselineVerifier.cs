#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Captures an idle, scene-only baseline from the generated production
    /// world. No AI search is started and no runtime semantics are changed.
    /// The object counts include inactive generated compatibility tables so
    /// the report makes hidden topology cost explicit.
    /// </summary>
    public static class GoClientSimSceneBaselineVerifier
    {
        private const int WarmupFrames = 30;
        private const int SampleFrames = 360;
        private const double TimeoutSeconds = 180.0;
        private const string PendingKey =
            "PureUdonGo.ClientSimSceneBaseline.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int startFrame;
        private static int[] baselineAiMoves;
        private static GoBoardPool pool;
        private static GoAiController[] controllers;
        private static UdonBehaviour poolProgram;
        private static UdonBehaviour[] controllerPrograms;
        private static bool baselineCaptured;
        private static readonly List<ProfilerRecorder> recorders =
            new List<ProfilerRecorder>(16);
        private static readonly List<string> recorderNames =
            new List<string>(16);

        [MenuItem("Tools/Pure Udon Go/Profile Scene-Only Idle Baseline")]
        public static void ProfileSceneOnlyIdleBaseline()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded");

            pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            controllers =
                UnityEngine.Object.FindObjectsOfType<GoAiController>(true);
            if (pool == null || controllers.Length == 0)
                throw new InvalidOperationException(
                    "Generated pool/controllers were not found");

            // Keep production autoStart/Update behaviour intact. A generated
            // room is idle because its matches have not started.

            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim = true,
                displayLogs = true,
                deleteEditorOnly = false,
                spawnPlayer = true,
                hideMenuOnLaunch = true,
                setTargetFrameRate = false,
                localPlayerIsMaster = true,
                isInstanceOwner = true,
                initializationDelay = 0f,
                currentLanguage = "en"
            });

            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_SCENE_BASELINE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " warmupFrames=" +
                WarmupFrames + " sampleFrames=" + SampleFrames +
                " productionControllersEnabled=True");
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            StopRecorders();
            SessionState.SetBool(PendingKey, false);
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            startFrame = 0;
            baselineAiMoves = null;
            pool = null;
            controllers = null;
            poolProgram = null;
            controllerPrograms=null;baselineCaptured=false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_SCENE_BASELINE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingKey, false))
            {
                Fail("play mode ended before baseline completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying) return;
            if (!enteredPlayMode) enteredPlayMode = true;
            try
            {
                if(EditorApplication.timeSinceStartup>=deadline)
                {Fail("scene baseline deadline exceeded");return;}
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                ResolveSceneReferences();
                if (pool == null || controllers == null ||
                    baselineAiMoves == null)
                {
                    FailIfTimedOut("generated baseline references were not restored");
                    return;
                }
                if (startFrame == 0)
                {
                    if (Time.frameCount < WarmupFrames) return;
                    if(!baselineCaptured)
                    {
                        baselineAiMoves=new int[controllerPrograms.Length];
                        for(int i=0;i<controllerPrograms.Length;i++)
                            baselineAiMoves[i]=ReadInt(controllerPrograms[i],"aiMoves");
                        baselineCaptured=true;
                    }
                    startFrame = Time.frameCount;
                    StartRecorders();
                    if (poolProgram != null)
                        poolProgram.SendCustomEvent("ResetFrameSamples");
                    Debug.Log("PURE_UDON_GO_SCENE_BASELINE_SAMPLE_BEGIN frame=" +
                        startFrame);
                    return;
                }
                if (Time.frameCount - startFrame < SampleFrames) return;
                VerifyIdleState();
                WriteResult();
            }
            catch (Exception exception)
            {
                Fail("scene baseline exception: " + exception);
            }
        }

        private static void StartRecorders()
        {
            StopRecorders();
            AddRecorder(ProfilerCategory.Internal, "Main Thread");
            AddRecorder(ProfilerCategory.Internal, "GC.Alloc");
            AddRecorder(ProfilerCategory.Render, "GPU Frame Time");
            AddRecorder(ProfilerCategory.Render, "Batches Count");
            AddRecorder(ProfilerCategory.Render, "SetPass Calls Count");
            AddRecorder(ProfilerCategory.Render, "Shadow Casters Count");
            AddRecorder(ProfilerCategory.Physics, "Physics.Simulate");
            AddRecorder(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases");
        }

        private static void AddRecorder(ProfilerCategory category, string name)
        {
            try
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, name, 512);
                recorders.Add(recorder);
                recorderNames.Add(name);
            }
            catch
            {
                // Marker availability differs between Unity/ClientSim builds;
                // missing markers are reported as unavailable, never guessed.
            }
        }

        private static void StopRecorders()
        {
            for (int i = 0; i < recorders.Count; i++)
            {
                if (recorders[i].Valid) recorders[i].Dispose();
            }
            recorders.Clear();
            recorderNames.Clear();
        }

        private static void VerifyIdleState()
        {
            if (controllers == null || baselineAiMoves == null)
                throw new InvalidOperationException(
                    "baseline controller references were not resolved");
            for (int i = 0; i < controllers.Length; i++)
            {
                if (ReadInt(controllerPrograms[i],"aiMoves") != baselineAiMoves[i])
                    throw new InvalidOperationException(
                        "scene-only baseline started an AI move on controller " + i);
                UdonBehaviour search=controllerPrograms[i].GetProgramVariable("search") as UdonBehaviour;
                int phase=ReadInt(search,"phase");
                if (search!=null&&phase!=GoMctsSearch.PHASE_IDLE&&phase!=GoMctsSearch.PHASE_CANCELLED)
                    throw new InvalidOperationException(
                        "scene-only baseline left a search active on controller " + i);
            }
        }

        private static void ResolveSceneReferences()
        {
            if (pool == null)
                pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if (poolProgram == null && pool != null)
                poolProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(pool);
            if (controllers == null)
                controllers = UnityEngine.Object.FindObjectsOfType<GoAiController>();
            if (controllers == null || controllers.Length == 0) return;
            if (controllerPrograms==null)
            {
                baselineAiMoves = new int[controllers.Length];
                controllerPrograms=new UdonBehaviour[controllers.Length];
                for(int i=0;i<controllers.Length;i++)
                {
                    controllerPrograms[i]=UdonSharpEditorUtility.GetBackingUdonBehaviour(controllers[i]);
                    if(controllerPrograms[i]==null)throw new InvalidOperationException("Missing backing AI program");
                }
            }
        }

        private static void WriteResult()
        {
            float[] frameSamples = ReadPoolFrameSamples();
            if(frameSamples.Length<SampleFrames)
                throw new InvalidOperationException("Incomplete Udon frame samples: "+frameSamples.Length);
            foreach(float sample in frameSamples)
                if(float.IsNaN(sample)||float.IsInfinity(sample)||sample<=0f)
                    throw new InvalidOperationException("Invalid Udon frame sample");
            Array.Sort(frameSamples);
            Renderer[] allRenderers =
                UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            string counts = "renderers=" + allRenderers.Length +
                " activeRenderers=" +
                UnityEngine.Object.FindObjectsOfType<Renderer>().Length +
                " colliders=" + UnityEngine.Object.FindObjectsOfType<Collider>(true).Length +
                " activeColliders=" +
                UnityEngine.Object.FindObjectsOfType<Collider>().Length +
                " boxColliders=" + UnityEngine.Object.FindObjectsOfType<BoxCollider>(true).Length +
                " activeBoxColliders=" +
                UnityEngine.Object.FindObjectsOfType<BoxCollider>().Length +
                " goBoardCells=" + UnityEngine.Object.FindObjectsOfType<GoBoardCell>(true).Length +
                " activeGoBoardCells=" +
                UnityEngine.Object.FindObjectsOfType<GoBoardCell>().Length +
                " udonBehaviours=" + UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).Length +
                " activeUdonBehaviours=" +
                UnityEngine.Object.FindObjectsOfType<UdonBehaviour>().Length +
                " canvases=" + UnityEngine.Object.FindObjectsOfType<Canvas>(true).Length +
                " activeCanvases=" + UnityEngine.Object.FindObjectsOfType<Canvas>().Length +
                " shadowCasters=" + CountShadowCasters(allRenderers, true) +
                " activeShadowCasters=" + CountShadowCasters(allRenderers, false);
            string profiler = "";
            for (int i = 0; i < recorders.Count; i++)
            {
                ProfilerRecorder recorder = recorders[i];
                profiler += " " + recorderNames[i].Replace(" ", "_") +
                    DescribeRecorder(recorder);
            }
            Debug.Log("PURE_UDON_GO_SCENE_BASELINE_SAMPLE frames=" +
                frameSamples.Length + " frameMeanMs=" + Mean(frameSamples).ToString("F3") +
                " frameP50Ms=" + Percentile(frameSamples, 0.50f).ToString("F3") +
                " frameP95Ms=" + Percentile(frameSamples, 0.95f).ToString("F3") +
                " frameP99Ms=" + Percentile(frameSamples, 0.99f).ToString("F3") +
                " frameMaxMs=" + Max(frameSamples).ToString("F3") + " " + counts +
                profiler + " scope=ClientSim-scene-only");
            StopWithResult(true);
        }

        private static float[] ReadPoolFrameSamples()
        {
            if (poolProgram == null)
                return new float[0];
            float[] source = poolProgram.GetProgramVariable(
                "frameMillisecondsSamples") as float[];
            if (source == null || source.Length == 0) return new float[0];
            int count = Mathf.Clamp(ReadInt(poolProgram, "frameSampleCount"),
                0, source.Length);
            float[] values = new float[count];
            int writeIndex = ReadInt(poolProgram, "frameSampleWriteIndex");
            int start = count == source.Length ? writeIndex : 0;
            for (int i = 0; i < count; i++)
                values[i] = source[(start + i) % source.Length];
            return values;
        }

        private static int CountShadowCasters(Renderer[] renderers,
            bool includeInactive)
        {
            if (renderers == null) return 0;
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    (!includeInactive && !renderer.gameObject.activeInHierarchy))
                    continue;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off) count++;
            }
            return count;
        }

        private static float Mean(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        private static float Max(float[] values)
        {
            float result = 0f;
            if (values == null) return result;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > result) result = values[i];
            return result;
        }

        private static float Percentile(float[] sorted, float fraction)
        {
            if (sorted == null || sorted.Length == 0) return 0f;
            int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * fraction) - 1,
                0, sorted.Length - 1);
            return sorted[index];
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null :
                program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static string DescribeRecorder(ProfilerRecorder recorder)
        {
            if(!recorder.Valid||recorder.Count==0)return "=unavailable";
            var samples=new List<ProfilerRecorderSample>();recorder.CopyTo(samples);
            double[] values=new double[samples.Count];double sum=0,max=0;
            for(int i=0;i<values.Length;i++){values[i]=samples[i].Value;sum+=values[i];max=Math.Max(max,values[i]);}
            if(max==0)return "=unavailable-or-zero";
            Array.Sort(values);
            return "={unit:"+recorder.UnitType+",count:"+values.Length+",avg:"+(sum/values.Length)+
                ",p95:"+values[Math.Max(0,(int)Math.Ceiling(values.Length*0.95)-1)]+
                ",p99:"+values[Math.Max(0,(int)Math.Ceiling(values.Length*0.99)-1)]+",max:"+max+"}";
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup >= deadline) Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten) return;
            Debug.LogError("PURE_UDON_GO_SCENE_BASELINE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool passed)
        {
            resultWritten = true;
            SessionState.SetBool(PendingKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            StopRecorders();
            if(poolProgram!=null)poolProgram.SendCustomEvent("StopFrameSamples");
            Debug.Log("PURE_UDON_GO_SCENE_BASELINE_RESULT pass=" + passed);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
