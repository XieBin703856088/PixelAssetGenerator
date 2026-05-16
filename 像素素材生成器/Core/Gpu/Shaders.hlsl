// Consolidated GPU shaders for Pixel Generator
// This file collects all compute shader entry points used by the application
// and provides a single authoritative HLSL source for future node expansion.

// --- Shape batch shader (CS_ShapeMain) ---
struct Shape
{
    float shapeType; // 0 circle, 1 rect, 2 diamond, 3 ring
    float radius;
    float centerX;
    float centerY;
    float rotation;
    float scaleX;
    float scaleY;
    float r;
    float g;
    float b;
    float padding0;
    float hardness;
    float invert;
    float gapDepth; // per-shape seam depth
    float edgeRoughness; // per-shape edge roughness
    float wear; // per-shape wear amount
    float seed; // per-shape seed for noise
};

StructuredBuffer<Shape> Shapes : register(t0);
cbuffer ShapeParamsCB : register(b0)
{
    int ShapeCount;
    // unique padding names to avoid collisions when consolidated
    float ShapeParams_pad0; float ShapeParams_pad1; float ShapeParams_pad2;
};

RWTexture2D<float4> ShapeOutput : register(u0);

float Shape_hash21(float2 p, float seed)
{
    return frac(sin(dot(p, float2(127.1,311.7)) + seed) * 43758.5453);
}

float Shape_noise(float2 p, float seed)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Shape_hash21(i + float2(0.0,0.0), seed);
    float b = Shape_hash21(i + float2(1.0,0.0), seed);
    float c = Shape_hash21(i + float2(0.0,1.0), seed);
    float d = Shape_hash21(i + float2(1.0,1.0), seed);
    float2 u = f*f*(3.0-2.0*f);
    return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
}

[numthreads(16,16,1)]
void CS_ShapeMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x;
    uint y = DTid.y;
    uint width, height;
    ShapeOutput.GetDimensions(width, height);
    if (x >= width || y >= height) return;

    float4 accum = float4(0,0,0,0);

    for (int si = 0; si < ShapeCount; si++)
    {
        Shape s = Shapes[si];
        float cx = (float)x - s.centerX;
        float cy = (float)y - s.centerY;

        if (abs(s.rotation) > 0.001f)
        {
            float ss = sin(s.rotation);
            float cs = cos(s.rotation);
            float rcx = cx * cs + cy * ss;
            float rcy = -cx * ss + cy * cs;
            cx = rcx; cy = rcy;
        }
        // Apply scale
        cx /= max(0.1f, s.scaleX);
        cy /= max(0.1f, s.scaleY);

        float dist = sqrt(cx*cx + cy*cy);
        float alpha = 0.0f;
        int ishape = (int)s.shapeType;
        int iinvert = (int)s.invert;

        if (ishape == 1) // rect
        {
            if (s.edgeRoughness > 0.001f)
            {
                float n = Shape_noise(float2((cx+cy)*0.5, (cx-cy)*0.5) * 0.2, s.seed);
                float off = (n - 0.5) * s.edgeRoughness * 8.0;
                cx += off;
                cy += off;
            }

            float adx = abs(cx);
            float ady = abs(cy);
            float d = max(adx, ady);
            dist = d;
            alpha = dist <= s.radius ? 1.0f : 0.0f;

            if (s.gapDepth > 0.001f)
            {
                float edgeDist = s.radius - d;
                // Use a smaller normalization factor so thin seams remain visible when
                // radius is large (pixel-space radii). The previous divisor used 0.5
                // which made edge factor negligibly small for typical brick sizes.
                float norm = saturate(edgeDist / max(1.0, s.radius * 0.05));
                float edgeFactor = smoothstep(0.0, 1.0, norm);
                alpha = saturate(edgeFactor);
            }
        }
        else if (ishape == 2) // diamond
        {
            float d = (abs(cx) + abs(cy)) * 0.70710678f;
            dist = d;
            alpha = dist <= s.radius ? 1.0f : 0.0f;
        }
        else if (ishape == 3) // ring
        {
            float d = dist;
            float rInner = s.radius * 0.6f;
            float ringWidth = s.radius - rInner;
            alpha = (abs(d - rInner) <= ringWidth) ? 1.0f : 0.0f;
        }
        else // circle
        {
            alpha = dist <= s.radius ? 1.0f : 0.0f;
        }

        if (s.hardness < 0.99f)
        {
            float feather = s.radius * (1.0f - s.hardness);
            if (feather > 0.0001f)
            {
                float t = saturate((s.radius - dist) / feather);
                alpha = t;
            }
        }

        if (iinvert == 1) alpha = 1.0f - alpha;

        float3 finalColor = float3(s.r, s.g, s.b);
        if (s.gapDepth > 0.001f)
        {
            float edgeDist = s.radius - dist;
            float norm = saturate(edgeDist / max(1.0, s.radius * 0.05));
            float depthFactor = saturate(s.gapDepth);
            float dark = lerp(0.0, 1.0, norm);
            float n = Shape_noise(float2((cx+cy)*0.7 + s.seed, (cx-cy)*0.3 + s.seed), s.seed);
            float irregular = lerp(0.9, 1.0, n);
            finalColor *= lerp(0.5 * (1.0 - depthFactor) + 0.5, 1.0, dark) * irregular;
        }
        else if (s.wear > 0.001f)
        {
            float edgeDist = s.radius - dist;
            float edgeFactor = saturate(edgeDist / max(1.0, s.radius * 0.05));
            float wearBlend = saturate(s.wear) * (1.0 - edgeFactor);
            float lum = (finalColor.x + finalColor.y + finalColor.z) / 3.0;
            finalColor = lerp(finalColor, float3(lum, lum, lum) * 1.05, wearBlend);
        }

        // Composite shapes using premultiplied alpha. Use the standard "src over dst"
        // ordering where the current shape (src) is placed over the accumulated
        // destination. This lets later shapes (for example seams) overwrite
        // earlier shapes when they have full opacity, matching the CPU path.
        float4 outColor = float4(finalColor * alpha, alpha);
        // src over dst: result = src + dst * (1 - src.a)
        accum = outColor + accum * (1.0 - outColor.a);
    }

    accum = saturate(accum);
    // Convert premultiplied alpha -> straight (un-premultiply) for downstream CPU/path
    // The shape compositor uses premultiplied alpha for correct 'over' blending but
    // the rest of the pipeline expects straight (non-premultiplied) color channels.
    // Safeguard against divide-by-zero.
    if (accum.a > 1e-6) { accum.xyz = accum.xyz / accum.a; }
    ShapeOutput[int2(x,y)] = accum;
}

// --- Solid color shader ---
cbuffer SolidColorCB : register(b0)
{
    float4 Color;
};
RWTexture2D<float4> SolidOutput : register(u0);
[numthreads(16,16,1)]
void CS_SolidColorMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint width, height; SolidOutput.GetDimensions(width, height); if (x >= width || y >= height) return; SolidOutput[int2(x,y)] = Color;
}

// --- Gradient shader ---
cbuffer GradientCB : register(b0)
{
    int Mode; int Repeat; int Gradient_Tiling; int Invert_Gradient;
    float R0; float G0; float B0; float R1;
    float G1; float B1; float Offset; float Midpoint;
    float Rotation; int Gradient_Pad0; int Gradient_Pad1; int Gradient_Pad2;
};

RWTexture2D<float4> GradientOutput : register(u0);

float Gradient_bias_func(float v, float m)
{
    float mm = clamp(m, 0.01, 0.99);
    float bias = log(0.5) / log(mm);
    return pow(v, bias);
}

[numthreads(8,8,1)]
void CS_GradientMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w,h; GradientOutput.GetDimensions(w,h);
    if (x >= w || y >= h) return;

    float2 uv = float2(x/(float)(w-1), y/(float)(h-1));
    float nx = uv.x; float ny = uv.y;

    if (abs(Rotation) > 0.001 && (Mode == 0 || Mode == 1 || Mode == 3))
    {
        float cx = nx - 0.5; float cy = ny - 0.5;
        float cr = cos(Rotation); float sr = sin(Rotation);
        nx = cx * cr - cy * sr + 0.5;
        ny = cx * sr + cy * cr + 0.5;
    }

    float t = 0.0;
    if (Mode == 1) t = ny; else if (Mode == 2) { float rcx = (nx - 0.5) * 2.0; float rcy = (ny - 0.5) * 2.0; t = sqrt(rcx*rcx + rcy*rcy); t = clamp(t, 0.0, 1.0); } else if (Mode == 3) t = (nx + ny) * 0.5; else t = nx;

    t = fmod(t + Offset, 1.0);
    if (t < 0.0) t += 1.0;
    t = fmod(t * Repeat, 1.0);
    if (Gradient_Tiling == 1) t = 1.0 - abs(t * 2.0 - 1.0);
    if (abs(Midpoint - 0.5) > 0.001) t = Gradient_bias_func(t, Midpoint);
    if (Invert_Gradient == 1) t = 1.0 - t;

    float3 c0 = float3(R0,G0,B0);
    float3 c1 = float3(R1,G1,B1);
    float3 outc = lerp(c0, c1, t);
    GradientOutput[int2(x,y)] = float4(outc, 1.0);
}

