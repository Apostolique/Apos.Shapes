#if __KNIFX__
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#elif OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#elif SM6
// Vulkan compiles through DXC, which requires shader model 6.
#define VS_SHADERMODEL vs_6_0
#define PS_SHADERMODEL ps_6_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 view_projection;
float2 half_viewport;
float dither_scale; // DitherStrength / 255, folded on the CPU so the shader adds ±half an 8-bit LSB directly.
float dither_mode; // 0: interleaved gradient noise, 1: the blue noise tile.
#if SM6
// DXC drops the legacy sampler syntax: declare texture/sampler pairs on matching
// registers so the Vulkan reflection treats them as combined image-samplers.
Texture2D TextureTex : register(t0); SamplerState TextureSampler : register(s0);
Texture2D FontTex : register(t1); SamplerState FontSampler : register(s1);
Texture2D BlueNoiseTex : register(t2); SamplerState BlueNoiseSampler : register(s2); // 64x64 tile, bound with wrapped point sampling.
float4 SampleTexture(float2 uv) { return TextureTex.Sample(TextureSampler, uv); }
float4 SampleFont(float2 uv) { return FontTex.Sample(FontSampler, uv); }
float4 SampleBlueNoise(float2 uv) { return BlueNoiseTex.Sample(BlueNoiseSampler, uv); }
#else
sampler TextureSampler : register(s0);
sampler FontSampler;
sampler BlueNoiseSampler : register(s2); // 64x64 tile, bound with wrapped point sampling.
float4 SampleTexture(float2 uv) { return tex2D(TextureSampler, uv); }
float4 SampleFont(float2 uv) { return tex2D(FontSampler, uv); }
float4 SampleBlueNoise(float2 uv) { return tex2D(BlueNoiseSampler, uv); }
#endif

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
// Pops the lowest base-radix digit off m, defined early for StrokeSDF. floor() can be
// off by one on some driver and translator combos, the remainder check corrects for it.
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
// Stroked path segment from (0, 0) to (len, 0) with half thickness r. Each end is round
// (arc, also used for round joins), butt (sharp face at the endpoint), square (sharp face
// pushed out by r), open (the slab runs on so quad geometry can shape a miter tip), or cut
// by a bevel plane through the joint. data.x packs the two end modes in base 8, data.y and
// data.z are the bevel plane directions as angles in the local frame.
float StrokeSDF(float2 p, float len, float r, float4 data) {
    float m = data.x;
    float modeA = DecodeDigit(m, 8.0);
    float modeB = m;
    float ay = abs(p.y);
    float xa = -p.x;
    float xb = p.x - len;
    // Excess past each end's face. Open and bevel ends never activate.
    float ea = modeA >= 2.5 ? -1e6 : (modeA >= 1.5 ? xa - r : xa);
    float eb = modeB >= 2.5 ? -1e6 : (modeB >= 1.5 ? xb - r : xb);
    float e = max(ea, eb);
    float mode = ea > eb ? modeA : modeB;
    float d;
    if (mode < 0.5) {
        // Round: distance to the spine clamped at the end.
        d = length(float2(max(e, 0.0), ay)) - r;
    } else {
        // Sharp: exact box corner. Interior still resolves to the plain slab.
        float2 q = float2(e, ay - r);
        d = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
    }
    if (modeA >= 3.5) {
        float2 dir;
        sincos(data.y, dir.y, dir.x);
        d = max(d, dot(p, dir) - r * abs(dir.y));
    }
    if (modeB >= 3.5) {
        float2 dir;
        sincos(data.z, dir.y, dir.x);
        d = max(d, dot(p - float2(len, 0.0), dir) - r * abs(dir.y));
    }
    return d;
}

// Signed distance along the contour to the nearest dash edge, negative inside a dash.
// data.x is the period in world units; data.y packs the dash fraction and the phase as two
// 11 bit values, both period-relative so the quantization stays subpixel. A dash's center
// sits at u = phase * period. The wrap seam lands mid gap where both sides agree, and a
// frac that lands exactly on 1.0 (see DecodeDigit) is absorbed by the abs symmetry.
float DashDistance(float u, float2 data) {
    float m = data.y;
    float ph = DecodeDigit(m, 2048.0) / 2047.0;
    float fr = m / 2047.0;
    float t = frac(u / data.x - ph + 0.5) - 0.5;
    return (abs(t) - fr * 0.5) * data.x;
}

// Signed pattern distance to the nearest dash edge, plus the contour positions of the two
// edges bounding the pixel's dash or gap, so the caller can measure to both edges' real
// geometry: near a corner they sit on different zones, and using only the nearest one
// would jump where the pattern midpoint flips between them.
float3 DashEdges(float u, float2 data) {
    float m = data.y;
    float ph = DecodeDigit(m, 2048.0) / 2047.0;
    float h = m / 2047.0 * 0.5;
    float t = frac(u / data.x - ph + 0.5) - 0.5;
    float db = min(frac(t - h), frac(t + h));
    float da = min(frac(h - t), frac(-h - t));
    return float3((abs(t) - h) * data.x, u - db * data.x, u + da * data.x);
}

float2 Perp(float2 v) {
    return float2(-v.y, v.x);
}

