// SemanticVRColor.hlsl: HLSL port of python/semanticvr_ref/color.py (and Runtime/Confusion/ColorMath.cs).
// All functions take LINEAR sRGB (BT.709, D65). float precision only (no half): parity-tested vs golden.json.
#ifndef SEMANTICVR_COLOR_INCLUDED
#define SEMANTICVR_COLOR_INCLUDED

#define SVR_CVD_PROTAN 0
#define SVR_CVD_DEUTAN 1
#define SVR_CVD_TRITAN 2

#define SVR_LAB_EPSILON (216.0 / 24389.0)
#define SVR_LAB_KAPPA   (24389.0 / 27.0)
#define SVR_POW25_7     6103515625.0

// Machado 2009: 3 types x 11 severities x 9 (row-major). Generated; keep identical to ColorMath.cs.
static const float SVR_MACHADO[297] =
{
    1.0f, 0.0f, -0.0f, 0.0f, 1.0f, 0.0f, -0.0f, -0.0f, 1.0f,  // protan 0.0
    0.856167f, 0.182038f, -0.038205f, 0.029342f, 0.955115f, 0.015544f, -0.00288f, -0.001563f, 1.004443f,  // protan 0.1
    0.734766f, 0.334872f, -0.069637f, 0.05184f, 0.919198f, 0.028963f, -0.004928f, -0.004209f, 1.009137f,  // protan 0.2
    0.630323f, 0.465641f, -0.095964f, 0.069181f, 0.890046f, 0.040773f, -0.006308f, -0.007724f, 1.014032f,  // protan 0.3
    0.539009f, 0.579343f, -0.118352f, 0.082546f, 0.866121f, 0.051332f, -0.007136f, -0.011959f, 1.019095f,  // protan 0.4
    0.458064f, 0.679578f, -0.137642f, 0.092785f, 0.846313f, 0.060902f, -0.007494f, -0.016807f, 1.024301f,  // protan 0.5
    0.38545f, 0.769005f, -0.154455f, 0.100526f, 0.829802f, 0.069673f, -0.007442f, -0.02219f, 1.029632f,  // protan 0.6
    0.319627f, 0.849633f, -0.169261f, 0.106241f, 0.815969f, 0.07779f, -0.007025f, -0.028051f, 1.035076f,  // protan 0.7
    0.259411f, 0.923008f, -0.18242f, 0.110296f, 0.80434f, 0.085364f, -0.006276f, -0.034346f, 1.040622f,  // protan 0.8
    0.203876f, 0.990338f, -0.194214f, 0.112975f, 0.794542f, 0.092483f, -0.005222f, -0.041043f, 1.046265f,  // protan 0.9
    0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f,  // protan 1.0
    1.0f, 0.0f, -0.0f, 0.0f, 1.0f, 0.0f, -0.0f, -0.0f, 1.0f,  // deutan 0.0
    0.866435f, 0.177704f, -0.044139f, 0.049567f, 0.939063f, 0.01137f, -0.003453f, 0.007233f, 0.99622f,  // deutan 0.1
    0.760729f, 0.319078f, -0.079807f, 0.090568f, 0.889315f, 0.020117f, -0.006027f, 0.013325f, 0.992702f,  // deutan 0.2
    0.675425f, 0.43385f, -0.109275f, 0.125303f, 0.847755f, 0.026942f, -0.00795f, 0.018572f, 0.989378f,  // deutan 0.3
    0.605511f, 0.52856f, -0.134071f, 0.155318f, 0.812366f, 0.032316f, -0.009376f, 0.023176f, 0.9862f,  // deutan 0.4
    0.547494f, 0.607765f, -0.155259f, 0.181692f, 0.781742f, 0.036566f, -0.01041f, 0.027275f, 0.983136f,  // deutan 0.5
    0.498864f, 0.674741f, -0.173604f, 0.205199f, 0.754872f, 0.039929f, -0.011131f, 0.030969f, 0.980162f,  // deutan 0.6
    0.457771f, 0.731899f, -0.18967f, 0.226409f, 0.731012f, 0.042579f, -0.011595f, 0.034333f, 0.977261f,  // deutan 0.7
    0.422823f, 0.781057f, -0.203881f, 0.245752f, 0.709602f, 0.044646f, -0.011843f, 0.037423f, 0.974421f,  // deutan 0.8
    0.392952f, 0.82361f, -0.216562f, 0.263559f, 0.69021f, 0.046232f, -0.01191f, 0.040281f, 0.97163f,  // deutan 0.9
    0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.01182f, 0.04294f, 0.968881f,  // deutan 1.0
    1.0f, 0.0f, -0.0f, 0.0f, 1.0f, 0.0f, -0.0f, -0.0f, 1.0f,  // tritan 0.0
    0.92667f, 0.092514f, -0.019184f, 0.021191f, 0.964503f, 0.014306f, 0.008437f, 0.054813f, 0.93675f,  // tritan 0.1
    0.89572f, 0.13333f, -0.02905f, 0.029997f, 0.9454f, 0.024603f, 0.013027f, 0.104707f, 0.882266f,  // tritan 0.2
    0.905871f, 0.127791f, -0.033662f, 0.026856f, 0.941251f, 0.031893f, 0.01341f, 0.148296f, 0.838294f,  // tritan 0.3
    0.948035f, 0.08949f, -0.037526f, 0.014364f, 0.946792f, 0.038844f, 0.010853f, 0.193991f, 0.795156f,  // tritan 0.4
    1.017277f, 0.027029f, -0.044306f, -0.006113f, 0.958479f, 0.047634f, 0.006379f, 0.248708f, 0.744913f,  // tritan 0.5
    1.104996f, -0.046633f, -0.058363f, -0.032137f, 0.971635f, 0.060503f, 0.001336f, 0.317922f, 0.680742f,  // tritan 0.6
    1.193214f, -0.109812f, -0.083402f, -0.058496f, 0.97941f, 0.079086f, -0.002346f, 0.403492f, 0.598854f,  // tritan 0.7
    1.257728f, -0.139648f, -0.118081f, -0.078003f, 0.975409f, 0.102594f, -0.003316f, 0.501214f, 0.502102f,  // tritan 0.8
    1.278864f, -0.125333f, -0.153531f, -0.084748f, 0.957674f, 0.127074f, -0.000989f, 0.601151f, 0.399838f,  // tritan 0.9
    1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.3039f,  // tritan 1.0
};

