# GN Drive V3 検証記録

更新: 2026-09-23。対象ソース: `GNdrive/GNSystems.cs`、0.2.0-alpha。

## 現在の結果

| 対象 | 状態 | 根拠・範囲 |
| --- | --- | --- |
| 仕様v0.2 | 読取済み | Behavior_Spec_v0.2.mdは指定Desktop原本のコピー |
| 着手前のV3ソース・検証記録 | 存在しなかった | ユーザー確認。「初期ソース」はV2を指す |
| V3ソース | 今回新規実装 | GNSystems.csにCoreとKSP接続を収容 |
| KSP参照付きDebugビルド | 成功 | VS2022付属MSBuild、既存GNtech.csproj全体。後続修正の最終確認はReleaseで実施 |
| KSP参照付きReleaseビルド | 成功 | EC流路修正後にVS2022付属MSBuildで再ビルド、.NET Framework 4.7.2、エラー/警告なし |
| 純粋C#コア試験 | 63件成功、0件失敗 | tests/GNDriveV3/Run-Tests.ps1。実際のGNSystems.csをGN_CORE_ONLYでコンパイル |
| 資源アダプター契約試験 | 11件成功、0件失敗 | 同じRun-Tests.ps1で実行。実ソースをGN_ADAPTER_TESTでAPIテストダブルへ接続。KSPの実流路挙動は未検証 |
| 乱数収支試験 | 成功 | 上記の1ケース内で固定seedの2,000条件。粒子・EC収支、非負残量、支払済み予算を検証 |
| テスト用Part cfg | 作成・静的確認 | 実在するstock girderモデルを参照。KSPによるロードは未検証 |
| KSP実機 | 旧版はユーザー実機でEC流路エラー。修正版は未検証 | 対象はユーザー指定のC:\Game\Kerbal Space Program、1.12.5 |
| ゲームへの配置 | 未実施 | ビルド時はPostBuildEventを無効化。既存インストールへコピーしていない |
| GitHub最新版との差分 | 未確認 | ローカルV2と未コミット変更を基準に実装 |

テスト結果の生成先は `tests/GNDriveV3/artifacts/core-test-results.txt`、Releaseビルドログは同ディレクトリの `release-build.txt`。artifactsとbinはGit対象外。ソースからRun-Tests.ps1で再現できる。

最初の試験では59/60成功。Hover試験のΔt=1秒と期待値（Δt=0.02秒の値）が不一致だったため、テスト入力を0.02秒へ訂正。その後60/60成功。入力溢れの検証・方向別資源アクセス・同tick操作ゲートとGen1推進OFF独立性の試験を追加し、最終63/63成功。

## 実装済み範囲

- 支持優先、Hover/AG排他、上昇/降下/ゼロG、加速度・Power制限、ブレーキ、並進/回転RCS。
- 同調式、混成Group0、命令事前検査、Generation/TD Blanketの同調操作、Repose、枯渇/Gen1再充填ラッチ。
- EC依存生成、TDブランケットのバリオン変換、同tick直接供給、差引取得、部分取得後の再配分、SafeGuard、TA解除後の予算再利用と余剰返却。
- Field維持・反発計算、対応機Power制約、資源不足停止、空力保護と解除、経験式抗力、簡易球描画。
- GNModuleの保存/UI/アクション、Variant基準値からの再構成、Vesselキャッシュとイベント解除、非操作船/packedの変換限定。
- 発光・ローター・展開・粒子・音の接続。実モデルに対応するcfg指定・素材が必要。旧V2演出の完全再現ではない。

## 実機で必ず確認すること

1. GNModule/VesselModuleがロードされ、独立テストPartにReady/Standbyが表示されること。
2. 操作軸6方向と回転3軸。N→KSPのkN相当、Nm→kNm相当の変換はビルドで検証できない。
3. Hover、満杯直接生成、ロック、枯渇後の自タンク満充填、TD半量/少量、TA、Fieldの状態表示と実資源。
4. 保存/再読込、Variant切替、分離/結合/破壊、off-rails、船切替、高速Warp。
5. CargoBayや他Modが同じShieldedFromAirstreamフラグを変更した場合の共存。
6. 大型非GN船を2m半径で近似する制限、0.5秒の境界再測定、Field描画の見え方。
7. 現在の演出Transform名、AudioPath、粒子形式と実モデルの一致。