// --- Color adjust shader (Rgb/Hsv helpers) ---
float3 Color_RgbToHsv(float3 c)
{
    float maxc = max(c.x, max(c.y, c.z));
    float minc = min(c.x, min(c.y, c.z));
    float d = maxc - minc; float h = 0.0;
    if (d > 1e-6)
    {
        if (abs(maxc - c.x) < 1e-6) h = fmod((c.y - c.z) / d, 6.0);
        else if (abs(maxc - c.y) < 1e-6) h = (c.z - c.x) / d + 2.0;
        else h = (c.x - c.y) / d + 4.0;
        h /= 6.0; if (h < 0.0) h += 1.0;
    }
    float s = maxc <= 1e-6 ? 0.0 : d / maxc; return float3(h, s, maxc);
}

float3 Color_HsvToRgb(float3 hsv)
{
    float h = hsv.x * 6.0; float s = hsv.y; float v = hsv.z; int i = (int)floor(h) % 6; float f = h - floor(h);
    float p = v * (1.0 - s); float q = v * (1.0 - f * s); float t = v * (1.0 - (1.0 - f) * s);
    if (i == 0) return float3(v, t, p); if (i == 1) return float3(q, v, p); if (i == 2) return float3(p, v, t);
    if (i == 3) return float3(p, q, v); if (i == 4) return float3(t, p, v); return float3(v, p, q);
}

cbuffer ColorAdjustCB : register(b0)
{
    // Prefixed names to avoid collisions when compiling consolidated HLSL
    float ColorAdj_Brightness; float ColorAdj_Contrast; float ColorAdj_Saturation; float ColorAdj_HueShift; float ColorAdj_Gamma;
    float ColorAdj_ColorTemp; float ColorAdj_TintR; float ColorAdj_TintG; float ColorAdj_TintB; float ColorAdj_ShadowClip; float ColorAdj_HighlightClip; float ColorAdj_PaletteSteps; int Invert_ColorAdjust; int ColorAdjust_Width; int ColorAdjust_Height; int ColorAdjust_Pad;
};
Texture2D<float4> ColorSrc : register(t0);
RWTexture2D<float4> ColorOut : register(u0);

[numthreads(8,8,1)]
void CS_ColorAdjustMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w; uint h; ColorOut.GetDimensions(w,h); if (x >= w || y >= h) return;
    float4 v = ColorSrc.Load(int3((int)x,(int)y,0)); float3 c = v.xyz;
    c += ColorAdj_Brightness;
    if (abs(ColorAdj_Contrast) > 1e-6) { float cf = 1.0 + ColorAdj_Contrast; c = (c - 0.5) * cf + 0.5; }
    if (abs(ColorAdj_Gamma - 1.0) > 1e-3) { float invG = 1.0 / max(ColorAdj_Gamma, 0.1); c = pow(saturate(c), float3(invG, invG, invG)); }
    if (abs(ColorAdj_Saturation) > 1e-6) { float lum = dot(c, float3(0.2126,0.7152,0.0722)); c = lum + (c - lum) * (1.0 + ColorAdj_Saturation); }
    if (abs(ColorAdj_HueShift) > 1e-3) { float3 hsv = Color_RgbToHsv(c); hsv.x = frac(hsv.x + ColorAdj_HueShift / 360.0); c = Color_HsvToRgb(hsv); }
    if (abs(ColorAdj_ColorTemp) > 1e-6) { c.x += ColorAdj_ColorTemp * 0.1; c.y += ColorAdj_ColorTemp * 0.02; c.z -= ColorAdj_ColorTemp * 0.1; }
    c *= float3(ColorAdj_TintR, ColorAdj_TintG, ColorAdj_TintB);
    if (ColorAdj_HighlightClip > ColorAdj_ShadowClip + 1e-6) { c = (c - ColorAdj_ShadowClip) / (ColorAdj_HighlightClip - ColorAdj_ShadowClip); }
    if (ColorAdj_PaletteSteps > 1.5) { float steps = max(1.0, ColorAdj_PaletteSteps - 1.0); c = round(c * steps) / steps; }
    c = saturate(c); if (Invert_ColorAdjust == 1) c = 1.0 - c; ColorOut[int2(x,y)] = float4(c, v.w);
}

// --- Convolution shader ---
cbuffer ConvolutionCB : register(b0)
{
    int KernelSize; float Divisor; float Strength; float MixRatio; int PreserveAlpha; int Conv_Width; int Conv_Height; int Convolution_Pad;
};
Texture2D<float4> ConvSrc : register(t0);
RWTexture2D<float4> ConvOut : register(u0);
cbuffer KernelCB : register(b1) { float Kernel[81]; };

[numthreads(8,8,1)]
void CS_ConvolutionMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w; uint h; ConvOut.GetDimensions(w,h); if (x >= w || y >= h) return;
    int ks = KernelSize; int halfK = ks/2; float3 sum = float3(0,0,0);
    for (int ky=-halfK; ky<=halfK; ky++) for (int kx=-halfK; kx<=halfK; kx++) { int sx = (int(x)+kx+(int)w) % (int)w; int sy = (int(y)+ky+(int)h) % (int)h; float4 s = ConvSrc.Load(int3(sx,sy,0)); int idx = (ky+halfK)*ks + (kx+halfK); float kval = Kernel[idx]; sum += s.xyz * kval; }
    if (abs(Divisor) > 1e-6) sum /= Divisor; sum *= Strength; float4 orig = ConvSrc.Load(int3((int)x,(int)y,0)); float3 outc = lerp(orig.xyz, sum, MixRatio); float a = PreserveAlpha==1 ? orig.w : saturate(max(max(abs(sum.x),abs(sum.y)),abs(sum.z))); ConvOut[int2(x,y)] = float4(outc,a);
}

// --- Noise shader (tileable, multiple types) ---
cbuffer NoiseCB : register(b0)
{
    int Seed;
    float Scale;
    int Octaves;
    float Persistence;
    float Lacunarity;
    int NoiseType;
    // Prefixed to avoid symbol collisions
    float Noise_Brightness;
    float Noise_Contrast;
    float OffsetX;
    float OffsetY;
    float ThreshLow;
    float ThreshHigh;
    int Invert_Noise;
    int ColorOutput;
    int TileOffsetX;
    int TileOffsetY;
};
RWTexture2D<float4> NoiseOutput : register(u0);

float Noise_Hash(int2 p, int seed)
{
    uint x = asuint(p.x);
    uint y = asuint(p.y);
    uint v = x * 374761393u + y * 668265263u + 0x9e3779b9u + (uint)seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

float Noise_ValueNoise(float2 p, int period, int seed)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    int2 i00 = int2((int)ip.x % period, (int)ip.y % period);
    int2 i10 = int2((int)(ip.x+1) % period, (int)ip.y % period);
    int2 i01 = int2((int)ip.x % period, (int)(ip.y+1) % period);
    int2 i11 = int2((int)(ip.x+1) % period, (int)(ip.y+1) % period);
    float v00 = Noise_Hash(i00, seed);
    float v10 = Noise_Hash(i10, seed);
    float v01 = Noise_Hash(i01, seed);
    float v11 = Noise_Hash(i11, seed);
    float sx = fp.x*fp.x*(3.0-2.0*fp.x);
    float sy = fp.y*fp.y*(3.0-2.0*fp.y);
    float a = lerp(v00, v10, sx);
    float b = lerp(v01, v11, sx);
    return lerp(a, b, sy);
}

float Noise_FBM(float2 p, int period)
{
    float amp = 1.0;
    float sum = 0.0;
    float freq = 1.0;
    float maxAmp = 0.0;
    for (int i=0;i<Octaves;i++)
    {
        float v = Noise_ValueNoise(p*freq, period, Seed);
        sum += v * amp;
        maxAmp += amp;
        amp *= Persistence;
        freq *= Lacunarity;
    }
    return sum / maxAmp;
}

