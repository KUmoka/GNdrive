using UnityEngine;

public class GNdrive : BaseGNCore
{
    // 旧GNdriveの「全部入り」に近い
    protected override GNCapability Capabilities =>
        GNCapability.Particles | GNCapability.Audio |
        GNCapability.Thrust | GNCapability.AntiGravity |
        GNCapability.HoverAssist | GNCapability.InertiaCtrl |
        GNCapability.TransAM;

    // 必要なら InertiaControl の詳細をここに移植
    protected override Vector3 ComputeInertiaControl()
    {
        // まずは無効でもOK。挙動を詰めたい時に移植しましょう
        return Vector3.zero;
    }

    protected override void UpdateVisuals()
    {
        // 通常色
        if (!taActivated) color = new Vector4(0f, 1f, 170f/255f, 1f);
        base.UpdateVisuals();
    }
}