コア試験成功はKSP動作保証ではない。初回はテスト船とバックアップしたセーブで確認する。

## 2026-09-23 EC流路エラーの修正

ユーザー実機ログの `ElectricCharge: only ALL_VESSEL supported` を調査。インストール先 `GameData/Squad/Resources/ResourcesGeneric.cfg` のElectricCharge定義が `STAGE_PRIORITY_FLOW` であることを確認した。初版のALL_VESSEL限定チェックは標準ECを拒否していた。コアだけの試験では実環境の資源定義を検出できなかった。

修正: STAGE_PRIORITY_FLOWを受け付け、`Part.GetConnectedResourceTotals` で引出/受入方向を指定して照会し、`Part.RequestResource(id, demand, mode)` で実決済する。貯蔵は負の要求、返却された負の受入量を正の実績へ変換。ALL_VESSELの既存経路は維持。標準ECのcfgは変更していない。

KSP 1.12.5参照DLLからAPIシグネチャとpulling引数を確認し、実DLL参照付きビルドが成功。追加契約試験ではECコンストラクター、API流路/方向、部分取得、受入不足、満杯、ゼロ/不正要求、ALL_VESSELロック、対応外流路、コアへの部分EC反映、TD余剰ECの受入実績を確認した。全74件成功。結果は `tests/GNDriveV3/artifacts/resource-flow-test-results.txt`。

ステージ優先順位・実バッテリーロックの処理そのものはKSPへ委譲しており、APIテストダブルの試験を実機成功とは扱わない。修正版DLLの配置とKSP再起動後の確認が必要。

## ローカルQwenの利用実績

指定モデル `qwen/qwen3.8-27b`、ユーザー指定Q4_K_M。接続先は `http://127.0.0.1:1234/v1`。ロード設定は変更していない。

| 作業 | 入力/出力トークン | 結果 |
| --- | --- | --- |
| 計画段階レビュー1 | 5785 / 3500 | length、本文空、採用せず |
| 計画段階レビュー2 | 5716 / 5011 | stop、本文取得。Qwen_Review_Assessment.mdで誤読を除外 |
| コア関数生成1 | 266 / 8000 | length、本文空、採用せず |
| 同調関数へ限定した生成2 | 140 / 696 | stop、コード取得。二段階の不偏分散計算を採用し、Framework非対応のdouble.IsFiniteと単独入力の検証漏れを修正 |
| コア全体レビュー1 | 5813 / 6000 | length、本文空、採用せず |
| 決済コードへ限定したレビュー2 | 1288 / 9731 | stop、本文取得。二重取得・SafeGuard・支払済み予算について確実な欠陥の指摘なし |

生成本文はQwen_Core_Generation.md、実装レビューはQwen_Implementation_Review.md。対応するJSONにモデル名・finish_reason・usageを保存。ローカルモデルの「問題なし」は検証成功の根拠にせず、実ビルドと試験で確認した。

低推論/Thinking無効のパラメーターを送っても推論トークンが返ったため、実行環境でそれらの設定が完全に反映されたとは断定しない。短い対象への分割と完結判定を継続する。

原本コピーのSHA256: `B9E010013F21AC36A4B79385C52A72E17F56EEFF506F88A2456C326084DF105D`。



追加のローカルQwenレビューでは、ALL_VESSEL側のCapacity集計が出力専用タンクを含み、標準APIの受入側容量と意味が異なる点を指摘された。CapacityをCanPut対象へ統一し、出力専用タンクの取得は可能だが受入容量・空き容量はゼロになる契約試験を追加。再実行はコア63件＋アダプター11件の全74件成功、Release再ビルドも成功。Qwenの応答はQwen_Resource_Flow_Review.mdに保存した。

## 2026-09-23: second single-drive test fixes