static const float3 SVR_WHITE_D65 = float3(0.3127 / 0.3290, 1.0, (1.0 - 0.3127 - 0.3290) / 0.3290);

// Returns the matrix ROWS in r0,r1,r2 (acts on column vectors). Explicit rows avoid HLSL
// matrix-constructor / packing order pitfalls.
void SVR_MachadoRows(int type, float severity, out float3 r0, out float3 r1, out float3 r2)
{
    float x = saturate(severity) * 10.0;
    int i = min((int)floor(x), 9);
    float t = x - (float)i;
    int b0 = (type * 11 + i) * 9;
    int b1 = b0 + 9;
    float m[9];
    [unroll] for (int k = 0; k < 9; k++)
        m[k] = SVR_MACHADO[b0 + k] + t * (SVR_MACHADO[b1 + k] - SVR_MACHADO[b0 + k]);
    r0 = float3(m[0], m[1], m[2]);
    r1 = float3(m[3], m[4], m[5]);
    r2 = float3(m[6], m[7], m[8]);
}

float3 SVR_SimulateCvd(float3 rgbLin, int type, float severity)
{
    float3 r0, r1, r2;
    SVR_MachadoRows(type, severity, r0, r1, r2);
    return float3(dot(r0, rgbLin), dot(r1, rgbLin), dot(r2, rgbLin));
}

float3 SVR_LinearRgbToXyz(float3 c)
{
    return float3(dot(float3(0.4124, 0.3576, 0.1805), c),
                  dot(float3(0.2126, 0.7152, 0.0722), c),
                  dot(float3(0.0193, 0.1192, 0.9505), c));
}

float SVR_LabF(float t)
{
    return t > SVR_LAB_EPSILON ? pow(t, 1.0 / 3.0) : (SVR_LAB_KAPPA * t + 16.0) / 116.0;
}

float3 SVR_XyzToLab(float3 xyz)
{
    float3 r = xyz / SVR_WHITE_D65;
    float fx = SVR_LabF(r.x), fy = SVR_LabF(r.y), fz = SVR_LabF(r.z);
    return float3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
}

float3 SVR_RgbLinToLab(float3 rgbLin)
{
    return SVR_XyzToLab(SVR_LinearRgbToXyz(rgbLin));
}

// cos/sin of an angle in DEGREES, range-reduced to [-180,180) first.
float SVR_CosDeg(float d)
{
    d -= 360.0 * floor(d / 360.0 + 0.5);
    return cos(radians(d));
}

float SVR_SinDeg(float d) { return SVR_CosDeg(90.0 - d); }

