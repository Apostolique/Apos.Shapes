#if __KNIFX__
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#elif OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 view_projection;
sampler TextureSampler : register(s0);
sampler FontSampler;

struct VertexInput {
    float4 Position : POSITION0;
    float4 TexCoord : TEXCOORD0; // xy: uv or local position, z: rounded, w: packed shape, gradient styles and color space.
    float4 FillA : TEXCOORD1; // Colors arrive as normalized shorts, every channel is in [0, 1].
    float4 FillB : TEXCOORD2;
    float4 BorderA : TEXCOORD3;
    float4 BorderB : TEXCOORD4;
    float4 FillCoord : TEXCOORD5;
    float4 BorderCoord : TEXCOORD6;
    float4 Meta1 : TEXCOORD7;
    float4 Meta2 : TEXCOORD8;
    float4 Meta3 : TEXCOORD9;
    float4 ClipDist : POSITION1;
    float2 ClipRoundAA : NORMAL0;
};
struct PixelInput {
    float4 Position : SV_Position0;
    float4 TexCoord : TEXCOORD0; // xy: uv or local position, z: rounded, w: packed shape, gradient styles and color space.
    float4 Fill : TEXCOORD1; // Two colors, each repacked as two 11 bit channels per float.
    float4 Border : TEXCOORD2;
    float4 FillCoord : TEXCOORD3;
    float4 BorderCoord : TEXCOORD4;
    float4 Meta1 : TEXCOORD5;
    float4 Meta2 : TEXCOORD6;
    float4 Meta3 : TEXCOORD7;
    float4 Pos : TEXCOORD8; // xy: world position, zw: left/top clip distances.
    float4 ClipMeta : TEXCOORD9; // xy: right/bottom clip distances, z: clip rounding, w: clip AA size.
};

