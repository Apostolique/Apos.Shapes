#if __KNIFX__
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#elif OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#elif SM6
// Vulkan and DirectX 12 compile through DXC, which requires shader model 6.
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
// Sampler order is load bearing, and the register annotations alone do not settle it: the
// OpenGL and Vulkan translator hands out texture units in the order the pixel shader first
// SAMPLES from, not the order the samplers are declared or the registers they ask for, while
// the KNI toolchain goes by the register. So the three have to agree, and that means listing
// them in the order the pixel shader reaches them: the texture and font masks return early,
// then the elliptical arc length table, then the dither noise at the very end. Getting this
// wrong is silent - the shader simply reads a different texture and the picture goes to noise.
#if SM6
// DXC drops the legacy sampler syntax: declare texture/sampler pairs on matching
// registers so the Vulkan reflection treats them as combined image-samplers.
Texture2D TextureTex : register(t0); SamplerState TextureSampler : register(s0);
Texture2D FontTex : register(t1); SamplerState FontSampler : register(s1);
Texture2D ArcTex : register(t2); SamplerState ArcSampler : register(s2); // Elliptical arc length table, bound with clamped point sampling.
Texture2D BlueNoiseTex : register(t3); SamplerState BlueNoiseSampler : register(s3); // 64x64 tile, bound with wrapped point sampling.
float4 SampleTexture(float2 uv) { return TextureTex.Sample(TextureSampler, uv); }
float4 SampleFont(float2 uv) { return FontTex.Sample(FontSampler, uv); }
float4 SampleArc(float2 uv) { return ArcTex.Sample(ArcSampler, uv); }
float4 SampleBlueNoise(float2 uv) { return BlueNoiseTex.Sample(BlueNoiseSampler, uv); }
#else
sampler TextureSampler : register(s0);
sampler FontSampler;
sampler ArcSampler : register(s2); // Elliptical arc length table, bound with clamped point sampling.
sampler BlueNoiseSampler : register(s3); // 64x64 tile, bound with wrapped point sampling.
float4 SampleTexture(float2 uv) { return tex2D(TextureSampler, uv); }
float4 SampleFont(float2 uv) { return tex2D(FontSampler, uv); }
float4 SampleArc(float2 uv) { return tex2D(ArcSampler, uv); }
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

// https://iquilezles.org/articles/distfunctions2d/
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
// Closest point on the ellipse with radii r to q, both already folded into the frame the solve
// wants: q in the first quadrant, the major axis on x, and the larger radius normalised to 1.
// Dashing needs the point itself, not just the distance to it, so this is the whole solve and
// EllipseSDF is the length of what it returns.
//
// The closest point satisfies the Lagrange condition with multiplier t, which this solves in
// the rational form
//     F(t) = (a*x / (a*a + t))^2 + (b*y / (b*b + t))^2 - 1 = 0
// rather than by clearing the denominators into a quartic the way the usual shadertoy
// versions do. F is strictly decreasing across the bracket below so its root is unique, and
// every cancellation lands against 1.0 instead of against (a*a+t)^2 * (b*b+t)^2, a quantity
// that grows like the eighth power of the radius and eats most of an fp32 mantissa on a
// large or thin ellipse. Radii are normalised to <= 1 first for the same reason. Measured
// against a double precision reference this holds ~1e-5 px across aspect ratios from 1:1 to
// 1:100, where the quartic form drifts to ~1e-2 px.
//
// The Newton loop runs a fixed number of times and MUST NOT gain an early break. ShadowDusk
// 0.14.0 gives a bounded HLSL loop a real GLSL loop header, whose fall-through exit its
// translation never assigns the result variable on, so on OpenGL alone any pixel that uses
// the whole iteration budget reads an uninitialised value. Keeping the loop unconditional
// keeps that variable live on every path.
float2 EllipseNearestPoint(float2 q, float2 r) {
    float aa = r.x * r.x;
    float bb = r.y * r.y;
    float aq = r.x * q.x;
    float bq = r.y * q.y;
    float ix = q.x / r.x;
    float iy = q.y / r.y;
    float inside = ix * ix + iy * iy - 1.0;

    // Both axes are 0/0 for F, so they get closed forms. On the major axis the closest point
    // is the vertex only outside the evolute cusp at (a*a - b*b)/a; inside it the point has
    // already passed the centre of curvature and the nearest point jumps off the axis.
    if (q.y <= 1e-5 * r.y) {
        if (q.x * r.x >= aa - bb) return float2(r.x, 0.0);
        float xc = aa * q.x / (aa - bb);
        float yc = r.y * sqrt(clamp(1.0 - xc * xc / aa, 0.0, 1.0));
        return float2(xc, yc);
    }
    // On the minor axis the matching cusp sits past the centre, so the covertex always wins.
    if (q.x <= 1e-5 * r.x) {
        return float2(0.0, r.y);
    }

    // Bracket the root, F(tmin) >= 0 >= F(tmax).
    //   t <= a*x - a*a drives the first term alone to 1, and likewise the second, hence max.
    //   (a*x)^2 + (b*y)^2 <= (b*b + t)^2 drives both together down to 1, hence tmax.
    float tmin = max(aq - aa, bq - bb);
    float tmax = length(float2(aq, bq)) - bb;

    // Tighten with the one term bounds, which shrink the bracket enough that eight Newton
    // steps reach fp32's floor. For t <= 0 the second term obeys v >= y/b, so u*u >= 1 -
    // (y/b)^2 already forces F >= 0 and caps t; for t >= 0 the inequality flips and it
    // floors t instead. A non-positive radicand means that term cannot reach 1 by itself, so
    // the bound does not apply and the sentinel makes the min a no-op.
    float ex = 1.0 - iy * iy;
    float ey = 1.0 - ix * ix;
    float bx = ex > 0.0 ? aq / sqrt(max(ex, 1e-30)) - aa : 1e30;
    float by = ey > 0.0 ? bq / sqrt(max(ey, 1e-30)) - bb : 1e30;
    if (inside < 0.0) {
        // Being inside forces both radicands positive, so both bounds are live here.
        tmax = min(tmax, 0.0);
        tmin = min(max(tmin, max(bx, by)), tmax);
    } else {
        tmin = max(tmin, 0.0);
        tmax = max(min(tmax, min(bx, by)), tmin);
    }

    float t = tmin;
    for (int i = 0; i < 8; i++) {
        float ia = 1.0 / (aa + t);
        float ib = 1.0 / (bb + t);
        float u = aq * ia;
        float v = bq * ib;
        // Newton on 1/|(u,v)| - 1 rather than on |(u,v)|^2 - 1. Same root, but this form is
        // very nearly linear over the whole bracket: against the pole at t = -b*b it tends to
        // (b*b + t)/(b*y) - 1, which is exactly linear, and far out it grows linearly too.
        // The squared form crawls away from that pole at 1.5x a step, which is what makes the
        // tip of a thin ellipse expensive.
        float rr = sqrt(u * u + v * v);
        float dd = u * u * ia + v * v * ib;
        t = clamp(t + (rr - 1.0) * rr * rr / max(dd, 1e-30), tmin, tmax);
    }

    // Renormalise onto the ellipse. (X/a, Y/b) is a unit vector by construction, so forcing
    // it to unit length turns whatever error is left in t into a tangential slip, which is
    // second order in the distance instead of first.
    float2 n = float2(aq / (aa + t), bq / (bb + t));
    n /= max(length(n), 1e-30);
    return n * r;
}