- Implemented the user's selected downward AG RCS behavior and support-before-Field-maintenance priority. See Behavior_Changes_v0.3.md for revised rules and game checks.
- Corrected reproducible Field roundoff shutdown and protected priority reservations when opposing RCS reduces resultant force cost.
- SafeGuard generation-OFF reconciliation and mutually exclusive AG/Hover now feed the same state published to PAW.
- Core: 77 passed, 0 failed (including 14 added regression cases and 2000 randomized opposing-force allocations). Resource adapter: 11 passed, 0 failed. Log: tests/GNDriveV3/artifacts/second-test-results.txt.
- Release GNtech.csproj build succeeded with post-build game deployment disabled. Final PAW diagnostic update was rebuilt successfully. No KSP runtime test or game DLL replacement performed by the agent.
- The original four symptoms were reported by the user in game. Reproduction of the rounding defect confirms a code defect but does not uniquely identify the cause in that save.
- Local qwen/qwen3.8-27b reviewed the allocator and reported no concrete mathematical bug. Its ForceKN question was checked against the actual computed property (support + brake + thrust). Review saved in Qwen_Second_Test_Review.md and qwen-second-test-review.json. Review was of the allocator before the final exhausted-budget zero-output guard; tests cover the final source. Model review is supplementary, not runtime verification.

## 2026-09-23: RCS rotation direction correction

User verified Field maintenance behavior and shutdown on particle exhaustion; repulsion remains untested. User then reported torque opposing control input.

Cause: GN input mapping used +right*pitch +forward*yaw -up*roll. Local KSP 1.12.5 Assembly-CSharp.dll ModuleReactionWheel IL sets inputVectorTemp=(pitch,roll,yaw), negates inputVector, transforms by ReferenceTransform.rotation, and calls Part.AddTorque. Part.AddTorque accumulates the supplied vector without changing its sign. Thus GN pitch and yaw were reversed; roll already matched. Inspection evidence: tests/GNDriveV3/artifacts/stock-torque-il.txt and stock-part-torque-il.txt.

Correction: KspControlFrame.Rotation maps -right*pitch -forward*yaw -up*roll. The runtime input reader uses this tested function. No changes to translation mapping, torque magnitude, particle rates, or allocation priority.

Validation: 79 core tests and 11 resource adapter tests passed. New cases cover all three axes with both input signs in three reference orientations (18 combinations), combined input normalization, resource cost, absence of unintended linear force, and RCS OFF. Release build was performed with automatic game deployment disabled. In-game direction and SAS behavior still require verification.

Game check: use nonzero Rotation share, enable RCS, disable SAS initially and test pitch/yaw/roll separately in both directions. Compare with stock control direction, including a changed Control From Here frame. Then enable SAS and check damping. Replace DLL only while KSP is closed.

## Mechanisms and emissive visuals

Implemented numeric GN_Rotor_/GN_Stator_/GN_Core_/GN_Arm_ discovery, relative initial-pose motion, binary clamp arms, support-sensitive load and TD-generation idle rotor including Repose. Variant RotationAngle explicit zero is retained; profile values override variant/common settings. Core and clamp updates include inactive variants; rotor spin only affects visible variants. Model-based motion and shader appearance require game validation. Muse supplied a bounded policy review; user clamp intent takes precedence over its suggestion to couple arms to load.
Core tests: 84 passed. Adapter tests: 11 passed. Release build succeeded with game deployment disabled. Log: tests/GNDriveV3/artifacts/mechanism-test-results.txt. Runtime Unity transform/variant/material verification remains pending.

## PAW tuning, staging, TD particles, stator correction

Existing 84 core and 11 adapter regression tests passed (artifacts/paw-test-results.txt). They cover simulation/resource behavior, not Unity UI, staging or transform inheritance. Release compiled against KSP 1.12.5 with post-build deployment disabled. Actual PartModule staging method signatures were reflected before implementing overrides. No game installation files were changed. User must verify staging, saved sliders, TD idle emission and stator stability. Details in Particle_Effects.md (PAW tuning and startup changes).

## Trans-AM target acceleration