// https://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
float CircleSDF(float2 p, float r) {
    return length(p) - r;
}
float RoundBoxSDF(float2 p, float2 b, float4 r) {
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.y  : r.x;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
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
float TriangleSDF(float2 p, float2 p0, float2 p1, float2 p2) {
    float2 e0 = p1 - p0;
    float2 e1 = p2 - p1;
    float2 e2 = p0 - p2;
    float2 v0 = p - p0;
    float2 v1 = p - p1;
    float2 v2 = p - p2;
    float2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    float2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    float2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x * e2.y - e0.y * e2.x);
    float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                       float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                       float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
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
float ArcSDF(float2 p, float2 sc, float ra, float rb) {
    p.x = abs(p.x);
    return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
}
float RingSDF(float2 p, float2 n, float r, float th) {
    p.x = abs(p.x);
    p = mul(p, float2x2(n.x, n.y, -n.y, n.x));
    return max(abs(length(p) - r) - th * 0.5, length(float2(p.x, max(0.0, abs(r - p.y) - th * 0.5))) * sign(p.x));
}

float LinearToGamma(float c) {
    return c >= 0.0031308 ? pow(abs(c), 1.0 / 2.4) * 1.055 - 0.055 : 12.92 * c;
}

float4 OkLabToRgb(float4 c) {
    float l_ = c.x + 0.3963377774f * c.y + 0.2158037573f * c.z;
    float m_ = c.x - 0.1055613458f * c.y - 0.0638541728f * c.z;
    float s_ = c.x - 0.0894841775f * c.y - 1.2914855480f * c.z;

    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;

    float r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
    float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
    float b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

    return float4(
        LinearToGamma(r),
        LinearToGamma(g),
        LinearToGamma(b),
        c.a
    );
}

float HueLerp(float a, float b, float t) {
    // Take the shortest path around the hue wheel.
    float d = frac(b - a + 0.5) - 0.5;
    return frac(a + d * t);
}
float4 LerpColor(float4 a, float4 b, float t, float space) {
    float4 c = lerp(a, b, t);
    if (space < 0.5) {
        c.z = HueLerp(a.z, b.z, t);
    }
    return c;
}
// Weight the color lerp by each side's alpha (premultiplied-style blend) so a
// transparent color's hidden RGB can't tint the blend on the other side.
float4 LerpColorPremul(float4 a, float4 b, float t, float space) {
    float oa = lerp(a.a, b.a, t);
    float4 c = LerpColor(a, b, oa > 0.0 ? t * b.a / oa : t, space);
    c.a = oa;
    return c;
}
float4 ToRgb(float4 c, float space) {
    float4 result = c; // Raw sRGB passes through untouched.
    if (space < 0.5) {
        // Oklch. Chroma is remapped from [0, 0.4], hue from [-pi, pi].
        float ch = c.y * 0.4;
        float h = c.z * 6.283185307179586 - 3.14159265358979;
        result = OkLabToRgb(float4(c.x, ch * cos(h), ch * sin(h), c.w));
    } else if (space < 1.5) {
        // Oklab. a and b are remapped from [-0.4, 0.4].
        result = OkLabToRgb(float4(c.x, c.y * 0.8 - 0.4, c.z * 0.8 - 0.4, c.w));
    }
    return result;
}

float2 Rotate(float2 a, float2 b, float2 c) {
    float ux = b.x - a.x;
    float uy = b.y - a.y;
    float vx = -c.x + a.x;
    float vy = c.y - a.y;

    float mag = sqrt(ux * ux + uy * uy);
    if (mag == 0) return c;

    ux /= mag;
    uy /= mag;

    float rx = ux * vx - uy * vy;
    float ry = uy * vx + ux * vy;

    return float2(rx, ry);
}

float Mod(float x, float m) {
    return (x % m + m) % m;
}

// Smooths the wrap seam of a periodic gradient by pulling values within half a
// band of the seam toward 0.5 — the box-filtered average of both sides, since
// color is linear in the gradient value. When the band covers the whole period
// (near a conical origin) everything collapses to 0.5 instead of biasing to 0.
float SmoothWrapDiscontinuity(float x, float size) {
    float h = max(0.5 * saturate(size), 1e-6);
    float v = frac(x);
    float hi = saturate((v - (1.0 - h)) / h);
    float lo = saturate((h - v) / h);
    return lerp(v, 0.5, max(hi, lo));
}

float RemapOffset(float x, float2 offset) {
    return (x - offset.x) / ((1.0 - offset.y) - offset.x);
}

float SawtoothWave(float x) {
    return frac(x);
}
float TriangularWave(float w) {
    return abs(Mod(w + 1.0, 2.0) - 1.0);
}
float SineWave(float w) {
    return sin(w * 3.14159265 - 1.5) * 0.5 + 0.5;
}

float RadialGradient(float2 a, float2 b, float2 c) {
    return length(c - a) / length(b - a);
}
float LinearGradient(float2 a, float2 b, float2 c) {
    float l = length(b - a);
    float2 d = normalize(b - a);
    b = float2(-d.y, d.x) + a;
    return ((b.x - a.x) * (a.y - c.y) - (a.x - c.x) * (b.y - a.y)) / sqrt(pow(b.x - a.x, 2) + pow(b.y - a.y, 2)) / l;
}
float BilinearGradient(float2 a, float2 b, float2 c) {
    float l = length(b - a);
    float2 d = normalize(b - a);
    b = float2(-d.y, d.x) + a;
    return abs((b.x - a.x) * (a.y - c.y) - (a.x - c.x) * (b.y - a.y)) / sqrt(pow(b.x - a.x, 2) + pow(b.y - a.y, 2)) / l;
}
float ConicalGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return abs(atan2(-c.y, -c.x) / 3.14159265);
}
float ConicalAsymGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return atan2(c.y, c.x) / 6.283185307179586 + 0.5;
}
float SquareGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return max(abs(c.x), abs(c.y)) / length(b - a);
}
float CrossGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return min(abs(c.x), abs(c.y)) / length(b - a);
}
// Magnitude of the spiral gradient per world unit. The radial term winds once
// per gradient length, the angular term once per turn, and they are orthogonal
// so the root sum keeps the smoothed seam aaSize wide at any radius.
float SpiralGradientSize(float4 posAB, float2 c) {
    float l = length(posAB.zw - posAB.xy);
    float r = 6.283185307179586 * length(c - posAB.xy);
    return sqrt(1.0 / (l * l) + 1.0 / max(r * r, 1e-12));
}
float SpiralCWGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return SawtoothWave(1.0 * atan2(-c.y, -c.x) / 6.283185307179586 + length(c) / length(b - a));
}
float SpiralCCWGradient(float2 a, float2 b, float2 c) {
    c = Rotate(a, b, c);
    return SawtoothWave(-1.0 * atan2(-c.y, -c.x) / 6.283185307179586 + length(c) / length(b - a));
}
float ShapeGradient(float a, float b, float c) {
    return (c - a) / (b - a);
}

