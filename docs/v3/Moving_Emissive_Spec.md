# V3 可動・発光仕様案

2026-09-23。ユーザー指定により、スロットル・RCSだけでなく実際に配分された支持出力も演出へ反映する。GNSystems.csに実装済み。ゲーム内の可動・発光は未検証。

## モデル契約

制御用の空Transformを GN_Rotor_01 / GN_Stator_01 / GN_Core_01 / GN_Arm_01 の形式で命名する。番号は識別用で欠番可。各Variant配下に必要な個数を置き、省略可。同じ役割の名前はVariantごとに使用可能。初期localPosition/localRotationを格納姿勢として保存する。

- Rotor: 原点を回転中心、ローカルY軸回転。子メッシュは発光マスク対応材質。
- Stator: 回転せず子メッシュの発光のみ。
- Core: 初期位置から親座標系の方向へ移動。コア初期ローカル-Yを親座標へ変換した固定方向を使用。
- Arm: ヒンジを原点、初期姿勢からローカル+X回転で開く。初期回転に相対回転を合成する。
- 制御Transformのscaleは原則1。子メッシュで形状を調整。EMIをCore配下に置けば放出口も追従する。
- 非表示Variantも検出する。ローターは表示中のみ回転し、コア/アームは非表示中も初期姿勢を基準に演出状態を適用して切替時の古い姿勢を防ぐ。

## 演出出力

稼働中の共通演出出力Lを0..1とする。
入力値=max(スロットル, RCS有効時の並進入力ノルム, RCS有効時の回転入力ノルム)を0..1に制限。SAS由来の回転入力も含む。
支持負荷=配分後SupportKNの大きさ / 稼働ドライブの合計Power。分母が0なら0、結果を0..1に制限。
L=max(入力値, 支持負荷)。力の合成後ノルムを使わず、下向きRCSとの相殺で支持負荷を消さない。現行の船全体で配分する方式に合わせ、稼働ドライブ間では同じ支持負荷比を共有する。
これは入力と支持の演出であり、厳密な実出力計器ではない。推力計算、粒子消費、Power予約には影響しない。

- 回転・発光: 稼働中は0.1+0.9L、停止/Repose/枯渇停止は0へ滑らかに遷移。
- Core: 稼働中かつSafeGuard=falseならL、それ以外は0へ遷移。無入力かつ無支持では格納する。
- Arm: クランプなので稼働中かつSafeGuard=falseなら負荷に関係なく全開、それ以外は全閉。開閉途中は30度/秒で移動する。
- TDによるEC発生率が正なら推進OFF、Repose、枯渇停止中もローターは低速回転（既定6度/秒）。発光もアイドル強度を維持。EC満タンで余剰が廃棄されても発生率が正なら継続。TDなしの粒子生成だけではローターを回さない。可動部は格納。
- SafeGuard ONでも回転・発光には負荷を反映するが、可動部は格納する。
- 発光色は既存V3の色選択に従う。マスク/テクスチャを変更せず、対応する材質の発光強度を制御する。

## V2から確認した値とV3初期値

添付旧cfg: C:/Game/Kerbal Space Program/OldMod/GNdrive/parts/GNDrive_2500/GNdrive_2500.cfg

- MoveDistance = 0.2（モデルスケール1で最大0.2m）
- RotationAngle: Default/Exia 30度、NakedExia 0度、Astrea 15度、NakedAstrea 0度。
- V2 GNDriveCore.csはRotorSpeed=60度/秒を設定（RPMではない）。V3初期値も最大60度/秒、稼働アイドル6度/秒を案とする。
- V2 GNVisuals.csはアーム遷移速度30度/秒。V3も初期値30度/秒を案とする。

V2アームはMoveOnで全開角へ移動していた。V3もユーザーの訂正に従い全開/全閉とする。V2の位置/回転の絶対上書きは継承せず、Unity配置を基準に相対適用する。
V2のVariant読み込みはRotationAngle=0を無視していた。V3は明示的な0を有効値として扱い、設定なしの場合だけ共通値へフォールバックする。

対応するcfg設定は下記の実装済み一覧を参照。機構別の個別上書き・発光上下限の設定は今後の拡張項目。

## 設定方法（実装済み）

GNModuleに指定する共通設定:
```
RotorSpeed = 60
DeployDistance = 0.2
RotationAngle = 30
ArmSpeed = 30
CoreSpeed = 0.2
VisualResponse = 2
```
RotorSpeed/ArmSpeedは度/秒、CoreSpeedはモデル距離/秒、VisualResponseは演出レベル/秒。発光強度は0.1〜1の既定範囲で、個別メカニズム別上書きは未実装。
GN_PROFILE内のRotorSpeed、MoveDistance、RotationAngleでVariant別に上書きできる。RotationAngleだけは既存ModulePartVariantsのVARIANT直下の値にも対応。優先順位はGN_PROFILE > VARIANT > GNModule共通値。0を有効な上書きとして扱う。

```
GN_PROFILE
{
    variant = Astrea
    MoveDistance = 0.2
    RotationAngle = 15
}
```

命名対象の子Rendererに発光対応材質とマスクを設定する。MaterialPropertyBlockで_EmissiveColor/_EmissionColorを変更し、共有材質の色は書き換えない。モデルのAnimator等で同じTransformを同時制御しない。

初期配置を基準としてCoreは自身の初期-Y方向、Armは自身の初期+X軸に相対適用する。現在の対応はエディターでのVariant選択と飛行開始時の設定継承。飛行中のVariant性能切替には対応しない。

Museのレビューではスムージングと負荷比の分母を確認。アームが無負荷でも開く点への指摘はユーザー指定のクランプ動作のため変更しない。
## Latest model contract: sibling rotor/stator (supersedes stator compensation)

All drives use GN_Core_01 with sibling GN_Rotor_01 and GN_Stator_01 pivots. The stator must not be a descendant of any spinning rotor. V3 does not write stator position or rotation; its authored local transform follows its parent naturally. Rotor rotation uses local Y / Space.Self. Core and arm retain relative local motion; stator emissive control is unchanged. Removed all world-pose capture/restore arrays and hierarchy-depth sorting for stators. This supersedes earlier documents describing support for stators below rotors. Model exports must adopt the sibling contract before testing this DLL.

## Condenser visuals

GN_CondenserRotor_00 (numeric suffix) rotates on local Y and its child mesh glows; GN_CondenserStator_00 only glows. Both derive intensity from their own Part GNparticle amount/maxAmount, clamped0..1; missing/zero-capacity resources produce0. Default CondenserRotorSpeed=60deg/s at full capacity, exposed by visual tuning PAW. Half-full means half speed/emissive multiplier. Smooth response uses VisualResponse. Propulsion OFF, Repose, SafeGuard and TD idle do not override this charge-based rule. Colour follows the normal drive colour/TA transition but speed/intensity do not follow throttle. These are visual effects and consume no extra resources. Lights and particle emission keep their prior rules.

Use sibling condenser rotor/stator pivots beneath a non-spinning core or mount. Their meshes must not include unrelated normal-drive meshes in their subtree. Condenser renderers take precedence over broad EmissiveTransform selection to avoid double writes. Normal rotor selection excludes both condenser names. No model or game installation files modified.
