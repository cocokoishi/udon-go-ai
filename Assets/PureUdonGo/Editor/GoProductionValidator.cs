#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDK3.Components;

namespace PureUdonGo.Editor
{
    /// <summary>
    /// Validates the generated Go world as a production artifact. This is a
    /// structural/import/runtime-contract gate; numerical NN equivalence,
    /// KaTrain strength calibration and VRChat-client behavior remain separate
    /// evidence layers.
    /// </summary>
    public static class GoProductionValidator
    {
        public const string MenuPath = "Tools/Pure Udon Go/Validate Production Scene";
        public const string ValidationReportPath =
            "Assets/PureUdonGo/Generated/GoProductionValidationReport.txt";

        private const string ModelManifestPath =
            "Assets/PureUdonGo/Model/Generated/ModelManifest.json";
        private const string PackingManifestPath =
            "Assets/PureUdonGo/Model/Generated/packing_manifest.json";
        private const string TensorManifestPath =
            "Assets/PureUdonGo/Model/Generated/tensor_manifest.json";
        private const string RuntimeWeightPath =
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.asset";
        private const string AuditWeightPath =
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.exr";
        private const string RawWeightPath =
            "Assets/PureUdonGo/Model/Generated/weights_rgba32f.raw";
        private const string RawModelPath =
            ".KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz";
        private const string NnMaterialPath =
            "Assets/PureUdonGo/Generated/Materials/PureUdonGoNNLayer.mat";
        private const string ShaderSourcePath =
            "Assets/PureUdonGo/Shaders/PureUdonGoNNLayer.shader";

        private const string CompressedModelSha256 =
            "1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04";
        private const string RawAtlasSha256 =
            "10285a17fd7a47a3b6bdde08a26d13c34c9bbcde566dfbd66c5fa27911c65866";
        private const string AuditAtlasSha256 =
            "e00f701b6ef1498037e56a330f6243d2878ff9c74e6f6fddd5b0cf06a2f94309";
        private const string RuntimeAtlasSha256 =
            "86842c9fa11f89c7cfc0c26f77b344cdc3fb2e364b01de987a9f0978473b3408";

        private static readonly List<string> failures = new List<string>(32);
        private static readonly List<string> warnings = new List<string>(16);