float Gradient(float2 type, float4 posAB, float2 c, float d, float aaSize, float2 offset) {
    float result;
    if (type.x < 0.5) {
        result = 1.0;
    } else {
        float grad;
        if (type.x < 1.5) {
            grad = RadialGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 2.5) {
            grad = LinearGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 3.5) {
            grad = BilinearGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 4.5) {
            grad = ConicalGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 5.5) {
            grad = ConicalAsymGradient(posAB.xy, posAB.zw, c);
            grad = SmoothWrapDiscontinuity(grad, aaSize / (6.283185307179586 * length(posAB.xy - c.xy)));
        } else if (type.x < 6.5) {
            grad = SquareGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 7.5) {
            grad = CrossGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 8.5) {
            grad = SpiralCWGradient(posAB.xy, posAB.zw, c);
            grad = SmoothWrapDiscontinuity(grad, aaSize * SpiralGradientSize(posAB, c));
        } else if (type.x < 9.5) {
            grad = SpiralCCWGradient(posAB.xy, posAB.zw, c);
            grad = SmoothWrapDiscontinuity(grad, aaSize * SpiralGradientSize(posAB, c));
        } else if (type.x < 10.5) {
            grad = ShapeGradient(posAB.x, posAB.y, d);
        }

        if (type.y < 0.5) {
        } else if (type.y < 1.5) {
            grad = SawtoothWave(grad);
            grad = SmoothWrapDiscontinuity(grad, aaSize / length(posAB.xy - posAB.zw));
        } else if (type.y < 2.5) {
            grad = TriangularWave(grad);
        } else if (type.y < 3.5) {
            grad = SineWave(grad);
        }
        grad = RemapOffset(grad, offset);

        result = saturate(grad);
    }
    return result;
}

// ps_3_0 only has 10 interpolators so the vertex shader repacks each color into
// two floats with two 11 bit channels each. The packed value stays under 2^22 so
// every intermediate is an exact integer in a float.
float Pack11(float a, float b) {
    return floor(a * 2047.0 + 0.5) * 2048.0 + floor(b * 2047.0 + 0.5);
}
float4 PackColors(float4 a, float4 b) {
    return float4(Pack11(a.x, a.y), Pack11(a.z, a.w), Pack11(b.x, b.y), Pack11(b.z, b.w));
}