Implemented fixed x3 acceleration target while a running TA-capable drive is in TA. Normal slider and MaxG display range remain unchanged; separate effective-target PAW field reports boost. Core tests 88 passed, adapter tests 11 passed; Release build succeeded without game deployment. Regression log: tests/GNDriveV3/artifacts/ta-acceleration-tests.txt. KSP runtime behavior remains unverified for this change.

## Vessel-wide Trans-AM visual colour

One running TA-capable drive in TA now gives every GNModule on that vessel the existing TA colour (1,0,0.7). The existing tested acceleration-activation predicate is reused and evaluated once per physics update. Leaving the last active TA restores each module's normal colour selection. Particle, emissive and light colour consume the shared override; inactive effects are not forcibly enabled. Other modules' TA flags, Power and resource costs remain unchanged. Field sphere tint remains unchanged. Release build passed; in-game multi-drive appearance remains unverified. No new simulation logic or additional test suite run for this colour-only change.

## Stator disappearance correction

Confirmed code defect: the rotor-inheritance fix allocated pose arrays and restored stators from them, but the intended per-frame capture was missing. Thus flight visual updates assigned default world position (0,0,0) and an invalid zero quaternion, whereas VAB did not execute visual Update. Added explicit current-frame world position/rotation capture before rotating any rotor, then kept the parent-before-child restore and subsequent core/arm motion. This was an implementation omission, not a model naming requirement. Source order verified; in-game transform behavior still requires verification. Prior simulation tests did not cover this Unity visual path.

## Stator local hierarchy simplification

User committed to sibling rotor/stator structure on all drives. Removed the entire stator world-pose compensation path; stator transforms are now untouched and only emissive properties are controlled. Rotor retains local-Y Space.Self rotation. Release build succeeded, removed-symbol search confirmed no stator pose arrays/restore remain. No new core logic and no core test rerun for this visual-only deletion. In-game validation with updated models remains pending. This supersedes the earlier nested-stator workaround and its missing-capture fix.

## 31t Trans-AM 5G report diagnosis

Inspected installed C:/Game/Kerbal Space Program/GameData/GNdrive/Plugins/GNdrive.dll. PropulsionController.Plan IL clamps AccelerationG to MaxG then multiplies by standard gravity; it contains no TA target multiplier (artifacts/installed-ta-il.txt). Installed SHA256 8DF063E6EBF184CBFB6BBC992EFB687590303F95BAC11035E9858B18453D8430 differs from current Release. This identifies an outdated installed acceleration implementation; it does not prove which DLL an already-running KSP process loaded. Added regression case 89: 31000kg, Power10000, normal max/target5G, dt.02, funded particles -> normal5G and TA15G (4560.09225kN), no TA fallback. 89 core and 11 adapter tests passed. Release build succeeded. Game DLL was not overwritten and KSP runtime remains user-verified. Replace while KSP is closed.

## Brake descent and stator separation report

92 core + 11 adapter tests passed; Release build succeeded. Added 500-step gravity/force integration test and tiny-speed cancellation test. Log: artifacts/brake-gravity-tests.txt. Installed game DLL still hashes 8DF063E6EBF184CBFB6BBC992EFB687590303F95BAC11035E9858B18453D8430 (same outdated binary as previous turn). Stator report cannot be attributed conclusively without testing current sibling-model DLL; current source has no stator transform/velocity writes. No game DLL was overwritten. In-game flight verification pending.

## Drive particle acceleration backflow

Drive particles use Force over Lifetime to add configurable acceleration opposite the actually allocated vessel GN force. `ParticleAcceleration` defaults to 20m/s2, is persistent and is exposed by visual tuning from 0..200; zero disables it. Its applied magnitude follows the smoothed 0..1 vessel particle load, giving zero near idle and the configured endpoint near full output. Propulsion-off generation/TD idle and zero/invalid applied force explicitly disable the module. Existing local simulation, outlet velocity/cone, resource accounting and physical forces are unchanged. Core tests 102-103 verify direction, full-load magnitude, load clamping and disabled edge cases. 103 core + 11 adapter tests passed and the KSP-reference Release build succeeded. Unity particle appearance remains an in-game validation item; no game DLL was overwritten.

## V2-style shield visual implementation

