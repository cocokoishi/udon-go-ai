直接修改 `cocokoishi/udon-go-ai` 当前 `main`。不要停在分析或方案阶段。

最终同时完成三件事：

1. 将 production NN 从逐 pass `Material.Set* + VRCGraphics.Blit` 重构为 **静态 OnDemand CustomRenderTexture DAG**。
2. 将固定分析面板从 10 行改成 11 行，在 COMPUTE 正上方加入 COUNT/算子行。
3. 将现有 **8-visits × 32 positions** 的 KataGo 最终 Top-1 exact agreement 做到 **≥30/32**。

额外硬门槛：

> 不允许为了提高一致率显著增加计算时间。

同 fixture、同 8 visits 下，最终比较 before/after 的 wall time、ms/visit、frame p95/p99/max。持续超过约 10% 的关键性能回退视为失败；5–10% 必须有明确原因且不能同时恶化 wall 和 frame tail。

**禁止**增加 visits/NN queries、fixture/hash 特判、硬编码答案、修改 comparator、放宽 exact match、调 seed 拟合测试集、降低 NN 精度、修改模型权重，或用同步阻塞代替现有 cooperative/async 架构。

按以下阶段执行；阶段中只跑必要的 compile/micro smoke，**完整回归、benchmark 和 8 32 parity 最后统一跑。**

### Phase 1 — 从当前代码建立真实基线

先完整阅读 `Runtime/ Editor/ Tests/ Shaders/ Tools/ Docs/ AGENTS.md`，以及仓库内 `.KataGO/KataGo-master`。

重点沿真实调用链读：

`GoAiController → GoGpuNeuralRuntime → GoGpuLayerExecutor → GoGpuNeuralOutputReader → GoMctsSearch`

并通读 KataGo：

`search.cpp / searchexplorehelpers.cpp / searchresults.cpp / searchparams.cpp / analysisdata.* / nninputs.cpp`

不要相信旧 Docs 的结论，所有架构和搜索行为以当前源码为准。

保存修改前基线：

`8×32 Top-1`、A-H 4/32 visits、1/2/3-table concurrent、wall/ms-per-visit/p95/p99/max、GPU bytes。

不要优化 48 visits；若原脚本顺带输出 48，只记录，不据此调参。

### Phase 2 — CRT capability 与静态 DAG

先验证 UdonSharp/ClientSim 中 `CustomRenderTexture.Update()` 或 `Update(1)` 真正可用，并做：

`A=input+1 → B=A+1 → C=B+1`

同帧 queue，下一帧必须得到 `input+3`。

随后新增 `PureUdonGoNNLayerCRT.shader`，数学逐算子移植现有 `PureUdonGoNNLayer.shader`：

* 相同 FP32；
* 相同 weight atlas；
* 相同 convolution loop / float4 accumulation；
* 不 fold BN；
* 不 FP16；
* 不改 channel packing。

实现 **Static CRT DAG**，禁止 giant atlas、double buffer、dynamic update zones。

production graph 保持现有 fused 数学：

`root = 65 CRT NN nodes + 1 existing packed-output Blit`

`non-root = nodes 0..63 + 1 packed-output Blit`

节点/检查点固定为：

`initial=0..2, rconv1=5, rconv2=8, rconv3=11, rconv4=14, rconv5=23, rconv6=26, rconv7=29, rconv8=38, rconv9=41, rconv10=44, trunkTip=45, policy=46..54, value=55..61, score=62..63, ownership=64`

其中必须保持：

`policyPass source=node49`

`ownership source=node55`

不是其他 head 的最终输出。

Activation/vector 尺寸、ARGBFloat、Point、Clamp、linear、无 mip 全部保持当前布局。

### Phase 3 — Runtime 接入，但不改变搜索接口

`GoGpuNeuralRuntime` 保留两个 backend：

`LEGACY_BLIT=0`

`CRT_DAG=1`

Generated visible table 默认 CRT；legacy 保留作为 numerical oracle/debug。

不要重写 Controller/MCTS/readback contract：

`BeginEvaluateEncoded → StepEvaluation → MatchesGraphIdentity → packed output → ONE AsyncGPUReadback`

CRT topology/material 全部由 Editor Generator 预生成。

每 table、每 node 独立 CRT + Material；只共享 shader 和 weights。三桌禁止共享 mutable material/CRT。

只给 `GoBoardPool.FIXED_VISIBLE_TABLES == 3` 的生产桌生成完整 CRT graph；隐藏 backing tables 不要生成 `16×65` heavy assets。

CRT backend 输入直接：

`spatial Texture2D → node0`

`global Texture2D → node1`

删除 CRT backend 的两次 input Blit。Texture2D 只在 resource create/recreate 时绑定到 node0/node1 material，不能每 evaluation SetTexture。

packed output 第一版继续使用现有单次 Blit 和现有 reader。

逻辑 pass telemetry 仍保持：

`root=66`

`non-root=65`

另记录 CRT updates/batches/queue/drain。

### Phase 4 — Deferred batch、cancel/drain 和性能约束

CRT `Update()` 是 deferred，绝不能 queue 后同帧认为完成。

实现跨 frame batch：

`queue nodes → crtBatchPending → 下一帧 barrier 后 mark complete → queue next batch`

初始 `crtBatchNodeLimit=7`，之后 benchmark 4/5/6/7/8/10，选 wall 最低且 p95/p99/max 不明显恶化的值。

保留现有 `gpuGraphStage / progress / residualBlockIndex / executedPasses`，Builder 给每 node 序列化 stage/progress/block metadata。