// Accurate atan2 (radians). GPU fast-math atan2 is only ~1e-4 rad accurate, and CIEDE2000's
// dH' = 2*sqrt(C1'C2')*sin(dh'/2) amplifies hue error by chroma, so we range-reduce and use a series.
float SVR_Atan2(float y, float x)
{
    float ax = abs(x), ay = abs(y);
    bool swap = ay > ax;
    float t = swap ? ax / ay : ay / ax;                 // [0,1]
    // two half-angle reductions: atan(t) = 4 * atan(u), |u| <= tan(pi/16) ~ 0.199
    t = t / (1.0 + sqrt(1.0 + t * t));
    t = t / (1.0 + sqrt(1.0 + t * t));
    float t2 = t * t;
    float r = 4.0 * t * (1.0 + t2 * (-1.0 / 3.0 + t2 * (1.0 / 5.0 + t2 * (-1.0 / 7.0 + t2 * (1.0 / 9.0 + t2 * (-1.0 / 11.0))))));
    if (swap) r = 1.57079632679 - r;
    if (x < 0.0) r = 3.14159265359 - r;
    return y < 0.0 ? -r : r;
}

float SVR_HueDeg(float b, float ap)
{
    // hue of a neutral (a=b=0) is 0 as in the Python reference (atan2(0,0) is NaN on some GPUs).
    if (b == 0.0 && ap == 0.0) return 0.0;
    float h = degrees(SVR_Atan2(b, ap));
    return h < 0.0 ? h + 360.0 : h;
}

// CIEDE2000, kL = kC = kH = 1.
float SVR_Ciede2000(float3 lab1, float3 lab2)
{
    float C1 = sqrt(lab1.y * lab1.y + lab1.z * lab1.z);
    float C2 = sqrt(lab2.y * lab2.y + lab2.z * lab2.z);
    float Cbar = 0.5 * (C1 + C2);
    float Cbar2 = Cbar * Cbar;
    float Cbar7 = Cbar2 * Cbar2 * Cbar2 * Cbar;
    float G = 0.5 * (1.0 - sqrt(Cbar7 / (Cbar7 + SVR_POW25_7)));
    float a1p = (1.0 + G) * lab1.y;
    float a2p = (1.0 + G) * lab2.y;
    float C1p = sqrt(a1p * a1p + lab1.z * lab1.z);
    float C2p = sqrt(a2p * a2p + lab2.z * lab2.z);
    float h1p = SVR_HueDeg(lab1.z, a1p);
    float h2p = SVR_HueDeg(lab2.z, a2p);

    float dLp = lab2.x - lab1.x;
    float dCp = C2p - C1p;
    bool chromaZero = (C1p * C2p) == 0.0;
    float dh = h2p - h1p;
    if (dh > 180.0) dh -= 360.0;
    else if (dh < -180.0) dh += 360.0;
    float dhp = chromaZero ? 0.0 : dh;
    float dHp = 2.0 * sqrt(C1p * C2p) * SVR_SinDeg(dhp * 0.5);

    float Lbarp = 0.5 * (lab1.x + lab2.x);
    float Cbarp = 0.5 * (C1p + C2p);
    float hsum = h1p + h2p;
    float hdiff = abs(h1p - h2p);
    float hbar;
    if (chromaZero) hbar = hsum;
    else if (hdiff <= 180.0) hbar = 0.5 * hsum;
    else if (hsum < 360.0) hbar = 0.5 * (hsum + 360.0);
    else hbar = 0.5 * (hsum - 360.0);

    float T = 1.0
        - 0.17 * SVR_CosDeg(hbar - 30.0)
        + 0.24 * SVR_CosDeg(2.0 * hbar)
        + 0.32 * SVR_CosDeg(3.0 * hbar + 6.0)
        - 0.20 * SVR_CosDeg(4.0 * hbar - 63.0);
    float u = (hbar - 275.0) / 25.0;
    float dtheta = 30.0 * exp(-(u * u));
    float Cbarp2 = Cbarp * Cbarp;
    float Cbarp7 = Cbarp2 * Cbarp2 * Cbarp2 * Cbarp;
    float Rc = 2.0 * sqrt(Cbarp7 / (Cbarp7 + SVR_POW25_7));
    float Lm50sq = (Lbarp - 50.0) * (Lbarp - 50.0);
    float Sl = 1.0 + 0.015 * Lm50sq / sqrt(20.0 + Lm50sq);
    float Sc = 1.0 + 0.045 * Cbarp;
    float Sh = 1.0 + 0.015 * Cbarp * T;
    float Rt = -SVR_SinDeg(2.0 * dtheta) * Rc;

    float tl = dLp / Sl, tc = dCp / Sc, th = dHp / Sh;
    return sqrt(tl * tl + tc * tc + th * th + Rt * tc * th);
}

#endif // SEMANTICVR_COLOR_INCLUDED