float2 Rot(float2 v, float a) {
    float s, c;
    sincos(a, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// Signed world distance to the nearest dash edge of a closed outline, negative inside a dash.
// Every dash edge is a straight line: perpendicular to a straight run, or a ray out of a
// corner arc's center. So the distance to one is exact, taken from any point on it and its
// unit tangent, and for an arc the center serves as that point since it lies on every ray.
// The alternative, rescaling the contour offset by the local gradient of the perimeter
// coordinate, is only right when the pixel and the dash edge sit in the same zone. That
// gradient jumps where a run meets a corner - parallel lines on one side, converging rays on
// the other - so a dash edge landing near a corner rather than on it puts a step in the
// middle of the anti-aliasing ramp, and coverage climbs again as it falls off.
float DashCutFromEdges(float2 q, float3 de, float2 pb, float2 nb, float2 pa, float2 na) {
    float m = min(dot(q - pb, nb), -dot(q - pa, na));
    return de.x >= 0.0 ? m : -m;
}

// Spine point crossed by the dash edge at contour position ue: on this segment in the
// linear zone, on the corner arc through the arc spans, and on the neighbor past them.
// fr is each end's fillet radius, which is not the stroke radius; see PathDashCut.
float2 PathEdgePoint(float ue, float2 fr, float startLen, float thA, float thB, float uA, float uB, float2 cA, float2 cB) {
    float aA = abs(thA);
    float aB = abs(thB);
    if (ue > uB && aB > 1e-4) {
        float sB = sign(thB);
        float se = ue - uB - aB * fr.y;
        if (se > 0.0) {
            float2 nb;
            sincos(thB, nb.y, nb.x);
            return cB + float2(sin(aB), -sB * cos(aB)) * fr.y + nb * se;
        }
        float psi = (ue - uB) / fr.y;
        return cB + float2(sin(psi), -sB * cos(psi)) * fr.y;
    }
    if (ue < uA && aA > 1e-4) {
        float sA = sign(thA);
        float se = uA - aA * fr.x - ue;
        if (se > 0.0) {
            float2 pv = float2(cos(thA), -sin(thA));
            return cA + float2(-sin(aA), -sA * cos(aA)) * fr.x - pv * se;
        }
        float psi = (uA - ue) / fr.x;
        return cA + float2(-sin(psi), -sA * cos(psi)) * fr.x;
    }
    return float2(ue - startLen, 0.0);
}

// Dash cut for a path segment, negative inside a dash. For dashing, each joint rounds the
// spine corner with a fillet arc tangent to both segments, and the pattern runs at unit
// speed along that rounded spine. The fillet radius, fr per end, is deliberately NOT the
// stroke radius: at exactly the stroke radius the fillet's inward offset collapses to a
// single point that lies on the stroke's own inner edge, every dash boundary in the fan is
// a ray out of that point, so all of them meet there and the gaps pinch shut to nothing.
// Whatever is done about it downstream, the anti-aliasing blur around that point still
// paints a speck adrift in the gap. A wider fillet puts its center clear of the stroke, so
// no two dash boundaries meet anywhere that gets drawn and the degeneracy is gone rather
// than patched. The CPU picks the radius per joint and sends it quantized, so both quads at
// the joint derive the same field and the partition seam stays invisible.
// Flat dashes are cut purely by the pixel's own contour coordinate, which can never ghost a
// cut across the joint, converted to a true distance so edges, borders and AA keep their
// true width through the corner fans. In a fan the cut is a ray out of the fillet center,
// so a contour offset at distance lw from it spans lw / fr of world distance; taking that
// factor from the geometry rather than from the coordinate's screen derivative is what
// keeps it exact where the derivative is worthless. Rounded dashes are the exact capsule
// around the rounded spine, built from the bounding edges' spine points. thA and thB are
// the signed turn angles at the ends, zero at caps and at collinear, overlapping, and
// reversed joints, where the pattern just runs straight out, matching the line shape.
// type >= 1.5 selects rounded dashes.
float PathDashCut(float2 q, float len, float r, float2 fr, float startLen, float thA, float thB, float2 data, float type) {
    float aA = abs(thA);
    float aB = abs(thB);
    float sA = sign(thA);
    float sB = sign(thB);
    float tA = aA > 1e-4 ? fr.x * tan(aA * 0.5) : 0.0;
    float tB = aB > 1e-4 ? fr.y * tan(aB * 0.5) : 0.0;
    float uA = startLen + tA;
    float uB = startLen + len - tB;
    float2 cA = float2(tA, sA * fr.x);
    float2 cB = float2(len - tB, sB * fr.y);

    float u = startLen + q.x;
    float v = q.y;
    float sc = 1.0; // World distance per unit of contour offset, 1 outside the fans.
    if (aB > 1e-4 && q.x > len - tB) {
        float2 w = q - cB;
        float lw = length(w);
        u = uB + clamp(atan2(w.x, -sB * w.y), 0.0, aB) * fr.y;
        v = lw - fr.y;
        sc = lw / fr.y;
    } else if (aA > 1e-4 && q.x < tA) {
        float2 w = q - cA;
        float lw = length(w);
        u = uA - clamp(atan2(-w.x, -sA * w.y), 0.0, aA) * fr.x;
        v = lw - fr.x;
        sc = lw / fr.x;
    }

    float3 de = DashEdges(u, data);

    if (type >= 1.5) {
        // The exact capsule around the rounded spine: inside the dash's span the distance
        // to the spine, outside it the distance to the nearer of the two cap circles.
        if (de.x < 0.0) {
            return abs(v) - r;
        }
        float2 pb = PathEdgePoint(de.y, fr, startLen, thA, thB, uA, uB, cA, cB);
        float2 pa = PathEdgePoint(de.z, fr, startLen, thA, thB, uA, uB, cA, cB);
        return min(length(q - pb), length(q - pa)) - r;
    }
    float du = de.x * sc;

    // Miter and bevel tips reach past the joint disc. A dash edge near the corner would
    // sweep them as a needle, so out there the dash is bounded by the disc instead and
    // the tip only grows back out, receding edge first, as the dash covers the whole
    // corner span with margin to spare. The margin comes from the exact maximum of the
    // sawtooth over the span, so the growth animates smoothly.
    float corner = -1e6;
    if (aB > 1e-4 && q.x > len) {
        float wB = aB * fr.y * 0.5;
        corner = length(q - float2(len, 0.0)) - r + min(DashDistance(uB + wB, data) + wB, 0.0) * 2.0;
    } else if (aA > 1e-4 && q.x < 0.0) {
        float wA = aA * fr.x * 0.5;
        corner = length(q) - r + min(DashDistance(uA - wA, data) + wA, 0.0) * 2.0;
    }
    return max(du, corner);
}

// The radius the dash pattern walks a corner on. Every dash cut in a corner is a ray out of
// that arc's center, so all of them meet there. At the shape's own rounding that center is
// the border band's inner vertex whenever the band is as thick as the rounding, and the
// anti-aliasing blur around it paints a speck adrift in the gap. Running the pattern on a
// wider arc puts the center past the band's inner edge, so nothing that gets drawn sees it.
// The cap keeps the widened corner inside the shape; at a rounding already wider than the
// band this returns the rounding untouched, so the usual case is bit for bit as before.
float PatternRadius(float ro, float lineSize, float cap) {
    return max(ro, min(1.5 * lineSize, cap));
}

// Perimeter coordinate of a regular polygon with the given apothem and half side, dilated
// outward by ro. Edge k's outward normal sits at normal0 + k * step; u runs one edge then
// one corner arc per sector, with sectors bounded by the rays through the corners. Sector
// indices from atan2 can differ by a full turn, which shifts u by the exact perimeter and
// washes out in the pattern wrap once the period is snapped. The corners run on the pattern
// radius, so the polygon is re-inset to keep those arcs tangent to the edges; the inset is
// radial, which leaves the sector rays exactly where they were.
float RegularPerimeter(float2 q, float aP, float hsP, float step, float normal0, float rp) {
    float th = atan2(q.y, q.x);
    float sector = floor((th - normal0) / step + 0.5);
    float ang = normal0 + sector * step;
    float2 dirN;
    sincos(ang, dirN.y, dirN.x);
    float t = dirN.x * q.y - dirN.y * q.x;
    float tc = clamp(t, -hsP, hsP);
    float u = sector * (2.0 * hsP + rp * step) + tc + hsP;
    float ex = t - tc;
    if (abs(ex) > 0.0) {
        float2 vtx = dirN * aP + Perp(dirN) * (sign(ex) * hsP);
        u += rp * atan2(dirN.x * (q.y - vtx.y) - dirN.y * (q.x - vtx.x), dot(q - vtx, dirN));
    }
    return u;
}

// Point on the perimeter at contour position ue, the unit tangent there, and the point the
// band's centerline crosses. The first two pin down the dash edge (see DashCutFromEdges); the
// third is a rounded dash's cap center. One sector is one edge run followed by one corner arc,
// so the sector index falls out of a floor and needs no wrapping.
void RegularFrame(float ue, float aP, float hsP, float step, float normal0, float rp, float rd,
                  out float2 pt, out float2 tng, out float2 ctr) {
    float sl = 2.0 * hsP + rp * step;
    float k = floor(ue / sl);
    float s = ue - k * sl;
    float2 dirN;
    sincos(normal0 + k * step, dirN.y, dirN.x);
    float2 e = Perp(dirN);
    float2 nh = dirN;
    if (s <= 2.0 * hsP) {
        pt = dirN * aP + e * (s - hsP);
        tng = e;
    } else {
        pt = dirN * aP + e * hsP; // The arc center, which every ray out of it passes through.
        nh = Rot(dirN, (s - 2.0 * hsP) / max(rp, 1e-6));
        tng = Perp(nh);
    }
    ctr = pt + nh * (rp - rd);
}

float RegularDashCut(float2 q, float apothem, float hs, float step, float normal0, float ro,
                     float lineSize, float2 data, out float2 capA, out float2 capB) {
    float aOut = apothem + ro; // Apothem of the outline itself.
    float rp = PatternRadius(ro, lineSize, aOut * 0.5);
    float aP = aOut - rp;
    float hsP = apothem > 1e-6 ? hs * aP / apothem : hs;

    float3 de = DashEdges(RegularPerimeter(q, aP, hsP, step, normal0, rp), data);
    float2 pb, nb, pa, na;
    RegularFrame(de.y, aP, hsP, step, normal0, rp, lineSize * 0.5, pb, nb, capA);
    RegularFrame(de.z, aP, hsP, step, normal0, rp, lineSize * 0.5, pa, na, capB);
    return DashCutFromEdges(q, de, pb, nb, pa, na);
}

// Perimeter coordinate of the rounded box, zero where the top edge leaves the top-left arc,
// increasing clockwise on screen. r is (top-right, bottom-right, top-left, bottom-left).
// Each corner runs on its own pattern radius, so the straight runs between them shorten to
// match; see PatternRadius. The CPU walks the same widened perimeter.
float RoundBoxPerimeter(float2 q, float2 b, float4 r) {
    float lRight = 2.0 * b.y - r.x - r.y;
    const float hpi = 1.5707963267948966;
    float uTR = 2.0 * b.x - r.z - r.x;
    float uRight = uTR + hpi * r.x;
    float uBottom = uRight + lRight + hpi * r.y;
    float uBL = uBottom + 2.0 * b.x - r.y - r.w;
    float uLeft = uBL + hpi * r.w;
    float uTL = uLeft + 2.0 * b.y - r.w - r.z;

    float rq = q.x > 0.0 ? (q.y > 0.0 ? r.y : r.x) : (q.y > 0.0 ? r.w : r.z);
    float2 c = float2(sign(q.x) * (b.x - rq), sign(q.y) * (b.y - rq));
    if (abs(q.x) > b.x - rq && abs(q.y) > b.y - rq) {
        // Corner arc: the angle from the arc's start direction, which rotates clockwise
        // on screen through top-right, bottom-right, bottom-left, top-left.
        float2 w = q - c;
        float2 s;
        float u0;
        if (q.x > 0.0) {
            if (q.y < 0.0) { s = float2(0.0, -1.0); u0 = uTR; }
            else { s = float2(1.0, 0.0); u0 = uRight + lRight; }
        } else {
            if (q.y > 0.0) { s = float2(0.0, 1.0); u0 = uBL; }
            else { s = float2(-1.0, 0.0); u0 = uTL; }
        }
        return u0 + rq * atan2(s.x * w.y - s.y * w.x, dot(s, w));
    }
    if (abs(q.x) - b.x > abs(q.y) - b.y) {
        return q.x > 0.0 ? uRight + q.y + b.y - r.x : uLeft + b.y - r.w - q.y;
    }
    return q.y > 0.0 ? uBottom + b.x - r.y - q.x : q.x + b.x - r.z;
}

// Point on the perimeter at contour position ue and the unit tangent there; the eight zones
// run in the same order the coordinate does. Unlike the regular polygon the corners differ,
// so ue wraps against the whole perimeter rather than falling out of one sector.
void RoundBoxFrame(float ue, float2 b, float4 r, float rd, out float2 pt, out float2 tng, out float2 ctr) {
    const float hpi = 1.5707963267948966;
    float uTR = 2.0 * b.x - r.z - r.x;
    float uRight = uTR + hpi * r.x;
    float uBR = uRight + 2.0 * b.y - r.x - r.y;
    float uBottom = uBR + hpi * r.y;
    float uBL = uBottom + 2.0 * b.x - r.y - r.w;
    float uLeft = uBL + hpi * r.w;
    float uTL = uLeft + 2.0 * b.y - r.w - r.z;
    float per = uTL + hpi * r.z;
    float s = ue - floor(ue / max(per, 1e-6)) * per;

    // On a run the perimeter point sits on the outline, so the centerline is one band radius
    // inward; on an arc it is that much in from the arc's own radius.
    if (s < uTR) {
        pt = float2(-b.x + r.z + s, -b.y);
        tng = float2(1.0, 0.0);
        ctr = float2(pt.x, -b.y + rd);
    } else if (s < uRight) {
        pt = float2(b.x - r.x, -b.y + r.x);
        float2 nh = Rot(float2(0.0, -1.0), (s - uTR) / max(r.x, 1e-6));
        tng = Perp(nh);
        ctr = pt + nh * (r.x - rd);
    } else if (s < uBR) {
        pt = float2(b.x, -b.y + r.x + (s - uRight));
        tng = float2(0.0, 1.0);
        ctr = float2(b.x - rd, pt.y);
    } else if (s < uBottom) {
        pt = float2(b.x - r.y, b.y - r.y);
        float2 nh = Rot(float2(1.0, 0.0), (s - uBR) / max(r.y, 1e-6));
        tng = Perp(nh);
        ctr = pt + nh * (r.y - rd);
    } else if (s < uBL) {
        pt = float2(b.x - r.y - (s - uBottom), b.y);
        tng = float2(-1.0, 0.0);
        ctr = float2(pt.x, b.y - rd);
    } else if (s < uLeft) {
        pt = float2(-b.x + r.w, b.y - r.w);
        float2 nh = Rot(float2(0.0, 1.0), (s - uBL) / max(r.w, 1e-6));
        tng = Perp(nh);
        ctr = pt + nh * (r.w - rd);
    } else if (s < uTL) {
        pt = float2(-b.x, b.y - r.w - (s - uLeft));
        tng = float2(0.0, -1.0);
        ctr = float2(-b.x + rd, pt.y);
    } else {
        pt = float2(-b.x + r.z, -b.y + r.z);
        float2 nh = Rot(float2(-1.0, 0.0), (s - uTL) / max(r.z, 1e-6));
        tng = Perp(nh);
        ctr = pt + nh * (r.z - rd);
    }
}

float RoundBoxDashCut(float2 q, float2 b, float4 rr, float lineSize, float2 data,
                      out float2 capA, out float2 capB) {
    float cap = min(b.x, b.y) * 0.5;
    float4 r = float4(PatternRadius(rr.x, lineSize, cap), PatternRadius(rr.y, lineSize, cap),
                      PatternRadius(rr.z, lineSize, cap), PatternRadius(rr.w, lineSize, cap));

    float3 de = DashEdges(RoundBoxPerimeter(q, b, r), data);
    float2 pb, nb, pa, na;
    RoundBoxFrame(de.y, b, r, lineSize * 0.5, pb, nb, capA);
    RoundBoxFrame(de.z, b, r, lineSize * 0.5, pa, na, capB);
    return DashCutFromEdges(q, de, pb, nb, pa, na);
}

// How far a vertex slides when both its edges are pushed inward by delta.
float2 MiterShift(float2 nIn, float2 nOut, float delta) {
    float2 s = nIn + nOut;
    return -s * (delta / max(1.0 + dot(nIn, nOut), 1e-3));
}

// Perimeter coordinate of the triangle vA → vB → vC, dilated outward by the pattern radius.
// The edges are walked in order with a corner arc of that radius times the exterior angle
// between them; the vertex wedges hand the angle term the way RegularPerimeter does.
float TrianglePerimeter(float2 q, float2 vA, float2 vB, float2 vC, float rp, float orr) {
    float2 e0 = vB - vA;
    float2 e1 = vC - vB;
    float2 e2 = vA - vC;
    float l0 = length(e0);
    float l1 = length(e1);
    float l2 = length(e2);
    float2 d0 = e0 / l0;
    float2 d1 = e1 / l1;
    float2 d2 = e2 / l2;

    // Exterior angles at b (between edge 0 and 1) and at c (between edge 1 and 2).
    float extB = orr * atan2(d0.x * d1.y - d0.y * d1.x, dot(d0, d1));
    float extC = orr * atan2(d1.x * d2.y - d1.y * d2.x, dot(d1, d2));

    // Unclamped along-edge coordinates and squared distances to each clamped edge.
    float t0 = dot(q - vA, d0);
    float t1 = dot(q - vB, d1);
    float t2 = dot(q - vC, d2);
    float2 p0 = q - vA - d0 * clamp(t0, 0.0, l0);
    float2 p1 = q - vB - d1 * clamp(t1, 0.0, l1);
    float2 p2 = q - vC - d2 * clamp(t2, 0.0, l2);
    float q0 = dot(p0, p0);
    float q1 = dot(p1, p1);
    float q2 = dot(p2, p2);

    float cum;
    float t;
    float len;
    float2 dir;
    float2 v0;
    if (q0 <= q1 && q0 <= q2) {
        cum = 0.0; t = t0; len = l0; dir = d0; v0 = vA;
    } else if (q1 <= q2) {
        cum = l0 + rp * extB; t = t1; len = l1; dir = d1; v0 = vB;
    } else {
        cum = l0 + l1 + rp * (extB + extC); t = t2; len = l2; dir = d2; v0 = vC;
    }
    float tc = clamp(t, 0.0, len);
    float u = cum + tc;
    float ex = t - tc;
    if (abs(ex) > 0.0) {
        // Vertex wedge: the angle of q around the vertex, measured from the edge's outward
        // normal so it runs negative before an edge and positive past it.
        float2 n = orr * Perp(-dir);
        float2 w = q - (v0 + dir * tc);
        u += rp * orr * atan2(n.x * w.y - n.y * w.x, dot(n, w));
    }
    return u;
}

// Point on the perimeter at contour position ue and the unit tangent there. The six zones are
// the three edge runs, each followed by the corner arc at the vertex it ends on.
void TriangleFrame(float ue, float2 vA, float2 vB, float2 vC, float rp, float orr, float rd,
                   out float2 pt, out float2 tng, out float2 ctr) {
    float2 e0 = vB - vA;
    float2 e1 = vC - vB;
    float2 e2 = vA - vC;
    float l0 = length(e0);
    float l1 = length(e1);
    float l2 = length(e2);
    float2 d0 = e0 / l0;
    float2 d1 = e1 / l1;
    float2 d2 = e2 / l2;

    float aB = rp * orr * atan2(d0.x * d1.y - d0.y * d1.x, dot(d0, d1));
    float aC = rp * orr * atan2(d1.x * d2.y - d1.y * d2.x, dot(d1, d2));
    float aA = rp * orr * atan2(d2.x * d0.y - d2.y * d0.x, dot(d2, d0));
    float per = l0 + l1 + l2 + aB + aC + aA;
    float s = ue - floor(ue / max(per, 1e-6)) * per;

    // Walk the zones in order, peeling each one off s as it is ruled out. The perimeter point
    // on a run sits on the inset triangle, so the centerline is that far out along the run's
    // outward normal; on an arc it is the same distance out from the vertex.
    float2 v = vB;
    float2 dIn = d0;
    if (s < l0) {
        pt = vA + d0 * s;
        tng = d0;
        ctr = pt + orr * Perp(-d0) * (rp - rd);
        return;
    }
    s -= l0;
    if (s >= aB) {
        s -= aB;
        if (s < l1) {
            pt = vB + d1 * s;
            tng = d1;
            ctr = pt + orr * Perp(-d1) * (rp - rd);
            return;
        }
        s -= l1;
        v = vC;
        dIn = d1;
        if (s >= aC) {
            s -= aC;
            if (s < l2) {
                pt = vC + d2 * s;
                tng = d2;
                ctr = pt + orr * Perp(-d2) * (rp - rd);
                return;
            }
            s -= l2;
            v = vA;
            dIn = d2;
        }
    }
    // Corner arc: a ray out of the vertex, so the vertex itself pins the line down. The arc
    // starts on the outward normal of the edge that runs into it and sweeps by the exterior
    // angle, and the tangent is a quarter turn ahead of wherever it has swept to.
    float2 nh = Rot(orr * Perp(-dIn), orr * s / max(rp, 1e-6));
    pt = v;
    tng = orr * Perp(nh);
    ctr = v + nh * (rp - rd);
}

// Dash cut for the triangle A(0,0) → b → c. The corner arcs run wider than the shape's own
// rounding (see PatternRadius), so the triangle is re-inset by the difference to keep them
// tangent to the same edges. Parallel inset never turns an edge, so the exterior angles, and
// with them the corner arc spans, are untouched.
float TriangleDashCut(float2 q, float2 b, float2 c, float ro, float lineSize, float2 data,
                      out float2 capA, out float2 capB) {
    // The winding sign makes the exterior angles positive for either input orientation.
    float orr = (b.x * (c.y - b.y) - b.y * (c.x - b.x)) >= 0.0 ? 1.0 : -1.0;

    float2 g0 = normalize(b);
    float2 g1 = normalize(c - b);
    float2 g2 = normalize(-c);
    // Outward edge normals, matching the vertex wedge's frame.
    float2 n0 = orr * Perp(-g0);
    float2 n1 = orr * Perp(-g1);
    float2 n2 = orr * Perp(-g2);

    // The inradius of the already inset triangle bounds how much further it can go.
    float inR = abs(b.x * c.y - b.y * c.x) / max(length(b) + length(c) + length(c - b), 1e-6);
    float rp = PatternRadius(ro, lineSize, ro + inR * 0.5);
    float delta = rp - ro;
    float2 vA = MiterShift(n2, n0, delta);
    float2 vB = b + MiterShift(n0, n1, delta);
    float2 vC = c + MiterShift(n1, n2, delta);

    float3 de = DashEdges(TrianglePerimeter(q, vA, vB, vC, rp, orr), data);
    float2 pb, nb, pa, na;
    TriangleFrame(de.y, vA, vB, vC, rp, orr, lineSize * 0.5, pb, nb, capA);
    TriangleFrame(de.z, vA, vB, vC, rp, orr, lineSize * 0.5, pa, na, capB);
    return DashCutFromEdges(q, de, pb, nb, pa, na);
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
// band of the seam toward 0.5, the box-filtered average of both sides, since
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

float2 Unpack11(float v) {
    float lo = DecodeDigit(v, 2048.0);
    return float2(v, lo) / 2047.0;
}
float4 UnpackColor(float2 c) {
    return float4(Unpack11(c.x), Unpack11(c.y));
}

#if VULKAN
// MonoGame's native Vulkan backend maps NormalizedShort4 attributes to SSCALED instead
// of SNORM (ToVkFormat in MGG_Vulkan.cpp), so the packed colors arrive as raw 0..32767
// integers. Unscale only when raw values show up: legitimate channels never exceed 1,
// so this goes quiet on its own once the mapping is fixed upstream.
float4 FixSnorm(float4 v) { return any(v > 1.5) ? v / 32767.0 : v; }
#else
float4 FixSnorm(float4 v) { return v; }
#endif

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    output.Fill = PackColors(FixSnorm(v.FillA), FixSnorm(v.FillB));
    output.Border = PackColors(FixSnorm(v.BorderA), FixSnorm(v.BorderB));
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
// interpolated world position (exact per-triangle constants under affine views,
// smooth and perspective-correct otherwise), never from derivatives of the SDF
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

// Interleaved gradient noise (Jimenez 2014). Screen-space noise with a spectrum close
// enough to blue noise to dither away gradient banding without a texture. The pattern
// is static: at half-LSB amplitude a fixed pattern is invisible whether the gradient
// moves under it or sits still, while animating it would shimmer. The coordinate is
// rebuilt from the interpolated world position because ps_3_0 offers no pixel position
// and every interpolator is taken; any affine map of true pixel coordinates works, so
// the viewport half-offset and y flip are dropped. The saturate guards the flaky
// ps_3_0 frac (see DecodeDigit): a rare off-by-one pixel stays a valid dither value.
float DitherNoise(float2 worldPos) {
    float4 clip = mul(float4(worldPos, 0.0, 1.0), view_projection);
    float2 px = clip.xy / clip.w * half_viewport;
    float ign = saturate(frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))));
    // Both sources are always evaluated so no gradient op sits inside flow control;
    // the blue noise fetch hits a 64x64 tile that never leaves the texture cache.
    float blue = SampleBlueNoise(px / 64.0).r;
    return dither_mode < 0.5 ? ign : blue;
}