Replaced flat transparent sphere with GNShieldV3Visuals: grain-textured CPU Fresnel rim, local spherical-shell particles at CoM and normal 2s membrane fade. One instance per vessel. Runtime resource/force semantics unchanged. Core94 + adapter11 passed; Release build successful. See Shield_Visuals.md for details and unverified in-game rendering checks. No installation files modified.

## Colour fade / stock aero visual suppression

97 core and 11 adapter tests passed (artifacts/color-aero-tests.txt); Release compiled against installed KSP 1.12.5 APIs. New colour cases test 1.5s completion, midpoint reversal and repeated-target stability. Muse authored the pure VisualColorTransition helper; reviewed and integrated. Aero suppression was checked against stock IL/API but not rendered in-game. Camera/renderer restoration, third-party interactions and actual FX disappearance remain runtime verification items. See Shield_Visuals.md for exact scope. Physical heating/drag not modified by this feature.

## Load-responsive drive emission

Added low/full-load speed, emission and inverse cone-angle interpolation; current visual input/support demand is combined with allocated motion/Field power usage. Per-vessel aggregation occurs once per physics step. Added lifetime-based 80-percent particle-capacity headroom without increasing maxParticles. 99 core +11 adapter tests passed; Release build succeeded. Log: artifacts/particle-load-tests.txt. User reported intermittent particle-free bands; capacity saturation is a plausible contributor, not a confirmed root cause. In-game transitions, saved PAW settings and banding remain to be checked. Shield shell unchanged.

## Particle colour correction after white-particle report

The previous smooth-colour path made particle startColour white and depended on material _TintColor for all RGB. White output is consistent with that tint not reaching the active shader, but exact runtime shader/state has not been observed. Replaced dynamic material tint with particle colorOverLifetime RGB gradients; material tint remains neutral. Birth colour stays white, age-dependent alpha fade is preserved, and current interpolated RGB applies to living particles as well as new births. Drive and shield particle layers both use this path. Reused gradient/key arrays avoid per-frame gradient allocation. Existing 1.5s transition and load-responsive settings remain unchanged. Release build passed; in-game colour verification pending. No simulation change or simulation-test rerun for this Unity-rendering-only correction.

## Condenser visual extension

Added local-tank-driven rotation/emissive groups with independent names. Release build succeeded. Tests: core100 + adapter11 passed (artifacts/condenser-visual-tests.txt), including capacity-zero/NaN/overflow ratio handling and naming separation. Actual Unity visual hierarchy/material behaviour remains unverified in game.

## 2026-09-24 Inherited normal colour with idle generators
- Condenser and Field shared normal colour now uses installed Generation capability, not per-tick Generated amount. Generation OFF and full tanks retain Tau red. No generator retains green fallback. Existing mixed-source and Trans-AM priorities unchanged; custom GeneratorColor routing remains pending.
- Regression 101 exercises generation disabled, full tank, and absence of generator. Core 101 + adapter 11 passed; Release build passed with post-build deployment disabled. In-game visuals await user verification.


## 2026-09-27 Trans-AM displaced silhouettes
- Added GNAfterimageVisuals, separate from GNDriveV3Visuals, owned/disposed by vessel glow. Four render-only copies per eligible mesh, sharing static meshes; skinned sources bake once per visible frame into one owned mesh shared by four copies. No Part components, colliders, or bones are cloned.
- Default offsets 1/2/3/4 metres opposite estimated net acceleration (GN applied force / vessel mass + gravity), only when GN force is nonzero and Trans-AM vessel glow is enabled. This estimate does not include aerodynamic, contact, or other engines' forces. These are displaced current poses, not recorded historical snapshots.
- VesselGlow keys: AfterimagesEnabled=true, AfterimageSpacing=1 (metres), AfterimageOpacity=0.3, AfterimageThreshold=0.5 (m/s^2). Alpha decreases 0.3/0.225/0.15/0.075, scaled by fade; additive rendering. Appearance ~0.083 s, disappearance 0.25 s. Settings loaded on game start as existing settings are.
- Uses same renderer exclusions as vessel glow. Materials/owned baked meshes destroyed on rebuild/dispose. Four extra mesh passes increase GPU draw workload; runtime performance and skinned appearance require KSP validation.
- Release build succeeded with deployment disabled. Existing core 104 and adapter 11 tests passed. These tests do not exercise Unity rendering. Pending in-game: acceleration direction, stationary AG, stopping/TA OFF fade, variants/staging, scene changes, skinned transforms and performance.

