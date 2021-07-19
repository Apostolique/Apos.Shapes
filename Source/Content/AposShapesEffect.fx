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
    float4 Meta : TEXCOORD1;
};
struct PixelInput {
    float4 Position : SV_Position0;
    float4 TexCoord : TEXCOORD0;
    float4 Color1 : COLOR0;
    float4 Color2 : COLOR1;
    float4 Meta : TEXCOORD1;
};

float CircleSDF(float2 p, float r) {
    return length(p) - r;
}
float BoxSDF(float2 p, float2 b) {
    float2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
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
    output.Meta = v.Meta;
    return output;
}
float4 SpritePixelShader(PixelInput p) : COLOR0 {
    float ps = p.Meta.z;
    float aaSize = 4.0;
    float aa = p.Meta.z * aaSize;
    float sdfSize = 1.0 - aa;

    float d;
    if (p.Meta.y == 0) {
        d = CircleSDF(p.TexCoord.xy, sdfSize);
    } else if (p.Meta.y == 1) {
        d = BoxSDF(p.TexCoord.xy, float2(p.Meta.w - aa, sdfSize));
    }

    float lineSize = p.Meta.x * ps - ps;

    float4 c1 = p.Color1 * Antialias(d + lineSize, aa);

    d = abs(d + lineSize) - lineSize;
    float4 c2 = p.Color2 * Antialias(d, aa);

    return float4(c2.rgb + (c1.rgb * (1.0 - c2.a)), c1.a);
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
