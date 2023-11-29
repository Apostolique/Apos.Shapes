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
// https://www.shadertoy.com/view/slS3Rw
// Gives better results than other ones.
float EllipseSDF(float2 p, float2 ab) {
    float x = p.x;
    float y = p.y;
    float ax = abs(p.x);
    float ay = abs(p.y);
    float a = ab.x;
    float b = ab.y;
    float aa = ab.x * ab.x;
    float bb = ab.y * ab.y;

    float2 closest = float2(0.0, 0.0);

    // edge special case, handle as AABB
    if (a * b <= 1e-15) {
        closest = clamp(p, -ab, ab);
        return length(closest - p);
    }

    // this epsilon will guarantee float precision result
    // (error<1e-6) for degenerate cases
    float epsilon = 1e-3;
    float diff = bb - aa;
    if (a < b) {
        if (ax <= epsilon * a) {
            if (ay * b < diff) {
                float yc = bb * y / diff;
                float xc = a * sqrt(1.0 - yc * yc / bb);
                closest = float2(xc, yc);
                return -length(closest - p);
            }
            closest = float2(x, b * sign(y));
            return ay - b;
        } else if (ay <= epsilon * b) {
            closest = float2(a * sign(x), y);
            return ax - a;
        }
    } else {
        if (ay <= epsilon * b) {
            if (ax * a < -diff) {
                float xc = aa * x / -diff;
                float yc = b * sqrt(1.0 - xc * xc / aa);
                closest = float2(xc, yc);
                return -length(closest - p);
            }
            closest = float2(a * sign(x), y);
            return ax - a;
        }
        else if (ax <= epsilon * a) {
            closest = float2(x, b * sign(y));
            return ay - b;
        }
    }

    float rx = x / a;
    float ry = y / b;
    float inside = rx * rx + ry * ry - 1.0;

    // get lower/upper bound for parameter t
    float s2 = sqrt(2.0);
    float tmin = max(a * ax - aa, b * ay - bb);
    float tmax = max(s2 * a * ax - aa, s2 * b * ay - bb);

    float xx = x * x * aa;
    float yy = y * y * bb;
    float rxx = rx * rx;
    float ryy = ry * ry;
    float t;
    if (inside < 0.0) {
        tmax = min(tmax, 0.0);
        if (ryy < 1.0)
            tmin = max(tmin, sqrt(xx / (1.0 - ryy)) - aa);
        if (rxx < 1.0)
            tmin = max(tmin, sqrt(yy / (1.0 - rxx)) - bb);
        t = tmin * 0.95;
    } else {
        tmin = max(tmin, 0.0);
        if (ryy < 1.0)
            tmax = min(tmax, sqrt(xx / (1.0 - ryy)) - aa);
        if (rxx < 1.0)
            tmax = min(tmax, sqrt(yy / (1.0 - rxx)) - bb);
        t = tmin;//2.0 * tmin * tmax / (tmin + tmax);
    }
    t = clamp(t, tmin, tmax);

    int newton_steps = 12;
    if (tmin >= tmax) {
        t = tmin;
        newton_steps = 0;
    }

    // iterate, most of the time 3 iterations are sufficient.
    // bisect/newton hybrid
    int i;
    for (i = 0; i < newton_steps; i++) {
        float at = aa + t;
        float bt = bb + t;
        float abt = at * bt;
        float xxbt = xx * bt;
        float yyat = yy * at;

        float f0 = xxbt * bt + yyat * at - abt * abt;
        float f1 = 2.0 * (xxbt + yyat - abt * (bt + at));
        // bisect
        if (f0 < 0.0)
            tmax = t;
        else if (f0 > 0.0)
            tmin = t;
        // newton iteration
        float newton = f0 / abs(f1);
        newton = clamp(newton, tmin - t, tmax - t);
        newton = min(newton, a * b * 2.0);
        t += newton;

        float absnewton = abs(newton);
        if (absnewton < 1e-6 * (abs(t) + 0.1) || tmin >= tmax)
            break;
    }

    closest = float2(x * a / (aa + t), y * b / (bb + t));
    // this normalization is a tradeoff in precision types
    closest = normalize(closest);
    closest *= ab;
    return length(closest-p) * sign(inside);
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
    } else if (p.Meta1.y < 5.5) {
        d = EllipseSDF(p.TexCoord.xy, float2(sdfSize, p.Meta1.w));
    }

    d -= p.Meta2.z;

    float4 c1 = p.Color1 * Antialias(d + lineSize * 2.0 + aaSize - ps * 1.0, aaSize);
    d = abs(d + lineSize) - lineSize + ps * 0.5;
    float4 c2 = p.Color2 * Antialias(d, aaSize * 0.75);

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
