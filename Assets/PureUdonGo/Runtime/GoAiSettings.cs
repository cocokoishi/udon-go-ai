using UdonSharp;
using UnityEngine;
using VRC.Udon.Common;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// The single authoritative AI-configuration domain for one Go table.
    /// Profiles remain local resolved views; this behaviour is the only object
    /// that serializes the shared/black/white user choices.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GoAiSettings : UdonSharpBehaviour
    {
        public const int BLACK=GoGame.BLACK;
        public const int WHITE=GoGame.WHITE;

        [UdonSynced] public int sharedPreset=GoDifficultyProfile.BEGINNER;
        [UdonSynced] public bool blackUseCustom;
        [UdonSynced] public int blackCustomVisits=GoDifficultyProfile.BEGINNER_VISITS;
        [UdonSynced] public bool whiteUseCustom;
        [UdonSynced] public int whiteCustomVisits=GoDifficultyProfile.BEGINNER_VISITS;
        [UdonSynced] public int configRevision;

        public GoGame game;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiController aiController;
        public GoUI ui;

        // Local diagnostics are ordinary program variables, not synchronized
        // fields; keeping them visible also lets ClientSim read the exact
        // Apply/serialization accounting without making them network state.
        public int applyCount;
        public int serializationCount;
        public int lastAppliedRevision;
        public bool lastApplySucceeded;
        public int lastSerializationByteCount;
        public bool lastSerializationSuccess;
        public int serializationFailureCount;

        private int previousConfigRevision=-1;

        public void Start()
        {
            EnsureProfileReferences();
            if(game!=null)
            {
                if(game.aiSettings==null)game.aiSettings=this;
            }
            ClampValues();
            ApplyResolvedProfiles();
            previousConfigRevision=configRevision;
            lastAppliedRevision=configRevision;
            if(IsLocalOwner()&&configRevision==0)
                RequestSerialization();
        }

        public override void OnDeserialization()
        {
            EnsureProfileReferences();
            ClampValues();
            ApplyResolvedProfiles();
            bool changed=configRevision!=previousConfigRevision;
            previousConfigRevision=configRevision;
            lastAppliedRevision=configRevision;
            if(game!=null)game.NotifyAISettingsApplied(configRevision);
            if(changed)
            {
                // Configuration snapshots affect the next SearchSession only.
                // Never invalidate the current root from this callback.
                if(aiController!=null)aiController.OnDifficultyDeserialized();
                if(ui!=null)ui.NotifyAppliedDifficultyChanged();
            }
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            lastSerializationByteCount=result.byteCount;
            lastSerializationSuccess=result.success;
            if(!result.success)serializationFailureCount++;
        }

        /// <summary>
        /// Commits all draft fields as one authority transaction. A successful
        /// call performs one config revision bump and one RequestSerialization.
        /// </summary>
        public bool ApplyDraft(int requestedSharedPreset,bool requestedBlackUseCustom,
            int requestedBlackCustomVisits,bool requestedWhiteUseCustom,
            int requestedWhiteCustomVisits)
        {
            return ApplyDraftInternal(requestedSharedPreset,requestedBlackUseCustom,
                requestedBlackCustomVisits,requestedWhiteUseCustom,
                requestedWhiteCustomVisits);
        }

        /// <summary>
        /// Three-integer transaction payload. Positive side visits select
        /// Custom; negative side visits select Follow Shared. This keeps the
        /// production path independent of cross-behaviour bool marshalling.
        /// </summary>
        public bool ApplyPackedVisits(int requestedSharedPreset,
            int requestedBlackVisits,int requestedWhiteVisits)
        {
            bool blackUseCustom=requestedBlackVisits>0;
            bool whiteUseCustom=requestedWhiteVisits>0;
            int blackVisits=requestedBlackVisits<0?-requestedBlackVisits:requestedBlackVisits;
            int whiteVisits=requestedWhiteVisits<0?-requestedWhiteVisits:requestedWhiteVisits;
            lastApplySucceeded=ApplyDraftInternal(requestedSharedPreset,
                blackUseCustom,blackVisits,whiteUseCustom,whiteVisits);
            return lastApplySucceeded;
        }

        private bool ApplyDraftInternal(int requestedSharedPreset,bool requestedBlackUseCustom,
            int requestedBlackCustomVisits,bool requestedWhiteUseCustom,
            int requestedWhiteCustomVisits)
        {
            // The UI disables controls for spectators, but the synchronized
            // domain must enforce the same boundary for direct/public Udon
            // calls as well. Ownership alone is not a match-control grant.
            if(game!=null&&!game.CanLocalMatchControl())
            {
                lastApplySucceeded=false;
                return false;
            }
            if(!TakeOwnership())
            {
                lastApplySucceeded=false;
                return false;
            }
            int clampedShared=ClampPreset(requestedSharedPreset);
            int clampedBlack=Mathf.Clamp(requestedBlackCustomVisits,1,
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            int clampedWhite=Mathf.Clamp(requestedWhiteCustomVisits,1,
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            bool changed=sharedPreset!=clampedShared||
                blackUseCustom!=requestedBlackUseCustom||
                blackCustomVisits!=clampedBlack||
                whiteUseCustom!=requestedWhiteUseCustom||
                whiteCustomVisits!=clampedWhite;
            sharedPreset=clampedShared;
            blackUseCustom=requestedBlackUseCustom;
            blackCustomVisits=clampedBlack;
            whiteUseCustom=requestedWhiteUseCustom;
            whiteCustomVisits=clampedWhite;
            ClampValues();
            if(changed)
            {
                configRevision++;
                if(configRevision==0)configRevision=1;
            }
            previousConfigRevision=configRevision;
            lastAppliedRevision=configRevision;
            applyCount++;
            ApplyResolvedProfiles();
            if(game!=null)game.NotifyAISettingsApplied(configRevision);
            if(changed)
            {
                RequestSerialization();
                serializationCount++;
            }
            lastApplySucceeded=true;
            if(aiController!=null)aiController.OnDifficultyChanged();
            if(ui!=null)ui.NotifyAppliedDifficultyChanged();
            return true;
        }

        public int GetEffectivePreset(int side)
        {
            if(side==BLACK&&blackUseCustom)return GoDifficultyProfile.CUSTOM;
            if(side==WHITE&&whiteUseCustom)return GoDifficultyProfile.CUSTOM;
            return ClampPreset(sharedPreset);
        }

        public int GetEffectiveVisits(int side)
        {
            if(side==BLACK&&blackUseCustom)return Mathf.Clamp(blackCustomVisits,1,
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            if(side==WHITE&&whiteUseCustom)return Mathf.Clamp(whiteCustomVisits,1,
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            return PresetVisits(ClampPreset(sharedPreset));
        }

        public bool UsesCustom(int side)
        {
            return side==BLACK?blackUseCustom:side==WHITE&&whiteUseCustom;
        }

        public int GetConfigRevision(){return configRevision;}

        public void RegisterProfiles(GoDifficultyProfile black,GoDifficultyProfile white)
        {
            blackDifficulty=black;whiteDifficulty=white;
            if(blackDifficulty!=null)
            {
                blackDifficulty.settings=this;
                blackDifficulty.settingsSide=GoGame.BLACK;
                blackDifficulty.settingsMirror=true;
            }
            if(whiteDifficulty!=null)
            {
                whiteDifficulty.settings=this;
                whiteDifficulty.settingsSide=GoGame.WHITE;
                whiteDifficulty.settingsMirror=true;
            }
            ApplyResolvedProfiles();
        }

        private void ApplyResolvedProfiles()
        {
            EnsureProfileReferences();
            // CUSTOM is a per-side mode, not a base preset.  Its complete
            // resolved profile is determined by that side's visits; the
            // synchronized shared preset resolves only Follow Shared sides.
            if(blackDifficulty!=null)
            {
                blackDifficulty.SendCustomEvent("ResolveFromSettings");
            }
            if(whiteDifficulty!=null)
            {
                whiteDifficulty.SendCustomEvent("ResolveFromSettings");
            }
        }

        /// <summary>
        /// Udon Start order is not guaranteed across sibling behaviours. Keep
        /// the settings domain authoritative even when its serialized profile
        /// references are temporarily unset during a generated-scene load.
        /// This is idempotent and never replaces an explicitly registered
        /// profile, so it is safe to call before every deterministic resolve.
        /// </summary>
        private void EnsureProfileReferences()
        {
            if(game==null)return;
            if(blackDifficulty==null)blackDifficulty=game.blackDifficulty;
            if(whiteDifficulty==null)whiteDifficulty=game.whiteDifficulty;
            if(blackDifficulty!=null)
            {
                blackDifficulty.settings=this;
                blackDifficulty.settingsSide=BLACK;
                blackDifficulty.settingsMirror=true;
            }
            if(whiteDifficulty!=null)
            {
                whiteDifficulty.settings=this;
                whiteDifficulty.settingsSide=WHITE;
                whiteDifficulty.settingsMirror=true;
            }
        }

        private int PresetVisits(int preset)
        {
            if(preset==GoDifficultyProfile.ADVANCED)return GoDifficultyProfile.ADVANCED_VISITS;
            if(preset==GoDifficultyProfile.MASTER)return GoDifficultyProfile.MASTER_VISITS;
            if(preset==GoDifficultyProfile.ULTRAHARD)return GoDifficultyProfile.ULTRAHARD_VISITS;
            return GoDifficultyProfile.BEGINNER_VISITS;
        }

        private int ClampPreset(int value)
        {
            return value<GoDifficultyProfile.BEGINNER||value>GoDifficultyProfile.ULTRAHARD
                ?GoDifficultyProfile.BEGINNER:value;
        }

        private void ClampValues()
        {
            sharedPreset=ClampPreset(sharedPreset);
            blackCustomVisits=Mathf.Clamp(blackCustomVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            whiteCustomVisits=Mathf.Clamp(whiteCustomVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            if(configRevision<0)configRevision=0;
        }

        private bool TakeOwnership()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            if(!Utilities.IsValid(local))return true;
            if(!Networking.IsOwner(gameObject))Networking.SetOwner(local,gameObject);
            return Networking.IsOwner(gameObject);
        }

        private bool IsLocalOwner()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            return !Utilities.IsValid(local)||Networking.IsOwner(gameObject);
        }
    }
}
