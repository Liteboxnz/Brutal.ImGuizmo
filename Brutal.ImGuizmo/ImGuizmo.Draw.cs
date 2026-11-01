using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace Brutal.ImGuizmoAPI;

public static partial class ImGuizmo
{
    static void Line(ImDrawListPtr dl, float2 a, float2 b, ImColor8 color, float thickness) => dl.AddLine(a, b, color, thickness);
    static void Circle(ImDrawListPtr dl, float2 center, float radius, ImColor8 color, int segments, float thickness) => dl.AddCircle(center, radius, color, segments, thickness);
    static void CircleFilled(ImDrawListPtr dl, float2 center, float radius, ImColor8 color, int segments) => dl.AddCircleFilled(center, radius, color, segments);
    static void TriangleFilled(ImDrawListPtr dl, float2 a, float2 b, float2 c, ImColor8 color) => dl.AddTriangleFilled(a, b, c, color);
    static void Text(ImDrawListPtr dl, float2 pos, ImColor8 color, string text) => dl.AddText(pos, color, text);
    static void Polyline(ImDrawListPtr dl, ReadOnlySpan<float2> pts, ImColor8 color, ImDrawFlags flags, float thickness) => dl.AddPolyline(pts, color, flags, thickness);
    static void ConvexFilled(ImDrawListPtr dl, ReadOnlySpan<float2> pts, ImColor8 color) => dl.AddConvexPolyFilled(pts, color);
    static void RectFilled(ImDrawListPtr dl, float2 min, float2 max, ImColor8 color) => dl.AddRectFilled(min, max, color, 0f, ImDrawFlags.None);

