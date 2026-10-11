# GN Drive V3 調査記録 － 現行仕様のソース抽出

調査日: 2026-10-04。対象: `GNdrive/GNSystems.cs`（3,122行 / 161,212 bytes / 更新 2026-10-03 23:09、ヘッダ表記 `GN Drive V3 0.2.0-alpha`、git 未コミット変更を含む作業ツリー）
付随対象: `GNdrive/GNtech.csproj`、`GameData/GNDriveV3/GNSettings.cfg`、`GameData/GNDriveV3/Examples/*.cfg.example`

本書は**実装されている挙動の調査記録**であり、要求仕様の `Behavior_Spec_v0.2.md` / `Behavior_Changes_v0.3.md` とは独立性を持つ。行番号は抽出時の `GNSystems.cs` を指す。
方法は全行の通読と、`VesselSimulation.Step` / `PropulsionController.Plan` / `Allocator.Allocate` / `KspResourcePort` / `GNDriveV3Vessel` の逐次追跡。ビルド・実機実行は行っていない（実機未検証範囲は docs/v3/Validation.md と §15 に列挙）。末尾に**修正提案**をまとめる。

---

## 0. 構成とビルド境界

- 1ファイル2namespace。`GNSystems.Core`（6〜1014行、Unity/KSP参照なし）と `GNSystems`（1016行〜、KSP接続層）。
- 条件コンパイル: `#if !GN_CORE_ONLY`（1015行）で KSP 層を丸ごと除去可能。`#if !GN_ADAPTER_TEST`（1019 / 1024 / 1767行）で設定ローダと `KspBaryonPort` を除去。
- コンパイル対象は `GNsword.cs` + `GNSystems.cs` + `Properties/AssemblyInfo.cs` の3点のみ。V2ファイル（GNDriveCore / GNPhysics / GNVisuals / GNMechanics / GNshield / GNVessels / PID）はツリーに残るが対象外。
- 参照: `C:\Game\Kerbal Space Program\KSP_x64_Data\Managed`。対象 KSP 1.12.5 / .NET Framework 4.7.2。

## 1. 単位系と数値規約

| 項目 | 値・規約 | 出典 |
| --- | --- | --- |
| 基準単位 | kg, m, s, N, N·m。予算単位は kN と粒子数 | 1行目コメント |
| 標準重力 | `G = 9.80665` | 10 |
| 許容誤差 | `Epsilon = 1e-9` | 10 |
| 予算充足判定 | `Covers(cost, budget)` = `cost <= budget` または相対 16ULP 以内。**budget=0 では正のコストは決して満たさない** | 31–36 |
| 配分時の厳密さ | `Allocator.Fit` は `Covers` を使わず素朴比較（toleranceで予算超過目盛を選ばない） | 770–773 |
| 入力検証 | `Nonnegative` / `Positive` / `Finite` 違反は即例外。非有限ベクトルは `ArgumentException("Non-finite input")` | 19–30, 833–845 |
| 座標 | 自社 `Vec`（readonly double X,Y,Z）。UnityEngine 型は `ToCore` / `ToUnity` の境界でのみ変換 | 37–99, 2219–2225 |

質量エネルギー換算（TD Blanket のみ使用）:

```
1 EC = 1 kJ                      (JoulesPerEC = 1000)
c  = 299792458 m/s
kg で必要な質量 = EC * 1000 / c^2
資源1単位あたりの EC = density[t/unit] * c^2   // t→kg と J→kJ の1000が厳密に相殺
```
（435–450）

## 2. 能力フラグと定格

`DriveDefinition`（100–118）の既定値:

| 能力フラグ | 既定 | 意味 |
| --- | --- | --- |
| Propulsion | false | 支持・推進・回転の可否 |
| Generation | false | 粒子生成の可否 |
| TD | false | Original（TopologicalDefects を持つ Original ドライブ）判定に使う |
| TDBlanket | false | 質能変換の有無 |
| TransAM | false | TA の可否 |
| MultiDrive | false | 同調（同期）対象になるか |
| Field | false | GN Field の可否 |
| Gen1 | false | Gen1 制約（TA手動解除拒否・強制解除時ロック） |

| 定格 | 既定 | 制約 |
| --- | --- | --- |
| Power | 100 | ≥0 |
| MaxGContribution | 5 | ≥0 |
| ParticleGenerationRate | 10 | ≥0 |
| ECPerParticle | 1 | **>0** |
| ECPerTDSecond | 2 | ≥0（**現行の計算式では未使用**、互換保持） |
| TDBlanketIdleECPerSecond | 2 | ≥0。cfg の `TDBlanket/IdleECPerSecond` が上書き |
| TransAMRatio | 3 | **≥1**。Power の倍率に使用 |
| MaxTorqueNm | 10000 | ≥0 |
| ParticlePerNmSecond | 0.0001 | **>0** |
| FieldReferenceDiameter | 10 | **>0** |
| FieldParticleRate | 1 | ≥0 |
| Individuality | 0.5 | 0..1（>1 は例外） |

`GNModule` の cfg（`MODULE { name = GNModule }`）は同名の `[KSPField]` として露出（1179–1231）。`GN_PROFILE` ノードは `variant = <PartVariant名>` 単位で Power / MaxGContribution / ParticleGenerationRate / ECPerParticle / ECPerTDSecond / TransAMRatio / MaxTorqueNm / ParticlePerNmSecond / FieldReferenceDiameter / FieldParticleRate を上書きする（1430–1451）。演出値（RotorSpeed, RotationAngle, MoveDistance など）は `VisualValue()` が GN_PROFILE → ModulePartVariants の順に参照し、負値・非数は `FormatException`（1252–1266）。**Transform名から性能は推測しない。**