## 2026-09-27 Size-relative afterimage spacing
- Supersedes fixed-metre spacing: each step is Field diameter / AfterimageSizeDivisor * AfterimageSpacing. Defaults divisor=8, spacing multiplier=1. AfterimageCount integer 0..16, default 4; zero creates no copies. Opacity decreases by (count-index)/count.
- Reuses cached Field radius (0.5-second measurement); no extra vessel scan. Measurement now excludes GN V3 Vessel Glow shells/afterimages and inactive hierarchy renderers, avoiding recursive growth. Radius remains the existing conservative world-AABB enclosure (minimum radius 0.5m), not exact length; orientation and articulated parts can change it.
- Existing cfg AfterimageSpacing values are now dimensionless multipliers. Restart KSP after cfg edits. Source meshes shared; count increases render cost proportionally.
- Release build passed, core regression 105 passed including diameter scaling. Unity appearance and runtime performance require game validation.

## 2026-09-27 Gradual afterimage extension
- AfterimageExtendTime defaults to 1 second, clamped 0.01..10. Sustained acceleration advances smoothstep extension from zero to full normalized spacing; opacity also scales with extension to avoid a bright stack at startup. Acceleration magnitude is not an additional length multiplier.
- Acceleration stopping holds extension during the existing 0.25s fade; once invisible, extension resets. Hide resets immediately. Brief interruptions resume the remaining extension.
- Release build and 106 core + 11 adapter tests passed; Unity visuals pending user validation. User authorized direct game deployment; existing DLL/cfg backed up and replaced, matching hashes verified.

## 2026-09-27 Afterimages catch up on release
- Replaced stationary 0.25s fade with reverse smoothstep extension. AfterimageExtendTime controls both full extension and full retraction; partial extension retraces only elapsed time. Opacity follows extension in both directions. Reacceleration resumes current progress; retained direction on release. Changing acceleration direction still redirects silhouettes as before.
- Release build passed; 107 core and 11 adapter tests passed. Backed up installed DLL and deployed new DLL with matching SHA256. Existing game cfg values need no change. In-game appearance pending.

## 2026-10-03 TD Blanket replaces direct TD generation
- Removed the legacy path where positive TopologicalDefects forced particle generation ON and independently generated EC. TopologicalDefects remains an Original-drive identity/colour input only; legacy ECPerTDSecond values are accepted but ignored.
- Generation is now the sole visible switch for particle generation and TD Blanket; the dedicated TD toggle/action is hidden and retained only as a coupled compatibility alias. Synchronized Generation commands switch both states together. Baryon starvation latches both OFF until a Generation ON command restarts them.
- Blanket output is `particle EC × 2 × local TD fill fraction`: 0% disables particle/EC generation without ordinary-EC fallback, 25% draws the uncovered half from vessel EC, 50% is break-even, and 100% produces twice the particle cost. Non-TD generators retain their ordinary EC path.
- Core regressions cover zero/quarter/half/full fill, coupled commands and loaded-state reconciliation, partial baryon starvation, randomized conservation, and non-TD isolation. Core 122 + adapter 11 passed. Release build passed with 0 warnings and 0 errors. KSP was confirmed stopped; the prior DLL was backed up outside GameData at `C:\Game\GNdrive-CodexBackups\20261003-222818-td-generation-fill-balance\GNdrive.dll`, then the new DLL was deployed with matching source/installed SHA-256 `B852305F2D1633BB550C214A7A4AD54604E4D0DC035056B63237BBE1D9532D43`. KSP runtime resource/UI behaviour remains pending.

