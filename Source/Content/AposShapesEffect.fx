#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 view_projection;
float aa;

struct VertexInput {
    float4 Position : POSITION0;
    float4 TexCoord : TEXCOORD0;
    float4 Color1 : COLOR0;
    float4 Color2 : COLOR1;
    float4 Thickness : TEXCOORD1;
};
struct PixelInput {
    float4 Position : SV_Position0;
    float4 TexCoord : TEXCOORD0;
    float4 Color1 : COLOR0;
    float4 Color2 : COLOR1;
    float4 Thickness : TEXCOORD1;
};

float CircleSDF(float2 p, float r) {
    return length(p) - r;
}

float Antialias(float dist, float gradientLength, float value) {
    float thresholdWidth = value * gradientLength;
    float antialiasedCircle = saturate((dist / thresholdWidth) + 0.5);
    return antialiasedCircle;
}

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    output.Color1 = v.Color1;
    output.Color2 = v.Color2;
    output.Thickness = v.Thickness;
    return output;
}
float4 SpritePixelShader(PixelInput p) : COLOR0 {
    float radius = 0.5;
    float dist = CircleSDF(p.TexCoord.xy, radius);

    float2 ddist = float2(ddx(dist), ddy(dist));
    float gradientLength = length(ddist);
    float size = p.Thickness.x * gradientLength;

    float4 border = lerp(p.Color1, p.Color2, Antialias(dist + size, gradientLength, aa));
    return lerp(border, float4(0, 0, 0, 0), Antialias(dist, gradientLength, aa));
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
