# 独立した生成炉とTD Blanket（2026-10-04）

旧TD50%補充制限・粒子消費に連動したBlanket発電・TD色補間を置き換える実装仕様。

- GN Generation Furnace: HasGenerationとGeneration ONで稼働許可。目標は「当ステップ粒子需要＋受入可能な空き容量」とParticleGenerationRate×Sync×dtの小さい方。ECPerParticle既定値10。供給不足なら供給ECに比例して縮小。TDゼロでも外部ECがあれば稼働できる。
- TD Blanket: HasTDBlanket && HasTDで常時稼働許可。HasGeneration、Generation ON、Engine、Repose、Syncから独立。最大発電はBlanketECGenerationRate×TD充填率×dt。充填率0なら発電0。
- EC受入予算はEC空き容量＋生成炉の同ステップ消費予定。複数Blanketは実発電分だけこの共通予算を減らす。実際に供給されたバリオンエネルギー分だけ発電・質量消費し、不足は出力制限として表示。ラッチ停止せず次ステップで自動再試行。旧Blanket操作は拒否し、Generationのみ切替可能。
- 発電分を生成炉に優先的に割り当て、不足分を既存ECから取得。余剰はECタンクへ保存。物理効果の決済後の粒子余剰は粒子タンクへ保存する。内部の低/高エネルギー粒子はモデル化しない。
- 色: 生成炉の通常色はUsesTDBlanket && TD>0なら緑。それ以外は設定GeneratorColor。時間的な色フェード、船全体Trans-AM色の上書きは維持。
- GN_PROFILEでもBlanketECGenerationRateを上書き可能。旧ECPerTDSecondは互換のため読み込むが発電量計算に使わない。
- 移行初期値: 各ゲームpart cfgのECPerParticleを10、BlanketECGenerationRateをParticleGenerationRate×20として明示。50%で定格粒子生成のEC収支が釣り合う設定であり、コードに50%の特別分岐はない。
- 消費を同ステップで把握できるのは本コーディネーター配下の生成炉。外部MOD等のEC消費はKSPの更新順序を越えて予測せず、観測された空きへ補充する。

検証: Core 125件（ランダム収支2000ケース含む）、Adapter 11件成功。旧仕様を断定したテストを新仕様に置換したため、テスト件数は以前と異なる。Releaseビルド成功。実機の資源表示・色は未確認。