## 3. 状態モデル

`DriveState`（121–164）。

- 派生状態:
  - `Original = Definition.TD && TDQuantity > 0`
  - `UsesTDBlanket = Generation && TDBlanket && TD`
  - `TDFraction` = `TDQuantity / TDCapacity` を 0..1 にクランプ（UsesTDBlanket かつ量>0 の時だけ正、それ以外 0）
  - `Full` = `Tank >= TankCapacity - max(1e-9, TankCapacity*1e-8)`（capacity>0）
  - `Running` = `Propulsion && Engine && !Repose && !Depleted && TankCapacity > 0`
- 資源の実体は船単位の `GNparticle` / `ElectricCharge`。Part の `Tank`/`TDQuantity` は Full・Depleted・TDFraction 判定にのみ使う（`ReadResources` 1452–1463）。
- `VesselSettings`（165–169）既定: `SafeGuard = true`, `Field = false`, `AccelerationG = 5`, `RotationShare = 0.2`, `HoverTime = 0.5`, `BrakeTime = 0.5`, `Deadband = 0.02`。
- 船設定の正（leader）は `persistentId` 最小の `GNModule`（1929–1934）。Rebuild 時にその PAW 値を採る。

## 4. 同期（MultiDrive / Individuality）

- `GNIndividuality.FromPersistentId(id)`: Murmur 系の avalanche（+0x9E3779B9, shift/xor, 0x85EBCA6B, 0xC2B2AE35）後、1024 桶で量子化。衝突は設計上許容（199–216）。`Individuality < 0`（既定 -1）なら起動時と `OnCopy` で自動採番（1314, 1334）。
- 同期係数 `Coefficient`（170–190）: 対象 = `Joined && !Repose && MultiDrive && Group == 0`。個体値を昇順ソートし、

```
factor = n * (1 - clamp(4 * Σ(x - mean)^2 / (n - 1), 0, 1))^n      // n>=2
factor = 1                                                        // n<2
```

  分散が最大（0/1 極端）なら 0 に、一致なら 1 に近づく。対象外は `Sync = 1`。
- `Sync` の影響先: Power、粒子生成 Rate、MaxTorqueNm、Blanket待機EC、色・演出の元になる同期表示。

## 5. 支持（Hover / AG / Brakes）

`HoverLaw.Support`（218–233）:

```
gravity == 0            -> 0                      // ゼロGで支持力なし
v = |verticalSpeed| <= deadband ? 0 : verticalSpeed
a = max(0, gravity + clamp(-v / max(dt, responseTime), -aLimit, +aLimit))
```

`PropulsionController.Plan`（691–765）での適用:

1. `MaxG = Σ MaxGContribution(Running)`。
2. `EffectiveAccelerationG = clamp(settings.AccelerationG, 0, MaxG) * TA倍率`。**TA倍率は現行実装で 3 固定**（`GNTransAMUnit.AccelerationMultiplier` は `TransAMRatio` を参照しない。523–526）。PAWスライダーの通常上限は TA 適用後に効くため、通常モードの上限でブーストが打ち消されない（698–702）。
3. `power = Σ Power(Running)`、`aLimit = EffectiveAccelerationG * G`、`linearLimit = min(power, MassKg * aLimit / 1000)`。
4. `support = Hover || AG || input.Brakes`。重力>0 のときのみ支持。`up = -gravity.Unit`、`verticalSpeed = Dot(Velocity, up)`。
5. 応答時間: Brakes は `BrakeTime`、Hover は `HoverTime`。**Brakes は deadband 0**（微小速度も除去する）、Hover は deadband を維持（710–715）。
6. `SupportKN = up * min(linearLimit, a * MassKg / 1000)`。
7. Brakes: 支持済みなら速度の水平成分のみを `Mass/(max(dt, BrakeTime))` で殺す。上限は `linearLimit - used`。
8. 推進: `remaining = power - used`、`rotationReserved = Rcs ? remaining * clamp(RotationShare,0,1) : 0`。`thrustCapacity = min(linearLimit - used, remaining - rotationReserved)`。**未使用の回転予約は推進へ再配分しない**（750行コメント）。
9. 下方向抑止: 支持中はスロットルの下向き成分を除去。RCS の下向きは「AGのみ・上昇中（`Dot(Velocity,up) > 0`）」に限る（Hover では下向き不可）。合成方向は `Limit(1)`。
10. 回転: `torqueMax = Σ MaxTorqueNm * Sync`、`torqueCostMax = Σ MaxTorqueNm * Sync * ParticlePerNmSecond`、`torqueScale = min(1, rotationReserved / torqueCostMax)`。`TorqueNm = Rotation.Limit(1) * torqueMax * torqueScale`、`TorqueRate = |Rotation| * torqueCostMax * torqueScale`。
11. `ReservedPower = used + |ThrustKN| + rotationReserved`。

## 6. GN Field

- 対象探索（`ReadInput` 2080–2096）: `FlightGlobals.VesselsLoaded` を id 順。自船 / packed / 未ロード / rootPart・rb なしを除外。半径は相手側の `GNDriveV3Vessel.Radius`、非GN船は **2 m 固定**。
- 侵入量 `penetration = Radius + otherRadius - |offset|`、>0 のみ。
- 反発加速度（543–552、既定 spring=2, damping=1, maxAccel=20 m/s²）:

