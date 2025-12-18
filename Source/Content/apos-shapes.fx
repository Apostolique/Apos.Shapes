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

struct VertexInput {
    float4 Position : POSITION0;
    float4 TexCoord : TEXCOORD0;
    float4 Fill : TEXCOORD1;
    float4 Border : TEXCOORD2;
    float4 FillCoord : TEXCOORD3;
    float4 BorderCoord : TEXCOORD4;
    float4 Meta1 : TEXCOORD5;
    float4 Meta2 : TEXCOORD6;
    float4 Meta3 : TEXCOORD7;
    float4 Meta4 : TEXCOORD8;
};
struct PixelInput {
    float4 Position : SV_Position0;
    float4 TexCoord : TEXCOORD0;
    float4 Fill : TEXCOORD1;
    float4 Border : TEXCOORD2;
    float4 FillCoord : TEXCOORD3;
    float4 BorderCoord : TEXCOORD4;
    float4 Meta1 : TEXCOORD5;
    float4 Meta2 : TEXCOORD6;
    float4 Meta3 : TEXCOORD7;
    float4 Meta4 : TEXCOORD8;
    float4 Pos : TEXCOORD9;
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

float GammaToLinear(float c) {
    return c >= 0.04045 ? pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
}
float LinearToGamma(float c) {
    return c >= 0.0031308 ? pow(c, 1.0 / 2.4) * 1.055 - 0.055 : 12.92 * c;
}

float4 RgbToOklab(float4 c) {
    c.r = GammaToLinear(c.r);
    c.g = GammaToLinear(c.g);
    c.b = GammaToLinear(c.b);

    float l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
    float m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
    float s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;

    float l_ = pow(l, 1.0 / 3.0);
    float m_ = pow(m, 1.0 / 3.0);
    float s_ = pow(s, 1.0 / 3.0);

    return float4(
        0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
        1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
        0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_,
        c.a
    );
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

float SmoothDiscontinuity(float x, float size) {
    float v = frac(x);
    float a = 1.0 / size;
    return saturate(v * a - (a - 1.0));
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
            grad *= 1.0 - SmoothDiscontinuity(grad, aaSize / (6.283185307179586 * length(posAB.xy - c.xy)));
        } else if (type.x < 6.5) {
            grad = SquareGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 7.5) {
            grad = CrossGradient(posAB.xy, posAB.zw, c);
        } else if (type.x < 8.5) {
            grad = SpiralCWGradient(posAB.xy, posAB.zw, c);
            // TODO: Fix this, the discontinuity is in the wrong coordinate system.
            grad *= 1.0 - SmoothDiscontinuity(grad, aaSize / length(posAB.xy - posAB.zw));
        } else if (type.x < 9.5) {
            grad = SpiralCCWGradient(posAB.xy, posAB.zw, c);
            // TODO: Fix this, the discontinuity is in the wrong coordinate system.
            grad *= 1.0 - SmoothDiscontinuity(grad, aaSize / length(posAB.xy - posAB.zw));
        } else if (type.x < 10.5) {
            grad = ShapeGradient(posAB.x, posAB.y, d);
        }

        if (type.y < 0.5) {
        } else if (type.y < 1.5) {
            grad = SawtoothWave(grad);
            grad *= 1.0 - SmoothDiscontinuity(grad, aaSize / length(posAB.xy - posAB.zw));
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

float2 Unpair(float n) {
    float2 result;

    float f1 = floor(sqrt(n));
    float f2 = n - f1 * f1;
    if (f2 < f1) {
        result.x = f2;
        result.y = f1;
    } else {
        result.x = f1;
        result.y = (f2 - f1);
    }
    return result;
}

float Antialias(float d, float size) {
    return lerp(1.0, 0.0, smoothstep(0.0, size, d));
}

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    output.Fill = v.Fill;
    output.Border = v.Border;
    output.FillCoord = v.FillCoord;
    output.BorderCoord = v.BorderCoord;
    output.Meta1 = v.Meta1;
    output.Meta2 = v.Meta2;
    output.Meta3 = v.Meta3;
    output.Meta4 = v.Meta4;
    output.Pos = v.Position;
    return output;
}
float4 SpritePixelShader(PixelInput p) : SV_TARGET {
    float ps = p.Meta2.x;
    float aaSize = ps * p.Meta2.y;
    float sdfSize = p.Meta1.z;
    float lineSize = p.Meta1.x;

    float2 fillR = Unpair(p.Fill.r) / 255.0;
    float2 fillG = Unpair(p.Fill.g) / 255.0;
    float2 fillB = Unpair(p.Fill.b) / 255.0;
    float2 fillA = Unpair(p.Fill.a) / 255.0;
    float4 fill1 = float4(fillR.x, fillG.x, fillB.x, fillA.x);
    float4 fill2 = float4(fillR.y, fillG.y, fillB.y, fillA.y);

    float2 borderR = Unpair(p.Border.r) / 255.0;
    float2 borderG = Unpair(p.Border.g) / 255.0;
    float2 borderB = Unpair(p.Border.b) / 255.0;
    float2 borderA = Unpair(p.Border.a) / 255.0;
    float4 border1 = float4(borderR.x, borderG.x, borderB.x, borderA.x);
    float4 border2 = float4(borderR.y, borderG.y, borderB.y, borderA.y);

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
        d = TriangleSDF(p.TexCoord.xy, p.Meta1.zw, p.Meta3.xy, p.Meta3.zw);
    } else if (p.Meta1.y < 6.5) {
        d = EllipseSDF(p.TexCoord.xy, float2(sdfSize, p.Meta1.w));
    } else if (p.Meta1.y < 7.5) {
        d = ArcSDF(p.TexCoord.xy, p.Meta3.xy, sdfSize, p.Meta3.z);
    } else if (p.Meta1.y < 8.5) {
        d = RingSDF(p.TexCoord.xy, p.Meta3.xy, sdfSize, p.Meta3.z);
    }

    d -= p.Meta2.z;

    float2 gradientStyles = Unpair(p.Meta2.w);
    float2 fillStyles = Unpair(gradientStyles.x);
    float2 borderStyles = Unpair(gradientStyles.y);

    float4 fc = lerp(RgbToOklab(fill1), RgbToOklab(fill2), Gradient(fillStyles, p.FillCoord, p.Pos.xy, d, aaSize, p.Meta4.xy));
    float4 bc = lerp(RgbToOklab(border1), RgbToOklab(border2), Gradient(borderStyles, p.BorderCoord, p.Pos.xy, d, aaSize, p.Meta4.zw));
    bc = lerp(bc, float4(bc.rgb, 0.0), smoothstep(0.0, 1.0, Gradient(10.0, float4(-aaSize, 0.0, 0.0, 0.0), p.Pos.xy, d - aaSize, aaSize, float2(0.0, 0.0))));

    float4 result = OkLabToRgb(lerp(fc, bc, smoothstep(0.0, 1.0, Gradient(10.0, float4(-aaSize, 0.0, 0.0, 0.0), p.Pos.xy, d + lineSize, aaSize, float2(0.0, 0.0)))));
    result.rgb *= result.a;

    return result;

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
