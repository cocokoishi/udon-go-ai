using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Go-native resolved AI settings. The search controller consumes
    /// maxVisits/cpuct directly. The synchronized choice lives in
    /// GoAiSettings; this component is a local, per-side resolver view kept
    /// for stable search and probe references.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GoDifficultyProfile : UdonSharpBehaviour
    {
        public const int BEGINNER=0;
        public const int ADVANCED=1;
        public const int MASTER=2;
        public const int ULTRAHARD=3;
        public const int CUSTOM=4;
        // Public product presets.  Keep these values in one place so the
        // runtime, generated UI and ClientSim gates cannot drift apart.
        public const int BEGINNER_VISITS=8;
        public const int ADVANCED_VISITS=20;
        public const int MASTER_VISITS=48;
        public const int ULTRAHARD_VISITS=120;

        // These are deliberately local resolved values. GoAiSettings is the
        // sole synchronized configuration domain for a table.
        public int preset=BEGINNER;
        public int maxVisits=BEGINNER_VISITS;
        public int maxNNQueries=BEGINNER_VISITS;
        public int maxTransitionsPerFrame=1;
        public float cpuct=1.85f;
        public float moveTemperature=0.10f;
        public int policyTopK=24;
        public float resignThreshold=-1f;
        public int settingsRevision;
        public GoAiController aiController;
        public GoAiSettings settings;
        public int settingsSide=GoGame.WHITE;
        public bool settingsMirror;
        public bool followsShared=true;
        private int previousSettingsRevision;

        public void Start()
        {
            previousSettingsRevision=settingsRevision;
            ClampValues();
        }

        public override void OnDeserialization()
        {
            if(settings!=null&&settingsMirror)return;
            ClampValues();
            if(settingsRevision!=previousSettingsRevision)
            {
                previousSettingsRevision=settingsRevision;
                // Configuration is independent from the board position. Tell
                // presentation about the next-search configuration without
                // invalidating a search already in flight.
                if(aiController!=null)aiController.OnDifficultyDeserialized();
            }
        }

        /// <summary>Applies a resolved profile without any network mutation.</summary>
        public void ApplyResolvedLocal(int requestedPreset,int requestedVisits,
            int revision,bool useCustom)
        {
            requestedPreset=ClampPublicPreset(requestedPreset);
            if(useCustom)ApplyCustomConstantsLocal(requestedVisits);
            else ApplyPresetConstantsLocal(requestedPreset);
            followsShared=!useCustom;
            preset=useCustom?CUSTOM:requestedPreset;
            settingsRevision=revision;
            previousSettingsRevision=settingsRevision;
        }

        /// <summary>
        /// Udon-safe resolution entry point used by GoAiSettings.  Passing a
        /// four-argument method call across behaviours is not reliable on all
        /// ClientSim/Udon VM versions, so the profile resolves from its
        /// already-serialized settings reference through a parameterless
        /// event.  GoAiSettings remains the sole authority; this only updates
        /// the local view consumed by search and telemetry.
        /// </summary>
        public void ResolveFromSettings()
        {
            if(settings==null)return;
            int basePreset=settings.sharedPreset;
            if(basePreset<BEGINNER||basePreset>ULTRAHARD)basePreset=BEGINNER;
            bool useCustom=settingsSide==GoGame.BLACK?
                settings.blackUseCustom:settings.whiteUseCustom;
            int visits=settingsSide==GoGame.BLACK?
                settings.blackCustomVisits:settings.whiteCustomVisits;
            if(!useCustom)
            {
                if(basePreset==ADVANCED)visits=ADVANCED_VISITS;
                else if(basePreset==MASTER)visits=MASTER_VISITS;
                else if(basePreset==ULTRAHARD)visits=ULTRAHARD_VISITS;
                else visits=BEGINNER_VISITS;
            }
            ApplyResolvedLocal(basePreset,visits,settings.configRevision,useCustom);
        }

        /// <summary>
        /// Resolves every effective search constant for one public preset.
        /// Custom visits use ApplyCustomConstantsLocal instead, so their
        /// visits-derived constants cannot inherit a shared preset.
        /// </summary>
        private void ApplyPresetConstantsLocal(int basePreset)
        {
            if(basePreset==BEGINNER)
            {
                maxVisits=BEGINNER_VISITS;maxNNQueries=BEGINNER_VISITS;
                maxTransitionsPerFrame=1;cpuct=1.85f;moveTemperature=0.10f;
                policyTopK=24;resignThreshold=-1f;
            }
            else if(basePreset==ADVANCED)
            {
                maxVisits=ADVANCED_VISITS;maxNNQueries=ADVANCED_VISITS;
                maxTransitionsPerFrame=2;cpuct=1.60f;moveTemperature=0.10f;
                policyTopK=64;resignThreshold=-1f;
            }
            else if(basePreset==MASTER)
            {
                maxVisits=MASTER_VISITS;maxNNQueries=MASTER_VISITS;
                maxTransitionsPerFrame=4;cpuct=1.35f;moveTemperature=0.10f;
                policyTopK=128;resignThreshold=-0.98f;
            }
            else
            {
                maxVisits=ULTRAHARD_VISITS;maxNNQueries=ULTRAHARD_VISITS;
                maxTransitionsPerFrame=8;cpuct=1.25f;moveTemperature=0.10f;
                policyTopK=GoGame.AREA+1;resignThreshold=-0.95f;
            }
        }

        /// <summary>
        /// Resolves the visits-only Custom mode from the four calibrated public
        /// anchors.  The transition budget uses the same anchors, while the
        /// controller/scheduler consuming it remains unchanged.  Search
        /// constants between anchors use logarithmic visits interpolation so a
        /// Custom value never silently inherits a neighbouring preset or stale
        /// local state.
        /// </summary>
        private void ApplyCustomConstantsLocal(int requestedVisits)
        {
            int visits=Mathf.Clamp(requestedVisits,1,
                GoMctsSearch.MAX_SUPPORTED_VISITS);
            maxVisits=visits;
            maxNNQueries=maxVisits;
            moveTemperature=0.10f;

            if(visits<=BEGINNER_VISITS)
            {
                maxTransitionsPerFrame=1;
                cpuct=1.85f;
                policyTopK=24;
                resignThreshold=-1f;
                return;
            }

            if(visits<ADVANCED_VISITS)
            {
                float t=Mathf.Log((float)visits/(float)BEGINNER_VISITS)/
                    Mathf.Log((float)ADVANCED_VISITS/(float)BEGINNER_VISITS);
                maxTransitionsPerFrame=Mathf.RoundToInt(Mathf.Lerp(1f,2f,t));
                cpuct=Mathf.Lerp(1.85f,1.60f,t);
                policyTopK=Mathf.RoundToInt(Mathf.Lerp(24f,64f,t));
                resignThreshold=Mathf.Lerp(-1f,-1f,t);
                return;
            }

            if(visits==ADVANCED_VISITS)
            {
                maxTransitionsPerFrame=2;
                cpuct=1.60f;
                policyTopK=64;
                resignThreshold=-1f;
                return;
            }

            if(visits<MASTER_VISITS)
            {
                float t=Mathf.Log((float)visits/(float)ADVANCED_VISITS)/
                    Mathf.Log((float)MASTER_VISITS/(float)ADVANCED_VISITS);
                maxTransitionsPerFrame=Mathf.RoundToInt(Mathf.Lerp(2f,4f,t));
                cpuct=Mathf.Lerp(1.60f,1.35f,t);
                policyTopK=Mathf.RoundToInt(Mathf.Lerp(64f,128f,t));
                resignThreshold=Mathf.Lerp(-1f,-0.98f,t);
                return;
            }

            if(visits==MASTER_VISITS)
            {
                maxTransitionsPerFrame=4;
                cpuct=1.35f;
                policyTopK=128;
                resignThreshold=-0.98f;
                return;
            }

            if(visits<ULTRAHARD_VISITS)
            {
                float t=Mathf.Log((float)visits/(float)MASTER_VISITS)/
                    Mathf.Log((float)ULTRAHARD_VISITS/(float)MASTER_VISITS);
                maxTransitionsPerFrame=Mathf.RoundToInt(Mathf.Lerp(4f,8f,t));
                cpuct=Mathf.Lerp(1.35f,1.25f,t);
                policyTopK=Mathf.RoundToInt(Mathf.Lerp(128f,362f,t));
                resignThreshold=Mathf.Lerp(-0.98f,-0.95f,t);
                return;
            }

            maxTransitionsPerFrame=8;
            cpuct=1.25f;
            policyTopK=GoGame.AREA+1;
            resignThreshold=-0.95f;
        }

        private int ClampPublicPreset(int value)
        {
            return value<BEGINNER||value>ULTRAHARD?BEGINNER:value;
        }

        public string GetPresetLabel()
        {
            if(preset==BEGINNER)return "Beginner";
            if(preset==ADVANCED)return "Advanced";
            if(preset==MASTER)return "Master";
            if(preset==ULTRAHARD)return "Ultrahard";
            return "Custom";
        }

        public string GetPresetLabelLocalized(bool english)
        {
            if(english)return GetPresetLabel();
            if(preset==BEGINNER)return "入门";
            if(preset==ADVANCED)return "进阶";
            if(preset==MASTER)return "大师";
            if(preset==ULTRAHARD)return "Ultrahard";
            return "自定义";
        }

        private void ClampValues()
        {
            if(preset<BEGINNER||preset>CUSTOM)preset=BEGINNER;
            maxVisits=Mathf.Clamp(maxVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            maxNNQueries=Mathf.Clamp(maxNNQueries,1,maxVisits);
            maxTransitionsPerFrame=Mathf.Clamp(maxTransitionsPerFrame,1,8);
            cpuct=Mathf.Max(0.01f,cpuct);moveTemperature=Mathf.Max(0.01f,moveTemperature);policyTopK=Mathf.Clamp(policyTopK,1,GoGame.AREA+1);
        }

    }
}