```
F = normal * otherMass * clamp(spring*penetration - damping*Dot(relVel, normal), 0, 20)
```

- 維持費（539–541）: `FieldParticleRate * (diameter / FieldReferenceDiameter)^2`。複数 Unit では **最小値を採用**。diameter は 2×実測半径（`MeasureBounds` 2099–2114、0.5 秒間隔、ParticleRenderer と GN V3 製 Renderer は除外、下限 0.5 m）。
- 反発の粒子コスト: `demand = Σ 2 * |F| / 1000`（粒子/s）。`fieldPower = max(0, Σ Power(field units) - ReservedPower)`、`scale = min(1, fieldPower / demand)`（751–762）。
- 空力保護: Field 有効時 `ShieldedFromAirstream = true` かつ `maximum_drag / minimum_drag / dragScalar = 0` を保存して適用、停止時に完全復元（2166–2190）。**自然抗力は GN の支出に不算**、独自抗力のみ加算:

```
drag = 0.5 * atmDensity * v^1.5 * 0.2 * π * Radius^2      // v > 0.1 かつ atmDensity > 0
```
 （V2 経験式の暫定継承。2151–2160）
- 状態表示（990–1001）: `Requested: inactive/packed vessel` / `OFF: maintenance particle shortage`（この時 settings.Field を false に落とす）/ `OFF: no active Field unit` / `Active: repulsion particle-limited` / `Active: Power-limited` / `Active`。

## 7. 配分（Allocator.Allocate、787–817）

予算 `budget`（粒子）に対する優先順は固定:

1. **支持** `SupportKN.Limit(budget/dt)`
2. **Field維持費**（`FieldMaintenance * dt`）。支持と合わせて払える時にのみ Field を立てる。**維持費は force/Power のコストではなく粒子予約**。
3. `motionBudget = budget - maintenance` の中で **ブレーキ**（`Fit` で素朴二分 60 反復、`|Support + x*Brake| * dt <= budget`）
4. **推進 + 回転**（同一スケール `s`。コスト `|base + x*thrust|*dt + x*TorqueRate*dt` は凸なので x=0 から上端を二分。逆向き RCS で非単調になっても解ける）
5. **Field反発**（残り予算で `FieldRepulsionRate` と各 Target の力を同じ scale で縮小）

`cost(1) <= budget` ならスケール 1。既に予算以上を消費している且つ内積>=0 なら即 0（無駄な二分を省く）。

## 8. 1 tick の資源決済（VesselSimulation.Step、831–1012）

前処理: 入力検証 → `Id` 順ソート・重複 Id 拒否 → `Definition.Validate()`・残量非負 → `Full` なら `TALocked = false` → `Generated / BlanketECGenerated / BaryonMassKg` を 0 クリア → `StateRules.Reconcile` → `GNMultiDriveUnit.Update` → `PropulsionController.Plan`。

`Receipt(actual, requested)` が**すべての実取得/実受入を要求範囲に強制**（`actual < -1e-9` や `actual > requested` は `InvalidOperationException("Invalid resource receipt")`）。

```
1) 生成計画   wanted = demand(plan.Cost) + pFree
              generation[d] = min(wanted, ParticleGenerationRate*Sync*dt)
              [Blanket且つ TD>50%] netECPerParticle = ECPerParticle*(2*TDFraction-1) > 0
                 chargingAmount = min(generationCapacity, chargingECFree / netECPerParticle)
                 amount = max(amount, chargingAmount)          // EC空きが埋まるまで満運転
              予約枠は chargingECFree / idleECFree として後続 Unit へ繰り越す
              （先行 Unit が埋めた EC空きを別 Unit が二重予約しない。894–897）
2) 事前検証   demand / ForceKN / TorqueNm が非負・有限であることを
              最初是不可逆リソース書き込みの前に確認（898–902, 951–953）
3) Blanket要求 activeDemand = generation*ECPerParticle*2*TDFraction
              generation==0 かつ Blanket ON なら idle = min(idleECFree, IdleECRate*dt)
              baryons.TakeEnergy(要求) -> Receipt -> blanketScale = supplied/requested
              generation *= blanketScale / BaryonMassKg = kgForEC(supplied)
              不足時: Blanket=false, BlanketStarved=true, Generate=false（ラッチ）
4) EC         requiredEC = Σ generation*ECPerParticle
              ecNeed = max(0, requiredEC - 自家発電)   -> ec.Take(ecNeed)
              ecScale = (自家発電+取得)/requiredEC -> 生成量を縮小、ECUsed = requiredEC*ecScale
              余剰 EC は ec.Put -> ECStored / ECSpilled
5) 粒子       available = Generated + (SafeGuard ? 0 : pStart)
              settlementBudget = max(割当コスト, 支持*dt + Field維持*dt)
                 → 優先予約を下支いする額だけ particles.Take（不足分のみ）
              paid = Generated + ParticleTaken
              Effects = Allocator.Allocate(plan, paid, dt)         // 実支払基準の第2パス
6) TA         SupplyLimited（demand > paid）で TA 中なら ForceOff → 再Plan → 再Allocate
              Gen1 は ForceOff 時に TALocked = true（再充填は Full まで不可）
7) 確定       Covers(effectCost, paid) を必須検証 → Consumed = min(effectCost, paid)
              余剰粒子は particles.Put -> ParticleStored / ParticleSpilled
8) ラッチ     SafeGuardでなく、能動・非packed、要求出力>0、実出力==0 の全 Running Unit に
              Depleted = true, Engine = false, TA = false
```

