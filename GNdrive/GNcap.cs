using UnityEngine;

public class GNcap : BaseGNCore
{
    [KSPField] public float ConvertRatio = 1.25f; // GN→EC 変換効率（お好みで使用）

    protected override GNCapability Capabilities =>
        GNCapability.Particles | GNCapability.Thrust | GNCapability.Converter;

    protected override void HandleConverter(float t01)
    {
        // 旧GNcapの趣旨：消費したGNに応じてECを発生させる
        // lastGNConsumed は Base が直近フレームの GN 消費量を格納
        if (lastGNConsumed > 0)
        {
            // 例：適当に 1/10 を EC として返す（元コードの雰囲気に合わせるならここを調整）
            double ecBack = -(lastGNConsumed / 10.0); // RequestResource は負で追加
            part.RequestResource("ElectricCharge", ecBack);
        }
        color = new Vector4(0f, 1f, 170f/255f, 1f);
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        // GNcap固有の回転軸(90度ズラす等)が必要ならここで調整
    }
}