float4 SpritePixelShader(PixelInput p) : SV_TARGET {
    float lineSize = p.Meta1.x;
    float aaPixels = p.Meta1.y;
    float sdfSize = p.Meta1.z;

    // Peel the packed meta apart field by field. Every intermediate stays an exact integer.
    float meta = p.TexCoord.w;
    float shape = DecodeDigit(meta, 16.0);
    float2 fillStyles;
    float2 borderStyles;
    fillStyles.x = DecodeDigit(meta, 16.0);
    fillStyles.y = DecodeDigit(meta, 4.0);
    borderStyles.x = DecodeDigit(meta, 16.0);
    borderStyles.y = DecodeDigit(meta, 4.0);
    float space = DecodeDigit(meta, 4.0);
    // 0 solid, 1 basic dashes, 2 rounded dashes. Where the pattern rides depends on the shape.
    float dashType = meta;

    float2 footprint = PixelFootprint(p.Pos.xy);

    // Rounded box SDF from the interpolated edge distances.
    float2 clipQ = p.ClipMeta.z - min(p.Pos.zw, p.ClipMeta.xy);
    float clipD = length(max(clipQ, 0.0)) + min(max(clipQ.x, clipQ.y), 0.0) - p.ClipMeta.z;
    float clipAa = PixelWidth(clipD, footprint) * p.ClipMeta.w;
    if (clipD >= clipAa) {
        discard;
    }
    float clipAlpha = 1.0 - smoothstep(0.0, 1.0, saturate(clipD / clipAa));

    if (shape >= 8.5 && shape < 10.5) {
        if (shape < 9.5) {
            return SampleTexture(p.TexCoord.xy) * UnpackColor(p.Fill.xy) * clipAlpha;
        }
        return SampleFont(p.TexCoord.xy) * UnpackColor(p.Fill.xy) * clipAlpha;
    }

    // Dash state. Strokes are cut into dashes through the SDF itself, from dashU along the
    // contour, dashV across it, and dashData, the (period, packed fraction and phase) pair
    // from the shape's spare channels. Closed outlines instead mask their border band with
    // dashCut, the world distance to the nearest dash edge. The defaults keep the flattened
    // out dash arithmetic finite when a shape isn't dashed.
    float2 q = p.TexCoord.xy;
    float rounded = p.TexCoord.z;
    float dashU = 0.0;
    float dashCut = 1.0;   // Closed outlines: world distance to the dash edge, negative inside.
    float2 dashCapA = float2(0.0, 0.0); // Where the band's centerline crosses each of the two
    float2 dashCapB = float2(0.0, 0.0); // bounding edges: a rounded dash's cap centers.
    float dashV = 0.0;
    float dashR = 0.0;
    float2 dashData = float2(1.0, 0.0);
    bool dashStroke = false;

    float d;
    if (shape < 0.5) {
        d = CircleSDF(q, sdfSize);
        if (dashType >= 0.5) {
            // The circle is one arc end to end, so every dash edge is a ray out of the center
            // and the center pins each of them down; see DashCutFromEdges.
            float rc = max(sdfSize, 1e-6);
            float3 de = DashEdges(atan2(q.y, q.x) * rc, p.Meta2.xy);
            float2 nb;
            sincos(de.y / rc, nb.y, nb.x);
            float2 na;
            sincos(de.z / rc, na.y, na.x);
            dashCut = DashCutFromEdges(q, de, float2(0.0, 0.0), Perp(nb), float2(0.0, 0.0), Perp(na));
            dashCapA = nb * (rc - lineSize * 0.5);
            dashCapB = na * (rc - lineSize * 0.5);
        }
    } else if (shape < 1.5) {
        float4 rr = p.Meta2;
        if (dashType >= 0.5) {
            // Dashed rectangles carry their corner radii as 11 bit fractions of the largest
            // allowed radius, freeing Meta2.zw for the pattern.
            float mr = min(sdfSize, p.Meta1.w);
            float mx = rr.x;
            float my = rr.y;
            float bx = DecodeDigit(mx, 2048.0);
            float by = DecodeDigit(my, 2048.0);
            rr = float4(mx, bx, my, by) / 2047.0 * mr;
            dashCut = RoundBoxDashCut(q, float2(sdfSize, p.Meta1.w), rr, lineSize, p.Meta2.zw, dashCapA, dashCapB);
        }
        d = RoundBoxSDF(q, float2(sdfSize, p.Meta1.w), rr);
    } else if (shape < 2.5) {
        d = SegmentSDF(q, float2(0.0, 0.0), float2(p.Meta1.w, 0.0));
        if (dashType >= 0.5) {
            dashU = q.x;
            dashV = q.y;
            dashR = sdfSize;
            dashData = p.Meta2.xy;
            dashStroke = true;
        }
    } else if (shape < 3.5) {
        d = HexagonSDF(q, sdfSize);
        if (dashType >= 0.5) {
            dashCut = RegularDashCut(q, sdfSize, sdfSize * 0.57735026919, 1.0471975512, 0.52359877560, rounded, lineSize, p.Meta2.xy, dashCapA, dashCapB);
        }
    } else if (shape < 4.5) {
        d = EquilateralTriangleSDF(q, sdfSize);
        if (dashType >= 0.5) {
            dashCut = RegularDashCut(q, sdfSize * 0.57735026919, sdfSize, 2.0943951024, 0.52359877560, rounded, lineSize, p.Meta2.xy, dashCapA, dashCapB);
        }
    } else if (shape < 5.5) {
        if (dashType >= 0.5) {
            // Dashed triangles put their first corner at the local origin, freeing Meta1.zw.
            d = TriangleSDF(q, float2(0.0, 0.0), p.Meta2.xy, p.Meta2.zw);
            dashCut = TriangleDashCut(q, p.Meta2.xy, p.Meta2.zw, rounded, lineSize, p.Meta1.zw, dashCapA, dashCapB);
        } else {
            d = TriangleSDF(q, p.Meta1.zw, p.Meta2.xy, p.Meta2.zw);
        }
    } else if (shape < 6.5) {
        d = EllipseSDF(q, float2(sdfSize, p.Meta1.w));
    } else if (shape < 7.5) {
        d = ArcSDF(q, p.Meta2.xy, sdfSize, p.Meta2.z);
        if (dashType >= 0.5) {
            dashU = (atan2(q.x, q.y) + atan2(p.Meta2.x, p.Meta2.y)) * sdfSize;
            dashV = length(q) - sdfSize;
            dashR = p.Meta2.z;
            dashData = float2(p.Meta1.w, p.Meta2.w);
            dashStroke = true;
        }
    } else if (shape < 8.5) {
        d = RingSDF(q, p.Meta2.xy, sdfSize, p.Meta2.z);
        if (dashType >= 0.5) {
            dashU = (atan2(q.x, q.y) + atan2(p.Meta2.y, p.Meta2.x)) * sdfSize;
            dashV = length(q) - sdfSize;
            dashR = p.Meta2.z * 0.5;
            dashData = float2(p.Meta1.w, p.Meta2.w);
            dashStroke = true;
        }
    } else {
        float4 sd = p.Meta2;
        float pathCut = -1e6;
        if (dashType >= 0.5) {
            // Dashed paths pack each end's signed turn angle into Meta2.y as two 11 bit codes
            // (1024 = 0, meaning caps and collinear joints), freeing Meta2.z for the segment's
            // start length and Meta2.w for the period. The rounding slot carries the packed
            // fraction and phase; paths never use it for rounding. The bevel plane directions
            // derive from the turn angles, so nothing else needs to travel. Each end's fillet
            // radius rides above the end modes in Meta2.x as a 7 bit fraction of the stroke
            // radius over [1, 2]; that keeps the packed value under 2^20, well inside what a
            // ps_3_0 interpolator carries exactly, unlike the two 11 bit codes which already
            // sit at the ceiling.
            float ma = sd.y;
            float thB = (DecodeDigit(ma, 2048.0) - 1024.0) / 1023.0 * 3.1415926536;
            float thA = (ma - 1024.0) / 1023.0 * 3.1415926536;
            float mr = sd.x;
            float modeBits = DecodeDigit(mr, 64.0);
            float frCodeA = DecodeDigit(mr, 128.0);
            float frCodeB = mr;
            float2 fr = sdfSize * (1.0 + float2(frCodeA, frCodeB) / 127.0);
            float2 hA;
            sincos(thA * 0.5, hA.y, hA.x);
            float2 hB;
            sincos(thB * 0.5, hB.y, hB.x);
            float sA = sign(thA);
            float sB = sign(thB);
            sd = float4(modeBits, atan2(-sA * hA.x, -sA * hA.y), atan2(-sB * hB.x, sB * hB.y), 0.0);
            pathCut = PathDashCut(q, p.Meta1.w, sdfSize, fr, p.Meta2.z, thA, thB, float2(p.Meta2.w, rounded), dashType);
            rounded = 0.0;
            dashType = 0.0;
        }
        d = max(StrokeSDF(q, p.Meta1.w, sdfSize, sd), pathCut);
    }

    d -= rounded;

    // Strokes are cut into dashes before AA so every dash gets its own edges, borders and
    // caps. Basic dashes cut flat across the spine; rounded dashes end in half circles that
    // exactly reproduce the round caps, so end dashes merge with them seamlessly.
    if (dashType >= 0.5 && dashStroke) {
        float du = DashDistance(dashU, dashData);
        if (dashType >= 1.5) {
            d = max(d, length(float2(max(du, 0.0), dashV)) - dashR);
        } else {
            d = max(d, du);
        }
    }

    float aaSize = PixelWidth(d, footprint) * aaPixels;

    // Beyond the outer AA edge every branch below resolves to premultiplied zero.
    if (d >= aaSize) {
        discard;
    }

    float4 fillA = UnpackColor(p.Fill.xy);
    float4 fillB = UnpackColor(p.Fill.zw);

    float edgeFade = 1.0 - smoothstep(0.0, 1.0, saturate(d / aaSize));
    float borderMix = smoothstep(0.0, 1.0, saturate((d + lineSize + aaSize) / aaSize));

    // Closed outlines mask their border band along the perimeter; the gaps show the fill.
    // The AA width comes from the pixel footprint alone, since the cut is already a world
    // distance and its screen derivative would misfire across the wrap seam. The cut is the
    // distance to the dash edge itself rather than a contour offset rescaled by how fast the
    // coordinate runs here, so it stays a true distance right through the corners, where the
    // two differ most: the band's inner side runs on a tighter arc than its outer side, and
    // the rescale also broke where a run meets a corner. See DashCutFromEdges.
    if (dashType >= 0.5 && !dashStroke) {
        float aaU = footprint.y * aaPixels;
        if (dashType >= 1.5) {
            // Round capped dashes: the exact capsule around the band's centerline, the way
            // PathDashCut builds one. Along the dash it is the band itself; past either end
            // it is the distance to that end's cap center. Measuring on the centerline is what
            // keeps the caps circular right across the band, at corners as much as anywhere.
            float rd = lineSize * 0.5;
            float capD = dashCut < 0.0 ? abs(d + rd) - rd
                                       : min(length(q - dashCapA), length(q - dashCapB)) - rd;
            borderMix = 1.0 - smoothstep(0.0, 1.0, saturate(capD / aaU));
        } else {
            borderMix *= 1.0 - smoothstep(0.0, 1.0, saturate(dashCut / aaU));
        }
    }

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

        float4 fr = ToRgb(fc, space);
        float4 br = ToRgb(bc, space);
        fr.rgb *= fr.a;
        br.rgb *= br.a;
        // The edge fade applies after the crossfade so the fill also fades where a dash gap
        // lets it reach the outer edge. Solid borders keep borderMix at 1 wherever the fade
        // is below 1, so this matches the old border-only fade exactly.
        result = lerp(fr, br, borderMix) * edgeFade;
    }

    result *= clipAlpha;
    // With premultiplied blending the source color adds straight into the framebuffer, so
    // offsetting rgb here dithers the post-blend value that actually quantizes to 8 bits,
    // covering banding from color and alpha gradients alike. Left unclamped on purpose:
    // the negative half must survive to dither near-black, and the target clamps on write.
    result.rgb += (DitherNoise(p.Pos.xy) - 0.5) * dither_scale;
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
