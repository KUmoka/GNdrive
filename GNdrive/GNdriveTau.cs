using UnityEngine;

public class Taudrive : BaseGNCore
{
    [KSPField] public float ConvertRatio = 1f;   // EC 1 → GN 1/ConvertRatio
    [KSPField] public float particlegrate = 800f;

    protected override GNCapability Capabilities =>
        GNCapability.Particles | GNCapability.Thrust |
        GNCapability.AntiGravity | GNCapability.Converter;

    protected override void HandleConverter(float t01)
    {
        // 旧Taudriveの趣旨：EC消費→GN補充
        double elcconsume = particlegrate * ConvertRatio * TimeWarp.fixedDeltaTime;
        double elcDrawn   = part.RequestResource("ElectricCharge", elcconsume);
        double ratio      = (elcconsume > 0) ? elcDrawn / elcconsume : 0;
        // GNをチャージ（負で投入）
        double gnIn = part.RequestResource("GNparticle", -(elcconsume / ConvertRatio) * ratio);
        // 収支調整（元コード準拠）
        part.RequestResource("ElectricCharge", -gnIn * ConvertRatio - elcDrawn);

        color = new Vector4(1f, 0f, 36f/255f, 1f);
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        // Taudriveは回転軸が違うならここで個別回転にしてもOK
    }
}
