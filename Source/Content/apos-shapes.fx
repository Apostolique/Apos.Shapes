#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 view_projection;

struct VertexInput {
    float4 Position : POSITION0;
    float4 TexCoord : TEXCOORD0;
    float4 Color1 : COLOR0;
    float4 Color2 : COLOR1;
    float4 Meta1 : TEXCOORD1;
    float4 Meta2 : TEXCOORD2;
};
struct PixelInput {
    float4 Position : SV_Position0;
    float4 TexCoord : TEXCOORD0;
    float4 Color1 : COLOR0;
    float4 Color2 : COLOR1;
    float4 Meta1 : TEXCOORD1;
    float4 Meta2 : TEXCOORD2;
};

// https://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
float CircleSDF(float2 p, float r) {
    return length(p) - r;
}
float BoxSDF(float2 p, float2 b) {
    float2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}
float SegmentSDF(float2 p, float2 a, float2 b) {
    float2 ba = b - a;
    float2 pa = p - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - h * ba);
}
float HexagonSDF(float2 p, float r) {
    const float3 k = float3(-0.866025404, 0.5, 0.577350269);
    p = abs(p);
    p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
    p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
    return length(p) * sign(p.y);
}
float EquilateralTriangleSDF(float2 p, float ha) {
    const float k = sqrt(3.0);
    p.x = abs(p.x) - ha;
    p.y = p.y + ha / k;
    if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
    p.x -= clamp(p.x, -2.0 * ha, 0.0);
    return -length(p) * sign(p.y);
}

float Antialias(float d, float size) {
    return lerp(1.0, 0.0, smoothstep(0.0, size, d));
}

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    output.Color1 = v.Color1;
    output.Color2 = v.Color2;
    output.Meta1 = v.Meta1;
    output.Meta2 = v.Meta2;
    return output;
}
float4 SpritePixelShader(PixelInput p) : SV_TARGET {
    float ps = p.Meta2.x;
    float aaSize = ps * p.Meta2.y;
    float sdfSize = p.Meta1.z;
    float lineSize = p.Meta1.x * 0.5;

    float d;
    if (p.Meta1.y < 0.5) {
        d = CircleSDF(p.TexCoord.xy, sdfSize);
    } else if (p.Meta1.y < 1.5) {
        d = BoxSDF(p.TexCoord.xy, float2(sdfSize, p.Meta1.w));
    } else if (p.Meta1.y < 2.5) {
        d = SegmentSDF(p.TexCoord.xy, float2(sdfSize, sdfSize), float2(sdfSize, p.Meta1.w)) - sdfSize;
    } else if (p.Meta1.y < 3.5) {
        d = HexagonSDF(p.TexCoord.xy, sdfSize);
    } else if (p.Meta1.y < 4.5) {
        d = EquilateralTriangleSDF(p.TexCoord.xy, sdfSize);
    }

    d -= p.Meta2.z;

    float4 c1 = p.Color1 * Antialias(d + lineSize * 2.0 + aaSize - ps * 0.5, aaSize);
    d = abs(d + lineSize) - lineSize;
    float4 c2 = p.Color2 * Antialias(d, aaSize);

    return c2 + c1 * (1.0 - c2.a);

    // float4 c1 = p.Color1 * step(d + lineSize * 2.0, 0.0);
    // d = abs(d + lineSize) - lineSize;
    // float4 c2 = p.Color2 * step(d, 0.0);

    // float4 c3 = c2 + c1 * (1.0 - c2.a);
    // // return c3;

    // float4 c4 = float4(1.0, 0.0, 0.0, 1.0);
    // return c3 + c4 * (1.0 - c3.a);
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
