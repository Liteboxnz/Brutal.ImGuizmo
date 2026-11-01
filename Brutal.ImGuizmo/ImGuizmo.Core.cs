using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace Brutal.ImGuizmoAPI;

public static partial class ImGuizmo
{
    const float Pi = 3.14159265358979323846f;
    const float Rad2Deg = 180f / Pi;
    const float Deg2Rad = Pi / 180f;
    const float ScreenRotateSize = 0.06f;
    const float RotationDisplayFactor = 1.2f;
    const float QuadMin = 0.5f;
    const float QuadMax = 0.8f;
    const int HalfCircleSegmentCount = 64;
    const float SnapTension = 0.5f;

    static readonly float4[] DirectionUnary =
    {
        new(1f, 0f, 0f, 0f),
        new(0f, 1f, 0f, 0f),
        new(0f, 0f, 1f, 0f),
    };

    static readonly ImGuizmoOperation[] TranslatePlans =
    {
        ImGuizmoOperation.TranslateY | ImGuizmoOperation.TranslateZ,
        ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateZ,
        ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY,
    };

    static readonly int[] TranslationInfoIndex = { 0, 0, 0, 1, 0, 0, 2, 0, 0, 1, 2, 0, 0, 2, 0, 0, 1, 0, 0, 1, 2 };
    static readonly float[] QuadUV = { QuadMin, QuadMin, QuadMin, QuadMax, QuadMax, QuadMax, QuadMax, QuadMin };

    static readonly GizmoContext gContext = new();
    static int s_overBox = -1;

    sealed class TripodState
    {
        public readonly bool[] BelowAxis = new bool[3];
        public readonly bool[] BelowPlane = new bool[3];
        public readonly float[] AxisFactor = { 1f, 1f, 1f };
    }

    sealed class ViewManipulateState
    {
        public ImGuiID Id;
        public bool Dragging;
        public bool Clicking;
        public int Frames;
        public float4 Up;
        public float4 Dir;
    }

    sealed class GizmoContext
    {
        public GizmoContext()
        {
            Style = new ImGuizmoStyle();
            EditingId = new ImGuiID(uint.MaxValue);
        }

        public ImDrawListPtr? DrawList;
        public ImGuizmoStyle Style;
        public ImGuizmoMode Mode;
        public float4x4 ViewMat;
        public float4x4 ProjectionMat;
        public float4x4 Model;
        public float4x4 ModelLocal;
        public float4x4 ModelInverse;
        public float4x4 ModelSource;
        public float4x4 ModelSourceInverse;
        public float4x4 MVP;
        public float4x4 MVPLocal;
        public float4x4 ViewProjection;
        public float4 ModelScaleOrigin;
        public float4 CameraEye;
        public float4 CameraRight;
        public float4 CameraDir;
        public float4 CameraUp;
        public float4 RayOrigin;
        public float4 RayVector;
        public float RadiusSquareCenter;
        public float2 ScreenSquareCenter;
        public float2 ScreenSquareMin;
        public float2 ScreenSquareMax;
        public float ScreenFactor;
        public float4 RelativeOrigin;
        public bool Using;
        public bool UsingViewManipulate;
        public bool EnableGizmo = true;
        public bool MouseOver;
        public bool ViewManipulatorHovered;
        public float4 TranslationPlan;
        public float4 TranslationPlanOrigin;
        public float4 MatrixOrigin;
        public float4 TranslationLastDelta;
        public float4 RotationVectorSource;
        public float RotationAngle;
        public float RotationAngleOrigin;
        public float4 Scale = new(1f, 1f, 1f, 0f);
        public float4 ScaleValueOrigin;
        public float4 ScaleLast;
        public float SaveMousePosX;
        public int AxisMask;
        public readonly TripodState Tripod = new();
        public readonly TripodState ActiveTripod = new();
        public float AxisLimit = 0.0025f;
        public float PlaneLimit = 0.02f;
        public float4 BoundsPivot;
        public float4 BoundsAnchor;
        public float4 BoundsPlan;
        public float4 BoundsLocalPivot;
        public int BoundsBestAxis;
        public readonly int[] BoundsAxis = new int[2];
        public bool UsingBounds;
        public float4x4 BoundsMatrix;
        public ImGuizmoMoveType CurrentHandleType;
        public ImGuizmoMoveType HoveredHandleType;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public float XMax;
        public float YMax;
        public float DisplayRatio = 1f;
        public bool IsOrthographic;
        public bool OverGizmoHotspot;
        public bool OverGizmoHotspotLastFrame;
        public ImGuiWindowPtr AlternativeWindow;
        public readonly List<ImGuiID> IdStack = new();
        public ImGuiID EditingId;
        public ImGuizmoOperation Operation;
        public readonly List<ViewManipulateState> ViewStates = new();
        public bool AllowAxisFlip = true;
        public float GizmoSizeClipSpace = 0.1f;

        public ImGuiID CurrentId
        {
            get
            {
                if (IdStack.Count == 0)
                    IdStack.Add(new ImGuiID(uint.MaxValue));
                return IdStack[^1];
            }
        }

        public TripodState CurrentTripod => Using && CurrentId == EditingId ? ActiveTripod : Tripod;

        public ViewManipulateState CurrentView()
        {
            var id = CurrentId;
            for (int i = 0; i < ViewStates.Count; i++)
            {
                if (ViewStates[i].Id == id)
                    return ViewStates[i];
            }
            var state = new ViewManipulateState { Id = id };
            ViewStates.Add(state);
            return state;
        }
    }

    static void CopyTripod(TripodState src, TripodState dst)
    {
        Array.Copy(src.BelowAxis, dst.BelowAxis, 3);
        Array.Copy(src.BelowPlane, dst.BelowPlane, 3);
        Array.Copy(src.AxisFactor, dst.AxisFactor, 3);
    }

    static bool Intersects(ImGuizmoOperation lhs, ImGuizmoOperation rhs) => (lhs & rhs) != 0;
    static bool Contains(ImGuizmoOperation lhs, ImGuizmoOperation rhs) => (lhs & rhs) == rhs;
    static bool IsTranslateType(ImGuizmoMoveType type) => type >= ImGuizmoMoveType.MT_MOVE_X && type <= ImGuizmoMoveType.MT_MOVE_SCREEN;
    static bool IsRotateType(ImGuizmoMoveType type) => type >= ImGuizmoMoveType.MT_ROTATE_X && type <= ImGuizmoMoveType.MT_ROTATE_SCREEN;
    static bool IsScaleType(ImGuizmoMoveType type) => type >= ImGuizmoMoveType.MT_SCALE_X && type <= ImGuizmoMoveType.MT_SCALE_XYZ;

