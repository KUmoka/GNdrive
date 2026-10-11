# GN Drive V3: VS2022 / KSP 1.12.5確認手順

V3コード本体は `GNdrive/GNSystems.cs`。現行プロジェクトはV2のドライブ・物理・演出・シールド・VesselModule群をコンパイル対象から外し、V3の `GNModule` と独立機能の `GNsword` をビルドする。

## ビルド

こちらでVS2022付属MSBuildによるReleaseビルド、63件のコア試験、11件の資源アダプター契約試験は成功済み。成果物は `GNdrive/bin/Release/GNdrive.dll`。

2026-09-23修正: 標準ElectricChargeの `STAGE_PRIORITY_FLOW` を受け付け、KSP標準の取得・貯蔵APIへ委譲する。以前の「ElectricCharge: only ALL_VESSEL supported」で停止した場合は修正版DLLへ置き換え、KSPを再起動する。失敗済みのVessel coordinatorは停止を保持するため、古いゲームプロセスのままの差し替えでは復帰しない。

VS2022ではルートの `GNdrive.sln` を開き、Release / Any CPUでビルドできる。参照DLLは既存設定の `C:\Game\Kerbal Space Program\KSP_x64_Data\Managed` を利用する。

**既存プロジェクトにはビルド後のGameDataコピー処理がある。** 今回こちらでのビルドでは次のオプションで無効化した。VSの通常ビルドを行う前に「プロジェクトのプロパティ → ビルドイベント」でコピー先を確認する。

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  .\GNdrive\GNtech.csproj /t:Rebuild /p:Configuration=Release `
  /p:PostBuildEvent= /p:RunPostBuildEvent=Never /v:minimal /nologo
```

コア試験と資源アダプターの契約試験はUnity/KSPの起動なしで再実行できる。後者は実際のKspResourcePortをテスト用APIへ接続し、要求の符号・流路・実取得量の引き渡しを確認する。KSP内部の流路アルゴリズム自体を検証するものではない。

```powershell
.\tests\GNDriveV3\Run-Tests.ps1
```

または `tests/GNDriveV3/GNDriveV3.Core.Tests.csproj` をVS2022で開き、コンソールアプリを実行する。GNSystems.csの同じソースを `GN_CORE_ONLY` でコンパイルするため、テスト専用の別実装ではない。

## 独立テストPartを入れる

1. KSPを終了し、確認用セーブを用意する。
2. `GNdrive.dll` はゲーム内の従来のGNdrive DLLを置き換える。**別フォルダーへ同じDLLを追加して二重ロードしない。** 現行ビルドにV2クラスは含まれないため、使用するPart cfgは `name = GNModule` へ移行しておく。
3. `GameData/GNDriveV3/Examples/GNDriveV3Test.cfg.example` をテスト用KSPの `GameData/GNDriveV3/GNDriveV3Test.cfg` として配置する。ファイル拡張子末尾の `.example` を外す。
4. GNparticle / TopologicalDefectsの定義が既存GNdriveにあれば、そのまま使う。未導入時だけ `Resources.cfg.example` を同じフォルダーへ `Resources.cfg` として配置する。ElectricChargeはstockを利用する。
5. サンドボックスのエディターでEngineカテゴリの「GN Drive V3 Test (alpha)」を選ぶ。stock girderを流用した試験部品なのでGN Driveの外見・ローター・専用音は付かない。

既存船・既存Partへの自動パッチは含めていない。同一PartにV2推進/FieldとGNModuleを同居させない。既存全モデル/cfgの移植は今回の範囲外。

## 最初の試験