制約として明示されている点:
- SafeGuard は「備蓄粒子を消費しない・生成物を貯蔵に回さない」而非「生成を止める」。生成が全 OFF なら SafeGuard 自体が自動降ろされる（268–269）。
- 丸めで clipped するのは `Covers` 相当の rounding のみで、実不足を消すことはしない（985–986）。
- TD は粒子/EC の独立生成源ではない。**生成経路は TD Blanket の質能変換のみ**（945–948）。

## 9. 命令系

- `CommandKind` = Engine / AG / Hover / TransAM / Generation / Blanket / Join / Repose / SafeGuard / Field（235–237）。
- `CommandGate.Accept(tick, scope, kind)`: `kind + ":" + scope` を key に**同一 UniverseTime の重複受付を拒否**（239–249）。scope は `Enqueue` で決まる（1911–1920）:
  - Field / SafeGuard → `"vessel"`
  - 同調中（Join/Repose 以外） → `"group0"`
  - それ以外 → Part の Id
- 受理された命令は `pending` に積まれ、`FixedUpdate` の先頭で `Commands.Apply` が一括適用。**同時競合は Id 順で最初に状態を変えた側が勝つ**（1972–1988）。
- 制約（`Commands.Check` 274–291）:
  - Engine ON: Propulsion 必須、Repose 不可、タンク容量>0、**Depleted 時は自タンク満タンでないと起動不可**
  - AG / Hover: Propulsion 必須、Repose 不可
  - Trans-AM ON: 定義あり・Running・(`!TALocked || Full`)。OFF: **Gen1 同乗時は手動解除を拒否**
  - Repose ON: TA 中は不可
  - Blanket ON: `UsesTDBlanket` のみ
- 排他・連動（`Set` 293–334, `StateRules.Reconcile` 257–270）:
  - Hover ON ⇔ AG OFF（相互に排他）。Engine OFF は TA を暗黙に解除しない（Gen1 を含む）
  - Repose ON で Joined / Engine / AG / Hover / TA を全て落とす
  - `Blanket` は `UsesTDBlanket` でない限り常に false。`BlanketStarved` ラッチ中は Generate/Blanket とも false
  - 通常は `Blanket = Generate`。**Blanket は Generation の互換エイリアス**（旧 AG/craft データ用、可視操作は Generation のみ。320–324）
  - `Generation` 自体に拒否条件はない（TA中でも受付可、待機出力は継続）。`Repose` 中のUnitは同期対象から除外される
- Join（346–397）: 既存の `Joined && !Repose && Group==0` の最小 Id を leader として Engine/AG/Hover/TransAM/Generation を**事前 Check してから**コピー（TAコピー時のみ一時的に Engine=true, Depleted=false を仮定して検証）。成功後 `Joined = true, Group = 0`。
- 同調中の命令は同一 Group の非 Repose 全員へ展開（402）。Repose だけ個別適用。
- 保存済み `Repose` で起動した Unit は、OnStart 時に Engine / AG / Hover / TA / Joined を強制的に落としてから起動する（1394〜1397行）。

## 10. KSP 資源アダプタ

`KspResourcePort`（1649–1766）:

| flowMode | 挙動 |
| --- | --- |
| `ALL_VESSEL` | `persistentId` 順の `PartResource` を自前走査。`CanTake` = `flowState && (Both|Out)`、`CanPut` = `flowState && (Both|In)`。`Capacity/FreeSpace` は**受入可能タンクのみ**（native の pulling=false 照会と一致）。`Take/Put` は `amount` を直接増減し、実績（要求−残り）を返す |
| `STAGE_PRIORITY_FLOW` | 走査しない。`GetConnectedResourceTotals(..., pulling)` で照会、`RequestResource(id, ±amount, flowMode)` に決済委譲。Put は負要求で、返り値の符号を反転して受入量とする |
| その他 | `NotSupportedException("<name>: unsupported resource flow ...")`。独自の一般流路グラフは実装しない |

`KspBaryonPort`（1768–1833）: rootPart 経由で `GNDRIVE_SETTINGS/TDBlanket/Resource` の各資源を `GetConnectedResourceTotals` + `RequestResource` で消費。`ECPerUnit = density * c^2`。超少要求対策として `MeasurableQuantum(amount) = max(1e-18, 2*ULP)` を前払いし、未使用分は `energyCredit` に繰越。受入量は `requestedEC` を超えず、不正な返り値は例外。

定義缺失・密度非正の資源は `Debug.LogWarning` でスキップ（起動は止めない）。

## 11. 船コーディネータ（GNDriveV3Vessel）

