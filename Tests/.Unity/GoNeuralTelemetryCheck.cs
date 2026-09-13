#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

public static class GoNeuralTelemetryCheck
{
    private static int failures;
    private static int frames;
    private static PureUdonGo.GoGame game;
    private static PureUdonGo.GoAiController ai;
    private static PureUdonGo.GoTelemetry telemetry;
    private static PureUdonGo.GoGpuNeuralRuntime runtime;

    public static void Run()
    {
        failures = 0;
        frames = 0;
        try
        {
            Networking.LocalPlayer = MakeOwner();
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;
            EditorSceneManager.OpenScene(PureUdonGo.Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            GameObject root = FindGeneratedRoot();
            Expect(root != null, "generated root");
            if (root == null)
            {
                Finish();
                return;
            }

            game = root.GetComponentInChildren<PureUdonGo.GoGame>(true);
            ai = root.GetComponentInChildren<PureUdonGo.GoAiController>(true);
            telemetry = root.GetComponentInChildren<PureUdonGo.GoTelemetry>(true);
            runtime = root.GetComponentInChildren<PureUdonGo.GoGpuNeuralRuntime>(true);
            Expect(game != null && ai != null && telemetry != null && runtime != null,
                "generated neural telemetry references");
            if (game == null || ai == null || telemetry == null || runtime == null)
            {
                Finish();
                return;
            }

            game.Start();
            if (game.whiteDifficulty != null)
                game.whiteDifficulty.Start();
            if (game.blackDifficulty != null && game.blackDifficulty != game.whiteDifficulty)
                game.blackDifficulty.Start();
            ai.Start();
            game.RequestStartMatch();
            Expect(game.TryPlay(0), "seed black move");
            if (game.whiteDifficulty != null)
                game.whiteDifficulty.ApplyPreset(PureUdonGo.GoDifficultyProfile.BEGINNER);
            ai.SyncControllerMirrorFromGame();
            EditorApplication.update += Pump;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_NEURAL_TELEMETRY_FAIL exception=" + exception);
            Finish();
        }
    }

    private static void Pump()
    {
        frames++;
        ai.Tick();
        EditorApplication.QueuePlayerLoopUpdate();
        if (ai.aiMoves >= 1)
        {
            float score = ai.search.rootNeuralScoreMean;
            float stdev = ai.search.rootNeuralScoreStdev;
            float lead = ai.search.rootNeuralLead;
            float ownership = ai.search.rootNeuralOwnershipMean;
            Expect(ai.search.rootNeuralOutputRevision > 0, "root neural revision");
            Expect(!float.IsNaN(score) && !float.IsInfinity(score), "score finite");
            Expect(!float.IsNaN(stdev) && !float.IsInfinity(stdev), "score stdev finite");
            Expect(!float.IsNaN(lead) && !float.IsInfinity(lead), "lead finite");
            Expect(!float.IsNaN(ownership) && !float.IsInfinity(ownership), "ownership finite");
            telemetry.RefreshNow();
            string telemetryValue = telemetry.panelText == null ? "" : telemetry.panelText.text;
            Expect(telemetryValue.IndexOf("NN SCORE", StringComparison.Ordinal) >= 0,
                "telemetry panel consumes neural score");
            Debug.Log("PURE_UDON_GO_NEURAL_TELEMETRY_METRICS frames=" + frames +
                " visits=" + ai.search.visitsCompleted +
                " score=" + score + " stdev=" + stdev + " lead=" + lead +
                " ownership=" + ownership + " revision=" + ai.search.rootNeuralOutputRevision);
            Finish();
            return;
        }
        if (ai.controllerState == PureUdonGo.GoAiController.STATE_ERROR)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_NEURAL_TELEMETRY_FAIL controller=" + ai.lastError);
            Finish();
            return;
        }
        if (frames > 20000)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_NEURAL_TELEMETRY_FAIL timeout phase=" + ai.search.phase);
            Finish();
        }
    }

    private static GameObject FindGeneratedRoot()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].GetComponent<PureUdonGo.GoGeneratedWorld>() != null)
                return roots[i];
        return null;
    }

    private static VRCPlayerApi MakeOwner()
    {
        VRCPlayerApi owner = new VRCPlayerApi();
        owner.playerId = 1;
        owner.displayName = "Neural Telemetry Owner";
        owner.isLocal = true;
        return owner;
    }

    private static void Expect(bool condition, string message)
    {
        if (condition)
            return;
        failures++;
        Debug.LogError("PURE_UDON_GO_NEURAL_TELEMETRY_FAIL " + message);
    }

    private static void Finish()
    {
        EditorApplication.update -= Pump;
        if (runtime != null)
            runtime.ReleaseAllResources();
        Networking.LocalPlayer = null;
        Networking.LocalPlayerOwnsObjects = true;
        Networking.IsMaster = true;
        if (failures == 0)
        {
            Debug.Log("PURE_UDON_GO_NEURAL_TELEMETRY_PASS failures=0");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("PURE_UDON_GO_NEURAL_TELEMETRY_FAIL count=" + failures);
            EditorApplication.Exit(6);
        }
    }
}
#endif