// Pops the lowest base-radix digit off m. floor() can be off by one on some
// driver and translator combos, the remainder check corrects for it.
float DecodeDigit(inout float m, float radix) {
    float q = floor(m / radix);
    float r = m - q * radix;
    if (r >= radix) {
        q += 1.0;
        r -= radix;
    } else if (r < 0.0) {
        q -= 1.0;
        r += radix;
    }
    m = q;
    return r;
}
float2 Unpack11(float v) {
    float lo = DecodeDigit(v, 2048.0);
    return float2(v, lo) / 2047.0;
}
float4 UnpackColor(float2 c) {
    return float4(Unpack11(c.x), Unpack11(c.y));
}

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    output.Fill = PackColors(v.FillA, v.FillB);
    output.Border = PackColors(v.BorderA, v.BorderB);
    output.FillCoord = v.FillCoord;
    output.BorderCoord = v.BorderCoord;
    output.Meta1 = v.Meta1;
    output.Meta2 = v.Meta2;
    output.Meta3 = v.Meta3;
    output.Pos = float4(v.Position.xy, v.ClipDist.xy);
    output.ClipMeta = float4(v.ClipDist.zw, v.ClipRoundAA);
    return output;
}

// SDF values are true distances in world units, so the AA band only needs the
// world-space footprint of one pixel. The footprint comes from derivatives of the
// interpolated world position — exact per-triangle constants under affine views,
// smooth and perspective-correct otherwise — never from derivatives of the SDF
// alone, whose finite differences misfire in quads that straddle corner creases.
// The screen-space SDF gradient picks the width within the footprint's singular
// value range: direction-correct under anisotropy, and under uniform scale the
// range collapses so corners stay as clean as a hardcoded pixel size.
float2 PixelFootprint(float2 pos) {
    float2 jx = ddx(pos);
    float2 jy = ddy(pos);
    float a = dot(jx, jx) + dot(jy, jy);
    float det = abs(jx.x * jy.y - jx.y * jy.x);
    float s = sqrt(max(a * a - 4.0 * det * det, 0.0));
    float pixMax = sqrt(0.5 * (a + s));
    return float2(det / max(pixMax, 1e-12), pixMax);
}
float PixelWidth(float d, float2 footprint) {
    return clamp(length(float2(ddx(d), ddy(d))), footprint.x, footprint.y);
}