    static float2 V2(float4 v) => new(v.X, v.Y);
    static float Len2(float2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    static void DrawInfo(ImDrawListPtr dl, float2 pos, string text)
    {
        var shadow = new float2(pos.X + 15f, pos.Y + 15f);
        var front = new float2(pos.X + 14f, pos.Y + 14f);
        Text(dl, shadow, StyleColor(ImGuizmoColor.TextShadow), text);
        Text(dl, front, StyleColor(ImGuizmoColor.Text), text);
    }

    static void DrawHatchedAxis(float4 axis)
    {
        if (gContext.Style.HatchedAxisLineThickness <= 0f)
            return;
        var dl = RequireDrawList();
        var color = StyleColor(ImGuizmoColor.HatchedAxisLines);
        for (int j = 1; j < 10; j++)
        {
            var a = WorldToPos(axis * 0.05f * (j * 2) * gContext.ScreenFactor, gContext.MVP);
            var b = WorldToPos(axis * 0.05f * (j * 2 + 1) * gContext.ScreenFactor, gContext.MVP);
            Line(dl, a, b, color, gContext.Style.HatchedAxisLineThickness);
        }
    }

    static void DrawRotationGizmo(ImGuizmoOperation op, ImGuizmoMoveType type)
    {
        if (!Intersects(op, ImGuizmoOperation.Rotate))
            return;
        var drawList = RequireDrawList();
        bool isMultipleAxesMasked = (gContext.AxisMask & (gContext.AxisMask - 1)) != 0;
        bool isNoAxesMasked = gContext.AxisMask == 0;
        Span<ImColor8> colors = stackalloc ImColor8[7];
        ComputeColors(colors, type, ImGuizmoOperation.Rotate);

        float4 viewDirNormalized;
        var viewInverse = Inverse(gContext.ViewMat);
        if (gContext.IsOrthographic)
        {
            viewDirNormalized = -viewInverse.Z;
        }
        else
        {
            float handSign = Dot3(Cross(viewInverse.X, viewInverse.Y), viewInverse.Z) >= 0f ? 1f : -1f;
            viewDirNormalized = Normalized(gContext.CameraDir) * handSign;
        }
        viewDirNormalized = TransformVector(viewDirNormalized, gContext.ModelInverse);
        gContext.RadiusSquareCenter = ScreenRotateSize * gContext.Height;
        bool hasScreen = Intersects(op, ImGuizmoOperation.RotateScreen);
        for (int axis = 0; axis < 3; axis++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.RotateZ >> axis)))
                continue;
            bool isAxisMasked = ((1 << (2 - axis)) & gContext.AxisMask) != 0;
            if ((!isAxisMasked || isMultipleAxesMasked) && !isNoAxesMasked)
                continue;
            bool usingAxis = gContext.Using && type == ImGuizmoMoveType.MT_ROTATE_Z - axis;
            int circleMul = hasScreen && !usingAxis ? 1 : 2;
            int count = circleMul * HalfCircleSegmentCount + 1;
            var circlePos = new float2[count];
            bool rightHanded = At(gContext.ProjectionMat, 2, 3) < 0f;
            float angleStart = MathF.Atan2(Comp(viewDirNormalized, (4 - axis) % 3), Comp(viewDirNormalized, (3 - axis) % 3))
                + (gContext.IsOrthographic ? Pi : -Pi) * 0.5f + (rightHanded ? 0f : Pi);
            for (int i = 0; i < count; i++)
            {
                float ng = angleStart + circleMul * Pi * (i / (float)(circleMul * HalfCircleSegmentCount));
                var axisPos = new float4(MathF.Cos(ng), MathF.Sin(ng), 0f, 0f);
                var pos = new float4(Comp(axisPos, axis), Comp(axisPos, (axis + 1) % 3), Comp(axisPos, (axis + 2) % 3), 0f) * gContext.ScreenFactor * RotationDisplayFactor;
                circlePos[i] = WorldToPos(pos, gContext.MVP);
            }
            if (!gContext.Using || usingAxis)
                Polyline(drawList, circlePos, colors[3 - axis], ImDrawFlags.None, gContext.Style.RotationLineThickness);
            var originScreen = WorldToPos(gContext.Model.W, gContext.ViewProjection);
            float radiusAxis = Len2(originScreen - circlePos[0]);
            if (radiusAxis > gContext.RadiusSquareCenter)
                gContext.RadiusSquareCenter = radiusAxis;
        }
        if (hasScreen && (!gContext.Using || type == ImGuizmoMoveType.MT_ROTATE_SCREEN) && !isMultipleAxesMasked && isNoAxesMasked)
            Circle(drawList, WorldToPos(gContext.Model.W, gContext.ViewProjection), gContext.RadiusSquareCenter, colors[0], 64, gContext.Style.RotationOuterLineThickness);

        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsRotateType(type))
        {
            var circlePos = new float2[HalfCircleSegmentCount + 1];
            circlePos[0] = WorldToPos(gContext.Model.W, gContext.ViewProjection);
            for (int i = 1; i < HalfCircleSegmentCount + 1; i++)
            {
                float ng = gContext.RotationAngle * ((i - 1) / (float)(HalfCircleSegmentCount - 1));
                var rotate = RotationAxis(gContext.TranslationPlan, ng);
                var pos = TransformPoint(gContext.RotationVectorSource, rotate) * gContext.ScreenFactor * RotationDisplayFactor;
                circlePos[i] = WorldToPos(pos + gContext.Model.W, gContext.ViewProjection);
            }
            ConvexFilled(drawList, circlePos, StyleColor(ImGuizmoColor.RotationUsingFill));
            Polyline(drawList, circlePos, StyleColor(ImGuizmoColor.RotationUsingBorder), ImDrawFlags.Closed, gContext.Style.RotationLineThickness);
            float degrees = gContext.RotationAngle / Pi * 180f;
            DrawInfo(drawList, circlePos[1], $"X/Y/Z : {degrees,5:0.00} deg {gContext.RotationAngle,5:0.00} rad".Replace("X/Y/Z", RotationLabel(type)));
        }
    }

    static string RotationLabel(ImGuizmoMoveType type) => type switch
    {
        ImGuizmoMoveType.MT_ROTATE_X => "X",
        ImGuizmoMoveType.MT_ROTATE_Y => "Y",
        ImGuizmoMoveType.MT_ROTATE_Z => "Z",
        _ => "Screen",
    };

    static void DrawScaleGizmo(ImGuizmoOperation op, ImGuizmoMoveType type)
    {
        if (!Intersects(op, ImGuizmoOperation.Scale))
            return;
        var drawList = RequireDrawList();
        Span<ImColor8> colors = stackalloc ImColor8[7];
        ComputeColors(colors, type, ImGuizmoOperation.Scale);
        var scaleDisplay = new float4(1f, 1f, 1f, 1f);
        if (gContext.Using && gContext.CurrentId == gContext.EditingId)
            scaleDisplay = gContext.Scale;
        for (int i = 0; i < 3; i++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.ScaleX << i)))
                continue;
            bool usingAxis = gContext.Using && type == ImGuizmoMoveType.MT_SCALE_X + i;
            if (gContext.Using && !usingAxis)
                continue;
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out _, out _, out bool belowAxisLimit, out _, true);
            if (!belowAxisLimit)
                continue;
            bool hasTranslate = Contains(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i));
            float markerScale = hasTranslate ? 1.4f : 1f;
            var baseSSpace = WorldToPos(dirAxis * 0.1f * gContext.ScreenFactor, gContext.MVP);
            var worldDirNoScale = WorldToPos(dirAxis * markerScale * gContext.ScreenFactor, gContext.MVP);
            var worldDir = WorldToPos(dirAxis * markerScale * Comp(scaleDisplay, i) * gContext.ScreenFactor, gContext.MVP);
            if (gContext.Using && gContext.CurrentId == gContext.EditingId)
            {
                var scaleLine = StyleColor(ImGuizmoColor.ScaleLine);
                Line(drawList, baseSSpace, worldDirNoScale, scaleLine, gContext.Style.ScaleLineThickness);
                CircleFilled(drawList, worldDirNoScale, gContext.Style.ScaleLineCircleSize, scaleLine, 0);
            }
            if (!hasTranslate || gContext.Using)
                Line(drawList, baseSSpace, worldDir, colors[i + 1], gContext.Style.ScaleLineThickness);
            CircleFilled(drawList, worldDir, gContext.Style.ScaleLineCircleSize, colors[i + 1], 0);
            if (gContext.CurrentTripod.AxisFactor[i] < 0f)
                DrawHatchedAxis(dirAxis * Comp(scaleDisplay, i));
        }
        CircleFilled(drawList, gContext.ScreenSquareCenter, gContext.Style.CenterCircleSize, colors[0], 32);
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsScaleType(type))
        {
            var destination = WorldToPos(gContext.Model.W, gContext.ViewProjection);
            int component = TranslationInfoIndex[(type - ImGuizmoMoveType.MT_SCALE_X) * 3];
            DrawInfo(drawList, destination, ScaleLabel(type, Comp(scaleDisplay, component)));
        }
    }

    static string ScaleLabel(ImGuizmoMoveType type, float value) => type switch
    {
        ImGuizmoMoveType.MT_SCALE_X => $"X : {value,5:0.00}",
        ImGuizmoMoveType.MT_SCALE_Y => $"Y : {value,5:0.00}",
        ImGuizmoMoveType.MT_SCALE_Z => $"Z : {value,5:0.00}",
        _ => $"XYZ : {value,5:0.00}",
    };

    static void DrawScaleUniversalGizmo(ImGuizmoOperation op, ImGuizmoMoveType type)
    {
        if (!Intersects(op, ImGuizmoOperation.ScaleU))
            return;
        var drawList = RequireDrawList();
        Span<ImColor8> colors = stackalloc ImColor8[7];
        ComputeColors(colors, type, ImGuizmoOperation.ScaleU);
        var scaleDisplay = new float4(1f, 1f, 1f, 1f);
        if (gContext.Using && gContext.CurrentId == gContext.EditingId)
            scaleDisplay = gContext.Scale;
        for (int i = 0; i < 3; i++)
        {
            if (!Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.ScaleXU << i)))
                continue;
            bool usingAxis = gContext.Using && type == ImGuizmoMoveType.MT_SCALE_X + i;
            if (gContext.Using && !usingAxis)
                continue;
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out _, out _, out bool belowAxisLimit, out _, true);
            if (!belowAxisLimit)
                continue;
            bool hasTranslate = Contains(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i));
            float markerScale = hasTranslate ? 1.4f : 1f;
            var worldDir = WorldToPos(dirAxis * markerScale * Comp(scaleDisplay, i) * gContext.ScreenFactor, gContext.MVPLocal);
            CircleFilled(drawList, worldDir, 12f, colors[i + 1], 0);
        }
        Circle(drawList, gContext.ScreenSquareCenter, 20f, colors[0], 32, gContext.Style.CenterCircleSize);
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsScaleType(type))
        {
            var destination = WorldToPos(gContext.Model.W, gContext.ViewProjection);
            int component = TranslationInfoIndex[(type - ImGuizmoMoveType.MT_SCALE_X) * 3];
            DrawInfo(drawList, destination, ScaleLabel(type, Comp(scaleDisplay, component)));
        }
    }

    static void DrawTranslationGizmo(ImGuizmoOperation op, ImGuizmoMoveType type)
    {
        if (!Intersects(op, ImGuizmoOperation.Translate))
            return;
        var drawList = RequireDrawList();
        Span<ImColor8> colors = stackalloc ImColor8[7];
        ComputeColors(colors, type, ImGuizmoOperation.Translate);
        var origin = WorldToPos(gContext.Model.W, gContext.ViewProjection);
        for (int i = 0; i < 3; i++)
        {
            ComputeTripodAxisAndVisibility(i, out var dirAxis, out var dirPlaneX, out var dirPlaneY, out bool belowAxisLimit, out bool belowPlaneLimit);
            if (!gContext.Using || type == ImGuizmoMoveType.MT_MOVE_X + i)
            {
                if (belowAxisLimit && Intersects(op, (ImGuizmoOperation)((int)ImGuizmoOperation.TranslateX << i)))
                {
                    var baseSSpace = WorldToPos(dirAxis * 0.1f * gContext.ScreenFactor, gContext.MVP);
                    var worldDir = WorldToPos(dirAxis * gContext.ScreenFactor, gContext.MVP);
                    Line(drawList, baseSSpace, worldDir, colors[i + 1], gContext.Style.TranslationLineThickness);
                    var dir = origin - worldDir;
                    float d = Len2(dir);
                    if (d > float.Epsilon)
                    {
                        dir = new float2(dir.X / d, dir.Y / d) * gContext.Style.TranslationLineArrowSize;
                        var ortho = new float2(dir.Y, -dir.X);
                        var a = worldDir + dir;
                        TriangleFilled(drawList, worldDir - dir, a + ortho, a - ortho, colors[i + 1]);
                    }
                    if (gContext.CurrentTripod.AxisFactor[i] < 0f)
                        DrawHatchedAxis(dirAxis);
                }
            }
            if (!gContext.Using || type == ImGuizmoMoveType.MT_MOVE_YZ + i)
            {
                if (belowPlaneLimit && Contains(op, TranslatePlans[i]))
                {
                    var quad = new float2[4];
                    for (int j = 0; j < 4; j++)
                    {
                        var corner = (dirPlaneX * QuadUV[j * 2] + dirPlaneY * QuadUV[j * 2 + 1]) * gContext.ScreenFactor;
                        quad[j] = WorldToPos(corner, gContext.MVP);
                    }
                    Polyline(drawList, quad, StyleColor(ImGuizmoColor.DirectionX + i), ImDrawFlags.Closed, 1f);
                    ConvexFilled(drawList, quad, colors[i + 4]);
                }
            }
        }
        CircleFilled(drawList, gContext.ScreenSquareCenter, gContext.Style.CenterCircleSize, colors[0], 32);
        if (gContext.Using && gContext.CurrentId == gContext.EditingId && IsTranslateType(type))
        {
            var lineColor = StyleColor(ImGuizmoColor.TranslationLine);
            var source = WorldToPos(gContext.MatrixOrigin, gContext.ViewProjection);
            var destination = WorldToPos(gContext.Model.W, gContext.ViewProjection);
            var dif = new float4(destination.X - source.X, destination.Y - source.Y, 0f, 0f);
            dif = Normalized(dif) * 5f;
            Circle(drawList, source, 6f, lineColor, 0, 1f);
            Circle(drawList, destination, 6f, lineColor, 0, 1f);
            Line(drawList, new float2(source.X + dif.X, source.Y + dif.Y), new float2(destination.X - dif.X, destination.Y - dif.Y), lineColor, 2f);
            var deltaInfo = gContext.Model.W - gContext.MatrixOrigin;
            int index = (type - ImGuizmoMoveType.MT_MOVE_X) * 3;
            DrawInfo(drawList, destination, TranslationLabel(type, deltaInfo, index));
        }
    }

    static string TranslationLabel(ImGuizmoMoveType type, float4 delta, int index)
    {
        float a = Comp(delta, TranslationInfoIndex[index]);
        float b = Comp(delta, TranslationInfoIndex[index + 1]);
        float c = Comp(delta, TranslationInfoIndex[index + 2]);
        return type switch
        {
            ImGuizmoMoveType.MT_MOVE_X => $"X : {a,5:0.000}",
            ImGuizmoMoveType.MT_MOVE_Y => $"Y : {a,5:0.000}",
            ImGuizmoMoveType.MT_MOVE_Z => $"Z : {a,5:0.000}",
            ImGuizmoMoveType.MT_MOVE_YZ => $"Y : {a,5:0.000} Z : {b,5:0.000}",
            ImGuizmoMoveType.MT_MOVE_ZX => $"X : {a,5:0.000} Z : {b,5:0.000}",
            ImGuizmoMoveType.MT_MOVE_XY => $"X : {a,5:0.000} Y : {b,5:0.000}",
            _ => $"X : {a,5:0.000} Y : {b,5:0.000} Z : {c,5:0.000}",
        };
    }

    static bool HandleAndDrawLocalBounds(ReadOnlySpan<float> bounds, ref float4x4 matrix, float3? snapValues, ImGuizmoOperation operation)
    {
        var io = ImGui.GetIO();
        var drawList = RequireDrawList();
        bool manipulated = false;
        Span<int> axes = stackalloc int[3];
        Span<float4> axesWorld = stackalloc float4[3];
        int numAxes = 1;
        axes[0] = gContext.BoundsBestAxis;
        int bestAxis = axes[0];
        var bestAxisWorld = float4.Zero;
        if (!gContext.UsingBounds)
        {
            numAxes = 0;
            float bestDot = 0f;
            for (int i = 0; i < 3; i++)
            {
                var dirPlaneNormalWorld = Normalized(TransformVector(DirectionUnary[i], gContext.ModelSource));
                float dt = MathF.Abs(Dot3(Normalized(gContext.CameraEye - gContext.ModelSource.W), dirPlaneNormalWorld));
                if (dt >= bestDot)
                {
                    bestDot = dt;
                    bestAxis = i;
                    bestAxisWorld = dirPlaneNormalWorld;
                }
                if (dt >= 0.1f)
                {
                    axes[numAxes] = i;
                    axesWorld[numAxes] = dirPlaneNormalWorld;
                    numAxes++;
                }
            }
        }
        if (numAxes == 0)
        {
            axes[0] = bestAxis;
            axesWorld[0] = bestAxisWorld;
            numAxes = 1;
        }
        else if (bestAxis != axes[0])
        {
            int bestIndex = 0;
            for (int i = 0; i < numAxes; i++)
            {
                if (axes[i] == bestAxis)
                {
                    bestIndex = i;
                    break;
                }
            }
            (axes[0], axes[bestIndex]) = (axes[bestIndex], axes[0]);
            (axesWorld[0], axesWorld[bestIndex]) = (axesWorld[bestIndex], axesWorld[0]);
        }

        Span<float4> aabb = stackalloc float4[4];
        for (int axisIndex = 0; axisIndex < numAxes; axisIndex++)
        {
            bestAxis = axes[axisIndex];
            bestAxisWorld = axesWorld[axisIndex];
            int secondAxis = (bestAxis + 1) % 3;
            int thirdAxis = (bestAxis + 2) % 3;
            for (int i = 0; i < 4; i++)
            {
                aabb[i] = float4.Zero;
                aabb[i] = SetComp(aabb[i], secondAxis, bounds[secondAxis + 3 * (i >> 1)]);
                aabb[i] = SetComp(aabb[i], thirdAxis, bounds[thirdAxis + 3 * ((i >> 1) ^ (i & 1))]);
            }
            var anchorAlpha = gContext.EnableGizmo ? Col32(0, 0, 0) : Col32(0, 0, 0, 0x80);
            var boundsMvp = gContext.ModelSource * gContext.ViewProjection;
            for (int i = 0; i < 4; i++)
            {
                var p0 = TransformPoint(aabb[i], boundsMvp);
                var p1 = TransformPoint(aabb[(i + 1) % 4], boundsMvp);
                const float wEps = 1e-3f;
                bool in0 = p0.W >= wEps;
                bool in1 = p1.W >= wEps;
                if (!in0 && !in1)
                    continue;
                if (!in0)
                {
                    float t = (p1.W - wEps) / (p1.W - p0.W);
                    p0 = new float4(p1.X + (p0.X - p1.X) * t, p1.Y + (p0.Y - p1.Y) * t, p1.Z + (p0.Z - p1.Z) * t, wEps);
                }
                else if (!in1)
                {
                    float t = (p0.W - wEps) / (p0.W - p1.W);
                    p1 = new float4(p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t, p0.Z + (p1.Z - p0.Z) * t, wEps);
                }
                float2 Clip(float4 c)
                {
                    float nx = c.X * (0.5f / c.W) + 0.5f;
                    float ny = 1f - (c.Y * (0.5f / c.W) + 0.5f);
                    return new float2(gContext.X + nx * gContext.Width, gContext.Y + ny * gContext.Height);
                }
                var worldBound1 = Clip(p0);
                var worldBound2 = Clip(p1);
                float boundDistance = Len2(worldBound1 - worldBound2);
                int stepCount = Math.Min((int)(boundDistance / 10f), 1000);
                var dash = Or(Col32(0xAA, 0xAA, 0xAA, 0), anchorAlpha);
                for (int j = 0; j < stepCount; j++)
                {
                    float stepLength = 1f / stepCount;
                    float t1 = j * stepLength;
                    float t2 = j * stepLength + stepLength * 0.5f;
                    Line(drawList, worldBound1 + (worldBound2 - worldBound1) * t1, worldBound1 + (worldBound2 - worldBound1) * t2, dash, 2f);
                }
                var midPoint = (aabb[i] + aabb[(i + 1) % 4]) * 0.5f;
                var pCorner = TransformPoint(aabb[i], boundsMvp);
                var pMid = TransformPoint(midPoint, boundsMvp);
                var worldBoundOrig = WorldToPos(aabb[i], boundsMvp);
                var midBound = WorldToPos(midPoint, boundsMvp);
                bool bigVisible = pCorner.W >= wEps && IsInContextRect(worldBoundOrig);
                bool smallVisible = pMid.W >= wEps && IsInContextRect(midBound);
                const float bigRadius = 8f;
                const float smallRadius = 6f;
                bool overBig = bigVisible && Len2(worldBoundOrig - io.MousePos) <= bigRadius * bigRadius;
                bool overSmall = smallVisible && Len2(midBound - io.MousePos) <= bigRadius * bigRadius;
                var hoverType = ImGuizmoMoveType.MT_NONE;
                if (Intersects(operation, ImGuizmoOperation.Translate))
                    hoverType = GetMoveType(operation);
                if (Intersects(operation, ImGuizmoOperation.Rotate) && hoverType == ImGuizmoMoveType.MT_NONE)
                    hoverType = GetRotateType(operation);
                if (Intersects(operation, ImGuizmoOperation.Scale) && hoverType == ImGuizmoMoveType.MT_NONE)
                    hoverType = GetScaleType(operation);
                if (hoverType != ImGuizmoMoveType.MT_NONE)
                {
                    overBig = false;
                    overSmall = false;
                }
                var selection = StyleColor(ImGuizmoColor.Selection);
                var bigColor = overBig ? selection : dash;
                var smallColor = overSmall ? selection : dash;
                var black = Col32(0, 0, 0);
                if (bigVisible)
                {
                    CircleFilled(drawList, worldBoundOrig, bigRadius, black, 0);
                    CircleFilled(drawList, worldBoundOrig, bigRadius - 1.2f, bigColor, 0);
                }
                if (smallVisible)
                {
                    CircleFilled(drawList, midBound, smallRadius, black, 0);
                    CircleFilled(drawList, midBound, smallRadius - 1.2f, smallColor, 0);
                }
                int oppositeIndex = (i + 2) % 4;
                if (!gContext.UsingBounds && gContext.EnableGizmo && overBig && CanActivate())
                {
                    gContext.BoundsPivot = TransformPoint(aabb[(i + 2) % 4], gContext.ModelSource);
                    gContext.BoundsAnchor = TransformPoint(aabb[i], gContext.ModelSource);
                    gContext.BoundsPlan = BuildPlan(gContext.BoundsAnchor, bestAxisWorld);
                    gContext.BoundsBestAxis = bestAxis;
                    gContext.BoundsAxis[0] = secondAxis;
                    gContext.BoundsAxis[1] = thirdAxis;
                    gContext.BoundsLocalPivot = float4.Zero;
                    gContext.BoundsLocalPivot = SetComp(gContext.BoundsLocalPivot, secondAxis, Comp(aabb[oppositeIndex], secondAxis));
                    gContext.BoundsLocalPivot = SetComp(gContext.BoundsLocalPivot, thirdAxis, Comp(aabb[oppositeIndex], thirdAxis));
                    gContext.UsingBounds = true;
                    gContext.EditingId = gContext.CurrentId;
                    gContext.BoundsMatrix = gContext.ModelSource;
                }
                if (!gContext.UsingBounds && gContext.EnableGizmo && overSmall && CanActivate())
                {
                    var midOpposite = (aabb[(i + 2) % 4] + aabb[(i + 3) % 4]) * 0.5f;
                    gContext.BoundsPivot = TransformPoint(midOpposite, gContext.ModelSource);
                    gContext.BoundsAnchor = TransformPoint(midPoint, gContext.ModelSource);
                    gContext.BoundsPlan = BuildPlan(gContext.BoundsAnchor, bestAxisWorld);
                    gContext.BoundsBestAxis = bestAxis;
                    int[] indices = { secondAxis, thirdAxis };
                    gContext.BoundsAxis[0] = indices[i % 2];
                    gContext.BoundsAxis[1] = -1;
                    gContext.BoundsLocalPivot = float4.Zero;
                    gContext.BoundsLocalPivot = SetComp(gContext.BoundsLocalPivot, gContext.BoundsAxis[0], Comp(aabb[oppositeIndex], indices[i % 2]));
                    gContext.UsingBounds = true;
                    gContext.EditingId = gContext.CurrentId;
                    gContext.BoundsMatrix = gContext.ModelSource;
                }
            }

            if (gContext.UsingBounds && gContext.CurrentId == gContext.EditingId)
            {
                var scale = float4x4.Identity;
                float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, gContext.BoundsPlan);
                var newPos = gContext.RayOrigin + gContext.RayVector * len;
                var deltaVector = Abs4(newPos - gContext.BoundsPivot);
                var referenceVector = Abs4(gContext.BoundsAnchor - gContext.BoundsPivot);
                for (int i = 0; i < 2; i++)
                {
                    int axis = gContext.BoundsAxis[i];
                    if (axis == -1)
                        continue;
                    float ratioAxis = 1f;
                    var axisDir = Abs4(Row(gContext.BoundsMatrix, axis));
                    float dtAxis = Dot4(axisDir, referenceVector);
                    float boundSize = bounds[axis + 3] - bounds[axis];
                    if (dtAxis > float.Epsilon)
                        ratioAxis = Dot4(axisDir, deltaVector) / dtAxis;
                    if (snapValues is float3 snap)
                    {
                        float length = boundSize * ratioAxis;
                        ComputeSnap(ref length, Comp(new float4(snap.X, snap.Y, snap.Z, 0f), axis));
                        if (boundSize > float.Epsilon)
                            ratioAxis = length / boundSize;
                    }
                    SetRow(ref scale, axis, Row(scale, axis) * ratioAxis);
                    if (MathF.Abs(ratioAxis - 1f) > float.Epsilon)
                        manipulated = true;
                }
                var preScale = Translation(-gContext.BoundsLocalPivot);
                var postScale = Translation(gContext.BoundsLocalPivot);
                matrix = preScale * scale * postScale * gContext.BoundsMatrix;
                var destination = WorldToPos(gContext.Model.W, gContext.ViewProjection);
                float sx = (bounds[3] - bounds[0]) * Len3(Row(gContext.BoundsMatrix, 0)) * Len3(Row(scale, 0));
                float sy = (bounds[4] - bounds[1]) * Len3(Row(gContext.BoundsMatrix, 1)) * Len3(Row(scale, 1));
                float sz = (bounds[5] - bounds[2]) * Len3(Row(gContext.BoundsMatrix, 2)) * Len3(Row(scale, 2));
                DrawInfo(drawList, destination, $"X: {sx:0.00} Y: {sy:0.00} Z: {sz:0.00}");
            }
            if (!io.MouseDown[0])
            {
                gContext.UsingBounds = false;
                gContext.EditingId = new ImGuiID(uint.MaxValue);
            }
            if (gContext.UsingBounds)
                break;
        }
        return manipulated;
    }

    static void ComputeFrustumPlanes(Span<float4> frustum, float4x4 clip)
    {
        static float C(float4x4 m, int i) => i switch
        {
            0 => m.X.X, 1 => m.X.Y, 2 => m.X.Z, 3 => m.X.W,
            4 => m.Y.X, 5 => m.Y.Y, 6 => m.Y.Z, 7 => m.Y.W,
            8 => m.Z.X, 9 => m.Z.Y, 10 => m.Z.Z, 11 => m.Z.W,
            12 => m.W.X, 13 => m.W.Y, 14 => m.W.Z, _ => m.W.W,
        };
        float E(int i) => C(clip, i);
        frustum[0] = new float4(E(3) - E(0), E(7) - E(4), E(11) - E(8), E(15) - E(12));
        frustum[1] = new float4(E(3) + E(0), E(7) + E(4), E(11) + E(8), E(15) + E(12));
        frustum[2] = new float4(E(3) + E(1), E(7) + E(5), E(11) + E(9), E(15) + E(13));
        frustum[3] = new float4(E(3) - E(1), E(7) - E(5), E(11) - E(9), E(15) - E(13));
        frustum[4] = new float4(E(3) - E(2), E(7) - E(6), E(11) - E(10), E(15) - E(14));
        frustum[5] = new float4(E(3) + E(2), E(7) + E(6), E(11) + E(10), E(15) + E(14));
        for (int i = 0; i < 6; i++)
            frustum[i] = Normalized(frustum[i]);
    }

    static bool ClipSegment(ref float4 ptA, ref float4 ptB, ReadOnlySpan<float4> frustum)
    {
        for (int i = 0; i < 6; i++)
        {
            float dA = DistanceToPlane(ptA, frustum[i]);
            float dB = DistanceToPlane(ptB, frustum[i]);
            if (dA < 0f && dB < 0f)
                return false;
            if (dA > 0f && dB > 0f)
                continue;
            if (dA < 0f)
            {
                float t = MathF.Abs(dA) / MathF.Abs(dA - dB);
                Lerp(ref ptA, ptB, t);
            }
            if (dB < 0f)
            {
                float t = MathF.Abs(dB) / MathF.Abs(dB - dA);
                Lerp(ref ptB, ptA, t);
            }
        }
        return true;
    }

    static void DrawAxesImpl(float4x4 view, float4x4 projection, ReadOnlySpan<float4x4> matrices)
    {
        var viewProj = view * projection;
        Span<float4> frustum = stackalloc float4[6];
        ComputeFrustumPlanes(frustum, viewProj);
        var drawList = RequireDrawList();
        var colors = new[] { Col32(255, 0, 0), Col32(0, 255, 0), Col32(0, 0, 255) };
        for (int i = 0; i < matrices.Length; i++)
        {
            var model = matrices[i];
            var mvp = model * viewProj;
            var origin = TransformPoint(float4.Zero, model);
            for (int a = 0; a < 3; a++)
            {
                var endLocal = DirectionUnary[a];
                var end = TransformPoint(endLocal, model);
                bool visible = true;
                for (int f = 0; f < 6; f++)
                {
                    if (DistanceToPlane(origin, frustum[f]) < 0f && DistanceToPlane(end, frustum[f]) < 0f)
                    {
                        visible = false;
                        break;
                    }
                }
                if (!visible)
                    continue;
                var clip0 = TransformPoint(float4.Zero, mvp);
                var clip1 = TransformPoint(endLocal, mvp);
                if (clip0.W <= 0f && clip1.W <= 0f)
                    continue;
                Line(drawList, WorldToPos(float4.Zero, mvp), WorldToPos(endLocal, mvp), colors[a], 2f);
            }
        }
    }

    static void DrawCubesImpl(float4x4 view, float4x4 projection, ReadOnlySpan<float4x4> matrices)
    {
        var faces = new List<(float Z, float2 A, float2 B, float2 C, float2 D, ImColor8 Color)>();
        Span<float4> frustum = stackalloc float4[6];
        var viewProjection = view * projection;
        ComputeFrustumPlanes(frustum, viewProjection);
        Span<float4> faceCoords = stackalloc float4[4];
        for (int cube = 0; cube < matrices.Length; cube++)
        {
            var model = matrices[cube];
            var res = model * view * projection;
            for (int iFace = 0; iFace < 6; iFace++)
            {
                int normalIndex = iFace % 3;
                int perpX = (normalIndex + 1) % 3;
                int perpY = (normalIndex + 2) % 3;
                float invert = iFace > 2 ? -1f : 1f;
                faceCoords[0] = DirectionUnary[normalIndex] + DirectionUnary[perpX] + DirectionUnary[perpY];
                faceCoords[1] = DirectionUnary[normalIndex] + DirectionUnary[perpX] - DirectionUnary[perpY];
                faceCoords[2] = DirectionUnary[normalIndex] - DirectionUnary[perpX] - DirectionUnary[perpY];
                faceCoords[3] = DirectionUnary[normalIndex] - DirectionUnary[perpX] + DirectionUnary[perpY];
                var centerVp = TransformPoint(DirectionUnary[normalIndex] * 0.5f * invert, res);
                bool inFrustum = true;
                for (int iFrustum = 0; iFrustum < 6 && inFrustum; iFrustum++)
                {
                    bool allOutside = true;
                    for (int iCoord = 0; iCoord < 4; iCoord++)
                    {
                        var worldPos = TransformPoint(faceCoords[iCoord] * 0.5f * invert, model);
                        if (DistanceToPlane(worldPos, frustum[iFrustum]) >= 0f)
                        {
                            allOutside = false;
                            break;
                        }
                    }
                    if (allOutside)
                        inFrustum = false;
                }
                if (!inFrustum)
                    continue;
                var screen = new float2[4];
                for (int iCoord = 0; iCoord < 4; iCoord++)
                    screen[iCoord] = WorldToPos(faceCoords[iCoord] * 0.5f * invert, res);
                var color = Or(StyleColor(ImGuizmoColor.DirectionX + normalIndex), Col32(0x80, 0x80, 0x80, 0));
                faces.Add((centerVp.Z / centerVp.W, screen[0], screen[1], screen[2], screen[3], color));
            }
        }
        faces.Sort((a, b) => a.Z < b.Z ? 1 : -1);
        var drawList = RequireDrawList();
        foreach (var face in faces)
            ConvexFilled(drawList, new[] { face.A, face.B, face.C, face.D }, face.Color);
    }

    static void DrawGridImpl(float4x4 view, float4x4 projection, float4x4 matrix, float gridSize)
    {
        var viewProjection = view * projection;
        Span<float4> frustum = stackalloc float4[6];
        ComputeFrustumPlanes(frustum, viewProjection);
        var res = matrix * viewProjection;
        var drawList = RequireDrawList();
        for (float f = -gridSize; f <= gridSize; f += 1f)
        {
            for (int dir = 0; dir < 2; dir++)
            {
                var ptA = new float4(dir != 0 ? -gridSize : f, 0f, dir != 0 ? f : -gridSize, 0f);
                var ptB = new float4(dir != 0 ? gridSize : f, 0f, dir != 0 ? f : gridSize, 0f);
                if (!ClipSegment(ref ptA, ref ptB, frustum))
                    continue;
                var col = Col32(0x80, 0x80, 0x80);
                float thickness = 1f;
                if (MathF.Abs(MathF.Abs(f) % 10f) < float.Epsilon)
                {
                    col = Col32(0x90, 0x90, 0x90);
                    thickness = 1.5f;
                }
                if (MathF.Abs(f) < float.Epsilon)
                {
                    col = Col32(0x40, 0x40, 0x40);
                    thickness = 2.3f;
                }
                Line(drawList, WorldToPos(ptA, res), WorldToPos(ptB, res), col, thickness);
            }
        }
    }

    static void DrawGridCustomColorImpl(float4x4 view, float4x4 projection, float4x4 matrix, float gridSize, float majorStep, uint subdivision, ImColor8 majorCol, ImColor8 minorCol, ImColor8 centerCol)
    {
        if (subdivision == 0)
            throw new ArgumentOutOfRangeException(nameof(subdivision), "At least 1 segment is required.");
        var viewProjection = view * projection;
        Span<float4> frustum = stackalloc float4[6];
        ComputeFrustumPlanes(frustum, viewProjection);
        var res = matrix * viewProjection;
        float minorStep = majorStep / subdivision;
        int lineCount = (int)MathF.Ceiling(gridSize / minorStep);
        var drawList = RequireDrawList();
        for (int i = -lineCount; i <= lineCount; i++)
        {
            float f = i * minorStep;
            bool isMajor = i % (int)subdivision == 0;
            bool isCenter = i == 0;
            var col = isCenter ? centerCol : isMajor ? majorCol : minorCol;
            float thickness = isCenter ? 2.3f : isMajor ? 1.5f : 1f;
            for (int dir = 0; dir < 2; dir++)
            {
                var ptA = new float4(dir != 0 ? -gridSize : f, 0f, dir != 0 ? f : -gridSize, 0f);
                var ptB = new float4(dir != 0 ? gridSize : f, 0f, dir != 0 ? f : gridSize, 0f);
                if (!ClipSegment(ref ptA, ref ptB, frustum))
                    continue;
                Line(drawList, WorldToPos(ptA, res), WorldToPos(ptB, res), col, thickness);
            }
        }
    }

    static void ViewManipulate(ref float4x4 view, float length, float2 position, float2 size, ImColor8 backgroundColor)
    {
        var state = gContext.CurrentView();
        var referenceUp = new float4(0f, 1f, 0f, 0f);
        gContext.MouseOver = IsHoveringWindow();
        var svgView = gContext.ViewMat;
        var svgProjection = gContext.ProjectionMat;
        var io = ImGui.GetIO();
        var rectMax = position + size;
        bool hovered = gContext.MouseOver && io.MousePos.X >= position.X && io.MousePos.Y >= position.Y && io.MousePos.X < rectMax.X && io.MousePos.Y < rectMax.Y;
        var drawList = RequireDrawList();
        RectFilled(drawList, position, rectMax, backgroundColor);
        var viewInverse = Inverse(view);
        bool rightHanded = At(gContext.ProjectionMat, 2, 3) < 0f;
        float handedness = rightHanded ? 1f : -1f;
        var camTarget = viewInverse.W - viewInverse.Z * handedness * length;
        const float distance = 3f;
        float fov = MathF.Acos(distance / MathF.Sqrt(distance * distance + 3f)) * Rad2Deg;
        var cubeProjection = Perspective(fov / MathF.Sqrt(2f), size.X / size.Y, 0.01f, 1000f, rightHanded);
        var dir = new float4(viewInverse.Z.X, viewInverse.Z.Y, viewInverse.Z.Z, 0f);
        var up = new float4(viewInverse.Y.X, viewInverse.Y.Y, viewInverse.Y.Z, 0f);
        var eye = dir * handedness * distance;
        var cubeView = LookAt(eye, float4.Zero, up, rightHanded);
        gContext.ViewMat = cubeView;
        gContext.ProjectionMat = cubeProjection;
        ComputeCameraRay(position, size);
        var res = cubeView * cubeProjection;

        var panelPosition = new[]
        {
            new float2(0.75f, 0.75f), new float2(0.25f, 0.75f), new float2(0f, 0.75f),
            new float2(0.75f, 0.25f), new float2(0.25f, 0.25f), new float2(0f, 0.25f),
            new float2(0.75f, 0f), new float2(0.25f, 0f), new float2(0f, 0f),
        };
        var panelSize = new[]
        {
            new float2(0.25f, 0.25f), new float2(0.5f, 0.25f), new float2(0.25f, 0.25f),
            new float2(0.25f, 0.5f), new float2(0.5f, 0.5f), new float2(0.25f, 0.5f),
            new float2(0.25f, 0.25f), new float2(0.5f, 0.25f), new float2(0.25f, 0.25f),
        };
        var boxes = new bool[27];
        for (int iPass = 0; iPass < 2; iPass++)
        {
            for (int iFace = 0; iFace < 6; iFace++)
            {
                int normalIndex = iFace % 3;
                int perpX = (normalIndex + 1) % 3;
                int perpY = (normalIndex + 2) % 3;
                float invert = iFace > 2 ? -1f : 1f;
                var indexVectorX = DirectionUnary[perpX] * invert;
                var indexVectorY = DirectionUnary[perpY] * invert;
                var boxOrigin = DirectionUnary[normalIndex] * -invert - indexVectorX - indexVectorY;
                var n = DirectionUnary[normalIndex] * invert;
                var viewSpaceNormal = Normalized(TransformVector(n, cubeView));
                var viewSpacePoint = TransformPoint(n * 0.5f, cubeView);
                var viewSpaceFacePlan = BuildPlan(viewSpacePoint, viewSpaceNormal);
                if (viewSpaceFacePlan.W > 0f)
                    continue;
                var facePlan = BuildPlan(n * 0.5f, n);
                float len = IntersectRayPlane(gContext.RayOrigin, gContext.RayVector, facePlan);
                var posOnPlan = gContext.RayOrigin + gContext.RayVector * len - n * 0.5f;
                float localx = Dot3(DirectionUnary[perpX], posOnPlan) * invert + 0.5f;
                float localy = Dot3(DirectionUnary[perpY], posOnPlan) * invert + 0.5f;
                var dx = DirectionUnary[perpX];
                var dy = DirectionUnary[perpY];
                var origin = DirectionUnary[normalIndex] - dx - dy;
                for (int iPanel = 0; iPanel < 9; iPanel++)
                {
                    var boxCoord = boxOrigin + indexVectorX * (iPanel % 3) + indexVectorY * (iPanel / 3) + new float4(1f, 1f, 1f, 0f);
                    var p = panelPosition[iPanel] * 2f;
                    var s = panelSize[iPanel] * 2f;
                    var panelPos = new[]
                    {
                        dx * p.X + dy * p.Y,
                        dx * p.X + dy * (p.Y + s.Y),
                        dx * (p.X + s.X) + dy * (p.Y + s.Y),
                        dx * (p.X + s.X) + dy * p.Y,
                    };
                    var faceCoords = new float2[4];
                    for (int iCoord = 0; iCoord < 4; iCoord++)
                        faceCoords[iCoord] = WorldToPos((panelPos[iCoord] + origin) * 0.5f * invert, res, position, size);
                    var corner0 = panelPosition[iPanel];
                    var corner1 = panelPosition[iPanel] + panelSize[iPanel];
                    bool inside = localx > corner0.X && localx < corner1.X && localy > corner0.Y && localy < corner1.Y;
                    int boxCoordInt = (int)(boxCoord.X * 9f + boxCoord.Y * 3f + boxCoord.Z);
                    if ((uint)boxCoordInt < 27)
                        boxes[boxCoordInt] |= inside && !state.Dragging && gContext.MouseOver;
                    if (iPass == 1 && (uint)boxCoordInt < 27)
                    {
                        var directionColor = Or(StyleColor(ImGuizmoColor.DirectionX + normalIndex), Col32(0x80, 0x80, 0x80, 0x80));
                        if (hovered)
                            directionColor = Or(directionColor, Col32(0x08, 0x08, 0x08, 0));
                        ConvexFilled(drawList, faceCoords, directionColor);
                        if (boxes[boxCoordInt])
                        {
                            ConvexFilled(drawList, faceCoords, StyleColor(ImGuizmoColor.Selection));
                            if (io.MouseDown[0] && !state.Clicking && !state.Dragging && ImGui.GetCurrentContext().ActiveId.Equals(new ImGuiID(0)))
                            {
                                s_overBox = boxCoordInt;
                                state.Clicking = true;
                                state.Dragging = true;
                            }
                        }
                    }
                }
            }
        }

        if (state.Frames != 0)
        {
            state.Frames--;
            var newDir = viewInverse.Z * handedness;
            Lerp(ref newDir, state.Dir * handedness, 0.2f);
            newDir = Normalized(newDir);
            var newUp = state.Up;
            var newEye = camTarget + newDir * length;
            view = LookAt(newEye, camTarget, newUp, rightHanded);
            viewInverse = Inverse(view);
        }
        gContext.ViewManipulatorHovered |= hovered;
        if (io.MouseDown[0] && (io.MouseDelta.X != 0f || io.MouseDelta.Y != 0f) && state.Clicking)
            state.Clicking = false;
        if (!io.MouseDown[0])
        {
            if (state.Clicking)
            {
                int cx = s_overBox / 9;
                int cy = (s_overBox - cx * 9) / 3;
                int cz = s_overBox % 3;
                state.Dir = Normalized(new float4(1f - cx, 1f - cy, 1f - cz, 0f) * handedness);
                if (MathF.Abs(Dot3(state.Dir, referenceUp)) > 0.99f)
                {
                    var right = viewInverse.X;
                    if (MathF.Abs(right.X) > MathF.Abs(right.Z))
                        right.Z = 0f;
                    else
                        right.X = 0f;
                    right = Normalized(right);
                    state.Up = Normalized(Cross(state.Dir * handedness, right));
                }
                else
                {
                    state.Up = referenceUp;
                }
                state.Frames = 40;
            }
            state.Clicking = false;
            state.Dragging = false;
        }
        if (state.Dragging)
        {
            var rx = RotationAxis(referenceUp, -io.MouseDelta.X * 0.01f * handedness);
            var ry = RotationAxis(viewInverse.X, -io.MouseDelta.Y * 0.01f * handedness);
            var roll = rx * ry;
            var newDir = Normalized(TransformVector(viewInverse.Z, roll));
            var planDir = Cross(viewInverse.X, referenceUp);
            planDir.Y = 0f;
            planDir = Normalized(planDir);
            float dt = Dot3(planDir, newDir);
            if (dt < 0f)
            {
                newDir += planDir * dt;
                newDir = Normalized(newDir);
            }
            var newEye = camTarget + newDir * handedness * length;
            view = LookAt(newEye, camTarget, referenceUp, rightHanded);
        }
        bool thisUsing = state.Frames != 0 || state.Dragging;
        gContext.UsingViewManipulate |= thisUsing;
        if (state.Clicking || thisUsing || hovered)
            WantMouse();
        ComputeContext(svgView, svgProjection, gContext.ModelSource, gContext.Mode);
    }
}
