#nullable disable
namespace Wasteland.Presentation
{
    /// <summary>
    /// Road strokes from <c>REDG.surface</c>. Widths are screen pixels and stay
    /// constant when the camera zooms.
    /// </summary>
    public static class RoadArt
    {
        public const int Paved = 2;
        public const int Gravel = 1;
        public const int Dirt = 0;
        public const int Amber = 0xD9B26A;
        public const int Ink = 0x1E1F1D;
        public const float PavedBodyPx = 4f;
        public const float PavedInkSidePx = 1f;
        public const float GravelPx = 2f;
        public const float DirtPx = 1.5f;
        public const float DashOnPx = 6f;
        public const float DashOffPx = 4f;

        public static RoadStroke ForSurface(int surface)
        {
            if (surface == Paved)
            {
                return new RoadStroke(PavedBodyPx, PavedInkSidePx, false, false, 0f, 0f, Amber, Ink);
            }

            if (surface == Gravel)
            {
                return new RoadStroke(GravelPx, 0f, true, false, 0f, 0f, Ink, Ink);
            }

            return new RoadStroke(DirtPx, 0f, true, true, DashOnPx, DashOffPx, Ink, Ink);
        }
    }

    public readonly struct RoadStroke
    {
        public readonly float BodyPx;
        public readonly float InkSidePx;
        public readonly bool InkOnly;
        public readonly bool Dashed;
        public readonly float DashOnPx;
        public readonly float DashOffPx;
        public readonly int BodyRgb;
        public readonly int InkRgb;

        public RoadStroke(
            float bodyPx,
            float inkSidePx,
            bool inkOnly,
            bool dashed,
            float dashOnPx,
            float dashOffPx,
            int bodyRgb,
            int inkRgb)
        {
            BodyPx = bodyPx;
            InkSidePx = inkSidePx;
            InkOnly = inkOnly;
            Dashed = dashed;
            DashOnPx = dashOnPx;
            DashOffPx = dashOffPx;
            BodyRgb = bodyRgb;
            InkRgb = inkRgb;
        }

        public float TotalPx
        {
            get { return InkOnly ? BodyPx : BodyPx + InkSidePx * 2f; }
        }
    }
}
