using System;
using System.Collections.Generic;

[Serializable] public class PointOut { public double lon; public double lat; public double h; }
[Serializable] public class FrameOut { public string label; public int t; public List<PointOut> points; }
[Serializable] public class FramesPayload { public string crs; public string height_ref; public List<FrameOut> frames; }