float Noise_Voronoi(float2 p, int period)
{
    float2 ip = floor(p);
    float best = 1e9;
    for (int oy=-1; oy<=1; oy++)
    for (int ox=-1; ox<=1; ox++)
    {
        int2 cell = int2((int)ip.x+ox, (int)ip.y+oy);
        int2 cellWrapped = int2((cell.x%period+period)%period, (cell.y%period+period)%period);
        float fx = Noise_Hash(cellWrapped, Seed);
        float fy = Noise_Hash(cellWrapped + int2(1,1), Seed);
        float2 fp = float2(fx, fy);
        float2 pos = (float2)cell + fp;
        float d = distance(pos, p);
        if (d < best) best = d;
    }
    return best;
}

[numthreads(8,8,1)]
void CS_NoiseMain(uint3 DTid : SV_DispatchThreadID)
{
    uint lx = DTid.x;
    uint ly = DTid.y;
    uint width, height;
    NoiseOutput.GetDimensions(width, height);

    int gx = int(lx) + TileOffsetX;
    int gy = int(ly) + TileOffsetY;
    if (gx < 0 || gy < 0) return;
    if (gx >= int(width) || gy >= int(height)) return;

    float2 uv = float2(gx/(float)width, gy/(float)height);
    float2 p = (uv + float2(OffsetX, OffsetY)) * Scale;
    int period = max(1, (int)Scale);

    float v = 0.0;
    if (NoiseType == 2)
    {
        v = Noise_Voronoi(p * period, period) / period;
    }
    else
    {
        v = Noise_FBM(p, period);
        if (NoiseType == 3) v = 1.0 - abs(v*2.0 - 1.0);
        else if (NoiseType == 4) v = abs(v*2.0 - 1.0);
    }

    v = clamp(v, 0.0, 1.0);
    if (ThreshHigh > ThreshLow) v = clamp((v - ThreshLow) / (ThreshHigh - ThreshLow), 0.0, 1.0);
    if (abs(Noise_Contrast) > 1e-6) { float cf = 1.0 + Noise_Contrast; v = (v - 0.5) * cf + 0.5; }
    v += Noise_Brightness;
    v = clamp(v, 0.0, 1.0);
    if (Invert_Noise == 1) v = 1.0 - v;

    float3 outc = float3(v,v,v);
    if (ColorOutput == 1)
    {
        float vr = Noise_FBM(p + float2(12.34,45.67), period);
        float vg = Noise_FBM(p + float2(78.9,11.12), period);
        float vb = Noise_FBM(p + float2(33.3,66.6), period);
        vr = clamp(vr + Noise_Brightness, 0.0, 1.0);
        vg = clamp(vg + Noise_Brightness, 0.0, 1.0);
        vb = clamp(vb + Noise_Brightness, 0.0, 1.0);
                        if (Invert_Noise == 1) { vr = 1.0 - vr; vg = 1.0 - vg; vb = 1.0 - vb; }
        outc = float3(vr, vg, vb);
    }

    NoiseOutput[int2(gx,gy)] = float4(outc, 1.0);
}

// --- Fibers shader ---
cbuffer FibersCB : register(b0)
{
    // Prefixed Density to avoid collision with other cbuffers
    float Fibers_Density; float SafeWidth; // per-fiber parameters
    float Fibers_Brightness; float Fibers_Contrast; float CosA; float SinA; int Invert_Fibers; int Fibers_Pad0; int Fibers_Pad1; int Fibers_Pad2;
};
RWTexture2D<float4> FibersOutput : register(u0);

[numthreads(8,8,1)]
void CS_FibersMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w; uint h; FibersOutput.GetDimensions(w,h); if (x >= w || y >= h) return;

    float nx = x / (float)w - 0.5f;
    float ny = y / (float)h - 0.5f;

    float pos = nx * Fibers_Density;
    int nearest = (int)round(pos);
    float localPerp = ny * Fibers_Density;

    float sigmaLong = max(0.0005f, SafeWidth * 0.6f);
    float sigmaPerp = max(0.0005f, SafeWidth * 1.4f);

    float vOut = 0.0f;
    for (int oi = -2; oi <= 2; ++oi)
    {
        float center = nearest + oi;
        float localLong = pos - center;
        float rLong = localLong * CosA - localPerp * SinA;
        float rPerp = localLong * SinA + localPerp * CosA;
        float ndLong = rLong / sigmaLong;
        float ndPerp = rPerp / sigmaPerp;
        float soft = exp(-0.5f * (ndLong*ndLong + ndPerp*ndPerp));
        float core = exp(-0.5f * ((ndLong*0.5f)*(ndLong*0.5f) + (ndPerp*0.5f)*(ndPerp*0.5f)));
        float widthFactor = clamp((SafeWidth - 0.001f) / 1.5f, 0.0f, 1.0f);
        float coreWeight = 0.35f * (1.0f - 0.6f * widthFactor);
        float softWeight = 0.75f * (1.0f - 0.12f * widthFactor);
        float contrib = saturate(soft * softWeight + core * coreWeight);
        vOut = max(vOut, contrib);
    }

    float widthFactorGlobal = clamp((SafeWidth - 0.001f) / 1.5f, 0.0f, 1.0f);
    float peakSoftenBase = 0.45f;
    float peakSoften = peakSoftenBase + 0.5f * widthFactorGlobal;
    float powBase = 1.25f;
    float powExp = powBase + 0.9f * widthFactorGlobal;
    vOut = vOut * (1.0f - peakSoften) + pow(vOut, powExp) * peakSoften;

    if (abs(Fibers_Contrast) > 1e-6) { float factor = 1.0 + Fibers_Contrast; vOut = (vOut - 0.5) * factor + 0.5; }
    vOut += Fibers_Brightness;
    vOut = clamp(vOut, 0.0, 1.0);
    if (Invert_Fibers == 1) vOut = 1.0 - vOut;

    float3 outc = float3(vOut, vOut, vOut);
    FibersOutput[int2(x,y)] = float4(outc, 1.0);
}

// --- Weave shader (simple) ---
cbuffer WeaveCB : register(b0)
{
    int Weave_Density; float Weave_Brightness; float Weave_Contrast; int Invert_Weave;
};
RWTexture2D<float4> WeaveOutput : register(u0);

float gaussian(float x, float sigma) { float nd = x / sigma; return exp(-0.5 * nd * nd); }

[numthreads(8,8,1)]
void CS_WeaveMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w, h; WeaveOutput.GetDimensions(w,h); if (x >= w || y >= h) return;
    float u = x / (float)w;
    float v = y / (float)h;
    float cellU = fmod(u, 0.5) / 0.5;
    float cellV = fmod(v, 0.5) / 0.5;
    int cellX = (int)floor(u / 0.5);
    int cellY = (int)floor(v / 0.5);
    bool vertical = ((cellX + cellY) & 1) == 0;
    float sigmaPerp = max(0.0005, 0.15 * 0.45);
    float capSigma = sigmaPerp * 1.15;
    float vOut = 0.0;
    for (int oi = -2; oi <= 2; ++oi)
    {
        if (vertical)
        {
            float pos = cellU * Weave_Density;
            float nearest = round(pos) + oi;
            float centerX = nearest / (float)Weave_Density;
            float dx = cellU - centerX;
            float ndPerp = dx / sigmaPerp;
            float barMain = exp(-0.5 * ndPerp * ndPerp);
            float distTop = sqrt(dx*dx + (cellV - 0.0)*(cellV - 0.0));
            float distBot = sqrt(dx*dx + (cellV - 1.0)*(cellV - 1.0));
            float capTop = exp(-0.5 * (distTop*distTop) / (capSigma*capSigma));
            float capBot = exp(-0.5 * (distBot*distBot) / (capSigma*capSigma));
            float contrib = max(barMain, max(capTop, capBot));
            vOut = max(vOut, contrib);
        }
        else
        {
            float pos = cellV * Weave_Density;
            float nearest = round(pos) + oi;
            float centerY = nearest / (float)Weave_Density;
            float dy = cellV - centerY;
            float ndPerp = dy / sigmaPerp;
            float barMain = exp(-0.5 * ndPerp * ndPerp);
            float distLeft = sqrt(dy*dy + (cellU - 0.0)*(cellU - 0.0));
            float distRight = sqrt(dy*dy + (cellU - 1.0)*(cellU - 1.0));
            float capLeft = exp(-0.5 * (distLeft*distLeft) / (capSigma*capSigma));
            float capRight = exp(-0.5 * (distRight*distRight) / (capSigma*capSigma));
            float contrib = max(barMain, max(capLeft, capRight));
            vOut = max(vOut, contrib);
        }
    }
    float widthFactorGlobal = clamp((0.15 - 0.001) / 1.5, 0.0, 1.0);
    float peakSoftenBase = 0.45;
    float peakSoften = peakSoftenBase + 0.5 * widthFactorGlobal;
    float powBase = 1.25;
    float powExp = powBase + 0.9 * widthFactorGlobal;
    vOut = vOut * (1.0 - peakSoften) + pow(vOut, powExp) * peakSoften;
    if (abs(Weave_Contrast) > 1e-6) { float factor = 1.0 + Weave_Contrast; vOut = (vOut - 0.5) * factor + 0.5; }
    vOut += Weave_Brightness;
    vOut = clamp(vOut, 0.0, 1.0);
    if (Invert_Weave == 1) vOut = 1.0 - vOut;
    float3 outc = float3(vOut, vOut, vOut);
    WeaveOutput[int2(x,y)] = float4(outc, 1.0);
}