保留 public checkpoint RT 引用，直接指向对应 CRT node，保证现有 numerical tooling 可继续读取。

必须实现 cancel drain：

旧 search queue CRT 后，如果 undo/reset/owner transfer，**在旧 queued batch 真正跨过 frame barrier 前禁止覆盖 input Texture2D 或启动新 graph**。

增加 `CanBeginEvaluationNow()`；Controller 只做必要 deferred/drain 适配，drain 是 transient thinking，不是 AI error。

现有 identity 一项都不能削弱：

`table/token/node/positionRevision/settingsRevision/profileRevision/ownerLifecycle/side/hash0..3`

旧 GPU work 可以完成，但绝不能进入新 SearchSession。

warm-up 必须走真实 CRT production path。

### Phase 5 — 11 行分析面板

保持单一 `GoTelemetry.panelText == GoUI.panelTelemetryText`，不新增 TMP。

固定第 10 行：

中文：

`算子  白−黑 <mean> ± <stdev>  ·  黑需 <blackNeed>  白需 <whiteNeed>`

英文：

`COUNT  W-B <mean> ± <stdev>  ·  B NEED <blackNeed>  W NEED <whiteNeed>`

原 COMPUTE 下移第 11 行。

`lastNeuralScoreMean` 已经是 White-Black，禁止再次按 side-to-move 翻符号。

没有 NN output 时显示：

`— ± —`

area count 必须复用 `GoGame` 当前 area-scoring flood-fill，不得在 Telemetry 再实现一套算法。

增加共享 current-area count core + revision cache，并定义：

`BLACK_TARGET=185`

`WHITE_TARGET=177`

`need=max(0,target-currentArea)`

final analysis 必须在 AI move commit 前保存对应 root 的 `blackNeed/whiteNeed`，避免旧 root score 配新棋盘 count。

注意：**当前源码 `SYNCED_INT_FIELD_COUNT` 是 20**。新增两个 synced int 后按实际字段重新核算 estimator，不要照旧文档写死 19→21。

UI verifier 改成严格 11 行，并验证中英文 label、B/W NEED、黑需/白需以及 final/live snapshot identity。

### Phase 6 — KataGo 8-visits 真正语义对齐

CRT 和 UI 稳定后再改搜索；不要把两类 bug 混在一起调。

目标仅：

`8 visits × 32 → exact Top-1 >= 30/32`

首先从测试脚本确认 reference Top-1 到底是：

`raw visit argmax`

`playSelectionValue top1`

还是 `getChosenMoveLoc()`

然后严格复刻对应 KataGo 路径，不能换 comparator。

当前代码已知需要重点逐行对齐：

`GoMctsSearch.SelectEdge / GetFpuValue / GetExploreScaling / score utility / Complete / ChooseRootMove`

因为当前 shipped `searchSemanticsMode=0` 会让 KataGo FPU、child-weight PUCT、recentScoreCenter、rule-aware no-result 仍是 opt-in diagnostic semantics；8-visits preset 当前又实际使用 `cpuct=1.85 / T=0.10 / topK=24`。

不要简单 `ConfigureSearchSemantics(15)` 就算完成。

必须根据 **测试实际使用的 KataGo SearchParams** 对齐：

* root/non-root FPU；
* child weight / PUCT scaling；
* cpuct 参数；
* score utility + recentScoreCenter；
* no-result；
* backup perspective；
* root `getPlaySelectionValues()`；
* reduced play-selection weight / LCB / chosenMoveSubtract / prune / pass handling；
* 若 reference 使用 `getChosenMoveLoc()`，再复刻其 temperature/RNG 语义。

禁止针对 32 个 fixture 搜参数。

调试只使用 “first divergence”：

同一个 position、同一个 NN output，比较 simulation 1..8 的 `P/N/Q/U/FPU/explore/selected child/backup/root playSelection`，找到 Udon 和 KataGo **第一次做出不同 decision 的 simulation**，只修能从 KataGo 源码证明的语义差异。

Feature encoder、Shader、Async readback 已有较强 differential/identity 基础；除非新的 trace 直接证明它们首先分叉，否则不要顺手重写。

### Phase 7 — 最后一次统一验收

所有代码完成后再跑完整 gate：

Generator、Production Validator、UdonSharp Compile、rules/superko/V7 feature、legacy-vs-CRT 17 checkpoints、reader A/B/C、stale/owner-transfer/cancel-drain、complete-game、11-line UI、三桌不同输入串扰、1/2/3-table concurrent、A-H legacy/CRT 4和32 visits。

CRT numerical tolerance继续：

`atol=1e-4`

`rtol=1e-5`

最后跑 **8 visits × 32** KataGo parity，必须：

`exact Top-1 >= 30/32`

最多剩 2 局 mismatch；每局必须报告 first divergence，不准用特判消掉。

最终报告只给真实数据：

`8×32 before → after`

`legacy → CRT wall/ms-per-visit/p95/p99/max`

`input Blit 2→0`

`NN operator Blit → CRT updates`

`pack Blit 1→1`

`CRT batches/drain`

`GPU bytes`

`16 fixtures × 17 checkpoints max error`

`1/2/3-table concurrent`

以及修改前后 top-3 bottleneck。

完成标准是：

> CRT 静态 DAG 和 11 行 UI 真正进入 production；8-visits exact Top-1 ≥30/32；没有 fixture hack；没有偷偷增加计算量；wall time 和 frame tail 没有显著性能回退。
