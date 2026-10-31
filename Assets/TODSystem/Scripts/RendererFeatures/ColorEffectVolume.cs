using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// ColorEffect 的 Volume 覆盖组件。
/// 在 Global Volume（或任意 Volume）里 Add Override → Color Effect Volume，
/// 即可在运行时动态控制 ColorEffect 材质的 _Red。
/// 未添加或未勾选 Red 的 Override 时，ColorEffectFeature 不做处理。
/// </summary>
public class ColorEffectVolume : VolumeComponent, IPostProcessComponent
{
    /// <summary>对应 ColorEffect 材质的 _Red</summary>
    public ClampedFloatParameter red = new ClampedFloatParameter(0.5f, 0f, 1f);

    public bool IsActive() => active && red.overrideState;

    public bool IsTileCompatible() => false;
}