    static float Comp(float4 v, int i) => i switch { 0 => v.X, 1 => v.Y, 2 => v.Z, _ => v.W };
    static float4 SetComp(float4 v, int i, float value) => i switch
    {
        0 => new float4(value, v.Y, v.Z, v.W),
        1 => new float4(v.X, value, v.Z, v.W),
        2 => new float4(v.X, v.Y, value, v.W),
        _ => new float4(v.X, v.Y, v.Z, value),
    };
    static float4 Row(in float4x4 m, int i) => i switch { 0 => m.X, 1 => m.Y, 2 => m.Z, _ => m.W };
    static void SetRow(ref float4x4 m, int i, float4 v)
    {
        switch (i)
        {
            case 0: m.X = v; break;
            case 1: m.Y = v; break;
            case 2: m.Z = v; break;
            default: m.W = v; break;
        }
    }
    static float At(in float4x4 m, int r, int c) => Comp(Row(m, r), c);

    static float Dot3(float4 a, float4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static float Dot4(float4 a, float4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    static float LenSq3(float4 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;
    static float Len3(float4 v) => MathF.Sqrt(LenSq3(v));
    static float4 Cross(float4 a, float4 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X, 0f);
    static float4 Normalized(float4 v)
    {
        float len = Len3(v);
        return v * (1f / (len > float.Epsilon ? len : float.Epsilon));
    }
    static float4 Abs4(float4 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z), MathF.Abs(v.W));
    static void Lerp(ref float4 v, float4 target, float t)
    {
        v.X += (target.X - v.X) * t;
        v.Y += (target.Y - v.Y) * t;
        v.Z += (target.Z - v.Z) * t;
        v.W += (target.W - v.W) * t;
    }
    static float Clamp(float x, float y, float z) => x < y ? y : (x > z ? z : x);
    static bool IsWithin(float x, float y, float z) => x >= y && x <= z;

    static float4 Transform(float4 v, in float4x4 m) => new(
        v.X * m.X.X + v.Y * m.Y.X + v.Z * m.Z.X + v.W * m.W.X,
        v.X * m.X.Y + v.Y * m.Y.Y + v.Z * m.Z.Y + v.W * m.W.Y,
        v.X * m.X.Z + v.Y * m.Y.Z + v.Z * m.Z.Z + v.W * m.W.Z,
        v.X * m.X.W + v.Y * m.Y.W + v.Z * m.Z.W + v.W * m.W.W);

    static float4 TransformPoint(float4 v, in float4x4 m) => new(
        v.X * m.X.X + v.Y * m.Y.X + v.Z * m.Z.X + m.W.X,
        v.X * m.X.Y + v.Y * m.Y.Y + v.Z * m.Z.Y + m.W.Y,
        v.X * m.X.Z + v.Y * m.Y.Z + v.Z * m.Z.Z + m.W.Z,
        v.X * m.X.W + v.Y * m.Y.W + v.Z * m.Z.W + m.W.W);

    static float4 TransformVector(float4 v, in float4x4 m) => new(
        v.X * m.X.X + v.Y * m.Y.X + v.Z * m.Z.X,
        v.X * m.X.Y + v.Y * m.Y.Y + v.Z * m.Z.Y,
        v.X * m.X.Z + v.Y * m.Y.Z + v.Z * m.Z.Z,
        v.X * m.X.W + v.Y * m.Y.W + v.Z * m.Z.W);

    static void OrthoNormalize(ref float4x4 m)
    {
        m.X = Normalized(m.X);
        m.Y = Normalized(m.Y);
        m.Z = Normalized(m.Z);
    }

    static float4x4 Inverse(in float4x4 m)
    {
        float4x4.Invert(m, out var inv);
        return inv;
    }

    static float4x4 Translation(float x, float y, float z)
    {
        var m = float4x4.Identity;
        m.W = new float4(x, y, z, 1f);
        return m;
    }

    static float4x4 Translation(float4 t) => Translation(t.X, t.Y, t.Z);

    static float4x4 ScaleMatrix(float x, float y, float z)
    {
        var m = float4x4.Identity;
        m.X = new float4(x, 0f, 0f, 0f);
        m.Y = new float4(0f, y, 0f, 0f);
        m.Z = new float4(0f, 0f, z, 0f);
        return m;
    }

    static float4x4 ScaleMatrix(float4 s) => ScaleMatrix(s.X, s.Y, s.Z);

    static float4x4 RotationAxis(float4 axis, float angle)
    {
        if (LenSq3(axis) < float.Epsilon)
            return float4x4.Identity;
        var n = axis * (1f / MathF.Sqrt(LenSq3(axis)));
        float s = MathF.Sin(angle);
        float c = MathF.Cos(angle);
        float k = 1f - c;
        float xx = n.X * n.X * k + c;
        float yy = n.Y * n.Y * k + c;
        float zz = n.Z * n.Z * k + c;
        float xy = n.X * n.Y * k;
        float yz = n.Y * n.Z * k;
        float zx = n.Z * n.X * k;
        float xs = n.X * s;
        float ys = n.Y * s;
        float zs = n.Z * s;
        return new float4x4(
            xx, xy + zs, zx - ys, 0f,
            xy - zs, yy, yz + xs, 0f,
            zx + ys, yz - xs, zz, 0f,
            0f, 0f, 0f, 1f);
    }

    static float4x4 Frustum(float left, float right, float bottom, float top, float znear, float zfar, bool rightHanded)
    {
        float temp = 2f * znear;
        float temp2 = right - left;
        float temp3 = top - bottom;
        float temp4 = zfar - znear;
        float sign = rightHanded ? -1f : 1f;
        return new float4x4(
            temp / temp2, 0f, 0f, 0f,
            0f, temp / temp3, 0f, 0f,
            (right + left) / temp2, (top + bottom) / temp3, sign * (zfar + znear) / temp4, sign,
            0f, 0f, (-temp * zfar) / temp4, 0f);
    }

    static float4x4 Perspective(float fovyInDegrees, float aspectRatio, float znear, float zfar, bool rightHanded)
    {
        float ymax = znear * MathF.Tan(fovyInDegrees * Deg2Rad);
        float xmax = ymax * aspectRatio;
        return Frustum(-xmax, xmax, -ymax, ymax, znear, zfar, rightHanded);
    }

    static float4x4 LookAt(float4 eye, float4 at, float4 up, bool rightHanded)
    {
        float4 z = Normalized(rightHanded ? eye - at : at - eye);
        float4 y = Normalized(up);
        float4 x = Normalized(Cross(y, z));
        y = Normalized(Cross(z, x));
        return new float4x4(
            x.X, y.X, z.X, 0f,
            x.Y, y.Y, z.Y, 0f,
            x.Z, y.Z, z.Z, 0f,
            -Dot3(x, eye), -Dot3(y, eye), -Dot3(z, eye), 1f);
    }

    static float4 BuildPlan(float4 point, float4 normal)
    {
        var n = Normalized(normal);
        return new float4(n.X, n.Y, n.Z, Dot4(n, point));
    }

    static ImColor8 Col32(byte r, byte g, byte b, byte a = 255) => (ImColor8)((uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24));
    static ImColor8 Or(ImColor8 a, ImColor8 b) => (ImColor8)((uint)a | (uint)b);
    static ImColor8 StyleColor(ImGuizmoColor color)
    {
        var c = gContext.Style.Colors[(int)color];
        return (ImColor8)ImGui.ColorConvertFloat4ToU32(in c);
    }

    static ImDrawListPtr RequireDrawList() => gContext.DrawList ?? throw new InvalidOperationException("Call ImGuizmo.BeginFrame or ImGuizmo.SetDrawlist before drawing.");

    static float2 WorldToPos(float4 worldPos, in float4x4 mat, float2 position, float2 size)
    {
        var trans = TransformPoint(worldPos, mat);
        if (MathF.Abs(trans.W) < float.Epsilon)
            return new float2(float.NegativeInfinity, float.NegativeInfinity);
        trans *= 0.5f / trans.W;
        trans += new float4(0.5f, 0.5f, 0f, 0f);
        trans.Y = 1f - trans.Y;
        trans.X *= size.X;
        trans.Y *= size.Y;
        trans.X += position.X;
        trans.Y += position.Y;
        return new float2(trans.X, trans.Y);
    }

    static float2 WorldToPos(float4 worldPos, in float4x4 mat) =>
        WorldToPos(worldPos, mat, new float2(gContext.X, gContext.Y), new float2(gContext.Width, gContext.Height));

    static void ComputeCameraRay(in float4x4 viewMatrix, in float4x4 projectionMatrix, float2 mousePosition, float2 position, float2 size, out float4 rayOrigin, out float4 rayDir)
    {
        var viewProjInverse = Inverse(viewMatrix * projectionMatrix);
        float mox = ((mousePosition.X - position.X) / size.X) * 2f - 1f;
        float moy = (1f - ((mousePosition.Y - position.Y) / size.Y)) * 2f - 1f;
        var hA = Transform(new float4(mox, moy, 0f, 1f), viewProjInverse);
        var hB = Transform(new float4(mox, moy, 1f - float.Epsilon, 1f), viewProjInverse);
        bool aAtInfinity = MathF.Abs(hA.W) < float.Epsilon;
        bool bAtInfinity = MathF.Abs(hB.W) < float.Epsilon;
        var pointA = aAtInfinity ? hA : hA * (1f / hA.W);
        var pointB = bAtInfinity ? hB : hB * (1f / hB.W);
        var eye = Inverse(viewMatrix).W;
        float4 nearPoint, farPoint;
        bool farAtInfinity;
        if (LenSq3(pointA - eye) <= LenSq3(pointB - eye))
        {
            nearPoint = pointA;
            farPoint = pointB;
            farAtInfinity = bAtInfinity;
        }
        else
        {
            nearPoint = pointB;
            farPoint = pointA;
            farAtInfinity = aAtInfinity;
        }
        rayOrigin = nearPoint;
        rayDir = farAtInfinity ? Normalized(nearPoint - eye) : Normalized(farPoint - nearPoint);
    }

    static void ComputeCameraRay(float2 position, float2 size)
    {
        var io = ImGui.GetIO();
        ComputeCameraRay(gContext.ViewMat, gContext.ProjectionMat, io.MousePos, position, size, out gContext.RayOrigin, out gContext.RayVector);
    }

    static void ComputeCameraRay() => ComputeCameraRay(new float2(gContext.X, gContext.Y), new float2(gContext.Width, gContext.Height));

    static float GetSegmentLengthClipSpace(float4 start, float4 end, bool localCoordinates = false)
    {
        var mvp = localCoordinates ? gContext.MVPLocal : gContext.MVP;
        var startOfSegment = TransformPoint(start, mvp);
        if (MathF.Abs(startOfSegment.W) > float.Epsilon)
            startOfSegment *= 1f / startOfSegment.W;
        var endOfSegment = TransformPoint(end, mvp);
        if (MathF.Abs(endOfSegment.W) > float.Epsilon)
            endOfSegment *= 1f / endOfSegment.W;
        var clipSpaceAxis = endOfSegment - startOfSegment;
        if (gContext.DisplayRatio < 1f)
            clipSpaceAxis.X *= gContext.DisplayRatio;
        else
            clipSpaceAxis.Y /= gContext.DisplayRatio;
        return MathF.Sqrt(clipSpaceAxis.X * clipSpaceAxis.X + clipSpaceAxis.Y * clipSpaceAxis.Y);
    }

    static float GetParallelogram(float4 ptO, float4 ptA, float4 ptB)
    {
        Span<float4> pts = stackalloc float4[3];
        pts[0] = ptO;
        pts[1] = ptA;
        pts[2] = ptB;
        for (int i = 0; i < 3; i++)
        {
            pts[i] = TransformPoint(pts[i], gContext.MVP);
            if (MathF.Abs(pts[i].W) > float.Epsilon)
                pts[i] *= 1f / pts[i].W;
        }
        var segA = pts[1] - pts[0];
        var segB = pts[2] - pts[0];
        segA.Y /= gContext.DisplayRatio;
        segB.Y /= gContext.DisplayRatio;
        var segAOrtho = Normalized(new float4(-segA.Y, segA.X, 0f, 0f));
        float dt = Dot3(segAOrtho, segB);
        return MathF.Sqrt(segA.X * segA.X + segA.Y * segA.Y) * MathF.Abs(dt);
    }

    static float4 PointOnSegment(float4 point, float4 vertPos1, float4 vertPos2)
    {
        var c = point - vertPos1;
        var v = Normalized(vertPos2 - vertPos1);
        float d = Len3(vertPos2 - vertPos1);
        float t = Dot3(v, c);
        if (t < 0f)
            return vertPos1;
        if (t > d)
            return vertPos2;
        return vertPos1 + v * t;
    }

    static float IntersectRayPlane(float4 origin, float4 vector, float4 plan)
    {
        float numer = Dot3(plan, origin) - plan.W;
        float denom = Dot3(plan, vector);
        if (MathF.Abs(denom) < float.Epsilon)
            return -1f;
        return -(numer / denom);
    }

    static float DistanceToPlane(float4 point, float4 plan) => Dot3(point, plan) + plan.W;

    static bool IsInContextRect(float2 p) => IsWithin(p.X, gContext.X, gContext.XMax) && IsWithin(p.Y, gContext.Y, gContext.YMax);

    static bool SameWindow(ImGuiWindowPtr a, ImGuiWindowPtr b) => a.Equals(b);

    static bool IsHoveringWindow()
    {
        if (gContext.DrawList is not ImDrawListPtr drawList)
            return false;
        var g = ImGui.GetCurrentContext();
        var window = ImGui.Internal.FindWindowByName(drawList._OwnerName);
        if (SameWindow(g.HoveredWindow, window))
            return true;
        if (!gContext.AlternativeWindow.Equals(default) && SameWindow(g.HoveredWindow, gContext.AlternativeWindow))
            return true;
        if (!g.HoveredWindow.Equals(default))
            return false;
        var inner = window.InnerRect;
        var min = inner.Min;
        var max = inner.Max;
        return ImGui.IsMouseHoveringRect(in min, in max, false);
    }

    static void ComputeContext(in float4x4 view, in float4x4 projection, in float4x4 matrix, ImGuizmoMode mode)
    {
        gContext.Mode = mode;
        gContext.ViewMat = view;
        gContext.ProjectionMat = projection;
        gContext.MouseOver = IsHoveringWindow();
        gContext.ModelLocal = matrix;
        OrthoNormalize(ref gContext.ModelLocal);
        if (mode == ImGuizmoMode.Local)
            gContext.Model = gContext.ModelLocal;
        else
            gContext.Model = Translation(matrix.W);
        gContext.ModelSource = matrix;
        gContext.ModelScaleOrigin = new float4(Len3(matrix.X), Len3(matrix.Y), Len3(matrix.Z), 0f);
        gContext.ModelInverse = Inverse(gContext.Model);
        gContext.ModelSourceInverse = Inverse(gContext.ModelSource);
        gContext.ViewProjection = gContext.ViewMat * gContext.ProjectionMat;
        gContext.MVP = gContext.Model * gContext.ViewProjection;
        gContext.MVPLocal = gContext.ModelLocal * gContext.ViewProjection;
        var viewInverse = Inverse(gContext.ViewMat);
        gContext.CameraDir = viewInverse.Z;
        gContext.CameraEye = viewInverse.W;
        gContext.CameraRight = viewInverse.X;
        gContext.CameraUp = viewInverse.Y;
        var rightViewInverse = TransformVector(viewInverse.X, gContext.ModelInverse);
        float rightLength = GetSegmentLengthClipSpace(float4.Zero, rightViewInverse);
        gContext.ScreenFactor = gContext.GizmoSizeClipSpace / rightLength;
        var center = WorldToPos(float4.Zero, gContext.MVP);
        gContext.ScreenSquareCenter = center;
        gContext.ScreenSquareMin = new float2(center.X - 10f, center.Y - 10f);
        gContext.ScreenSquareMax = new float2(center.X + 10f, center.Y + 10f);
        ComputeCameraRay();
    }

    static void ComputeColors(Span<ImColor8> colors, ImGuizmoMoveType type, ImGuizmoOperation operation)
    {
        if (gContext.EnableGizmo)
        {
            var selection = StyleColor(ImGuizmoColor.Selection);
            var white = Col32(255, 255, 255);
            switch (operation)
            {
                case ImGuizmoOperation.Translate:
                    colors[0] = type == ImGuizmoMoveType.MT_MOVE_SCREEN ? selection : white;
                    for (int i = 0; i < 3; i++)
                    {
                        colors[i + 1] = type == ImGuizmoMoveType.MT_MOVE_X + i ? selection : StyleColor(ImGuizmoColor.DirectionX + i);
                        colors[i + 4] = type == ImGuizmoMoveType.MT_MOVE_YZ + i ? selection : StyleColor(ImGuizmoColor.PlaneX + i);
                        if (type == ImGuizmoMoveType.MT_MOVE_SCREEN)
                            colors[i + 4] = selection;
                    }
                    break;
                case ImGuizmoOperation.Rotate:
                    colors[0] = type == ImGuizmoMoveType.MT_ROTATE_SCREEN ? selection : white;
                    for (int i = 0; i < 3; i++)
                        colors[i + 1] = type == ImGuizmoMoveType.MT_ROTATE_X + i ? selection : StyleColor(ImGuizmoColor.DirectionX + i);
                    break;
                case ImGuizmoOperation.ScaleU:
                case ImGuizmoOperation.Scale:
                    colors[0] = type == ImGuizmoMoveType.MT_SCALE_XYZ ? selection : white;
                    for (int i = 0; i < 3; i++)
                        colors[i + 1] = type == ImGuizmoMoveType.MT_SCALE_X + i ? selection : StyleColor(ImGuizmoColor.DirectionX + i);
                    break;
            }
        }
        else
        {
            var inactive = StyleColor(ImGuizmoColor.Inactive);
            for (int i = 0; i < 7; i++)
                colors[i] = inactive;
        }
    }

    static void ComputeTripodAxisAndVisibility(int axisIndex, out float4 dirAxis, out float4 dirPlaneX, out float4 dirPlaneY, out bool belowAxisLimit, out bool belowPlaneLimit, bool localCoordinates = false)
    {
        dirAxis = DirectionUnary[axisIndex];
        dirPlaneX = DirectionUnary[(axisIndex + 1) % 3];
        dirPlaneY = DirectionUnary[(axisIndex + 2) % 3];
        var tripod = gContext.CurrentTripod;
        if (gContext.Using && gContext.CurrentId == gContext.EditingId)
        {
            belowAxisLimit = tripod.BelowAxis[axisIndex] && ((1 << axisIndex) & gContext.AxisMask) == 0;
            belowPlaneLimit = tripod.BelowPlane[axisIndex] && (((1 << axisIndex) == gContext.AxisMask) || gContext.AxisMask == 0);
            dirAxis *= tripod.AxisFactor[axisIndex];
            dirPlaneX *= tripod.AxisFactor[(axisIndex + 1) % 3];
            dirPlaneY *= tripod.AxisFactor[(axisIndex + 2) % 3];
            return;
        }

        float lenDir = GetSegmentLengthClipSpace(float4.Zero, dirAxis, localCoordinates);
        float lenDirMinus = GetSegmentLengthClipSpace(float4.Zero, -dirAxis, localCoordinates);
        float lenDirPlaneX = GetSegmentLengthClipSpace(float4.Zero, dirPlaneX, localCoordinates);
        float lenDirMinusPlaneX = GetSegmentLengthClipSpace(float4.Zero, -dirPlaneX, localCoordinates);
        float lenDirPlaneY = GetSegmentLengthClipSpace(float4.Zero, dirPlaneY, localCoordinates);
        float lenDirMinusPlaneY = GetSegmentLengthClipSpace(float4.Zero, -dirPlaneY, localCoordinates);
        bool allowFlip = gContext.AllowAxisFlip;
        float mulAxis = allowFlip && lenDir < lenDirMinus && MathF.Abs(lenDir - lenDirMinus) > float.Epsilon ? -1f : 1f;
        float mulAxisX = allowFlip && lenDirPlaneX < lenDirMinusPlaneX && MathF.Abs(lenDirPlaneX - lenDirMinusPlaneX) > float.Epsilon ? -1f : 1f;
        float mulAxisY = allowFlip && lenDirPlaneY < lenDirMinusPlaneY && MathF.Abs(lenDirPlaneY - lenDirMinusPlaneY) > float.Epsilon ? -1f : 1f;
        dirAxis *= mulAxis;
        dirPlaneX *= mulAxisX;
        dirPlaneY *= mulAxisY;
        float axisLength = GetSegmentLengthClipSpace(float4.Zero, dirAxis * gContext.ScreenFactor, localCoordinates);
        float paraSurf = GetParallelogram(float4.Zero, dirPlaneX * gContext.ScreenFactor, dirPlaneY * gContext.ScreenFactor);
        belowPlaneLimit = paraSurf > gContext.AxisLimit && (((1 << axisIndex) == gContext.AxisMask) || gContext.AxisMask == 0);
        belowAxisLimit = axisLength > gContext.PlaneLimit && ((1 << axisIndex) & gContext.AxisMask) == 0;
        tripod.AxisFactor[axisIndex] = mulAxis;
        tripod.AxisFactor[(axisIndex + 1) % 3] = mulAxisX;
        tripod.AxisFactor[(axisIndex + 2) % 3] = mulAxisY;
        tripod.BelowAxis[axisIndex] = belowAxisLimit;
        tripod.BelowPlane[axisIndex] = belowPlaneLimit;
    }

    static void ComputeSnap(ref float value, float snap)
    {
        if (snap <= float.Epsilon)
            return;
        float modulo = value % snap;
        float moduloRatio = MathF.Abs(modulo) / snap;
        if (moduloRatio < SnapTension)
            value -= modulo;
        else if (moduloRatio > 1f - SnapTension)
            value = value - modulo + snap * (value < 0f ? -1f : 1f);
    }

    static void ComputeSnap(ref float4 value, float3 snap)
    {
        float x = value.X, y = value.Y, z = value.Z;
        ComputeSnap(ref x, snap.X);
        ComputeSnap(ref y, snap.Y);
        ComputeSnap(ref z, snap.Z);
        value = new float4(x, y, z, value.W);
    }

    static float ComputeAngleOnPlan()
    {
        float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
        var localPos = Normalized(gContext.RayOrigin + gContext.RayVector * len - gContext.Model.W);
        var perpendicular = Normalized(Cross(gContext.RotationVectorSource, gContext.TranslationPlan));
        float angle = MathF.Acos(Clamp(Dot3(localPos, gContext.RotationVectorSource), -1f, 1f));
        angle *= Dot3(localPos, perpendicular) < 0f ? 1f : -1f;
        return angle;
    }

    static bool CanActivate() => ImGui.IsMouseClicked(ImGuiMouseButton.Left, false) && !ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive();

    static void WantMouse() => ImGui.SetNextFrameWantCaptureMouse(true);

    static ImGuizmoMoveType GetScaleType(ImGuizmoOperation op)
    {
        if (gContext.Using)
            return ImGuizmoMoveType.MT_NONE;
        var io = ImGui.GetIO();
        var type = ImGuizmoMoveType.MT_NONE;
        if (io.MousePos.X >= gContext.ScreenSquareMin.X && io.MousePos.X <= gContext.ScreenSquareMax.X &&
            io.MousePos.Y >= gContext.ScreenSquareMin.Y && io.MousePos.Y <= gContext.ScreenSquareMax.Y &&
            Contains(op, ImGuizmoOperation.Scale))
            type = ImGuizmoMoveType.MT_SCALE_XYZ;

        for (int i = 0; i < 3 && type == ImGuizmoMoveType.MT_NONE; i++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.ScaleX << i)))
                continue;
            bool isAxisMasked = ((1 << i) & gContext.AxisMask) != 0;
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out _, out _, out _, out _, true);
            dirAxis = TransformVector(dirAxis, gContext.ModelLocal);
            float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, BuildPlan(gContext.ModelLocal.W, dirAxis));
            var posOnPlan = gContext.RayOrigin + gContext.RayVector * len;
            float startOffset = Contains(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i)) ? 1f : 0.1f;
            float endOffset = Contains(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i)) ? 1.4f : 1f;
            var posOnPlanScreen = WorldToPos(posOnPlan, gContext.ViewProjection);
            var axisStart = WorldToPos(gContext.ModelLocal.W + dirAxis * gContext.ScreenFactor * startOffset, gContext.ViewProjection);
            var axisEnd = WorldToPos(gContext.ModelLocal.W + dirAxis * gContext.ScreenFactor * endOffset, gContext.ViewProjection);
            var closest = PointOnSegment(new float4(posOnPlanScreen.X, posOnPlanScreen.Y, 0f, 0f), new float4(axisStart.X, axisStart.Y, 0f, 0f), new float4(axisEnd.X, axisEnd.Y, 0f, 0f));
            if (Len3(closest - new float4(posOnPlanScreen.X, posOnPlanScreen.Y, 0f, 0f)) < 12f && !isAxisMasked)
                type = ImGuizmoMoveType.MT_SCALE_X + i;
        }

        var deltaScreen = new float4(io.MousePos.X - gContext.ScreenSquareCenter.X, io.MousePos.Y - gContext.ScreenSquareCenter.Y, 0f, 0f);
        float dist = Len3(deltaScreen);
        if (Contains(op, ImGuizmoOperation.ScaleU) && dist >= 17f && dist < 23f)
            type = ImGuizmoMoveType.MT_SCALE_XYZ;

        for (int i = 0; i < 3 && type == ImGuizmoMoveType.MT_NONE; i++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.ScaleXU << i)))
                continue;
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out _, out _, out bool belowAxisLimit, out _, true);
            if (!belowAxisLimit)
                continue;
            bool hasTranslate = Contains(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i));
            float markerScale = hasTranslate ? 1.4f : 1f;
            var worldDir = WorldToPos(dirAxis * markerScale * gContext.ScreenFactor, gContext.MVPLocal);
            float dx = worldDir.X - io.MousePos.X;
            float dy = worldDir.Y - io.MousePos.Y;
            if (MathF.Sqrt(dx * dx + dy * dy) < 12f)
                type = ImGuizmoMoveType.MT_SCALE_X + i;
        }
        return type;
    }

    static ImGuizmoMoveType GetRotateType(ImGuizmoOperation op)
    {
        if (gContext.Using)
            return ImGuizmoMoveType.MT_NONE;
        bool isNoAxesMasked = gContext.AxisMask == 0;
        bool isMultipleAxesMasked = (gContext.AxisMask & (gContext.AxisMask - 1)) != 0;
        var io = ImGui.GetIO();
        var type = ImGuizmoMoveType.MT_NONE;
        float dist = MathF.Sqrt(MathF.Pow(io.MousePos.X - gContext.ScreenSquareCenter.X, 2) + MathF.Pow(io.MousePos.Y - gContext.ScreenSquareCenter.Y, 2));
        if (Intersects(op, ImGuizmoOperation.RotateScreen) && dist >= gContext.RadiusSquareCenter - 4f && dist < gContext.RadiusSquareCenter + 4f)
        {
            if (!isNoAxesMasked)
                return ImGuizmoMoveType.MT_NONE;
            type = ImGuizmoMoveType.MT_ROTATE_SCREEN;
        }

        var planNormals = new[] { gContext.Model.X, gContext.Model.Y, gContext.Model.Z };
        var modelViewPos = TransformPoint(gContext.Model.W, gContext.ViewMat);
        for (int i = 0; i < 3 && type == ImGuizmoMoveType.MT_NONE; i++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.RotateX << i)))
                continue;
            bool isAxisMasked = ((1 << i) & gContext.AxisMask) != 0;
            var pickupPlan = BuildPlan(gContext.Model.W, planNormals[i]);
            float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, pickupPlan);
            var intersectWorldPos = gContext.RayOrigin + gContext.RayVector * len;
            var intersectViewPos = TransformPoint(intersectWorldPos, gContext.ViewMat);
            if (MathF.Abs(modelViewPos.Z) - MathF.Abs(intersectViewPos.Z) < -float.Epsilon)
                continue;
            var localPos = intersectWorldPos - gContext.Model.W;
            var ideal = Normalized(localPos);
            ideal = TransformVector(ideal, gContext.ModelInverse);
            var idealScreen = WorldToPos(ideal * RotationDisplayFactor * gContext.ScreenFactor, gContext.MVP);
            float sdx = idealScreen.X - io.MousePos.X;
            float sdy = idealScreen.Y - io.MousePos.Y;
            if (MathF.Sqrt(sdx * sdx + sdy * sdy) < 8f)
            {
                if ((!isAxisMasked || isMultipleAxesMasked) && !isNoAxesMasked)
                    break;
                type = ImGuizmoMoveType.MT_ROTATE_X + i;
            }
        }
        return type;
    }

    static ImGuizmoMoveType GetMoveType(ImGuizmoOperation op)
    {
        if (!Intersects(op, ImGuizmoOperation.Translate) || gContext.Using || !gContext.MouseOver)
            return ImGuizmoMoveType.MT_NONE;
        bool isNoAxesMasked = gContext.AxisMask == 0;
        bool isMultipleAxesMasked = (gContext.AxisMask & (gContext.AxisMask - 1)) != 0;
        var io = ImGui.GetIO();
        var type = ImGuizmoMoveType.MT_NONE;
        if (io.MousePos.X >= gContext.ScreenSquareMin.X && io.MousePos.X <= gContext.ScreenSquareMax.X &&
            io.MousePos.Y >= gContext.ScreenSquareMin.Y && io.MousePos.Y <= gContext.ScreenSquareMax.Y &&
            Contains(op, ImGuizmoOperation.Translate))
            type = ImGuizmoMoveType.MT_MOVE_SCREEN;

        var screenCoord = new float4(io.MousePos.X - gContext.X, io.MousePos.Y - gContext.Y, 0f, 0f);
        for (int i = 0; i < 3 && type == ImGuizmoMoveType.MT_NONE; i++)
        {
            bool isAxisMasked = ((1 << i) & gContext.AxisMask) != 0;
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out var dirPlaneX, out var dirPlaneY, out _, out bool belowPlaneLimit);
            dirAxis = TransformVector(dirAxis, gContext.Model);
            dirPlaneX = TransformVector(dirPlaneX, gContext.Model);
            dirPlaneY = TransformVector(dirPlaneY, gContext.Model);
            float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, BuildPlan(gContext.Model.W, dirAxis));
            var posOnPlan = gContext.RayOrigin + gContext.RayVector * len;
            var axisStart = WorldToPos(gContext.Model.W + dirAxis * gContext.ScreenFactor * 0.1f, gContext.ViewProjection);
            var axisEnd = WorldToPos(gContext.Model.W + dirAxis * gContext.ScreenFactor, gContext.ViewProjection);
            var axisStartLocal = new float4(axisStart.X - gContext.X, axisStart.Y - gContext.Y, 0f, 0f);
            var axisEndLocal = new float4(axisEnd.X - gContext.X, axisEnd.Y - gContext.Y, 0f, 0f);
            var closest = PointOnSegment(screenCoord, axisStartLocal, axisEndLocal);
            if (Len3(closest - screenCoord) < 12f && Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i)))
            {
                if (isAxisMasked)
                    break;
                type = ImGuizmoMoveType.MT_MOVE_X + i;
            }
            var rel = (posOnPlan - gContext.Model.W) * (1f / gContext.ScreenFactor);
            float dx = Dot3(dirPlaneX, rel);
            float dy = Dot3(dirPlaneY, rel);
            if (belowPlaneLimit && dx >= QuadUV[0] && dx <= QuadUV[4] && dy >= QuadUV[1] && dy <= QuadUV[3] && Contains(op, TranslatePlans[i]))
            {
                if ((!isAxisMasked || isMultipleAxesMasked) && !isNoAxesMasked)
                    break;
                type = ImGuizmoMoveType.MT_MOVE_YZ + i;
            }
        }
        return type;
    }

    static bool HandleTranslation(ref float4x4 matrix, ref float4x4 deltaMatrix, bool writeDelta, ImGuizmoOperation op, ref ImGuizmoMoveType type, float3? snap)
    {
        if (!Intersects(op, ImGuizmoOperation.Translate) || type != ImGuizmoMoveType.MT_NONE)
            return false;
        var io = ImGui.GetIO();
        bool applyRotationLocally = gContext.Mode == ImGuizmoMode.Local || gContext.CurrentHandleType == ImGuizmoMoveType.MT_MOVE_SCREEN;
        bool modified = false;
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsTranslateType(gContext.CurrentHandleType))
        {
            WantMouse();
            float signedLength = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
            float len = MathF.Abs(signedLength);
            var newPos = gContext.RayOrigin + gContext.RayVector * len;
            var newOrigin = newPos - gContext.RelativeOrigin * gContext.ScreenFactor;
            var delta = newOrigin - gContext.Model.W;
            if (gContext.CurrentHandleType is >= ImGuizmoMoveType.MT_MOVE_X and <= ImGuizmoMoveType.MT_MOVE_Z)
            {
                int axisIndex = gContext.CurrentHandleType - ImGuizmoMoveType.MT_MOVE_X;
                var axisValue = Row(gContext.Model, axisIndex);
                delta = axisValue * Dot3(axisValue, delta);
            }
            if (snap is float3 snapValue)
            {
                var cumulative = gContext.Model.W + delta - gContext.MatrixOrigin;
                if (applyRotationLocally)
                {
                    var modelSourceNormalized = gContext.ModelSource;
                    OrthoNormalize(ref modelSourceNormalized);
                    var inverse = Inverse(modelSourceNormalized);
                    cumulative = TransformVector(cumulative, inverse);
                    ComputeSnap(ref cumulative, snapValue);
                    cumulative = TransformVector(cumulative, modelSourceNormalized);
                }
                else
                {
                    ComputeSnap(ref cumulative, snapValue);
                }
                delta = gContext.MatrixOrigin + cumulative - gContext.Model.W;
            }
            if (delta.X != gContext.TranslationLastDelta.X || delta.Y != gContext.TranslationLastDelta.Y || delta.Z != gContext.TranslationLastDelta.Z || delta.W != gContext.TranslationLastDelta.W)
                modified = true;
            gContext.TranslationLastDelta = delta;
            var deltaTranslation = Translation(delta);
            if (writeDelta)
                deltaMatrix = deltaTranslation;
            matrix = gContext.ModelSource * deltaTranslation;
            if (!io.MouseDown[0])
                gContext.Using = false;
            type = gContext.CurrentHandleType;
        }
        else
        {
            type = gContext.OverGizmoHotspot ? ImGuizmoMoveType.MT_NONE : GetMoveType(op);
            gContext.OverGizmoHotspot |= type != ImGuizmoMoveType.MT_NONE;
            if (type != ImGuizmoMoveType.MT_NONE)
                WantMouse();
            if (CanActivate() && type != ImGuizmoMoveType.MT_NONE)
            {
                CopyTripod(gContext.Tripod, gContext.ActiveTripod);
                gContext.Using = true;
                gContext.EditingId = gContext.CurrentId;
                gContext.CurrentHandleType = type;
                var movePlanNormal = new[]
                {
                    gContext.Model.X, gContext.Model.Y, gContext.Model.Z,
                    gContext.Model.X, gContext.Model.Y, gContext.Model.Z,
                    -gContext.CameraDir,
                };
                var cameraToModel = Normalized(gContext.Model.W - gContext.CameraEye);
                for (int i = 0; i < 3; i++)
                {
                    var ortho = Cross(movePlanNormal[i], cameraToModel);
                    movePlanNormal[i] = Normalized(Cross(movePlanNormal[i], ortho));
                }
                gContext.TranslationPlan = BuildPlan(gContext.Model.W, movePlanNormal[type - ImGuizmoMoveType.MT_MOVE_X]);
                float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
                gContext.TranslationPlanOrigin = gContext.RayOrigin + gContext.RayVector * len;
                gContext.MatrixOrigin = gContext.Model.W;
                gContext.RelativeOrigin = (gContext.TranslationPlanOrigin - gContext.Model.W) * (1f / gContext.ScreenFactor);
            }
        }
        return modified;
    }

    static bool HandleScale(ref float4x4 matrix, ref float4x4 deltaMatrix, bool writeDelta, ImGuizmoOperation op, ref ImGuizmoMoveType type, float3? snap)
    {
        if ((!Intersects(op, ImGuizmoOperation.Scale) && !Intersects(op, ImGuizmoOperation.ScaleU)) || type != ImGuizmoMoveType.MT_NONE || !gContext.MouseOver)
            return false;
        var io = ImGui.GetIO();
        bool modified = false;
        if (!gContext.Using)
        {
            type = gContext.OverGizmoHotspot ? ImGuizmoMoveType.MT_NONE : GetScaleType(op);
            gContext.OverGizmoHotspot |= type != ImGuizmoMoveType.MT_NONE;
            if (type != ImGuizmoMoveType.MT_NONE)
                WantMouse();
            if (CanActivate() && type != ImGuizmoMoveType.MT_NONE)
            {
                CopyTripod(gContext.Tripod, gContext.ActiveTripod);
                gContext.Using = true;
                gContext.EditingId = gContext.CurrentId;
                gContext.CurrentHandleType = type;
                var movePlanNormal = new[]
                {
                    gContext.ModelLocal.Y, gContext.ModelLocal.Z, gContext.ModelLocal.X,
                    gContext.ModelLocal.Z, gContext.ModelLocal.Y, gContext.ModelLocal.X,
                    -gContext.CameraDir,
                };
                gContext.TranslationPlan = BuildPlan(gContext.ModelLocal.W, movePlanNormal[type - ImGuizmoMoveType.MT_SCALE_X]);
                float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
                gContext.TranslationPlanOrigin = gContext.RayOrigin + gContext.RayVector * len;
                gContext.MatrixOrigin = gContext.ModelLocal.W;
                gContext.Scale = new float4(1f, 1f, 1f, 0f);
                gContext.RelativeOrigin = (gContext.TranslationPlanOrigin - gContext.ModelLocal.W) * (1f / gContext.ScreenFactor);
                gContext.ScaleValueOrigin = new float4(Len3(gContext.ModelSource.X), Len3(gContext.ModelSource.Y), Len3(gContext.ModelSource.Z), 0f);
                gContext.SaveMousePosX = io.MousePos.X;
            }
        }
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsScaleType(gContext.CurrentHandleType))
        {
            WantMouse();
            float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
            var newPos = gContext.RayOrigin + gContext.RayVector * len;
            var newOrigin = newPos - gContext.RelativeOrigin * gContext.ScreenFactor;
            var delta = newOrigin - gContext.ModelLocal.W;
            if (gContext.CurrentHandleType is >= ImGuizmoMoveType.MT_SCALE_X and <= ImGuizmoMoveType.MT_SCALE_Z)
            {
                int axisIndex = gContext.CurrentHandleType - ImGuizmoMoveType.MT_SCALE_X;
                var axisValue = Row(gContext.ModelLocal, axisIndex);
                float lengthOnAxis = Dot3(axisValue, delta);
                delta = axisValue * lengthOnAxis;
                var baseVector = gContext.TranslationPlanOrigin - gContext.ModelLocal.W;
                float ratio = Dot3(axisValue, baseVector + delta) / Dot3(axisValue, baseVector);
                gContext.Scale = SetComp(gContext.Scale, axisIndex, MathF.Max(ratio, 0.001f));
            }
            else
            {
                float scaleDelta = (io.MousePos.X - gContext.SaveMousePosX) * 0.01f;
                float uniform = MathF.Max(1f + scaleDelta, 0.001f);
                gContext.Scale = new float4(uniform, uniform, uniform, 0f);
            }
            if (snap is float3 snapValue)
                ComputeSnap(ref gContext.Scale, new float3(snapValue.X, snapValue.X, snapValue.X));
            gContext.Scale = new float4(MathF.Max(gContext.Scale.X, 0.001f), MathF.Max(gContext.Scale.Y, 0.001f), MathF.Max(gContext.Scale.Z, 0.001f), gContext.Scale.W);
            if (gContext.ScaleLast.X != gContext.Scale.X || gContext.ScaleLast.Y != gContext.Scale.Y || gContext.ScaleLast.Z != gContext.Scale.Z)
                modified = true;
            gContext.ScaleLast = gContext.Scale;
            var deltaScale = gContext.Scale * gContext.ScaleValueOrigin;
            matrix = ScaleMatrix(deltaScale) * gContext.ModelLocal;
            if (writeDelta)
            {
                var divider = new float4(1f / gContext.ModelScaleOrigin.X, 1f / gContext.ModelScaleOrigin.Y, 1f / gContext.ModelScaleOrigin.Z, 0f);
                deltaMatrix = ScaleMatrix(new float4(deltaScale.X * divider.X, deltaScale.Y * divider.Y, deltaScale.Z * divider.Z, 0f));
            }
            if (!io.MouseDown[0])
            {
                gContext.Using = false;
                gContext.Scale = new float4(1f, 1f, 1f, 0f);
            }
            type = gContext.CurrentHandleType;
        }
        return modified;
    }

    static bool HandleRotation(ref float4x4 matrix, ref float4x4 deltaMatrix, bool writeDelta, ImGuizmoOperation op, ref ImGuizmoMoveType type, float3? snap)
    {
        if (!Intersects(op, ImGuizmoOperation.Rotate) || type != ImGuizmoMoveType.MT_NONE || !gContext.MouseOver)
            return false;
        var io = ImGui.GetIO();
        bool applyRotationLocally = gContext.Mode == ImGuizmoMode.Local;
        bool modified = false;
        if (!gContext.Using)
        {
            type = gContext.OverGizmoHotspot ? ImGuizmoMoveType.MT_NONE : GetRotateType(op);
            gContext.OverGizmoHotspot |= type != ImGuizmoMoveType.MT_NONE;
            if (type != ImGuizmoMoveType.MT_NONE)
                WantMouse();
            if (type == ImGuizmoMoveType.MT_ROTATE_SCREEN)
                applyRotationLocally = true;
            if (CanActivate() && type != ImGuizmoMoveType.MT_NONE)
            {
                gContext.Using = true;
                gContext.EditingId = gContext.CurrentId;
                gContext.CurrentHandleType = type;
                var rotatePlanNormal = new[] { gContext.Model.X, gContext.Model.Y, gContext.Model.Z, -gContext.CameraDir };
                gContext.TranslationPlan = applyRotationLocally
                    ? BuildPlan(gContext.Model.W, rotatePlanNormal[type - ImGuizmoMoveType.MT_ROTATE_X])
                    : BuildPlan(gContext.ModelSource.W, DirectionUnary[type - ImGuizmoMoveType.MT_ROTATE_X]);
                float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.TranslationPlan);
                var localPos = gContext.RayOrigin + gContext.RayVector * len - gContext.Model.W;
                gContext.RotationVectorSource = Normalized(localPos);
                gContext.RotationAngleOrigin = ComputeAngleOnPlan();
            }
        }
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsRotateType(gContext.CurrentHandleType))
        {
            WantMouse();
            gContext.RotationAngle = ComputeAngleOnPlan();
            if (snap is float3 snapValue)
            {
                float angle = gContext.RotationAngle;
                ComputeSnap(ref angle, snapValue.X * Deg2Rad);
                gContext.RotationAngle = angle;
            }
            var rotationAxisLocalSpace = Normalized(TransformVector(new float4(gContext.TranslationPlan.X, gContext.TranslationPlan.Y, gContext.TranslationPlan.Z, 0f), gContext.ModelInverse));
            var deltaRotation = RotationAxis(rotationAxisLocalSpace, gContext.RotationAngle - gContext.RotationAngleOrigin);
            if (gContext.RotationAngle != gContext.RotationAngleOrigin)
                modified = true;
            gContext.RotationAngleOrigin = gContext.RotationAngle;
            var scaleOrigin = ScaleMatrix(gContext.ModelScaleOrigin);
            if (applyRotationLocally)
            {
                matrix = scaleOrigin * deltaRotation * gContext.ModelLocal;
            }
            else
            {
                var res = gContext.ModelSource;
                res.W = float4.Zero;
                matrix = res * deltaRotation;
                matrix.W = gContext.ModelSource.W;
            }
            if (writeDelta)
                deltaMatrix = gContext.ModelInverse * deltaRotation * gContext.Model;
            if (!io.MouseDown[0])
            {
                gContext.Using = false;
                gContext.EditingId = new ImGuiID(uint.MaxValue);
            }
            type = gContext.CurrentHandleType;
        }
        return modified;
    }

    static bool Manipulate(float4x4 view, float4x4 projection, ImGuizmoOperation operation, ImGuizmoMode mode, ref float4x4 matrix, ref float4x4 deltaMatrix, float3? snap, (float3 Min, float3 Max)? localBounds, float3? boundsSnap, bool writeDelta)
    {
        var drawList = RequireDrawList();
        var clipMin = new float2(gContext.X, gContext.Y);
        var clipMax = new float2(gContext.X + gContext.Width, gContext.Y + gContext.Height);
        drawList.PushClipRect(in clipMin, in clipMax, false);
        try
        {
            ComputeContext(view, projection, matrix, Intersects(operation, ImGuizmoOperation.Scale) ? ImGuizmoMode.Local : mode);
            gContext.HoveredHandleType = ImGuizmoMoveType.MT_NONE;
            if (writeDelta)
                deltaMatrix = float4x4.Identity;

            var camSpacePosition = TransformPoint(float4.Zero, gContext.MVP);
            if (!gContext.IsOrthographic && camSpacePosition.Z < 0.001f && !gContext.Using)
                return false;

            var type = ImGuizmoMoveType.MT_NONE;
            bool manipulated = false;
            if (gContext.EnableGizmo && !gContext.UsingBounds)
            {
                manipulated = HandleTranslation(ref matrix, ref deltaMatrix, writeDelta, operation, ref type, snap)
                    || HandleScale(ref matrix, ref deltaMatrix, writeDelta, operation, ref type, snap)
                    || HandleRotation(ref matrix, ref deltaMatrix, writeDelta, operation, ref type, snap);
            }
            if (localBounds is (float3 min, float3 max) && !gContext.Using)
            {
                Span<float> bounds = stackalloc float[6];
                bounds[0] = min.X; bounds[1] = min.Y; bounds[2] = min.Z;
                bounds[3] = max.X; bounds[4] = max.Y; bounds[5] = max.Z;
                manipulated |= HandleAndDrawLocalBounds(bounds, ref matrix, boundsSnap, operation);
            }
            gContext.Operation = operation;
            gContext.HoveredHandleType = !gContext.Using && !gContext.UsingBounds ? type : ImGuizmoMoveType.MT_NONE;
            if (!gContext.UsingBounds)
            {
                DrawRotationGizmo(operation, type);
                DrawTranslationGizmo(operation, type);
                DrawScaleGizmo(operation, type);
                DrawScaleUniversalGizmo(operation, type);
            }
            return manipulated;
        }
        finally
        {
            drawList.PopClipRect();
        }
    }
}