// --- Benchmark fill shader ---
cbuffer BenchCB : register(b0) { int Bench_Width; int Bench_Height; int Bench_Pad0; int Bench_Pad1; };
RWTexture2D<float4> BenchOutput : register(u0);
[numthreads(16,16,1)]
void CS_BenchmarkMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w = (uint)Bench_Width; uint h = (uint)Bench_Height; if (x >= w || y >= h) return; BenchOutput[int2(x,y)] = float4(0.5, 0.25, 0.75, 1.0);
}

// --- Checkerboard shader ---
cbuffer CheckerboardCB : register(b0)
{
    int Checker_Cells;
    float Checker_AR; float Checker_AG; float Checker_AB;
    float Checker_BR; float Checker_BG; float Checker_BB;
    int Checker_Invert;
};
RWTexture2D<float4> CheckerOutput : register(u0);

[numthreads(8,8,1)]
void CS_CheckerboardMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y; uint w, h;
    CheckerOutput.GetDimensions(w, h);
    if (x >= w || y >= h) return;

    // Sample at pixel centers to match CPU behavior and avoid asymmetric artifacts.
    float u = (x + 0.5) / (float)w;
    float v = (y + 0.5) / (float)h;

    int cx = (int)floor(u * Checker_Cells);
    int cy = (int)floor(v * Checker_Cells);
    int parity = (cx + cy) & 1;
    if (Checker_Invert == 1) parity = 1 - parity;

    float r = parity == 0 ? Checker_AR : Checker_BR;
    float g = parity == 0 ? Checker_AG : Checker_BG;
    float b = parity == 0 ? Checker_AB : Checker_BB;

    CheckerOutput[int2(x, y)] = float4(saturate(r), saturate(g), saturate(b), 1.0);
}

// --- Post-process shader (CS_PostProcessMain) ---
// In-place brightness/contrast/threshold/invert adjustment on a UAV texture.
cbuffer PostProcessCB : register(b0)
{
    float PP_Brightness;
    float PP_Contrast;
    float PP_ThreshLow;
    float PP_ThreshHigh;
    int PP_Invert;
    int PP_ColorOutput;
    int PP_Width;
    int PP_Height;
};

RWTexture2D<float4> PPTex : register(u0);

[numthreads(8,8,1)]
void CS_PostProcessMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x;
    uint y = DTid.y;
    if (x >= (uint)PP_Width || y >= (uint)PP_Height) return;

    float4 v = PPTex[int2(x,y)];
    float3 c = v.xyz;

    if (PP_ColorOutput == 0)
    {
        float val = c.x;
        if (PP_ThreshHigh > PP_ThreshLow)
        {
            val = (val - PP_ThreshLow) / (PP_ThreshHigh - PP_ThreshLow);
        }
        if (abs(PP_Contrast) > 1e-6) { float cf = 1.0 + PP_Contrast; val = (val - 0.5) * cf + 0.5; }
        val += PP_Brightness;
        val = clamp(val, 0.0, 1.0);
        if (PP_Invert == 1) val = 1.0 - val;
        c = float3(val, val, val);
    }
    else
    {
        for (int i = 0; i < 3; i++)
        {
            float vch = c[i];
            if (PP_ThreshHigh > PP_ThreshLow)
            {
                vch = (vch - PP_ThreshLow) / (PP_ThreshHigh - PP_ThreshLow);
            }
            if (abs(PP_Contrast) > 1e-6) { float cf = 1.0 + PP_Contrast; vch = (vch - 0.5) * cf + 0.5; }
            vch += PP_Brightness;
            vch = clamp(vch, 0.0, 1.0);
            if (PP_Invert == 1) vch = 1.0 - vch;
            c[i] = vch;
        }
    }

    PPTex[int2(x,y)] = float4(c, 1.0);
}

// --- Seamless blend shader (CS_SeamlessBlendMain) ---
// Blends input image with its half-offset wrap for seamless tiling.
cbuffer SeamlessCB : register(b0)
{
    float SB_BlendWidth;
    int SB_BlendShape;
    int SB_BlendDirection;
    float SB_BlendStrength;
    int SB_ShowSeam;
    int SB_Width;
    int SB_Height;
    int SB_Pad;
};

Texture2D<float4> SBSrc : register(t0);
RWTexture2D<float4> SBOut : register(u0);

[numthreads(8,8,1)]
void CS_SeamlessBlendMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)SB_Width || y >= (uint)SB_Height) return;

    float cx, cy, edgeDistance;
    if (SB_BlendDirection == 1) { cx = abs((float)x - SB_Width*0.5 + 0.5) / (SB_Width*0.5); edgeDistance = cx; }
    else if (SB_BlendDirection == 2) { cy = abs((float)y - SB_Height*0.5 + 0.5) / (SB_Height*0.5); edgeDistance = cy; }
    else { cx = abs((float)x - SB_Width*0.5 + 0.5) / (SB_Width*0.5); cy = abs((float)y - SB_Height*0.5 + 0.5) / (SB_Height*0.5); edgeDistance = max(cx,cy); }

    float blendStart = 1.0 - SB_BlendWidth * 2.0;
    float t;
    if (edgeDistance <= blendStart) t = 0.0;
    else if (edgeDistance >= 1.0) t = 1.0;
    else
    {
        float normalized = (edgeDistance - blendStart) / (1.0 - blendStart);
        if (SB_BlendShape == 1) t = 0.5 - 0.5 * cos(normalized * 3.14159265);
        else if (SB_BlendShape == 2) t = 1.0 - exp(-normalized*normalized*4.0);
        else if (SB_BlendShape == 3) t = normalized*normalized*(3.0 - 2.0*normalized);
        else t = normalized;
    }
    t = clamp(t * SB_BlendStrength, 0.0, 1.0);

    int half_w = SB_Width/2;
    int half_h = SB_Height/2;
    int ox = ((int)x + half_w) % SB_Width;
    int oy = ((int)y + half_h) % SB_Height;
    if (ox < 0) ox += SB_Width;
    if (oy < 0) oy += SB_Height;

    float4 a = SBSrc.Load(int3((int)x, (int)y, 0));
    float4 b = SBSrc.Load(int3(ox, oy, 0));
    float4 outc = lerp(a, b, t);

    if (SB_ShowSeam == 1)
    {
        float edgePixels = 1.0 / SB_Width;
        if (abs(edgeDistance - blendStart) < edgePixels * 2.0)
        {
            outc = float4(1,0,0,1);
        }
    }

    SBOut[int2(x,y)] = outc;
}

// --- Distortion shader (CS_DistortMain) ---
// Displaces pixels using noise, swirl, wave, pinch, or polar distortion.
cbuffer DistortCB : register(b0)
{
    int Dist_Width;
    int Dist_Height;
    int Dist_DistortType;
    float Dist_Strength;
    float Dist_Frequency;
    int Dist_Octaves;
    float Dist_XStrength;
    float Dist_YStrength;
    float Dist_Angle;
    float Dist_CenterX;
    float Dist_CenterY;
    int Dist_Seed;
};

Texture2D<float4> DistSrc : register(t0);
RWTexture2D<float4> DistOut : register(u0);

