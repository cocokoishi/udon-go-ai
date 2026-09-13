#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class GoDifficultyProfileCheck
{
    private static readonly int[] Presets =
    {
        PureUdonGo.GoDifficultyProfile.BEGINNER,
        PureUdonGo.GoDifficultyProfile.ADVANCED,
        PureUdonGo.GoDifficultyProfile.MASTER,
        PureUdonGo.GoDifficultyProfile.ULTRAHARD
    };

    private static readonly string[] Names = { "Beginner", "Advanced", "Master", "Ultrahard" };
    // The Chinese UI intentionally keeps the product name "Ultrahard" direct;
    // do not regress to the legacy composite alias.
    private static readonly string[] LocalizedNames = { "入门", "进阶", "大师", "Ultrahard" };
    private static readonly int[] ExpectedVisits = { 4, 32, 128, 384 };
    private static readonly int[] ExpectedTransitions = { 1, 2, 4, 8 };
    private static readonly int[] ExpectedTopK = { 24, 64, 128, PureUdonGo.GoGame.AREA + 1 };
    private static readonly float[] ExpectedCpuct = { 1.85f, 1.60f, 1.35f, 1.25f };
    private static readonly float[] ExpectedTemperature = { 1.15f, 0.85f, 0.30f, 0.10f };

    public static void Run()
    {
        GameObject profileObject = null;
        int failures = 0;
        try
        {
            profileObject = new GameObject("GoDifficultyProfileCheck");
            PureUdonGo.GoDifficultyProfile profile =
                profileObject.AddComponent<PureUdonGo.GoDifficultyProfile>();
            for (int i = 0; i < Presets.Length; i++)
            {
                profile.ApplyPreset(Presets[i]);
                bool ok = profile.GetPresetLabel() == Names[i] &&
                    profile.GetPresetLabelLocalized(false) == LocalizedNames[i] &&
                    profile.maxVisits == ExpectedVisits[i] &&
                    profile.maxNNQueries == ExpectedVisits[i] &&
                    profile.maxTransitionsPerFrame == ExpectedTransitions[i] &&
                    profile.policyTopK == ExpectedTopK[i] &&
                    Mathf.Abs(profile.cpuct - ExpectedCpuct[i]) < 0.0001f &&
                    Mathf.Abs(profile.moveTemperature - ExpectedTemperature[i]) < 0.0001f;
                if (!ok)
                {
                    failures++;
                    Debug.LogError("PURE_UDON_GO_DIFFICULTY_PROFILE_FAIL preset=" + Names[i] +
                        " label=" + profile.GetPresetLabel() + " visits=" + profile.maxVisits +
                        " nnQueries=" + profile.maxNNQueries + " transitions=" + profile.maxTransitionsPerFrame +
                        " cpuct=" + profile.cpuct + " temperature=" + profile.moveTemperature +
                        " topK=" + profile.policyTopK);
                }
                else
                {
                    Debug.Log("PURE_UDON_GO_DIFFICULTY_PROFILE_METRICS preset=" + Names[i] +
                        " visits=" + profile.maxVisits + " nnQueries=" + profile.maxNNQueries +
                        " transitions=" + profile.maxTransitionsPerFrame + " cpuct=" + profile.cpuct +
                        " temperature=" + profile.moveTemperature + " topK=" + profile.policyTopK);
                }
            }
            if (PureUdonGo.GoMctsSearch.MAX_SUPPORTED_VISITS < ExpectedVisits[ExpectedVisits.Length - 1])
            {
                failures++;
                Debug.LogError("PURE_UDON_GO_DIFFICULTY_PROFILE_FAIL fixed capacity below Ultrahard visits");
            }
            if (failures == 0)
            {
                Debug.Log("PURE_UDON_GO_DIFFICULTY_PROFILE_PASS failures=0 presets=4");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("PURE_UDON_GO_DIFFICULTY_PROFILE_FAIL failures=" + failures);
                EditorApplication.Exit(6);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("PURE_UDON_GO_DIFFICULTY_PROFILE_FAIL exception=" + exception);
            EditorApplication.Exit(6);
        }
        finally
        {
            if (profileObject != null)
                UnityEngine.Object.DestroyImmediate(profileObject);
        }
    }
}
#endif
