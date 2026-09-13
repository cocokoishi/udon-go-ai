using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Runtime-only ClientSim probe for the generated gallery floor. The
    /// editor harness drops the local player above the actual generated
    /// collider, then this compiled Udon program observes gravity, collision,
    /// and grounded state. It is intentionally not wired into production.
    /// </summary>
    public sealed class GoFloorGravityProbe : UdonSharpBehaviour
    {
        public Vector3 testPosition;
        public float floorY;
        public float settledTolerance = 0.08f;
        public float floorCrossingTolerance = 0.08f;
        public float minimumDrop = 0.25f;
        public int requiredStableFrames = 12;
        public int maxFrames = 240;

        public bool probeFinished;
        public bool probePassed;
        public bool groundedAtEnd;
        public bool sawDownwardMotion;
        public bool crossedFloor;
        public int stableFrames;
        public int sampleFrames;
        public float startY;
        public float finalY;
        public float minY;
        public float finalVelocityY;
        public string failure;

        private bool running;
        private int stableFrameCount;

        public void RunFloorProbe()
        {
            probeFinished = false;
            probePassed = false;
            groundedAtEnd = false;
            sawDownwardMotion = false;
            crossedFloor = false;
            stableFrames = 0;
            sampleFrames = 0;
            startY = testPosition.y;
            finalY = testPosition.y;
            minY = testPosition.y;
            finalVelocityY = 0f;
            failure = "";
            stableFrameCount = 0;

            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                Finish("ClientSim local player was not ready");
                return;
            }

            player.TeleportTo(testPosition, Quaternion.identity);
            player.SetVelocity(Vector3.zero);
            running = true;
        }

        public void Update()
        {
            if (!running || probeFinished)
                return;

            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                Finish("ClientSim local player disappeared during gravity probe");
                return;
            }

            Vector3 position = player.GetPosition();
            Vector3 velocity = player.GetVelocity();
            sampleFrames++;
            finalY = position.y;
            finalVelocityY = velocity.y;
            if (position.y < minY)
                minY = position.y;
            if (position.y <= startY - minimumDrop)
                sawDownwardMotion = true;

            if (position.y < floorY - floorCrossingTolerance)
            {
                crossedFloor = true;
                Finish("player crossed below the generated floor");
                return;
            }

            groundedAtEnd = player.IsPlayerGrounded();
            bool settled = groundedAtEnd &&
                Mathf.Abs(position.y - floorY) <= settledTolerance;
            if (settled)
                stableFrameCount++;
            else
                stableFrameCount = 0;
            stableFrames = stableFrameCount;

            if (sawDownwardMotion && stableFrameCount >= requiredStableFrames)
            {
                Finish("");
                return;
            }

            if (sampleFrames >= maxFrames)
            {
                Finish("player did not settle on the generated floor");
            }
        }

        private void Finish(string error)
        {
            running = false;
            probePassed = string.IsNullOrEmpty(error) && sawDownwardMotion &&
                groundedAtEnd && !crossedFloor;
            failure = error;
            probeFinished = true;
        }
    }
}