- `VesselModule`。`ShouldBeActive = vessel != null && vessel.loaded`。
-購読イベント: `onVesselWasModified`, `onVesselGoOffRails`（→ dirty）, `onVesselGoOnRails`（→ dirty + 保護解除 + Field非表示）, `onVesselChange`（非アクティブ時 保護解除/非表示）, `onPartDie`, `onPartResourceListChange`（→ dirty）。
- `Rebuild()`（1922–1954）: 保護解除 → glow Rebuild → `GNModule` を persistentId 順に収集 → **いずれかに `ConfigurationError` なら例外**（＝当該船の GN 停止）→ leader から SafeGuard/Field/AccelerationG/RotationShare を採用 → `KspResourcePort("GNparticle")` / `KspResourcePort("ElectricCharge")` / `KspBaryonPort(...)` を再構築 → 重複なし Rigidbody 集合を更新。
- `FixedUpdate()`（1955–2048）: `LoadedSceneIsFlight && vessel.loaded && !failed` のみ。`Planetarium.GetUniversalTime()` が前回と同じなら何もしない（**1物理tick=1回計算**）。順序は `dirty→Rebuild`、`ReadResources`、スライダー競合解決、命令適用、`AccelerationG` を `Σ MaxGContribution(Running)` にクランプ、`ReadInput`、`simulation.Step`、`ApplyEffects`（非能動/packed では保護解除+Field非表示）、色・glow・残像の演出値算出、`Publish`。
- 例外時は `failed = true`、pending クリア、保護解除、Field非表示、全 Unit の status を `ERROR: <msg>`、`Debug.LogError("[GN V3] Coordinator stopped for <vessel>: ...")`。**同一プロセスでの自動復帰なし**（設定修正後に再ロード）。
- 物理適用（2115–2165）: 各 Rigidbody に `AddForce(acceleration * rb.mass, ForceMode.Force)`（KSP の rb.mass はトン＝値として kN 換算と整合）。トルクは `TorqueNm * 0.001 * rb.mass / Σmass` で質量按分。Field 反発も対象船の rb に質量按分で加力。
- 演出は `LateUpdate`（2203–2217）と `GNModule.OnUpdate`（1521–1525）だけ。**描画更新で物理・資源を変えない**。

## 12. 演出契約

- 名称契約（`VisualPolicy.Named` = 前置 + 数字、2788–2810）:
  - `GN_Rotor_<n>` または `RotorTransform` 名 → 回転子（`GN_Stator_*` / `GN_CondenserRotor_*` / `GN_CondenserStator_*` は除外）
  - `GN_Core_<n>` または `DeployTransform` → 展開（`-Y` 方向へ `MoveDistance`/`DeployDistance` まで `CoreSpeed`）
  - `GN_Arm_<n>` → 腕（`RotationAngle` まで `ArmSpeed`、SafeGuard 中は開かない）
  - `GN_CondenserRotor_<n>` → **自タンク充填率のみに連動**（推進・TA・SafeGuard と独立）
  - `GNLight` / `GNLight_<n>` → Light（通常は `GNLightIntensity * level`、TA で `TransAMIntensity` / `TransAMRange` / `TransAMColor` へ補間）
  - `ParticleTransform`（既定 `EMI`、**空文字で粒子制御無効**）→ 生成アンカー。Unity cone の +Z をモデル契約の **−Y** に回転。既存 `KSPParticleEmitter` は `emit=false, enabled=false` で恒久退役
- 色（`DriveColorPolicy` 595–624）: Original = `(0, 1, 0.65)`（緑）、非 Original = `(1, 0, 0.15)`（赤）、混在 = 赤基調。船共有色は Original/非Original の分布で `(1,1,0)`。Blanket 稼働中は緑で上書き（2023–2024）。`GeneratorColor = "r,g,b"`（各 0..1、非数は `FormatException`）。遷移は `VisualColorTransition`（既定 1.5 s、目標が変われば再開始、554–593）。
- 粒子: 生成 PS は 4096 上限、`SafeRate = min(rate, capacity*0.8/lifetime)`。idle↔full を `particleLevel` で補間（速度・発生率・円錐角）。寿命 0.05..10 s。加算合成の 32×32 生成テクスチャ。リンググローは `ParticleGlowRate/Size/Intensity`。`ParticleVisualPolicy.BackflowAcceleration = -Force.Unit * magnitude`（実力に反発させる）。
- 音: `AudioPath` があれば `AudioSource`（loop / spatialBlend=1 / playOnAwake=false）、`volume = rotorLevel * GameSettings.SHIP_VOLUME`。
- 船全体の演出（`GNVesselRedGlow` / `GNAfterimageVisuals`）: Trans-AM 時にみ点灯。残像は `AfterimageCount`（0..16）、間隔 `2*Radius / AfterimageSizeDivisor * AfterimageSpacing * (i+1)`、`AfterimageExtendTime` で伸長・release は同じ曲線で巻き戻し、`AfterimageThreshold` m/s² 以上で有効。`ExcludeObjectKeyword` で除外。Part コンポーネントやコライダーは複製しない（2250 行コメント）。
- `GNAeroVisualSuppression` は Field 表示中の排気・空気力可視化を抑止（`ShouldSuppress` は owner / packed / failed を見る）。

## 13. 設定ファイル `GNDRIVE_SETTINGS`

`GameData/GNDriveV3/GNSettings.cfg`。**ノードが 2 つ以上あれば例外**（TDBlanket側1038、VesselGlow側1097）。読み取り失敗は既定値にフォールバックして `Debug.LogError`。

| キー | 既定 |
| --- | --- |
| `TDBlanket/IdleECPerSecond` | 2 |
| `TDBlanket/Resource`（複数可・先頭から消費） | IntakeAir, MonoPropellant, LiquidFuel, Oxidizer |
| `GNLight/Enabled, Intensity, Range, TransAMIntensity, TransAMRange` | true, 10, 5, 20, 10 |
| `VesselGlow/Enabled, Intensity, Transparency, ShellScale, ResponseSpeed` | true, 2, 0.8, 1.02, 2 |
| `VesselGlow/Afterimages*`（Enabled, Count, SizeDivisor, Spacing, Opacity, Threshold, ExtendTime） | true, 4, 8, 1, 0.3, 0.5, 1 |
| `TransAMColor`, `TransAMCoreColor`, `TransAMCoreIntensity` | 1,0.05,0.25 / 1,0.95,0.95 / 4 |