float Dist_Hash(int2 p)
{
    uint hx = asuint(p.x);
    uint hy = asuint(p.y);
    uint v = hx * 374761393u + hy * 668265263u + 0x9e3779b9u + (uint)Dist_Seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

float Dist_ValueNoise(float2 p)
{
    int period = max(1, (int)Dist_Frequency);
    float2 ip = floor(p);
    float2 fp = frac(p);
    int2 i00 = int2(((int)ip.x) % period, ((int)ip.y) % period);
    int2 i10 = int2((((int)ip.x)+1) % period, ((int)ip.y) % period);
    int2 i01 = int2(((int)ip.x) % period, (((int)ip.y)+1) % period);
    int2 i11 = int2((((int)ip.x)+1) % period, (((int)ip.y)+1) % period);
    float v00 = Dist_Hash(i00);
    float v10 = Dist_Hash(i10);
    float v01 = Dist_Hash(i01);
    float v11 = Dist_Hash(i11);
    float sx = fp.x*fp.x*(3.0-2.0*fp.x);
    float sy = fp.y*fp.y*(3.0-2.0*fp.y);
    float la = lerp(v00, v10, sx);
    float lb = lerp(v01, v11, sx);
    return lerp(la, lb, sy);
}

[numthreads(8,8,1)]
void CS_DistortMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Dist_Width || y >= (uint)Dist_Height) return;

    float2 uv = float2(x/(float)Dist_Width, y/(float)Dist_Height);
    float2 p = uv * Dist_Frequency;
    float dx = 0.0; float dy = 0.0;

    if (Dist_DistortType == 1) // swirl
    {
        float relx = uv.x - Dist_CenterX; float rely = uv.y - Dist_CenterY;
        float dist = sqrt(relx*relx + rely*rely);
        float swirl = dist * Dist_Strength * 40.0;
        float ca = cos(swirl); float sa = sin(swirl);
        float sx = relx * ca - rely * sa; float sy = relx * sa + rely * ca;
        dx = (sx - relx) * Dist_Width; dy = (sy - rely) * Dist_Height;
    }
    else if (Dist_DistortType == 2) // wave
    {
        float relx = uv.x - Dist_CenterX; float rely = uv.y - Dist_CenterY;
        float dist = sqrt(relx*relx + rely*rely);
        float wave = sin(dist * Dist_Frequency * 6.28318 + Dist_Angle) * Dist_Strength;
        if (dist > 0.0001) { dx = wave * (relx / dist) * Dist_Width; dy = wave * (rely / dist) * Dist_Height; }
    }
    else if (Dist_DistortType == 3) // pinch
    {
        float relx = uv.x - Dist_CenterX; float rely = uv.y - Dist_CenterY;
        float dist = sqrt(relx*relx + rely*rely);
        float factor = pow(dist, 1.0 + Dist_Strength*5.0) / max(dist, 0.001);
        dx = (relx * factor - relx) * Dist_Width; dy = (rely * factor - rely) * Dist_Height;
    }
    else if (Dist_DistortType == 4) // polar
    {
        float relx = uv.x - Dist_CenterX; float rely = uv.y - Dist_CenterY;
        float theta = atan2(rely, relx);
        float dist = sqrt(relx*relx + rely*rely);
        float polarX = (theta / 6.28318 + 0.5) * Dist_Width;
        float polarY = dist * 2.0 * Dist_Height;
        dx = (polarX - x) * Dist_Strength * 2.0; dy = (polarY - y) * Dist_Strength * 2.0;
    }
    else // noise
    {
        float n = 0.0; float amp = 1.0; float freqLocal = 1.0; float maxAmp = 0.0;
        for (int o = 0; o < Dist_Octaves; o++)
        {
            n += Dist_ValueNoise(p * freqLocal) * amp;
            maxAmp += amp; amp *= 0.5; freqLocal *= 2.0;
        }
        n = n / maxAmp - 0.5;
        dx = n * Dist_Strength * Dist_Width * Dist_XStrength;
        dy = n * Dist_Strength * Dist_Height * Dist_YStrength;
    }

    int sx = ((int)(x + dx) % Dist_Width + Dist_Width) % Dist_Width;
    int sy = ((int)(y + dy) % Dist_Height + Dist_Height) % Dist_Height;
    float4 col = DistSrc.Load(int3(sx, sy, 0));
    DistOut[int2(x,y)] = col;
}

// --- Pixelate shader (CS_PixelateMain) ---
// Downsamples and re-upsamples for pixel-art look with optional ordered dithering.
cbuffer PixelateCB : register(b0)
{
    int Pix_BlockSize;
    int Pix_SampleMode;
    int Pix_PaletteSteps;
    int Pix_DitherMode;
    int Pix_Width;
    int Pix_Height;
    int Pix_Pad0;
    int Pix_Pad1;
};

Texture2D<float4> PixSrc : register(t0);
RWTexture2D<float4> PixDst : register(u0);

float4 Pix_Quantize(float4 c, int steps)
{
    if (steps <= 1) return c;
    float s = (float)(steps - 1);
    c.xyz = round(c.xyz * s) / s;
    return c;
}

float Pix_DitherValue(int x, int y)
{
    int ix = x & 3;
    int iy = y & 3;
    // 4x4 Bayer matrix
    int m[16] = { 0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5 };
    return m[iy * 4 + ix] / 16.0;
}

[numthreads(8,8,1)]
void CS_PixelateMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x;
    uint y = DTid.y;
    if (x >= (uint)Pix_Width || y >= (uint)Pix_Height) return;

    int bx = (int)x / Pix_BlockSize;
    int by = (int)y / Pix_BlockSize;
    int cx = bx * Pix_BlockSize + Pix_BlockSize / 2;
    int cy = by * Pix_BlockSize + Pix_BlockSize / 2;
    cx = clamp(cx, 0, Pix_Width - 1);
    cy = clamp(cy, 0, Pix_Height - 1);

    float4 col;
    if (Pix_SampleMode == 1)
    {
        int sx = bx * Pix_BlockSize;
        int sy = by * Pix_BlockSize;
        sx = clamp(sx, 0, Pix_Width - 1);
        sy = clamp(sy, 0, Pix_Height - 1);
        col = PixSrc.Load(int3(sx, sy, 0));
    }
    else
    {
        col = PixSrc.Load(int3(cx, cy, 0));
    }

    if (Pix_DitherMode == 1 && Pix_PaletteSteps > 1)
    {
        float dv = Pix_DitherValue((int)x, (int)y);
        float s = (float)(Pix_PaletteSteps - 1);
        col.xyz = clamp(col.xyz + (dv - 0.5) / s, 0.0, 1.0);
    }

    col = Pix_Quantize(col, Pix_PaletteSteps);
    PixDst[int2(x,y)] = float4(col.xyz, 1.0);
}

// --- Lattice shader (CS_LatticeMain) ---
cbuffer LatticeCB : register(b0)
{
    float Lat_Scale;
    float Lat_Thickness;
    float Lat_Rotation;
    float Lat_Softness;
    int Lat_Invert;
    int Lat_Width;
    int Lat_Height;
    int Lat_Pad;
};
RWTexture2D<float4> LatticeOutput : register(u0);

[numthreads(8,8,1)]
void CS_LatticeMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Lat_Width || y >= (uint)Lat_Height) return;

    float u = (x + 0.5) / (float)Lat_Width - 0.5;
    float v = (y + 0.5) / (float)Lat_Height - 0.5;

    float ca = cos(Lat_Rotation);
    float sa = sin(Lat_Rotation);
    float ru = u * ca - v * sa;
    float rv = u * sa + v * ca;

    float du = abs(ru) * Lat_Scale;
    float dv = abs(rv) * Lat_Scale;
    float duMod = frac(du);
    float dvMod = frac(dv);

    float duDist = min(duMod, 1.0 - duMod);
    float dvDist = min(dvMod, 1.0 - dvMod);
    float dist = min(duDist, dvDist);

    float val = 1.0 - smoothstep(0.0, Lat_Thickness, dist);
    val = lerp(val, 1.0, Lat_Softness);

    if (Lat_Invert == 1) val = 1.0 - val;
    LatticeOutput[int2(x,y)] = float4(val, val, val, 1.0);
}

// --- Concentric shader (CS_ConcentricMain) ---
cbuffer ConcentricCB : register(b0)
{
    float Conc_Count;
    float Conc_Thickness;
    float Conc_Distortion;
    float Conc_CenterX;
    float Conc_CenterY;
    int Conc_Invert;
    int Conc_Width;
    int Conc_Height;
    int Conc_Smooth;
};
RWTexture2D<float4> ConcentricOutput : register(u0);

