shader_type canvas_item;

// 银龙奥卡——战斗能量球「外框扫光」（我们自己的 shader）。
//
// 做两件事：
//   ① 和 orca_orb_tint.gdshader 一样，按亮度在「暗色 → 亮色」之间重映射（板甲＝黑红 / 婚纱＝红白）；
//   ② **扫光**：以圆环中心为极点算角度，在 sweep_angle 附近一个扇形带里
//      把颜色混向 sweep_color（白）并把 alpha 压到 sweep_alpha（半透明）——
//      角度从 0 扫到 360，看起来就是"外框上每一段依次变白、变淡、再恢复原色，绕一圈"。
//      sweep_strength = 0 时完全不生效（不扫的时候跟普通换色材质一模一样）。
uniform vec4 color_dark : source_color = vec4(0.03, 0.03, 0.04, 1.0);
uniform vec4 color_bright : source_color = vec4(0.72, 0.09, 0.10, 1.0);
uniform float mid_gamma : hint_range(0.2, 3.0) = 1.0;
uniform float lum_boost : hint_range(0.1, 4.0) = 1.0;

uniform vec4 sweep_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float sweep_angle : hint_range(-180.0, 720.0) = 0.0;   // 扫到哪儿（度）
uniform float sweep_width : hint_range(1.0, 180.0) = 42.0;     // 光带宽度（度）
uniform float sweep_strength : hint_range(0.0, 1.0) = 0.0;     // 0 = 不扫
uniform float sweep_alpha : hint_range(0.0, 1.0) = 0.42;       // 变白的同时淡到多少
uniform float sweep_whiteout : hint_range(0.0, 1.0) = 0.0;     // 1 = 整圈一起变白半透明
// ★ 累积模式（用户口径："变白特效旋转把原本颜色变为白色，然后全部变白的瞬间重新变回原来的颜色"）：
//   1 = 已扫过的角度**保持白色**（白色像进度一样绕一圈刷过），0 = 只有一条光带在走。
uniform float sweep_accumulate : hint_range(0.0, 1.0) = 1.0;
uniform float sweep_soft : hint_range(1.0, 90.0) = 14.0;       // 扫白前缘的柔化宽度（度）
// ★ 前缘的**起点角度**（每次随机）：累积量要以它为基准算"被扫过多少"，
//   否则起点之后的半圈会在开局就被判成"已扫过"⇒ 一片白（踩过一次的坑）。
uniform float sweep_start : hint_range(0.0, 360.0) = 0.0;
// ★ 灰化：用户口径"婚纱下是**白色变成灰色再变成白色**" ⇒ 回退阶段先整体压成灰、再回到原色。
uniform float sweep_gray : hint_range(0.0, 1.0) = 0.0;
uniform vec4 gray_color : source_color = vec4(0.45, 0.45, 0.47, 1.0);

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    // ① 亮度重映射（与能量球本体同一套配色）
    float lum = clamp(dot(tex.rgb, vec3(0.299, 0.587, 0.114)) * lum_boost, 0.0, 1.0);
    lum = pow(lum, mid_gamma);
    vec3 col = mix(color_dark.rgb, color_bright.rgb, lum);

    // ② 极角（0..360）
    vec2 d = UV - vec2(0.5);
    float ang = degrees(atan(d.y, d.x)) + 180.0;

    // ③a 光带模式：与光带的角距离 → 带内混白
    float diff = abs(mod(ang - sweep_angle + 540.0, 360.0) - 180.0);
    float band = 1.0 - smoothstep(0.0, sweep_width, diff);

    // ③b 累积模式：这个角度"被前缘扫过多少度" → 越靠后越白（前缘处柔和过渡）
    //    ① 先把角度折算成"距起点的弧长" rel（0..360，这里用 mod 是正确的：它做的是角度归一化）；
    //    ② 再用 sweep_angle（本次从 0 走到 360+柔化宽度）相减 —— 这一步**不能**回绕，
    //       否则前缘还没到，后面的角度就会被算成"已扫过"⇒ 直接全白（实测踩过）。
    float rel = mod(ang - sweep_start + 720.0, 360.0);
    float swept = sweep_angle - rel;                 // >0 = 已被前缘扫过
    float acc = clamp(swept / sweep_soft, 0.0, 1.0);

    float sweep_k = mix(band, acc, sweep_accumulate);
    float k = clamp(sweep_k * sweep_strength + sweep_whiteout, 0.0, 1.0);
    col = mix(col, sweep_color.rgb, k);
    col = mix(col, gray_color.rgb, sweep_gray);          // 回退时整体压灰
    float a = tex.a * mix(1.0, sweep_alpha, k);

    COLOR = vec4(col, a) * COLOR;
}