1. 操縦可能なカプセル等へテストPartを1個取り付けて発射台へ出す。
2. PAWでPropulsionをONにし、**Accelerationを1G以上へ設定**する。全ドライブOFF時はスライダー上限が0なので、ON後に設定が必要。Rotation shareは最初0、スロットル0で確認する。
3. Anti-gravity / Hoverを切り替え、支持力が重複しないことを確認。Hoverは高度固定ではなく鉛直速度制動である。
4. Generation ON時にTDブランケットも追随する。TDが0%なら生成不能、50%なら粒子生成ECとブランケット出力が釣り合い、100%なら設定されたバリオンを消費して粒子生成ECの2倍を供給する。TD>50%かつEC不足なら粒子満杯でも最大処理率で充電し、余剰粒子を廃棄する。TD<=50%では待機発電・自動補充をせず、実効果に不足する粒子だけを生成する。TD<50%の不足ECは船体から補助し、利用可能ECで生成量を制限する。粒子を生成しない間はバリオンも消費しない。粒子と通常ドライブ発光はTD 0%の`GeneratorColor`から100%の緑へ線形変化し、Trans-AMは本体発光だけ特殊色へ変える。実際に浮けるかは船の質量とPower/生成量による。
5. スロットル、Brakes、RCSスイッチ、並進・回転を順に確認。回転枠は入力がなくても通常推進へ戻らない。
6. SafeGuardをOFFにして粒子を減らし、部分出力、TA強制解除、枯渇停止、自己タンク満充填後の手動再始動を確認。
7. 2個以上を載せてSynchronizationへ参加。個体差0.5同士なら2台の係数は各2。非参加・推進OFF・Repose、およびGeneration操作が同調群へ適用され、対応機のTDブランケットも同時に切り替わることを確認する。バリオン枯渇時はGeneration/TDブランケットがともに停止し、補給後にGeneration ONで再始動する。
8. GN FieldをONにして近傍デブリ/EVA等への反発を確認。支持の次に維持費を確保する。反発費だけが不足する場合は反発を弱めて維持し、維持費不足時はOFFになり、補給だけでは再起動しない。複数Fieldアクションを同じアクショングループへ登録しても同tickで重複反転しないことを確認。
9. 保存/再読込、船切替、分離/結合、Warp、CargoBay共存を確認。非操作船は生成だけ継続し、浮遊支持を失う。
10. エディターのStandard/Boost Variantを切り替える。旧TD係数は互換用で計算には使われない。飛行中は性能を変更しない。

エラー時はPAWのGN V3 statusとKSP.logの `[GN V3]` を確認する。資源流路等の例外時は当該VesselのGN処理を停止する。設定を修正して再ロードする。未知の外部Mod変更まで含めた資源ロールバックは行わない。

## 構成と制約

- `GNSystems.Core`: 入出力・単位、命令、各Unit、優先配分、差引決済。外部資源操作はIResourcePort越しに一箇所で行う。
- `GNModule`: cfg、Part状態保存、Variant、UIと命令。`GNDriveV3Vessel`: 資源/物理/Field/キャッシュ。
- `KspResourcePort`: ALL_VESSELはflowStateとIn/Out/Bothを尊重し、安定Part ID順に直接amountを決済する。STAGE_PRIORITY_FLOW（標準EC）は、残量/空き容量の照会と実際の取得/貯蔵をKSP標準APIへ委譲する。対応外の流路はエラーにする。独自の一般流路グラフは実装しない。
- `GNDriveV3Visuals`: cfgで指定したローター・展開・発光Transform、モデル内粒子/Light、AudioPathを使用。Transform名から性能は推測しない。
- Vessel共通設定は各GNModuleへ同じ値を保存する。分離時は継承し、結合時は安定ID最小のモジュールの共通設定を採用する暫定規則。同調のON/OFF命令は以後Group0全員へ適用する。
- 生成器間で効率が異なる場合は安定ID順に生成枠を確保し、EC不足分は予定量を同率縮小する。効率最適化は行わない。
- Propulsion OFFとTA解除は独立。推進OFFではTA状態を自動解除しないため、Gen1の手動解除禁止を迂回しない。OFF中の推進Powerはゼロ。
- Fieldの空力保護は初期V2方式。保護前から遮蔽されているPartには介入しないが、保護中に他Modが所有を変更するケースの完全な共存は未保証。
- Fieldの描画は簡易透明球。V2のフレネル表現完全再現、未ロード生成、Kerbalism、新モデルは含まない。
- `GNDriveV3Vessel.LastResult` で直近の生成・取得・消費・貯蔵・廃棄実績を参照できる。PAWでは生成/sとVessel消費/sを表示する。

確認した結果と未確認項目の正本は `Validation.md`。


第2回テスト後の変更と追加確認手順は [Behavior_Changes_v0.3.md](Behavior_Changes_v0.3.md) を参照。
