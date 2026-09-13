#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.Core;
using VRC.SDK3.Components;
using VRC.Udon;

using Object = UnityEngine.Object;

namespace PureUdonGo.Editor
{
    /// <summary>
    /// One-click, idempotent production builder. The composition intentionally
    /// follows the Pure Udon Xiangqi control-wall/table language while every
    /// board object, label, action and engine setting remains Go-specific.
    /// </summary>
    public static class GoWorldGenerator
    {
        public const string MenuPath = "Tools/Pure Udon Go/Generate Final Production Scene";
        // Production always uses the single world-space UGUI board receiver.
        // The old 361-BoxCollider A/B path is intentionally gone: the board
        // has one explicit UIShape trigger collider (required by VRChat),
        // while stones, table top and table legs have no solid colliders.
        public const string ScenePath = "Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity";
        public const string ReaderRecoveryScenePath =
            "Assets/PureUdonGo/Generated/Scenes/PureUdonGoReaderRecovery.unity";
        public const string WeightPath = "Assets/PureUdonGo/Model/Generated/weights_rgba32f.asset";
        public const string ShaderName = "PureUdonGo/NNLayer";

        private const string GeneratedFolder = "Assets/PureUdonGo/Generated";
        private const string SceneFolder = GeneratedFolder + "/Scenes";
        private const string MaterialFolder = GeneratedFolder + "/Materials";
        private const string AudioFolder = GeneratedFolder + "/Audio";
        private const string TextureFolder = GeneratedFolder + "/Textures";
        private const string ReportPath = GeneratedFolder + "/GoGenerationReport.txt";

        // These are the reference family's proportions. Board-specific sizes
        // below are intentionally Go-native because 19x19 has more points than
        // Xiangqi's 9x10 board.
        private const float SceneWorldScale = 0.60f;
        // Keep the Xiangqi family table height, but lower the Go surface a
        // little so the 19x19 board reads as a low viewing table rather than
        // a floating display stand.
        private const float TableHeightScale = 0.76f;
        private const float BoardWorldScale = 0.26f;
        private const float BoardSpacing = 0.42f;
        private const float BoardHeight = 0.82f;
        // The source-family row spacing is 6.25 unscaled units under the
        // shared 60% world scale.  The three visible roots are centered in a
        // compact indoor room; the remaining serialized slots stay hidden.
        private const float TableRowSpacingUnscaled = 6.25f;
        public const float TableRowSpacingWorld = TableRowSpacingUnscaled * SceneWorldScale;
        private const float GalleryFloorUnscaledLength =
            TableRowSpacingUnscaled * (GoBoardPool.INITIAL_VISIBLE_TABLES - 1) + 6.00f;
        private const float GalleryFloorUnscaledDepth = 13.50f;
        public const float GalleryFloorLengthWorld = GalleryFloorUnscaledLength * SceneWorldScale;
        public const float GalleryFloorDepthWorld = GalleryFloorUnscaledDepth * SceneWorldScale;
        public const float RoomWallHeightWorld = 3.20f;
        private const float RoomWallThickness = 0.14f;
        private const float RoomCenterZ = 0.15f;
        private const float ReferenceCameraFarClip = 30f;
        // Unit-sphere source mesh, flattened into a deliberately small Go
        // stone.  Its bottom is exactly at the ink plane (0.086 local), so
        // the stone cannot visibly sink into or hover above the maple.
        private const float StoneWidth = 0.38f;
        private const float StoneHeight = 0.17f;
        private const float CaptureAnimationDuration = 0.22f;
        private const float CaptureSinkDistance = 0.018f;
        private const float FrontBackBoardOffset = 2.65f;
        // Keep the 19x19 ink lines hairline-thin like the supplied reference board;
        // parent scaling turns this into the final world-space width.
        private const float GridLineWidth = 0.0012f;
        private const float GridLineY = 0.086f;
        private const float StoneCenterY = GridLineY + StoneHeight * 0.5f;
        // Keep every point-oriented preview datum on the same board-local
        // plane. The marker is flush with the flattened stone crown rather
        // than visibly floating above it in oblique desktop/VR views.
        private const float BoardPointMarkerY = StoneCenterY + StoneHeight * 0.50f;
        private const float ColumnLabelDepthOffset = 0.14f;
        private const float BoardInteractionCenterY = GridLineY + 0.090f;
        private const float PanelWorldScale = 0.00170f;
        // Xiangqi's 0.80 baseline is correct for a 9x10 board. A 19x19 Go
        // board is materially deeper, so keep the same family composition but
        // reserve a real gap instead of allowing the wall to intersect the
        // rear board edge.
        private const float PanelBackOffset = 1.34f;
        private const float PanelLift = 0.14f;
        private const float BoardAudioRadius = 2.2f;

        private static readonly Color Background = new Color(0.052f, 0.056f, 0.062f, 0.995f);
        private static readonly Color Surface = new Color(0.092f, 0.098f, 0.108f, 1f);
        private static readonly Color SurfaceRaised = new Color(0.125f, 0.132f, 0.145f, 1f);
        private static readonly Color TextPrimary = new Color(0.940f, 0.940f, 0.950f, 1f);
        private static readonly Color TextSecondary = new Color(0.670f, 0.690f, 0.720f, 1f);
        private static readonly Color Accent = new Color(0.280f, 0.460f, 0.580f, 1f);
        private static readonly Color Danger = new Color(0.950f, 0.235f, 0.190f, 1f);
        private static readonly Color GoBlack = new Color(0.028f, 0.034f, 0.044f, 1f);
        private static readonly Color GoWhite = new Color(0.965f, 0.945f, 0.885f, 1f);
        private static readonly Color Walnut = new Color(0.155f, 0.095f, 0.058f, 1f);
        private static readonly Color Maple = new Color(0.840f, 0.675f, 0.485f, 1f);
        private static readonly Color GridInk = new Color(0.095f, 0.050f, 0.022f, 1f);
        private static readonly Color Brass = new Color(0.300f, 0.330f, 0.375f, 1f);
        private static readonly Color LastMove = new Color(1f, 0.58f, 0.10f, 1f);
        private static readonly Color Ko = new Color(0.95f, 0.15f, 0.10f, 1f);
        private static readonly Color Hint = new Color(0.08f, 0.72f, 0.58f, 1f);
        // The room palette is intentionally light and low-saturation: warm
        // ivory walls, a linen-grey contrast wall, and a soft white ceiling.
        // Avoid saturated aqua so the small room reads as a calm Go study.
        private static readonly Color RoomAmbient = new Color(0.82f, 0.81f, 0.77f, 1f);
        private static readonly Color WallpaperBeige = new Color(0.92f, 0.88f, 0.81f, 1f);
        // Keep the second wall a warm linen grey rather than cyan/blue.  It
        // still provides gentle contrast with the beige wall while preserving
        // the requested pale, calm indoor room palette.
        private static readonly Color WallpaperMist = new Color(0.86f, 0.85f, 0.81f, 1f);
        private static readonly Color WallpaperTrim = new Color(0.58f, 0.49f, 0.39f, 1f);
        private static readonly Color RoomCeiling = new Color(0.97f, 0.96f, 0.92f, 1f);

        private sealed class ButtonBinding
        {
            public Button button;
            public GoUiButton endpoint;
        }

        private sealed class LocalizedBinding
        {
            public TMP_Text text;
            public string chinese;
            public string english;
        }

        private static readonly List<ButtonBinding> bindings = new List<ButtonBinding>(512);
        private static readonly List<ButtonBinding> controlBindings = new List<ButtonBinding>(32);
        private static readonly List<LocalizedBinding> localizedBindings = new List<LocalizedBinding>(128);
        private static Sprite roundedSprite;
        private static bool waitingForTmpEssentials;

        [MenuItem(MenuPath)]
        public static void GenerateFinalProductionScene()
        {
            try
            {
                EnsureFolder(GeneratedFolder);
                EnsureFolder(SceneFolder);
                EnsureFolder(MaterialFolder);
                EnsureFolder(AudioFolder);
                EnsureFolder(TextureFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (!EnsureTmpEssentials())
                    return;

                Texture2D weights = AssetDatabase.LoadAssetAtPath<Texture2D>(WeightPath);
                if (weights == null)
                    throw new InvalidOperationException("Missing committed production model asset: " + WeightPath);
                Shader nnShader = Shader.Find(ShaderName);
                if (nnShader == null)
                    throw new InvalidOperationException("Missing production shader: " + ShaderName);
                TMP_FontAsset font = ResolveFont();
                roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

                Material floor = GetMaterial("Indoor Floor", new Color(0.64f, 0.56f, 0.47f), 0.04f, 0.32f, false);
                Material walnut = GetMaterial("Dark Walnut", Walnut, 0.12f, 0.68f, false);
                Material maple = GetMaterial("Warm Maple", Maple, 0.01f, 0.58f, false);
                Material grid = GetMaterial("Grid Ink", GridInk, 0f, 0.34f, false);
                Material brass = GetMaterial("Antique Brass", Brass, 0.34f, 0.72f, false);
                Material blackStone = GetMaterial("Go Black Stone", GoBlack, 0.02f, 0.74f, false);
                Material whiteStone = GetMaterial("Go White Stone", GoWhite, 0.01f, 0.78f, false);
                Material legal = GetMaterial("Legal Point", new Color(0.20f, 0.64f, 0.52f), 0.08f, 0.62f, true);
                Material selected = GetMaterial("Selected Point", new Color(0.78f, 0.54f, 0.20f), 0.16f, 0.72f, true);
                Material last = GetMaterial("Last Move", LastMove, 0.22f, 0.78f, true);
                Material ko = GetMaterial("Ko Marker", Ko, 0.18f, 0.78f, true);
                Material hint = GetMaterial("AI Hint", Hint, 0.18f, 0.82f, true);
                Material surface = GetMaterial("Graphite Surface", Surface, 0.16f, 0.55f, false);
                Material surfaceRaised = GetMaterial("Graphite Surface Raised", SurfaceRaised, 0.20f, 0.65f, false);
                Material background = GetMaterial("Gallery Background", Background, 0.02f, 0.35f, false);
                Material accent = GetMaterial("Control Accent", Accent, 0.12f, 0.70f, true);
                Material danger = GetMaterial("Control Danger", Danger, 0.05f, 0.55f, true);
                Texture2D mapleGrain = BuildWoodGrainTexture();
                Texture2D wallpaperTexture = BuildWallpaperTexture();
                Material wallpaperBeige = GetMaterial("Indoor Wallpaper Beige", WallpaperBeige, 0.0f, 0.28f, false);
                Material wallpaperMist = GetMaterial("Indoor Wallpaper Mist", WallpaperMist, 0.0f, 0.28f, false);
                Material wallpaperTrim = GetMaterial("Indoor Wallpaper Trim", WallpaperTrim, 0.05f, 0.34f, false);
                Material ceiling = GetMaterial("Indoor Ceiling Soft White", RoomCeiling, 0.0f, 0.22f, false);
                ApplyTexture(wallpaperBeige, wallpaperTexture, new Vector2(2.4f, 2.4f));
                ApplyTexture(wallpaperMist, wallpaperTexture, new Vector2(2.4f, 2.4f));
                ApplyTexture(ceiling, wallpaperTexture, new Vector2(2.0f, 2.0f));
                if (maple.HasProperty("_MainTex"))
                {
                    maple.SetTexture("_MainTex", mapleGrain);
                    maple.SetTextureScale("_MainTex", new Vector2(1.15f, 1.15f));
                    maple.SetColor("_Color", Color.white);
                    EditorUtility.SetDirty(maple);
                }
                Material nnMaterial = GetMaterial("PureUdonGoNNLayer", Color.white, 0f, 0f, false);
                nnMaterial.shader = nnShader;
                nnMaterial.SetTexture("_Weights", weights);
                EditorUtility.SetDirty(nnMaterial);

                AudioClip placeClip;
                AudioClip captureClip;
                AudioClip passClip;
                AudioClip gameEndClip;
                AudioClip resetClip;
                BuildAudioAssets(out placeClip, out captureClip, out passClip, out gameEndClip, out resetClip);

                EnsureUdonProgramAssets();
                bindings.Clear();
                controlBindings.Clear();
                localizedBindings.Clear();
                Scene scene = OpenOrCreateScene();
                GameObject root = EnsureOwnedRoot(scene);
                GoGeneratedWorld marker = EnsureUdon<GoGeneratedWorld>(root);
                marker.generatorId = "PureUdonGo.FinalProduction";
                marker.schemaVersion = GoGeneratedWorld.CURRENT_SCHEMA;

                BuildEnvironment(root, floor, background, brass, wallpaperBeige,
                    wallpaperMist, wallpaperTrim, ceiling);

                GameObject tablePoolRoot = CreateChild(root.transform, "PureUdonGo.TablePool");
                GoBoardPool boardPool = EnsureUdon<GoBoardPool>(root);
                GameObject[] tableRoots = new GameObject[GoBoardPool.MAX_TABLES];
                GoGame[] games = new GoGame[GoBoardPool.MAX_TABLES];
                GoBoardView[] views = new GoBoardView[GoBoardPool.MAX_TABLES];
                GoUI[] uis = new GoUI[GoBoardPool.MAX_TABLES];
                GoTelemetry[] telemetries = new GoTelemetry[GoBoardPool.MAX_TABLES];
                GoAiController[] ais = new GoAiController[GoBoardPool.MAX_TABLES];

                for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
                {
                    GameObject tableRoot = CreateChild(tablePoolRoot.transform,
                        "PureUdonGo.Table." + tableIndex.ToString("000"));
                    tableRoot.transform.localPosition = GetTablePosition(tableIndex);
                    tableRoots[tableIndex] = tableRoot;

                    GoGame tableGame;
                    GoBoardView tableView;
                    GoUI tableUI;
                    GoTelemetry tableTelemetry;
                    GoAiSettings tableSettings;
                    GoDifficultyProfile tableDifficulty;
                    GoDifficultyProfile tableBlackDifficulty;
                    GoAiController tableAi;
                    BuildRuntime(tableRoot, weights, nnMaterial, out tableGame, out tableView,
                        out tableUI, out tableTelemetry, out tableSettings, out tableDifficulty,
                        out tableBlackDifficulty, out tableAi);
                    GameObject tableBoard = BuildBoard(tableRoot, tableGame, tableView, tableUI,
                        maple, walnut, grid, brass, blackStone, whiteStone, legal, selected,
                        last, ko, hint);
                    int localizedStart = localizedBindings.Count;
                    int controlStart = controlBindings.Count;
                    BuildControlWall(tableRoot, tableBoard, tableUI, tableGame, tableTelemetry,
                        tableDifficulty, tableBlackDifficulty, tableAi, font, surface,
                        surfaceRaised, accent, danger, brass);
                    BuildBoardAudio(tableBoard, tableGame, placeClip, captureClip, passClip,
                        gameEndClip, resetClip);
                    AssignLocalizedText(tableUI, localizedStart);
                    AssignControlButtons(tableUI, controlStart);
                    WireRuntime(tableGame, tableView, tableUI, tableTelemetry,
                        tableSettings, tableDifficulty, tableBlackDifficulty, tableAi,
                        boardPool, tableIndex);

                    games[tableIndex] = tableGame;
                    views[tableIndex] = tableView;
                    uis[tableIndex] = tableUI;
                    telemetries[tableIndex] = tableTelemetry;
                    ais[tableIndex] = tableAi;
                    tableRoot.SetActive(tableIndex < GoBoardPool.INITIAL_VISIBLE_TABLES);
                }

                boardPool.tableRoots = tableRoots;
                boardPool.games = games;
                boardPool.views = views;
                boardPool.uis = uis;
                boardPool.controllers = ais;
                boardPool.profilingEnabled = false;
                boardPool.diagnosticsEnabled = false;
                boardPool.moveMaskWarmupMillisecondsPerFrame =
                    GoGame.MOVE_MASK_WARMUP_DEFAULT_MS;
                boardPool.primaryUI = uis[0];
                boardPool.visibleTableCount = GoBoardPool.INITIAL_VISIBLE_TABLES;
                boardPool.revision = 0;

                GoGame game = games[0];
                GoBoardView view = views[0];
                GoUI ui = uis[0];
                GoTelemetry telemetry = telemetries[0];
                GoDifficultyProfile difficulty = game.whiteDifficulty;
                GoDifficultyProfile blackDifficulty = game.blackDifficulty;
                GoAiController ai = ais[0];

                BindAllButtons();
                UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions
                {
                    IsEditorBuild = true,
                    ConcurrentBuild = false,
                    DisableLogging = false
                });
                SyncUdonProxies(root);
                DisableButtonInteractionHints();
                view.RefreshNow();
                ui.RefreshNow();
                telemetry.RefreshNow();
                if (string.IsNullOrEmpty(scene.path) &&
                    !EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity failed to establish generated scene path: " + ScenePath);
                VerifyGeneratedScene(scene, root, boardPool, game, view, ui, telemetry,
                    difficulty, blackDifficulty, ai);

                EditorUtility.SetDirty(root);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity failed to save generated scene: " + ScenePath);
                WriteGenerationReport(scene, root);
                Selection.activeGameObject = root;
                Debug.Log("PURE_UDON_GO_GENERATOR_PASS scene=" + ScenePath +
                    " root=" + root.name + " cells=" + GoGame.AREA +
                    " visibleTables=" + GoBoardPool.INITIAL_VISIBLE_TABLES +
                    " panel=3-screen ultraHardPresetVisits=" +
                    GoDifficultyProfile.ULTRAHARD_VISITS + " engineMaxVisits=" +
                    GoMctsSearch.MAX_SUPPORTED_VISITS);
            }
            catch (Exception exception)
            {
                Debug.LogError("PURE_UDON_GO_GENERATOR_FAIL " + exception);
                throw;
            }
        }

        /// <summary>
        /// Builds the smallest real Udon graph that can submit the production
        /// neural shader and exercise the A/B/C readback lifecycle.  Keeping
        /// this separate from the production room prevents ClientSim from
        /// serializing sixteen table graphs merely to test three reader IDs.
        /// </summary>
        public static void GenerateReaderRecoveryFixtureScene()
        {
            EnsureFolder(GeneratedFolder);
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Texture2D weights = AssetDatabase.LoadAssetAtPath<Texture2D>(WeightPath);
            if (weights == null)
                throw new InvalidOperationException(
                    "Missing committed production model asset: " + WeightPath);
            Shader nnShader = Shader.Find(ShaderName);
            if (nnShader == null)
                throw new InvalidOperationException("Missing production shader: " + ShaderName);
            Material nnMaterial = GetMaterial(
                "PureUdonGoNNLayer", Color.white, 0f, 0f, false);
            nnMaterial.shader = nnShader;
            nnMaterial.SetTexture("_Weights", weights);
            EditorUtility.SetDirty(nnMaterial);

            EnsureUdonProgramAssets();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("PureUdonGo.ReaderRecoveryFixture");
            SceneManager.MoveGameObjectToScene(root, scene);

            VRCSceneDescriptor descriptor = root.AddComponent<VRCSceneDescriptor>();
            GameObject spawn = CreateChild(root.transform, "Reader Fixture Spawn");
            spawn.transform.localPosition = new Vector3(0f, 1.1f, -1.5f);
            descriptor.spawns = new[] { spawn.transform };

            GoGame game;
            GoAiController controller;
            BuildReaderRecoveryRuntime(root, weights, nnMaterial, out game, out controller);

            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions
            {
                IsEditorBuild = true,
                ConcurrentBuild = false,
                DisableLogging = false
            });
            SyncUdonProxies(root);
            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ReaderRecoveryScenePath))
                throw new InvalidOperationException(
                    "Unity failed to save reader recovery fixture: " +
                    ReaderRecoveryScenePath);

