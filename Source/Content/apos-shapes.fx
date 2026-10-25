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
float2 ramp_texel; // 1 / the ramp table's texel counts: x across a row, y across the rows.
float2 band_tex_size; // (width, rows) of the glyph band texture in texels.
float2 band_texel; // 1 / band_tex_size.
float2 curve_texel; // 1 / the glyph curve texture's texel counts.
// The clips everything in the batch sits inside, innermost last. Each is a rounded rectangle:
// frame is its center and the cosine and sine of its rotation, size its half extents, corner
// radius and antialiasing width in pixels.
float4 clip_frame[4];
float4 clip_size[4];
float clip_count;
// The batch hands every texture over by name, and every technique reads all five in register
// order before anything else (see the top of ShapePixel), so each backend puts them on the same
// units in every technique.
#if SM6
// DXC drops the legacy sampler syntax: declare texture/sampler pairs on matching
// registers so the Vulkan reflection treats them as combined image-samplers.
Texture2D TextureTex : register(t0); SamplerState TextureSampler : register(s0);
Texture2D BandTex : register(t1); SamplerState BandSampler : register(s1); // Glyph band headers and curve lists, bound with clamped point sampling.
Texture2D CurveTex : register(t2); SamplerState CurveSampler : register(s2); // Glyph curve control points, bound with clamped point sampling.
Texture2D BlueNoiseTex : register(t3); SamplerState BlueNoiseSampler : register(s3); // 64x64 tile, bound with wrapped point sampling.
Texture2D RampTex : register(t4); SamplerState RampSampler : register(s4); // Ramp weight curves, bound with clamped point sampling.
float4 SampleTexture(float2 uv) { return TextureTex.Sample(TextureSampler, uv); }
float4 SampleBand(float2 uv) { return BandTex.Sample(BandSampler, uv); }
float4 SampleCurve(float2 uv) { return CurveTex.Sample(CurveSampler, uv); }
float4 SampleBlueNoise(float2 uv) { return BlueNoiseTex.Sample(BlueNoiseSampler, uv); }
float4 SampleRamp(float2 uv) { return RampTex.Sample(RampSampler, uv); }
#else
// Each sampler names its texture, so the batch hands textures over by name and every technique
// finds them on whatever units its own shader was given. Only the texture mask leaves its
// sampling to the batch, which sets it from Begin.
texture TextureTex;
texture BandTex;
texture CurveTex;
texture BlueNoiseTex;
texture RampTex;
sampler TextureSampler : register(s0) = sampler_state { Texture = <TextureTex>; };
// Glyph band headers and curve lists, and the curve control points, read texel by texel as data.
sampler BandSampler : register(s1) = sampler_state { Texture = <BandTex>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
sampler CurveSampler : register(s2) = sampler_state { Texture = <CurveTex>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
sampler BlueNoiseSampler : register(s3) = sampler_state { Texture = <BlueNoiseTex>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Wrap; AddressV = Wrap; };
sampler RampSampler : register(s4) = sampler_state { Texture = <RampTex>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
float4 SampleTexture(float2 uv) { return tex2D(TextureSampler, uv); }
float4 SampleBand(float2 uv) { return tex2D(BandSampler, uv); }
float4 SampleCurve(float2 uv) { return tex2D(CurveSampler, uv); }
float4 SampleBlueNoise(float2 uv) { return tex2D(BlueNoiseSampler, uv); }
float4 SampleRamp(float2 uv) { return tex2D(RampSampler, uv); }
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
};
// A glyph quad reads its fill gradient exactly like every other shape, so FillCoord means what
// it always means and the glyph's own data takes the channels a glyph has no use for:
// TexCoord.xy is the corner in em units, Meta2 is (band texel base, band count, pixels per em x,
// pixels per em y) - four dash channels no glyph dashes with - and BorderCoord is the em to band
// index transform, scale in xy and offset in zw, since a glyph has no border band to place a
// second gradient on. TexCoord.z, the corner rounding, stays 0 because the tail takes it off
// the distance for every shape alike. The band texel base is an integer that reaches six
// figures, so it keeps a channel to itself rather than sharing one - see the note on the packed
// meta in ShapePixel for the budget an interpolator carries exactly.
//
// An SVG element's quad is a glyph quad in every one of those channels. All that sets it apart
// is its shape id, which picks the even-odd fill rule at the end of the same arm.
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
    float2 Pos : TEXCOORD8; // World position.
};

// The distance functions below that aren't derived in place come from Inigo Quilez (MIT),
// https://iquilezles.org/articles/distfunctions2d/. See THIRD_PARTY_NOTICES.md.
float CircleSDF(float2 p, float r) {
    return length(p) - r;
}
float RoundBoxSDF(float2 p, float2 b, float4 r) {
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.y  : r.x;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}
// Box with half size b whose corners are cut straight across, each by r measured along both of
// its edges from the corner. The radii arrive in the order RoundBoxSDF takes its own, and the
// corner the point sits over picks one the same way.
//
// Folding onto q.x >= q.y halves what is left. The corner's own region is an intersection of
// three half planes - the two edges and the cut - and it is symmetric across that diagonal
// whatever b is, so one octant answers for both. Which of the three is nearest is then two
// compares: the edge, the cut, or the vertex where they meet, exact inside and out.
//
// One corner alone is exact OUTSIDE, since a point in a quadrant has its nearest point in that
// quadrant: clamping any candidate back into the quadrant keeps it inside the shape and never
// moves it further off. Inside it is not, because a neighbour's cut can run nearer than
// anything this corner has, and a shallow cut beside a deep one leaves a step along the axis
// between them - right where a border band's inner edge lands. Inside a convex polygon the
// distance to the boundary is the smallest distance to any edge's LINE, so folding the other
// three cuts back in as plain half planes settles it. Outside they are supporting lines, which
// can never beat the true distance, so the same max leaves that case alone.
float ChamferBoxSDF(float2 p, float2 b, float4 r) {
    float2 rx = (p.x > 0.0) ? r.xy : r.zw;
    float cut = (p.y > 0.0) ? rx.y : rx.x;

    float2 q = abs(p) - b;
    q = (q.y > q.x) ? q.yx : q.xy;
    q.y += cut;
    float d;
    if (q.y < 0.0 && q.y - q.x * 0.41421356237 < 0.0) { // 1 - sqrt(2): the vertex's own plane.
        d = q.x;
    } else if (q.x < q.y) {
        d = (q.x + q.y) * 0.70710678119;
    } else {
        d = length(q);
    }

    // The four cuts as half planes, each at 45 degrees, so all of them read off one sum and one
    // difference. Scaled by the shared 1 / sqrt(2) once the max has picked one.
    float4 e = float4(p.x - p.y, p.x + p.y, -p.x - p.y, -p.x + p.y) - (b.x + b.y) + r;
    return max(d, max(max(e.x, e.y), max(e.z, e.w)) * 0.70710678119);
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

// Signed distance to an origin centred, axis aligned ellipse with radii ab. Hands back the
// folded frame and the nearest point in it as well, because dashing needs exactly those and
// the solve that produces them is the most expensive thing on this shape: eight Newton steps,
// each with two divides and a root. Solving once and spending the answer twice is what keeps
// a dashed ellipse from paying for it all over again.
// nearest is assigned before any early exit so it is live on every path, degenerate ones
// included, and never leaves a caller reading an uninitialised value.
float EllipseSDF(float2 p, float2 ab, out float2 q, out float2 r, out float2 nearest, out float s) {
    s = EllipseFold(p, ab, q, r);
    nearest = q;
    if (s <= 0.0) return length(p);

    // A collapsed minor axis degenerates to a segment, which the solve cannot represent: its
    // bracket needs both denominators strictly positive.
    if (r.y <= 1e-7) {
        return length(float2(q.x - clamp(q.x, 0.0, r.x), q.y)) * s;
    }

    nearest = EllipseNearestPoint(q, r);
    float ix = q.x / r.x;
    float iy = q.y / r.y;
    float sgn = ix * ix + iy * iy - 1.0 < 0.0 ? -1.0 : 1.0;
    return length(nearest - q) * s * sgn;
}
float ArcSDF(float2 p, float2 sc, float ra, float rb) {
    p.x = abs(p.x);
    return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
}
// hw is the band's half thickness, measured from the centerline out, exactly as an arc's
// rb is. The two shapes only differ in how they end.
float RingSDF(float2 p, float2 n, float r, float hw) {
    p.x = abs(p.x);
    p = mul(p, float2x2(n.x, n.y, -n.y, n.x));
    float band = abs(length(p) - r) - hw;
    float cut = length(float2(p.x, max(0.0, abs(r - p.y) - hw))) * sign(p.x);
    // A full turn arrives with its sine pinned to zero and has no caps to cut against. It
    // folds along p.x = 0, and there the cut is exactly 0 whatever its sign, which reads as
    // the shape's own boundary: a half covered hairline straight across the band. Nothing
    // showed while the fade sat outside the edge, since a distance of 0 painted solid there.
    return max(band, n.y > 0.0 ? cut : -1e6);
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
// would jump where the pattern midpoint flips between them. How long one dash runs comes
// along in w, since it is already unpacked here and a round cap's own end needs it.
float4 DashEdges(float u, float2 data) {
    float m = data.y;
    float ph = DecodeDigit(m, 2048.0) / 2047.0;
    float h = m / 2047.0 * 0.5;
    float t = frac(u / data.x - ph + 0.5) - 0.5;
    float db = min(frac(t - h), frac(t + h));
    float da = min(frac(h - t), frac(-h - t));
    return float4((abs(t) - h) * data.x, u - db * data.x, u + da * data.x, 2.0 * h * data.x);
}

// A square corner at the origin, inside where x and y are both negative, cut straight across c
// back along both edges. It is symmetric across y = x, so one cut end and one edge remain.
float CutCornerSDF(float2 q, float c) {
    q = q.x >= q.y ? q : q.yx;
    float2 v = q + float2(0.0, c);
    float across = (v.x + v.y) * 0.70710678;
    float d = max(q.x, across);
    if (d <= 0.0 || v.y <= 0.0) return d;
    return v.y >= v.x ? across : length(v);
}

// A dashed outline arrives cut into pieces at its corners, and each piece is one corner of the
// border band or a straight run of it. A dashed path arrives as half segments, each with the
// joint or cap at its end. Either way the pattern turns the piece's corner in a frame with the
// corner at the origin, the incoming edge's outward normal on x and the direction of travel on
// y, so the outgoing edge's normal sits tau further round. A straight run is the same thing with
// tau = 0. The pattern turns the corner on a circle of its own around piv, of radius rc where it
// crosses the band's centerline. For butt caps that circle runs wider than the corner itself:
// every cut through the fan is a ray out of its center, and the wider circle puts that center
// past the band's inner edge, where the rays meeting is never drawn.
//
// A half segment lies along the incoming edge, and the joint at the segment's far end turns the
// pattern again. far is how long the straight run between the two circles is, the far circle's
// radius and turn, and which side of x it sits on. A piece of an outline has no far end.
//
// The centerline point at pattern position v, counted from where the fan starts, and the
// direction the pattern runs there, which is the normal of the dash edge through it.
float2 PieceEdge(float v, float arc, float rc, float2 piv, float2 n2, float4 far, out float2 n) {
    float2 t2 = float2(-n2.y, n2.x);
    if (v > arc) {
        n = t2;
        return piv + n2 * rc + t2 * (v - arc);
    }
    float2 cs;
    if (v >= 0.0) {
        sincos(v / max(rc, 1e-4), cs.y, cs.x);
        n = float2(-cs.y, cs.x);
        return piv + cs * rc;
    }
    float2 back = piv + float2(rc, v);
    n = float2(0.0, 1.0);
    if (v >= -far.x) return back;
    // Back around the far circle, then on along the segment before it.
    float psi = (-far.x - v) / max(far.y, 1e-4);
    float2 center = piv + float2(rc + far.w * far.y, -far.x);
    sincos(min(psi, far.z), cs.y, cs.x);
    n = float2(-far.w * cs.y, cs.x);
    return center - far.y * float2(far.w * cs.x, cs.y) + n * min(v + far.x + far.z * far.y, 0.0);
}

// Half the band's width at pattern position v: a taper runs between the two circles, and its
// wall leans by the slope, which stands it 1 / c.w further out than the radius.
float PieceRadius(float v, float4 c) {
    return lerp(c.x, c.y, saturate(-v / c.z)) * c.w;
}

// Distance to the segment that leaves a along the unit direction d and runs len.
float SegRun(float2 q, float2 a, float2 d, float len) {
    float2 f = q - a;
    return length(f - d * clamp(dot(f, d), 0.0, len));
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

// Puts c into the frame a → b, which every gradient that reads an angle works in. mag is
// that frame's own length, |b - a|, which the caller already needs for the gradient scale,
// so it is passed in rather than rooted a second time here.
float2 Rotate(float2 a, float2 b, float2 c, float mag) {
    float ux = b.x - a.x;
    float uy = b.y - a.y;
    float vx = -c.x + a.x;
    float vy = c.y - a.y;

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

// Every gradient below is a fraction of the frame a → b, so each takes that frame's length
// as gl rather than measuring it again: the caller needs it in any case and it is the only
// square root most of them would otherwise pay for. The six that read an angle take the
// rotated point rc in place of c, for the same reason.
float RadialGradient(float2 a, float2 c, float gl) {
    return length(c - a) / gl;
}
// Signed offset along the frame, as a fraction of its length. This was built as the distance
// to the perpendicular through a, but that perpendicular was raised on a NORMALIZED direction,
// so its own length was exactly 1 and the root that divided it back out, along with the
// normalize and the two pows feeding it, were all undoing each other. A plain projection is
// what was left, and it is exact rather than an approximation of the old form.
float LinearGradient(float2 a, float2 b, float2 c, float gl) {
    return dot(b - a, c - a) / (gl * gl);
}
float BilinearGradient(float2 a, float2 b, float2 c, float gl) {
    return abs(dot(b - a, c - a)) / (gl * gl);
}
float ConicalGradient(float2 rc) {
    return abs(atan2(-rc.y, -rc.x) / 3.14159265);
}
float ConicalAsymGradient(float2 rc) {
    return atan2(rc.y, rc.x) / 6.283185307179586 + 0.5;
}
float SquareGradient(float2 rc, float gl) {
    return max(abs(rc.x), abs(rc.y)) / gl;
}
float CrossGradient(float2 rc, float gl) {
    return min(abs(rc.x), abs(rc.y)) / gl;
}
// Magnitude of the spiral gradient per world unit. The radial term winds once
// per gradient length, the angular term once per turn, and they are orthogonal
// so the root sum keeps the smoothed seam aaSize wide at any radius.
float SpiralGradientSize(float2 a, float2 c, float gl) {
    float r = 6.283185307179586 * length(c - a);
    return sqrt(1.0 / (gl * gl) + 1.0 / max(r * r, 1e-12));
}
// The two spirals are one gradient wound either way, so dir carries the sign rather than
// the pair being written out twice.
float SpiralGradient(float2 rc, float gl, float dir) {
    return SawtoothWave(dir * atan2(-rc.y, -rc.x) / 6.283185307179586 + length(rc) / gl);
}
float ShapeGradient(float a, float b, float c) {
    return (c - a) / (b - a);
}

// pal marks a cosine palette reading the result. The wrap smoothing below stands in for the
// box filter of a color that is linear in the gradient value, which two stops are and a
// palette is not: pulling toward 0.5 paints the palette's midpoint color along the seam.
// A palette's whole number frequencies make it periodic across the wrap instead, so for one
// the seam is already continuous and the smoothing is what would draw a line there. A ramp
// skips it the same way and filters its own jumps instead, seam included, so it needs two
// things the smoothing never did: tAa, the AA band's width measured in gradient value at
// this pixel, and wrapF, set when the value wraps 1 back to 0 so RampValue can run the row
// periodic. An offset moves the wrap seam away from the row's ends, so it clears the flag;
// the seam is then aliased, exactly as an offset palette's already is.
float Gradient(float2 type, float4 posAB, float2 c, float d, float aaSize, float2 offset, bool pal, bool rampd, out float tAa, out float wrapF) {
    float result;
    tAa = 0.0;
    wrapF = 0.0;
    if (type.x < 0.5) {
        result = 1.0;
    } else {
        // The frame's length is what every gradient here scales by and what the sawtooth wrap
        // measures against, so it is rooted once for all of them. The rotation is only the six
        // angular gradients' business, so only they pay for it, and it reuses that same length
        // instead of rooting the same vector a second time.
        float gl = length(posAB.zw - posAB.xy);
        float2 rc = type.x >= 3.5 && type.x < 9.5 ? Rotate(posAB.xy, posAB.zw, c, gl) : c;

        float grad;
        if (type.x < 1.5) {
            grad = RadialGradient(posAB.xy, c, gl);
            tAa = aaSize / gl;
        } else if (type.x < 2.5) {
            grad = LinearGradient(posAB.xy, posAB.zw, c, gl);
            tAa = aaSize / gl;
        } else if (type.x < 3.5) {
            grad = BilinearGradient(posAB.xy, posAB.zw, c, gl);
            tAa = aaSize / gl;
        } else if (type.x < 4.5) {
            grad = ConicalGradient(rc);
            tAa = aaSize / (3.14159265 * max(length(posAB.xy - c.xy), 1e-6));
        } else if (type.x < 5.5) {
            grad = ConicalAsymGradient(rc);
            tAa = aaSize / (6.283185307179586 * max(length(posAB.xy - c.xy), 1e-6));
            wrapF = 1.0;
            if (!pal && !rampd) {
                grad = SmoothWrapDiscontinuity(grad, aaSize / (6.283185307179586 * length(posAB.xy - c.xy)));
            }
        } else if (type.x < 6.5) {
            grad = SquareGradient(rc, gl);
            tAa = aaSize / gl;
        } else if (type.x < 7.5) {
            grad = CrossGradient(rc, gl);
            tAa = aaSize / gl;
        } else if (type.x < 9.5) {
            grad = SpiralGradient(rc, gl, type.x < 8.5 ? 1.0 : -1.0);
            tAa = aaSize * SpiralGradientSize(posAB.xy, c, gl);
            wrapF = 1.0;
            if (!pal && !rampd) {
                grad = SmoothWrapDiscontinuity(grad, tAa);
            }
        } else if (type.x < 10.5) {
            grad = ShapeGradient(posAB.x, posAB.y, d);
            tAa = aaSize / max(abs(posAB.y - posAB.x), 1e-6);
        }

        if (type.y < 0.5) {
        } else if (type.y < 1.5) {
            grad = SawtoothWave(grad);
            wrapF = 1.0;
            if (!pal && !rampd) {
                grad = SmoothWrapDiscontinuity(grad, aaSize / gl);
            }
        } else if (type.y < 2.5) {
            grad = TriangularWave(grad);
            wrapF = 0.0;
        } else if (type.y < 3.5) {
            // Chain rule before the wave replaces the value: where the sine flattens, the
            // value moves slowly through a stop and the filter rightly narrows.
            tAa *= abs(cos(grad * 3.14159265 - 1.5)) * 1.5707963268;
            grad = SineWave(grad);
            wrapF = 0.0;
        }
        grad = RemapOffset(grad, offset);
        tAa /= max(abs((1.0 - offset.y) - offset.x), 1e-6);
        if (offset.x != 0.0 || offset.y != 0.0) {
            wrapF = 0.0;
        }

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

// One logical texel of the ramp table is two physical texels. The first is the curve's
// value at the texel's two edges, unorm16 pairs split across bytes like the arc table's
// values and for the same reason: hardware filtering would blend the bytes independently,
// so the shader reads them apart by hand. The second is the curve's running integral at
// the texel's start, a 24 bit fraction of the whole row.
float2 RampSeg(float row, float col) {
    float4 c = SampleRamp(float2((col * 2.0 + 0.5) * ramp_texel.x, (row + 0.5) * ramp_texel.y));
    return float2(floor(c.g * 255.0 + 0.5) * 256.0 + floor(c.r * 255.0 + 0.5),
                  floor(c.a * 255.0 + 0.5) * 256.0 + floor(c.b * 255.0 + 0.5)) / 65535.0;
}
// The integral from the row's start to col + u, in value times texel units: the stored
// prefix plus the segment's own trapezoid. The prefix was accumulated from the same
// quantized edge values the trapezoid reads, so the two agree exactly.
float RampInt(float row, float col, float2 seg, float u) {
    float4 c = SampleRamp(float2((col * 2.0 + 1.5) * ramp_texel.x, (row + 0.5) * ramp_texel.y));
    float f = (floor(c.r * 255.0 + 0.5) + floor(c.g * 255.0 + 0.5) * 256.0 + floor(c.b * 255.0 + 0.5) * 65536.0) * (256.0 / 16777215.0);
    return f + (seg.x + (seg.y - seg.x) * 0.5 * u) * u;
}
// The ramp curve box filtered over the AA band: x = va, y = vb, the curve's values at the
// window's two edges; w = m, the window's exact mean; z = the blend that reproduces m from
// va and vb, or -1 when no such blend exists. The window is aaT wide in gradient value
// with no cap, so a hard stop antialiases over the same band a shape edge does however
// short the gradient runs. Two stops read m outright, since their color is linear in the
// value. A palette blends the colors AT va and vb by z instead - the mean position would
// paint whatever color the palette keeps between the two sides along the edge, the trap
// the wrap smoothing in Gradient falls into on a palette - which is exact wherever the
// window holds one jump between two flat runs, the shape every band edge has. Everything
// here is a continuous function of x: an earlier filter that picked the nearest texel
// boundary turned the one ulp of noise in a GPU's t into a ragged line of flipped pixels
// along every hard stop, because axis aligned pixel centers land exactly on that tie.
// wrapped runs the row periodic for gradients whose value wraps, which carries a hard stop
// cleanly across a sawtooth seam.
float4 RampBox(float t, float row, float aaT, bool wrapped) {
    float h = 0.5 * clamp(aaT * 256.0, 1e-5, 256.0);
    float a = t * 256.0 - h;
    float b = t * 256.0 + h;
    if (!wrapped) {
        // The window truncates at the row's ends rather than reaching past them.
        a = clamp(a, 0.0, 256.0);
        b = clamp(b, 0.0, 256.0);
    }
    float fa = floor(a);
    float fb = floor(b);
    float ja = wrapped ? Mod(fa, 256.0) : min(fa, 255.0);
    float jb = wrapped ? Mod(fb, 256.0) : min(fb, 255.0);
    // A clamped window whose top edge sits exactly on 256 is inside texel 255, not past a
    // boundary: floor would call it one, the split formula below would then hand the right
    // piece a full texel's width inside a sub texel window, and the inflated mean paints a
    // bright line down the last half pixel of every clamped gradient. Wrapped keeps floor,
    // since there the seam is a real boundary.
    float fbe = wrapped ? fb : min(fb, 255.0);
    float2 sa = RampSeg(row, ja);
    float2 sb = RampSeg(row, jb);
    float ua = a - (wrapped ? fa : ja);
    float ub = b - (wrapped ? fb : jb);
    float va = lerp(sa.x, sa.y, ua);
    float vb = lerp(sb.x, sb.y, ub);

    float w = max(b - a, 1e-5);
    float m;
    if (b - a <= 1.0) {
        // At most one boundary inside the window: the mean reads straight off the segments,
        // exact at any magnification, with none of the quantization the stored prefix has.
        m = fbe - fa < 0.5
            ? (va + vb) * 0.5
            : ((1.0 - ua) * lerp(sa.x, sa.y, (1.0 + ua) * 0.5) + ub * lerp(sb.x, sb.y, ub * 0.5)) / w;
    } else {
        float intA = RampInt(row, ja, sa, ua);
        float intB = RampInt(row, jb, sb, ub);
        if (wrapped) {
            // Each full revolution between the window's edges adds the whole row's integral.
            float2 last = RampSeg(row, 255.0);
            intB += (floor(b / 256.0) - floor(a / 256.0)) * RampInt(row, 255.0, last, 1.0);
        }
        m = (intB - intA) / w;
    }

    float dv = vb - va;
    float al = (vb - m) / (abs(dv) > 1e-3 ? dv : 1e30);
    if (al < 0.0 || al > 1.0 || abs(dv) <= 1e-3) {
        al = -1.0;
    }
    return float4(va, vb, al, m);
}

// A color ramp is RampBox rebuilt for colors. One logical texel is two physical texels
// holding the color at the texel's two edges as straight RGBA8, one-sided limits like the
// scalar row's, so a hard stop sits exactly between two texels. A companion row keeps each
// channel's running integral at the texel's start as unorm16 pairs, [R G] then [B A], read
// apart by hand for the same reason as everywhere else. The row is in the batch's remapped
// frame, except that Oklch bakes into Oklab's axes so none of the filters below average a
// hue angle across the wheel (see Bake in ColorRamp.cs), which is why the call sites read
// what comes back through Oklab.
float4 LutTexel(float row, float col, float side) {
    return SampleRamp(float2((col * 2.0 + 0.5 + side) * ramp_texel.x, (row + 0.5) * ramp_texel.y));
}
float4 LutPrefix(float introw, float col) {
    float4 t0 = SampleRamp(float2((col * 2.0 + 0.5) * ramp_texel.x, (introw + 0.5) * ramp_texel.y));
    float4 t1 = SampleRamp(float2((col * 2.0 + 1.5) * ramp_texel.x, (introw + 0.5) * ramp_texel.y));
    return float4(
        floor(t0.g * 255.0 + 0.5) * 256.0 + floor(t0.r * 255.0 + 0.5),
        floor(t0.a * 255.0 + 0.5) * 256.0 + floor(t0.b * 255.0 + 0.5),
        floor(t1.g * 255.0 + 0.5) * 256.0 + floor(t1.r * 255.0 + 0.5),
        floor(t1.a * 255.0 + 0.5) * 256.0 + floor(t1.b * 255.0 + 0.5)) / 65535.0;
}
// The integral from the row's start to col + u per channel, in color times texel units: the
// stored prefix plus the segment's own trapezoid, accumulated CPU-side from the same
// quantized edge bytes these reads recover.
float4 LutInt(float introw, float col, float4 e0, float4 e1, float u) {
    return LutPrefix(introw, col) * 256.0 + (e0 + (e1 - e0) * 0.5 * u) * u;
}
// The row box filtered over the AA band, straight in the frame's channels: between stops
// color is linear in the gradient value, so the box mean IS the filtered color, and across
// a hard stop it is the two flats blended by coverage, the same shape a shape edge fades
// with. The three cases mirror RampBox: no boundary in the window reads the segment's
// middle, one boundary reads both segments exactly, more goes through the integrals. Like
// RampBox, everything is a continuous function of t, and wrapped runs the row periodic so
// a sawtooth seam carries a hard stop cleanly.
float4 LutColor(float t, float row, float introw, float aaT, bool wrapped) {
    float h = 0.5 * clamp(aaT * 256.0, 1e-5, 256.0);
    float a = t * 256.0 - h;
    float b = t * 256.0 + h;
    if (!wrapped) {
        a = clamp(a, 0.0, 256.0);
        b = clamp(b, 0.0, 256.0);
    }
    float fa = floor(a);
    float fb = floor(b);
    float ja = wrapped ? Mod(fa, 256.0) : min(fa, 255.0);
    float jb = wrapped ? Mod(fb, 256.0) : min(fb, 255.0);
    // Same end guard as RampBox: a clamped top edge on exactly 256 is inside texel 255, not
    // past a boundary, or the split formula inflates the mean into a bright end line.
    float fbe = wrapped ? fb : min(fb, 255.0);
    float ua = a - (wrapped ? fa : ja);
    float ub = b - (wrapped ? fb : jb);
    float4 a0 = LutTexel(row, ja, 0.0);
    float4 a1 = LutTexel(row, ja, 1.0);
    float4 b0 = LutTexel(row, jb, 0.0);
    float4 b1 = LutTexel(row, jb, 1.0);

    float w = max(b - a, 1e-5);
    float4 m;
    if (fbe - fa < 0.5) {
        m = lerp(a0, a1, (ua + ub) * 0.5);
    } else if (fbe - fa < 1.5) {
        m = ((1.0 - ua) * lerp(a0, a1, (1.0 + ua) * 0.5) + ub * lerp(b0, b1, ub * 0.5)) / w;
    } else {
        float4 intA = LutInt(introw, ja, a0, a1, ua);
        float4 intB = LutInt(introw, jb, b0, b1, ub);
        if (wrapped) {
            // Each full revolution between the window's edges adds the whole row's integral.
            float4 l0 = LutTexel(row, 255.0, 0.0);
            float4 l1 = LutTexel(row, 255.0, 1.0);
            intB += (floor(b / 256.0) - floor(a / 256.0)) * LutInt(introw, 255.0, l0, l1, 1.0);
        }
        m = (intB - intA) / w;
    }
    return m;
}

// A packed color float driven negative by the vertex shader carries a cosine palette
// instead of two stops: bias + amplitude * cos(tau * (frequency * t + phase)) per channel,
// after https://iquilezles.org/articles/palettes/ (MIT, see THIRD_PARTY_NOTICES.md). The
// channels come out in the active color space's remapped [0, 1] frame, exactly what ToRgb
// takes, so in Oklab the cosines swing lightness and the two color axes. Bit layout lives
// with PackPalette in ShapeVertex.cs; whole number frequencies are what let a palette tile
// with no seam.
// rampd marks a ramp row aboard: ch6 and ch7 reshape to carry it, and t runs through the
// row's curve before the cosines see it.
float4 PaletteColor(float4 data, float t, float tAa, float wrapF, bool rampd) {
    float m = -data.x - 1.0;
    float ch1 = DecodeDigit(m, 2048.0);
    float ch0 = m;
    m = data.y;
    float ch3 = DecodeDigit(m, 2048.0);
    float ch2 = m;
    m = data.z;
    float ch5 = DecodeDigit(m, 2048.0);
    float ch4 = m;
    m = data.w;
    float ch7 = DecodeDigit(m, 2048.0);
    float ch6 = m;

    float bx = DecodeDigit(ch0, 128.0);
    float by = DecodeDigit(ch1, 128.0);
    float bz = DecodeDigit(ch2, 128.0);
    float ax = DecodeDigit(ch3, 128.0);
    float ay = DecodeDigit(ch4, 128.0);
    float az = DecodeDigit(ch5, 128.0);

    float3 freq = float3(ch0, ch1, ch2);
    float3 phase;
    float alpha;
    float4 rb = float4(t, t, -1.0, t);
    if (rampd) {
        float dx = DecodeDigit(ch6, 4.0);
        float dy = DecodeDigit(ch6, 4.0);
        float dz = DecodeDigit(ch6, 4.0);
        alpha = DecodeDigit(ch7, 64.0);
        phase = (float3(ch3, ch4, ch5) * 4.0 + float3(dx, dy, dz)) / 64.0;
        rb = RampBox(t, ch6 + 32.0 * ch7, tAa, wrapF > 0.5);
    } else {
        float dx = DecodeDigit(ch6, 32.0);
        float dz = DecodeDigit(ch7, 32.0);
        alpha = ch7;
        phase = (float3(ch3, ch4, ch5) * 32.0 + float3(dx, ch6, dz)) / 512.0;
    }
    float3 bias = float3(bx, by, bz);
    float3 amp = float3(ax, ay, az);
    float3 c;
    if (rb.z >= 0.0) {
        // One jump between two flat runs: blend the two edge colors, see RampBox.
        float3 cA = saturate((bias + amp * cos(6.283185307179586 * (freq * rb.x + phase))) / 127.0);
        float3 cB = saturate((bias + amp * cos(6.283185307179586 * (freq * rb.y + phase))) / 127.0);
        c = lerp(cB, cA, rb.z);
    } else {
        c = saturate((bias + amp * cos(6.283185307179586 * (freq * rb.w + phase))) / 127.0);
    }
    return float4(c, alpha / 63.0);
}

// The row a ramped pair of stops carries: 3 bits above each alpha byte and 2 more as lane
// signs (see EmbedRamp in ShapeVertex.cs). Puts the true alphas back while it is in there,
// and they come back exact: an alpha is a byte at heart, so the byte rides untouched under
// the row bits.
float StopRampRow(float4 pk, bool zN, bool wN, inout float4 ca, inout float4 cb) {
    float m = pk.y;
    float chA = DecodeDigit(m, 2048.0);
    ca.a = DecodeDigit(chA, 256.0) / 255.0;
    m = pk.w;
    float chB = DecodeDigit(m, 2048.0);
    cb.a = DecodeDigit(chB, 256.0) / 255.0;
    return chA + 8.0 * chB + (zN ? 64.0 : 0.0) + (wN ? 128.0 : 0.0);
}

#if VULKAN
// MonoGame's native Vulkan backend maps NormalizedShort4 attributes to SSCALED instead
// of SNORM (ToVkFormat in MGG_Vulkan.cpp), so the packed colors arrive as raw 0..32767
// integers. Unscale only when raw values show up: legitimate channels never exceed 1,
// so this goes quiet on its own once the mapping is fixed upstream.
float4 FixSnorm(float4 v) { return any(abs(v) > 1.5) ? v / 32767.0 : v; }
#else
float4 FixSnorm(float4 v) { return v; }
#endif

PixelInput SpriteVertexShader(VertexInput v) {
    PixelInput output;

    output.Position = mul(v.Position, view_projection);
    output.TexCoord = v.TexCoord;
    // Negated lanes are flags (see PackPalette and EmbedRamp in ShapeVertex.cs): the first
    // marks a cosine palette, the third a ramp, and the fifth and seventh carry a ramped
    // stop pair's top row bits. The payloads repack exactly like colors do; each sign moves
    // onto its packed float, pushed past -1 so a payload of zero still keeps it.
    float4 fillA = FixSnorm(v.FillA);
    float4 fillB = FixSnorm(v.FillB);
    float4 borderA = FixSnorm(v.BorderA);
    float4 borderB = FixSnorm(v.BorderB);
    output.Fill = PackColors(abs(fillA), abs(fillB));
    output.Border = PackColors(abs(borderA), abs(borderB));
    if (fillA.x < 0.0) output.Fill.x = -output.Fill.x - 1.0;
    if (fillA.z < 0.0) output.Fill.y = -output.Fill.y - 1.0;
    if (fillB.x < 0.0) output.Fill.z = -output.Fill.z - 1.0;
    if (fillB.z < 0.0) output.Fill.w = -output.Fill.w - 1.0;
    if (borderA.x < 0.0) output.Border.x = -output.Border.x - 1.0;
    if (borderA.z < 0.0) output.Border.y = -output.Border.y - 1.0;
    if (borderB.x < 0.0) output.Border.z = -output.Border.z - 1.0;
    if (borderB.z < 0.0) output.Border.w = -output.Border.w - 1.0;
    output.FillCoord = v.FillCoord;
    output.BorderCoord = v.BorderCoord;
    output.Meta1 = v.Meta1;
    output.Meta2 = v.Meta2;
    output.Meta3 = v.Meta3;
    output.Pos = v.Position.xy;
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
float2 PixelFootprint(float2 jx, float2 jy) {
    float a = dot(jx, jx) + dot(jy, jy);
    float det = abs(jx.x * jy.y - jx.y * jy.x);
    float s = sqrt(max(a * a - 4.0 * det * det, 0.0));
    float pixMax = sqrt(0.5 * (a + s));
    return float2(det / max(pixMax, 1e-12), pixMax);
}
// The derivatives are taken before anything branches on them, so no gradient sits inside
// flow control; see the ANGLE note on DitherNoise for why that matters.
// gd is the distance's change across one pixel on each screen axis.
float PixelWidthAlong(float2 gd, float2 footprint, float bias) {
    // Square on, a pixel spans exactly one unit along the edge normal. Across a diagonal it
    // spans sqrt(2), which is |nx| + |ny| for a unit normal, and that is this sum once d is a
    // distance. A fade exactly that wide leaves every pixel the edge misses at its own color,
    // at any angle. Only the centred fade takes it: widening the outward one would move
    // pixels that are otherwise staying exactly as they are.
    //
    // The pair is two differences a pixel apart though, not a gradient, and a quad sitting on a
    // corner reads one edge across and the other one down. The sum then claims the diagonal's
    // widening for a corner that is square, dimming the corner pixel and painting the ones
    // outside it, at whichever corners the quads happen to land that way. Reading the same
    // width a second way settles it: a distance field has a unit gradient, so the larger of the
    // two differences pins a normal down on its own - the smaller one can only be whatever is
    // left of the unit - and that normal asks for m + sqrt(1 - m*m). On plain geometry the two
    // readings agree, since the pair is that normal and the arithmetic is the same. They part
    // only where the pair has overshot the footprint, which nothing but a corner or a curve
    // tighter than a pixel does, and the narrower reading is the right one in both: at a corner
    // the dominant difference is the real edge's and the other is the crease's, and on a tight
    // curve the sum carries the same stepping error twice over. The outward fade needs none of
    // this, its ceiling being one pixel to begin with.
    float m = saturate(max(abs(gd.x), abs(gd.y)) / max(footprint.y, 1e-12));
    float w = bias > 0.0 ? min(abs(gd.x) + abs(gd.y), footprint.y * (m + sqrt(1.0 - m * m))) : length(gd);
    return clamp(w, footprint.x, footprint.y * (bias > 0.0 ? 1.4142135624 : 1.0));
}
float PixelWidth(float d, float2 footprint, float bias) {
    return PixelWidthAlong(float2(ddx(d), ddy(d)), footprint, bias);
}

// How far outside one clip a world position sits, in that clip's antialiasing widths. The
// distance is a rounded box's, and its slope across the screen comes from the box's own
// normal and the position's derivatives, so nothing here takes a derivative of its own and
// the clips can sit behind a branch.
float ClipDistance(float2 pos, float4 frame, float4 size, float2 jx, float2 jy, float2 footprint, float bias) {
    float2 r = pos - frame.xy;
    float2 l = float2(dot(r, frame.zw), frame.z * r.y - frame.w * r.x);
    float2 q = abs(l) - size.xy + size.z;
    float2 qo = max(q, 0.0);
    float d = length(qo) + min(max(q.x, q.y), 0.0) - size.z;
    float2 n = q.x > 0.0 || q.y > 0.0 ? qo / max(length(qo), 1e-12) : (q.x > q.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
    n *= sign(l);
    float2 g = n.x * frame.zw + n.y * float2(-frame.w, frame.z);
    float w = PixelWidthAlong(float2(dot(g, jx), dot(g, jy)), footprint, bias) * size.w;
    return d / max(w, 1e-12);
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

// Glyph coverage, from the Slug algorithm by way of Forme (see THIRD_PARTY_NOTICES.md). A
// glyph is a list of quadratic curves per band rather than a distance field, so its arm of the
// shape ladder below runs two loops where the others evaluate an SDF, and everything the loops
// touch is held to what GLSL ES 1.00 Appendix A admits: a fixed count, a branchless single
// block body, and carried state limited to independent accumulators. A carried latch, a break,
// or a non-constant bound each degrade the emitted for header into something ANGLE rejects.
//
// The baker pads every band's curve list out to exactly this, so all sixteen lanes fetch real
// texels and the count guard is a stateless step of the loop index rather than an early out.
#define MAX_BAND_CURVES 16

// Positive operands only, which is what keeps it apart from the Mod above: that one folds a
// negative back around through HLSL's %, and % lowers to an fmod whose GLSL spelling has
// broken macOS before. A texel index is never negative and this runs sixteen times a pixel.
float BandMod(float x, float y) {
    return x - y * floor(x / y);
}

#if __KNIFX__
// WebGL1 through KNI cannot take a float texture at all - the interop wraps every upload in a
// Uint8Array and WebGL answers a FLOAT texImage2D with INVALID_OPERATION - so both glyph
// textures arrive as RGBA8 and the two fetches decode them. Everything the loops below do with
// what comes back is the same on every target. See GlyphRepack.cs for the encodings.
//
// A sampled RGBA8 channel is exactly k / 255, so k comes back with one multiply and a round,
// and every field boundary is a power of two, which keeps the divisions exact.
float4 Byte4(float4 t) {
    return floor(t * 255.0 + 0.5);
}

// One texel is two integers, 12 bits then 20. The low nibble of g holds the first value's high
// four bits and the high nibble holds the second value's low four.
float2 FetchBandTexel(float texelIndex) {
    float tx = BandMod(texelIndex, band_tex_size.x);
    float ty = floor(texelIndex * band_texel.x);
    float4 t = Byte4(SampleBand((float2(tx, ty) + 0.5) * band_texel));
    float hi = floor(t.g * 0.0625);
    float lo = t.g - hi * 16.0;
    return float2(t.r + lo * 256.0, hi + t.b * 16.0 + t.a * 4096.0);
}

// One logical texel is two physical ones stacked: the four values' low bytes in row 2y and
// their high bytes in row 2y + 1, each 16 bit value fixed point over [-2, 2].
float4 FetchCurveTexel(float2 curveLoc) {
    float2 at = float2(curveLoc.x, curveLoc.y * 2.0) + 0.5;
    float4 lo = Byte4(SampleCurve(at * curve_texel));
    float4 hi = Byte4(SampleCurve((at + float2(0.0, 1.0)) * curve_texel));
    return (lo + hi * 256.0) * (4.0 / 65535.0) - 2.0;
}
#else
float2 FetchBandTexel(float texelIndex) {
    float tx = BandMod(texelIndex, band_tex_size.x);
    float ty = floor(texelIndex * band_texel.x);
    return SampleBand((float2(tx, ty) + 0.5) * band_texel).rg;
}

float4 FetchCurveTexel(float2 curveLoc) {
    return SampleCurve((curveLoc + 0.5) * curve_texel);
}
#endif

// Slug root eligibility from the signs of the three control point y coordinates.
// 1 - step(y, 0) keeps the reference's strict y > 0 convention, which is what makes a
// scanline through a shared endpoint count exactly one of the two adjoining curves.
float2 RootEligibility(float y1, float y2, float y3) {
    float s0 = 1.0 - step(y1, 0.0);
    float s1 = 1.0 - step(y2, 0.0);
    float s2 = 1.0 - step(y3, 0.0);
    float n0 = 1.0 - s0;
    float n1 = 1.0 - s1;
    float n2 = 1.0 - s2;
    float root1 = saturate(s0 * n1 * n2 + n0 * s1 * n2 + s0 * s1 * n2 + s0 * n1 * s2);
    float root2 = saturate(n0 * s1 * n2 + n0 * n1 * s2 + s0 * n1 * s2 + n0 * s1 * s2);
    return float2(root1, root2);
}

// abs() generalized to a parity: a triangle wave peaking at every odd integer and zero at every
// even one, which is what turns a crossing count into an even-odd fill. Symmetric about every
// integer and continuous everywhere, so an accumulator drifting across an edge fades rather than
// snapping. Folding to the nearest even integer rather than through a mod is what keeps it abs to
// the last bit on [-1, 1]: the floor is 0 there and nothing is subtracted.
float Tri(float x) {
    float a = abs(x);
    return abs(a - 2.0 * floor((a + 1.0) * 0.5));
}
// Tri with the accumulator's own sign put back, which makes it the identity on [-1, 1]. The sign
// is the same branchless one FixDenom uses.
float TriSigned(float x) {
    return (step(0.0, x) * 2.0 - 1.0) * Tri(x);
}

// Sign-preserving nudge away from zero: |x| stays >= eps without a branch, so 1/x is
// always finite. step(0, x) * 2 - 1 maps x >= 0 to +1 and x < 0 to -1. A dead lane has to
// produce large-but-finite garbage rather than a NaN, which a multiply mask cannot kill.
float FixDenom(float x, float eps) {
    float s = step(0.0, x) * 2.0 - 1.0;
    return x + s * max(eps - abs(x), 0.0);
}

// x coordinates where the sample-relative quadratic crosses y = 0, blended between the
// quadratic and near-linear forms by mask instead of the reference's branch. Forme widened
// the near-linear threshold from Slug's 1/65536 to 1e-4 for transpiled GLSL precision.
// The lerp stays spelled this way: under D3D's x + s * (y - x) lowering a huge first
// argument at s = 1 gives 0 rather than tl, and that lane never reaches an eligible root.
float2 SolveHorizPoly(float4 p12, float2 p3) {
    float2 a = p12.xy - p12.zw * 2.0 + p3;
    float2 b = p12.xy - p12.zw;
    float ra = 1.0 / FixDenom(a.y, 1e-9);
    float rb = 0.5 / FixDenom(b.y, 1e-9);
    float d = sqrt(max(b.y * b.y - a.y * p12.y, 0.0));
    float lin = step(abs(a.y), 0.0001);
    float tl = p12.y * rb;
    float t1 = lerp((b.y - d) * ra, tl, lin);
    float t2 = lerp((b.y + d) * ra, tl, lin);
    return float2(
        (a.x * t1 - b.x * 2.0) * t1 + p12.x,
        (a.x * t2 - b.x * 2.0) * t2 + p12.x);
}

// The whole pixel shader, built once per combination of the four features that cost the most
// to compile. A variant without one holds that feature's inputs where they sit when nothing
// uses it, and the compiler drops whatever they would have reached. See the techniques at the
// end of the file.
float4 ShapePixel(PixelInput p, bool useDash, bool useFillGradient, bool useBorderGradient, bool useText) {
    float lineSize = p.Meta1.x;
    // A negative width asks for the fade to straddle the true edge instead of sitting outside
    // it, which is what makes a shape cover exactly the pixels its size claims. It rides in the
    // sign rather than in a channel of its own because every interpolator is spoken for, and
    // the packed meta is already at 2^21, one doubling short of where a ps_3_0 interpolator
    // stops carrying an integer exactly. A blur puts a world radius in this slot instead, and
    // that is always positive, so it reads as the outward fade and never reaches the branches
    // below anyway.
    float aaPixels = abs(p.Meta1.y);
    float aaBias = p.Meta1.y < 0.0 ? 0.5 : 0.0;
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
    if (!useDash) dashType = 0.0;
    // The border's style stays as it came: a border on the fill's own gradient reads the fill's
    // result through sameGradient below, which compares the two, and never needs its own.
    if (!useFillGradient) fillStyles.x = 0.0;

    float2 jx = ddx(p.Pos);
    float2 jy = ddy(p.Pos);
    float2 footprint = PixelFootprint(jx, jy);

    // Each clip's distance in its own antialiasing widths; the pixel is as far out as the
    // furthest clip puts it.
    float clipAlpha = 1.0;
    if (clip_count > 0.5) {
        float clipN = ClipDistance(p.Pos, clip_frame[0], clip_size[0], jx, jy, footprint, aaBias);
        if (clip_count > 1.5) clipN = max(clipN, ClipDistance(p.Pos, clip_frame[1], clip_size[1], jx, jy, footprint, aaBias));
        if (clip_count > 2.5) clipN = max(clipN, ClipDistance(p.Pos, clip_frame[2], clip_size[2], jx, jy, footprint, aaBias));
        if (clip_count > 3.5) clipN = max(clipN, ClipDistance(p.Pos, clip_frame[3], clip_size[3], jx, jy, footprint, aaBias));
        if (clipN >= 1.0 - aaBias) {
            discard;
        }
        clipAlpha = 1.0 - smoothstep(0.0, 1.0, saturate(clipN + aaBias));
    }

    float2 q0 = p.TexCoord.xy;
    if (shape >= 8.5 && shape < 9.5) {
        return SampleTexture(p.TexCoord.xy) * UnpackColor(p.Fill.xy) * clipAlpha;
    }
    // Every technique reads all five textures, in register order, so every one of them keeps
    // the same samplers on the same units: the Vulkan translator drops a sampler nothing reads
    // and numbers the rest by first use. A texel size is never negative, so this never runs,
    // but no compiler can know that.
    if (ramp_texel.x < 0.0) {
        return SampleBand(q0) + SampleCurve(q0) + SampleBlueNoise(q0) + SampleRamp(q0);
    }

    // The inside of a large shape with a flat fill, which is the fill and nothing else. This is
    // what the tail below arrives at there, without the distance it would work out on the way.
    if (shape > 14.5) {
        float4 inside = ToRgb(LerpColorPremul(UnpackColor(p.Fill.xy), UnpackColor(p.Fill.zw), 1.0, space), space);
        inside.rgb *= inside.a;
        inside *= clipAlpha;
        inside.rgb += (DitherNoise(p.Pos.xy) - 0.5) * dither_scale;
        return inside;
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
    float dashV = 0.0;
    float dashR = 0.0;
    float2 dashData = float2(1.0, 0.0);
    bool dashStroke = false;
    // A dashed outline's piece and a dashed path's half segment share the walk around their
    // corner, below the shapes; see PieceSpine for the frame they set up here.
    bool piece = false;
    bool piecePath = false;
    float2 pq = q;
    float pTau = 0.0;
    float2 pH = float2(1.0, 0.0); // Cosine and sine of half the turn.
    float2 pN2 = float2(1.0, 0.0);
    float2 pPiv = float2(0.0, 0.0);
    float pRc = 0.0;
    float pU0 = 0.0;
    float pBand = 0.0;
    float4 pFar = float4(1e30, 0.0, 0.0, 1.0);
    float4 pRadius = float4(0.0, 0.0, 1.0, 1.0);
    bool pLeaving = false;
    float pCorner = -1e6;

    // A glyph is coverage rather than a distance field, so it stands where a shape's SDF would
    // and hands the tail below its coverage in place of the edge fade. The 1 is what every other
    // shape leaves here, and the 0 distance is only ever read by the shape gradient.
    //
    // An SVG element takes the same arm with the even-odd fill rule instead of nonzero. It is the
    // same curves through the same two loops, so the rule is a second shape id rather than a
    // branch of its own, and it costs one step here and two lerps at the very end.
    bool isGlyph = useText && shape >= 12.5 && shape < 14.5;
    float evenOdd = step(13.5, shape);
    float glyphFade = 1.0;

    float d = 0.0;
    if (isGlyph) {
        // Glyph coverage, from the Slug algorithm by way of Forme. The two loops are held to
        // what GLSL ES 1.00 Appendix A admits; see MAX_BAND_CURVES above.
        float2 emCoord = q;
        float bandBase = p.Meta2.x;
        float bandCount = p.Meta2.y;
        float bandMax = bandCount - 1.0;
        // Per axis rather than one number: an anisotropic transform stretches the two scans by
        // different amounts, and each loop only ever measures along its own axis.
        float2 pixelsPerEm = p.Meta2.zw;

        float2 bandPos = emCoord * p.BorderCoord.xy + p.BorderCoord.zw;
        float bandIndexY = clamp(floor(bandPos.y), 0.0, bandMax);
        float bandIndexX = clamp(floor(bandPos.x), 0.0, bandMax);

        float xcov = 0.0;
        float xwgt = 0.0;
        float2 hHeader = FetchBandTexel(bandBase + bandIndexY);
        float hCurveCount = hHeader.x;
        float hCurveOffset = hHeader.y;

        for (int i = 0; i < MAX_BAND_CURVES; i++) {
            float2 curveLoc = FetchBandTexel(hCurveOffset + float(i));
            // Sample relative before anything else: the solver's near-linear threshold is a
            // test on the second difference of these, and forming a.y any other way moves it.
            float4 p12 = FetchCurveTexel(curveLoc) - float4(emCoord, emCoord);
            float2 p3 = FetchCurveTexel(float2(curveLoc.x + 1.0, curveLoc.y)).xy - emCoord;

            // The count guard is stateless. There is no early-out mask: curves past the
            // sorted cut lie entirely left of the sample, where both the coverage and weight
            // terms already clamp to zero.
            float mask = step(float(i) + 0.5, hCurveCount);

            float2 elig = RootEligibility(p12.y, p12.w, p3.y) * mask;
            float2 r = SolveHorizPoly(p12, p3) * pixelsPerEm.x;
            xcov += elig.x * saturate(r.x + 0.5) - elig.y * saturate(r.y + 0.5);
            xwgt = max(xwgt, elig.x * saturate(1.0 - abs(r.x) * 2.0));
            xwgt = max(xwgt, elig.y * saturate(1.0 - abs(r.y) * 2.0));
        }

        float ycov = 0.0;
        float ywgt = 0.0;
        float2 vHeader = FetchBandTexel(bandBase + bandCount + bandIndexX);
        float vCurveCount = vHeader.x;
        float vCurveOffset = vHeader.y;

        for (int j = 0; j < MAX_BAND_CURVES; j++) {
            float2 curveLoc = FetchBandTexel(vCurveOffset + float(j));
            float4 raw12 = FetchCurveTexel(curveLoc);
            float2 raw3 = FetchCurveTexel(float2(curveLoc.x + 1.0, curveLoc.y)).xy;

            // Swap x and y so the horizontal solver and eligibility logic can be reused.
            float4 p12 = float4(raw12.y, raw12.x, raw12.w, raw12.z) - float4(emCoord.yx, emCoord.yx);
            float2 p3 = raw3.yx - emCoord.yx;

            // Same stateless guard as the horizontal loop.
            float mask = step(float(j) + 0.5, vCurveCount);

            float2 elig = RootEligibility(p12.y, p12.w, p3.y) * mask;
            float2 r = SolveHorizPoly(p12, p3) * pixelsPerEm.y;
            ycov += elig.x * saturate(r.x + 0.5) - elig.y * saturate(r.y + 0.5);
            ywgt = max(ywgt, elig.x * saturate(1.0 - abs(r.x) * 2.0));
            ywgt = max(ywgt, elig.y * saturate(1.0 - abs(r.y) * 2.0));
        }

        // Each accumulator holds a signed count of the crossings on one side of the sample, so
        // the fill rule is only how that count is read: nonzero takes its magnitude, even-odd
        // takes its parity. TriSigned is the identity on [-1, 1], which is everywhere the two
        // rules agree, so an outline that draws the same either way comes out bit for bit the
        // same whichever id it carries. Substituting the accumulators is the whole change: the
        // weighted mix and the min below stay exactly what the nonzero fill ships with.
        //
        // The min is not redundant. The vertical scan swaps x and y rather than turning them, so
        // ycov always carries the opposite sign to xcov, and on a 45 degree edge both weights
        // reach 1 and the mix cancels to nothing. The min of the two magnitudes is what holds
        // that edge up.
        float px = lerp(xcov, TriSigned(xcov), evenOdd);
        float py = lerp(ycov, TriSigned(ycov), evenOdd);
        float coverage = max(
            abs(px * xwgt + py * ywgt) / max(xwgt + ywgt, 0.0001),
            min(abs(px), abs(py)));
        // Coverage is the pixel's area inside the outline, so it goes on as is. Boosting it
        // (a square root, say) spreads every edge outward and the text reads bold and soft.
        glyphFade = saturate(coverage);
    } else if (shape < 0.5) {
        d = CircleSDF(q, sdfSize);
    } else if (shape < 1.5) {
        d = RoundBoxSDF(q, float2(sdfSize, p.Meta1.w), p.Meta2);
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
    } else if (shape < 4.5) {
        if (shape < 3.5) {
            d = HexagonSDF(q, sdfSize);
        } else {
            d = EquilateralTriangleSDF(q, sdfSize);
        }
    } else if (shape < 5.5) {
        d = TriangleSDF(q, p.Meta1.zw, p.Meta2.xy, p.Meta2.zw);
    } else if (shape < 6.5) {
        float2 ab = float2(sdfSize, p.Meta1.w);
        float2 eq, er, eNear;
        float eScale;
        d = EllipseSDF(q, ab, eq, er, eNear, eScale);
        if (dashType >= 0.5) {
            // A dashed ellipse comes in pieces, each with a cubic in Meta2 from the folded angle
            // of the nearest point to the outline's length so far, already in periods and turned
            // around where the quadrant runs backwards. The pattern runs on the band's centerline,
            // which falls behind the outline by half the band per radian turned, and the cubic's
            // own slope against the outline's speed is the 1 / period that takes into periods.
            // The rounding slot carries the fraction and phase, since an ellipse has no rounding.
            float2 radii = er * eScale;
            float2 cs = eNear / max(er, 1e-30);
            float t = atan2(cs.y, cs.x);
            float4 c = p.Meta2;
            float speed = max(length(radii * cs.yx), 1e-6);
            float ab2 = radii.x * radii.y;
            float rd = lineSize * 0.5;
            float slope = c.y + t * (2.0 * c.z + 3.0 * c.w * t);
            float k = slope / speed;
            float v = c.x + t * (c.y + t * (c.z + t * c.w)) - rd * k * atan2(radii.x * cs.y, radii.y * cs.x);
            float dv = slope * (1.0 - rd * ab2 / (speed * speed * speed));
            float4 de = DashEdges(v, float2(1.0, rounded));
            rounded = 0.0;
            if (dashType >= 1.5) {
                // Round caps are the capsule around the centerline, as on a piece. Each cap's
                // angle is one Newton step on from the pixel's own, which is all a cap close
                // enough to matter needs, and past the axis the folded angle carries on into the
                // mirror image, which is where that stretch of the outline really is.
                float2 pf = eq * eScale;
                float band = abs(d + rd) - rd;
                float2 ca;
                sincos(t + (de.y - v) / dv, ca.y, ca.x);
                float2 cb;
                sincos(t + (de.z - v) / dv, cb.y, cb.x);
                float caps = min(length(pf - radii * ca + rd * normalize(radii.yx * ca)),
                                 length(pf - radii * cb + rd * normalize(radii.yx * cb))) - rd;
                dashCut = de.x < 0.0 ? band : max(band, caps);
            } else {
                dashCut = de.x * (speed + d * ab2 / (speed * speed)) / max(abs(dv), 1e-6);
            }
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
            dashR = p.Meta2.z;
            dashData = float2(p.Meta1.w, p.Meta2.w);
            dashStroke = true;
        }
    } else if (shape < 10.5) {
        // One piece of a dashed outline. Meta2 is the turn, the outline's own corner radius, the
        // radius the pattern turns on, and the pattern position where its fan starts. The pattern
        // itself rides in the two size slots, which a piece has no use for. A chamfer's square
        // corner cut by less than its pattern circle arrives with the cut as a negative radius.
        pTau = p.Meta2.x;
        float ro = max(p.Meta2.y, 0.0);
        float rd = lineSize * 0.5;
        sincos(pTau * 0.5, pH.y, pH.x);
        pN2 = float2(pH.x * pH.x - pH.y * pH.y, 2.0 * pH.x * pH.y);
        // The rounded corner: the fillet's circle across its own cone, the two edges either side.
        bool cone = pTau > 0.0 && q.y >= 0.0 && q.x * pN2.y - q.y * pN2.x >= 0.0;
        d = p.Meta2.y < 0.0 ? CutCornerSDF(q, -p.Meta2.y) : cone ? length(q) - ro : max(q.x, dot(q, pN2)) - ro;
        piece = true;
        pPiv = -pH * ((p.Meta2.z - ro) / max(pH.x, 1e-4));
        pRc = p.Meta2.z - rd;
        pU0 = p.Meta2.w;
        dashData = float2(sdfSize, p.Meta1.w);
        pBand = abs(d + rd) - rd;
        pRadius = float4(rd, rd, 1.0, 1.0);
    } else if (shape > 11.5) {
        d = ChamferBoxSDF(q, float2(sdfSize, p.Meta1.w), p.Meta2);
    } else {
        float4 sd = p.Meta2;
        float pathLen = p.Meta1.w;
        if (dashType >= 0.5) {
            // Half of a dashed path's segment, in a frame with the joint or cap at its own end at
            // the origin, x running into the segment and y on the outer side of the turn. The
            // half that leaves the joint carries a negative length. The walk reads every half as
            // the one arriving at its joint, which for the leaving half mirrors it across the
            // bisector, and turns the pattern around instead. Meta2.x is the two ends' turns as
            // 11 bit codes over [0, pi], this end's above, and Meta2.y packs the end mode, which
            // side the far circle sits on, then both circles as 7 bit codes over [0, 2r]. Then
            // come the far end's half width and the period, with the fraction and the phase,
            // counted from this end's circle, in the rounding slot. A round dash follows the
            // spine's own corners.
            float t = sd.x;
            float tauFar = DecodeDigit(t, 2048.0) / 2047.0 * 3.1415926536;
            pTau = t / 2047.0 * 3.1415926536;
            float m = sd.y;
            float mode = DecodeDigit(m, 8.0);
            float farSide = DecodeDigit(m, 2.0);
            float codeOwn = DecodeDigit(m, 128.0);
            pLeaving = pathLen < 0.0;
            pathLen = abs(pathLen);
            sincos(pTau * 0.5, pH.y, pH.x);
            pN2 = float2(pH.x * pH.x - pH.y * pH.y, 2.0 * pH.x * pH.y);
            pq = float2(q.y, -q.x);
            bool butt = dashType < 1.5;
            pRc = butt ? sdfSize * codeOwn / 63.5 : 0.0;
            pPiv = -pH * (pRc / max(pH.x, 1e-4));
            float frFar = butt ? sd.z * m / 63.5 : 0.0;
            float2 hf;
            sincos(tauFar * 0.5, hf.y, hf.x);
            pFar = float4(pathLen + pPiv.y - frFar * hf.y / max(hf.x, 1e-4), frFar, tauFar, farSide > 0.5 ? -1.0 : 1.0);
            float slope = (sd.z - sdfSize) / max(pFar.x, 1e-4);
            pRadius = float4(sdfSize, sd.z, max(pFar.x, 1e-4), abs(slope) < 1.0 ? rsqrt(1.0 - slope * slope) : 1.0);
            dashData = float2(sd.w, rounded);
            // Miter and bevel tips reach past the joint disc, and a dash edge near the corner
            // would sweep them as a needle. Out there the dash is bounded by the disc instead,
            // and the tip only grows back once the dash covers the whole fan with margin to
            // spare, which the dash's distance at the fan's middle measures.
            if (pTau > 1e-4 && q.x < 0.0) {
                float mid = pTau * pRc * 0.5;
                pCorner = length(q) - sdfSize + min(DashDistance(mid, dashData) + mid, 0.0) * 2.0;
            }
            // The far end runs on open, since this half never reaches it, and a bevel cuts square
            // across the bisector.
            sd = float4(mode + 24.0, 1.5707963 + pTau * 0.5, 0.0, sd.z);
            rounded = 0.0;
            piece = true;
            piecePath = true;
        }
        // Solid paths carry the far end's half width in Meta2.w so a stroke can taper. It
        // equals sdfSize on a uniform path, which takes the plain capsule and keeps those
        // pixels exactly as they were.
        d = sd.w == sdfSize
            ? StrokeSDF(q, pathLen, sdfSize, sd)
            : StrokeConeSDF(q, pathLen, sdfSize, sd.w, sd);
        pBand = d;
    }

    if (piece) {
        // The pixel's place along the pattern: straight along the incoming edge before the fan,
        // the angle around the pattern circle through it, and straight along the outgoing edge
        // after it.
        float arc = pTau * max(pRc, 0.0);
        float2 w = pq - pPiv;
        float2 t2 = float2(-pN2.y, pN2.x);
        float v;
        bool before = w.y < 0.0;
        bool after = pTau <= 0.0 || w.x * pN2.y - w.y * pN2.x < 0.0;
        // Behind the pivot both edges are candidates, and the nearer one is the one whose own
        // coordinate is larger, which is a test against the bisector.
        bool fold = before && after;
        if (fold) {
            before = w.x >= dot(w, pN2);
            after = !before;
        }
        if (before) {
            v = w.y;
        } else if (after) {
            v = arc + dot(w, t2);
        } else {
            v = atan2(w.y, w.x) * max(pRc, 0.0);
        }
        float4 de = DashEdges(pU0 + (pLeaving ? arc - v : v), dashData);
        float v1 = de.y - pU0;
        float v2 = de.z - pU0;
        v1 = pLeaving ? arc - v1 : v1;
        v2 = pLeaving ? arc - v2 : v2;
        float2 n1, n2;
        float2 e1 = PieceEdge(v1, arc, pRc, pPiv, pN2, pFar, n1);
        float2 e2 = PieceEdge(v2, arc, pRc, pPiv, pN2, pFar, n2);
        float r1 = PieceRadius(v1, pRadius);
        float r2 = PieceRadius(v2, pRadius);
        float cut;
        if (dashType >= 1.5) {
            // Round caps are the capsule around the centerline. A pixel's own position along the
            // pattern is its nearest centerline point's, so inside a dash the nearest spine point
            // is inside it too and the band is the answer; in a gap it is one of the two caps.
            cut = de.x < 0.0 ? pBand : max(pBand, min(length(pq - e1) - r1, length(pq - e2) - r2));
            if (fold) {
                // Behind the pivot the centerline folds, and the run the pixel didn't pick can
                // hold a dash that reaches it all the same, so that run's capsule counts too.
                float vo = before ? arc + dot(w, t2) : w.y;
                float lo = before ? abs(dot(w, pN2) - pRc) : abs(w.x - pRc);
                float4 eo = DashEdges(pU0 + (pLeaving ? arc - vo : vo), dashData);
                float o1 = eo.y - pU0;
                float o2 = eo.z - pU0;
                o1 = pLeaving ? arc - o1 : o1;
                o2 = pLeaving ? arc - o2 : o2;
                float co = eo.x < 0.0 ? lo - PieceRadius(vo, pRadius)
                                      : min(length(pq - PieceEdge(o1, arc, pRc, pPiv, pN2, pFar, n1)) - PieceRadius(o1, pRadius),
                                            length(pq - PieceEdge(o2, arc, pRc, pPiv, pN2, pFar, n2)) - PieceRadius(o2, pRadius));
                cut = min(cut, max(pBand, co));
            }
        } else {
            // A butt dash ends on a straight cut across the band, square to the direction the
            // pattern runs there. The cut stops at the band's inner edge, since a line carried
            // round a corner turning more than a right angle comes back on the wrong side of
            // itself, while an outline's runs on past the outer edge to reach a corner's tip.
            float aa = footprint.y * aaPixels;
            float2 c1 = float2(n1.y, -n1.x);
            float2 c2 = float2(n2.y, -n2.x);
            // A path's stroke is bounded past its outer edge by the joint disc instead.
            float reach1 = piecePath ? 2.0 * (r1 + aa) : 1e6;
            float reach2 = piecePath ? 2.0 * (r2 + aa) : 1e6;
            float run = min(SegRun(pq, e1 - c1 * (r1 + aa), c1, reach1), SegRun(pq, e2 - c2 * (r2 + aa), c2, reach2));
            cut = de.x < 0.0 ? -run : run;
        }
        if (piecePath) {
            // A path's dash cuts the whole stroke, so every dash gets its own edges and border.
            d = max(max(d, cut), pCorner);
            dashType = 0.0;
        } else {
            dashCut = cut;
        }
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
    float pixelWidth = PixelWidth(d, footprint, aaBias);

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

    // A glyph has no edge to measure the fade across, so what a gradient antialiases its stops
    // over is the pixel's own footprint, arrived at the same way a dash edge's width is.
    float aaSize = (isGlyph ? footprint.y : pixelWidth) * aaPixels;

    // Beyond the outer AA edge every branch below resolves to premultiplied zero, and so does
    // everything a glyph's coverage misses - which is most of its quad, since the outline's box
    // is padded out to hold the fade.
    if (isGlyph ? glyphFade <= 0.0 : d >= aaSize * (1.0 - aaBias)) {
        discard;
    }

    // The packed floats' signs are flags (see the vertex shader): y marks a ramp riding the
    // gradient, z and w carry a ramped stop pair's top row bits. The payloads come back
    // positive before anything unpacks them; x keeps its sign, PaletteColor reads it itself.
    bool fillRamp = useFillGradient && p.Fill.y < -0.5;
    bool fillZN = p.Fill.z < -0.5;
    bool fillWN = p.Fill.w < -0.5;
    bool borderRamp = useBorderGradient && p.Border.y < -0.5;
    bool borderZN = p.Border.z < -0.5;
    bool borderWN = p.Border.w < -0.5;
    // The third lane's sign without the ramp flag beside it marks a color ramp; a ramped
    // stop pair only ever sets it with the ramp flag, so the pair can't collide.
    bool fillLut = useFillGradient && fillZN && !fillRamp;
    bool borderLut = useBorderGradient && borderZN && !borderRamp;
    float4 fillPk = float4(p.Fill.x,
                           fillRamp ? -p.Fill.y - 1.0 : p.Fill.y,
                           fillZN ? -p.Fill.z - 1.0 : p.Fill.z,
                           fillWN ? -p.Fill.w - 1.0 : p.Fill.w);
    float4 borderPk = float4(p.Border.x,
                             borderRamp ? -p.Border.y - 1.0 : p.Border.y,
                             borderZN ? -p.Border.z - 1.0 : p.Border.z,
                             borderWN ? -p.Border.w - 1.0 : p.Border.w);
    float4 fillA = UnpackColor(fillPk.xy);
    float4 fillB = UnpackColor(fillPk.zw);

    // The glyph's own coverage is its fade, and it has no border band for the crossfade to
    // reach: the border slots stand down on a glyph quad (see the ctor in ShapeVertex.cs) and
    // BorderCoord carries the band transform rather than a second gradient's frame.
    float edgeFade = isGlyph ? glyphFade : 1.0 - smoothstep(0.0, 1.0, saturate(d / aaSize + aaBias));
    float borderMix = isGlyph ? 0.0 : smoothstep(0.0, 1.0, saturate((d + lineSize) / aaSize + 1.0 - aaBias));

    // Closed outlines mask their border band along the perimeter; the gaps show the fill. Every
    // piece hands over the world distance to its dash's edge, or for round caps to the capsule
    // around the band's centerline, so the fade only needs the pixel's footprint: the cut's own
    // screen derivative would misfire across the pattern's wrap.
    if (dashType >= 0.5 && !dashStroke) {
        float dashFade = 1.0 - smoothstep(0.0, 1.0, saturate(dashCut / (footprint.y * aaPixels) + aaBias));
        // A round cap is the band itself rather than a cut across it.
        borderMix = dashType >= 1.5 ? dashFade : borderMix * dashFade;
    }

    // The fill/border crossfade is coverage, not a gradient: blend premultiplied in
    // sRGB so the inner AA fringe matches the framebuffer blend outside the edge.
    //
    // Each side is evaluated at most once, and the two shortcuts are which of them gets to
    // stand down rather than formulas of their own. Fill and border on the same gradient
    // collapses the crossfade to edge coverage, so the border simply reads what the fill
    // produced; a fill transparent at both stops contributes nothing anywhere, so it stays
    // zero and only the border runs. Both fall out of the same crossfade below, which is why
    // it is written once. The vertex data is uniform per quad, so both tests are coherent
    // across it.
    // Evaluating each side once is also what holds the gradient machinery to two copies in
    // the compiled shader rather than four. That machinery is the largest thing in this file
    // and the driver compiles every line of it at load, so the three cases written out long
    // hand cost around a fifth of the shader on their own.
    bool sameGradient = all(p.Fill == p.Border) && all(p.FillCoord == p.BorderCoord) && all(fillStyles == borderStyles) && all(p.Meta3.xy == p.Meta3.zw);
    // A palette's unpacked stops are payload bits, not colors, so it never reads as a
    // transparent fill here, a ramped fill's alpha lanes carry row bits over the alpha, and
    // a color ramp's lanes are all payload, so those skip the shortcut too.
    bool fillPal = useFillGradient && p.Fill.x < -0.5;
    bool borderOnly = !fillPal && !fillRamp && !fillLut && fillA.a == 0.0 && fillB.a == 0.0 && !sameGradient;
    if (borderOnly && borderMix <= 0.0) {
        // Transparent fill: everything inside the border band contributes nothing.
        discard;
    }

    // A loop over the two sides would leave one copy rather than two, but ShadowDusk lowers a
    // loop that carries values across iterations into a comma expression in the update clause,
    // and Appendix A of GLSL ES 1.00 admits nothing there but the index's own step. WebGL and
    // Android hold this shader to that grammar, so the sides stay written out.
    //
    // A side the crossfade gives no weight to is skipped, so only the thin fringe between the
    // two pays for both. Nothing in either side takes a derivative, and the ramps they read
    // have no mipmaps to pick between.
    float4 fr = float4(0.0, 0.0, 0.0, 0.0);
    float4 br = float4(0.0, 0.0, 0.0, 0.0);
    if (!borderOnly && (sameGradient || borderMix < 1.0)) {
        float tAa, wrapF;
        float t = Gradient(fillStyles, p.FillCoord, p.Pos.xy, d, aaSize, p.Meta3.xy, fillPal, fillRamp || fillLut, tAa, wrapF);
        float4 c;
        float cSpace = space;
        if (fillPal) {
            c = PaletteColor(fillPk, t, tAa, wrapF, fillRamp);
        } else if (fillLut) {
            float m = fillPk.z;
            float introw = DecodeDigit(m, 2048.0);
            c = LutColor(t, m, introw, tAa, wrapF > 0.5);
            // The row carries Oklch on Oklab's axes, so Oklch reads back as Oklab. Oklab and
            // Rgb stay themselves, which is what max leaves them as.
            cSpace = max(space, 1.0);
        } else {
            if (fillRamp) {
                float row = StopRampRow(fillPk, fillZN, fillWN, fillA, fillB);
                // Two stops are linear in the gradient value, so the window's mean value IS
                // the filtered color; only a palette has to blend colors instead.
                t = RampBox(t, row, tAa, wrapF > 0.5).w;
            }
            c = LerpColorPremul(fillA, fillB, t, space);
        }
        fr = ToRgb(c, cSpace);
        fr.rgb *= fr.a;
        // A shared gradient leaves the border reading exactly this, so it never runs its own.
        br = fr;
    }
    if (!sameGradient && borderMix > 0.0) {
        bool borderPal = useBorderGradient && p.Border.x < -0.5;
        float tAa, wrapF;
        float t = Gradient(useBorderGradient ? borderStyles : float2(0.0, 0.0), p.BorderCoord, p.Pos.xy, d, aaSize, p.Meta3.zw, borderPal, borderRamp || borderLut, tAa, wrapF);
        float4 c;
        float cSpace = space;
        if (borderPal) {
            c = PaletteColor(borderPk, t, tAa, wrapF, borderRamp);
        } else if (borderLut) {
            float m = borderPk.z;
            float introw = DecodeDigit(m, 2048.0);
            c = LutColor(t, m, introw, tAa, wrapF > 0.5);
            cSpace = max(space, 1.0);
        } else {
            float4 bA = UnpackColor(borderPk.xy);
            float4 bB = UnpackColor(borderPk.zw);
            if (borderRamp) {
                float row = StopRampRow(borderPk, borderZN, borderWN, bA, bB);
                t = RampBox(t, row, tAa, wrapF > 0.5).w;
            }
            c = LerpColorPremul(bA, bB, t, space);
        }
        br = ToRgb(c, cSpace);
        br.rgb *= br.a;
    }

    // The edge fade applies after the crossfade so the fill also fades where a dash gap
    // lets it reach the outer edge. Solid borders keep borderMix at 1 wherever the fade
    // is below 1, so the fade still lands on the border alone there.
    float4 result = lerp(fr, br, borderMix) * edgeFade;

    result *= clipAlpha;
    // With premultiplied blending the source color adds straight into the framebuffer, so
    // offsetting rgb here dithers the post-blend value that actually quantizes to 8 bits,
    // covering banding from color and alpha gradients alike. Left unclamped on purpose:
    // the negative half must survive to dither near-black, and the target clamps on write.
    result.rgb += (DitherNoise(p.Pos.xy) - 0.5) * dither_scale;
    return result;
}

// One technique per combination of dashes (bit 0), fill gradients beyond a flat color (bit 1),
// border gradients that differ from the fill's (bit 2) and text (bit 3), at the index those bits
// make, which is how ShapeBatch picks one. A technique's shaders compile the first time
// something draws with it, so a game only waits on the ones it uses.
float4 ShapePixel0(PixelInput p) : SV_TARGET { return ShapePixel(p, false, false, false, false); }
float4 ShapePixel1(PixelInput p) : SV_TARGET { return ShapePixel(p, true, false, false, false); }
float4 ShapePixel2(PixelInput p) : SV_TARGET { return ShapePixel(p, false, true, false, false); }
float4 ShapePixel3(PixelInput p) : SV_TARGET { return ShapePixel(p, true, true, false, false); }
float4 ShapePixel4(PixelInput p) : SV_TARGET { return ShapePixel(p, false, false, true, false); }
float4 ShapePixel5(PixelInput p) : SV_TARGET { return ShapePixel(p, true, false, true, false); }
float4 ShapePixel6(PixelInput p) : SV_TARGET { return ShapePixel(p, false, true, true, false); }
float4 ShapePixel7(PixelInput p) : SV_TARGET { return ShapePixel(p, true, true, true, false); }
float4 ShapePixel8(PixelInput p) : SV_TARGET { return ShapePixel(p, false, false, false, true); }
float4 ShapePixel9(PixelInput p) : SV_TARGET { return ShapePixel(p, true, false, false, true); }
float4 ShapePixel10(PixelInput p) : SV_TARGET { return ShapePixel(p, false, true, false, true); }
float4 ShapePixel11(PixelInput p) : SV_TARGET { return ShapePixel(p, true, true, false, true); }
float4 ShapePixel12(PixelInput p) : SV_TARGET { return ShapePixel(p, false, false, true, true); }
float4 ShapePixel13(PixelInput p) : SV_TARGET { return ShapePixel(p, true, false, true, true); }
float4 ShapePixel14(PixelInput p) : SV_TARGET { return ShapePixel(p, false, true, true, true); }
float4 ShapePixel15(PixelInput p) : SV_TARGET { return ShapePixel(p, true, true, true, true); }

technique Shapes0 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel0();
    }
}
technique Shapes1 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel1();
    }
}
technique Shapes2 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel2();
    }
}
technique Shapes3 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel3();
    }
}
technique Shapes4 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel4();
    }
}
technique Shapes5 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel5();
    }
}
technique Shapes6 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel6();
    }
}
technique Shapes7 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel7();
    }
}
technique Shapes8 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel8();
    }
}
technique Shapes9 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel9();
    }
}
technique Shapes10 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel10();
    }
}
technique Shapes11 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel11();
    }
}
technique Shapes12 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel12();
    }
}
technique Shapes13 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel13();
    }
}
technique Shapes14 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel14();
    }
}
technique Shapes15 {
    pass {
        VertexShader = compile VS_SHADERMODEL SpriteVertexShader();
        PixelShader = compile PS_SHADERMODEL ShapePixel15();
    }
}