## 14. 永続化と Version Gate

- `schemaVersion`（既定 1）**不一致は `Unsupported GN state version`**（1328–1330）→ その Part は `ConfigurationError` となり、Rebuild 時に船ごと停止。
- 永続フィールド: `schemaVersion, initialized, internalEC, internalParticles, selectedProfile, engineOn, agOn, hoverOn, taOn, generationOn, blanketOn, blanketStarved, joined, group, repose, depleted, taLocked, safeGuard, fieldOn, accelerationG, rotationShare, Individuality`（既定 -1）および粒子・ローターの調整値。
- `initialized` が false の時だけ `InitialInternalEC` / `InitialInternalParticles` を採用（以後は保存値）。
- `OnSave` は `State` の最新フラグを公開フィールドへ書き戻してから `base.OnSave`（1502–1520）。**Unit/Part/Vessel で状態の二重所有をしない為の単方向コピー**。
- `OnLoad` で `GN_PROFILE` を拾い、エディタでは `ModulePartVariants` の選択名を `selectedProfile` にして `ReadProfile()`。Variant 切替は `onVariantApplied` / `onEditorVariantApplied` 経由（**飛行中は性能を変えない**）。
- `Publish()`（1464–1500）で状態・率・スライダー上限（`maxValue = result.Effects.MaxG`）を PAW へ反映。status 文言:
  `Ready` / `Standby` / `Running` / `Supply limited` / `Trans-AM supply stop` / `Depleted: fill own tank, then ON` / `Repose` / `GN vessel coordinator unavailable` / `Coordinator stopped; reload after fixing configuration.` / `ERROR: ...`
  Blanket 状態: `OFF: no baryons` / `OFF` / `ON: no TopologicalDefects` / `Active: <n>% TD`。

## 15. 現行ソース上の未使用・要注意点

| 項目 | 状態 |
| --- | --- |
| `TransAMRatio` | `Power` の倍率にのみ使用。**支持加速度の TA 倍率は 3 固定**（`AccelerationMultiplier`） |
| `ECPerTDSecond` | 検証対象に含まれるが計算式では未使用（TD が生成源でなくなった名残） |
| `InitialInternalEC` / `InitialInternalParticles` / `InternalEC` / `InternalParticles` | 保存・復元されるが現行の決済・物理計算から参照されない |
| `DriveState.Group` | 0 以外の扱いがない（同期は Group==0 のみ） |
| `blanketOn` の PAW/Event/Action | 非表示（`ToggleBlanket` は `ToggleGeneration` へ委譲）。互換データ用 |
| Part 単位の粒子タンク | 生成物は船共有タンク側で決済。Part の Tank/TD は Full・Depleted・TDFraction 判定用 |
| KSP 実流路の検証 | `ResourceAdapterTests`（11件）は API ダブルに対する契約試験。KSP 内部の流路アルゴリズム自体は未検証（`Validation.md` の明示範囲） |


---

# 後編：調査で残った疑問と修正提案

上記は実装の実態そのまま。以下は「読んで不可解だった／実害が出そう」と判断した項目を、原因行とセットで並べたもの。**提案であり未適用**（コードは変更していない）。前編 §15 の「未使用・要注意点」のうち大半は v0.2 本文が意図を明記していたため、**D 群で「修正不要」へ格上げ**している。残った A〜C が本当に手当てが要る候補。

## A. 仕様と実装が食い違っている

### A-1 「独立した待機発電率は設けない」に対して待機発電が存在する
- 仕様: v0.2 §11「旧`ECPerTDSecond`はパーツcfg互換用に読み込むが計算には使用しない。**独立した待機発電率は設けない**」
- 実装: `DriveDefinition.TDBlanketIdleECPerSecond`（既定 2、103 行）に `GNDRIVE_SETTINGS/TDBlanket/IdleECPerSecond` を乗せて `GNTDBlanketUnit.IdleECRate`（512–518）が毎 tick EC を出す。生成が 0 のときだけ（911–917）で、粒子を変換しない純粋な待機出力。
- どちらを直すか: 待機発電は 2026-10-03 の TD Blanket 再実装（`20261003-21*〜231054-td-full-charge-final` バックアップ列）で入った実機能なので、**仕様側を v0.3 に反映する**のが妥当。v0.2 §11 の一文を「待機発電率は `GNDRIVE_SETTINGS` の 1 値のみ。Variant・Unit ごとに独立した値は設けない」へ書き換える。
- 併せて: 旧 `ECPerTDSecond` は実機能としては消滅済み。`GN_PROFILE` の上書き対象（1437）から外すと、Variant 間で無意味な差異を書けなくなる。

