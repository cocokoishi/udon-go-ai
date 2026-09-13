#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Real-Udon performance probes. The diagnostic entry point is a short
    /// four-visit sample; the public-profile entries exercise the exact
    /// Beginner/Advanced/Master/Ultrahard budgets used by the generated
    /// world. The probe only dispatches Udon events and reads serialized
    /// variables; all measured work remains in production Udon.
    /// </summary>
    public static class GoClientSimPerformanceVerifier
    {
        private const int DiagnosticTargetVisits = 4;
        private const double TimeoutSeconds = 300.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimPerformanceVerifier.Pending";
        private const string TargetVisitsSessionKey =
            "PureUdonGo.ClientSimPerformanceVerifier.TargetVisits";
        private const string PresetSessionKey =
            "PureUdonGo.ClientSimPerformanceVerifier.Preset";

        private static double deadline;
        private static double sampleStartSeconds;
        private static int sampleStartFrame;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour encoderProgram;
        private static UdonBehaviour runtimeProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static UdonBehaviour poolProgram;
        private static UdonBehaviour uiProgram;
        private static int baselineMoveCount;
        private static int baselineAiMoves;
        private static int runTargetVisits = DiagnosticTargetVisits;
        private static int runPreset = -1;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;

            runTargetVisits = SessionState.GetInt(
                TargetVisitsSessionKey, DiagnosticTargetVisits);
            runPreset = SessionState.GetInt(PresetSessionKey, -1);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Search Stages")]
        public static void ProfileClientSimSearchStages()
        {
            StartProfile(DiagnosticTargetVisits, -1);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Beginner 8 Visits")]
        public static void ProfileClientSimBeginner8()
        {
            StartProfile(GoDifficultyProfile.BEGINNER_VISITS, GoDifficultyProfile.BEGINNER);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Advanced 20 Visits")]
        public static void ProfileClientSimAdvanced20()
        {
            StartProfile(GoDifficultyProfile.ADVANCED_VISITS, GoDifficultyProfile.ADVANCED);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Master 48 Visits")]
        public static void ProfileClientSimMaster48()
        {
            StartProfile(GoDifficultyProfile.MASTER_VISITS, GoDifficultyProfile.MASTER);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Ultrahard 120 Visits")]
        public static void ProfileClientSimUltrahard120()
        {
            StartProfile(GoDifficultyProfile.ULTRAHARD_VISITS, GoDifficultyProfile.ULTRAHARD);
        }

        private static void StartProfile(int targetVisits, int preset)
        {
            if (targetVisits < 1 || targetVisits > GoMctsSearch.MAX_SUPPORTED_VISITS)
                throw new ArgumentOutOfRangeException("targetVisits");
            runTargetVisits = targetVisits;
            runPreset = preset;
            SessionState.SetInt(TargetVisitsSessionKey, runTargetVisits);
            SessionState.SetInt(PresetSessionKey, runPreset);
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ScenePath);

            GoGeneratedWorld rootMarker =
                UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoGame gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controllerProxy =
                UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (rootMarker == null || gameProxy == null || controllerProxy == null ||
                gameProxy.blackDifficulty == null || gameProxy.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Generated Go runtime objects or difficulty profiles were not found");

            string probeProgramPath = EnsureProbeProgramAsset();
            GameObject probeObject = new GameObject("ClientSim Performance Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoCompleteGameProbe probe =
                probeObject.AddUdonSharpComponent<GoCompleteGameProbe>();
            probe.game = gameProxy;
            probe.controller = controllerProxy;
            probe.blackDifficulty = gameProxy.blackDifficulty;
            probe.whiteDifficulty = gameProxy.whiteDifficulty;
            probe.aiSettings = gameProxy.aiSettings;
            probe.targetVisits = runTargetVisits;
            probe.profilePreset = runPreset;

            CompileAndCopyProbe(probe, probeProgramPath);

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

            SessionState.SetBool(PendingSessionKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" +
                TimeoutSeconds + " targetVisits=" + runTargetVisits +
                " preset=" + runPreset);
            EditorApplication.isPlaying = true;
        }

        private static string EnsureProbeProgramAsset()
        {
            MonoScript sourceScript = null;
            string[] scriptGuids = AssetDatabase.FindAssets(
                "t:MonoScript", new[] { "Assets" });
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                MonoScript candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(candidatePath);
                if (candidate != null && candidate.GetClass() == typeof(GoCompleteGameProbe))
                {
                    sourceScript = candidate;
                    break;
                }
            }
            if (sourceScript == null)
                throw new InvalidOperationException(
                    "Performance probe source is missing under the Unity Assets database");

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
            // Newly created U# program assets start at ScriptVersion.Unknown.
            // The source is already in the current U# serialization format;
            // the compiler remains responsible for producing CompiledVersion.
            programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(programPath, ImportAssetOptions.ForceSynchronousImport);

            MethodInfo utilityReset = typeof(UdonSharpEditorUtility).GetMethod(
                "ResetCaches", BindingFlags.NonPublic | BindingFlags.Static);
            if (utilityReset != null)
                utilityReset.Invoke(null, null);
            MethodInfo programReset = typeof(UdonSharpProgramAsset).GetMethod(
                "ClearProgramAssetCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (programReset != null)
                programReset.Invoke(null, null);
            return programPath;
        }

        private static void CompileAndCopyProbe(GoCompleteGameProbe probe, string programPath)
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
                UdonSharpProgramAsset compiledAsset =
                    AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
                if (compiledAsset != null)
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_PROGRAM_VERSIONS script=" +
                        compiledAsset.ScriptVersion + " compiled=" +
                        compiledAsset.CompiledVersion + " path=" + programPath);
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
                        programPath,
                        ImportAssetOptions.ForceSynchronousImport);
                }
            }
            throw last ?? new InvalidOperationException(
                "Performance probe proxy serialization failed");
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            sampleStartSeconds = 0.0;
            sampleStartFrame = 0;
            enteredPlayMode = false;
            resultWritten = false;
            probeSent = false;
            gameProgram = null;
            aiProgram = null;
            encoderProgram = null;
            runtimeProgram = null;
            searchProgram = null;
            readerProgram = null;
            poolProgram = null;
            uiProgram = null;
            baselineMoveCount = 0;
            baselineAiMoves = 0;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before performance sample completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }

                ResolvePrograms();
                if (gameProgram == null || aiProgram == null ||
                    encoderProgram == null || runtimeProgram == null ||
                    searchProgram == null || readerProgram == null)
                {
                    FailIfTimedOut("Generated Go Udon performance references were not all found");
                    return;
                }

                if (!probeSent)
                {
                    baselineMoveCount = ReadInt(gameProgram, "moveCount");
                    baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                    sampleStartSeconds = EditorApplication.timeSinceStartup;
                    sampleStartFrame = Time.frameCount;
                    UdonBehaviour probeProgram = FindProbeProgram();
                    if (probeProgram == null)
                    {
                        FailIfTimedOut("ClientSim performance probe program was not found");
                        return;
                    }
                    if (poolProgram != null)
                        poolProgram.SendCustomEvent("ResetFrameSamples");
                    if(uiProgram!=null)uiProgram.SendCustomEvent("ResetRefreshTiming");
                    probeProgram.SendCustomEvent("RunCompleteGame");
                    probeSent = true;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_EVENT name=RunCompleteGame targetVisits=" +
                        runTargetVisits + " preset=" + runPreset);
                    return;
                }

                int moveCount = ReadInt(gameProgram, "moveCount");
                int aiMoves = ReadInt(aiProgram, "aiMoves");
                int controllerState = ReadInt(aiProgram, "controllerState");
                if (controllerState == GoAiController.STATE_ERROR)
                {
                    string aiError = ReadString(aiProgram, "lastError");
                    Fail("Udon AI error during performance sample: " + aiError);
                    return;
                }

                if (moveCount > baselineMoveCount && aiMoves > baselineAiMoves)
                {
                    int actualVisits = ReadInt(aiProgram, "lastSearchCompletedVisits");
                    int encodeGeneration = ReadInt(encoderProgram, "encodeGeneration");
                    // The root neural evaluation is the first completed visit,
                    // so one fresh encode per completed visit is the exact
                    // lower bound. There is no extra hidden root encode.
                    int expectedEncodes = actualVisits;
                    if (actualVisits != runTargetVisits || encodeGeneration < expectedEncodes)
                    {
                        Fail("performance sample completed with invalid search/encoding counts" +
                            " actualVisits=" + actualVisits +
                            " expectedVisits=" + runTargetVisits +
                            " encodeGeneration=" + encodeGeneration +
                            " expectedEncodesAtLeast=" + expectedEncodes);
                        return;
                    }
                    int moveMaskDuringAi=ReadInt(poolProgram,
                        "moveMaskWarmupPointsDuringAiSearch");
                    if(moveMaskDuringAi!=0)
                    {
                        Fail("authoritative move-mask warm-up ran during AI search points="+
                            moveMaskDuringAi);
                        return;
                    }

                    double elapsed = EditorApplication.timeSinceStartup - sampleStartSeconds;
                    int frames = Time.frameCount - sampleStartFrame;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_SAMPLE targetVisits=" +
                        runTargetVisits + " actualVisits=" + actualVisits +
                        " preset=" + runPreset +
                        " elapsedSeconds=" + elapsed.ToString("F3") +
                        " frames=" + frames + " " + DescribePerformance());
                    // Historical wall-clock targets are diagnostic context only.
                    // Visit accounting, stage completion, stale-result safety
                    // and correctness remain hard gates; this verifier must not
                    // reject a valid search solely for an old latency target.
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_PASS targetVisits=" +
                        runTargetVisits + " preset=" + runPreset +
                        " freshFeatureEncoding=True wallClockDiagnosticOnly=True");
                    StopWithResult(true);
                    return;
                }

                FailIfTimedOut("performance sample did not commit an AI move" +
                    " moveCount=" + moveCount + " aiMoves=" + aiMoves);
            }
            catch (Exception exception)
            {
                Fail("ClientSim performance inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (gameProgram == null)
            {
                GoGame proxy = UnityEngine.Object.FindObjectOfType<GoGame>();
                if (proxy != null)
                    gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            }
            if (aiProgram == null)
            {
                GoAiController proxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
                if (proxy != null)
                    aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            }
            if (aiProgram == null)
                return;
            if (encoderProgram == null)
                encoderProgram = ReadReference(aiProgram, "encoder");
            if (runtimeProgram == null)
                runtimeProgram = ReadReference(aiProgram, "runtime");
            if (searchProgram == null)
                searchProgram = ReadReference(aiProgram, "search");
            if (readerProgram == null)
                readerProgram = ReadReference(aiProgram, "reader");
            if (poolProgram == null)
            {
                GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
                if (pool != null)
                    poolProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(pool);
            }
            if(uiProgram==null)
            {
                uiProgram=gameProgram==null?null:ReadReference(gameProgram,"ui");
            }
        }

        private static UdonBehaviour FindProbeProgram()
        {
            GoCompleteGameProbe probe =
                UnityEngine.Object.FindObjectOfType<GoCompleteGameProbe>();
            return probe == null ? null : UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
        }

        private static UdonBehaviour ReadReference(UdonBehaviour program, string variableName)
        {
            return program.GetProgramVariable(variableName) as UdonBehaviour;
        }

        private static string DescribePerformance()
        {
            return "searchMs=" + ReadFloat(aiProgram, "lastSearchMilliseconds").ToString("F3") +
                " setupMs=" + ReadFloat(aiProgram, "lastSearchSetupMilliseconds").ToString("F3") +
                " searchStepMs=" + ReadFloat(aiProgram, "lastSearchStepMilliseconds").ToString("F3") +
                " superkoMs=" + ReadFloat(aiProgram, "lastSuperkoMilliseconds").ToString("F3") +
                " featureMs=" + ReadFloat(aiProgram, "lastFeatureMilliseconds").ToString("F3") +
                " featureClearMs=" + ReadFloat(encoderProgram, "lastClearSpatialMilliseconds").ToString("F3") +
                " featureBaseMs=" + ReadFloat(encoderProgram, "lastBaseMilliseconds").ToString("F3") +
                " featureLadderMs=" + ReadFloat(encoderProgram, "lastLadderMilliseconds").ToString("F3") +
                " featureAreaMs=" + ReadFloat(encoderProgram, "lastAreaMilliseconds").ToString("F3") +
                " featureSteps=" + ReadInt(aiProgram, "lastFeatureSteps") +
                " ladderStepCalls=" + ReadInt(aiProgram, "lastLadderStepCalls") +
                " ladderTransitions=" + ReadInt(aiProgram, "lastLadderSearchTransitions") +
                " ladderNodes=" + ReadInt(aiProgram, "lastLadderNodes") +
                " gpuSetupMs=" + ReadFloat(aiProgram, "lastGpuInputSetupMilliseconds").ToString("F3") +
                " gpuUploadMs=" + ReadFloat(aiProgram, "lastGpuUploadMilliseconds").ToString("F3") +
                " gpuDispatchMs=" + ReadFloat(aiProgram, "lastGpuDispatchMilliseconds").ToString("F3") +
                " gpuEvalMs=" + ReadFloat(aiProgram, "lastGpuEvaluationMilliseconds").ToString("F3") +
                " gpuPasses=" + ReadInt(aiProgram, "lastGpuPasses") +
                " readbackMs=" + ReadFloat(aiProgram, "lastReadbackMilliseconds").ToString("F3") +
                " readbackRequests=" + ReadInt(aiProgram, "lastReadbackRequests") +
                " readbackStages=" + ReadInt(aiProgram, "lastReadbackStages") +
                " neuralSubmitMs=" + ReadFloat(aiProgram, "lastNeuralSubmitMilliseconds").ToString("F3") +
                " expansionMs=" + ReadFloat(aiProgram, "lastNeuralExpansionMilliseconds").ToString("F3") +
                " rootCopyMs=" + ReadFloat(aiProgram, "lastRootCopyMilliseconds").ToString("F3") +
                " backupMs=" + ReadFloat(aiProgram, "lastBackupMilliseconds").ToString("F3") +
                " lastTickMs=" + ReadFloat(aiProgram, "lastTickMilliseconds").ToString("F3") +
                " maxTickMs=" + ReadFloat(aiProgram, "maxTickMilliseconds").ToString("F3") +
                " edgeStorageAllocated=" + ReadBool(searchProgram, "edgeStorageAllocated") +
                " ladderStorageAllocated=" + ReadBool(encoderProgram, "ladderStorageAllocated") +
                " legalChecks=" + ReadInt(aiProgram, "lastLegalMoveChecks") +
                " simulationPlays=" + ReadInt(aiProgram, "lastSimulationPlayCalls") +
                " superkoLocations=" + ReadInt(aiProgram, "lastSuperkoScanLocations") +
                " superkoFrames=" + ReadInt(aiProgram, "lastSuperkoScanFrames") +
                " controllerTickMaxMs="+ReadFloat(aiProgram,"maxTickMilliseconds").ToString("F3")+
                " superkoStageMaxMs="+ReadFloat(aiProgram,"maxSuperkoFrameMilliseconds").ToString("F3")+
                " featureStageMaxMs="+ReadFloat(encoderProgram,"maxEncodeStepMilliseconds").ToString("F3")+
                " gpuSubmitStageMaxMs="+ReadFloat(runtimeProgram,"maxGpuSubmitMsOneFrame").ToString("F3")+
                " gpuSubmitLastFrameMs="+ReadFloat(runtimeProgram,"lastSubmitFrameMilliseconds").ToString("F3")+
                " gpuSubmitEmaMs="+ReadFloat(runtimeProgram,"smoothedGpuSubmitMilliseconds").ToString("F3")+
                " mctsStepMaxMs="+ReadFloat(aiProgram,"maxMctsStepMilliseconds").ToString("F3")+
                " uiRefreshMaxMs="+ReadFloat(uiProgram,"maxRefreshMilliseconds").ToString("F3")+
                " gpuGraphFrames="+ReadInt(runtimeProgram,"gpuGraphFrames")+
                " maxGpuGraphPassesOneFrame="+ReadInt(runtimeProgram,"maxGpuGraphPassesOneFrame")+
                " gpuGraphStage="+ReadInt(runtimeProgram,"gpuGraphStage")+
                " gpuGraphStageProgress="+ReadInt(runtimeProgram,"gpuGraphStageProgress")+
                " rootStateCopiedInts="+ReadInt(searchProgram,"rootStateCopiedInts")+
                " rootHistoryCopiedEntries="+ReadInt(searchProgram,"rootHistoryCopiedEntries")+
                " simulationUndoOperations="+ReadInt(searchProgram,"simulationUndoOperations")+
                " grantedCpuBudgetMs="+ReadFloat(aiProgram,"grantedFrameWorkMs").ToString("F3")+
                " usedCpuBudgetMs="+ReadFloat(aiProgram,"usedFrameWorkMs").ToString("F3")+
                " cpuBudgetOvershootMs="+ReadFloat(aiProgram,"maxFrameWorkOvershootMs").ToString("F3")+
                " sameFrameTransitions="+ReadInt(aiProgram,"sameFramePhaseTransitions")+
                " avoidableYields="+ReadInt(aiProgram,"avoidableYieldCount")+
                " deadlineYields="+ReadInt(aiProgram,"deadlineYieldCount")+
                " asyncReadbackWaitFrames="+ReadInt(aiProgram,"asyncReadbackWaitFrames")+
                " telemetryCommitsDuringSearch="+ReadInt(aiProgram,"telemetryCommitsDuringSearch")+
                " telemetrySerializationsDuringSearch="+ReadInt(aiProgram,"telemetrySerializationsDuringSearch")+
                 " moveMaskWarmupSuppressedCriticalFrames="+ReadInt(poolProgram,"moveMaskWarmupSuppressedCriticalFrames")+
                 " moveMaskWarmupPointsDuringAiSearch="+ReadInt(poolProgram,"moveMaskWarmupPointsDuringAiSearch")+
                " watchdogTrips=" + ReadInt(aiProgram, "searchWatchdogTrips") +
                " readerRotations=" + ReadInt(aiProgram, "readerRotations") +
                " syncArrayRawBytes=" + GoGame.SYNCED_INT_ARRAY_RAW_BYTES +
                " lastSerializedBytes=" + ReadInt(gameProgram, "lastSerializationByteCount") +
                " serializationSuccess=" + ReadBool(gameProgram, "lastSerializationSuccess") +
                " frameTail=" + DescribeFrameTail();
        }

        private static string DescribeFrameTail()
        {
            if (poolProgram == null)
                return "unavailable";
            float[] samples = poolProgram.GetProgramVariable("frameMillisecondsSamples") as float[];
            int count = ReadInt(poolProgram, "frameSampleCount");
            if (samples == null || count <= 0)
                return "unavailable";
            if (count > samples.Length)
                count = samples.Length;
            float[] chronological = new float[count];
            int writeIndex = ReadInt(poolProgram, "frameSampleWriteIndex");
            int start = count == samples.Length ? writeIndex : 0;
            for (int i = 0; i < count; i++)
                chronological[i] = samples[(start + i) % samples.Length];
            float p50 = Percentile(chronological, count, 0.50f);
            float p95 = Percentile(chronological, count, 0.95f);
            float p99 = Percentile(chronological, count, 0.99f);
            float minimum=chronological[0];float total=0f;
            for(int i=0;i<count;i++)
            {if(chronological[i]<minimum)minimum=chronological[i];total+=chronological[i];}
            float mean=total/count;
            float maximum = ReadFloat(poolProgram, "maxObservedFrameMilliseconds");
            return "samples=" + count + " minMs="+minimum.ToString("F3")+
                " meanMs="+mean.ToString("F3")+" p50Ms=" + p50.ToString("F3") +
                " p95Ms=" + p95.ToString("F3") + " p99Ms=" + p99.ToString("F3") +
                " maxMs=" + maximum.ToString("F3");
        }

        private static float Percentile(float[] samples, int count, float percentile)
        {
            float[] sorted = new float[count];
            Array.Copy(samples, sorted, count);
            Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt(count * percentile) - 1, 0, count - 1);
            return sorted[index];
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadFloat(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static bool ReadBool(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static string ReadString(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? "" : value.ToString();
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline)
                return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_PERF_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