## 2026-10-03 TD idle EC generation during packed time warp
- Added a separate global `TDBlanket/IdleECPerSecond` setting, default 2 EC/s at 100% TD. Legacy production part cfgs commonly contain `ECPerTDSecond = 100`, so that field remains ignored rather than unexpectedly becoming a 100 EC/s idle source.
- When no particles are otherwise required and TD fill is above 50%, an EC deficit now raises conversion up to the drive's full particle rate. Net charge per generated particle is `ECPerParticle × (2 × fill - 1)`; generation is capped to the amount needed to fill EC during that step, and unused particles are recorded as `ParticleSpilled`. At or below 50%, forced conversion would produce no positive net EC, so the small idle source remains the fallback. Full EC storage neither wastes baryons nor triggers starvation.
- Regressions 123-129 cover packed full-rate charging, exact stop at full EC in a 100-second step, TD/synchronization idle scaling, 50% fallback, 75% net output, particle disposal, 2,000 randomized EC/particle/baryon conservation cases, and shared EC-capacity accounting across multiple drives. Core 129 + adapter 11 passed and the Release build succeeded. KSP was confirmed stopped; the prior DLL/cfg were backed up outside GameData at `C:\Game\GNdrive-CodexBackups\20261003-231054-td-full-charge-final`, then the final DLL/cfg were deployed. Source and installed SHA-256 match: DLL `1B6559A0A8D17C568A7196C36B2401F116E7CF65FDDE9B8356582EABE1ECF8A7`, cfg `D541AC5B43D02F5974A0D98AE83FE6506B01456AB1F3E0B1EFD05E49687F6CB9`. Actual KSP on-rails time-warp observation remains pending.

## 2026-10-04 TD break-even correction and fill-gradient colours
- Supersedes the independent idle-output behaviour above. `IdleECPerSecond` and the no-particle EC path were removed: no generated particles now means no blanket EC and no baryon use. At or below 50% TD, the generator uses stored vessel particles first and converts only the remaining current effect demand; it does not refill particle storage while idle. Below 50%, planned conversion is capped by accessible vessel EC using `ECPerParticle × (1 - 2 × fill)` per particle. At 50% it is energy-neutral. Above 50%, the prior full-power charge-until-full and particle-spill behaviour remains.
- Drive particle, ring-glow and normal emissive colour now linearly interpolate from each module's `GeneratorColor` at 0% TD (default `1,0,0.15`) to Original green `0,1,0.65` at 100%. The existing 1.5-second visual transition remains. Trans-AM does not replace particle colour; the existing Trans-AM core/emissive and GNLight overrides remain applied to the drive itself.
- Regressions 110 and 123-133 cover endpoints/midpoint/custom zero-TD colour, TA-independent base colour, idle non-consumption, stored-particle priority, EC-assisted rate limiting and zero-assist refusal, break-even behaviour, full-rate charging, spill, multi-drive EC capacity sharing and randomized conservation. Core 133 + adapter 11 passed; the KSP-reference Release build succeeded. KSP was confirmed stopped; the previous DLL/cfg were backed up outside GameData at `C:\Game\GNdrive-CodexBackups\20261004-013209-td-breakeven-colour-gradient`, then the new DLL/cfg were deployed. Source and installed SHA-256 match: DLL `A78E89A40FC1D3597852C7572AF9251E405C9D55690C96D48BFC18AB41334691`, cfg `95FED8D5CB21FC907333A7FE14BF9A1413C0C70AC60E88CBC6BB32AEB96C4223`. Unity/KSP colour rendering and packed-warp resource behaviour remain runtime checks.

## 2026-10-04 Independent furnace and automatic TD Blanket
- Supersedes break-even correction: see Independent_Generation_Blanket.md. Furnace refill allowed at every TD ratio if EC is available. Blanket independent of furnace/engine/sync; rated EC multiplied by local TD fraction, receives same-step furnace consumption plus EC free space. Baryon shortages auto-recover without disabling furnace. Positive TD Blanket uses green normal colour.
- Core 125 + adapter 11 passed, including 2000 random conservation cases; superseded coupled/50%-threshold tests replaced. Release build succeeded. Runtime confirmation pending.

