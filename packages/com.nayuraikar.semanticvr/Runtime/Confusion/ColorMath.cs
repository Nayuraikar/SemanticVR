using Unity.Burst;
using Unity.Mathematics;

namespace SemanticVR.Confusion
{
    /// <summary>Colour-vision-deficiency type. Values match the HLSL include (SVR_CVD_*).</summary>
    public enum CvdType { Protan = 0, Deutan = 1, Tritan = 2 }

    /// <summary>
    /// Burst-compatible colour maths. Port of python/semanticvr_ref/color.py; every function
    /// works on LINEAR sRGB (BT.709 primaries, D65). Keep in sync with Shaders/SemanticVRColor.hlsl.
    /// </summary>
    [BurstCompile]
    public static class ColorMath
    {
        // Machado 2009 matrices, 3 types x 11 severities (0.0 .. 1.0) x 9 (row-major) = 297 floats.
        // Generated from python/semanticvr_ref/color.py (_MACHADO_TABLE). Do not edit by hand.
        static readonly float[] MachadoTable =
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

        // Lab constants (exact rationals)
        const float LabEpsilon = 216f / 24389f;
        const float LabKappa = 24389f / 27f;

        // D65 white from xy = (0.3127, 0.3290), Y = 1
        static readonly float3 WhiteD65 = new float3(0.3127f / 0.3290f, 1f, (1f - 0.3127f - 0.3290f) / 0.3290f);

        /// <summary>Machado matrix (acts on column vectors of linear sRGB). Severity in [0,1] is
        /// interpolated linearly between the two bracketing 0.1-grid matrices (same as the Python reference).</summary>
        public static float3x3 MachadoMatrix(CvdType type, float severity)
        {
            float x = math.saturate(severity) * 10f;
            int i = math.min((int)math.floor(x), 9);
            float t = x - i;
            int b0 = ((int)type * 11 + i) * 9;
            int b1 = b0 + 9;
            // float3x3 constructor takes COLUMNS; table is row-major, so column c = (m[0][c], m[1][c], m[2][c]).
            float3 c0 = Lerp3(b0 + 0, b0 + 3, b0 + 6, b1 + 0, b1 + 3, b1 + 6, t);
            float3 c1 = Lerp3(b0 + 1, b0 + 4, b0 + 7, b1 + 1, b1 + 4, b1 + 7, t);
            float3 c2 = Lerp3(b0 + 2, b0 + 5, b0 + 8, b1 + 2, b1 + 5, b1 + 8, t);
            return new float3x3(c0, c1, c2);
        }

        static float3 Lerp3(int a0, int a1, int a2, int b0, int b1, int b2, float t)
        {
            return new float3(
                MachadoTable[a0] + t * (MachadoTable[b0] - MachadoTable[a0]),
                MachadoTable[a1] + t * (MachadoTable[b1] - MachadoTable[a1]),
                MachadoTable[a2] + t * (MachadoTable[b2] - MachadoTable[a2]));
        }

        /// <summary>Simulated linear sRGB (not clamped).</summary>
        public static float3 SimulateCvd(float3 rgbLin, CvdType type, float severity)
        {
            return math.mul(MachadoMatrix(type, severity), rgbLin);
        }

        public static float3 LinearRgbToXyz(float3 c)
        {
            return new float3(
                math.dot(new float3(0.4124f, 0.3576f, 0.1805f), c),
                math.dot(new float3(0.2126f, 0.7152f, 0.0722f), c),
                math.dot(new float3(0.0193f, 0.1192f, 0.9505f), c));
        }

        static float LabF(float t)
        {
            return t > LabEpsilon ? math.pow(t, 1f / 3f) : (LabKappa * t + 16f) / 116f;
        }

