// 输入：point 待旋转向量，eulerDeg 欧拉角（度），invRot 是否反转旋转顺序（不反转按照Z-X-Y顺序旋转，反转则按照Y-X-Z顺序旋转）
// 输出：旋转后的向量，与 C# 的 Quaternion.Euler(eulerDeg) * point 一致
float3 rotateByEulerAngles(float3 p, float3 eulerDeg, float invRot)
{
    // eulerDeg = lerp(eulerDeg, -eulerDeg, invRot);

    // 角度转弧度
    float x = radians(eulerDeg.x);
    float y = radians(eulerDeg.y);
    float z = radians(eulerDeg.z);

    // 计算 sin/cos
    float sx, cx;
    float sy, cy;
    float sz, cz;
    sincos(x, sx, cx);
    sincos(y, sy, cy);
    sincos(z, sz, cz);

    // 构造旋转矩阵 R = R_y * R_x * R_z （行主序）
    float3x3 R = float3x3(
        cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx,
        cx * sz, cx * cz, -sx,
        -sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx);

    R = lerp(R, transpose(R), invRot);

    // 矩阵乘列向量
    return mul(R, p);
}

float3 calculateCelestialUV(float3 eulerDeg)
{
    float x = radians(eulerDeg.x);
    float y = radians(eulerDeg.y);
    return float3(-cos(x) * sin(y), sin(x), -cos(x) * cos(y));
}

float2 normalizeSafe2(float2 v)
{
    return v / max(length(v), 0.0001);
}