### A-2 TD 0 % の生成停止と「Repose 中は TD がなくても生成継続」の矛盾
- 仕様: v0.2 §14 検証方針に「TDブランケットの**0 % 停止**」、しかし §10 Repose に「**TDがない場合も、Repose中はEC供給範囲で粒子生成を継続する**」。同じ v0.2 内で要求が割れている。
- 実装: `GNParticleGenerationUnit.Rate` は `UsesTDBlanket && TDFraction <= 0` で 0（505–507、コメント「EC への暗黙フォールバックを許さない」）。§14 側を採用している。
- 影響: Original 機が TD を使い切ると生成・Blanket とも全停止。`StateRules.GenerationRequested`（253–255）は定義フラグしか見ないので **SafeGuard は ON のまま**（`Behavior_Changes_v0.3` の「SafeGuard は生成要求がある限り ON を維持」規定どおり）。結果として **SafeGuard ON かつ TD 空だと、粒子タンクが満船でも支持・推進・回転・Field が全部 0**（`available = Generated(0) + 0`、966 / 971 行）。PAW には `ON: no TopologicalDefects` と `Supply limited` が出るだけで、「TD が無い」と「SafeGuard が備蓄を使わせない」の组合が利用者に伝わらない。
- 提案: (1) 仕様は §14（0 % 停止）で確定させ、§10 の文を「TD を持つ Generation 機は TD 枯渇で生成停止する」へ修正。(2) `StepResult` に `GenerationBlocked` を立て、`status = "Generation blocked: TD 0% (SafeGuard ON)"` を出す。(3) 50 ケース表に「TD 0 % ＋ SafeGuard ON ＋ 粒子タンク満」を追加（21–25 群の未使用番号）。

## B. 実装バグ候補

### B-1 `AccelerationG` のクランプが永続設定を 0 に潰す
- 症状: 全 Unit が非 `Running`（Engine OFF / Repose / Depleted / タンク容量 0）の tick が 1 回でもあると `settings.AccelerationG` が 0 になり、`Publish` 経由で各 Part の永続フィールド `accelerationG` に書き戻されたまま保存される。エンジンを入れ直すとスライダー上限 `MaxG` は復活するが**値は 0 のまま**で、`linearLimit = min(power, Mass×aLimit/1000) = 0` となるため支持も推進も回転も出ない。PAW スライダーを手動で上げるまで復旧しない。
- 原因: 1990 行 `settings.AccelerationG = Numbers.Clamp(settings.AccelerationG, 0, states.Where(d => d.Running).Sum(d => d.Definition.MaxGContribution));`（一時的な稼働状況の反映）＋ `Publish` 1482 行（設定への書き戻し）。`PropulsionController.Plan` 698 行に `clamp(settings.AccelerationG, 0, p.MaxG)` が既にあり、**表示用の切り捨ては Plan 側だけで足りる**。
- 仕様: v0.2 §4.3「スライダー上限は ON ドライブの最大G寄与の合計」「0 なら支持も不可」＝**上限表示と 0 の意味**を決めていて、保存値を上書きするとは書いていない。よって実装側を直す。
- 修正: 1990 行の代入を削除（クランプ結果は plan / PAW の `MaxG` にのみ反映）。局所変更で、コア試験に載せられる。
- 試験: 「Engine ON（加速度 5）→ 全 Engine OFF で 5 tick → Engine ON」で `accelerationG == 5` が保たれること。

### B-2 1 つの設定ミス Part が船全体の GN を止める
- 症状: `Rebuild` は `modules.Any(m => m.ConfigurationError != null)` で例外（1931–1932）→ `failed = true` で当該船の GN が恒久停止（自動復帰なし、2033–2048）。`schemaVersion != 1` は `GNModule.OnStart`（1328–1330）で例外にするため、旧バージョンの Part が 1 個混ざると船全体が死ぬ。
- 仕様: v0.2 §13.10 は「**流路・KSP API** に問題が起きた場合はログと状態表示を出して当該管理を停止する」。Part の cfg・schemaVersion を船単位で止める根拠はない。
- 修正: 設定エラー Unit は「計算対象から除外＋PAW に赤 status」で隔離し、正常 Unit だけ継続する。船全体の停止は資源アダプタ例外など、続行すると資源を壊す場合に限定する。

### B-3 同一 UniverseTime で tick を落とす
- 症状: `if (tick == lastTick) return;`（1960–1962）。同一 UT で `FixedUpdate` が複数回走る状況（サブノード境界・ラグ回収）で決済を 1 回にまとめるが、`Dt` は `TimeWarp.fixedDeltaTime` のままなので**進んだ時間分の資源・物理が消える**。
- 修正: 「経過 UT ≥ 前回適用 UT ＋ Dt」で発火させるか、スキップ時に実経過を拾って `Dt` に反映する。`Validation.md` の「高速 Warp」項目と同時に確認する。

## C. 仕様判断が必要な項目（コードより先に方針を決めたい）

### C-1 質能変換による実質無限 EC
`ECPerResourceUnit = density × c²`（446–450、1 EC = 1 kJ 固定は v0.2 §13.3 で決定済み）。`LiquidFuel` 1 単位 ≈ 4.5×10¹⁴ EC なので、TD > 50 % の Blanket が払う baryon は測定不能レベル。**TD 残量がある限り EC は実質無限**（収入は `ParticleGenerationRate × (2·TDFraction − 1) × ECPerParticle × Sync` と EC 空きで頭打ち）。物理換算は仕様どおりで正しいので、絞るなら `BlanketEfficiency` のような明示的な倍率キーを増やし、「E=mc² を満效率で使う」という前提を cfg で選べるようにする。効率を入れると `netECPerParticle`（885）と `activeDemand`（908）の符号境界が TD 50 % から動くため、しきい値も cfg 化が要る。

### C-2 反発力の質量基準
`GNFieldUnit.Repulsion` は `normal × otherMass × clamp(spring·penetration − damping·v_n, 0, 20)`（543–552）。侵入量・接近速度は自船側の量なのに係数が**相手質量**で、重い相手ほど大きな力を得る（加速度は 20 m/s² 上限で揃う）。v0.2 §9 は「反発の接近速度・侵入量からの式、ばね・減衰・最大加速度は調整値」としか決めておらず、質量の扱いが空白。換算質量 `m₁m₂/(m₁+m₂)` にするか、質量依存をコスト側へ移すかを決め、50 ケース表に「巨大船 vs 小舟の非対称」を入れる。
※「相手に F を加え、自船への反作用は補償済み」（v0.2 §9）という決定自体は生きているので、**反作用の非対称は修正不要**。問題は質量基準だけ。