[numthreads(8,8,1)]
void CS_ConcentricMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Conc_Width || y >= (uint)Conc_Height) return;

    float u = (x + 0.5) / (float)Conc_Width;
    float v = (y + 0.5) / (float)Conc_Height;

    float cx = u - Conc_CenterX;
    float cy = v - Conc_CenterY;
    float dist = sqrt(cx*cx + cy*cy);

    // Noise distortion
    if (Conc_Distortion > 0.001)
    {
        float n = frac(sin(dot(float2(cx,cy) * 12.0, float2(127.1,311.7))) * 43758.5453);
        dist += (n - 0.5) * Conc_Distortion * 0.1;
    }

    float ring = frac(dist * Conc_Count);
    float val;
    if (Conc_Smooth == 1)
        val = 1.0 - smoothstep(0.0, Conc_Thickness, min(ring, 1.0 - ring) * 2.0);
    else
        val = ring < Conc_Thickness ? 1.0 : 0.0;

    if (Conc_Invert == 1) val = 1.0 - val;
    ConcentricOutput[int2(x,y)] = float4(val, val, val, 1.0);
}

// --- Spiral shader (CS_SpiralMain) ---
cbuffer SpiralCB : register(b0)
{
    float Spir_Arms;
    float Spir_Turns;
    float Spir_LineWidth;
    float Spir_Distortion;
    int Spir_Type; // 0=archimedean, 1=logarithmic
    int Spir_Invert;
    int Spir_Width;
    int Spir_Height;
    int Spir_Seed;
};
RWTexture2D<float4> SpiralOutput : register(u0);

float Spir_Hash(int2 p, int seed)
{
    uint v = asuint(p.x) * 374761393u + asuint(p.y) * 668265263u + 0x9e3779b9u + (uint)seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

[numthreads(8,8,1)]
void CS_SpiralMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Spir_Width || y >= (uint)Spir_Height) return;

    float u = (x + 0.5) / (float)Spir_Width * 2.0 - 1.0;
    float v = (y + 0.5) / (float)Spir_Height * 2.0 - 1.0;

    float r = sqrt(u*u + v*v);
    float theta = atan2(v, u);

    float spiralVal;
    if (Spir_Type == 1)
        theta += Spir_Arms * log(r + 0.001) * Spir_Turns;
    else
        theta = (r * Spir_Turns * 6.28318 - theta * Spir_Arms) * Spir_Arms;

    float dTheta = abs(frac(theta / 6.28318) - 0.5) * 2.0;
    spiralVal = 1.0 - smoothstep(0.0, Spir_LineWidth, dTheta);

    float fade = 1.0 - smoothstep(0.7, 1.0, r);
    spiralVal *= fade;

    if (Spir_Invert == 1) spiralVal = 1.0 - spiralVal;
    SpiralOutput[int2(x,y)] = float4(spiralVal, spiralVal, spiralVal, 1.0);
}

// --- Honeycomb shader (CS_HoneycombMain) ---
cbuffer HoneycombCB : register(b0)
{
    float Honey_Scale;
    float Honey_WallThick;
    float Honey_Bevel;
    float Honey_R;
    float Honey_G;
    float Honey_B;
    float Honey_WallR;
    float Honey_WallG;
    float Honey_WallB;
    int Honey_Invert;
    int Honey_Width;
    int Honey_Height;
};
RWTexture2D<float4> HoneycombOutput : register(u0);

[numthreads(8,8,1)]
void CS_HoneycombMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Honey_Width || y >= (uint)Honey_Height) return;

    float u = (x + 0.5) / (float)Honey_Width;
    float v = (y + 0.5) / (float)Honey_Height;

    float hx = Honey_Scale * 1.5;
    float hy = Honey_Scale * 1.73205;

    float q = u / hx;
    float r2 = v / hy - q * 0.5;

    float qr = round(q);
    float rr = round(r2);

    float dq = q - qr;
    float dr = r2 - rr;

    if (abs(dq) + abs(dr) > 0.5)
    {
        if (abs(dq) < abs(dr)) qr += 0.5; else rr += 0.5;
    }

    float distQ = u / hx - qr;
    float distR = v / hy - qr * 0.5 - rr;
    float cx = distQ * hx;
    float cy = distR * hy;
    float dist = sqrt(cx*cx + cy*cy);

    float edgeDist = (Honey_Scale - dist) / Honey_Scale;

    float3 innerColor = float3(Honey_R, Honey_G, Honey_B);
    float3 wallColor = float3(Honey_WallR, Honey_WallG, Honey_WallB);
    float3 outColor;

    if (edgeDist > Honey_WallThick)
    {
        outColor = innerColor;
        if (Honey_Bevel > 0.001 && edgeDist < Honey_WallThick + Honey_Scale * Honey_Bevel * 0.3)
        {
            float bf = (edgeDist - Honey_WallThick) / (Honey_Scale * Honey_Bevel * 0.3);
            bf = clamp(bf, 0.0, 1.0);
            outColor *= (1.0 - bf * 0.2);
        }
    }
    else
    {
        outColor = wallColor;
    }

    if (Honey_Invert == 1) outColor = 1.0 - outColor;
    HoneycombOutput[int2(x,y)] = float4(outColor, 1.0);
}

// --- Wave shader (CS_WaveMain) ---
cbuffer WaveCB : register(b0)
{
    int Wave_Type; // 0=sine, 1=square, 2=triangle, 3=sawtooth, 4=ripple
    float Wave_FreqX;
    float Wave_FreqY;
    float Wave_Amp;
    float Wave_Phase;
    float Wave_Sharpness;
    int Wave_Invert;
    int Wave_Width;
    int Wave_Height;
    int Wave_Seed;
};
RWTexture2D<float4> WaveOutput : register(u0);

[numthreads(8,8,1)]
void CS_WaveMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Wave_Width || y >= (uint)Wave_Height) return;

    float u = (x + 0.5) / (float)Wave_Width;
    float v = (y + 0.5) / (float)Wave_Height;

    float val = 0.0;
    float t = u * Wave_FreqX * 6.28318 + v * Wave_FreqY * 6.28318 + Wave_Phase;

    if (Wave_Type == 4) // ripple
    {
        float ru = u * 2.0 - 1.0;
        float rv = v * 2.0 - 1.0;
        float d = sqrt(ru*ru + rv*rv);
        val = sin(d * Wave_FreqX * 6.28318 + Wave_Phase) * Wave_Amp;
        val = val * 0.5 + 0.5;
    }
    else if (Wave_Type == 1) // square
    {
        val = sin(t) > 0.0 ? 1.0 : -1.0;
        if (Wave_Sharpness < 1.0)
        {
            float soft = abs(sin(t));
            float blend = pow(soft, 1.0 - Wave_Sharpness * 0.9);
            val *= blend;
        }
        val = val * Wave_Amp * 0.5 + 0.5;
    }
    else if (Wave_Type == 2) // triangle
    {
        float norm = t / 6.28318;
        float fracN = norm - floor(norm);
        val = fracN < 0.25 ? fracN * 4.0 :
              fracN < 0.75 ? 2.0 - fracN * 4.0 :
              fracN * 4.0 - 4.0;
        val = val * Wave_Amp * 0.5 + 0.5;
    }
    else if (Wave_Type == 3) // sawtooth
    {
        float norm = t / 6.28318;
        float fracN = norm - floor(norm);
        val = 1.0 - fracN * 2.0;
        val = val * Wave_Amp * 0.5 + 0.5;
    }
    else // sine
    {
        val = sin(t) * Wave_Amp * 0.5 + 0.5;
    }

    val = clamp(val, 0.0, 1.0);
    if (Wave_Invert == 1) val = 1.0 - val;
    WaveOutput[int2(x,y)] = float4(val, val, val, 1.0);
}

// --- Normal map shader (CS_NormalMapMain) ---
cbuffer NormalMapCB : register(b0)
{
    float NM_Strength;
    int NM_FlipX;
    int NM_FlipY;
    int NM_Width;
    int NM_Height;
    int NM_Pad0;
    int NM_Pad1;
    int NM_Pad2;
};
Texture2D<float4> NMSrc : register(t0);
RWTexture2D<float4> NMOut : register(u0);

float NM_SampleHeight(Texture2D<float4> tex, int x, int y, int w, int h)
{
    x = clamp(x, 0, w-1);
    y = clamp(y, 0, h-1);
    float4 c = tex.Load(int3(x, y, 0));
    return c.x * 0.299 + c.y * 0.587 + c.z * 0.114;
}