            if (root.GetComponentsInChildren<GoGame>(true).Length != 1 ||
                root.GetComponentsInChildren<GoAiController>(true).Length != 1 ||
                root.GetComponentsInChildren<GoGpuNeuralRuntime>(true).Length != 1 ||
                root.GetComponentsInChildren<GoGpuNeuralOutputReader>(true).Length != 3 ||
                root.GetComponentsInChildren<GoBoardPool>(true).Length != 0 ||
                root.GetComponentsInChildren<GoBoardCell>(true).Length != 0 ||
                root.GetComponentsInChildren<Canvas>(true).Length != 0)
                throw new InvalidOperationException(
                    "Reader recovery fixture contains non-minimal production-room objects");

            Debug.Log("PURE_UDON_GO_READER_FIXTURE_PASS scene=" +
                ReaderRecoveryScenePath + " games=1 controllers=1 runtimes=1 readers=3" +
                " boardPools=0 boardCells=0 canvases=0");
        }

        private static void BuildReaderRecoveryRuntime(GameObject root,
            Texture2D weights, Material nnMaterial, out GoGame game,
            out GoAiController ai)
        {
            GameObject gameplay = CreateChild(root.transform,
                "PureUdonGo.ReaderRecoveryGameplay");
            game = EnsureUdon<GoGame>(gameplay);
            GoDifficultyProfile whiteDifficulty =
                EnsureUdon<GoDifficultyProfile>(gameplay);
            GameObject blackProfileObject = CreateChild(gameplay.transform,
                "Black AI Profile");
            GoDifficultyProfile blackDifficulty =
                EnsureUdon<GoDifficultyProfile>(blackProfileObject);
            GameObject settingsObject = CreateChild(gameplay.transform,
                "AI Settings · Shared Sync Domain");
            GoAiSettings settings = EnsureUdon<GoAiSettings>(settingsObject);
            GoFeatureEncoder encoder = EnsureUdon<GoFeatureEncoder>(gameplay);
            GoSearchState state = EnsureUdon<GoSearchState>(gameplay);
            GoMctsSearch search = EnsureUdon<GoMctsSearch>(gameplay);
            GoGpuLayerExecutor executor = EnsureUdon<GoGpuLayerExecutor>(gameplay);
            GoGpuNeuralRuntime runtime = EnsureUdon<GoGpuNeuralRuntime>(gameplay);
            GoGpuNeuralOutputReader readerA =
                EnsureUdon<GoGpuNeuralOutputReader>(gameplay);
            GameObject readerBObject = CreateChild(gameplay.transform,
                "Go GPU Readback B · stale callback quarantine");
            GoGpuNeuralOutputReader readerB =
                EnsureUdon<GoGpuNeuralOutputReader>(readerBObject);
            GameObject readerCObject = CreateChild(gameplay.transform,
                "Go GPU Readback C · stale callback quarantine");
            GoGpuNeuralOutputReader readerC =
                EnsureUdon<GoGpuNeuralOutputReader>(readerCObject);
            ai = EnsureUdon<GoAiController>(gameplay);

            game.positionalSuperko = true;
            game.areaScoring = true;
            game.multiStoneSuicideLegal = true;
            game.komiTimes2 = 15;
            game.blackIsAI = false;
            game.whiteIsAI = true;
            game.matchStarted = false;
            game.starter = GoGame.BLACK;
            game.humanSide = GoGame.BLACK;
            game.aiSettings = settings;
            game.blackDifficulty = blackDifficulty;
            game.whiteDifficulty = whiteDifficulty;
            game.aiController = ai;

            executor.layerMaterial = nnMaterial;
            executor.weightTexture = weights;
            executor.weightWidth = GoProductionModelLayout.WeightWidth;
            executor.weightHeight = GoProductionModelLayout.WeightHeight;
            runtime.executor = executor;
            runtime.featureEncoder = encoder;
            runtime.useEquivalentFusion = true;
            readerA.runtime = runtime;
            readerB.runtime = runtime;
            readerC.runtime = runtime;
            readerA.eventReceiver =
                UdonSharpEditorUtility.GetBackingUdonBehaviour(readerA);
            readerB.eventReceiver =
                UdonSharpEditorUtility.GetBackingUdonBehaviour(readerB);
            readerC.eventReceiver =
                UdonSharpEditorUtility.GetBackingUdonBehaviour(readerC);
            search.simulationState = state;
            search.authoritativeGame = game;

            settings.sharedPreset = GoDifficultyProfile.BEGINNER;
            settings.blackUseCustom = false;
            settings.blackCustomVisits = GoDifficultyProfile.BEGINNER_VISITS;
            settings.whiteUseCustom = false;
            settings.whiteCustomVisits = GoDifficultyProfile.BEGINNER_VISITS;
            settings.configRevision = 0;
            settings.game = game;
            settings.blackDifficulty = blackDifficulty;
            settings.whiteDifficulty = whiteDifficulty;
            settings.aiController = ai;

            whiteDifficulty.settings = settings;
            whiteDifficulty.settingsSide = GoGame.WHITE;
            whiteDifficulty.settingsMirror = true;
            whiteDifficulty.aiController = ai;
            blackDifficulty.settings = settings;
            blackDifficulty.settingsSide = GoGame.BLACK;
            blackDifficulty.settingsMirror = true;
            blackDifficulty.aiController = ai;
            settings.RegisterProfiles(blackDifficulty, whiteDifficulty);

            ai.game = game;
            ai.aiSettings = settings;
            ai.difficulty = whiteDifficulty;
            ai.blackDifficulty = blackDifficulty;
            ai.whiteDifficulty = whiteDifficulty;
            ai.encoder = encoder;
            ai.runtime = runtime;
            ai.reader = readerA;
            ai.alternateReader = readerB;
            ai.tertiaryReader = readerC;
            ai.readerA = readerA;
            ai.readerB = readerB;
            ai.readerC = readerC;
            ai.activeReaderIndex = 0;
            ai.search = search;
            ai.mode = GoAiController.MODE_PVAI;
            ai.aiColor = GoGame.WHITE;
            ai.autoStart = true;
        }

        private static Scene OpenOrCreateScene()
        {
            if (File.Exists(ScenePath))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static GameObject EnsureOwnedRoot(Scene scene)
        {
            GameObject root = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GoGeneratedWorld marker = roots[i].GetComponent<GoGeneratedWorld>();
                if (marker == null)
                    continue;
                if (root == null)
                    root = roots[i];
                else
                    Object.DestroyImmediate(roots[i]);
            }
            if (root == null)
            {
                root = new GameObject("PureUdonGo.ProductionRoot");
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            root.name = "PureUdonGo.ProductionRoot";
            ClearChildren(root.transform);
            return root;
        }

        private static void ClearChildren(Transform parent)
        {
            while (parent.childCount > 0)
                Object.DestroyImmediate(parent.GetChild(0).gameObject);
        }

        private static void BuildEnvironment(GameObject root, Material floor, Material background,
            Material brass, Material wallpaperBeige, Material wallpaperMist,
            Material wallpaperTrim, Material ceiling)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = RoomAmbient;
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.fog = false;

            PipelineManager pipeline = root.GetComponent<PipelineManager>();
            if (pipeline == null)
                pipeline = root.AddComponent<PipelineManager>();

            VRCSceneDescriptor descriptor = root.GetComponent<VRCSceneDescriptor>();
            if (descriptor == null)
                descriptor = root.AddComponent<VRCSceneDescriptor>();
            descriptor.RespawnHeightY = -2f;

            GameObject environment = CreateChild(root.transform, "PureUdonGo.Environment · Indoor Room");
            GameObject floorObject = CreatePrimitive(environment.transform,
                "Indoor Room Floor · Walkable", PrimitiveType.Plane,
                new Vector3(0f, 0f, RoomCenterZ),
                new Vector3(GalleryFloorUnscaledLength * 0.1f, 1f, GalleryFloorUnscaledDepth * 0.1f) * SceneWorldScale,
                Quaternion.identity, floor, true);
            floorObject.isStatic = true;

            float roomWidth = GalleryFloorLengthWorld;
            float roomDepth = GalleryFloorDepthWorld;
            float halfWidth = roomWidth * 0.5f;
            float halfDepth = roomDepth * 0.5f;
            float wallY = RoomWallHeightWorld * 0.5f;
            CreatePrimitive(environment.transform, "Indoor Back Wall · Mist Wallpaper", PrimitiveType.Cube,
                new Vector3(0f, wallY, RoomCenterZ + halfDepth),
                new Vector3(roomWidth, RoomWallHeightWorld, RoomWallThickness),
                Quaternion.identity, wallpaperMist, true);
            CreatePrimitive(environment.transform, "Indoor Front Wall · Beige Wallpaper", PrimitiveType.Cube,
                new Vector3(0f, wallY, RoomCenterZ - halfDepth),
                new Vector3(roomWidth, RoomWallHeightWorld, RoomWallThickness),
                Quaternion.identity, wallpaperBeige, true);
            CreatePrimitive(environment.transform, "Indoor Left Wall · Beige Wallpaper", PrimitiveType.Cube,
                new Vector3(-halfWidth, wallY, RoomCenterZ),
                new Vector3(RoomWallThickness, RoomWallHeightWorld, roomDepth),
                Quaternion.identity, wallpaperBeige, true);
            CreatePrimitive(environment.transform, "Indoor Right Wall · Mist Wallpaper", PrimitiveType.Cube,
                new Vector3(halfWidth, wallY, RoomCenterZ),
                new Vector3(RoomWallThickness, RoomWallHeightWorld, roomDepth),
                Quaternion.identity, wallpaperMist, true);
            CreatePrimitive(environment.transform, "Indoor Ceiling · Soft White", PrimitiveType.Cube,
                new Vector3(0f, RoomWallHeightWorld, RoomCenterZ),
                new Vector3(roomWidth, RoomWallThickness, roomDepth),
                Quaternion.identity, ceiling, true);
            CreatePrimitive(environment.transform, "Indoor Room Baseboard · Warm Trim", PrimitiveType.Cube,
                new Vector3(0f, 0.16f, RoomCenterZ + halfDepth - RoomWallThickness * 0.6f),
                new Vector3(roomWidth, 0.24f, 0.08f),
                Quaternion.identity, wallpaperTrim, false);
            CreatePrimitive(environment.transform, "Central Orientation Runner", PrimitiveType.Cube,
                new Vector3(0f, 0.008f, RoomCenterZ),
                new Vector3(0.72f, 0.010f, 0.018f),
                Quaternion.identity, brass, false);

            GameObject lightObject = CreateChild(environment.transform, "Neutral Gallery Key");
            lightObject.transform.localRotation = Quaternion.Euler(46f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 0.82f;
            light.shadows = LightShadows.None;
            light.bounceIntensity = 0f;

            GameObject fillObject = CreateChild(environment.transform, "Soft Maple Fill");
            fillObject.transform.localRotation = Quaternion.Euler(28f, 150f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.88f, 0.86f, 0.80f, 1f);
            fill.intensity = 0.22f;
            fill.shadows = LightShadows.None;
            fill.bounceIntensity = 0f;

            GameObject cameraObject = CreateChild(environment.transform, "PureUdonGo.ProductionCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, RoomCenterZ - halfDepth + 1.20f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.76f, RoomCenterZ + 1.55f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.86f, 0.85f, 0.80f, 1f);
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = ReferenceCameraFarClip;
            camera.fieldOfView = 56f;
            cameraObject.AddComponent<AudioListener>();

            GameObject spawnObject = CreateChild(environment.transform,
                "Player Spawn · Go Table");
            spawnObject.transform.localPosition = new Vector3(0f, 0.11f, RoomCenterZ - halfDepth + 1.10f);
            spawnObject.transform.localRotation = Quaternion.identity;
            descriptor.ReferenceCamera = cameraObject;
            descriptor.spawns = new[] { spawnObject.transform };

            GameObject eventSystem = CreateChild(root.transform, "EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static void BuildRuntime(GameObject root, Texture2D weights, Material nnMaterial,
            out GoGame game, out GoBoardView view, out GoUI ui, out GoTelemetry telemetry,
            out GoAiSettings settings, out GoDifficultyProfile difficulty,
            out GoDifficultyProfile blackDifficulty, out GoAiController ai)
        {
            GameObject gameplay = CreateChild(root.transform, "PureUdonGo.Gameplay");
            GameObject boardObject = CreateChild(root.transform, "PureUdonGo.Board");
            GameObject uiObject = CreateChild(root.transform, "PureUdonGo.UI");
            GameObject telemetryObject = CreateChild(root.transform, "PureUdonGo.Telemetry");

            game = EnsureUdon<GoGame>(gameplay);
            view = EnsureUdon<GoBoardView>(boardObject);
            ui = EnsureUdon<GoUI>(uiObject);
            telemetry = EnsureUdon<GoTelemetry>(telemetryObject);
            GameObject settingsObject = CreateChild(gameplay.transform, "AI Settings · Shared Sync Domain");
            settings = EnsureUdon<GoAiSettings>(settingsObject);
            difficulty = EnsureUdon<GoDifficultyProfile>(gameplay);
            GameObject blackProfileObject = CreateChild(gameplay.transform, "Black AI Profile");
            blackDifficulty = EnsureUdon<GoDifficultyProfile>(blackProfileObject);
            GoFeatureEncoder encoder = EnsureUdon<GoFeatureEncoder>(gameplay);
            GoSearchState state = EnsureUdon<GoSearchState>(gameplay);
            GoMctsSearch search = EnsureUdon<GoMctsSearch>(gameplay);
            GoGpuLayerExecutor executor = EnsureUdon<GoGpuLayerExecutor>(gameplay);
            GoGpuNeuralRuntime runtime = EnsureUdon<GoGpuNeuralRuntime>(gameplay);
            GoGpuNeuralOutputReader reader = EnsureUdon<GoGpuNeuralOutputReader>(gameplay);
            GameObject alternateReaderObject = CreateChild(gameplay.transform,
                "Go GPU Readback B · stale callback quarantine");
            GoGpuNeuralOutputReader alternateReader =
                EnsureUdon<GoGpuNeuralOutputReader>(alternateReaderObject);
            GameObject tertiaryReaderObject = CreateChild(gameplay.transform,
                "Go GPU Readback C · stale callback quarantine");
            GoGpuNeuralOutputReader tertiaryReader =
                EnsureUdon<GoGpuNeuralOutputReader>(tertiaryReaderObject);
            ai = EnsureUdon<GoAiController>(gameplay);

            game.positionalSuperko = true;
            game.areaScoring = true;
            game.multiStoneSuicideLegal = true;
            game.komiTimes2 = 15;
            executor.layerMaterial = nnMaterial;
            executor.weightTexture = weights;
            executor.weightWidth = GoProductionModelLayout.WeightWidth;
            executor.weightHeight = GoProductionModelLayout.WeightHeight;
            runtime.executor = executor;
            runtime.featureEncoder = encoder;
            reader.runtime = runtime;
            reader.eventReceiver = UdonSharpEditorUtility.GetBackingUdonBehaviour(reader);
            alternateReader.runtime = runtime;
            alternateReader.eventReceiver = UdonSharpEditorUtility.GetBackingUdonBehaviour(alternateReader);
            tertiaryReader.runtime = runtime;
            tertiaryReader.eventReceiver = UdonSharpEditorUtility.GetBackingUdonBehaviour(tertiaryReader);
            search.simulationState = state;
            search.authoritativeGame = game;
            ai.encoder = encoder;
            ai.runtime = runtime;
            ai.reader = reader;
            ai.alternateReader = alternateReader;
            ai.tertiaryReader = tertiaryReader;
            ai.readerA = reader;
            ai.readerB = alternateReader;
            ai.readerC = tertiaryReader;
            ai.activeReaderIndex = 0;
            ai.search = search;
            ai.mode = GoAiController.MODE_PVAI;
            ai.aiColor = GoGame.WHITE;
            ai.autoStart = true;

            settings.sharedPreset = GoDifficultyProfile.BEGINNER;
            settings.blackUseCustom = false;
            settings.blackCustomVisits = GoDifficultyProfile.BEGINNER_VISITS;
            settings.whiteUseCustom = false;
            settings.whiteCustomVisits = GoDifficultyProfile.BEGINNER_VISITS;
            settings.configRevision = 0;

            game.blackIsAI = false;
            game.whiteIsAI = true;
            game.matchStarted = false;
            game.starter = GoGame.BLACK;
            game.humanSide = GoGame.BLACK;

            settings.game = game;
            settings.blackDifficulty = blackDifficulty;
            settings.whiteDifficulty = difficulty;
            settings.aiController = ai;
            difficulty.settings = settings;
            difficulty.settingsSide = GoGame.WHITE;
            difficulty.settingsMirror = true;
            blackDifficulty.settings = settings;
            blackDifficulty.settingsSide = GoGame.BLACK;
            blackDifficulty.settingsMirror = true;
            settings.RegisterProfiles(blackDifficulty, difficulty);
        }

        private static void WireRuntime(GoGame game, GoBoardView view, GoUI ui,
            GoTelemetry telemetry, GoAiSettings settings,
            GoDifficultyProfile difficulty, GoDifficultyProfile blackDifficulty,
            GoAiController ai,
            GoBoardPool boardPool, int tableIndex)
        {
            game.view = view;
            view.captureAnimationDuration = CaptureAnimationDuration;
            view.captureSinkDistance = CaptureSinkDistance;
            view.moveMaskWarmupManagedByPool = true;
            game.ui = ui;
            game.telemetry = telemetry;
            game.aiController = ai;
            game.aiSettings = settings;
            game.blackDifficulty = blackDifficulty;
            game.whiteDifficulty = difficulty;

            ui.game = game;
            ui.aiSettings = settings;
            ui.difficulty = difficulty;
            ui.blackDifficulty = blackDifficulty;
            ui.whiteDifficulty = difficulty;
            ui.aiController = ai;
            ui.telemetry = telemetry;

            telemetry.game = game;
            telemetry.difficulty = difficulty;
            telemetry.aiController = ai;
            telemetry.ui = ui;
            telemetry.panelText = ui.panelTelemetryText;
            telemetry.showDeveloperDiagnostics = false;
            telemetry.showAdvancedAnalysis = false;
            ui.advancedAnalysisEnabled = false;

            difficulty.aiController = ai;
            blackDifficulty.aiController = ai;
            settings.game = game;
            settings.ui = ui;
            settings.aiController = ai;
            settings.RegisterProfiles(blackDifficulty, difficulty);

            ai.game = game;
            ai.boardPool = boardPool;
            game.boardPool = boardPool;
            game.tableIndex = tableIndex;
            ai.aiSettings = settings;
            ai.difficulty = difficulty;
            ai.blackDifficulty = blackDifficulty;
            ai.whiteDifficulty = difficulty;
            ai.view = view;
            ai.ui = ui;
            ai.telemetry = telemetry;

            view.game = game;
        }

        private static GameObject BuildBoard(GameObject root, GoGame game, GoBoardView view,
            GoUI ui, Material maple, Material walnut, Material grid, Material brass,
            Material blackStone, Material whiteStone, Material legal, Material selected,
            Material lastMove, Material ko, Material hint)
        {
            GameObject board = root.transform.Find("PureUdonGo.Board").gameObject;
            StripBoardPhysicsColliders(board.transform);
            board.transform.localPosition = new Vector3(0f,
                BoardHeight * SceneWorldScale * TableHeightScale,
                FrontBackBoardOffset * SceneWorldScale);
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = Vector3.one * SceneWorldScale;
            BuildBoardPedestal(board.transform, walnut, brass);

            Transform origin = CreateChild(board.transform, "Board Origin · 19 × 19").transform;
            origin.localScale = Vector3.one * BoardWorldScale;
            BuildPhysicalBoard(origin, grid, maple, walnut, brass);

            Transform stones = CreateChild(origin, "Stone Pool · 361 Intersections").transform;
            BuildCentralBoardInput(origin,game,view);
            Transform overlays = CreateChild(origin, "Go Overlays · Legal Ko Last Hint").transform;
            view.game = game;
            view.gridSpacing = BoardSpacing;
            view.markerHeight = BoardPointMarkerY;
            view.hoverMarkerHeight = GridLineY + 0.010f;
            view.koMarkerHeight = GridLineY + 0.010f;
            view.blackStoneMaterial = blackStone;
            view.whiteStoneMaterial = whiteStone;
            view.legalHoverMaterial = legal;
            view.illegalHoverMaterial = ko;
            view.stoneObjects = new GameObject[GoGame.AREA];
            view.stoneRenderers = new Renderer[GoGame.AREA];

            Mesh pebbleMesh = BuildPebbleStoneMesh();
            for (int loc = 0; loc < GoGame.AREA; loc++)
            {
                Vector3 point = LocationToLocal(loc, StoneCenterY);
                GameObject stone = CreatePebbleStone(stones, "Stone " + loc.ToString("000"),
                    point, pebbleMesh, blackStone);
                stone.SetActive(false);
                view.stoneObjects[loc] = stone;
                view.stoneRenderers[loc] = stone.GetComponent<Renderer>();

            }

            GameObject preview = CreatePebbleStone(overlays,
                "Preview Stone · Pooled Teleport", new Vector3(0f, -0.30f, 0f),
                pebbleMesh, blackStone);
            preview.SetActive(false);
            Renderer previewRenderer = preview.GetComponent<Renderer>();
            if (previewRenderer != null)
            {
                previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
                previewRenderer.receiveShadows = false;
            }
            view.previewStone = preview;
            view.previewStoneRenderer = previewRenderer;
            view.previewStoneCenterY = StoneCenterY;
            view.previewStoragePosition = new Vector3(0f, -0.30f, 0f);

            GameObject last = CreatePrimitive(overlays, "Last Move · Point", PrimitiveType.Cylinder,
                Vector3.zero, new Vector3(0.13f, 0.005f, 0.13f),
                Quaternion.identity, lastMove, false);
            GameObject koMarker = CreatePrimitive(overlays, "Ko · Forbidden Point", PrimitiveType.Cylinder,
                Vector3.zero, new Vector3(0.18f, 0.006f, 0.18f),
                Quaternion.identity, ko, false);
            GameObject hover = CreatePrimitive(overlays, "Hover · Legal Point", PrimitiveType.Cylinder,
                Vector3.zero, new Vector3(0.29f, 0.005f, 0.29f),
                Quaternion.identity, legal, false);
            GameObject hintMarker = CreatePrimitive(overlays, "AI Hint · Policy Point", PrimitiveType.Cylinder,
                Vector3.zero, new Vector3(0.24f, 0.005f, 0.24f),
                Quaternion.identity, hint, false);
            last.SetActive(false);
            koMarker.SetActive(false);
            hover.SetActive(false);
            hintMarker.SetActive(false);
            DisableMarkerShadows(last);
            DisableMarkerShadows(koMarker);
            DisableMarkerShadows(hover);
            DisableMarkerShadows(hintMarker);
            view.lastMoveMarker = last;
            view.koMarker = koMarker;
            view.hoverMarker = hover;
            view.hintMarker = hintMarker;
            view.hoverRenderer = hover.GetComponent<Renderer>();

            // Keep this assertion next to construction so a future primitive
            // or prefab change cannot silently put a solid collider back on
            // Board Origin, the grid, stones, overlays or table pedestal. The
            // one allowed collider is the UIShape trigger on the board input
            // Canvas itself.
            BoxCollider boardInputCollider = view.inputReceiver == null ? null :
                view.inputReceiver.GetComponent<BoxCollider>();
            Collider[] boardColliders = board.GetComponentsInChildren<Collider>(true);
            if (boardColliders.Length != 1 || boardColliders[0] != boardInputCollider ||
                boardInputCollider == null || !boardInputCollider.enabled || !boardInputCollider.isTrigger)
                throw new InvalidOperationException(
                    "Generated Go board hierarchy must contain only the UIShape trigger collider.");

            return board;
        }

        private static void StripBoardPhysicsColliders(Transform board)
        {
            if (board == null)
                return;
            // A regenerated scene can contain an older manually-authored
            // collider on Board Origin. Remove only colliders below the board;
            // the room and the separate status-panel collider are untouched.
            Collider[] stale = board.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < stale.Length; i++)
                if (stale[i] != null)
                    Object.DestroyImmediate(stale[i]);
        }

        private static void BuildCentralBoardInput(Transform origin,GoGame game,GoBoardView view)
        {
            GameObject go=new GameObject("Board Interaction Canvas · 361 Intersections",
                typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),
                typeof(GraphicRaycaster),typeof(VRCUiShape));
            go.transform.SetParent(origin,false);
            RectTransform rect=go.GetComponent<RectTransform>();
            rect.sizeDelta=new Vector2(1900f,1900f);
            rect.localPosition=new Vector3(0f,BoardInteractionCenterY,0f);
            // Same upward-facing world-space UI convention as Xiangqi.
            rect.localRotation=Quaternion.Euler(90f,0f,0f);
            rect.localScale=Vector3.one*(BoardSpacing/100f);
            Canvas canvas=go.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit=2f;
            AddBoardInputCollider(go,rect.sizeDelta);
            GoBoardInput receiver=EnsureUdon<GoBoardInput>(go);
            receiver.game=game;receiver.view=view;view.inputReceiver=receiver;
            receiver.DisableInteractive=true;
            UdonBehaviour backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
            if(backing!=null)backing.DisableInteractive=true;
            for(int loc=0;loc<GoGame.AREA;loc++)
            {
                GameObject hit=new GameObject("Intersection "+loc.ToString("000"),
                    typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button),typeof(EventTrigger));
                hit.transform.SetParent(rect,false);
                RectTransform point=hit.GetComponent<RectTransform>();
                point.anchorMin=point.anchorMax=new Vector2(0.5f,0.5f);
                point.sizeDelta=new Vector2(82f,82f);
                point.anchoredPosition=new Vector2((loc%19-9)*100f,(loc/19-9)*100f);
                Image image=hit.GetComponent<Image>();
                image.color=new Color(1f,1f,1f,0.001f);image.raycastTarget=true;
                Button button=hit.GetComponent<Button>();button.targetGraphic=image;
                button.transition=Selectable.Transition.None;
                button.navigation=new Navigation{mode=Navigation.Mode.None};
                UnityEventTools.AddStringPersistentListener(button.onClick,backing.SendCustomEvent,"Click"+loc.ToString("000"));
                EventTrigger trigger=hit.GetComponent<EventTrigger>();
                var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};
                UnityEventTools.AddStringPersistentListener(enter.callback,backing.SendCustomEvent,"Hover"+loc.ToString("000"));
                trigger.triggers.Add(enter);
                var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};
                UnityEventTools.AddStringPersistentListener(exit.callback,backing.SendCustomEvent,"Exit"+loc.ToString("000"));
                trigger.triggers.Add(exit);
            }
        }

        private static void AddBoardInputCollider(GameObject canvasObject, Vector2 canvasSize)
        {
            // VRCUiShape requires a collider to define its UI hit surface. An
            // explicit trigger prevents VRChat from auto-creating a solid
            // collider after upload, so players can walk through the board
            // while the laser/mouse still reaches the UGUI targets.
            BoxCollider collider = canvasObject.GetComponent<BoxCollider>();
            if (collider == null)
                collider = canvasObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(canvasSize.x, canvasSize.y, 0.020f);
            collider.isTrigger = true;
            collider.enabled = true;
        }

        private static void BuildPhysicalBoard(Transform origin, Material grid, Material maple,
            Material walnut, Material brass)
        {
            CreatePrimitive(origin, "Walnut Base", PrimitiveType.Cube,
                new Vector3(0f, -0.065f, 0f), new Vector3(8.35f, 0.22f, 8.35f),
                Quaternion.identity, walnut, false);
            CreatePrimitive(origin, "Maple Playing Surface", PrimitiveType.Cube,
                new Vector3(0f, 0.045f, 0f), new Vector3(8.08f, 0.075f, 8.08f),
                Quaternion.identity, maple, false);
            float min = -9f * BoardSpacing;
            float max = 9f * BoardSpacing;
            for (int i = 0; i < GoGame.SIZE; i++)
            {
                float coordinate = (i - 9) * BoardSpacing;
                CreateLine(origin, "Grid · File " + i.ToString("00"),
                    new Vector3(coordinate, GridLineY, min),
                    new Vector3(coordinate, GridLineY, max), GridLineWidth, grid);
                CreateLine(origin, "Grid · Rank " + i.ToString("00"),
                    new Vector3(min, GridLineY, coordinate),
                    new Vector3(max, GridLineY, coordinate), GridLineWidth, grid);
            }

            int[] stars = { 3, 9, 15 };
            for (int x = 0; x < stars.Length; x++)
            {
                for (int y = 0; y < stars.Length; y++)
                {
                    int loc = stars[y] * GoGame.SIZE + stars[x];
                    Vector3 point = LocationToLocal(loc, 0.089f);
                    CreatePrimitive(origin, "Star Point · " + stars[x] + "·" + stars[y],
                        PrimitiveType.Cylinder, point, new Vector3(0.11f, 0.006f, 0.11f),
                        Quaternion.identity, grid, false);
                }
            }

            string[] columnLabels = { "A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T" };
            for (int i = 0; i < columnLabels.Length; i++)
            {
                float coordinate = (i - 9) * BoardSpacing;
                CreateWorldText(origin, "Coordinate " + columnLabels[i], columnLabels[i],
                    new Vector3(coordinate, 0.105f, max + ColumnLabelDepthOffset), Quaternion.Euler(90f, 0f, 0f),
                    Vector3.one * 0.070f, 22f, FontStyles.Bold, grid.color);
            }
        }

        private static Mesh BuildPebbleStoneMesh()
        {
            // Older generator revisions emitted a standalone pebble mesh at
            // this path. Keep any legacy asset untouched: production
            // generation must be non-destructive and simply use the current
            // generated stone objects/materials.
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(template);
            if (mesh == null)
                throw new InvalidOperationException("Unity rounded pebble mesh could not be created.");
            return mesh;
        }

        private static GameObject CreatePebbleStone(Transform parent, string name,
            Vector3 position, Mesh mesh, Material material)
        {
            GameObject go = CreateChild(parent, name);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(StoneWidth, StoneHeight, StoneWidth);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static void BuildBoardPedestal(Transform root, Material walnut, Material brass)
        {
            CreatePrimitive(root, "Minimal Table · Undertray", PrimitiveType.Cube,
                new Vector3(0f, -0.075f * TableHeightScale, 0f),
                new Vector3(1.54f, 0.070f * TableHeightScale, 1.54f),
                Quaternion.identity, walnut, false);
            CreatePrimitive(root, "Minimal Table · Left Leg", PrimitiveType.Cube,
                new Vector3(-0.54f, -0.445f * TableHeightScale, 0f),
                new Vector3(0.080f, 0.72f * TableHeightScale, 0.60f),
                Quaternion.identity, walnut, false);
            CreatePrimitive(root, "Minimal Table · Right Leg", PrimitiveType.Cube,
                new Vector3(0.54f, -0.445f * TableHeightScale, 0f),
                new Vector3(0.080f, 0.72f * TableHeightScale, 0.60f),
                Quaternion.identity, walnut, false);
            CreatePrimitive(root, "Minimal Table · Left Foot", PrimitiveType.Cube,
                new Vector3(-0.54f, -0.795f * TableHeightScale, 0f),
                new Vector3(0.30f, 0.035f * TableHeightScale, 0.68f),
                Quaternion.identity, brass, false);
            CreatePrimitive(root, "Minimal Table · Right Foot", PrimitiveType.Cube,
                new Vector3(0.54f, -0.795f * TableHeightScale, 0f),
                new Vector3(0.30f, 0.035f * TableHeightScale, 0.68f),
                Quaternion.identity, brass, false);
        }

        private static void BuildControlWall(GameObject root, GameObject board, GoUI ui, GoGame game,
            GoTelemetry telemetry, GoDifficultyProfile difficulty, GoDifficultyProfile blackDifficulty,
            GoAiController ai,
            TMP_FontAsset font, Material surface, Material surfaceRaised, Material accent,
            Material danger, Material brass)
        {
            GameObject uiRoot = root.transform.Find("PureUdonGo.UI").gameObject;
            uiRoot.transform.localPosition = board.transform.localPosition;
            uiRoot.transform.localRotation = board.transform.localRotation;
            uiRoot.transform.localScale = board.transform.localScale;
            Transform mount = CreateChild(uiRoot.transform, "Adaptive Control Wall Mount").transform;
            ui.controlPanelMount = mount;

            Vector2 panelSize = new Vector2(2720f, 1040f);
            GameObject canvasObject = new GameObject("AI Control Deck · VR Scale",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(VRCUiShape));
            canvasObject.transform.SetParent(mount, false);
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = panelSize;
            canvasRect.pivot = new Vector2(0.5f, 0f);
            canvasRect.localPosition = new Vector3(0f, 0.055f + PanelLift, PanelBackOffset);
            canvasRect.localScale = Vector3.one * PanelWorldScale;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 0;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 2f;
            AddStatusPanelCollider(canvasObject, panelSize);

            float physicalWidth = panelSize.x * PanelWorldScale;
            float physicalHeight = panelSize.y * PanelWorldScale;
            float wallBottom = (-BoardHeight + 0.02f) * TableHeightScale + PanelLift;
            float wallTop = 0.055f + PanelLift + physicalHeight + 0.050f;
            CreatePrimitive(mount, "Control Wall · Gemma Graphite", PrimitiveType.Cube,
                new Vector3(0f, (wallBottom + wallTop) * 0.5f, PanelBackOffset + 0.050f),
                new Vector3(physicalWidth + 0.100f, wallTop - wallBottom, 0.080f),
                Quaternion.identity, surface, false);
            CreatePrimitive(mount, "Control Wall · Accent Header", PrimitiveType.Cube,
                new Vector3(0f, 0.072f + PanelLift + physicalHeight, PanelBackOffset - 0.004f),
                new Vector3(physicalWidth * 0.18f, 0.010f, 0.010f),
                Quaternion.identity, brass, false);

            CreatePanelShell(canvas.transform, panelSize, surfaceRaised.color);
            BuildMatchScreen(canvas.transform, ui, font, accent.color, danger.color, surface.color, surfaceRaised.color);
            BuildSearchScreen(canvas.transform, ui, font, accent.color, surface.color, surfaceRaised.color);
            BuildSettingsScreen(canvas.transform, ui, font, accent.color, danger.color, surface.color, surfaceRaised.color);
        }

        private static void AddStatusPanelCollider(GameObject canvasObject, Vector2 panelSize)
        {
            // Keep one explicit physical hit volume for the complete control /
            // status deck.  Individual UGUI buttons still receive the actual
            // pointer events through GraphicRaycaster; this collider is not
            // placed anywhere in the board hierarchy.
            BoxCollider collider = canvasObject.GetComponent<BoxCollider>();
            if (collider == null)
                collider = canvasObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, panelSize.y * 0.5f, 0f);
            collider.size = new Vector3(panelSize.x, panelSize.y, 0.020f);
            collider.isTrigger = false;
            collider.enabled = true;
        }

        private static void BuildMatchScreen(Transform root, GoUI ui, TMP_FontAsset font,
            Color accent, Color danger, Color surface, Color surfaceRaised)
        {
            Image screen = CreateImage(root, "LEFT SCREEN · Go Match", new Rect(24f, 24f, 640f, 992f), Background);
            CreateImage(screen.transform, "Match Accent", new Rect(28f, 960f, 82f, 5f), accent).raycastTarget = false;
            ui.titleText = CreateText(screen.transform, "Title", "Udon Go AI · 19×19", font, 34f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(28f, 918f, 584f, 40f), TextPrimary);
            ui.panelStatusText = CreateText(screen.transform, "Status", "GAME READY", font, 20f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(28f, 882f, 584f, 28f), accent);
            ui.panelTurnText = CreateText(screen.transform, "Turn", "BLACK TO PLAY", font, 17f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(28f, 850f, 290f, 24f), TextSecondary);
            ui.panelScoreText = CreateText(screen.transform, "Score", "W − B 0.0 · KOMI 7.5", font, 16f,
                FontStyles.Normal, TextAlignmentOptions.Right, new Rect(300f, 850f, 312f, 24f), TextSecondary);
            ui.panelCaptureText = CreateText(screen.transform, "Captures", "CAPTURES  B 0  W 0", font, 15f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(28f, 822f, 584f, 22f), TextSecondary);
            ui.panelSeatText = CreateText(screen.transform, "Seats", "BLACK Open  |  WHITE AI", font, 15f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(28f, 798f, 584f, 22f), TextSecondary);
            ui.panelControllerText = CreateText(screen.transform, "Controllers", "BLACK · PLAYER  /  WHITE · AI", font, 14f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(28f, 774f, 390f, 21f), accent);
            ui.panelStarterText = CreateText(screen.transform, "Starter", "BLACK STARTS", font, 14f,
                FontStyles.Bold, TextAlignmentOptions.Right, new Rect(410f, 774f, 202f, 21f), TextSecondary);

            CreateLocalizedText(screen.transform, "Match Section", "对局模式", "MATCH", font, 13f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(28f, 746f, 584f, 18f), TextSecondary);
            GameObject mode = CreateRectObject(screen.transform, "Go Match Mode", new Rect(28f, 680f, 584f, 58f));
            AddLocalizedPanelButton(mode.transform, ui, font, "PVP", "PVP\nPLAYER VS PLAYER", "PVP\n玩家 vs 玩家", 3,
                new Rect(0f, 0f, 136f, 58f), surfaceRaised);
            AddLocalizedPanelButton(mode.transform, ui, font, "PVAI", "PVAI\nPLAYER VS AI", "PVAI\n玩家 vs AI", 4,
                new Rect(148f, 0f, 136f, 58f), accent);
            AddLocalizedPanelButton(mode.transform, ui, font, "AIVP", "AIVP\nAI VS PLAYER", "AIVP\nAI vs 玩家", 5,
                new Rect(296f, 0f, 136f, 58f), surfaceRaised);
            AddLocalizedPanelButton(mode.transform, ui, font, "AIVAI", "AIVAI\nAI VS AI", "AIVAI\nAI vs AI", 6,
                new Rect(444f, 0f, 140f, 58f), surfaceRaised);

            Image action = CreateImage(screen.transform, "Last Action Card", new Rect(28f, 618f, 584f, 46f), surface);
            ui.panelActionText = CreateText(action.transform, "Last Action", "NEW GAME · POSITION READY", font, 16f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(14f, 6f, 556f, 32f), TextPrimary);

            AddLocalizedPanelButton(screen.transform, ui, font, "New Game", "NEW", "新局", 0,
                new Rect(28f, 554f, 80f, 52f), accent);
            AddLocalizedPanelButton(screen.transform, ui, font, "Start Match", "START", "开始", 34,
                new Rect(112f, 554f, 80f, 52f), accent);
            AddLocalizedPanelButton(screen.transform, ui, font, "Pass", "PASS", "停一手", 1,
                new Rect(196f, 554f, 80f, 52f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Undo", "UNDO", "悔棋", 44,
                new Rect(280f, 554f, 80f, 52f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Draw", "DRAW", "和棋", 45,
                new Rect(364f, 554f, 80f, 52f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Resign", "RESIGN", "认输", 2,
                new Rect(448f, 554f, 80f, 52f), danger);
            AddLocalizedPanelButton(screen.transform, ui, font, "Force Reset", "RESET", "重置", 40,
                new Rect(532f, 554f, 80f, 52f), danger);

            AddLocalizedPanelButton(screen.transform, ui, font, "Join Black", "JOIN BLACK", "加入黑方", 30,
                new Rect(28f, 498f, 136f, 42f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Join White", "JOIN WHITE", "加入白方", 31,
                new Rect(176f, 498f, 136f, 42f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Leave Seat", "LEAVE SEAT", "离开棋席", 33,
                new Rect(324f, 498f, 136f, 42f), surfaceRaised);
            AddLocalizedPanelButton(screen.transform, ui, font, "Swap Sides", "SWAP SIDES", "交换执棋方", 35,
                new Rect(472f, 498f, 140f, 42f), surfaceRaised);

            AddLocalizedPanelButton(screen.transform, ui, font, "Black Starts", "BLACK FIRST", "黑方先手", 38,
                new Rect(28f, 446f, 282f, 42f), accent);
            AddLocalizedPanelButton(screen.transform, ui, font, "White Starts", "WHITE FIRST", "白方先手", 39,
                new Rect(330f, 446f, 282f, 42f), surfaceRaised);

            Image hintRow = CreateImage(screen.transform, "AI Hint + Recommended Row",
                new Rect(28f, 314f, 584f, 120f), surface);
            CreateLocalizedText(hintRow.transform, "Hint Row Label", "AI 提示 · 推荐着法",
                "AI HINT · RECOMMENDED MOVE", font, 13f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(12f, 94f, 560f, 18f), TextSecondary);
            AddLocalizedPanelButton(hintRow.transform, ui, font, "AI Hint Permission",
                "ENABLE BEFORE START", "开局前确认", 49,
                new Rect(12f, 46f, 270f, 42f), new Color(0.10f, 0.34f, 0.30f, 1f));
            AddLocalizedPanelButton(hintRow.transform, ui, font, "AI Hint Request",
                "RECOMMEND MOVE", "推荐着法", 43,
                new Rect(302f, 46f, 270f, 42f), new Color(0.10f, 0.34f, 0.30f, 1f));
            ui.panelHintText = CreateText(hintRow.transform, "Hint Status",
                "AI Hint · enable before Start Match", font, 13f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(12f, 12f, 560f, 24f), TextSecondary);
            AddLocalizedPanelButton(screen.transform, ui, font, "AI Move Now",
                "AI MOVE NOW · USE ESTIMATE", "AI 立即落子 · 使用当前估计", 50,
                new Rect(28f, 260f, 584f, 42f), new Color(0.27f, 0.22f, 0.10f, 1f));

            Image intro = CreateImage(screen.transform, "Project Introduction", new Rect(28f, 42f, 584f, 204f), surface);
            CreateLocalizedText(intro.transform, "Introduction",
                "你好，我是椰子梨梨花。Udon Go AI是一个基于Udon的VRChat围棋AI引擎。由于Udon性能有限（在我的测试中，其性能与Intel于1989年发布的80486 CPU处于同一数量级），我们设计了高度定制的Udon-Shader协处理架构：Udon负责游戏逻辑、状态管理与搜索控制，并通过纹理读写与Shader交换数据，将主要计算offload到GPU。通过这一方式，我们成功将KataGo移植进VRChat，实现了可实际游玩的高性能本地围棋AI。",
                "Hi, I'm 椰子梨梨花. Udon Go AI is a VRChat Go engine built with Udon and shaders. Udon handles rules, state and search control while the GPU accelerates neural inference. KataGo-compatible features make it a playable local Go AI in VRChat.",
                font, 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft,
                new Rect(18f, 18f, 548f, 168f), TextPrimary);

        }

        private static void BuildSearchScreen(Transform root, GoUI ui, TMP_FontAsset font,
            Color accent, Color surface, Color surfaceRaised)
        {
            Image screen = CreateImage(root, "CENTER SCREEN · Search", new Rect(682f, 24f, 1100f, 992f), Background);
            CreateLocalizedText(screen.transform, "Search Title", "当前局面分析", "CURRENT POSITION ANALYSIS", font, 38f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(28f, 930f, 650f, 46f), TextPrimary);
            ui.panelSearchText = CreateText(screen.transform, "Search Status", "AI READY · STANDBY", font, 18f,
                FontStyles.Bold, TextAlignmentOptions.Right, new Rect(650f, 940f, 420f, 28f), accent);
            Image telemetryCard = CreateImage(screen.transform, "Search Telemetry", new Rect(28f, 350f, 1044f, 550f), surface);
            ui.panelTelemetryText = CreateText(telemetryCard.transform, "Telemetry Values",
                 "AI STATE  Player vs AI · Ready to start\nSEARCH  0/8 visits\nBEST MOVE  —\nCANDIDATES  —  |  —  |  —\nWINRATE  B —  W —  NR —\nSCORE LEAD W−B — pts\nWHITE OWNERSHIP MEAN —\nLEVEL  Beginner  ·  CAPTURES B 0  W 0\nMOVE  0  ·  LAST —\nSTATIC CN SCORE W−B — zi\nCOMPUTE  —  ·  BLACK Open  |  WHITE Open", font, 28f,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, new Rect(28f, 70f, 988f, 470f), TextPrimary);
            ui.panelTelemetryText.richText = true;
            AddLocalizedPanelButton(screen.transform,ui,font,"Advanced Analysis",
                "ADVANCED ANALYSIS · OFF","高级分析 · 关",48,
                new Rect(780f,280f,292f,52f),surfaceRaised);

            Image progressCard = CreateImage(screen.transform, "Search Progress", new Rect(28f, 190f, 1044f, 140f), surface);
            CreateLocalizedText(progressCard.transform, "Search Progress Label", "搜索进度", "SEARCH PROGRESS", font, 18f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(20f, 98f, 1004f, 24f), TextPrimary);
            Image track = CreateImage(progressCard.transform, "Search Progress Track", new Rect(20f, 44f, 1004f, 18f), surfaceRaised);
            Image fill = CreateImage(track.transform, "Search Progress Fill", new Rect(0f, 0f, 1004f, 18f), accent);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            ui.searchProgressFill = fill;
        }

        private static void BuildSettingsScreen(Transform root, GoUI ui, TMP_FontAsset font,
            Color accent, Color danger, Color surface, Color surfaceRaised)
        {
            Image screen = CreateImage(root, "RIGHT SCREEN · Controls", new Rect(1800f, 24f, 896f, 992f), Background);
            CreateLocalizedText(screen.transform, "Settings Title", "难度与搜索", "DIFFICULTY & SEARCH", font, 34f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(28f, 930f, 600f, 42f), TextPrimary);
            ui.panelSettingsText = CreateText(screen.transform, "Settings Summary", "PVAI · SYNCED MATCH", font, 17f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(28f, 892f, 840f, 28f), TextSecondary);
            CreateLocalizedText(screen.transform, "Go Settings Hint", "19×19 围棋 · AI 设置", "19×19 GO · AI SETTINGS", font, 15f, FontStyles.Bold,
                TextAlignmentOptions.Right, new Rect(612f, 944f, 256f, 24f), accent);

            Image profiles = CreateImage(screen.transform, "AI Settings Card", new Rect(28f, 420f, 840f, 442f), surface);
            CreateLocalizedText(profiles.transform, "Profile Label", "共享档位 · 应用后同步", "SHARED LEVEL · APPLY TO SYNC", font, 16f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(20f, 392f, 800f, 24f), TextSecondary);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Preset Beginner", "BEGINNER\n8 VISITS", "入门\n8 次搜索", 10,
                new Rect(20f, 310f, 154f, 68f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Preset Advanced", "ADVANCED\n20 VISITS", "进阶\n20 次搜索", 11,
                new Rect(184f, 310f, 154f, 68f), accent);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Preset Master", "MASTER\n48 VISITS", "大师\n48 次搜索", 12,
                new Rect(348f, 310f, 154f, 68f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Preset Ultrahard", "Ultrahard\n120 VISITS", "Ultrahard\n120 次搜索", 13,
                new Rect(512f, 310f, 154f, 68f), new Color(0.31f, 0.15f, 0.37f, 1f));
            AddLocalizedPanelButton(profiles.transform, ui, font, "Black Follow Shared", "BLACK · FOLLOW SHARED", "黑方 · 跟随共享", 51,
                new Rect(20f, 228f, 190f, 54f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Black Custom", "BLACK · CUSTOM", "黑方 · 自定义", 52,
                new Rect(220f, 228f, 190f, 54f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "White Follow Shared", "WHITE · FOLLOW SHARED", "白方 · 跟随共享", 53,
                new Rect(430f, 228f, 190f, 54f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "White Custom", "WHITE · CUSTOM", "白方 · 自定义", 54,
                new Rect(630f, 228f, 190f, 54f), surfaceRaised);
            AddLocalizedPanelButton(profiles.transform, ui, font, "Apply Profile", "APPLY PROFILE · SYNC", "应用档位 · 同步", 23,
                new Rect(20f, 132f, 800f, 58f), accent);
            ui.panelAppliedProfileText = CreateText(profiles.transform, "Applied Profile Summary",
                "APPLIED · SHARED Beginner · BLACK PLAYER · WHITE AI", font, 14f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(20f, 100f, 800f, 24f), accent);

            Image custom = CreateImage(screen.transform, "Independent Custom Visits Card", new Rect(28f, 194f, 840f, 202f), surface);
            CreateLocalizedText(custom.transform, "Custom Visits Label", "黑方 / 白方独立自定义搜索次数", "INDEPENDENT BLACK / WHITE CUSTOM VISITS", font, 16f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(20f, 166f, 800f, 24f), TextSecondary);
            CreateLocalizedText(custom.transform, "Black Custom Visits Label", "黑方独立搜索次数", "BLACK INDEPENDENT SEARCH VISITS", font, 12f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(20f, 136f, 380f, 20f), TextSecondary);
            CreateLocalizedText(custom.transform, "White Custom Visits Label", "白方独立搜索次数", "WHITE INDEPENDENT SEARCH VISITS", font, 12f,
                FontStyles.Bold, TextAlignmentOptions.Left, new Rect(440f, 136f, 380f, 20f), TextSecondary);
            ui.blackCustomVisitsInput = CreateCustomVisitsInput(custom.transform,
                new Rect(20f, 88f, 380f, 46f), accent, surfaceRaised, "Black Custom Visits Input");
            ui.whiteCustomVisitsInput = CreateCustomVisitsInput(custom.transform,
                new Rect(440f, 88f, 380f, 46f), accent, surfaceRaised, "White Custom Visits Input");
            ui.blackCustomVisitsValueText = CreateText(custom.transform, "Black Custom Visits Value",
                "BLACK CUSTOM  4", font, 15f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(20f, 62f, 380f, 24f), accent);
            ui.whiteCustomVisitsValueText = CreateText(custom.transform, "White Custom Visits Value",
                "WHITE CUSTOM  4", font, 15f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Rect(440f, 62f, 380f, 24f), accent);
            CreateLocalizedText(custom.transform, "Custom Visits Apply Hint", "仅启用自定义的一方可编辑；范围 1–1226。输入数字后按上方“应用档位 · 同步”。", "Only a side in Custom mode is editable; range 1–1226. Enter a number, then press Apply Profile · Sync.", font, 12f,
                FontStyles.Normal, TextAlignmentOptions.Left, new Rect(20f, 22f, 800f, 28f), TextSecondary);
            AddLocalizedPanelButton(screen.transform, ui, font, "Language", "LANGUAGE · ENGLISH", "语言 · 中文", 22,
                new Rect(28f, 112f, 840f, 58f), surfaceRaised);
        }

        private static void AssignLocalizedText(GoUI ui, int startIndex)
        {
            int start = Mathf.Clamp(startIndex, 0, localizedBindings.Count);
            int count = localizedBindings.Count - start;
            ui.localizedTexts = new TMP_Text[count];
            ui.chineseTexts = new string[count];
            ui.englishTexts = new string[count];
            for (int i = 0; i < count; i++)
            {
                LocalizedBinding binding = localizedBindings[start + i];
                ui.localizedTexts[i] = binding.text;
                ui.chineseTexts[i] = binding.chinese;
                ui.englishTexts[i] = binding.english;
            }
        }

        private static void CreatePanelShell(Transform parent, Vector2 size, Color surfaceRaised)
        {
            Image shadow = CreateImage(parent, "Soft Ambient Shadow",
                new Rect(-14f, -14f, size.x + 28f, size.y + 28f), new Color(0f, 0f, 0f, 0.34f));
            shadow.raycastTarget = false;
            Image edge = CreateImage(parent, "Machined Edge", new Rect(0f, 0f, size.x, size.y), surfaceRaised);
            edge.raycastTarget = false;
            Image glass = CreateImage(parent, "Graphite Glass", new Rect(3f, 3f, size.x - 6f, size.y - 6f), Background);
            glass.raycastTarget = true;
        }

        private static Button AddPanelButton(Transform parent, GoUI ui, TMP_FontAsset font,
            string name, string label, int action, Rect rect, Color color)
        {
            return AddLocalizedPanelButton(parent, ui, font, name, label, label, action, rect, color);
        }

        private static Button AddLocalizedPanelButton(Transform parent, GoUI ui, TMP_FontAsset font,
            string name, string englishLabel, string chineseLabel, int action, Rect rect, Color color)
        {
            Image image = CreateImage(parent, name, rect, color);
            Button button = ConfigureButton(image);
            AddButtonEndpoint(button, ui, action);
            TMP_Text text = CreateLocalizedText(image.transform, "Label", chineseLabel, englishLabel, font,
                englishLabel.IndexOf('\n') >= 0 ? 18f : 22f, FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Rect(8f, 5f, rect.width - 16f, rect.height - 10f), TextPrimary);
            text.enableAutoSizing = true;
            text.fontSizeMax = englishLabel.IndexOf('\n') >= 0 ? 18f : 22f;
            text.fontSizeMin = englishLabel.IndexOf('\n') >= 0 ? 11f : 13f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (name == "Undo") ui.undoButtonText = text;
            if (name == "Draw") ui.drawButtonText = text;
            if (name == "Advanced Analysis") ui.advancedAnalysisButtonText = text;
            if (name == "AI Hint Permission") ui.aiHintPermissionButtonText = text;
            if (name == "AI Move Now") ui.aiMoveNowButtonText = text;
            if (name == "Force Reset") ui.forceResetButtonText = text;
            return button;
        }

        private static TMP_Text CreateLocalizedText(Transform parent, string name,
            string chinese, string english, TMP_FontAsset font, float fontSize,
            FontStyles style, TextAlignmentOptions alignment, Rect rect, Color color)
        {
            TMP_Text text = CreateText(parent, name, english, font, fontSize, style, alignment, rect, color);
            localizedBindings.Add(new LocalizedBinding
            {
                text = text,
                chinese = chinese,
                english = english
            });
            return text;
        }

        private static void AddButtonEndpoint(Button button, GoUI ui, int action)
        {
            GoUiButton endpoint = button.gameObject.AddUdonSharpComponent<GoUiButton>();
            endpoint.ui = ui;
            endpoint.action = action;
            ButtonBinding binding = new ButtonBinding { button = button, endpoint = endpoint };
            bindings.Add(binding);
            if (action < 1000)
                controlBindings.Add(binding);
        }

        private static void AssignControlButtons(GoUI ui, int startIndex)
        {
            int start = Mathf.Clamp(startIndex, 0, controlBindings.Count);
            int count = controlBindings.Count - start;
            ui.controlButtons = new Button[count];
            ui.controlButtonActions = new int[count];
            for (int i = 0; i < count; i++)
            {
                ButtonBinding binding = controlBindings[start + i];
                ui.controlButtons[i] = binding.button;
                ui.controlButtonActions[i] = binding.endpoint.action;
            }
        }

        private static Button ConfigureButton(Image image)
        {
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.76f, 0.76f, 0.76f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        private static TMP_InputField CreateCustomVisitsInput(Transform parent, Rect rect,
            Color accentColor, Color backgroundColor, string objectName)
        {
            Image background = CreateImage(parent, objectName, rect, backgroundColor);
            background.raycastTarget = true;
            TMP_InputField input = background.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Integer;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.keyboardType = TouchScreenKeyboardType.NumberPad;
            input.interactable = true;
            input.text = GoDifficultyProfile.BEGINNER_VISITS.ToString();

            TMP_Text text = CreateText(background.transform, "Input Text", "4", ResolveFont(), 22f,
                FontStyles.Bold, TextAlignmentOptions.Center,
                new Rect(12f, 5f, rect.width - 24f, rect.height - 10f), TextPrimary);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            input.textComponent = text as TextMeshProUGUI;
            input.caretColor = accentColor;
            input.selectionColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.45f);
            Navigation navigation = input.navigation;
            navigation.mode = Navigation.Mode.None;
            input.navigation = navigation;
            return input;
        }

        private static void BindAllButtons()
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                ButtonBinding binding = bindings[i];
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(binding.endpoint);
                if (!backing || !backing.programSource)
                    throw new InvalidOperationException("UI button has no compiled Udon program: " + binding.button.name);
                // UGUI buttons are pointer targets, not VRChat Interact
                // targets.  DisableInteractive removes the world-space Use
                // tooltip/highlight while leaving Button.onClick intact.
                backing.DisableInteractive = true;
                EditorUtility.SetDirty(backing);
                UnityEventTools.AddStringPersistentListener(binding.button.onClick,
                    backing.SendCustomEvent, "Press");
                EditorUtility.SetDirty(binding.button);
            }
        }

        private static void DisableButtonInteractionHints()
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                ButtonBinding binding = bindings[i];
                if (binding == null || binding.endpoint == null) continue;
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(binding.endpoint);
                if (backing == null) continue;
                backing.DisableInteractive = true;
                EditorUtility.SetDirty(backing);
            }
        }

        private static TMP_FontAsset ResolveFont()
        {
            if (!EnsureTmpEssentials())
                throw new InvalidOperationException("TMP Essential Resources are being imported; generation will resume after import.");
            string generatedPath = GeneratedFolder + "/PureUdonGo UI SDF.asset";
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generatedPath);
            if (font != null)
                return font;
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/PureUdonGo/Fonts/NotoSansSC-Regular.otf");
            if (sourceFont != null)
            {
                font = CreateGeneratedFontAsset(sourceFont, generatedPath);
                if (font != null)
                    return font;
            }
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font != null)
                return font;
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (font != null)
                    return font;
            }
            Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtin == null)
                throw new InvalidOperationException("No TMP_FontAsset or Unity built-in font is available for the production control wall.");
            return CreateGeneratedFontAsset(builtin, generatedPath);
        }

        private static TMP_FontAsset CreateGeneratedFontAsset(Font source, string generatedPath)
        {
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source);
            if (font == null)
                throw new InvalidOperationException("TMP could not create the generated production UI font asset.");
            font.name = "PureUdonGo UI SDF";
            AssetDatabase.CreateAsset(font, generatedPath);
            if (font.atlasTexture != null)
                AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
            if (font.material != null)
                AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static bool EnsureTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
                return true;
            string embeddedPackage = Path.Combine(Application.dataPath,
                "PureUdonGo/Editor/Resources/TMP Essential Resources.unitypackage");
            if (File.Exists(embeddedPackage))
            {
                BeginTmpEssentialsImport(embeddedPackage);
                return false;
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string packageCache = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCache))
                return false;
            string[] packages = Directory.GetFiles(packageCache, "TMP Essential Resources.unitypackage", SearchOption.AllDirectories);
            if (packages.Length == 0)
                return false;
            BeginTmpEssentialsImport(packages[0]);
            return false;
        }

        private static void BeginTmpEssentialsImport(string packagePath)
        {
            if (waitingForTmpEssentials)
                return;
            waitingForTmpEssentials = true;
            AssetDatabase.importPackageCompleted += OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed += OnTmpEssentialsImportFailed;
            AssetDatabase.ImportPackage(packagePath, false);
            Debug.Log("PURE_UDON_GO_TMP_IMPORT_QUEUED path=" + packagePath);
        }

        private static void OnTmpEssentialsImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed -= OnTmpEssentialsImportFailed;
            waitingForTmpEssentials = false;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                Debug.LogError("PURE_UDON_GO_TMP_IMPORT_FAIL shader unavailable after package completion: " + packageName);
                return;
            }
            Debug.Log("PURE_UDON_GO_TMP_IMPORT_PASS package=" + packageName);
            EditorApplication.delayCall += GenerateFinalProductionScene;
        }

        private static void OnTmpEssentialsImportFailed(string packageName, string error)
        {
            AssetDatabase.importPackageCompleted -= OnTmpEssentialsImported;
            AssetDatabase.importPackageFailed -= OnTmpEssentialsImportFailed;
            waitingForTmpEssentials = false;
            Debug.LogError("PURE_UDON_GO_TMP_IMPORT_FAIL package=" + packageName + " error=" + error);
        }

        private static Image CreateImage(Transform parent, string name, Rect rect, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), rect);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.sprite = roundedSprite;
            image.type = roundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            return image;
        }

        private static TMP_Text CreateText(Transform parent, string name, string value,
            TMP_FontAsset font, float fontSize, FontStyles style, TextAlignmentOptions alignment,
            Rect rect, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), rect);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.richText = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Transform CreateWorldText(Transform parent, string name, string value,
            Vector3 position, Quaternion rotation, Vector3 scale, float fontSize,
            FontStyles style, Color color)
        {
            GameObject go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            TextMeshPro text = go.GetComponent<TextMeshPro>();
            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.text = value;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return go.transform;
        }

        private static GameObject CreateRectObject(Transform parent, string name, Rect rect)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), rect);
            return go;
        }

        private static void SetRect(RectTransform rect, Rect value)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(value.x, value.y);
            rect.sizeDelta = new Vector2(value.width, value.height);
        }

        private static Vector3 LocationToLocal(int loc, float y)
        {
            int x = loc % GoGame.SIZE;
            int z = loc / GoGame.SIZE;
            return new Vector3((x - 9) * BoardSpacing, y, (z - 9) * BoardSpacing);
        }

        private static Vector3 GetTablePosition(int tableIndex)
        {
            if (tableIndex < 0)
                tableIndex = 0;
            float centerOffset=(GoBoardPool.INITIAL_VISIBLE_TABLES-1)*0.5f;
            return new Vector3(TableRowSpacingWorld * (tableIndex-centerOffset), 0f, 0f);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreatePrimitive(Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale, Quaternion rotation, Material material, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation;
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            if (!collider)
            {
                Collider value = go.GetComponent<Collider>();
                if (value != null)
                    Object.DestroyImmediate(value);
            }
            return go;
        }

        private static void CreateLine(Transform parent, string name, Vector3 start, Vector3 end,
            float width, Material material)
        {
            GameObject go = CreateChild(parent, name);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.sharedMaterial = material;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private static void DisableMarkerShadows(GameObject marker)
        {
            Renderer renderer = marker == null ? null : marker.GetComponent<Renderer>();
            if (renderer == null)
                return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material GetMaterial(string name, Color color, float metallic,
            float smoothness, bool emission)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    throw new InvalidOperationException("No Unity material shader is available for " + name);
                material = new Material(shader);
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.55f);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyTexture(Material material, Texture2D texture, Vector2 scale)
        {
            if (material == null || texture == null)
                return;
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", scale);
                EditorUtility.SetDirty(material);
            }
        }

        private static Texture2D BuildWoodGrainTexture()
        {
            string path = TextureFolder + "/Go Maple Grain.png";
            Texture2D texture = new Texture2D(256, 256, TextureFormat.RGBA32, true, false);
            Color[] pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float wave = Mathf.Sin(x * 0.105f + Mathf.Sin(y * 0.021f) * 2.4f);
                    float fine = Mathf.Sin(x * 0.42f + y * 0.018f) * 0.020f;
                    float band = Mathf.Sin((x + y * 0.18f) * 0.024f) * 0.026f;
                    float grain = wave * 0.035f + fine + band;
                    float edge = Mathf.Abs(y - 128f) / 128f;
                    Color value = new Color(
                        Mathf.Clamp01(0.84f + grain - edge * 0.018f),
                        Mathf.Clamp01(0.67f + grain * 0.72f - edge * 0.012f),
                        Mathf.Clamp01(0.48f + grain * 0.42f - edge * 0.008f), 1f);
                    pixels[y * 256 + x] = value;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException("Generated Go wood grain texture could not be imported.");
            return texture;
        }

        private static Texture2D BuildWallpaperTexture()
        {
            string path = TextureFolder + "/Indoor Wallpaper Weave.png";
            Texture2D texture = new Texture2D(128, 128, TextureFormat.RGBA32, true, false);
            Color[] pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float warp = Mathf.Sin(x * 0.43f + Mathf.Sin(y * 0.075f) * 1.8f) * 0.012f;
                    float weave = Mathf.Sin(y * 1.15f) * 0.008f + Mathf.Sin(x * 2.1f) * 0.006f;
                    float panel = ((x / 16) & 1) == 0 ? 0.010f : -0.006f;
                    float value = Mathf.Clamp01(0.93f + warp + weave + panel);
                    pixels[y * 128 + x] = new Color(value, value, value * 0.985f, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException("Generated indoor wallpaper texture could not be imported.");
            return texture;
        }

        private static void BuildBoardAudio(GameObject board, GoGame game, AudioClip place,
            AudioClip capture, AudioClip pass, AudioClip gameEnd, AudioClip reset)
        {
            GameObject audioObject = CreateChild(board.transform, "Board Audio · 2.2 m Spatial SFX");
            audioObject.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0.82f;
            source.priority = 96;
            source.spatialBlend = 1f;
            source.spatialize = true;
            source.dopplerLevel = 0f;
            source.spread = 18f;
            source.minDistance = 0.45f;
            source.maxDistance = BoardAudioRadius;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.70f, 0.82f),
                new Keyframe(0.90f, 0.34f), new Keyframe(1f, 0f)));
            VRCSpatialAudioSource spatial = audioObject.AddComponent<VRCSpatialAudioSource>();
            spatial.Gain = 3f;
            spatial.Near = 0.45f;
            spatial.Far = BoardAudioRadius;
            spatial.VolumetricRadius = 0f;
            spatial.EnableSpatialization = true;
            spatial.UseAudioSourceVolumeCurve = true;
            game.audioSource = source;
            game.placeClip = place;
            game.captureClip = capture;
            game.passClip = pass;
            game.gameEndClip = gameEnd;
            game.resetClip = reset;
        }

        private static void BuildAudioAssets(out AudioClip place, out AudioClip capture,
            out AudioClip pass, out AudioClip gameEnd, out AudioClip reset)
        {
            const int sampleRate = 44100;
            string placePath = AudioFolder + "/Place - Maple Clack.wav";
            string capturePath = AudioFolder + "/Capture - Stone Double Clack.wav";
            string passPath = AudioFolder + "/Pass - Low Resonance.wav";
            string endPath = AudioFolder + "/Game End - Bronze Chime.wav";
            string resetPath = AudioFolder + "/Reset - Soft Return.wav";
            WriteGeneratedWav(placePath, 0, 0.17f, sampleRate);
            WriteGeneratedWav(capturePath, 1, 0.27f, sampleRate);
            WriteGeneratedWav(passPath, 2, 0.32f, sampleRate);
            WriteGeneratedWav(endPath, 3, 0.72f, sampleRate);
            WriteGeneratedWav(resetPath, 4, 0.24f, sampleRate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            place = AssetDatabase.LoadAssetAtPath<AudioClip>(placePath);
            capture = AssetDatabase.LoadAssetAtPath<AudioClip>(capturePath);
            pass = AssetDatabase.LoadAssetAtPath<AudioClip>(passPath);
            gameEnd = AssetDatabase.LoadAssetAtPath<AudioClip>(endPath);
            reset = AssetDatabase.LoadAssetAtPath<AudioClip>(resetPath);
            if (place == null || capture == null || pass == null || gameEnd == null || reset == null)
                throw new InvalidOperationException("Generated Go audio assets could not be imported.");
        }

        private static void WriteGeneratedWav(string path, int cue, float duration, int sampleRate)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * sampleRate));
            using (MemoryStream stream = new MemoryStream(44 + sampleCount * 2))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount * 2);
                for (int i = 0; i < sampleCount; i++)
                {
                    float t = i / (float)sampleRate;
                    float phase = t / duration;
                    float envelope = Mathf.Clamp01(1f - phase) * Mathf.Clamp01(phase * 50f);
                    float frequency = cue == 3 ? 440f + 220f * phase : 180f + cue * 70f;
                    float value = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.42f;
                    if (cue == 1)
                        value += Mathf.Sin(2f * Mathf.PI * (frequency * 1.83f) * t) * envelope * 0.20f;
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(value * 32767f), short.MinValue, short.MaxValue));
                }
                File.WriteAllBytes(path, stream.ToArray());
            }
        }

        private static void VerifyGeneratedScene(Scene scene, GameObject root, GoBoardPool boardPool,
            GoGame game, GoBoardView view, GoUI ui, GoTelemetry telemetry,
            GoDifficultyProfile difficulty, GoDifficultyProfile blackDifficulty, GoAiController ai)
        {
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Generated scene path mismatch: " + scene.path);
            if (root == null || root.GetComponent<GoGeneratedWorld>() == null)
                throw new InvalidOperationException("Generated root marker is missing.");
            if (root.GetComponent<GoGeneratedWorld>().schemaVersion != GoGeneratedWorld.CURRENT_SCHEMA)
                throw new InvalidOperationException("Generated root schema is stale; regenerate the production scene.");
            const string primaryTablePath =
                "PureUdonGo.TablePool/PureUdonGo.Table.000";
            Transform tablePool = root.transform.Find("PureUdonGo.TablePool");
            Transform primaryTable = root.transform.Find(primaryTablePath);
            if (root.transform.Find("PureUdonGo.Environment · Indoor Room") == null ||
                tablePool == null || primaryTable == null || boardPool == null)
                throw new InvalidOperationException("Generated product roots are incomplete.");
            if (root.GetComponentsInChildren<GoGeneratedWorld>(true).Length != 1)
                throw new InvalidOperationException("Generated world must contain exactly one owned marker.");
            if (root.GetComponentsInChildren<GoAiSettings>(true).Length != GoBoardPool.MAX_TABLES)
                throw new InvalidOperationException("Generated world must contain exactly one GoAiSettings domain per table.");
            if (boardPool.tableRoots == null || boardPool.tableRoots.Length != GoBoardPool.MAX_TABLES ||
                boardPool.primaryUI != ui)
                throw new InvalidOperationException("Generated Go table pool arrays are incomplete or not primary-bound.");
            if (boardPool.visibleTableCount != GoBoardPool.INITIAL_VISIBLE_TABLES)
                throw new InvalidOperationException("Generated Go table pool must start with three visible tables.");
            for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
            {
                Transform table = tablePool.Find("PureUdonGo.Table." + tableIndex.ToString("000"));
                GoGame tableGame = table == null ? null : table.GetComponentInChildren<GoGame>(true);
                GoBoardView tableView = table == null ? null : table.GetComponentInChildren<GoBoardView>(true);
                GoUI tableUI = table == null ? null : table.GetComponentInChildren<GoUI>(true);
                GoTelemetry tableTelemetry = table == null ? null : table.GetComponentInChildren<GoTelemetry>(true);
                GoAiSettings tableSettings = table == null ? null : table.GetComponentInChildren<GoAiSettings>(true);
                GoAiController tableAi = table == null ? null : table.GetComponentInChildren<GoAiController>(true);
                GoGpuNeuralOutputReader[] tableReaders = table == null ?
                    new GoGpuNeuralOutputReader[0] :
                    table.GetComponentsInChildren<GoGpuNeuralOutputReader>(true);
                if (table == null || boardPool.tableRoots[tableIndex] != table.gameObject ||
                    table.gameObject.activeSelf != (tableIndex < GoBoardPool.INITIAL_VISIBLE_TABLES) ||
                    tableGame == null || tableView == null || tableUI == null ||
                    tableSettings == null ||
                    tableTelemetry == null || tableAi == null || tableReaders.Length != 3 ||
                    tableAi.reader == null || tableAi.alternateReader == null ||
                    tableAi.tertiaryReader == null ||
                    tableAi.readerA != tableAi.reader ||
                    tableAi.readerB != tableAi.alternateReader ||
                    tableAi.readerC != tableAi.tertiaryReader ||
                    tableAi.reader == tableAi.alternateReader ||
                    tableAi.reader == tableAi.tertiaryReader ||
                    tableAi.alternateReader == tableAi.tertiaryReader)
                    throw new InvalidOperationException("Generated Go table pool entry is incomplete at index " + tableIndex + ".");
                if (tableIndex > 0 &&
                    (tableGame == game || tableView == view || tableUI == ui || tableAi == ai))
                    throw new InvalidOperationException("Generated Go table pool entries must own independent runtime state.");
                Transform tableBoard = table.Find("PureUdonGo.Board");
                Transform tableDeck = table.Find(
                    "PureUdonGo.UI/Adaptive Control Wall Mount/AI Control Deck · VR Scale");
                BoxCollider tableDeckCollider = tableDeck == null ? null :
                    tableDeck.GetComponent<BoxCollider>();
                Transform tableInputCanvas = tableBoard == null ? null : tableBoard.Find(
                    "Board Origin · 19 × 19/Board Interaction Canvas · 361 Intersections");
                BoxCollider tableBoardCollider = tableInputCanvas == null ? null :
                    tableInputCanvas.GetComponent<BoxCollider>();
                Collider[] tableBoardColliders = tableBoard == null ? new Collider[0] :
                    tableBoard.GetComponentsInChildren<Collider>(true);
                if (tableBoard == null || tableBoardColliders.Length != 1 ||
                    tableBoardCollider == null || tableBoardColliders[0] != tableBoardCollider ||
                    !tableBoardCollider.enabled || !tableBoardCollider.isTrigger ||
                    tableDeckCollider == null || !tableDeckCollider.enabled || tableDeckCollider.isTrigger)
                    throw new InvalidOperationException(
                        "Every generated table must have only the UIShape trigger on its board and a solid status panel.");
                Vector3 expectedTablePosition = GetTablePosition(tableIndex);
                if (table == null || Vector3.Distance(table.localPosition, expectedTablePosition) > 0.0001f)
                    throw new InvalidOperationException(
                        "Generated Go table pool must be a single Xiangqi-style row at index " + tableIndex + ".");
            }
            Transform generatedFloor = root.transform.Find(
                "PureUdonGo.Environment · Indoor Room/Indoor Room Floor · Walkable");
            if (generatedFloor == null || generatedFloor.GetComponent<Collider>() == null ||
                !generatedFloor.GetComponent<Collider>().enabled ||
                generatedFloor.GetComponent<Collider>().isTrigger)
                throw new InvalidOperationException(
                    "Generated indoor floor must have an enabled collider for VR locomotion.");
            Renderer floorRenderer = generatedFloor.GetComponent<Renderer>();
            if (floorRenderer == null || floorRenderer.bounds.size.x < GalleryFloorLengthWorld - 0.10f)
                throw new InvalidOperationException(
                    "Generated indoor floor must cover the complete three-table room.");
            string[] roomSurfaces =
            {
                "Indoor Back Wall · Mist Wallpaper",
                "Indoor Front Wall · Beige Wallpaper",
                "Indoor Left Wall · Beige Wallpaper",
                "Indoor Right Wall · Mist Wallpaper",
                "Indoor Ceiling · Soft White"
            };
            for (int i = 0; i < roomSurfaces.Length; i++)
            {
                Transform surface = root.transform.Find("PureUdonGo.Environment · Indoor Room/" + roomSurfaces[i]);
                if (surface == null || surface.GetComponent<Renderer>() == null ||
                    surface.GetComponent<Collider>() == null || surface.GetComponent<Collider>().isTrigger)
                    throw new InvalidOperationException("Generated indoor room surface is incomplete: " + roomSurfaces[i]);
            }
            Transform boardTransform = root.transform.Find(primaryTablePath + "/PureUdonGo.Board");
            Transform controlDeck = root.transform.Find(primaryTablePath + "/PureUdonGo.UI/" +
                "Adaptive Control Wall Mount/AI Control Deck · VR Scale");
            if (boardTransform == null)
                throw new InvalidOperationException("Primary generated Go board is missing.");
            RectTransform controlDeckRect = controlDeck == null ? null : controlDeck.GetComponent<RectTransform>();
            if (controlDeckRect == null || Mathf.Abs(controlDeckRect.localPosition.y - (0.055f + PanelLift)) > 0.002f)
                throw new InvalidOperationException("Control wall vertical placement drifted from the separated Go layout.");
            float boardBackEdgeWorld = boardTransform.position.z +
                boardTransform.lossyScale.z * BoardWorldScale * 8.35f * 0.5f;
            if (controlDeck.position.z - boardBackEdgeWorld < 0.10f)
                throw new InvalidOperationException("Control wall canvas is not physically in front of the rear board edge.");
            float boardHalfDepth = 8.35f * BoardWorldScale * 0.5f;
            float panelFront = PanelBackOffset - 0.040f;
            if (panelFront <= boardHalfDepth + 0.15f)
                throw new InvalidOperationException("Control wall is too close to the 19x19 board; spatial gap is unsafe.");
            foreach(GoBoardView tableView in root.GetComponentsInChildren<GoBoardView>(true))
                GoBoardInputValidator.Validate(tableView);
            if (view == null || view.stoneObjects == null || view.stoneObjects.Length != GoGame.AREA)
                throw new InvalidOperationException("Generated stone pool is not 361 entries.");
            Transform firstStone = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Stone Pool · 361 Intersections/Stone 000");
            MeshFilter firstStoneFilter = firstStone == null ? null : firstStone.GetComponent<MeshFilter>();
            float firstStoneBottom = firstStone == null || firstStoneFilter == null || firstStoneFilter.sharedMesh == null
                ? -1f : firstStone.localPosition.y + firstStone.localScale.y * firstStoneFilter.sharedMesh.bounds.min.y;
            if (firstStone == null || firstStoneFilter == null || firstStoneFilter.sharedMesh == null ||
                firstStoneBottom < 0.084f)
                throw new InvalidOperationException("Generated pebble stones must sit above the maple playing surface.");
            Transform stonePool = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Stone Pool · 361 Intersections");
            Transform boardInputCanvas = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Board Interaction Canvas · 361 Intersections");
            Transform boardOrigin = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19");
            BoxCollider statusPanelCollider = controlDeck == null ? null : controlDeck.GetComponent<BoxCollider>();
            Collider[] boardColliders = boardTransform == null ? new Collider[0] :
                boardTransform.GetComponentsInChildren<Collider>(true);
            Collider boardOriginCollider = boardOrigin == null ? null : boardOrigin.GetComponent<Collider>();
            BoxCollider boardInputCollider = boardInputCanvas == null ? null :
                boardInputCanvas.GetComponent<BoxCollider>();
            if (stonePool == null || boardInputCanvas == null ||
                boardInputCanvas.GetComponent<GoBoardInput>() == null ||
                statusPanelCollider == null || !statusPanelCollider.enabled || statusPanelCollider.isTrigger ||
                boardColliders.Length != 1 || boardColliders[0] != boardInputCollider ||
                boardInputCollider == null || !boardInputCollider.enabled || !boardInputCollider.isTrigger ||
                boardOriginCollider != null)
                throw new InvalidOperationException(
                    "Generated board must use one UIShape trigger collider and keep one solid status-panel collider.");
            if (Mathf.Abs(view.markerHeight - BoardPointMarkerY) > 0.0001f)
                throw new InvalidOperationException("Board preview datum drifted from the Go grid plane.");
            for (int loc = 0; loc < GoGame.AREA; loc++)
            {
                Transform stone = stonePool.Find("Stone " + loc.ToString("000"));
                if (stone == null || stone.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException(
                        "Generated stone pool contains a missing stone or physics collider at location " + loc + ".");
            }
            LineRenderer gridLine = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · File 00")?.GetComponent<LineRenderer>();
            LineRenderer outerFile = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · File 18")?.GetComponent<LineRenderer>();
            LineRenderer outerRank = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Grid · Rank 00")?.GetComponent<LineRenderer>();
            Transform duplicateTrim = root.transform.Find(
                primaryTablePath + "/PureUdonGo.Board/Board Origin · 19 × 19/Trim · Front");
            if (gridLine == null || outerFile == null || outerRank == null || duplicateTrim != null ||
                Mathf.Abs(gridLine.startWidth - GridLineWidth) > 0.0001f ||
                Mathf.Abs(outerFile.startWidth - GridLineWidth) > 0.0001f ||
                Mathf.Abs(outerRank.startWidth - GridLineWidth) > 0.0001f ||
                outerFile.sharedMaterial != gridLine.sharedMaterial ||
                outerRank.sharedMaterial != gridLine.sharedMaterial ||
                Mathf.Abs(outerFile.GetPosition(0).y - gridLine.GetPosition(0).y) > 0.0001f ||
                Mathf.Abs(outerRank.GetPosition(0).y - gridLine.GetPosition(0).y) > 0.0001f)
                throw new InvalidOperationException("Generated outer board edge must be the single thin coplanar grid layer; duplicate trim lines are forbidden.");
            if (ui == null || ui.panelTelemetryText == null || ui.panelSettingsText == null ||
                ui.panelActionText == null || ui.panelHintText == null ||
                ui.undoButtonText == null || ui.drawButtonText == null ||
                ui.advancedAnalysisButtonText == null ||
                ui.aiHintPermissionButtonText == null || ui.aiMoveNowButtonText == null ||
                ui.blackCustomVisitsInput == null || ui.whiteCustomVisitsInput == null ||
                ui.blackCustomVisitsValueText == null || ui.whiteCustomVisitsValueText == null ||
                !ui.blackCustomVisitsInput.gameObject.activeSelf ||
                !ui.whiteCustomVisitsInput.gameObject.activeSelf ||
                ui.searchProgressFill == null ||
                ui.localizedTexts == null || ui.chineseTexts == null || ui.englishTexts == null ||
                ui.localizedTexts.Length != ui.chineseTexts.Length ||
                ui.localizedTexts.Length != ui.englishTexts.Length)
                throw new InvalidOperationException("Generated Go control wall bindings are incomplete.");
            if (ui.searchProgressFill != null &&
                (ui.searchProgressFill.type != Image.Type.Filled ||
                 ui.searchProgressFill.fillMethod != Image.FillMethod.Horizontal))
                throw new InvalidOperationException("Generated search progress must be a horizontal filled image.");
            VerifyControlActionSet(ui);
            TMP_Text[] generatedLabels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < generatedLabels.Length; i++)
            {
                string label = generatedLabels[i] == null ? "" : generatedLabels[i].text;
                if (label.IndexOf("AI 决策摘要", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("对局提示", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("KataGo（简化版）", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("推荐算法", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("高级设置", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("室内围棋", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("添加棋桌", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("移除最后棋桌", StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("Generated center UI contains an obsolete analysis label.");
            }
            for (int i = 0; i < ui.controlButtons.Length; i++)
                if (ui.controlButtons[i] == null)
                    throw new InvalidOperationException(
                        "Generated Go control button binding is null at index " + i + ".");
            if (view.hintMarker == null || view.previewStone == null ||
                view.previewStone.activeSelf || view.previewStone.transform.localPosition.y >= 0f ||
                !view.moveMaskWarmupManagedByPool)
                throw new InvalidOperationException("Generated Go board is missing the pooled hidden preview stone or hint marker.");
            if (telemetry == null || telemetry.panelText == null)
                throw new InvalidOperationException("Generated Go telemetry is not bound to the search screen.");
            if (!HasFixedAnalysisTemplate(telemetry.panelText.text))
                throw new InvalidOperationException("Generated center analysis must use the fixed eleven-line English template.");
            if (game == null || game.aiSettings == null || ui.aiSettings != game.aiSettings ||
                ai == null || ai.aiSettings != game.aiSettings ||
                game.aiSettings.sharedPreset != GoDifficultyProfile.BEGINNER ||
                game.aiSettings.blackUseCustom || game.aiSettings.whiteUseCustom ||
                difficulty == null || difficulty.settings != game.aiSettings ||
                blackDifficulty == null || blackDifficulty.settings != game.aiSettings ||
                !difficulty.settingsMirror || !blackDifficulty.settingsMirror ||
                difficulty.GetPresetLabel() != "Beginner" ||
                difficulty.maxVisits != GoDifficultyProfile.BEGINNER_VISITS || blackDifficulty == null ||
                blackDifficulty.maxVisits != GoDifficultyProfile.BEGINNER_VISITS || GoMctsSearch.MAX_SUPPORTED_VISITS != 1226)
                throw new InvalidOperationException("Difficulty profile or fixed engine capacity is invalid.");
            if (ai == null || ai.mode != GoAiController.MODE_PVAI || ai.aiColor != GoGame.WHITE ||
                game.blackIsAI || !game.whiteIsAI || game.matchStarted || game.starter != GoGame.BLACK)
                throw new InvalidOperationException("Generated default mode must be PvAI with White AI.");
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            if (canvases.Length != GoBoardPool.MAX_TABLES+root.GetComponentsInChildren<GoBoardInput>(true).Length)
                throw new InvalidOperationException("Unexpected control-wall/board input canvas count.");
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].GetComponent<GraphicRaycaster>() == null ||
                    canvases[i].GetComponent<VRCUiShape>() == null)
                    throw new InvalidOperationException("Canvas missing GraphicRaycaster/VRCUiShape: " + canvases[i].name);
            }
            if (root.GetComponentsInChildren<AudioListener>(true).Length != 1)
                throw new InvalidOperationException("Generated scene must have exactly one AudioListener.");
            if (root.GetComponentsInChildren<AudioSource>(true).Length != GoBoardPool.MAX_TABLES)
                throw new InvalidOperationException("Generated Go world must have one spatial audio source per table.");
            for (int tableIndex = 0; tableIndex < GoBoardPool.MAX_TABLES; tableIndex++)
            {
                Transform table = tablePool.Find("PureUdonGo.Table." + tableIndex.ToString("000"));
                GoGame tableGame = table == null ? null : table.GetComponentInChildren<GoGame>(true);
                if (tableGame.audioSource == null || tableGame.placeClip == null ||
                    tableGame.captureClip == null || tableGame.passClip == null ||
                    tableGame.gameEndClip == null || tableGame.resetClip == null)
                    throw new InvalidOperationException("Generated Go spatial audio is incomplete at table " + tableIndex + ".");
            }
        }

        private static bool HasFixedAnalysisTemplate(string value)
        {
            if (value == null)
                return false;
            string[] lines = value.Replace("\r", "").Split('\n');
            if (lines.Length != 11)
                return false;
            string[] labels = new string[]
            {
                "AI STATE  ", "SEARCH  ", "BEST MOVE  ", "CANDIDATES  ",
                "WINRATE  B ", "SCORE LEAD W−B ", "WHITE OWNERSHIP MEAN ", "LEVEL  ",
                "MOVE  ", "STATIC CN SCORE W−B ", "COMPUTE  "
            };
            for (int i = 0; i < labels.Length; i++)
                if (!lines[i].StartsWith(labels[i], StringComparison.Ordinal))
                    return false;
            if (lines[9].IndexOf(" zi", StringComparison.Ordinal) < 0)
                return false;
            return true;
        }

        private static void VerifyControlActionSet(GoUI ui)
        {
            if(ui==null||ui.controlButtons==null||ui.controlButtonActions==null||
                ui.controlButtons.Length!=ui.controlButtonActions.Length)
                throw new InvalidOperationException("Generated Go control action bindings are incomplete.");
            int[] required={0,1,2,3,4,5,6,10,11,12,13,22,23,30,31,33,34,35,
                38,39,40,43,44,45,48,49,50,51,52,53,54};
            for(int i=0;i<ui.controlButtonActions.Length;i++)
            {
                int action=ui.controlButtonActions[i];
                if(action==46||action==47)
                    throw new InvalidOperationException("Obsolete append/remove table action is still generated: "+action);
                for(int j=i+1;j<ui.controlButtonActions.Length;j++)
                    if(ui.controlButtonActions[j]==action)
                        throw new InvalidOperationException("Duplicate generated control action: "+action);
            }
            for(int i=0;i<required.Length;i++)
            {
                bool found=false;
                for(int j=0;j<ui.controlButtonActions.Length;j++)
                    if(ui.controlButtonActions[j]==required[i]){found=true;break;}
                if(!found)throw new InvalidOperationException("Required generated control action is missing: "+required[i]);
            }
        }

        private static void WriteGenerationReport(Scene scene, GameObject root)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("Pure Udon Go · Generation Report");
            report.AppendLine("================================");
            report.AppendLine("Scene: " + ScenePath);
            report.AppendLine("Generator schema: " + GoGeneratedWorld.CURRENT_SCHEMA);
            report.AppendLine("Reference visual language: Pure Udon Xiangqi control-wall/table composition");
            report.AppendLine("Game content: Go only · 19x19 · black/white stones · Go rules");
            report.AppendLine("World scale: 60% · table height scale: 76% · compact indoor room");
            report.AppendLine("Visible tables: " + GoBoardPool.INITIAL_VISIBLE_TABLES +
                " fixed · serialized backing slots: " + GoBoardPool.MAX_TABLES);
            report.AppendLine("Table row spacing: " + TableRowSpacingWorld.ToString("0.00") +
                " m · room floor: " + GalleryFloorLengthWorld.ToString("0.0") +
                " × " + GalleryFloorDepthWorld.ToString("0.0") + " m");
            report.AppendLine("Board spacing: " + BoardSpacing.ToString("0.00") + " · origin scale: " + BoardWorldScale.ToString("0.00"));
            report.AppendLine("UI: world-space UGUI/TMP · 3 screens · 31 command buttons · one board UIShape trigger · one solid status-panel collider per table");
            report.AppendLine("Modes: PvP / PvAI / AIvP / AIvAI");
            report.AppendLine("Difficulty: Beginner 8 / Advanced 20 / Master 48 / Ultrahard 120 visits / Custom");
            report.AppendLine("Ultrahard maximum fixed visits: " + GoMctsSearch.MAX_SUPPORTED_VISITS);
            report.AppendLine("Controllers: independent Black/White PLAYER or AI · seat claim/release · swap sides");
            report.AppendLine("AI settings: one synchronized GoAiSettings domain · shared + independent Follow Shared/Custom visits");
            report.AppendLine("Starter: Black or White selectable before the first move · AI-AI supported");
            report.AppendLine("NN: KataGo-derived serialized weights + PureUdonGo/NNLayer shader");
            report.AppendLine("GPU readback: three fixed A/B/C channels per table; cancelled channels remain quarantined until callback");
            report.AppendLine("Audio: place / capture / pass / game-end / reset · spatial radius 2.2 m");
            report.AppendLine("Idempotence: owned root is repaired in place and rebuilt without duplicate generated children");
            report.AppendLine("Runtime marker: " + root.name + " · active scene: " + scene.path);
            File.WriteAllText(ReportPath, report.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static T EnsureUdon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T value = go.GetComponent<T>();
            return value != null ? value : go.AddUdonSharpComponent<T>();
        }

        private static void EnsureUdonProgramAssets()
        {
            EnsureUdonProgramAsset(typeof(GoGeneratedWorld));
            EnsureUdonProgramAsset(typeof(GoGame));
            EnsureUdonProgramAsset(typeof(GoBoardView));
            EnsureUdonProgramAsset(typeof(GoUI));
            EnsureUdonProgramAsset(typeof(GoTelemetry));
            EnsureUdonProgramAsset(typeof(GoAiSettings));
            EnsureUdonProgramAsset(typeof(GoDifficultyProfile));
            EnsureUdonProgramAsset(typeof(GoFeatureEncoder));
            EnsureUdonProgramAsset(typeof(GoSearchState));
            EnsureUdonProgramAsset(typeof(GoMctsSearch));
            EnsureUdonProgramAsset(typeof(GoGpuLayerExecutor));
            EnsureUdonProgramAsset(typeof(GoGpuNeuralRuntime));
            EnsureUdonProgramAsset(typeof(GoGpuNeuralOutputReader));
            EnsureUdonProgramAsset(typeof(GoAiController));
            EnsureUdonProgramAsset(typeof(GoBoardPool));
            EnsureUdonProgramAsset(typeof(GoBoardCell));
            EnsureUdonProgramAsset(typeof(GoBoardInput));
            EnsureUdonProgramAsset(typeof(GoUiButton));

            AssetDatabase.SaveAssets();
            ResetUdonSharpCaches();
        }

        private static void EnsureUdonProgramAsset(Type behaviourType)
        {
            // The package is normally imported as Assets/PureUdonGo, but the
            // production repository is also supported as a nested checkout
            // such as Assets/udon-go-ai/Assets/PureUdonGo.  In the latter
            // layout GoNestedCheckoutCompatibility stages only immutable
            // non-code artifacts into the project-level namespace; it must
            // not duplicate the UdonSharp source files.  Searching only the
            // project-level Runtime folder therefore makes valid source types
            // look missing during the one-click generator.
            string[] scriptGuids = AssetDatabase.FindAssets(
                "t:MonoScript", new[] { "Assets" });
            MonoScript sourceScript = null;
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                MonoScript candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (candidate != null && candidate.GetClass() == behaviourType)
                {
                    sourceScript = candidate;
                    break;
                }
            }

            if (sourceScript == null)
                throw new InvalidOperationException(
                    "Missing UdonSharp source script for " + behaviourType.FullName +
                    " under the current Unity Assets database.");

            string sourcePath = AssetDatabase.GetAssetPath(sourceScript).Replace('\\', '/');
            Debug.Log("PURE_UDON_GO_PROGRAM_SOURCE type=" + behaviourType.FullName +
                " path=" + sourcePath);
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
                Debug.Log("PURE_UDON_GO_PROGRAM_ASSET_CREATED type=" + behaviourType.FullName +
                    " path=" + programPath);
            }
            else
            {
                // A stale checkout can retain a program asset whose source
                // reference is null or points at a previous .cs.meta GUID.
                // UdonSharp's editor updater reports that state before the user
                // can enter PlayMode, so the one-click generator must repair it
                // instead of treating the stale reference as authoritative.
                if (programAsset.sourceCsScript != sourceScript)
                {
                    Debug.LogWarning("PURE_UDON_GO_PROGRAM_SOURCE_REPAIRED type=" +
                        behaviourType.FullName + " path=" + programPath);
                    programAsset.sourceCsScript = sourceScript;
                }
                programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                EditorUtility.SetDirty(programAsset);
            }
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

        private static void SyncUdonProxies(GameObject root)
        {
            UdonSharpBehaviour[] proxies =
                root.GetComponentsInChildren<UdonSharpBehaviour>(true);
            for (int i = 0; i < proxies.Length; i++)
            {
                if (proxies[i] != null)
                    UdonSharpEditorUtility.CopyProxyToUdon(
                        proxies[i], ProxySerializationPolicy.All);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
