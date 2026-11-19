namespace SemanticVR.Perception
{
    /// <summary>Verified tensor geometry of the RF-DETR-Seg Nano ONNX export (SHA-256 c03620...b37c6).</summary>
    public static class RfDetrModelSpec
    {
        /// <summary>Square model input edge in pixels (input tensor is [1,3,312,312]).</summary>
        public const int InputSize = 312;
        /// <summary>Number of DETR queries (rows of dets/labels/masks).</summary>
        public const int Queries = 100;
        /// <summary>Class logits per query (COCO-91 id space).</summary>
        public const int Classes = 91;
        /// <summary>Mask logit grid edge per query (masks tensor is [1,100,78,78]).</summary>
        public const int MaskSize = 78;
        public const string InputName = "input";
        public const string DetsName = "dets";
        public const string LabelsName = "labels";
        public const string MasksName = "masks";
        public static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        public static readonly float[] Std = { 0.229f, 0.224f, 0.225f };
    }
}