[numthreads(8,8,1)]
void CS_NormalMapMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)NM_Width || y >= (uint)NM_Height) return;

    float hL = NM_SampleHeight(NMSrc, (int)x-1, (int)y, NM_Width, NM_Height);
    float hR = NM_SampleHeight(NMSrc, (int)x+1, (int)y, NM_Width, NM_Height);
    float hD = NM_SampleHeight(NMSrc, (int)x, (int)y-1, NM_Width, NM_Height);
    float hU = NM_SampleHeight(NMSrc, (int)x, (int)y+1, NM_Width, NM_Height);

    float gx = (hR - hL) * NM_Strength;
    float gy = (hU - hD) * NM_Strength;
    if (NM_FlipX == 1) gx = -gx;
    if (NM_FlipY == 1) gy = -gy;

    float len = sqrt(gx*gx + gy*gy + 1.0);
    float nx = gx / len * 0.5 + 0.5;
    float ny = gy / len * 0.5 + 0.5;
    float nz = (1.0 / len) * 0.5 + 0.5;

    NMOut[int2(x,y)] = float4(nx, ny, nz, 1.0);
}

// --- Outline shader (CS_OutlineMain) ---
cbuffer OutlineCB : register(b0)
{
    int OL_Mode; // 0=external, 1=internal, 2=both, 3=glow
    float OL_Thickness;
    float OL_Strength;
    float OL_R;
    float OL_G;
    float OL_B;
    int OL_Width;
    int OL_Height;
    int OL_Pad;
};
Texture2D<float4> OLSrc : register(t0);
RWTexture2D<float4> OLOut : register(u0);

float OL_Luminance(float4 c)
{
    return c.x * 0.299 + c.y * 0.587 + c.z * 0.114;
}

[numthreads(8,8,1)]
void CS_OutlineMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)OL_Width || y >= (uint)OL_Height) return;

    // Sobel edge detection
    float sobelX = 0.0, sobelY = 0.0;
    float kx[9] = { -1,0,1, -2,0,2, -1,0,1 };
    float ky[9] = { -1,-2,-1, 0,0,0, 1,2,1 };

    for (int oy = -1; oy <= 1; oy++)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            int sx = clamp((int)x + ox, 0, OL_Width-1);
            int sy = clamp((int)y + oy, 0, OL_Height-1);
            float4 s = OLSrc.Load(int3(sx, sy, 0));
            float lum = OL_Luminance(s);
            int idx = (oy+1)*3 + (ox+1);
            sobelX += lum * kx[idx];
            sobelY += lum * ky[idx];
        }
    }

    float edge = sqrt(sobelX*sobelX + sobelY*sobelY);
    edge = clamp(edge * OL_Strength, 0.0, 1.0);

    float4 orig = OLSrc.Load(int3((int)x, (int)y, 0));
    float4 outColor;

    if (OL_Mode == 3) // glow
    {
        outColor = float4(OL_R, OL_G, OL_B, edge);
    }
    else if (OL_Mode == 1) // internal
    {
        float3 outline = float3(OL_R, OL_G, OL_B);
        float3 blended = lerp(orig.xyz, outline, edge);
        outColor = float4(blended, orig.w);
    }
    else // external or both
    {
        float3 outline = float3(OL_R, OL_G, OL_B);
        float3 blended = lerp(orig.xyz, outline, edge);
        outColor = float4(blended, orig.w);
    }

    OLOut[int2(x,y)] = outColor;
}

// --- Wood shader (CS_WoodMain) ---
cbuffer WoodCB : register(b0)
{
    float Wood_Density;
    float Wood_Distortion;
    float Wood_Sharpness;
    float Wood_R1;
    float Wood_G1;
    float Wood_B1;
    float Wood_R2;
    float Wood_G2;
    float Wood_B2;
    int Wood_Invert;
    int Wood_Width;
    int Wood_Height;
    int Wood_Seed;
};
RWTexture2D<float4> WoodOutput : register(u0);

float Wood_Hash(float2 p, int seed)
{
    uint v = asuint(p.x * 100.0) * 374761393u + asuint(p.y * 100.0) * 668265263u + 0x9e3779b9u + (uint)seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

float Wood_Noise(float2 p, int seed)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Wood_Hash(i, seed);
    float b = Wood_Hash(i + float2(1,0), seed);
    float c = Wood_Hash(i + float2(0,1), seed);
    float d = Wood_Hash(i + float2(1,1), seed);
    float2 u = f*f*(3.0-2.0*f);
    return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
}

[numthreads(8,8,1)]
void CS_WoodMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Wood_Width || y >= (uint)Wood_Height) return;

    float u = (x + 0.5) / (float)Wood_Width;
    float v = (y + 0.5) / (float)Wood_Height;

    float cx = u - 0.5;
    float cy = v - 0.5;

    // Concentric rings with angular perturbation
    float angle = atan2(cy, cx);
    float radius = sqrt(cx*cx + cy*cy) * 2.0;
    float perturb = Wood_Noise(float2(cos(angle)*0.5+0.5, sin(angle)*0.5+0.5)*4.0 + Wood_Seed, Wood_Seed+10) * Wood_Distortion;
    float ring = radius * Wood_Density + perturb;

    float ringFrac = frac(ring);
    float ringVal = smoothstep(0.0, 1.0 - Wood_Sharpness, ringFrac);

    // Knots
    float knot = Wood_Noise(float2(cx*8+0.5, cy*8+0.5)*2.0, Wood_Seed+20);
    float knotMask = smoothstep(0.7, 0.95, knot);
    ringVal = lerp(ringVal, ringVal * 0.3 + 0.7, knotMask);

    float3 col1 = float3(Wood_R1, Wood_G1, Wood_B1);
    float3 col2 = float3(Wood_R2, Wood_G2, Wood_B2);
    float3 outColor = lerp(col1, col2, ringVal);

    if (Wood_Invert == 1) outColor = 1.0 - outColor;
    WoodOutput[int2(x,y)] = float4(outColor, 1.0);
}

// --- Cloud shader (CS_CloudMain) ---
cbuffer CloudCB : register(b0)
{
    float Cloud_Scale;
    float Cloud_Density;
    float Cloud_Sharpness;
    float Cloud_Coverage;
    float Cloud_Detail;
    int Cloud_Octaves;
    float Cloud_SkyR;
    float Cloud_SkyG;
    float Cloud_SkyB;
    float Cloud_CloudR;
    float Cloud_CloudG;
    float Cloud_CloudB;
    int Cloud_Invert;
    int Cloud_Width;
    int Cloud_Height;
    int Cloud_Seed;
};
RWTexture2D<float4> CloudOutput : register(u0);

float Cloud_Hash(int2 p, int seed)
{
    uint v = asuint(p.x) * 374761393u + asuint(p.y) * 668265263u + 0x9e3779b9u + (uint)seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

float Cloud_Noise(float2 p, int period, int seed)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    int2 i00 = int2(((int)ip.x) % period, ((int)ip.y) % period);
    int2 i10 = int2((((int)ip.x)+1) % period, ((int)ip.y) % period);
    int2 i01 = int2(((int)ip.x) % period, (((int)ip.y)+1) % period);
    int2 i11 = int2((((int)ip.x)+1) % period, (((int)ip.y)+1) % period);
    float v00 = Cloud_Hash(i00, seed);
    float v10 = Cloud_Hash(i10, seed);
    float v01 = Cloud_Hash(i01, seed);
    float v11 = Cloud_Hash(i11, seed);
    float sx = fp.x*fp.x*(3.0-2.0*fp.x);
    float sy = fp.y*fp.y*(3.0-2.0*fp.y);
    return lerp(lerp(v00, v10, sx), lerp(v01, v11, sx), sy);
}

float Cloud_FBM(float2 p, int period, int octaves, int seed)
{
    float amp = 1.0, sum = 0.0, freq = 1.0, maxA = 0.0;
    for (int i = 0; i < octaves; i++)
    {
        sum += Cloud_Noise(p * freq, period, seed + i * 101) * amp;
        maxA += amp; amp *= 0.5; freq *= 2.0;
    }
    return sum / maxA;
}