// Folds p and ab into that frame: q is |p| scaled so the larger radius is 1, with the major
// axis on x, and r the radii likewise. Every intermediate in the solve then stays near 1 no
// matter how many pixels across the ellipse is. The problem is symmetric under swapping both
// the radii and the coordinates, which is what puts the major axis on x and halves the solve's
// special cases; a caller that wants points back out swaps them the same way. Returns the
// scale, or 0 for an ellipse with no size at all.
float EllipseFold(float2 p, float2 ab, out float2 q, out float2 r) {
    float s = max(ab.x, ab.y);
    q = abs(p) / max(s, 1e-30);
    r = ab / max(s, 1e-30);
    if (r.y > r.x) {
        r = r.yx;
        q = q.yx;
    }
    return s;
}

// Signed distance to an origin centred, axis aligned ellipse with radii ab.
float EllipseSDF(float2 p, float2 ab) {
    float2 q, r;
    float s = EllipseFold(p, ab, q, r);
    if (s <= 0.0) return length(p);

    // A collapsed minor axis degenerates to a segment, which the solve cannot represent: its
    // bracket needs both denominators strictly positive.
    if (r.y <= 1e-7) {
        return length(float2(q.x - clamp(q.x, 0.0, r.x), q.y)) * s;
    }

    float ix = q.x / r.x;
    float iy = q.y / r.y;
    float sgn = ix * ix + iy * iy - 1.0 < 0.0 ? -1.0 : 1.0;
    return length(EllipseNearestPoint(q, r) - q) * s * sgn;
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

// Stroked path segment whose half width runs from rA at (0, 0) to rB at (len, 0). The sides
// are the two end circles' outer tangents, which is what keeps the edge continuous through a
// joint: neighbouring segments meet at the same width there, so their walls meet at the same
// point. End modes work the same as in StrokeSDF. With rA == rB this reduces to that capsule,
// which is why the caller only reaches for it when the two differ.
float StrokeConeSDF(float2 p, float len, float rA, float rB, float4 data) {
    float m = data.x;
    float modeA = DecodeDigit(m, 8.0);
    float modeB = m;
    float ay = abs(p.y);
    float xa = -p.x;
    float xb = p.x - len;
    float ea = modeA >= 2.5 ? -1e6 : (modeA >= 1.5 ? xa - rA : xa);
    float eb = modeB >= 2.5 ? -1e6 : (modeB >= 1.5 ? xb - rB : xb);
    float e = max(ea, eb);
    float mode = ea > eb ? modeA : modeB;

    float d;
    float b = (rA - rB) / len;
    if (abs(b) >= 1.0) {
        // One end's circle swallows the other, so the hull is that circle on its own and the
        // tangents don't exist. Rare, but the sqrt below would go imaginary.
        float2 c = rA >= rB ? float2(0.0, 0.0) : float2(len, 0.0);
        d = length(p - c) - max(rA, rB);
    } else {
        float a = sqrt(1.0 - b * b);
        // The wall is the tangent line with unit normal (b, a): it sits rA from the near circle
        // and rB from the far one, so it leans in as the stroke narrows. side is the distance to
        // it, k is how far along it the point projects. The wall spans k in [0, a * len]; past
        // either end the nearest thing is that end's circle.
        float side = b * p.x + a * ay - rA;
        float k = a * p.x - b * ay;
        if (mode < 0.5) {
            // Where the wall runs out, the nearest thing is that end's circle - unless the end is
            // open or bevelled, which both say there is nothing there to be near: the wall carries
            // on and the quad shapes the miter tip out of it, exactly as the capsule's own open end
            // does by clamping its excess to zero.
            if (k < 0.0 && modeA < 2.5) d = length(p) - rA;
            else if (k > a * len && modeB < 2.5) d = length(p - float2(len, 0.0)) - rB;
            else d = side;
        } else {
            // Sharp: the wall meets a flat end face. Interior still resolves to the wall.
            float2 q = float2(e, side);
            d = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
        }
    }
    if (modeA >= 3.5) {
        float2 dir;
        sincos(data.y, dir.y, dir.x);
        d = max(d, dot(p, dir) - rA * abs(dir.y));
    }
    if (modeB >= 3.5) {
        float2 dir;
        sincos(data.z, dir.y, dir.x);
        d = max(d, dot(p - float2(len, 0.0), dir) - rB * abs(dir.y));
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
// linear zone, on the corner arc through the arc spans, and on the neighbor past them. Also
// returns the unit direction the contour coordinate grows in there, which is the edge's own
// normal, since a dash edge is exactly the set of points at one contour position: the spine's
// own direction on a straight run, and the tangent to the fillet arc through a corner fan,
// where the edge is a ray out of the fillet center and every point on it shares its angle.
// fr is each end's fillet radius, which is not the stroke radius; see PathDashCut.
float2 PathEdgeFrame(float ue, float2 fr, float startLen, float thA, float thB, float uA, float uB, float2 cA, float2 cB, out float2 n) {
    float aA = abs(thA);
    float aB = abs(thB);
    if (ue > uB && aB > 1e-4) {
        float sB = sign(thB);
        float se = ue - uB - aB * fr.y;
        if (se > 0.0) {
            float2 nb;
            sincos(thB, nb.y, nb.x);
            n = nb;
            return cB + float2(sin(aB), -sB * cos(aB)) * fr.y + nb * se;
        }
        float psi = (ue - uB) / fr.y;
        n = float2(cos(psi), sB * sin(psi));
        return cB + float2(sin(psi), -sB * cos(psi)) * fr.y;
    }
    if (ue < uA && aA > 1e-4) {
        float sA = sign(thA);
        float se = uA - aA * fr.x - ue;
        if (se > 0.0) {
            float2 pv = float2(cos(thA), -sin(thA));
            n = pv;
            return cA + float2(-sin(aA), -sA * cos(aA)) * fr.x - pv * se;
        }
        float psi = (uA - ue) / fr.x;
        n = float2(cos(psi), -sA * sin(psi));
        return cA + float2(-sin(psi), -sA * cos(psi)) * fr.x;
    }
    n = float2(1.0, 0.0);
    return float2(ue - startLen, 0.0);
}

// Half the stroke's width at a contour position. A joint's fan is one width all the way round -
// the joint point's own - so both quads sharing it agree everywhere in it, and the taper runs
// between the fans over the straight middle. Uniform strokes answer rA everywhere.
// lean is the cosine of the taper over that middle, where the stroke's wall is the two end
// circles' common tangent and so stands 1 / lean further out than the radius. A rounded dash is
// built as a capsule around the spine, and a capsule of the plain radius would cut the stroke
// narrow by exactly that factor, so the radius it reaches with is the one that lands on the
// wall. The fans keep the plain radius: their own boundary is the joint's disc, not a tangent.
float PathRadiusAt(float ue, float uA, float uB, float rA, float rB, float lean) {
    float r = lerp(rA, rB, saturate((ue - uA) / max(uB - uA, 1e-6)));
    return ue > uA && ue < uB ? r / lean : r;
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
// Flat dashes measure the world distance to the two bounding dash edges themselves, which is
// what keeps edges, borders and AA at their true width right through the corner fans. Every
// dash edge is a straight line - perpendicular to a straight run, or a ray out of the fillet
// center - so the distance to one is exact from any point on it and its unit tangent, which
// is what PathEdgeFrame returns. See DashCutFromEdges, the same measurement every closed
// outline makes.
// Rounded dashes are the exact capsule around the rounded spine, built from the bounding
// edges' spine points. thA and thB are the signed turn angles at the ends, zero at caps and
// at collinear, overlapping, and reversed joints, where the pattern just runs straight out,
// matching the line shape.
// The stroke's half width runs from rA at this end to rB at the far one, and every place the
// pattern needs a width - the fillets it rounds corners with, the discs that bound a tip, a
// rounded dash's body and caps - takes the width where it stands rather than one for the whole
// segment. A uniform stroke passes the same radius twice and every one of them collapses.
// type >= 1.5 selects rounded dashes.
float PathDashCut(float2 q, float len, float rA, float rB, float2 fr, float startLen, float thA, float thB, float2 data, float type) {
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
    // The taper the dash sees over the straight middle, and the cosine that goes with it. Past a
    // slope of one the tangent stops existing - one end's circle has swallowed the other - and
    // there is nothing sensible to lean, so it stays upright.
    float slope = (rB - rA) / max(uB - uA, 1e-6);
    float lean = abs(slope) < 1.0 ? sqrt(1.0 - slope * slope) : 1.0;

    // The pixel's own contour coordinate, which is what puts it between a pair of dash edges.
    // Inside a fan it is the angle around the fillet center, so the coordinate never folds:
    // the center sits clear of the stroke, so nothing that gets drawn reaches it.
    float u = startLen + q.x;
    float v = q.y;
    if (aB > 1e-4 && q.x > len - tB) {
        float2 w = q - cB;
        u = uB + clamp(atan2(w.x, -sB * w.y), 0.0, aB) * fr.y;
        v = length(w) - fr.y;
    } else if (aA > 1e-4 && q.x < tA) {
        float2 w = q - cA;
        u = uA - clamp(atan2(-w.x, -sA * w.y), 0.0, aA) * fr.x;
        v = length(w) - fr.x;
    }

    float3 de = DashEdges(u, data);

    // The exact capsule around the rounded spine: inside the dash's span the distance to the
    // spine, and nothing else is needed there.
    if (type >= 1.5 && de.x < 0.0) {
        return abs(v) - PathRadiusAt(u, uA, uB, rA, rB, lean);
    }

    float2 nb, na;
    float2 pb = PathEdgeFrame(de.y, fr, startLen, thA, thB, uA, uB, cA, cB, nb);
    float2 pa = PathEdgeFrame(de.z, fr, startLen, thA, thB, uA, uB, cA, cB, na);
    if (type >= 1.5) {
        // Past the dash's span the capsule is the distance to the nearer of the two cap circles,
        // each one as wide as the stroke is where it sits.
        return min(length(q - pb) - PathRadiusAt(de.y, uA, uB, rA, rB, lean),
                   length(q - pa) - PathRadiusAt(de.z, uA, uB, rA, rB, lean));
    }
    float du = DashCutFromEdges(q, de, pb, nb, pa, na);

    // Miter and bevel tips reach past the joint disc. A dash edge near the corner would
    // sweep them as a needle, so out there the dash is bounded by the disc instead and
    // the tip only grows back out, receding edge first, as the dash covers the whole
    // corner span with margin to spare. The margin comes from the exact maximum of the
    // sawtooth over the span, so the growth animates smoothly.
    float corner = -1e6;
    if (aB > 1e-4 && q.x > len) {
        float wB = aB * fr.y * 0.5;
        corner = length(q - float2(len, 0.0)) - rB + min(DashDistance(uB + wB, data) + wB, 0.0) * 2.0;
    } else if (aA > 1e-4 && q.x < 0.0) {
        float wA = aA * fr.x * 0.5;
        corner = length(q) - rA + min(DashDistance(uA - wA, data) + wA, 0.0) * 2.0;
    }
    return max(du, corner);
}

// The radius the dash pattern walks a corner on. Every dash cut in a corner is a ray out of
// that arc's center, so all of them meet there. At the shape's own rounding that center is
// the border band's inner vertex whenever the band is as thick as the rounding, and the
// anti-aliasing blur around it paints a speck adrift in the gap. Running the pattern on a
// wider arc puts the center past the band's inner edge, so nothing that gets drawn sees it.
// The cap keeps the widened corner inside the shape; at a rounding already wider than the
// band this returns the rounding untouched.
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

// An ellipse's arc length is an incomplete elliptic integral of the second kind, with no closed
// form either way round, and dashing needs the map in both directions: the contour coordinate of
// the pixel's own nearest point, and the point sitting at each bounding dash edge's coordinate.
// So both ride in a table, 256 columns along one quadrant by 64 rows over the aspect ratio b/a,
// which symmetry extends to the whole ellipse. Each texel packs the two maps as 16 bit fractions,
// the inverse in RG and the forward in BA; see EllipseArc.cs for how they are built and why both
// axes are sqrt warped.
#define ARC_W 256.0
#define ARC_H 64.0

// One texel of the table, as the pair of fractions it packs.
float2 ArcTexel(float2 t) {
    float4 c = SampleArc((t + 0.5) / float2(ARC_W, ARC_H));
    return float2(floor(c.r * 255.0 + 0.5) * 256.0 + floor(c.g * 255.0 + 0.5),
                  floor(c.b * 255.0 + 0.5) * 256.0 + floor(c.a * 255.0 + 0.5)) / 65535.0;
}
// Bilinear over the table, by hand off point samples. Hardware filtering would blend the high
// and low byte of each value independently, which lands anywhere at all across the boundaries
// where the low byte wraps, and a texture format it can filter is not something every backend
// here is guaranteed to have.
float2 ArcLut(float col, float row) {
    float2 f = float2(saturate(col) * (ARC_W - 1.0), saturate(row) * (ARC_H - 1.0));
    float2 i0 = floor(f);
    float2 i1 = min(i0 + 1.0, float2(ARC_W - 1.0, ARC_H - 1.0));
    float2 w = f - i0;
    float2 top = lerp(ArcTexel(i0), ArcTexel(float2(i1.x, i0.y)), w.x);
    float2 bot = lerp(ArcTexel(float2(i0.x, i1.y)), ArcTexel(i1), w.x);
    return lerp(top, bot, w.y);
}

// A tip of the major axis is the ellipse's corner, and the pattern has to walk it the way it
// walks a rounded box's: every dash edge near a tip is very nearly a ray out of the tip's centre
// of curvature, b*b/a inside the outline, so as soon as the border is thicker than that they all
// meet inside the band and each one cuts across the far side of its own dash. See PatternRadius,
// which is the same problem and the same fix - run the pattern's tip on a wider arc, one whose
// centre clears the band, and the edges never meet anywhere that gets drawn.
// The fan reaches as far as the outline's own normal crosses the major axis at that same depth,
// which is where the two are tangent, so the pattern's edges stay exactly the outline's normals
// outside the fan and become rays out of the pivot inside it with no break at the junction. Each
// ray still meets the outline exactly at its own contour position - the pattern is placed on the
// outline, only the direction it cuts across the band is the fan's - so lengths are untouched and
// the pattern still tiles the perimeter.
// psi is the junction ray's angle, uj the arc length there, xp the pivot's distance from the
// centre. A tip no sharper than the band gives psi = 0 and no fan at all; a circle gives a
// quarter turn and the pivot at the centre, which is exactly what DrawCircle already draws.
void EllipseTipFan(float2 ab, float sq, float lineSize, out float psi, out float uj, out float xp) {
    // The fan's radius: the band's, so the pivot clears what gets drawn, but capped at a few
    // times the tip's own so the fan stays local to the tip. Past that cap the pivot sits so far
    // back that its rays meet the outline at a glance rather than across it, and a dash gets cut
    // along the band instead of across it - worst on a needle, whose normals stay near parallel
    // for a long stretch. Capped, a band thicker than that leaves the pivot inside the band,
    // which is the same compromise PatternRadius makes at its own cap.
    float rc = ab.y * ab.y / ab.x;
    float rho = max(rc, min(1.5 * lineSize, min(3.0 * rc, 0.95 * ab.y)));
    float sp = rho * ab.x / max(ab.y, 1e-30);
    float sin2 = saturate((sp * sp - ab.y * ab.y) / max(ab.x * ab.x - ab.y * ab.y, 1e-12));
    float st = sqrt(sin2);
    float ct = sqrt(1.0 - sin2);
    psi = atan2(ab.x * st, ab.y * ct);
    uj = sq * ArcLut(atan2(st, ct) * 0.63661977236758134, sqrt(ab.y / max(ab.x, 1e-30))).y;
    xp = (ab.x - ab.y * ab.y / ab.x) * ct;
}

// Room between p and the dash edge at contour position ue, positive on the side the pattern's own
// interval lies, so a min over the two bounding edges is the distance to the nearer of them. side
// is +1 for the edge behind p along the contour and -1 for the one ahead. Also returns where the
// band's centerline crosses the edge and how far along the edge that is, which are a rounded
// dash's cap center and the radius that reaches the band's two edges from it.
// The edge is straight - the outline's normal, or a ray out of the tip fan - but it is a SEGMENT,
// not a line, and that is the whole point. A line does not stay put behind a tip: every normal
// there passes close to the tip's centre of curvature, so carried far enough it comes back out
// through the band on the far side of the major axis and bites into the far end of its own dash.
// So the sides are read off the geometry only where the edge really spans, which is everywhere
// the boundary can be, and past its ends all that is left to give is the distance - the pattern's
// coordinate has already answered which side, since it put the pixel between these two edges to
// begin with.
// ab is the world radii with the major axis on x and sq is a quarter of the perimeter. The
// coordinate starts at the tip of the major axis and each quadrant is the tabulated one
// reflected, so the quadrant index picks the signs.
float EllipseEdgeRoom(float2 p, float ue, float side, float2 ab, float sq, float psi, float uj,
                      float xp, float lineSize, float aa, out float2 ctr, out float ctrR) {
    float per = 4.0 * sq;
    float s = ue - floor(ue / max(per, 1e-6)) * per;
    float k = min(floor(s / max(sq, 1e-6)), 3.0);
    float t = s - k * sq;
    float sx = (k < 0.5 || k > 2.5) ? 1.0 : -1.0;
    float sy = k < 1.5 ? 1.0 : -1.0;
    float dir = (k < 0.5 || (k > 1.5 && k < 2.5)) ? 1.0 : -1.0;
    // Arc from the tip of the major axis this quadrant runs off, which is what the fan spans.
    float tt = dir > 0.0 ? t : sq - t;

    float2 cs;
    sincos(ArcLut(sqrt(saturate(tt / max(sq, 1e-6))),
                  sqrt(ab.y / max(ab.x, 1e-30))).x * 1.5707963267948966, cs.y, cs.x);
    float2 onCurve = float2(sx * ab.x * cs.x, sy * ab.y * cs.y);

    // The speed along the parameter angle doubles as the length of the unnormalised normal.
    float speed = max(length(float2(ab.x * cs.y, ab.y * cs.x)), 1e-30);
    float2 inward = -float2(sx * ab.y * cs.x, sy * ab.x * cs.y) / speed;

    // A normal reaches the far side of the band at a band's depth, since depth along it IS
    // distance, plus the anti-aliasing that spills past the inner edge - miss that and the corner
    // where a dash edge meets the inner edge rounds off. A fan ray is not a normal on either
    // count: it leans, so distance along it runs ahead of depth, and behind a tip the band itself
    // reaches deeper than a band's worth, since the two sides close on the medial axis instead of
    // staying a band apart. So in the fan it runs to the pivot instead, which is where it stops
    // being this edge and where the band has ended in any case. Anything shorter ends the edge
    // partway across the band, and the deep part of a dash's edge loses its anti-aliasing.
    float2 dirIn = inward;
    float reach = lineSize + aa;
    if (psi > 1e-6 && tt < uj) {
        float2 v = float2(sx * xp, 0.0) - onCurve;
        float vl = max(length(v), 1e-30);
        dirIn = v / vl;
        reach = vl;
    }

    // Where the band's centerline crosses the edge, which along a leaning ray is farther along
    // than half a band by however much it leans. That same distance is how far the band's two
    // edges are from it along the ray, so it is also the radius a round cap needs to reach them:
    // a leaning cut crosses more band than a square one, so a disc of half a band's width would
    // fall short of both corners and notch the cap. Off the fan the ray is the normal, the two
    // coincide, and the cap is a plain half band disc as everywhere else.
    ctrR = lineSize * 0.5 / max(dot(dirIn, inward), 0.25);
    ctr = onCurve + dirIn * ctrR;

    // Outward the span reaches as far, to cover the anti-aliasing outside the outline. The edge's
    // own tangent points the way the contour coordinate grows.
    float2 rel = p - onCurve;
    float along = dot(rel, dirIn);
    // What the edge takes away is the half plane behind it, and only where the edge really spans;
    // past either end it takes away nothing. That region's complement is the intersection of two
    // half planes - behind the edge, and inside the span - so its exact distance is the standard
    // one for a quadrant, and taking it as one quantity is what makes this continuous. Reading
    // the two apart puts a seam along the span's end, since past the end that gives the distance
    // to the end while a step inside it still gives how far BEHIND the edge the pixel is, and
    // those two only agree in front of the edge.
    float e = max(along - reach, -(lineSize + aa) - along);
    float lat = side * dot(rel, float2(dirIn.y, -dirIn.x));
    return length(max(float2(e, lat), 0.0)) + min(max(e, lat), 0.0);
}

// One rounded dash's capsule around the band's centerline: the cut squared off across the band,
// which is the band and the cut whichever binds, unioned with a disc on the centerline at each
// end and clipped back to the band. See the rounded branch of the pixel shader, which builds the
// same thing for every other shape; the ellipse builds its own because behind a sharp tip both
// sides of the outline reach the same pixels and what is wanted there is the union of their two
// capsules, which needs both sides' ends at once.
float EllipseCapsule(float2 p, float d, float cut, float2 cA, float2 cB, float2 cR, float rd) {
    float2 r = max(cR, rd);
    return max(abs(d + rd) - rd,
               min(cut, min(length(p - cA) - r.x, length(p - cB) - r.y)));
}

// Dash cut for the ellipse, the world distance to the nearest dash edge, negative inside a dash.
// Every dash edge is a straight line - the outline's normal, or a ray out of the tip fan - so a
// dash keeps a straight edge front and back everywhere on the ellipse, tips included, the way
// every other closed outline does; see DashCutFromEdges, which is the same idea with the sides
// taken from the geometry instead.
// The side comes from the pixel's own contour coordinate, and the whole reason that works is that
// the coordinate does not fold: inside the fan it is where the pixel's own ray out of the pivot
// meets the outline, outside it the nearest point, and the two agree along the junction ray where
// they hand over. Read off the nearest point everywhere it would fold: behind a tip runs the
// medial axis, where the outline's two sides are equally near and the nearest point jumps from
// one to the other, and the pattern kinks with it. The fan keeps that stretch out of it as long as
// the pivot clears the band, which it does unless the tip is sharp enough that the cap on the fan's
// radius binds first - past that the seam is drawn, and the tail of this function is what makes it
// an edge rather than a step.
float EllipseDashCut(float2 p, float2 ab, float sq, float lineSize, float2 data, float aa,
                     bool roundCap, float sdf) {
    float2 capA = float2(0.0, 0.0);
    float2 capB = float2(0.0, 0.0);
    float2 capR = float2(0.0, 0.0);

    float2 q, r;
    float s = EllipseFold(p, ab, q, r);
    // Fold the signed inputs the same way, so the quadrant symmetry can be undone afterwards.
    float swap = ab.y > ab.x ? 1.0 : 0.0;
    float2 pw = swap > 0.5 ? p.yx : p;
    float2 abw = swap > 0.5 ? ab.yx : ab;
    if (s <= 0.0 || r.y <= 1e-7) return 1.0;

    float psi, uj, xp;
    EllipseTipFan(abw, sq, lineSize, psi, uj, xp);

    // Which tip's fan the pixel could be in is its own side of the minor axis, and both tips fold
    // together with the quadrant symmetry.
    float2 qa = abs(pw);
    float phi = atan2(qa.y, qa.x - xp);
    bool inFan = psi > 1e-6 && phi < psi;
    float footY = 0.0; // The nearest point's own distance off the major axis, for the fold below.
    float u1;
    if (inFan) {
        // The pixel's own edge through the fan is the ray out of the pivot, so its contour
        // position is where that ray meets the outline: a quadratic in how far along it that is.
        float2 v = qa - float2(xp, 0.0);
        float ea = v.x * v.x / (abw.x * abw.x) + v.y * v.y / (abw.y * abw.y);
        float eb = 2.0 * xp * v.x / (abw.x * abw.x);
        float ec = xp * xp / (abw.x * abw.x) - 1.0;
        float lam = (-eb + sqrt(max(eb * eb - 4.0 * ea * ec, 0.0))) / max(2.0 * ea, 1e-30);
        float2 hit = float2(xp + lam * v.x, lam * v.y);
        u1 = sq * ArcLut(atan2(hit.y / abw.y, hit.x / abw.x) * 0.63661977236758134, sqrt(r.y)).y;
    } else {
        // The angle of the nearest point, renormalised: the solve leaves whatever is left of its
        // error as a slip along the ellipse, and the angle is exactly what the table indexes by.
        float2 n = normalize(EllipseNearestPoint(q, r) / r);
        u1 = sq * ArcLut(atan2(n.y, n.x) * 0.63661977236758134, sqrt(r.y)).y;
        footY = n.y * abw.y;
    }

    // Arc length counts up through the first and third quadrants and back down through the
    // second and fourth, which is the same reflection EllipseEdgeRoom undoes.
    float u = pw.x >= 0.0 ? (pw.y >= 0.0 ? u1 : 4.0 * sq - u1)
                          : (pw.y >= 0.0 ? 2.0 * sq - u1 : 2.0 * sq + u1);

    // How much room to the nearer of the two bounding edges, in the same swapped frame the
    // coordinate was read in, which is a reflection and so leaves every distance alone - so the
    // cap centers stay in it too, and a rounded dash's capsule is built here without unswapping
    // anything.
    float3 de = DashEdges(u, data);
    float db = EllipseEdgeRoom(pw, de.y, 1.0, abw, sq, psi, uj, xp, lineSize, aa, capA, capR.x);
    float da = EllipseEdgeRoom(pw, de.z, -1.0, abw, sq, psi, uj, xp, lineSize, aa, capB, capR.y);

    float m = min(db, da);
    float cut = de.x >= 0.0 ? m : -m;

    // Behind a tip sharp enough that the fan cannot reach the whole band, the seam the fold leaves
    // is drawn, and it is a real boundary of the dash: the pattern partitions the perimeter, so
    // each side of the outline carries its own stretch of it and owns its own side of the medial
    // axis, and where one side lands on a dash and the other on a gap the dash simply stops there.
    // Everything above reads one side alone and walks past the seam without the field ever
    // reaching zero, which leaves that boundary with no anti-aliasing. Both sides' fields, each
    // cut off at the seam and unioned, is the boundary with its distance either side of it. It
    // comes out symmetric under the reflection that swaps the two sides, which is why it is
    // continuous across the seam at all.
    // The far side is the same field at the reflected contour position, since reflecting across
    // the major axis takes u to 4S - u, and the cost is only paid within a band of the axis. Past
    // that the seam's own distance dwarfs both cuts and the pair collapses back to the near side,
    // so nothing else on the ellipse moves - the medial axis is the segment inside the evolute's
    // cusps, which on a circle is the centre alone and never within a band of anything drawn.
    // A rounded dash is not partitioned that way: it is the capsule around the band's centerline,
    // and a capsule reaching in from one side simply runs past the axis and overlaps the one
    // coming the other way, so there is no boundary there to find and nothing to trim to. What it
    // wants is the plain UNION of the two sides' capsules - which is symmetric under the same
    // reflection, so it is continuous across the seam for the same reason, and it is a union of
    // whole capsules rather than of cuts, so no cap center ever has to switch sides mid field.
    float rd = lineSize * 0.5;
    // Only where the fold is real, on two counts. Inside the fan the coordinate is single valued
    // by construction, so there is no second side there to trim against or to union with, and
    // reaching for one anyway paints the mirrored dash over the tip. And the far side has to
    // actually REACH: its own foot is the near one mirrored, so it lies |d|^2 + 4|y|*Py away by
    // the cosine rule, which is |d| on the axis and climbs steeply off it. Distance to the seam
    // is not that test - a pixel a hair off the axis but out near the tip can sit a whole band
    // from the far side while the seam is right beside it.
    float ex = (abw.x * abw.x - abw.y * abw.y) / max(abw.x, 1e-30);
    float w = length(float2(max(abs(pw.x) - ex, 0.0), pw.y));
    float mirror = sqrt(sdf * sdf + 4.0 * abs(pw.y) * footY);
    if (!inFan && mirror < lineSize + aa) {
        float2 fcapA, fcapB, fcapR;
        float3 df = DashEdges(4.0 * sq - u, data);
        float fb = EllipseEdgeRoom(pw, df.y, 1.0, abw, sq, psi, uj, xp, lineSize, aa, fcapA, fcapR.x);
        float fa = EllipseEdgeRoom(pw, df.z, -1.0, abw, sq, psi, uj, xp, lineSize, aa, fcapB, fcapR.y);
        float mf = min(fb, fa);
        float far = df.x >= 0.0 ? mf : -mf;
        if (roundCap) {
            // The far capsule is measured in the far side's OWN band, whose depth here is the
            // distance to its foot, so it fades out by itself exactly as that side stops reaching
            // - no cliff where a gate would switch it off, and nothing left to tune. Bounded as
            // well by the evolute's cusp, past which the outline has no second normal at all and
            // the mirrored foot is just a point on the far side that happens to be near, which on
            // a blunt ellipse with a thick band runs the length of the tip. The cusp is exact on
            // the axis, which is where the second normal appears, and the reach above covers the
            // way off it.
            float cusp = abs(pw.x) - ex;
            return min(EllipseCapsule(pw, sdf, cut, capA, capB, capR, rd),
                       max(EllipseCapsule(pw, -mirror, far, fcapA, fcapB, fcapR, rd), cusp));
        }
        cut = min(max(cut, -w), max(far, w));
    }
    return roundCap ? EllipseCapsule(pw, sdf, cut, capA, capB, capR, rd) : cut;
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

// Abramowitz and Stegun 7.1.26. The error peaks at 1.5e-7, orders below an 8 bit alpha step,
// so the profile is exact for every purpose the coverage is put to here.
float Erf(float x) {
    float a = abs(x);
    float t = 1.0 / (1.0 + 0.3275911 * a);
    float y = 1.0 - (((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t) * exp(-a * a);
    return x < 0.0 ? -y : y;
}
// Coverage of a half plane at signed distance d under a Gaussian of width sigma. This is the
// exact blur of a straight edge, so it holds wherever the contour is locally straight; a corner
// tighter than sigma reads slightly too solid, since a convolution there depends on how much
// shape is nearby rather than on the distance alone.
float GaussianCoverage(float d, float sigma) {
    return 0.5 * (1.0 - Erf(d / (sigma * 1.414213562373095)));
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
    float dashType = DecodeDigit(meta, 4.0);
    // Set when Meta1.y carries a world space blur instead of a screen space AA width.
    float blurred = meta;

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
    float2 dashCapR = float2(0.0, 0.0); // Each cap's radius, when a shape needs one wider than
                                        // half the band; below the band's own half wins.
    bool dashCapDone = false; // Set when the cut already IS the rounded capsule, see below.
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
        // Meta2.z is the far end's half width. Equal to sdfSize the line is a capsule, which is
        // the rounding slot's own job, so that stays exactly the shape it always was; different
        // and the two end circles' hull takes over, and the radius is already inside the field.
        if (p.Meta2.z == sdfSize) {
            d = SegmentSDF(q, float2(0.0, 0.0), float2(p.Meta1.w, 0.0));
        } else {
            d = StrokeConeSDF(q, p.Meta1.w, sdfSize, p.Meta2.z, float4(0.0, 0.0, 0.0, 0.0));
            rounded = 0.0;
        }
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
        float2 ab = float2(sdfSize, p.Meta1.w);
        d = EllipseSDF(q, ab);
        if (dashType >= 0.5) {
            // Meta2 is entirely spare on an ellipse, so the pattern and the quarter perimeter
            // travel as plain floats with nothing packed. The dash anti-aliasing width goes along
            // because a dash edge has to reach past the band's inner edge by that much.
            // A rounded ellipse comes back with its capsule already built; see EllipseCapsule.
            dashCut = EllipseDashCut(q, ab, p.Meta2.z, lineSize, p.Meta2.xy,
                                     footprint.y * aaPixels, dashType >= 1.5, d);
            dashCapDone = true;
        }
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
            // (1024 = 0, meaning caps and collinear joints), freeing Meta2.z and Meta2.w. The
            // rounding slot carries the packed fraction and phase; paths never use it for
            // rounding. The bevel plane directions derive from the turn angles, so nothing else
            // needs to travel. Each end's fillet radius rides above the end modes in Meta2.x as
            // a 7 bit fraction of that end's own stroke radius over [1, 2], and a flag bit above
            // those says what Meta2.z holds; that keeps the packed value under 2^21, well inside
            // what a ps_3_0 interpolator carries exactly, unlike the two 11 bit codes which
            // already sit at the ceiling.
            // A uniform path puts the segment's start length there and takes its far radius off
            // sdfSize. A tapering one needs the slot for that radius, so the pattern's phase
            // arrives already slid back by the start instead and the contour coordinate is the
            // segment's own. Only the taper pays for that, which is a phase rounded to one part
            // in 2047 of a period rather than a length carried exactly.
            float ma = sd.y;
            float thB = (DecodeDigit(ma, 2048.0) - 1024.0) / 1023.0 * 3.1415926536;
            float thA = (ma - 1024.0) / 1023.0 * 3.1415926536;
            float mr = sd.x;
            float modeBits = DecodeDigit(mr, 64.0);
            float frCodeA = DecodeDigit(mr, 128.0);
            float frCodeB = DecodeDigit(mr, 128.0);
            float tapered = mr;
            float rEnd = tapered >= 0.5 ? sd.z : sdfSize;
            float startLen = tapered >= 0.5 ? 0.0 : sd.z;
            // Each end's fillet is a multiple of the width there, so the two quads at a joint
            // read it off the same radius and walk the corner to the same arc length.
            float2 fr = float2(sdfSize, rEnd) * (1.0 + float2(frCodeA, frCodeB) / 127.0);
            float2 hA;
            sincos(thA * 0.5, hA.y, hA.x);
            float2 hB;
            sincos(thB * 0.5, hB.y, hB.x);
            float sA = sign(thA);
            float sB = sign(thB);
            sd = float4(modeBits, atan2(-sA * hA.x, -sA * hA.y), atan2(-sB * hB.x, sB * hB.y), rEnd);
            pathCut = PathDashCut(q, p.Meta1.w, sdfSize, rEnd, fr, startLen, thA, thB, float2(p.Meta2.w, rounded), dashType);
            rounded = 0.0;
            dashType = 0.0;
        }
        // Solid paths carry the far end's half width in Meta2.w so a stroke can taper. It
        // equals sdfSize on a uniform path, which takes the plain capsule and keeps those
        // pixels exactly as they were.
        d = max(sd.w == sdfSize
                ? StrokeSDF(q, p.Meta1.w, sdfSize, sd)
                : StrokeConeSDF(q, p.Meta1.w, sdfSize, sd.w, sd), pathCut);
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

    // Hoisted above the blur branch on purpose: this is the last derivative the shader takes,
    // and taking it inside conditional flow is what the ANGLE gradient bug punishes.
    float pixelWidth = PixelWidth(d, footprint);

    if (blurred >= 0.5) {
        // A blur is a world space Gaussian, so unlike the AA fade it reaches equally to both
        // sides of the edge and the shape keeps its size instead of growing outward. The floor
        // catches a blur that falls under a pixel, whether it was authored that way or zoomed
        // out until it did: at half a pixel the same profile is already a better antialiaser
        // than the fade below, so no separate AA term rides along.
        float sigma = max(p.Meta1.y, 0.5 * pixelWidth);
        // Three sigma is where the tail drops under half of an 8 bit alpha step. The quad is
        // built to exactly this reach, so the outer test only trims the corners it cannot cover.
        // A border is a band rather than a fill, so it has an inner tail to leave behind too,
        // which is what keeps a large ring from shading the whole hole it encloses.
        float reach = 3.0 * sigma;
        if (d >= reach || (lineSize > 0.0 && d <= -lineSize - reach)) {
            discard;
        }
        // Flat color by construction, so it factors out of the convolution and the whole blur
        // collapses to coverage. That is what makes this exact rather than an approximation of
        // one, and it is why the gradient machinery below is skipped rather than adapted.
        float coverage = GaussianCoverage(d, sigma);
        if (lineSize > 0.0) {
            // The band is the shape minus the same shape offset inward by the thickness, so its
            // blur is the difference of the two half plane profiles that bound it. Past a
            // thickness of a few sigma the inner term vanishes and this returns to a fill.
            coverage -= GaussianCoverage(d + lineSize, sigma);
        }
        float4 bc = UnpackColor(p.Fill.xy);
        bc.a *= coverage;
        float4 br = ToRgb(bc, space);
        br.rgb *= br.a;
        br *= clipAlpha;
        br.rgb += (DitherNoise(p.Pos.xy) - 0.5) * dither_scale;
        return br;
    }

    float aaSize = pixelWidth * aaPixels;

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
    // distance and its screen derivative would misfire across the wrap seam. On every shape
    // with corners the cut is the distance to the dash edge itself rather than a contour offset
    // rescaled by how fast the coordinate runs here, so it stays a true distance right through
    // the corners, where the two differ most: the band's inner side runs on a tighter arc than
    // its outer side, and the rescale factor jumps where a run meets a corner. See
    // DashCutFromEdges, and EllipseDashCut, which measures to the edges for a reason of its own.
    if (dashType >= 0.5 && !dashStroke) {
        float aaU = footprint.y * aaPixels;
        if (dashType >= 1.5) {
            // Round capped dashes: the exact capsule around the band's centerline, the way
            // PathDashCut builds one. It is the dash cut square across the band, which is the
            // band and the cut whichever binds, unioned with a disc on the centerline at each
            // end. Measuring the ends on the centerline is what keeps the caps circular right
            // across the band, at corners as much as anywhere.
            // Written as one union clipped back to the band, rather than as the band inside the
            // dash's span and the discs outside it, because those two only agree along the span's
            // own boundary where the dash edge crosses the centerline SQUARELY: the disc is round,
            // so its distance grows at the band's rate only across a square cut. It does wherever
            // an edge is a normal, which is everywhere but an ellipse's tip fan, and there the
            // fan's ray leans and the two branches would part company mid band and notch the cap.
            // A union cannot part company with itself, and the cap radius carries the lean, so the
            // disc reaches the band's edges. Clipping to the band is what keeps a wider cap from
            // spilling past the inner edge; everywhere else the cap is half a band and the clip
            // does nothing.
            // The ellipse has already done all of this to itself, since behind a sharp tip it
            // needs the union of the capsules coming in from both sides of the outline.
            float rd = lineSize * 0.5;
            float2 cr = max(dashCapR, rd);
            float capD = dashCapDone ? dashCut
                                     : max(abs(d + rd) - rd,
                                           min(dashCut, min(length(q - dashCapA) - cr.x,
                                                            length(q - dashCapB) - cr.y)));
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
        // is below 1, so the fade still lands on the border alone there.
        result = lerp(fr, br, borderMix) * edgeFade;
    }

    result *= clipAlpha;
    // With premultiplied blending the source color adds straight into the framebuffer, so
    // offsetting rgb here dithers the post-blend value that actually quantizes to 8 bits,
    // covering banding from color and alpha gradients alike. Left unclamped on purpose:
    // the negative half must survive to dither near-black, and the target clamps on write.
    result.rgb += (DitherNoise(p.Pos.xy) - 0.5) * dither_scale;
    return result;
}

technique SpriteBatch {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL SpritePixelShader();
    }
}
