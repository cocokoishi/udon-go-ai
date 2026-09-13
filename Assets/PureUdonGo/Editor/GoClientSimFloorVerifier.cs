#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Runs the generated production scene in real ClientSim and verifies
    /// locomotion against its actual gallery floor. Structural collider checks
    /// remain in GoProductionValidator; this test proves that the simulated
    /// player falls, collides, and stays grounded instead of merely finding a
    /// decorative MeshCollider in the scene.
    /// </summary>
    public static class GoClientSimFloorVerifier
    {
        private const double TimeoutSeconds = 30.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimFloorVerifier.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;

            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Floor Gravity")]
        public static void VerifyClientSimFloorGravity()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ScenePath);

            GoGeneratedWorld rootMarker = UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            VRCSceneDescriptor descriptor = rootMarker == null ? null :
                rootMarker.GetComponent<VRCSceneDescriptor>();
            Transform floor = rootMarker == null ? null : rootMarker.transform.Find(
                "PureUdonGo.Environment · Indoor Room/Indoor Room Floor · Walkable");
            Collider floorCollider = floor == null ? null : floor.GetComponent<Collider>();
            if (descriptor == null || descriptor.spawns == null ||
                descriptor.spawns.Length == 0 || descriptor.spawns[0] == null ||
                floorCollider == null || !floorCollider.enabled || floorCollider.isTrigger)
                throw new InvalidOperationException(
                    "Generated scene is missing a valid spawn or walkable floor collider");

            string probeProgramPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoFloorGravityProbe), "Floor-gravity");
            GameObject probeObject = new GameObject("ClientSim Floor Gravity Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoFloorGravityProbe probe = probeObject.AddUdonSharpComponent<GoFloorGravityProbe>();
            float floorY = floorCollider.bounds.max.y;
            Vector3 spawn = descriptor.spawns[0].position;
            probe.floorY = floorY;
            probe.testPosition = new Vector3(spawn.x, floorY + 2.0f, spawn.z);
            probe.settledTolerance = 0.08f;
            probe.floorCrossingTolerance = 0.08f;
            probe.minimumDrop = 0.25f;
            probe.requiredStableFrames = 12;
            probe.maxFrames = 240;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe, probeProgramPath);

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " floorY=" + floorY +
                " testY=" + probe.testPosition.y + " timeoutSeconds=" + TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            probeSent = false;
            probeProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before floor gravity verification completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }

                if (!LocalPlayerReady())
                {
                    FailIfTimedOut("ClientSim local player was not ready");
                    return;
                }

                if (probeProgram == null)
                {
                    GoFloorGravityProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoFloorGravityProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon floor probe was not found");
                    return;
                }

                if (!probeSent)
                {
                    probeProgram.SendCustomEvent("RunFloorProbe");
                    probeSent = true;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_EVENT name=RunFloorProbe");
                    return;
                }

                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon floor gravity probe did not finish");
                    return;
                }

                bool passed = ReadBool("probePassed");
                float startY = ReadFloat("startY");
                float finalY = ReadFloat("finalY");
                float minY = ReadFloat("minY");
                float floorY = ReadFloat("floorY");
                float finalVelocityY = ReadFloat("finalVelocityY");
                int stableFrames = ReadInt("stableFrames");
                int sampleFrames = ReadInt("sampleFrames");
                bool grounded = ReadBool("groundedAtEnd");
                bool sawDrop = ReadBool("sawDownwardMotion");
                bool crossedFloor = ReadBool("crossedFloor");
                string failure = ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_RESULT pass=" + passed +
                    " startY=" + startY + " finalY=" + finalY + " minY=" + minY +
                    " floorY=" + floorY + " grounded=" + grounded +
                    " sawDrop=" + sawDrop + " crossedFloor=" + crossedFloor +
                    " stableFrames=" + stableFrames + " sampleFrames=" + sampleFrames +
                    " finalVelocityY=" + finalVelocityY + " failure=" + failure);
                if (!passed)
                {
                    Fail("Udon floor gravity probe failed: " + failure);
                    return;
                }

                Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_PASS");
                StopWithResult(true);
            }
            catch (Exception exception)
            {
                Fail("ClientSim floor gravity inspection exception: " + exception);
            }
        }

        private static bool LocalPlayerReady()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.playerId > 0;
        }

        private static int ReadInt(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadFloat(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static bool ReadBool(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static string ReadString(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? "" : Convert.ToString(value);
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_FLOOR_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FLOOR_FINAL pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
