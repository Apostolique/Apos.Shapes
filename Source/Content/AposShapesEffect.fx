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
float4 SpritePixelShader(PixelInput p) : COLOR0 {
    float ps = p.Meta1.z;
    float aaSize = 4.0;
    float aa = p.Meta1.z * aaSize;
    float sdfSize = 1.0 - aa;

    float d;
    if (p.Meta1.y == 0) {
        d = CircleSDF(p.TexCoord.xy, sdfSize);
    } else if (p.Meta1.y == 1) {
        d = BoxSDF(p.TexCoord.xy, float2(p.Meta1.w - aa, sdfSize));
    } else if (p.Meta1.y == 2) {
        d = SegmentSDF(p.TexCoord.xy, float2(-p.Meta1.w + aa, 0.0), float2(p.Meta1.w - aa, 0.0)) - p.Meta2.x + aa / 2.0;
    }

    float lineSize = p.Meta1.x * ps - ps * 2.0;

    float4 c1 = p.Color1 * Antialias(d + lineSize * 2.0 + ps * 3.0, aa);

    d = abs(d + lineSize) - lineSize;
    float4 c2 = p.Color2 * Antialias(d, aa);

    return c2 + c1 * (1.0 - c2.a);
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