        [MenuItem(MenuPath, false, 1210)]
        public static void ValidateFinalProductionScene()
        {
            failures.Clear();
            warnings.Clear();
            try
            {
                ValidateModelAssets();
                ValidateShaderAndMaterial();
                ValidateSourceContracts();
                ValidateUdonProgramAssets();
                ValidateGeneratedScene();
            }
            catch (Exception exception)
            {
                Fail("Unhandled validator exception: " + exception);
            }
            finally
            {
                WriteValidationReport();
            }

            if (failures.Count > 0)
            {
                Debug.LogError("PURE_UDON_GO_VALIDATOR_FAIL failures=" + failures.Count +
                    " warnings=" + warnings.Count + " report=" + ValidationReportPath);
                for (int i = 0; i < failures.Count; i++)
                    Debug.LogError("PURE_UDON_GO_VALIDATOR_FAILURE " + failures[i]);
                throw new InvalidOperationException(
                    "Pure Udon Go production validation failed with " + failures.Count +
                    " failure(s). See " + ValidationReportPath + ".");
            }

            Debug.Log("PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=" + warnings.Count +
                " scene=" + GoWorldGenerator.ScenePath + " model=" +
                GoProductionModelLayout.ModelName + " maxVisits=" +
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            for (int i = 0; i < warnings.Count; i++)
                Debug.LogWarning("PURE_UDON_GO_VALIDATOR_WARNING " + warnings[i]);
        }

        private static void ValidateModelAssets()
        {
            string modelManifest = ReadRequiredAssetText(ModelManifestPath);
            string packingManifest = ReadRequiredAssetText(PackingManifestPath);
            string tensorManifest = ReadRequiredAssetText(TensorManifestPath);

            RequireText(modelManifest, "pure-udon-go.production-model.v2",
                "ModelManifest schema");
            RequireText(modelManifest, "g170e-b10c128-s1141046784-d204142634",
                "ModelManifest model name");
            RequireText(modelManifest, "\"model_version\": 8", "ModelManifest version");
            RequireText(modelManifest, "\"tensor_count\": 127", "ModelManifest tensor count");
            RequireText(modelManifest, "\"tensor_element_count\": 3000071",
                "ModelManifest tensor element count");
            RequireText(modelManifest, "\"width\": 1024", "ModelManifest width");
            RequireText(modelManifest, "\"height\": 733", "ModelManifest height");
            RequireText(modelManifest, RuntimeAtlasSha256, "ModelManifest runtime hash");
            RequireText(packingManifest, "pure-udon-go.weight-packing.v2",
                "packing manifest schema");
            RequireText(packingManifest, "\"capacity_float_count\": 3002368",
                "packing capacity");
            RequireText(packingManifest, CompressedModelSha256, "packing source hash");
            RequireText(tensorManifest, "pure-udon-go.katago-v8-tensor-manifest.v1",
                "tensor manifest schema");
            RequireText(tensorManifest, "\"num_input_channels\": 22",
                "tensor manifest spatial channel count");
            RequireText(tensorManifest, "\"num_input_global_channels\": 19",
                "tensor manifest global channel count");

            ValidateFileHash(RuntimeWeightPath, RuntimeAtlasSha256);
            ValidateFileHash(RawWeightPath, RawAtlasSha256);
            ValidateFileHash(AuditWeightPath, AuditAtlasSha256);
            if (File.Exists(ProjectFile(RawModelPath)))
                ValidateFileHash(RawModelPath, CompressedModelSha256);
            else
                Warn("Raw KataGo oracle is outside this Unity project root; baked asset and manifests were checked.");

            Texture2D runtimeWeights = AssetDatabase.LoadAssetAtPath<Texture2D>(RuntimeWeightPath);
            Require(runtimeWeights != null, "runtime weight Texture2D is loadable");
            if (runtimeWeights == null)
                return;
            Require(runtimeWeights.width == GoProductionModelLayout.WeightWidth &&
                runtimeWeights.height == GoProductionModelLayout.WeightHeight,
                "runtime weight dimensions are 1024x733");
            Require(runtimeWeights.format == TextureFormat.RGBAFloat,
                "runtime weight format is RGBAFloat");
            Require(runtimeWeights.filterMode == FilterMode.Point,
                "runtime weight filter mode is Point");
            Require(runtimeWeights.wrapMode == TextureWrapMode.Clamp,
                "runtime weight wrap mode is Clamp");
            Require(runtimeWeights.mipmapCount <= 1, "runtime weight has no mip chain");

            TextureImporter importer = AssetImporter.GetAtPath(AuditWeightPath) as TextureImporter;
            string importerFailure;
            Require(GoModelTextureImporter.IsConfigured(importer, out importerFailure),
                "audit weight importer: " + importerFailure);
            Texture2D auditWeights = AssetDatabase.LoadAssetAtPath<Texture2D>(AuditWeightPath);
            Require(auditWeights != null && auditWeights.width == GoProductionModelLayout.WeightWidth &&
                auditWeights.height == GoProductionModelLayout.WeightHeight,
                "audit EXR weight dimensions are 1024x733");
        }

        private static void ValidateShaderAndMaterial()
        {
            string shaderSource = ReadRequiredProjectText(ShaderSourcePath);
            RequireText(shaderSource, "Shader \"PureUdonGo/NNLayer\"", "NN shader declaration");
            RequireText(shaderSource, "#pragma target 4.5", "NN shader target");
            RequireText(shaderSource, "LoadWeight", "NN shader weight lookup");
            RequireText(shaderSource, "_Mode", "NN shader dispatch modes");

            Shader shader = Shader.Find(GoWorldGenerator.ShaderName);
            Require(shader != null, "NN shader resolves by production name");
            if (shader != null)
            {
                Require(shader.isSupported, "NN shader is supported on " +
                    SystemInfo.graphicsDeviceType);
                Require(!ShaderUtil.ShaderHasError(shader), "NN shader has no compiler errors");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(NnMaterialPath);
            Texture2D weights = AssetDatabase.LoadAssetAtPath<Texture2D>(RuntimeWeightPath);
            Require(material != null, "generated NN material exists");
            if (material != null)
            {
                Require(shader == null || material.shader == shader,
                    "generated NN material uses PureUdonGo/NNLayer");
                Require(material.HasProperty("_Weights"), "generated NN material exposes _Weights");
                Require(material.GetTexture("_Weights") == weights,
                    "generated NN material binds serialized production weights");
            }
        }

        private static void ValidateSourceContracts()
        {
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGame.cs",
                "[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)",
                "[UdonSynced] public int gameState", "matchStarted", "blackIsAI", "whiteIsAI",
                "ClaimBlack", "ClaimWhite", "SetStarter", "RequestStartMatch", "revision",
                "RequestSerialization()", "TakeOwnership()", "IsRulesLegalMove", "loc!=koLoc",
                "FinishByMoveLimit", "hintMove", "RequestUndo", "CanLocalUndo", "OfferOrAcceptDraw",
                "aiHintsEnabled", "aiHintPermissionLocked", "ToggleAiHintPermission",
                "moveHistory", "RebuildDerivedHistoryFromMoveLog", "HasVerifiedPositionHistory", "GetMoveMask",
                "pendingBoardRefreshReason=captured>0",
                "undoOfferRevision", "drawOfferRevision",
                "OnPostSerialization", "lastSerializationByteCount",
                "RebuildDerivedHistory", "TryRebuildPositionToMoveCount", "StepMoveMaskCache",
                "StepMoveMaskCacheTimeSliced", "MOVE_MASK_WARMUP_DEFAULT_MS",
                "MOVE_MASK_WARMUP_MIN_MS", "MOVE_MASK_WARMUP_MAX_MS",
                "moveMaskCacheHits", "moveMaskCacheMisses", "maxMoveMaskPointsOneFrame",
                 "CaptureRebuildTransaction", "RestoreRebuildTransaction",
                "rebuildStoneCountFrequencySnapshot",
                 "rebuildSnapshotStoneCountFrequencyValid", "lastRebuildHistoryEntriesCleared",
                 "EstimatedGoGameIntArrayBytes", "EstimatedRawSyncedBytes", "pendingBoardRefreshReason", "CanLocalMatchControl",
                 "FORCE_RESET_STALL_SECONDS", "turnStartedServerSecond", "IsForceResetTimeoutOpen",
                 "IsMoveMaskWarmupDeferred", "moveMaskWarmupDeferredUntilFrame",
                "groupVisitedStamp", "libertyVisitedStamp", "groupGeneration",
                "SetHandicap", "matchStarted&&!CanLocalForceReset()",
                 "collectedGroupLibertyCount", "CountLiberties(){return collectedGroupLibertyCount;}",
                 "RequestAiResourceWarmup",
                 "New game is locked during a live match; use Force Reset as table admin",
                "Controller changes are locked during a live match",
                "Starter changes are locked during a live match",
                "Side swaps are locked during a live match",
                "IsValidPackedMove");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGame.cs",
                "positionChanged", "lastNetworkRevision", "lastNetworkMoveCount",
                "configuration-only snapshot", "positionChanged&&aiController!=null",
                "aiSettings", "NotifyAISettingsApplied");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGame.cs",
                "if(localSide==sideToMove)return true;",
                "undoOfferSide==-localSide&&");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoAiController.cs",
                "Networking.IsOwner(game.gameObject)", "ownerLifecycle", "OnOwnershipTransferred",
                "ReaderMatchesPending", "game.revision!=search.rootRevision",
                "search.rootOwnerLifecycle!=ownerLifecycle", "AI search result became stale before commit",
                "FinishPerformance();",
                "GetActiveDifficulty", "activeSearchTransitionsPerFrame",
                "TryPlayFromAI", "PassFromAI", "tertiaryReader", "readerA", "readerB", "readerC",
                "activeReaderIndex", "StepSuperkoPreparation",
                "superkoProbePoints", "maxSuperkoPointsOneFrame",
                 "gpuGraphPassesThisFrame", "maxGpuSubmitMsOneFrame",
                 "PumpSearchUntilDeadline", "grantedFrameWorkMs",
                 "usedFrameWorkMs", "sameFramePhaseTransitions",
                 "grantedGpuBudgetMs", "usedGpuSubmitMs",
                 "avoidableYieldCount", "deadlineYieldCount",
                 "asyncReadbackWaitFrames",
                 "HasCriticalSearchWork", "visit0CompletionFrame",
                "ownershipTelemetryPoints", "ownershipTelemetryActive",
                "maxOwnershipTelemetryPointsOneFrame", "OwnershipTelemetryMatchesPending",
                "ConvertSideToMoveScoreToWhiteMinusBlack",
                "ConvertSideToMoveOwnershipLogitToWhitePositive",
                "CheckSearchWatchdog");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoDifficultyProfile.cs",
                "ApplyResolvedLocal", "settingsMirror", "GoAiSettings",
                "settingsRevision", "Ultrahard");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoAiSettings.cs",
                "[UdonSynced] public int sharedPreset", "blackUseCustom",
                 "whiteUseCustom", "configRevision", "ApplyDraft",
                  "CanLocalMatchControl", "RequestSerialization()", "OnPostSerialization",
                  "ApplyResolvedProfiles", "EnsureProfileReferences", "UsesCustom",
                  "ApplyPackedVisits");
            string settingsContractSource=ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoAiSettings.cs");
            RequireNotText(settingsContractSource,"stagedCustomMask",
                "settings staging must encode mode in the signed visits payload");
            RequireNotText(settingsContractSource,"StageBlackCustom",
                "settings must not retain per-side mask staging helpers");
            RequireNotText(settingsContractSource,"StageWhiteCustom",
                "settings must not retain per-side mask staging helpers");
            RequireNotText(settingsContractSource,"stagedSharedPreset",
                "settings must not retain a second staged settings domain");
            RequireNotText(settingsContractSource,"StageDraftValues",
                "settings must commit the complete draft through one transaction");
            RequireNotText(settingsContractSource,"ApplyStagedDraft",
                "settings must not retain a deferred staging commit path");
            string profileSource=ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoDifficultyProfile.cs");
            RequireNotText(profileSource,"public void SetCustom(",
                "resolved profiles cannot expose legacy local custom knobs");
            RequireNotText(profileSource,"public void ApplyPreset(",
                "resolved profiles cannot bypass GoAiSettings presets");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGpuNeuralOutputReader.cs",
                "resultOwnerLifecycle", "resultSettingsRevision", "resultHash0",
                "CancelAsyncReadback", "staleCallbacks", "READBACK_QUARANTINED",
                "READBACK_RECOVERED", "quarantinePending", "recoveredCallbacks");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoMctsSearch.cs",
                "edgePrior", "edgeValueSum", "nodeValueSum", "staleResultsDiscarded",
                "SubmitNeuralLogitsForNode", "GetMoveMask", "PHASE_EXPANDING",
                 "PHASE_SELECTING", "PHASE_BACKING_UP", "StepPendingExpansion",
                 "StepPendingExpansionUntil",
                "EnsureNodeStorage", "EnsureEdgeStorage", "ReleaseHeavyResources",
                "rootStateCopiedInts", "rootHistoryCopiedEntries",
                 "simulationUndoOperations", "nodeStorageAllocated", "edgeStorageAllocated",
                 "RestoreHistoryBucketsToCount", "lastScoreUtilityMilliseconds");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoSearchState.cs",
                "historyBucketHead", "historyBucketNext", "historyEntriesExamined",
                "RestoreHistoryBucketsToCount");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoFeatureEncoder.cs",
                 "EnsureLadderStorage", "ladderStorageAllocated", "encodeStepDeadline",
                 "StepEncodeUntil",
                "EncodeStepDeadlineReached", "ReleaseHeavyResources",
                "maxEncodeStepMilliseconds", "synchronous feature wrapper did not complete",
                "encodeNeedsTrackedSpatialClear", "spatialClearCount", "clearQuota",
                "baseGroupProcessedStamp", "baseGroupStamp", "ladderChangedStamp",
                "ladderChangedLocations", "ladderChangedOriginal", "libertyList",
                "generatedTargetLiberties", "generatedTargetLibertyCount",
                "ladderCaptureProbeStamp", "ladderCaptureProbeMove",
                "leftNeighbor", "rightNeighbor", "upNeighbor", "downNeighbor",
                "EnsureNeighborTables",
                "LADDER_CACHE_ENTRIES", "ladderCacheKey", "ladderCacheMaskPacked",
                "ladderCacheWorkingPacked", "FindLadderCache", "CommitLadderCache",
                "ladderMoveCaptureStamp", "ladderMoveCaptureGeneration",
                "BeginLadderAttempt",
                "EndLadderAttempt", "RecordLadderAttemptMutation", "SetLadderValue");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoFeatureEncoder.cs"),
                "if(libertyStamp[i]==groupStamp)output[count++] = i",
                "ladder liberty collection must not rescan all board points");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGpuNeuralRuntime.cs",
                "NN_INPUT_UPLOAD", "NN_RES_BLOCK", "NN_PACK_OUTPUT",
                "NN_READY_FOR_READBACK", "StepEvaluation",
                 "maxGpuSubmitMillisecondsPerFrame", "gpuGraphStageProgress",
                 "lastSubmitFrameMilliseconds",
                "MatchesGraphIdentity", "ReleaseAllResources",
                "Object.Destroy(spatialUploadTexture)",
                "Object.Destroy(globalUploadTexture)");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoGpuNeuralRuntime.cs"),
                "smoothedGpuSubmitMilliseconds=0f",
                "persistent graph-submit EMA must not be reset at every graph start");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoBoardView.cs",
                "REFRESH_CAPTURE", "REFRESH_UNDO", "REFRESH_RESET",
                "REFRESH_DESERIALIZE", "RefreshForReason",
                "captureAnimationStartCount", "UpdateHoverPresentation",
                "StepMoveMaskCacheTimeSliced", "MOVE_MASK_WARMUP_DEFAULT_MS",
                "hoverEvents", "fullBoardRefreshCount",
                "maxHoverPresentationMilliseconds");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoUI.cs",
                "bool forceResetController=game.CanLocalForceReset", "if(action==40)return forceResetController",
                "GetAppliedProfileSummaryLocalized", "forceResetArmedUntil",
                "liveStructureLocked", "controller&&!liveStructureLocked",
                "if(action==44)return game.CanLocalUndo",
                "Undo", "CanInteract", "searchProgressFill",
                "progressTarget", "progressCompleted", "settingsDraftDirty",
                "aiSettings", "sharedPresetDraft", "blackUseCustomDraft", "whiteUseCustomDraft",
                 "SetBlackFollowShared", "SetBlackCustom", "SetWhiteFollowShared", "SetWhiteCustom",
                 "InitializeSettingsDrafts",
                 "NotifyAppliedDifficultyChanged", "NormalizeStaticDifficultyLabel",
                 "value.Replace(\"\\u6781\\u96BE\",\"Ultrahard\")",
                 "value.Replace(\"\\u6781\\u96BE（Ultrahard）\",\"Ultrahard\")",
                 "blackCustomVisitsInput", "whiteCustomVisitsInput",
                 "RefreshIndependentCustomInput", "GoTelemetry owns panelTelemetryText");
            string uiSource=ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoUI.cs");
            RequireNotText(uiSource, "public Slider customVisitsSlider", "GoUI must not retain the legacy single custom slider");
            RequireNotText(uiSource, "blackCustomVisitsSlider", "GoUI must use numeric custom visits inputs");
            RequireNotText(uiSource, "whiteCustomVisitsSlider", "GoUI must use numeric custom visits inputs");
            RequireNotText(uiSource, "draftSettingsDisplaySide", "GoUI must not retain selected-side settings state");
            RequireNotText(uiSource, "SelectBlackSettings", "GoUI must not expose selected-side settings actions");
            RequireNotText(uiSource, "SelectWhiteSettings", "GoUI must not expose selected-side settings actions");
            ValidateSourceFile("Assets/PureUdonGo/Editor/GoWorldGenerator.cs",
                "AI Hint + Recommended Row", "RECOMMEND MOVE", "ENABLE BEFORE START",
                "searchProgressFill", "Independent Custom Visits Card",
                "Black Custom Visits Input", "White Custom Visits Input",
                "moveMaskWarmupManagedByPool", "ReaderRecoveryScenePath",
                "GenerateReaderRecoveryFixtureScene", "BuildReaderRecoveryRuntime",
                "games=1 controllers=1 runtimes=1 readers=3", "font, 28f");
            string generatorContract=ReadRequiredProjectText("Assets/PureUdonGo/Editor/GoWorldGenerator.cs");
            RequireNotText(generatorContract, "Legacy Custom Visits Slider", "generator must not emit the legacy single slider");
            RequireNotText(generatorContract, "Legacy Table Endpoints", "generator must not emit hidden table endpoints");
            RequireNotText(generatorContract, "Secondary Controller Endpoints", "generator must not emit hidden controller endpoints");
            RequireNotText(generatorContract, "Go Interactions · 361 Points · Collider Input",
                "generator must not emit the legacy 361-point collider board");
            RequireNotText(generatorContract, "cell.AddComponent<BoxCollider>()",
                "generator must not add physics colliders to board cells");
            RequireText(generatorContract, "BuildCentralBoardInput(origin,game,view)",
                "generator must use the single UGUI board receiver");
            RequireText(generatorContract, "AddBoardInputCollider(go,rect.sizeDelta)",
                "generator must provide the UIShape board trigger collider explicitly");
            RequireText(generatorContract, "AddStatusPanelCollider(canvasObject, panelSize)",
                "generator must retain one explicit status-panel collider");
            ValidateSourceFile(
                "Assets/PureUdonGo/Editor/GoClientSimReadbackRecoveryVerifier.cs",
                "GenerateReaderRecoveryFixtureScene", "ReaderRecoveryScenePath",
                "FindObjectsOfType<GoGpuNeuralOutputReader>().Length!=3",
                "FindObjectOfType<GoBoardPool>()!=null",
                "ObserveDiscardedCallbackForVerifier", "recoveredReaderSelections");
            ValidateSourceFile(
                "Assets/PureUdonGo/Editor/GoClientSimReaderPoolVerifier.cs",
                "GenerateReaderRecoveryFixtureScene", "ReaderRecoveryScenePath",
                "FindObjectsOfType<GoGpuNeuralOutputReader>().Length!=3",
                "FindObjectOfType<GoBoardPool>()!=null");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Editor/GoClientSimReadbackRecoveryVerifier.cs"),
                "GoWorldGenerator.ScenePath",
                "reader recovery verifier must not serialize the production room");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Editor/GoClientSimReaderPoolVerifier.cs"),
                "GoWorldGenerator.ScenePath",
                "reader-pool verifier must not serialize the production room");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoUiButton.cs",
                "DisableInteractive=true", "Button.onClick", "Press");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoTelemetry.cs",
                "showAdvancedAnalysis", "lastNeuralScoreMean", "SCORE LEAD W−B",
                "WINRATE  B", "WHITE OWNERSHIP MEAN", "STATIC CN SCORE W−B",
                "预计分差 白−黑", "折合子差 白−黑", "计算端",
                "rootOwnerLifecycle==aiController.ownerLifecycle", "ToggleAdvancedAnalysis", "CaptureFinalAnalysis", "hasFinalAnalysis",
                "finalAnalysisSnapshot", "finalAnalysisTargetVisits", "CaptureFinalAnalysisDeferred", "PublishDeferredFinalAnalysis", "OnPostSerialization",
                "EstimatedRawSyncedBytes", "lastSerializationByteCount",
                "serializationFailureCount");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoBoardPool.cs",
                "FIXED_VISIBLE_TABLES", "INITIAL_VISIBLE_TABLES", "MAX_TABLES", "visibleTableCount",
                "OnDeserialization", "OnOwnershipTransferred", "RefreshGpuSchedule",
                "int visibleCount=GetClampedVisibleTableCount()",
                "CalculateDispatchCapacity", "ResetFrameSamples", "frameMillisecondsSamples",
                "RecordFrameSample", "allocatedSearchWorkers",
                "allocatedEdgeCapacity", "estimatedSearchArrayBytes", "nodeStorageAllocated",
                "estimatedGpuTextureBytes", "RefreshMemoryAccounting", "StepRoomMoveMaskWarmup",
                 "moveMaskWarmupQuota", "moveMaskWarmupPointsThisFrame",
                  "moveMaskWarmupPointsDuringAiSearch", "moveMaskWarmupFramesDuringAiSearch",
                   "moveMaskWarmupMillisecondsPerFrame", "maxMoveMaskWarmupMillisecondsOneFrame",
                   "moveMaskWarmupSuppressedCriticalFrames", "HasCriticalRoomWork",
                  "IsMoveMaskWarmupDeferred",
                 "smoothedGpuSubmitMilliseconds",
                 "persistentGraphSubmitEmaMs");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoBoardPool.cs",
                "if(profilingEnabled)RecordFrameSample()", "diagnosticsEnabled&&",
                "pendingMoveMaskTables", "CacheReferences", "NotifySearchWork");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGpuNeuralRuntime.cs",
                 "useEquivalentFusion=true", "FUSED_PASSES_WITH_OWNERSHIP",
                 "FUSED_PASSES_WITHOUT_OWNERSHIP", "RunConvolutionBatchNorm",
                "RunConvolutionResidual", "PrepareRuntimeResources",
                "BeginPipelineWarmup", "StepPipelineWarmup", "pipelineWarmupToken");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoAiController.cs",
                "NeedsPerFrameTick", "WakeForStateChange", "RequestAiResourceWarmup",
                "resourceWarmupActive", "resourceWarmupComplete",
                "pipelineWarmupReadbackComplete", "AI pipeline warm-up failed");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoMctsSearch.cs",
                "PrepareRuntimeStorage");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoFeatureEncoder.cs",
                "PrepareRuntimeStorage");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoGpuNeuralOutputReader.cs",
                "PrepareRuntimeResources");
            ValidateSourceFile("Assets/PureUdonGo/Runtime/GoBoardInput.cs",
                 "game.TryPlay(loc)", "Click000", "Click360", "Hover360", "Exit360");
            ValidateSourceFile("Assets/PureUdonGo/Editor/GoBoardInputValidator.cs",
                "Board must contain only its enabled UIShape trigger collider",
                "Board must have one UGUI receiver and zero legacy collider cells");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Editor/GoBoardInputValidator.cs"),
                "Collider board must have exactly 361 cells",
                "board validator must not restore the legacy collider topology");
            ValidateSourceFile("Assets/PureUdonGo/Tests/GoLongHistoryStressProbe.cs",
                "targetPlies={0,32,64,128,256,520}", "CASE_TIMEOUT_SECONDS=90f",
                "deterministic random corpus exhausted all legal points case=",
                "feature encode failed case=", "incomplete search case=",
                 "historyEntriesExamined");
            ValidateSourceFile("Assets/PureUdonGo/Tests/GoPerformanceCorpusProbe.cs",
                "FIXTURE_COUNT=8", "targetVisits={BEGINNER_TEST_VISITS",
                "fixtureDDense", "fixtureELong", "fixtureHEndgame",
                "fixtureDDense.Length!=128", "fixtureELong.Length!=256",
                "fixtureHEndgame.Length!=520",
                "fixtureSequenceHashes", "frameSampleCounts", "frameSamples",
                "millisecondsPerVisit", "mctsRootCopyMilliseconds",
                "scoreUtilityMilliseconds", "inputPackingMilliseconds",
                "spatialApplyMilliseconds", "globalApplyMilliseconds",
                "inputBlitMilliseconds", "frameP50Milliseconds",
                "frameP95Milliseconds", "frameP99Milliseconds", "frameMaxMilliseconds");
            ValidateSourceFile("Assets/PureUdonGo/Editor/GoClientSimSettingsLifecycleVerifier.cs",
                "SendCustomEvent(\"_onDeserialization\")", "MatchesProfile",
                "targetVisits", "remote settings deserialization");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Editor/GoClientSimSettingsLifecycleVerifier.cs"),
                "SendCustomEvent(\"OnDeserialization\")",
                "settings verifier must use the compiled Udon deserialization event");
            RequireNotText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Tests/GoLongHistoryStressProbe.cs"),
                "if(!accepted)return;",
                "long-history stress must report a legal-move failure instead of hiding it");
            RequireText(ReadRequiredProjectText(
                "Assets/PureUdonGo/Tests/GoNetworkRecoveryProbe.cs"),
                "Finish(\"leave recovery invariant failed\")",
                "network recovery probe must terminate explicitly on an invariant failure");
            ValidateSourceFile("Assets/PureUdonGo/Tests/GoDerivedHistoryRebuildProbe.cs",
                "WARM_MOVE_MASK", "StepMoveMaskCacheTimeSliced", "IsMoveMaskCacheComplete",
                "moveMaskWarmupPoints", "moveMaskWarmupFrames",
                "moveMaskWarmupMaxPointsOneFrame", "RunTransactionalCorruptionCheck",
                "transactionalCorruptionPassed");

            string difficultySource = ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoDifficultyProfile.cs");
            string generatorSource = ReadRequiredProjectText(
                "Assets/PureUdonGo/Editor/GoWorldGenerator.cs");
            string gameSource = ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoGame.cs");
            string aiControllerSource = ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoAiController.cs");
            RequireNotText(gameSource, "[UdonSynced] public int[] hash0",
                "hash0 stays a local derived cache");
            RequireNotText(gameSource, "[UdonSynced] public int[] hash1",
                "hash1 stays a local derived cache");
            RequireNotText(gameSource, "[UdonSynced] public int[] hash2",
                "hash2 stays a local derived cache");
            RequireNotText(gameSource, "[UdonSynced] public int[] hash3",
                "hash3 stays a local derived cache");
            RequireNotText(gameSource, "[UdonSynced] public int[] historyStoneCount",
                "stone-count index stays a local derived cache");
            RequireNotText(gameSource, "[UdonSynced] public int gameState=STATE_PLAYING, winner=EMPTY, lastMove=NONE, koLoc=NONE, revision, settingsRevision, handicapStones",
                "settings revision stays outside the game position snapshot");
            RequireNotText(gameSource, "aiSettingsDisplaySide",
                "selected-side settings state was removed from the authoritative game");
            RequireNotText(gameSource, "aiController.OnGameOwnershipChanged();",
                "game ownership callback must not double-increment AI owner lifecycle");
            RequireNotText(aiControllerSource,
                "game.settingsRevision!=search.rootGameSettingsRevision",
                "configuration changes do not cancel an active SearchSession");
            RequireNotText(aiControllerSource,
                "activeProfile!=null&&activeProfile.settingsRevision!=search.rootSettingsRevision",
                "live profile revisions do not replace captured search settings");
            RequireNotText(uiSource,
                "game.NewGame();if(aiController!=null)aiController.InvalidateSearch()",
                "UI must not cancel a search when New Game is denied");
            RequireNotText(uiSource,
                "game.Pass();if(aiController!=null)aiController.InvalidateSearch()",
                "UI must not cancel a search when Pass is denied");
            RequireNotText(uiSource,
                "game.Resign();if(aiController!=null)aiController.InvalidateSearch()",
                "UI must not cancel a search when Resign is denied");
            RequireText(difficultySource, "BEGINNER_VISITS=8", "Beginner visits");
            RequireText(difficultySource, "ADVANCED_VISITS=20", "Advanced visits");
            RequireText(difficultySource, "MASTER_VISITS=48", "Master visits");
            RequireText(difficultySource, "ULTRAHARD_VISITS=120", "Ultrahard visits");
            RequireText(difficultySource, "ApplyPresetConstantsLocal", "complete deterministic preset resolver");
            RequireText(difficultySource, "maxNNQueries=maxVisits", "Custom overrides visits/query count only");
            RequireNotText(difficultySource, "cpuct=exploration", "Custom cannot mutate mirrored cpuct");
            RequireNotText(difficultySource, "moveTemperature=temperature", "Custom cannot mutate mirrored temperature");
            RequireNotText(difficultySource, "policyTopK=topK", "Custom cannot mutate mirrored policy topK");
            RequireText(difficultySource, "if(preset==ULTRAHARD)return \"Ultrahard\";",
                "Ultrahard localized display label");
            RequireNotText(difficultySource, "\u6781\u96BE", "Ultrahard display label is direct");
            RequireText(generatorSource, "\"Ultrahard\\n120 次搜索\"",
                "generated Ultrahard label is direct (no Chinese alias)");
            RequireText(generatorSource, "\"Ultrahard\\n120 VISITS\"",
                "generated English Ultrahard label uses the direct product name");
            RequireText(generatorSource, "\"BEGINNER\\n8 VISITS\"",
                "generated Beginner label uses the current public visit budget");
            RequireText(generatorSource, "\"ADVANCED\\n20 VISITS\"",
                "generated Advanced label uses the current public visit budget");
            RequireText(generatorSource, "\"MASTER\\n48 VISITS\"",
                "generated Master label uses the current public visit budget");
            RequireText(generatorSource, "\"当前局面分析\"",
                "center screen is explicitly current-position analysis");
            RequireNotText(generatorSource, "\"ULTRAHARD\\n120 VISITS\"",
                "generator must not emit an all-caps legacy Ultrahard label");
            RequireNotText(generatorSource, "\u6781\u96BE",
                "generator must not emit the legacy Chinese Ultrahard alias");
            RequireText(difficultySource, "GetPresetLabel", "difficulty labels");
            string settingsSource=ReadRequiredProjectText(
                "Assets/PureUdonGo/Runtime/GoAiSettings.cs");
            RequireText(settingsSource, "ApplyResolvedProfiles",
                "GoAiSettings owns deterministic profile resolution");
            RequireText(difficultySource, "ResolveFromSettings",
                "resolved profiles expose the deterministic settings resolver");
            RequireText(difficultySource, "int basePreset=settings.sharedPreset",
                "Custom profiles resolve from the synchronized shared preset");
        }

        private static void ValidateUdonProgramAssets()
        {
            Type[] requiredTypes =
            {
                typeof(GoGeneratedWorld), typeof(GoGame), typeof(GoBoardView),
                typeof(GoUI), typeof(GoTelemetry), typeof(GoDifficultyProfile),
                typeof(GoAiSettings),
                typeof(GoFeatureEncoder), typeof(GoSearchState), typeof(GoMctsSearch),
                typeof(GoGpuLayerExecutor), typeof(GoGpuNeuralRuntime),
                typeof(GoGpuNeuralOutputReader), typeof(GoAiController),
                typeof(GoBoardPool), typeof(GoBoardCell), typeof(GoBoardInput), typeof(GoUiButton)
            };
            UdonSharpProgramAsset[] programs = UdonSharpProgramAsset.GetAllUdonSharpPrograms();
            for (int i = 0; i < requiredTypes.Length; i++)
            {
                Type required = requiredTypes[i];
                UdonSharpProgramAsset found = null;
                for (int j = 0; j < programs.Length; j++)
                {
                    UdonSharpProgramAsset candidate = programs[j];
                    if (candidate == null || candidate.sourceCsScript == null) continue;
                    if (candidate.sourceCsScript.GetClass() == required)
                    {
                        found = candidate;
                        break;
                    }
                }
                Require(found != null, "Udon program has a non-null source script for " + required.Name);
                if (found != null)
                    Require(found.GetSerializedUdonProgramAsset() != null,
                        "Udon program is compiled for " + required.Name);
            }
        }

        private static void ValidateGeneratedScene()
        {
            if (!File.Exists(ProjectFile(GoWorldGenerator.ScenePath)))
            {
                Fail("Generated production scene is missing: " + GoWorldGenerator.ScenePath);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(GoWorldGenerator.ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded, "generated scene opens successfully");
            GameObject[] roots = scene.GetRootGameObjects();
            GoGeneratedWorld[] markers = UnityEngine.Object.FindObjectsOfType<GoGeneratedWorld>(true);
            Require(markers.Length == 1, "scene contains exactly one generated-world marker");
            GameObject root = markers.Length == 1 ? markers[0].gameObject : null;
            if (root == null)
                return;
            Require(root.name == "PureUdonGo.ProductionRoot", "generated root has stable name");
            Require(markers[0].schemaVersion == GoGeneratedWorld.CURRENT_SCHEMA,
                "generated scene schema matches the current runtime contract");
            Require(root.transform.Find("PureUdonGo.Environment · Indoor Room") != null, "indoor room environment root exists");
            Require(root.transform.Find("PureUdonGo.TablePool") != null, "table-pool root exists");
            Require(root.GetComponent<GoBoardPool>() != null, "synchronized GoBoardPool exists");
            const string primaryTablePath = "PureUdonGo.TablePool/PureUdonGo.Table.000";
            Require(root.transform.Find(primaryTablePath + "/PureUdonGo.Gameplay") != null,
                "primary gameplay root exists");
            Require(root.transform.Find(primaryTablePath + "/PureUdonGo.Board") != null,
                "primary board root exists");
            Require(root.transform.Find(primaryTablePath + "/PureUdonGo.UI") != null,
                "primary UI root exists");
            Require(root.transform.Find(primaryTablePath + "/PureUdonGo.Telemetry") != null,
                "primary telemetry root exists");

            VRCSceneDescriptor sceneDescriptor = root.GetComponent<VRCSceneDescriptor>();
            Require(sceneDescriptor != null, "VRChat scene descriptor exists on generated root");
            if (sceneDescriptor != null)
            {
                Require(sceneDescriptor.ReferenceCamera != null,
                    "scene descriptor has the generated reference camera");
                Require(sceneDescriptor.spawns != null && sceneDescriptor.spawns.Length > 0 &&
                    sceneDescriptor.spawns[0] != null,
                    "scene descriptor has a generated player spawn");
                if (sceneDescriptor.spawns != null && sceneDescriptor.spawns.Length > 0 &&
                    sceneDescriptor.spawns[0] != null)
                    Require(sceneDescriptor.RespawnHeightY < sceneDescriptor.spawns[0].position.y,
                        "scene respawn height is below the generated player spawn");
            }

            GoGame[] games = root.GetComponentsInChildren<GoGame>(true);
            GoBoardView[] views = root.GetComponentsInChildren<GoBoardView>(true);
            GoUI[] uis = root.GetComponentsInChildren<GoUI>(true);
            GoTelemetry[] telemetry = root.GetComponentsInChildren<GoTelemetry>(true);
            GoDifficultyProfile[] difficulties = root.GetComponentsInChildren<GoDifficultyProfile>(true);
            GoAiSettings[] settings = root.GetComponentsInChildren<GoAiSettings>(true);
            GoFeatureEncoder[] encoders = root.GetComponentsInChildren<GoFeatureEncoder>(true);
            GoSearchState[] states = root.GetComponentsInChildren<GoSearchState>(true);
            GoMctsSearch[] searches = root.GetComponentsInChildren<GoMctsSearch>(true);
            GoGpuLayerExecutor[] executors = root.GetComponentsInChildren<GoGpuLayerExecutor>(true);
            GoGpuNeuralRuntime[] runtimes = root.GetComponentsInChildren<GoGpuNeuralRuntime>(true);
            GoGpuNeuralOutputReader[] readers = root.GetComponentsInChildren<GoGpuNeuralOutputReader>(true);
            GoAiController[] ais = root.GetComponentsInChildren<GoAiController>(true);
            GoBoardPool boardPool = root.GetComponent<GoBoardPool>();

            Require(boardPool != null && boardPool.tableRoots != null &&
                boardPool.tableRoots.Length == GoBoardPool.MAX_TABLES &&
                boardPool.primaryUI != null,
                "GoBoardPool has all 16 synchronized table references");
            Require(games.Length == GoBoardPool.MAX_TABLES,
                "exactly 16 independent GoGame instances");
            Require(views.Length == GoBoardPool.MAX_TABLES,
                "exactly 16 independent GoBoardView instances");
            Require(uis.Length == GoBoardPool.MAX_TABLES,
                "exactly 16 independent GoUI instances");
            Require(telemetry.Length == GoBoardPool.MAX_TABLES,
                "exactly 16 independent GoTelemetry instances");
            Require(difficulties.Length == GoBoardPool.MAX_TABLES * 2,
                "exactly two local resolved GoDifficultyProfiles per table");
            Require(settings.Length == GoBoardPool.MAX_TABLES,
                "exactly one synchronized GoAiSettings domain per table");
            Require(encoders.Length == GoBoardPool.MAX_TABLES &&
                states.Length == GoBoardPool.MAX_TABLES && searches.Length == GoBoardPool.MAX_TABLES,
                "16 independent feature/search state stacks");
            Require(executors.Length == GoBoardPool.MAX_TABLES &&
                runtimes.Length == GoBoardPool.MAX_TABLES &&
                readers.Length == GoBoardPool.MAX_TABLES * 3,
                "16 independent GPU runtime stacks with three readback channels per table");
            Require(ais.Length == GoBoardPool.MAX_TABLES,
                "exactly 16 GoAiController instances");
            if (boardPool == null || boardPool.tableRoots == null ||
                boardPool.tableRoots.Length != GoBoardPool.MAX_TABLES ||
                games.Length != GoBoardPool.MAX_TABLES || views.Length != GoBoardPool.MAX_TABLES ||
                uis.Length != GoBoardPool.MAX_TABLES || telemetry.Length != GoBoardPool.MAX_TABLES ||
                difficulties.Length != GoBoardPool.MAX_TABLES * 2 ||
                settings.Length != GoBoardPool.MAX_TABLES ||
                encoders.Length != GoBoardPool.MAX_TABLES || states.Length != GoBoardPool.MAX_TABLES ||
                searches.Length != GoBoardPool.MAX_TABLES || executors.Length != GoBoardPool.MAX_TABLES ||
                runtimes.Length != GoBoardPool.MAX_TABLES || readers.Length != GoBoardPool.MAX_TABLES * 3 ||
                ais.Length != GoBoardPool.MAX_TABLES)
                return;

            Require(!boardPool.profilingEnabled&&!boardPool.diagnosticsEnabled,
                "production idle statistics default off");
            Require(boardPool.games!=null&&boardPool.games.Length==GoBoardPool.MAX_TABLES&&
                boardPool.views!=null&&boardPool.views.Length==GoBoardPool.MAX_TABLES&&
                boardPool.uis!=null&&boardPool.uis.Length==GoBoardPool.MAX_TABLES&&
                boardPool.controllers!=null&&boardPool.controllers.Length==GoBoardPool.MAX_TABLES,
                "room scheduler has prebound runtime references");

            Transform primaryTable = root.transform.Find(primaryTablePath);
            GoGame game = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoGame>(true);
            GoBoardView view = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoBoardView>(true);
            GoUI ui = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoUI>(true);
            GoTelemetry boardTelemetry = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoTelemetry>(true);
            GoAiSettings aiSettings = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoAiSettings>(true);
            GoFeatureEncoder encoder = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoFeatureEncoder>(true);
            GoSearchState searchState = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoSearchState>(true);
            GoMctsSearch search = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoMctsSearch>(true);
            GoGpuLayerExecutor executor = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoGpuLayerExecutor>(true);
            GoGpuNeuralRuntime runtime = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoGpuNeuralRuntime>(true);
            GoAiController ai = primaryTable == null ? null : primaryTable.GetComponentInChildren<GoAiController>(true);
            GoGpuNeuralOutputReader reader = ai == null ? null : ai.reader;
            GoGpuNeuralOutputReader alternateReader = ai == null ? null : ai.alternateReader;
            GoGpuNeuralOutputReader tertiaryReader = ai == null ? null : ai.tertiaryReader;
            GoDifficultyProfile difficulty = game == null ? null : game.whiteDifficulty;
            GoDifficultyProfile blackDifficulty = game == null ? null : game.blackDifficulty;

            Require(game.board != null && game.board.Length == GoGame.AREA, "361 synced board cells");
            Require(game.previousBoard1 != null && game.previousBoard1.Length == GoGame.AREA &&
                game.previousBoard2 != null && game.previousBoard2.Length == GoGame.AREA,
                "two board-history planes");
            Require(game.recentMoveLoc != null && game.recentMoveLoc.Length == 5 &&
                game.recentMovePla != null && game.recentMovePla.Length == 5,
                "five recent-move history entries");
            Require(game.hash0 != null && game.hash0.Length == GoGame.MAX_POSITION_HISTORY &&
                game.hash1 != null && game.hash1.Length == GoGame.MAX_POSITION_HISTORY &&
                game.hash2 != null && game.hash2.Length == GoGame.MAX_POSITION_HISTORY &&
                game.hash3 != null && game.hash3.Length == GoGame.MAX_POSITION_HISTORY,
                "four-lane local positional history cache");
            Require(game.moveHistory != null && game.moveHistory.Length == GoGame.MAX_MOVES,
                "packed move history for Go rollback");
            Require(GoGame.SYNCED_INT_ARRAY_COUNT == 3141 &&
                GoGame.SYNCED_INT_ARRAY_RAW_BYTES == 12564,
                "compact synchronized Go array payload lower bound");
            Require(game.positionalSuperko && game.areaScoring && game.multiStoneSuicideLegal &&
                game.komiTimes2 == 15, "generated Go rules and 7.5 komi defaults");
            Require(boardPool.visibleTableCount == GoBoardPool.INITIAL_VISIBLE_TABLES &&
                boardPool.primaryUI == ui,
                "pool starts with three visible tables and the primary UI");
            HashSet<GoGpuNeuralOutputReader> uniqueReaders =
                new HashSet<GoGpuNeuralOutputReader>();
            for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
            {
                Transform table = root.transform.Find("PureUdonGo.TablePool/PureUdonGo.Table." +
                    tableIndex.ToString("000"));
                GoGpuNeuralOutputReader[] tableReaders = table == null ?
                    new GoGpuNeuralOutputReader[0] :
                    table.GetComponentsInChildren<GoGpuNeuralOutputReader>(true);
                GoAiController tableAi = table == null ? null :
                    table.GetComponentInChildren<GoAiController>(true);
                GoGpuNeuralRuntime tableRuntime = table == null ? null :
                    table.GetComponentInChildren<GoGpuNeuralRuntime>(true);
                Require(tableAi!=null&&tableAi.boardPool==boardPool&&tableAi.game!=null&&
                    tableAi.game.boardPool==boardPool&&tableAi.game.tableIndex==tableIndex,
                    "table wake-up events target their room and stable table index");
                Require(tableRuntime!=null,
                    "generated table has a GPU runtime whose Start enables validated graph fusion");
                Require(table != null && boardPool.tableRoots[tableIndex] == table.gameObject &&
                    table.gameObject.activeSelf == (tableIndex < GoBoardPool.INITIAL_VISIBLE_TABLES),
                    "table pool visibility and stable root at index " + tableIndex);
                Require(table != null && table.GetComponentInChildren<GoGame>(true) != null &&
                    table.GetComponentInChildren<GoBoardView>(true) != null &&
                    table.GetComponentInChildren<GoUI>(true) != null &&
                    table.GetComponentInChildren<GoTelemetry>(true) != null &&
                    table.GetComponentInChildren<GoAiController>(true) != null,
                    "table pool runtime references at index " + tableIndex);
                Transform tableBoard = table == null ? null : table.Find("PureUdonGo.Board");
                Transform tableDeck = table == null ? null : table.Find(
                    "PureUdonGo.UI/Adaptive Control Wall Mount/AI Control Deck · VR Scale");
                BoxCollider tableDeckCollider = tableDeck == null ? null :
                    tableDeck.GetComponent<BoxCollider>();
                Transform tableInputCanvas = tableBoard == null ? null : tableBoard.Find(
                    "Board Origin · 19 × 19/Board Interaction Canvas · 361 Intersections");
                BoxCollider tableBoardCollider = tableInputCanvas == null ? null :
                    tableInputCanvas.GetComponent<BoxCollider>();
                Collider[] tableBoardColliders = tableBoard == null ? new Collider[0] :
                    tableBoard.GetComponentsInChildren<Collider>(true);
                Require(tableBoard != null && tableBoardColliders.Length == 1 &&
                    tableBoardColliders[0] == tableBoardCollider && tableBoardCollider != null &&
                    tableBoardCollider.enabled && tableBoardCollider.isTrigger,
                    "table " + tableIndex + " board has only its non-blocking UIShape trigger collider");
                Require(tableDeckCollider != null && tableDeckCollider.enabled &&
                    !tableDeckCollider.isTrigger,
                    "table " + tableIndex + " keeps the status-panel collider");
                Require(tableReaders.Length == 3,
                    "table " + tableIndex + " has active readers A/B/C for stale callback quarantine");
                bool tableReadersAreIndependent = tableReaders.Length == 3 &&
                    tableAi != null && tableRuntime != null &&
                    tableAi.reader != null && tableAi.alternateReader != null &&
                    tableAi.tertiaryReader != null &&
                    tableAi.readerA == tableAi.reader &&
                    tableAi.readerB == tableAi.alternateReader &&
                    tableAi.readerC == tableAi.tertiaryReader &&
                    tableAi.reader != tableAi.alternateReader &&
                    tableAi.reader != tableAi.tertiaryReader &&
                    tableAi.alternateReader != tableAi.tertiaryReader &&
                    tableAi.reader.runtime == tableRuntime &&
                    tableAi.alternateReader.runtime == tableRuntime &&
                    tableAi.tertiaryReader.runtime == tableRuntime &&
                    uniqueReaders.Add(tableAi.reader) &&
                    uniqueReaders.Add(tableAi.alternateReader) &&
                    uniqueReaders.Add(tableAi.tertiaryReader);
                Require(tableReadersAreIndependent,
                    "table " + tableIndex + " reader A/B/C are unique and share only that table runtime");
            }
            Require(view.game == game && ui.game == game && boardTelemetry.game == game,
                "game/view/UI/telemetry binding");
            Require(view.stoneObjects != null && view.stoneObjects.Length == GoGame.AREA &&
                view.stoneRenderers != null && view.stoneRenderers.Length == GoGame.AREA,
                "361 preallocated visual stones");
            Require(view.previewStone != null && view.previewStoneRenderer != null &&
                !view.previewStone.activeSelf && view.previewStone.transform.localPosition.y < 0f &&
                view.moveMaskWarmupManagedByPool,
                "one pooled preview stone starts below the table and is teleported on hover");
            Require(boardTelemetry.panelText == ui.panelTelemetryText,
                "telemetry is bound to the generated center screen");
            Require(ui.searchProgressFill != null &&
                ui.searchProgressFill.type == Image.Type.Filled &&
                ui.searchProgressFill.fillMethod == Image.FillMethod.Horizontal,
                "center search progress is bound to a horizontal filled image");
            Require(!boardTelemetry.showDeveloperDiagnostics&&
                !boardTelemetry.showAdvancedAnalysis&&!ui.advancedAnalysisEnabled,
                "generated center analysis defaults to the player-facing collapsed view");
            Require(boardTelemetry.panelText != null &&
                HasFixedAnalysisTemplate(boardTelemetry.panelText.text, true) &&
                boardTelemetry.panelText.text.IndexOf("REV ", StringComparison.OrdinalIgnoreCase) < 0,
                "center telemetry uses the fixed eleven-line player-facing analysis ABI");
            Require(aiSettings != null && game.aiSettings == aiSettings && ui.aiSettings == aiSettings &&
                ai.aiSettings == aiSettings && aiSettings.blackDifficulty == game.blackDifficulty &&
                aiSettings.whiteDifficulty == game.whiteDifficulty &&
                game.blackDifficulty != null && game.whiteDifficulty != null &&
                ui.blackDifficulty == game.blackDifficulty && ui.whiteDifficulty == game.whiteDifficulty,
                "one synchronized settings domain resolves both local black/white profiles");
            Require(ui.panelActionText != null && ui.panelHintText != null &&
                ui.undoButtonText != null && ui.drawButtonText != null &&
                ui.advancedAnalysisButtonText != null &&
                 ui.aiHintPermissionButtonText != null &&
                 ui.aiMoveNowButtonText != null &&
                 ui.forceResetButtonText != null && ui.panelAppliedProfileText != null &&
                ui.blackCustomVisitsInput != null && ui.whiteCustomVisitsInput != null &&
                ui.blackCustomVisitsValueText != null && ui.whiteCustomVisitsValueText != null &&
                ui.blackCustomVisitsInput.gameObject.activeSelf &&
                ui.whiteCustomVisitsInput.gameObject.activeSelf &&
                ui.blackCustomVisitsInput.contentType == TMP_InputField.ContentType.IntegerNumber &&
                ui.whiteCustomVisitsInput.contentType == TMP_InputField.ContentType.IntegerNumber &&
                ui.blackCustomVisitsInput.characterValidation == TMP_InputField.CharacterValidation.Integer &&
                ui.whiteCustomVisitsInput.characterValidation == TMP_InputField.CharacterValidation.Integer &&
                ui.searchProgressFill != null,
                "Go action, AI hint permission, immediate move and independent black/white numeric custom visits inputs are bound");
            ValidateControlActionSet(ui);
            if (ui.controlButtons != null)
                for (int i = 0; i < ui.controlButtons.Length; i++)
                    Require(ui.controlButtons[i] != null,
                        "control-wall Button binding is non-null at index " + i);
            Require(ui.localizedTexts != null && ui.chineseTexts != null && ui.englishTexts != null &&
                ui.localizedTexts.Length > 0 &&
                ui.localizedTexts.Length == ui.chineseTexts.Length &&
                ui.localizedTexts.Length == ui.englishTexts.Length,
                "static UI labels have one active-language text slot each");
            Require(view.hintMarker != null && view.previewStone != null &&
                !view.previewStone.activeSelf && view.previewStone.transform.localPosition.y < 0f,
                "board has synchronized hint marker and a pooled hidden preview stone");
            Require(view.captureAnimationDuration >= 0.18f && view.captureAnimationDuration <= 0.30f &&
                view.captureSinkDistance > 0f && view.captureSinkDistance <= 0.05f,
                "board capture presentation uses a bounded local sink/shrink animation");

            Require(executor.weightTexture == AssetDatabase.LoadAssetAtPath<Texture2D>(RuntimeWeightPath),
                "GPU executor binds production weight asset");
            Require(executor.weightWidth == GoProductionModelLayout.WeightWidth &&
                executor.weightHeight == GoProductionModelLayout.WeightHeight,
                "GPU executor dimensions match baked layout");
            Require(executor.layerMaterial != null && executor.layerMaterial.shader != null,
                "GPU executor has NN layer material");
            Require(runtime.executor == executor && runtime.featureEncoder == encoder,
                "GPU runtime/executor/encoder binding");
            Require(reader.runtime == runtime, "async output reader binding");
            Require(reader.eventReceiver == UdonSharpEditorUtility.GetBackingUdonBehaviour(reader),
                "GPU reader targets its exact serialized Udon event receiver");
            Require(alternateReader != null && alternateReader != reader &&
                alternateReader.runtime == runtime &&
                alternateReader.eventReceiver == UdonSharpEditorUtility.GetBackingUdonBehaviour(alternateReader),
                "GPU stale-callback quarantine reader is bound to the same table runtime");
            Require(tertiaryReader != null && tertiaryReader != reader && tertiaryReader != alternateReader &&
                tertiaryReader.runtime == runtime &&
                tertiaryReader.eventReceiver == UdonSharpEditorUtility.GetBackingUdonBehaviour(tertiaryReader),
                "GPU second stale-callback quarantine reader is bound to the same table runtime");
            Require(search.simulationState == searchState && search.authoritativeGame == game,
                "MCTS state/game binding");
            Require(ai.game == game && ai.aiSettings == aiSettings && ai.difficulty == difficulty &&
                ai.blackDifficulty == blackDifficulty && ai.whiteDifficulty == difficulty &&
                ai.encoder == encoder &&
                ai.runtime == runtime && ai.reader == reader &&
                ai.alternateReader == alternateReader && ai.tertiaryReader == tertiaryReader &&
                ai.readerA == reader && ai.readerB == alternateReader &&
                ai.readerC == tertiaryReader && ai.activeReaderIndex == 0 &&
                ai.search == search &&
                ai.view == view && ai.ui == ui && ai.telemetry == boardTelemetry,
                "AI controller complete binding");

            Require(difficulty != null && blackDifficulty != null &&
                difficulty.preset == GoDifficultyProfile.BEGINNER && difficulty.maxVisits == GoDifficultyProfile.BEGINNER_VISITS &&
                difficulty.maxNNQueries == GoDifficultyProfile.BEGINNER_VISITS && blackDifficulty.preset == GoDifficultyProfile.BEGINNER &&
                blackDifficulty.maxVisits == GoDifficultyProfile.BEGINNER_VISITS && blackDifficulty.maxNNQueries == GoDifficultyProfile.BEGINNER_VISITS,
                "generated default is Beginner with 8 visits on both side profiles");
            Require(GoMctsSearch.MAX_SUPPORTED_VISITS == 1226,
                "fixed engine capacity is 1226 visits; Ultrahard preset is 120");
            Require(ai.mode == GoAiController.MODE_PVAI && ai.aiColor == GoGame.WHITE && ai.autoStart &&
                !game.blackIsAI && game.whiteIsAI && !game.matchStarted && game.starter == GoGame.BLACK,
                "generated default mode is PvAI with White AI");

            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            foreach(GoBoardView tableView in views)GoBoardInputValidator.Validate(tableView);
            Require(canvases.Length == GoBoardPool.MAX_TABLES+root.GetComponentsInChildren<GoBoardInput>(true).Length,
                "one control-wall Canvas plus an optional exclusive UGUI board per table");
            for (int i = 0; i < canvases.Length; i++)
            {
                Require(canvases[i].GetComponent<GraphicRaycaster>() != null,
                    "Canvas has GraphicRaycaster: " + canvases[i].name);
                Require(canvases[i].GetComponent<VRCUiShape>() != null,
                    "Canvas has VRCUiShape: " + canvases[i].name);
            }
            for(int i=0;i<uis.Length;i++)ValidateControlActionSet(uis[i]);
            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            int persistentListeners = 0;
            for (int i = 0; i < buttons.Length; i++)
                persistentListeners += buttons[i].onClick.GetPersistentEventCount();
            Require(persistentListeners == buttons.Length,
                "every generated button has one persistent Udon listener");
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            bool hasUltrahard = false;
            bool hasExactUltrahard = false;
            bool hasLegacyUltrahardLabel = false;
            bool hasLegacyChineseUltrahardLabel = false;
            bool hasCurrentPositionAnalysis = false;
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].text.IndexOf("ULTRAHARD", StringComparison.OrdinalIgnoreCase) >= 0)
                    hasUltrahard = true;
                if (labels[i].text.IndexOf("Ultrahard", StringComparison.Ordinal) >= 0)
                    hasExactUltrahard = true;
                if (labels[i].text.IndexOf("ULTRAHARD", StringComparison.Ordinal) >= 0)
                    hasLegacyUltrahardLabel = true;
                if (labels[i].text.IndexOf("极难", StringComparison.Ordinal) >= 0)
                    hasLegacyChineseUltrahardLabel = true;
                if (labels[i].text.IndexOf("当前局面分析", StringComparison.Ordinal) >= 0 ||
                    labels[i].text.IndexOf("CURRENT POSITION ANALYSIS", StringComparison.OrdinalIgnoreCase) >= 0)
                    hasCurrentPositionAnalysis = true;
            }
            Require(hasUltrahard, "control wall exposes Ultrahard difficulty label");
            Require(hasExactUltrahard, "control wall keeps the exact Ultrahard product casing");
            Require(!hasLegacyUltrahardLabel,
                "generated labels do not retain the legacy all-caps ULTRAHARD casing");
            Require(!hasLegacyChineseUltrahardLabel,
                "generated labels use direct Ultrahard instead of the Chinese alias/composite");
            Require(hasCurrentPositionAnalysis,
                "center screen exposes the current-position analysis title");

            AudioSource[] audioSources = root.GetComponentsInChildren<AudioSource>(true);
            AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
            Require(audioSources.Length == GoBoardPool.MAX_TABLES && listeners.Length == 1,
                "one spatial board AudioSource per table and one reference AudioListener");
            for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
            {
                Transform table = root.transform.Find("PureUdonGo.TablePool/PureUdonGo.Table." +
                    tableIndex.ToString("000"));
                GoGame tableGame = table == null ? null : table.GetComponentInChildren<GoGame>(true);
                Require(tableGame != null && tableGame.audioSource != null &&
                    tableGame.placeClip != null && tableGame.captureClip != null &&
                    tableGame.passClip != null && tableGame.gameEndClip != null &&
                    tableGame.resetClip != null,
                    "all five generated Go audio cues are bound at table " + tableIndex);
            }

            ValidateSpatialPlacement(root);
            ValidateWalkableEnvironment(root);
            ValidateRenderTextureContracts(executor);
            ValidateUniqueGeneratedNames(root);
        }

        private static void ValidateWalkableEnvironment(GameObject root)
        {
            Transform floor = root.transform.Find(
                "PureUdonGo.Environment · Indoor Room/Indoor Room Floor · Walkable");
            Collider collider = floor == null ? null : floor.GetComponent<Collider>();
            Renderer renderer = floor == null ? null : floor.GetComponent<Renderer>();
            VRCSceneDescriptor descriptor = root.GetComponent<VRCSceneDescriptor>();
            Require(floor != null && collider != null && collider.enabled && !collider.isTrigger &&
                floor.gameObject.layer == 0,
                "indoor floor has an enabled non-trigger default-layer collider for VR locomotion");
            Require(renderer != null && renderer.bounds.size.x >= GoWorldGenerator.GalleryFloorLengthWorld - 0.10f,
                "indoor floor spans the complete three-table room");
            ValidateIndoorRoom(root);
            Require(descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0 &&
                descriptor.spawns[0] != null && descriptor.spawns[0].position.y > floor.position.y &&
                descriptor.RespawnHeightY < floor.position.y,
                "spawn is above the walkable floor and respawn height is below it");
        }

        private static void ValidateIndoorRoom(GameObject root)
        {
            string[] names =
            {
                "Indoor Back Wall · Mist Wallpaper",
                "Indoor Front Wall · Beige Wallpaper",
                "Indoor Left Wall · Beige Wallpaper",
                "Indoor Right Wall · Mist Wallpaper",
                "Indoor Ceiling · Soft White"
            };
            for (int i = 0; i < names.Length; i++)
            {
                Transform wall = root.transform.Find("PureUdonGo.Environment · Indoor Room/" + names[i]);
                Collider collider = wall == null ? null : wall.GetComponent<Collider>();
                Renderer renderer = wall == null ? null : wall.GetComponent<Renderer>();
                Require(wall != null && collider != null && collider.enabled && !collider.isTrigger &&
                    renderer != null && renderer.sharedMaterial != null,
                    "indoor room surface exists with collision/material: " + names[i]);
            }
            Require(root.transform.Find("PureUdonGo.Environment · Indoor Room") != null,
                "generated root contains the indoor room environment");
        }

        private static void ValidateSpatialPlacement(GameObject root)
        {
            const string primaryTablePath = "PureUdonGo.TablePool/PureUdonGo.Table.000";
            Transform board = root.transform.Find(primaryTablePath + "/PureUdonGo.Board");
            Transform deck = root.transform.Find(
                primaryTablePath + "/PureUdonGo.UI/Adaptive Control Wall Mount/AI Control Deck · VR Scale");
            Transform stone = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Stone Pool · 361 Intersections/Stone 000");
            Transform stonePool = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Stone Pool · 361 Intersections");
            Transform boardInputCanvas = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Board Interaction Canvas · 361 Intersections");
            Transform boardOrigin = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19");
            Renderer boardRenderer = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Maple Playing Surface")?.GetComponent<Renderer>();
            LineRenderer gridLine = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · File 00")?.GetComponent<LineRenderer>();
            LineRenderer outerFile = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · File 18")?.GetComponent<LineRenderer>();
            LineRenderer outerRank = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · Rank 00")?.GetComponent<LineRenderer>();
            Transform duplicateTrim = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Trim · Front");
            Renderer stoneRenderer = stone == null ? null : stone.GetComponent<Renderer>();
            RectTransform deckRect = deck == null ? null : deck.GetComponent<RectTransform>();
            BoxCollider statusPanelCollider = deck == null ? null : deck.GetComponent<BoxCollider>();
            bool centralInput = boardInputCanvas != null &&
                boardInputCanvas.GetComponent<GoBoardInput>() != null;
            Collider[] boardColliders = board == null ? new Collider[0] :
                board.GetComponentsInChildren<Collider>(true);
            Collider boardOriginCollider = boardOrigin == null ? null : boardOrigin.GetComponent<Collider>();
            BoxCollider boardInputCollider = boardInputCanvas == null ? null :
                boardInputCanvas.GetComponent<BoxCollider>();
            Require(board != null && deckRect != null && stoneRenderer != null && boardRenderer != null &&
                stonePool != null && centralInput && statusPanelCollider != null &&
                statusPanelCollider.enabled && !statusPanelCollider.isTrigger && boardColliders.Length == 1 &&
                boardColliders[0] == boardInputCollider && boardInputCollider != null &&
                boardInputCollider.enabled && boardInputCollider.isTrigger && boardOriginCollider == null,
                "spatial placement probe objects exist with a non-blocking board trigger and solid status panel");
            if (board == null || deckRect == null || stoneRenderer == null || boardRenderer == null ||
                stonePool == null || !centralInput || statusPanelCollider == null ||
                boardColliders.Length != 1 || boardInputCollider == null || boardOriginCollider != null)
                return;

            Require(deckRect.localPosition.y > 0.18f,
                "control wall canvas is lifted clear of the board surface");
            Require(deck.position.z - boardRenderer.bounds.max.z > 0.09f,
                "control wall canvas is physically in front of the rear board edge");
            Require(stoneRenderer.bounds.min.y >= boardRenderer.bounds.max.y - 0.0005f,
                "generated pebble Stone 000 does not intersect the maple surface");
            Require(stone.localScale.x > 0.379f && stone.localScale.x < 0.381f &&
                stone.localScale.z > 0.379f && stone.localScale.z < 0.381f &&
                stone.localScale.y > 0.169f && stone.localScale.y < 0.171f &&
                stone.localPosition.y > 0.170f && stone.localPosition.y < 0.172f,
                "generated Go stones use the smaller, flatter near-surface production profile");
            Require(viewMarkerIsOnPointPlane(root),
                "board preview marker shares the Go grid datum with the UGUI point input");
            Require(gridLine != null && Mathf.Abs(gridLine.startWidth - 0.0012f) < 0.0001f &&
                Mathf.Abs(gridLine.endWidth - 0.0012f) < 0.0001f &&
                gridLine.shadowCastingMode == ShadowCastingMode.Off && !gridLine.receiveShadows,
                "19x19 grid lines use the supplied hairline visual profile");
            Require(outerFile != null && outerRank != null && duplicateTrim == null &&
                Mathf.Abs(outerFile.startWidth - 0.0012f) < 0.0001f &&
                Mathf.Abs(outerFile.endWidth - 0.0012f) < 0.0001f &&
                Mathf.Abs(outerRank.startWidth - 0.0012f) < 0.0001f &&
                Mathf.Abs(outerRank.endWidth - 0.0012f) < 0.0001f &&
                outerFile.sharedMaterial == gridLine.sharedMaterial &&
                outerRank.sharedMaterial == gridLine.sharedMaterial &&
                Mathf.Abs(outerFile.GetPosition(0).y - gridLine.GetPosition(0).y) < 0.0001f &&
                Mathf.Abs(outerRank.GetPosition(0).y - gridLine.GetPosition(0).y) < 0.0001f &&
                outerFile.shadowCastingMode == ShadowCastingMode.Off && !outerFile.receiveShadows &&
                outerRank.shadowCastingMode == ShadowCastingMode.Off && !outerRank.receiveShadows,
                "outer board edge is the single thin coplanar ink layer; no duplicate trim exists");
            MeshFilter filter = stone.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount >= 200 &&
                Mathf.Abs(filter.sharedMesh.bounds.size.x - 1f) < 0.01f &&
                Mathf.Abs(filter.sharedMesh.bounds.size.y - 1f) < 0.01f &&
                Mathf.Abs(filter.sharedMesh.bounds.size.z - 1f) < 0.01f,
                "generated pebble mesh is a complete unit rounded sphere");
            for (int loc = 0; loc < GoGame.AREA; loc++)
            {
                Transform placedStone = stonePool.Find("Stone " + loc.ToString("000"));
                Require(placedStone != null &&
                    placedStone.GetComponentsInChildren<Collider>(true).Length == 0,
                    "stone pool remains collider-free at location " + loc);
            }
            for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
            {
                Transform table = root.transform.Find("PureUdonGo.TablePool/PureUdonGo.Table." +
                    tableIndex.ToString("000"));
                float expectedX = GoWorldGenerator.TableRowSpacingWorld *
                    (tableIndex - (GoBoardPool.INITIAL_VISIBLE_TABLES - 1) * 0.5f);
                Require(table != null && Mathf.Abs(table.localPosition.y) < 0.0001f &&
                    Mathf.Abs(table.localPosition.z) < 0.0001f &&
                    Mathf.Abs(table.localPosition.x - expectedX) < 0.0001f,
                    "table " + tableIndex + " is placed in the centered table row");
            }
        }

        private static bool viewMarkerIsOnPointPlane(GameObject root)
        {
            GoBoardView view = root == null ? null : root.GetComponentInChildren<GoBoardView>(true);
            return view != null && view.markerHeight > 0.24f && view.markerHeight < 0.30f &&
                view.hoverMarkerHeight > 0.086f && view.hoverMarkerHeight < 0.12f &&
                view.koMarkerHeight > 0.086f && view.koMarkerHeight < 0.12f &&
                view.hintMarker != null;
        }

        private static void ValidateRenderTextureContracts(GoGpuLayerExecutor executor)
        {
            if (executor == null)
                return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Fail("GPU RenderTexture validation requires a graphics device; do not run with -nographics.");
                return;
            }

            RenderTexture activation = null;
            RenderTexture vector = null;
            try
            {
                activation = executor.CreateActivation(128);
                vector = executor.CreateVector(80);
                Require(activation != null && activation.width == 19 && activation.height == 19 * 32,
                    "19x19 activation RenderTexture dimensions");
                Require(vector != null && vector.width == 20 && vector.height == 1,
                    "vector RenderTexture dimensions");
                if (activation != null)
                {
                    Require(activation.format == RenderTextureFormat.ARGBFloat,
                        "activation RenderTexture format is ARGBFloat");
                    Require(activation.filterMode == FilterMode.Point &&
                        activation.wrapMode == TextureWrapMode.Clamp && !activation.useMipMap &&
                        !activation.autoGenerateMips, "activation RenderTexture sampling contract");
                }
                if (vector != null)
                {
                    Require(vector.format == RenderTextureFormat.ARGBFloat &&
                        vector.filterMode == FilterMode.Point && vector.wrapMode == TextureWrapMode.Clamp &&
                        !vector.useMipMap && !vector.autoGenerateMips,
                        "vector RenderTexture sampling contract");
                }
            }
            finally
            {
                if (activation != null)
                    UnityEngine.Object.DestroyImmediate(activation);
                if (vector != null)
                    UnityEngine.Object.DestroyImmediate(vector);
            }
        }

        private static void ValidateUniqueGeneratedNames(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            int duplicateSuffixes = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name.EndsWith("(1)", StringComparison.Ordinal) ||
                    transforms[i].name.EndsWith("(2)", StringComparison.Ordinal))
                    duplicateSuffixes++;
            }
            Require(duplicateSuffixes == 0, "generated scene contains no numbered duplicate names");
        }

        private static string ReadRequiredAssetText(string assetPath)
        {
            return ReadRequiredProjectText(assetPath);
        }

        private static string ReadRequiredProjectText(string projectPath)
        {
            string resolvedProjectPath = ResolveProjectAssetPath(projectPath);
            string path = ProjectFile(resolvedProjectPath);
            if (!File.Exists(path))
            {
                Fail("Required project file is missing: " + projectPath);
                return string.Empty;
            }
            return File.ReadAllText(path);
        }

        private static bool HasFixedAnalysisTemplate(string value,bool english)
        {
            if(value==null)return false;
            string[] lines=value.Replace("\r","").Split('\n');
            if(lines.Length!=11)return false;
            string[] labels=english
                ?new string[]{"AI STATE  ","SEARCH  ","BEST MOVE  ","CANDIDATES  ",
                    "WINRATE  B ","SCORE LEAD W−B ","WHITE OWNERSHIP MEAN ","LEVEL  ",
                    "MOVE  ","STATIC CN SCORE W−B ","COMPUTE  "}
                :new string[]{"AI 状态  ","搜索  ","推荐着法  ","候选着法 ",
                    "胜率  黑 ","预计分差 白−黑 ","白方领地均值 ","档位  ",
                    "手数 ","折合子差 白−黑 ","计算端 "};
            for(int i=0;i<labels.Length;i++)
                if(!lines[i].StartsWith(labels[i],StringComparison.Ordinal))return false;
            if(english)
            {
                if(lines[9].IndexOf(" zi",StringComparison.Ordinal)<0)return false;
            }
            else if(lines[9].IndexOf(" 子",StringComparison.Ordinal)<0)return false;
            return true;
        }

        private static void ValidateControlActionSet(GoUI ui)
        {
            if(ui==null||ui.controlButtons==null||ui.controlButtonActions==null||
                ui.controlButtons.Length!=ui.controlButtonActions.Length)
            {
                Fail("control-wall action bindings are incomplete");
                return;
            }
            int[] required={0,1,2,3,4,5,6,10,11,12,13,22,23,30,31,33,34,35,
                38,39,40,43,44,45,48,49,50,51,52,53,54};
            for(int i=0;i<ui.controlButtonActions.Length;i++)
            {
                int action=ui.controlButtonActions[i];
                Require(action!=46&&action!=47,
                    "obsolete append/remove action is absent: "+action);
                for(int j=i+1;j<ui.controlButtonActions.Length;j++)
                    Require(ui.controlButtonActions[j]!=action,
                        "control action is unique: "+action);
            }
            for(int i=0;i<required.Length;i++)
            {
                bool found=false;
                for(int j=0;j<ui.controlButtonActions.Length;j++)
                    if(ui.controlButtonActions[j]==required[i]){found=true;break;}
                Require(found,"required control action exists: "+required[i]);
            }
        }

        private static string ResolveProjectAssetPath(string projectPath)
        {
            string normalized = projectPath.Replace('\\', '/').TrimStart('/');
            if (File.Exists(ProjectFile(normalized)))
                return normalized;

            // A package checkout may live below Assets/<package>/ while the
            // generated production namespace remains project-level
            // Assets/PureUdonGo. Source contracts must inspect the actual
            // imported package file instead of assuming the flat layout.
            string suffix = "/" + normalized;
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < assetPaths.Length; i++)
            {
                string candidate = assetPaths[i].Replace('\\', '/');
                if (candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return normalized;
        }

        private static void ValidateFileHash(string projectPath, string expected)
        {
            string path = ProjectFile(projectPath);
            if (!File.Exists(path))
            {
                Fail("Required model artifact is missing: " + projectPath);
                return;
            }
            string actual;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                actual = BytesToHex(sha.ComputeHash(stream));
            Require(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                "SHA-256 matches " + projectPath);
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static string ProjectFile(string path)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void ValidateSourceFile(string path, params string[] requiredTokens)
        {
            string source = ReadRequiredProjectText(path);
            for (int i = 0; i < requiredTokens.Length; i++)
                RequireText(source, requiredTokens[i], path + " contract");
        }

        private static void RequireText(string text, string token, string label)
        {
            Require(text != null && text.IndexOf(token, StringComparison.Ordinal) >= 0,
                label + " contains " + token);
        }

        private static void RequireNotText(string text, string token, string label)
        {
            Require(text == null || text.IndexOf(token, StringComparison.Ordinal) < 0,
                label + " does not contain " + token);
        }

        private static void Require(bool condition, string message)
        {
            if (condition)
                return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            failures.Add(message);
        }

        private static void Warn(string message)
        {
            warnings.Add(message);
        }

        private static void WriteValidationReport()
        {
            try
            {
                string path = ProjectFile(ValidationReportPath);
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                StringBuilder report = new StringBuilder();
                report.AppendLine("Pure Udon Go · Production Validation");
                report.AppendLine("====================================");
                report.AppendLine("Status: " + (failures.Count == 0 ? "PASS" : "FAIL"));
                report.AppendLine("Scene: " + GoWorldGenerator.ScenePath);
                report.AppendLine("Model: " + GoProductionModelLayout.ModelName +
                    " · v" + GoProductionModelLayout.ModelVersion);
                report.AppendLine("Failures: " + failures.Count);
                for (int i = 0; i < failures.Count; i++)
                    report.AppendLine("FAIL: " + failures[i]);
                report.AppendLine("Warnings: " + warnings.Count);
                for (int i = 0; i < warnings.Count; i++)
                    report.AppendLine("WARN: " + warnings[i]);
                report.AppendLine("Numerical equivalence, strength calibration and VRChat runtime are separate gates.");
                File.WriteAllText(path, report.ToString());
                AssetDatabase.ImportAsset(ValidationReportPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                Debug.LogError("PURE_UDON_GO_VALIDATOR_REPORT_FAIL " + exception);
            }
        }
    }
}
#endif
