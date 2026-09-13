# UI and AI settings implementation contract

This file describes the exact schema-14 settings model. The implementation order
is retained in `IMPLEMENTATION_PLAN.md`.

## 1. Visible product UI

The generated room has three functional areas:

1. match/actions;
2. current-position analysis;
3. difficulty/search settings.

The center analysis uses real search state and real
`visitsCompleted / targetVisits` progress. Do not add decorative/fake analysis
cards and do not restore `推荐算法 / KataGo（简化版）`.

The center analysis text is rendered by `GoTelemetry`. `GoUI.ToggleLanguage` and
`OnLanguageChanged` explicitly refresh that renderer, so the live and persisted
analysis card changes language immediately without waiting for a new search or
network revision. The card is a fixed eleven-line ABI in both languages; rows are
never added or removed when a search starts, produces output, completes, or
serves an AI Hint/Immediate Estimate. Missing values are rendered as `—`.

The fixed rows are:

```text
AI STATE / AI 状态
SEARCH / 搜索
BEST MOVE / 推荐着法
CANDIDATES / 候选着法
WINRATE / 胜率
SCORE LEAD W−B / 预计分差 白−黑
WHITE OWNERSHIP MEAN / 白方领地均值
LEVEL / 档位
MOVE / 手数
EST. CHINESE SCORE W−B / 预计子差（中国） 白−黑
COMPUTE / 计算端
```

The generated telemetry body uses a 28 px base size as the requested final size
(down from 32 px), so all eleven rows fit the existing center card. The title, status, progress
label and buttons retain their existing sizes.

The live `SCORE LEAD W−B` and `EST. CHINESE SCORE W−B` rows use the current
root NN score lead only; they never use a leaf output or a mid-game board
flood-fill. A score-based terminal uses the synchronized `finalWhiteMinusBlackScore`
and divides it by two exactly once. Resignation and agreed draw show `—` in the
numeric terminal row. Chinese handicap compensation is ordinary komi plus the
handicap count, while `komiTimes2` remains the raw synchronized komi field.

The public strongest label is exactly `Ultrahard`.

The settings wall shows a live `Applied Profile Summary` line directly below
`应用档位 · 同步` / `APPLY PROFILE · SYNC`. It is derived from the synchronized
shared preset and resolved side profiles, for example `APPLIED · SHARED
Advanced · BLACK PLAYER · WHITE Advanced 20 visits`; it never reports an
uncommitted local draft as active.

The red Force Reset control uses a local four-second double-press confirmation.
The game authority rechecks permission on the second press: a live turn is
limited to the instance/table owner or the human side currently to move. A
finished/pre-match table is recoverable by any participant, and a synchronized
server-time stall timer opens the same recovery permission to everyone after
50 seconds without a move. No automatic reset is performed.

## 2. Public search presets

```text
Beginner      visits=8   NN=8   transitions=1  cpuct=1.85 temp=0.10 topK=24  resign=-1
Advanced      visits=20  NN=20  transitions=2  cpuct=1.60 temp=0.10 topK=64  resign=-1
Master        visits=48  NN=48  transitions=4  cpuct=1.35 temp=0.10 topK=128 resign=-0.98
Ultrahard     visits=120 NN=120 transitions=8  cpuct=1.25 temp=0.10 topK=362 resign=-0.95
```

The constants in `GoDifficultyProfile` are executable truth. One helper must
assign the complete preset every time a profile is resolved.

## 3. Settings state

`GoAiSettings` is the only synchronized user-choice domain:

```text
sharedPreset
blackUseCustom
blackCustomVisits
whiteUseCustom
whiteCustomVisits
configRevision
```

The final `GoUI` local draft is exactly the same five choices plus dirty/source
revision state:

```text
sharedPresetDraft
blackUseCustomDraft
blackCustomVisitsDraft
whiteUseCustomDraft
whiteCustomVisitsDraft
settingsDraftDirty
draftSourceSettingsRevision
```

There is no selected-side settings editor in the target architecture because
Black and White cards are visible simultaneously.

## 4. Follow Shared and Custom semantics

Custom is **custom visits only**.

For either side:

```text
base = complete Parameters(sharedPreset)

Follow Shared:
    resolved = base

Custom:
    resolved = base
    resolved.maxVisits = sideCustomVisits
    resolved.maxNNQueries = sideCustomVisits
```

Therefore Custom always inherits these fields from `sharedPreset`:

```text
maxTransitionsPerFrame
cpuct
moveTemperature
policyTopK
resignThreshold
```

A profile must never inherit those values from its previous local state.

Example:

```text
shared = Master
Black = Custom 64
White = Follow Shared
```

must resolve to:

```text
Black: visits=64, NN=64, transitions=4, cpuct=1.35,
       temp=0.10, topK=128, resign=-0.98
White: full Master/48
```

Every fresh/local/simulated-remote reconstruction from the same synchronized
settings must produce identical complete profiles.

## 5. Draft and Apply

Preset buttons change `sharedPresetDraft` only. They do not force Black or White
out of Custom mode.

Black Follow/Custom controls change only `blackUseCustomDraft`.
White Follow/Custom controls change only `whiteUseCustomDraft`.
Each visible numeric input changes only its own visit draft. It is a TMP
integer input field, so VRChat can open its native keyboard/popup for precise
entry instead of requiring a long-range slider drag.

Nothing serializes until Apply.

One Apply does:

```text
permission check
 -> take settings ownership once
 -> clamp all five choices
 -> write coherent settings snapshot
 -> configRevision++ once if changed
 -> RequestSerialization() once if changed
 -> deterministic local profile resolution
 -> UI refresh
```

An unchanged Apply should not create another settings serialization.

## 6. Search lifecycle

`BeginSearch` captures the complete resolved profile. It does not consult mutable
UI drafts afterward.

Required sequence:

```text
Beginner/8 search starts
Apply Advanced/20 while active
current token unchanged
current target and captured knobs remain Beginner/8
current search finishes
next real BeginSearch captures full Advanced/20
```

A simulated incoming settings deserialization inside one ClientSim follows the
same rule: current valid root continues, next search uses the received profile.

## 7. Removed settings compatibility paths

The following no longer exist in `GoUI`:

```text
customVisitsSlider
customVisitsValueText
customVisitsDraft
draftSettingsDisplaySide
blackPresetDraft
whitePresetDraft
blackDraftDirty
whiteDraftDirty
selected-side draft getters/setters
single-slider fallback logic
```

The generated `Legacy Custom Visits Slider`, selected-side controller endpoints
and table actions 46/47 are absent. Validator source/runtime checks reject their
reintroduction.

Verifiers use the real Black/White numeric inputs and the one `GoAiSettings` domain.

## 8. Settings ClientSim gate

The settings verifier must compare the complete resolved profile:

```text
maxVisits
maxNNQueries
maxTransitionsPerFrame
cpuct
moveTemperature
policyTopK
resignThreshold
```

It must cover:

- shared Advanced + Black Custom64 + White Follow;
- changing shared to Master while Black stays Custom64;
- both sides Custom with different visits;
- deliberately poisoned local profile fields followed by simulated incoming
  settings/deserialization and deterministic restoration;
- Apply during a running search;
- next real search after that Apply;
- same lifecycle using simulated remote deserialization.

All of this is required and testable in one Unity/ClientSim VM.