[numthreads(8,8,1)]
void CS_CloudMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Cloud_Width || y >= (uint)Cloud_Height) return;

    float u = (x + 0.5) / (float)Cloud_Width;
    float v = (y + 0.5) / (float)Cloud_Height;

    float2 p = float2(u, v) * Cloud_Scale;
    int period = max(1, (int)Cloud_Scale);

    // Low frequency base
    float base = Cloud_FBM(p, period, Cloud_Octaves, Cloud_Seed);
    // High frequency detail
    float detail = Cloud_FBM(p * 2.0 + 50.0, period * 2, max(1, Cloud_Octaves-1), Cloud_Seed + 100);

    float cloud = base * (1.0 - Cloud_Detail * 0.3) + detail * Cloud_Detail * 0.3;
    cloud = smoothstep(1.0 - Cloud_Coverage, 2.0 - Cloud_Coverage, cloud);
    cloud = pow(cloud, 1.0 + (1.0 - Cloud_Sharpness) * 2.0);

    float3 skyColor = float3(Cloud_SkyR, Cloud_SkyG, Cloud_SkyB);
    float3 cloudColor = float3(Cloud_CloudR, Cloud_CloudG, Cloud_CloudB);
    float3 outColor = lerp(skyColor, cloudColor, cloud * Cloud_Density);

    if (Cloud_Invert == 1) outColor = 1.0 - outColor;
    CloudOutput[int2(x,y)] = float4(outColor, 1.0);
}

// --- Marble shader (CS_MarbleMain) ---
cbuffer MarbleCB : register(b0)
{
    float Marble_Scale;
    float Marble_Freq;
    float Marble_Sharpness;
    float Marble_Distortion;
    int Marble_Octaves;
    float Marble_R1;
    float Marble_G1;
    float Marble_B1;
    float Marble_R2;
    float Marble_G2;
    float Marble_B2;
    int Marble_Invert;
    int Marble_Width;
    int Marble_Height;
    int Marble_Seed;
};
RWTexture2D<float4> MarbleOutput : register(u0);

float Marble_Hash(int2 p, int seed)
{
    uint v = asuint(p.x) * 374761393u + asuint(p.y) * 668265263u + 0x9e3779b9u + (uint)seed;
    v = (v ^ (v >> 13)) * 1274126177u;
    return frac(v * 2.3283064365387e-10);
}

float Marble_Noise(float2 p, int period, int seed)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    int2 i00 = int2(((int)ip.x) % period, ((int)ip.y) % period);
    int2 i10 = int2((((int)ip.x)+1) % period, ((int)ip.y) % period);
    int2 i01 = int2(((int)ip.x) % period, (((int)ip.y)+1) % period);
    int2 i11 = int2((((int)ip.x)+1) % period, (((int)ip.y)+1) % period);
    float v00 = Marble_Hash(i00, seed);
    float v10 = Marble_Hash(i10, seed);
    float v01 = Marble_Hash(i01, seed);
    float v11 = Marble_Hash(i11, seed);
    float sx = fp.x*fp.x*(3.0-2.0*fp.x);
    float sy = fp.y*fp.y*(3.0-2.0*fp.y);
    return lerp(lerp(v00, v10, sx), lerp(v01, v11, sx), sy);
}

float Marble_FBM(float2 p, int period, int octaves, int seed)
{
    float amp = 1.0, sum = 0.0, freq = 1.0, maxA = 0.0;
    for (int i = 0; i < octaves; i++)
    {
        sum += Marble_Noise(p * freq, period, seed + i * 103) * amp;
        maxA += amp; amp *= 0.5; freq *= 2.0;
    }
    return sum / maxA;
}

[numthreads(8,8,1)]
void CS_MarbleMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)Marble_Width || y >= (uint)Marble_Height) return;

    float u = (x + 0.5) / (float)Marble_Width;
    float v = (y + 0.5) / (float)Marble_Height;

    float2 p = float2(u, v) * Marble_Scale;
    int period = max(1, (int)Marble_Scale);

    float noiseVal = Marble_FBM(p, period, Marble_Octaves, Marble_Seed);
    float t = sin((p.x + p.y) * Marble_Freq + noiseVal * Marble_Distortion * 6.28318);
    t = t * 0.5 + 0.5;
    t = smoothstep(0.0, 1.0, t);
    t = pow(t, 1.0 + (1.0 - Marble_Sharpness) * 3.0);

    float3 c1 = float3(Marble_R1, Marble_G1, Marble_B1);
    float3 c2 = float3(Marble_R2, Marble_G2, Marble_B2);
    float3 outColor = lerp(c1, c2, t);

    if (Marble_Invert == 1) outColor = 1.0 - outColor;
    MarbleOutput[int2(x,y)] = float4(outColor, 1.0);
}

// --- Drop shadow shader (CS_DropShadowMain) ---
// Simple separable Gaussian blur + composite for drop/inner/glow shadows.
cbuffer DropShadowCB : register(b0)
{
    int DS_Mode; // 0=drop, 1=inner, 2=glow
    float DS_OffsetX;
    float DS_OffsetY;
    float DS_BlurRadius;
    float DS_Opacity;
    float DS_R;
    float DS_G;
    float DS_B;
    int DS_Pass; // 0=hblur, 1=vblur, 2=composite
    int DS_Width;
    int DS_Height;
    int DS_Pad;
};
Texture2D<float4> DSSrc : register(t0);
RWTexture2D<float4> DSOut : register(u0);
RWTexture2D<float4> DSTemp : register(u1); // temporary buffer for separable blur

[numthreads(8,8,1)]
void CS_DropShadowMain(uint3 DTid : SV_DispatchThreadID)
{
    uint x = DTid.x; uint y = DTid.y;
    if (x >= (uint)DS_Width || y >= (uint)DS_Height) return;

    if (DS_Pass == 0)
    {
        // Horizontal blur
        float total = 0.0;
        float weightSum = 0.001;
        int rad = max(1, (int)DS_BlurRadius);
        for (int ox = -rad; ox <= rad; ox++)
        {
            int sx = clamp((int)x + ox, 0, DS_Width-1);
            float4 s = DSSrc.Load(int3(sx, (int)y, 0));
            float w = exp(-(ox*ox) / (2.0 * max(0.5, DS_BlurRadius*DS_BlurRadius)));
            total += s.w * w;
            weightSum += w;
        }
        DSTemp[int2(x,y)] = float4(0,0,0, total / weightSum);
    }
    else if (DS_Pass == 1)
    {
        // Vertical blur
        float total = 0.0;
        float weightSum = 0.001;
        int rad = max(1, (int)DS_BlurRadius);
        for (int oy = -rad; oy <= rad; oy++)
        {
            int sy = clamp((int)y + oy, 0, DS_Height-1);
            float4 s = DSTemp.Load(int3((int)x, sy, 0));
            float w = exp(-(oy*oy) / (2.0 * max(0.5, DS_BlurRadius*DS_BlurRadius)));
            total += s.w * w;
            weightSum += w;
        }
        DSTemp[int2(x,y)] = float4(0,0,0, total / weightSum);
    }
    else if (DS_Pass == 2)
    {
        // Composite
        float4 orig = DSSrc.Load(int3((int)x, (int)y, 0));

        if (DS_Mode == 2) // glow
        {
            float4 blur = DSTemp.Load(int3((int)x, (int)y, 0));
            float glow = blur.w * DS_Opacity;
            float3 glowCol = float3(DS_R, DS_G, DS_B);
            float3 blended = lerp(orig.xyz, glowCol, glow);
            DSOut[int2(x,y)] = float4(blended, max(orig.w, glow));
        }
        else if (DS_Mode == 1) // inner shadow
        {
            float4 blur = DSTemp.Load(int3((int)x, (int)y, 0));
            float shadow = (1.0 - orig.w) * blur.w * DS_Opacity;
            float3 shadowCol = float3(DS_R, DS_G, DS_B);
            float3 blended = lerp(orig.xyz, shadowCol, shadow);
            DSOut[int2(x,y)] = float4(blended, orig.w);
        }
        else // drop shadow
        {
            int ox = (int)x - (int)DS_OffsetX;
            int oy = (int)y - (int)DS_OffsetY;
            ox = clamp(ox, 0, DS_Width-1);
            oy = clamp(oy, 0, DS_Height-1);
            float4 blur = DSTemp.Load(int3(ox, oy, 0));
            float shadowAlpha = blur.w * DS_Opacity;
            float3 shadowCol = float3(DS_R, DS_G, DS_B);
            float3 blended = lerp(shadowCol * shadowAlpha + orig.xyz * orig.w * (1.0 - shadowAlpha), orig.xyz, orig.w);
            float outA = max(shadowAlpha, orig.w);
            if (outA > 0.001)
                blended /= outA;
            DSOut[int2(x,y)] = float4(blended, outA);
        }
    }
}

// End of consolidated shader file
