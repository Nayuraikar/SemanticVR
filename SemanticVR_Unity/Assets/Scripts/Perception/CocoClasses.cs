namespace SemanticVR.Perception
{
    /// <summary>
    /// COCO-91 class-id to name table (the id space used by the RF-DETR labels output). Unused ids are null.
    /// Verified against the names emitted by tools/scripts/run_still.py (person=1 ... stop sign=13).
    /// </summary>
    public static class CocoClasses
    {
        static readonly string[] Names = BuildNames();

        static string[] BuildNames()
        {
            var n = new string[RfDetrModelSpec.Classes];
            string[] list = {
                "1:person","2:bicycle","3:car","4:motorcycle","5:airplane","6:bus","7:train","8:truck","9:boat","10:traffic light",
                "11:fire hydrant","13:stop sign","14:parking meter","15:bench","16:bird","17:cat","18:dog","19:horse","20:sheep","21:cow",
                "22:elephant","23:bear","24:zebra","25:giraffe","27:backpack","28:umbrella","31:handbag","32:tie","33:suitcase","34:frisbee",
                "35:skis","36:snowboard","37:sports ball","38:kite","39:baseball bat","40:baseball glove","41:skateboard","42:surfboard",
                "43:tennis racket","44:bottle","46:wine glass","47:cup","48:fork","49:knife","50:spoon","51:bowl","52:banana","53:apple",
                "54:sandwich","55:orange","56:broccoli","57:carrot","58:hot dog","59:pizza","60:donut","61:cake","62:chair","63:couch",
                "64:potted plant","65:bed","67:dining table","70:toilet","72:tv","73:laptop","74:mouse","75:remote","76:keyboard",
                "77:cell phone","78:microwave","79:oven","80:toaster","81:sink","82:refrigerator","84:book","85:clock","86:vase",
                "87:scissors","88:teddy bear","89:hair drier","90:toothbrush" };
            foreach (var e in list) { int i = e.IndexOf(':'); n[int.Parse(e.Substring(0, i))] = e.Substring(i + 1); }
            return n;
        }

        /// <summary>True if <paramref name="id"/> is a real COCO class (not one of the 10 unused ids).</summary>
        public static bool IsValid(int id) => id >= 0 && id < Names.Length && Names[id] != null;

        /// <summary>Class name, or "unused-id-N" for invalid ids.</summary>
        public static string NameOf(int id) => IsValid(id) ? Names[id] : "unused-id-" + id;

        /// <summary>Number of valid classes (expected 80).</summary>
        public static int ValidCount { get { int c = 0; foreach (var s in Names) if (s != null) c++; return c; } }
    }
}