## 2026-10-04 Stateful PAW toggles
- Propulsion, AG, Hover, Trans-AM, Generation, Synchronization, Repose, SafeGuard and Field use stock UI_Toggle with ON/OFF text. Old event buttons hidden; methods/actions retained. Depleted and automatic Blanket remain status-only.
- UI changes restore the prior backing field before enqueueing the requested value through existing Commands. Coordinator Publish is authoritative for acceptance, rejection, synchronization and automatic transitions. KSP-reference Release build succeeded. Stock skin's selected appearance requires in-game confirmation; no custom UI tint has been forced.
- Core 125 + adapter 11 passed. Installed DLL backed up outside GameData and replaced while KSP stopped; SHA256 matched. Runtime UI testing pending.
- 2026-10-04 zero-EC investigation: added regression 126 matching saved craft TD40%, furnace2000/s, blanket40000EC/s. Core confirms 16000EC/s generation with zero storage, and charging when furnace OFF. Core126/adapter11 passed; this does not verify native KSP baryon routing. No runtime fix or DLL deployment in this investigation.

## 2026-10-04 Empty-EC native receiving-space correction
- Confirmed in actual KSP 1.12.5 Assembly-CSharp.dll IL: Part.GetConnectedResourceTotals(... pulling=false) routes to PartSet, which sums maxAmount-amount into its amount output, and maxAmount into capacity. Adapter incorrectly subtracted this free-space value from capacity again. Empty EC was interpreted as no receiving room, full EC as full receiving room.
- KspResourcePort.FreeSpace now uses the returned amount directly for native receiving queries. The old adapter stub encoded the same mistaken assumption; corrected to actual KSP semantics. Added empty/full boundary and zero-EC automatic Blanket integration tests (furnace OFF, charge to full, then stop baryon use).
- Screenshot showed Blanket EC 12000/s and baryon use 1.335E-10 kg/s while furnace ON: generation was occurring despite displayed zero EC. Furnace OFF exposed the zero recharge demand bug. UI toggle coupling is not the cause.
- Core126 and adapter13 passed; Release build succeeded. KSP stopped, prior DLL backed up outside GameData and fixed DLL deployed with matching hash. Actual runtime recharge remains user verification.

## 2026-10-06 Sync-scaled acceleration contribution
- Effective normal acceleration cap is sum(MaxGContribution * Sync) over Running drives. Definition/cfg contributions remain unchanged. Step computes Sync before the cap; the adapter clamps the PAW target after Step, removing the former unscaled-cap clamp that would discard bonuses.
- Matched twins at base5G each have Sync2 and combined20G. Chosen target is not automatically multiplied; PAW max increases. TA still triples the clamped normal target. Mismatched or stopped/reposed drive contributions behave according to existing Running and synchronization membership rules; Tau uses the same coefficient.
- PAW status is Unsynchronized for synchronization participants with Sync<=0.5; higher-priority status such as depletion or supply limit is retained with the warning appended. No change to synchronization membership/coefficient, output power, generation, or Blanket.
- Regressions127-130 verify matched Tau cap, TA interaction, poor matches, unjoined contribution, inclusive warning boundary, and target preservation. Runtime PAW/flight verification pending.
- Core130 + adapter13 passed; Release build succeeded. DLL backed up outside GameData and deployed with matching SHA256 while KSP stopped. No cfg changes.

## 2026-10-06 N-power coefficient clarification
- Confirmed requested coefficient already matches GNMultiDriveUnit.Coefficient: q=1-clamp(4*sampleVariance(Individuality),0,1), multiplier=N*q^N. q is pre-exponent matching quality; DriveState.Sync and PAW Synchronization are the final multiplier, not q.
- Example N=2,q=0.7 gives per-drive multiplier0.98, total1.96 times a single identical drive, approximately ordinary double-drive output. q=1 gives multiplier N and aggregate N^2 for identical participating drives.
- N retains existing membership: Joined (Sync ON), MultiDrive capable, Group0, not Repose. Engine OFF alone does not remove a participant. Nonparticipants and a sole participant use multiplier1.
- MaxGContribution uses this final multiplier once. Unsynchronized threshold<=0.5 applies to the final multiplier, not q. No runtime code changes or redeployment required for this clarification.