        public static float3 XyzToLab(float3 xyz)
        {
            float3 r = xyz / WhiteD65;
            float fx = LabF(r.x), fy = LabF(r.y), fz = LabF(r.z);
            return new float3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        public static float3 RgbLinToLab(float3 rgbLin) => XyzToLab(LinearRgbToXyz(rgbLin));

        // cos of an angle in degrees, range-reduced first (mirrors HLSL SVR_CosDeg).
        static float CosDeg(float d)
        {
            d -= 360f * math.floor(d / 360f + 0.5f);
            return math.cos(math.radians(d));
        }

        static float HueDeg(float b, float ap)
        {
            if (b == 0f && ap == 0f) return 0f; // neutral: hue 0 (atan2(0,0) is NaN on some GPUs; keep HLSL identical)
            float h = math.degrees(math.atan2(b, ap));
            return h < 0f ? h + 360f : h;
        }

        /// <summary>CIEDE2000 with kL = kC = kH = 1 (Sharma, Wu, Dalal 2005).</summary>
        public static float Ciede2000(float3 lab1, float3 lab2)
        {
            const float pow25_7 = 6103515625f; // 25^7
            float C1 = math.sqrt(lab1.y * lab1.y + lab1.z * lab1.z);
            float C2 = math.sqrt(lab2.y * lab2.y + lab2.z * lab2.z);
            float Cbar = 0.5f * (C1 + C2);
            float Cbar7 = Cbar * Cbar * Cbar * Cbar * Cbar * Cbar * Cbar;
            float G = 0.5f * (1f - math.sqrt(Cbar7 / (Cbar7 + pow25_7)));
            float a1p = (1f + G) * lab1.y;
            float a2p = (1f + G) * lab2.y;
            float C1p = math.sqrt(a1p * a1p + lab1.z * lab1.z);
            float C2p = math.sqrt(a2p * a2p + lab2.z * lab2.z);
            float h1p = HueDeg(lab1.z, a1p);
            float h2p = HueDeg(lab2.z, a2p);

            float dLp = lab2.x - lab1.x;
            float dCp = C2p - C1p;
            bool chromaZero = C1p * C2p == 0f;
            float dh = h2p - h1p;
            if (dh > 180f) dh -= 360f;
            else if (dh < -180f) dh += 360f;
            float dhp = chromaZero ? 0f : dh;
            float dHp = 2f * math.sqrt(C1p * C2p) * math.sin(math.radians(dhp * 0.5f));

            float Lbarp = 0.5f * (lab1.x + lab2.x);
            float Cbarp = 0.5f * (C1p + C2p);
            float hsum = h1p + h2p;
            float hdiff = math.abs(h1p - h2p);
            float hbar;
            if (chromaZero) hbar = hsum;
            else if (hdiff <= 180f) hbar = 0.5f * hsum;
            else if (hsum < 360f) hbar = 0.5f * (hsum + 360f);
            else hbar = 0.5f * (hsum - 360f);

            float T = 1f
                - 0.17f * CosDeg(hbar - 30f)
                + 0.24f * CosDeg(2f * hbar)
                + 0.32f * CosDeg(3f * hbar + 6f)
                - 0.20f * CosDeg(4f * hbar - 63f);
            float u = (hbar - 275f) / 25f;
            float dtheta = 30f * math.exp(-(u * u));
            float Cbarp7 = Cbarp * Cbarp * Cbarp * Cbarp * Cbarp * Cbarp * Cbarp;
            float Rc = 2f * math.sqrt(Cbarp7 / (Cbarp7 + pow25_7));
            float Lm50sq = (Lbarp - 50f) * (Lbarp - 50f);
            float Sl = 1f + 0.015f * Lm50sq / math.sqrt(20f + Lm50sq);
            float Sc = 1f + 0.045f * Cbarp;
            float Sh = 1f + 0.015f * Cbarp * T;
            float Rt = -math.sin(math.radians(2f * dtheta)) * Rc;

            float tl = dLp / Sl, tc = dCp / Sc, th = dHp / Sh;
            return math.sqrt(tl * tl + tc * tc + th * th + Rt * tc * th);
        }
    }
}