### C-3 Field 半径が外接球で過大になる
`MeasureBounds` は CoM から Renderer bounds の**角**までの最大距離（2099–2114）。細長い船だと全長の半分に相当し、維持費は `(直径/FieldReferenceDiameter)²` で二乗効く（539–541）。v0.2 §13.6 が「境界の過大推定を実機確認」と留保している通りで、**仕様上の留保どおりだが実用上きつい**。主軸方向の半径、または体積等価球半径へ置き換える判断が要る。

### C-4 同調係数 0 で全出力が消える（仕様どおりだが警告がない）
`Coefficient = n(1 − clamp(4s²/(n−1),0,1))^n`（170–190）は v0.2 §8 の V2 式そのもの（個体差ゼロで各機 n 倍＝合計 n² 倍、不一致で 1 未満・ゼロになり得る）で、**ゼロも仕様の想定内**。ただし個体値が persistentId ハッシュの 1024 桶（199–216）なので、2 機を同調させた組の約 8 % が偶然ゼロ、平均係数は 1.17 程度（自作の一様分布シミュレーション。ソースの挙動そのものは実機で要確認）。ゼロだと支持も生成も 0 で、`Synchronization = 0.000` という PAW 表示だけが手がかり。仕様維持でよいので、**係数が一定値未満のときに警告文言**を出す提案だけおく。

## D. 仕様どおり・修正不要（読みづらさだけ）

| 項目 | 仕様側の決定 | 実装 | 提案 |
| --- | --- | --- | --- |
| TA 中の加速度倍率が 3 固定 | `Behavior_Changes_v0.3`「Trans-AM acceleration change」で *exactly 3 / cfg TransAMRatio 独立* と明記済み | 523–526 | 変更不要。`AccelerationMultiplier` のコメントに仕様節参照を追記 |
| Field 反発に自船反作用なし | v0.2 §9「自船への反作用は補償済みとする」 | 2139–2151 | 変更不要 |
| `BlanketStarved` で Generate までラッチ停止 | v0.2 §6「補給後の Generation ON で再始動」 | 937–943 | 変更不要（status 文言のみ改善余地） |
| Hover の deadband で微小速度が残る | v0.2 §4.2「速度デッドバンドを設ける」 | 228–230 | 変更不要（Brakes 側は v0.3 で deadband 0 化済み） |
| 非 GN 船を半径 2 m 近似 | v0.2 §13.5 で留保済み | 2087 | 変更不要（改善候选） |
| 粒子 = 1 kN·s の暗黙 | v0.2 §5.1「並進粒子費用 C = \|F\|[kN]×Δt」、§9「反発は 2F」 | 474–478 / 757 | 定数 `ParticlesPerKN` / `FieldRepulsionMultiplier` に抽出するだけ |
| `ECPerTDSecond` 未使用 | v0.2 §11「cfg 互換用に読み込むが計算には使用しない」 | 111 / 1437 | **削除しない**。コメントで「cfg 互換専用」と明記 |
| `InternalEC` / `InternalParticles` 未使用 | v0.2 §6「内部維持を閉じた循環として扱う。毎ステップ消費する詳細モデルは実装しない」 | 1338–1339 / 1389–1390 | **削除しない**。同上 |
| `DriveState.Group`（常に 0） | v0.2 §8「Vessel 内の Group0 一つを使用し、状態には Group 番号を保持する」 | 398 / 402 | **削除しない** |
| `blanketOn` 非表示・Generation へ委譲 | v0.2 §6「専用 Toggle は隠し、旧 craft/action 互換の呼出しだけ残す」 | 1319–1324 | 変更不要 |

## 着手順の提案

1. **B-1**（1990 行の削除）→ コア試験に保存往復ケースを追加。実害が最も大きく、差分が最小。
2. **B-2・B-3**（隔離と tick 条件）。既存 107 core ＋ 11 adapter の回帰に乗せる。
3. **A-1・A-2**（仕様文の更新）。コードは A-2 の表示追加だけ。`Behavior_Changes_v0.3.md` に「v0.2 §6/§10/§11 を上書き」として追記する。
4. **C-1〜C-4** は方針決定後。C-1 と C-2 はバランス直結なので、`Examples/*.cfg.example` の仮値更新も同時に必要。
5. 修正適用後、本書の行番号を再校正する（行番号は 2026-10-03 23:09 時点の作業ツリー基準。コミットでズレる）。

## 本調査の範囲外・未確認

- ビルド・テスト実行・KSP 実機動作は一切行っていない（現状の合格実績は `Validation.md` の記録が正）。
- `GNsword.cs`、V2 側ファイル（`GNDriveCore.cs` ほか）は未読。`GNsword` は本番ビルドに含まれるが本記録の対象外。
- Unity 演出部（2250 行以降）は契約名と cfg のみ確認。シェーダ・マテリアル・描画順序の妥当性は未検証。
- C-4 の平均係数・ゼロ確率は、個体値を一様分布と仮定した簡易シミュレーション（2 万試行・固定 seed 系）からのもので、`GNIndividuality` の実ハッシュ分布は未計測。