float4 SpritePixelShader(PixelInput p) : SV_TARGET {
    float lineSize = p.Meta1.x;
    float aaPixels = p.Meta1.y;
    float sdfSize = p.Meta1.z;

    // Peel the packed meta apart field by field. Every intermediate stays an exact integer.
    float meta = p.TexCoord.w;
    float shape = DecodeDigit(meta, 16.0);

    float2 footprint = PixelFootprint(p.Pos.xy);

    // Rounded box SDF from the interpolated edge distances.
    float2 clipQ = p.ClipMeta.z - min(p.Pos.zw, p.ClipMeta.xy);
    float clipD = length(max(clipQ, 0.0)) + min(max(clipQ.x, clipQ.y), 0.0) - p.ClipMeta.z;
    float clipAa = PixelWidth(clipD, footprint) * p.ClipMeta.w;
    if (clipD >= clipAa) {
        discard;
    }
    float clipAlpha = 1.0 - smoothstep(0.0, 1.0, saturate(clipD / clipAa));

    if (shape >= 8.5) {
        if (shape < 9.5) {
            return tex2D(TextureSampler, p.TexCoord.xy) * UnpackColor(p.Fill.xy) * clipAlpha;
        }
        return tex2D(FontSampler, p.TexCoord.xy) * UnpackColor(p.Fill.xy) * clipAlpha;
    }

    float d;
    if (shape < 0.5) {
        d = CircleSDF(p.TexCoord.xy, sdfSize);
    } else if (shape < 1.5) {
        d = RoundBoxSDF(p.TexCoord.xy, float2(sdfSize, p.Meta1.w), p.Meta2);
    } else if (shape < 2.5) {
        d = SegmentSDF(p.TexCoord.xy, float2(0.0, 0.0), float2(p.Meta1.w, 0.0));
    } else if (shape < 3.5) {
        d = HexagonSDF(p.TexCoord.xy, sdfSize);
    } else if (shape < 4.5) {
        d = EquilateralTriangleSDF(p.TexCoord.xy, sdfSize);
    } else if (shape < 5.5) {
        d = TriangleSDF(p.TexCoord.xy, p.Meta1.zw, p.Meta2.xy, p.Meta2.zw);
    } else if (shape < 6.5) {
        d = EllipseSDF(p.TexCoord.xy, float2(sdfSize, p.Meta1.w));
    } else if (shape < 7.5) {
        d = ArcSDF(p.TexCoord.xy, p.Meta2.xy, sdfSize, p.Meta2.z);
    } else {
        d = RingSDF(p.TexCoord.xy, p.Meta2.xy, sdfSize, p.Meta2.z);
    }

    d -= p.TexCoord.z;

    float aaSize = PixelWidth(d, footprint) * aaPixels;

    // Beyond the outer AA edge every branch below resolves to premultiplied zero.
    if (d >= aaSize) {
        discard;
    }

    float2 fillStyles;
    float2 borderStyles;
    fillStyles.x = DecodeDigit(meta, 16.0);
    fillStyles.y = DecodeDigit(meta, 4.0);
    borderStyles.x = DecodeDigit(meta, 16.0);
    borderStyles.y = DecodeDigit(meta, 4.0);
    float space = meta;

    float4 fillA = UnpackColor(p.Fill.xy);
    float4 fillB = UnpackColor(p.Fill.zw);

    float edgeFade = 1.0 - smoothstep(0.0, 1.0, saturate(d / aaSize));
    float borderMix = smoothstep(0.0, 1.0, saturate((d + lineSize + aaSize) / aaSize));

    // The fill/border crossfade is coverage, not a gradient: blend premultiplied in
    // sRGB so the inner AA fringe matches the framebuffer blend outside the edge.
    // The vertex data is uniform per quad, so these branches are coherent.
    float4 result;
    if (all(p.Fill == p.Border) && all(p.FillCoord == p.BorderCoord) && all(fillStyles == borderStyles) && all(p.Meta3.xy == p.Meta3.zw)) {
        // Fill and border are the same gradient, so the crossfade collapses to edge coverage.
        float4 fc = LerpColorPremul(fillA, fillB, Gradient(fillStyles, p.FillCoord, p.Pos.xy, d, aaSize, p.Meta3.xy), space);
        fc.a *= edgeFade;
        result = ToRgb(fc, space);
        result.rgb *= result.a;
    } else if (fillA.a == 0.0 && fillB.a == 0.0) {
        // Transparent fill: everything inside the border band contributes nothing.
        if (borderMix <= 0.0) {
            discard;
        }
        float4 bc = LerpColorPremul(UnpackColor(p.Border.xy), UnpackColor(p.Border.zw), Gradient(borderStyles, p.BorderCoord, p.Pos.xy, d, aaSize, p.Meta3.zw), space);
        bc.a *= edgeFade;
        result = ToRgb(bc, space);
        result.rgb *= result.a;
        result *= borderMix;
    } else {
        float4 fc = LerpColorPremul(fillA, fillB, Gradient(fillStyles, p.FillCoord, p.Pos.xy, d, aaSize, p.Meta3.xy), space);
        float4 bc = LerpColorPremul(UnpackColor(p.Border.xy), UnpackColor(p.Border.zw), Gradient(borderStyles, p.BorderCoord, p.Pos.xy, d, aaSize, p.Meta3.zw), space);
        bc.a *= edgeFade;

        float4 fr = ToRgb(fc, space);
        float4 br = ToRgb(bc, space);
        fr.rgb *= fr.a;
        br.rgb *= br.a;
        result = lerp(fr, br, borderMix);
    }

    return result * clipAlpha;

    // float4 c1 = p.Color1 * step(d + lineSize * 2.0, 0.0);
    // d = abs(d + lineSize) - lineSize;
    // float4 c2 = p.Color2 * step(d, 0.0);

    // float4 c3 = c2 + c1 * (1.0 - c2.a);
    // // return c3;

    // float4 c4 = float4(0.3, 0.0, 0.0, 0.3);
    // return result + c4 * (1.0 - result.a);
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
