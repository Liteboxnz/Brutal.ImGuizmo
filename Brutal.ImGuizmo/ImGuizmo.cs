// C# port for the Brutal framework (ImGuizmo v1.92.5 WIP behavior).
// https://github.com/CedricGuillemet/ImGuizmo
//
// The MIT License (MIT)
// Copyright (c) 2016-2026 Cedric Guillemet and contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace Brutal.ImGuizmoAPI;

/// <summary>
/// Bitmask of gizmo handles. Combined values match the original ImGuizmo OPERATION enum.
/// </summary>
[Flags]
public enum ImGuizmoOperation
{
    TranslateX = 1 << 0,
    TranslateY = 1 << 1,
    TranslateZ = 1 << 2,
    RotateX = 1 << 3,
    RotateY = 1 << 4,
    RotateZ = 1 << 5,
    RotateScreen = 1 << 6,
    ScaleX = 1 << 7,
    ScaleY = 1 << 8,
    ScaleZ = 1 << 9,
    Bounds = 1 << 10,
    ScaleXU = 1 << 11,
    ScaleYU = 1 << 12,
    ScaleZU = 1 << 13,

    Translate = TranslateX | TranslateY | TranslateZ,
    Rotate = RotateX | RotateY | RotateZ | RotateScreen,
    Scale = ScaleX | ScaleY | ScaleZ,
    ScaleU = ScaleXU | ScaleYU | ScaleZU,
    Universal = Translate | Rotate | ScaleU,
    Default = 0,
}

public enum ImGuizmoMode
{
    Local,
    World,
}

public enum ImGuizmoColor
{
    DirectionX,
    DirectionY,
    DirectionZ,
    PlaneX,
    PlaneY,
    PlaneZ,
    Selection,
    Inactive,
    TranslationLine,
    ScaleLine,
    RotationUsingBorder,
    RotationUsingFill,
    HatchedAxisLines,
    Text,
    TextShadow,
    Count,
}

public enum ImGuizmoMoveType
{
    MT_NONE,
    MT_MOVE_X,
    MT_MOVE_Y,
    MT_MOVE_Z,
    MT_MOVE_YZ,
    MT_MOVE_ZX,
    MT_MOVE_XY,
    MT_MOVE_SCREEN,
    MT_ROTATE_X,
    MT_ROTATE_Y,
    MT_ROTATE_Z,
    MT_ROTATE_SCREEN,
    MT_SCALE_X,
    MT_SCALE_Y,
    MT_SCALE_Z,
    MT_SCALE_XYZ,
}

public struct ImGuizmoStyle
{
    public float TranslationLineThickness;
    public float TranslationLineArrowSize;
    public float RotationLineThickness;
    public float RotationOuterLineThickness;
    public float ScaleLineThickness;
    public float ScaleLineCircleSize;
    public float HatchedAxisLineThickness;
    public float CenterCircleSize;
    public float4[] Colors;

    public ImGuizmoStyle()
    {
        TranslationLineThickness = 3.0f;
        TranslationLineArrowSize = 6.0f;
        RotationLineThickness = 2.0f;
        RotationOuterLineThickness = 3.0f;
        ScaleLineThickness = 3.0f;
        ScaleLineCircleSize = 6.0f;
        HatchedAxisLineThickness = 6.0f;
        CenterCircleSize = 6.0f;
        Colors = new float4[(int)ImGuizmoColor.Count];
        Colors[(int)ImGuizmoColor.DirectionX] = new float4(0.666f, 0.000f, 0.000f, 1.000f);
        Colors[(int)ImGuizmoColor.DirectionY] = new float4(0.000f, 0.666f, 0.000f, 1.000f);
        Colors[(int)ImGuizmoColor.DirectionZ] = new float4(0.000f, 0.000f, 0.666f, 1.000f);
        Colors[(int)ImGuizmoColor.PlaneX] = new float4(0.666f, 0.000f, 0.000f, 0.380f);
        Colors[(int)ImGuizmoColor.PlaneY] = new float4(0.000f, 0.666f, 0.000f, 0.380f);
        Colors[(int)ImGuizmoColor.PlaneZ] = new float4(0.000f, 0.000f, 0.666f, 0.380f);
        Colors[(int)ImGuizmoColor.Selection] = new float4(1.000f, 0.500f, 0.062f, 0.541f);
        Colors[(int)ImGuizmoColor.Inactive] = new float4(0.600f, 0.600f, 0.600f, 0.600f);
        Colors[(int)ImGuizmoColor.TranslationLine] = new float4(0.666f, 0.666f, 0.666f, 0.666f);
        Colors[(int)ImGuizmoColor.ScaleLine] = new float4(0.250f, 0.250f, 0.250f, 1.000f);
        Colors[(int)ImGuizmoColor.RotationUsingBorder] = new float4(1.000f, 0.500f, 0.062f, 1.000f);
        Colors[(int)ImGuizmoColor.RotationUsingFill] = new float4(1.000f, 0.500f, 0.062f, 0.500f);
        Colors[(int)ImGuizmoColor.HatchedAxisLines] = new float4(0.000f, 0.000f, 0.000f, 0.500f);
        Colors[(int)ImGuizmoColor.Text] = new float4(1.000f, 1.000f, 1.000f, 1.000f);
        Colors[(int)ImGuizmoColor.TextShadow] = new float4(0.000f, 0.000f, 0.000f, 1.000f);
    }
}

public static partial class ImGuizmo
{
    /// <summary>Draw into this list. Pass null to use the current window draw list.</summary>
    public static void SetDrawlist(ImDrawListPtr? drawlist = null)
    {
        gContext.DrawList = drawlist ?? ImGui.GetWindowDrawList();
    }

    /// <summary>Call immediately after ImGui NewFrame.</summary>
    public static void BeginFrame()
    {
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        var viewport = ImGui.GetMainViewport();
        var size = viewport.Size;
        var pos = viewport.Pos;
        ImGui.SetNextWindowSize(in size, ImGuiCond.None);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.None);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, (ImColor8)0u);
        ImGui.PushStyleColor(ImGuiCol.Border, (ImColor8)0u);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.Begin("gizmo"u8, flags);
        gContext.DrawList = ImGui.GetWindowDrawList();
        gContext.OverGizmoHotspotLastFrame = gContext.OverGizmoHotspot;
        gContext.OverGizmoHotspot = false;
        gContext.UsingViewManipulate = false;
        gContext.ViewManipulatorHovered = false;
        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    public static void SetImGuiContext(ImGuiContextPtr ctx) => ImGui.SetCurrentContext(ctx);

    public static bool IsOver() => gContext.OverGizmoHotspotLastFrame || IsUsingAny();

    public static bool IsUsing() => (gContext.Using && gContext.CurrentId == gContext.EditingId) || gContext.UsingBounds;

    public static bool IsUsingViewManipulate() => gContext.UsingViewManipulate;

    public static bool IsViewManipulateHovered() => gContext.ViewManipulatorHovered;

    public static bool IsUsingAny() => gContext.Using || gContext.UsingBounds;

    public static void Enable(bool enable)
    {
        gContext.EnableGizmo = enable;
        if (!enable && (!gContext.Using || gContext.CurrentId == gContext.EditingId))
        {
            gContext.Using = false;
            gContext.UsingBounds = false;
            gContext.CurrentHandleType = ImGuizmoMoveType.MT_NONE;
            gContext.HoveredHandleType = ImGuizmoMoveType.MT_NONE;
        }
    }

    /// <summary>
    /// Splits a matrix into translation, Euler degrees, and scale.
    /// These functions have some numerical stability issues. Use with caution.
    /// </summary>
    public static void DecomposeMatrixToComponents(float4x4 matrix, out float3 translation, out float3 rotation, out float3 scale)
    {
        scale = new float3(Len3(matrix.X), Len3(matrix.Y), Len3(matrix.Z));
        OrthoNormalize(ref matrix);
        rotation = new float3(
            Rad2Deg * MathF.Atan2(matrix.Y.Z, matrix.Z.Z),
            Rad2Deg * MathF.Atan2(-matrix.X.Z, MathF.Sqrt(matrix.Y.Z * matrix.Y.Z + matrix.Z.Z * matrix.Z.Z)),
            Rad2Deg * MathF.Atan2(matrix.X.Y, matrix.X.X));
        translation = new float3(matrix.W.X, matrix.W.Y, matrix.W.Z);
    }

    public static float4x4 RecomposeMatrixFromComponents(float3 translation, float3 rotation, float3 scale)
    {
        var rx = RotationAxis(DirectionUnary[0], rotation.X * Deg2Rad);
        var ry = RotationAxis(DirectionUnary[1], rotation.Y * Deg2Rad);
        var rz = RotationAxis(DirectionUnary[2], rotation.Z * Deg2Rad);
        var mat = rx * ry * rz;
        float sx = MathF.Abs(scale.X) < float.Epsilon ? 0.001f : scale.X;
        float sy = MathF.Abs(scale.Y) < float.Epsilon ? 0.001f : scale.Y;
        float sz = MathF.Abs(scale.Z) < float.Epsilon ? 0.001f : scale.Z;
        mat.X *= sx;
        mat.Y *= sy;
        mat.Z *= sz;
        mat.W = new float4(translation.X, translation.Y, translation.Z, 1f);
        return mat;
    }

    public static void SetRect(float x, float y, float width, float height)
    {
        gContext.X = x;
        gContext.Y = y;
        gContext.Width = width;
        gContext.Height = height;
        gContext.XMax = x + width;
        gContext.YMax = y + height;
        gContext.DisplayRatio = width / height;
    }

    public static void SetOrthographic(bool isOrthographic) => gContext.IsOrthographic = isOrthographic;

    public static void DrawAxes(float4x4 view, float4x4 projection, ReadOnlySpan<float4x4> matrices) => DrawAxesImpl(view, projection, matrices);

    public static void DrawCubes(float4x4 view, float4x4 projection, ReadOnlySpan<float4x4> matrices) => DrawCubesImpl(view, projection, matrices);

    public static void DrawGrid(float4x4 view, float4x4 projection, float4x4 matrix, float gridSize) =>
        DrawGridImpl(view, projection, matrix, gridSize);

    public static void DrawGridCustom(float4x4 view, float4x4 projection, float4x4 matrix, float gridSize, float majorStep, uint subdivision) =>
        DrawGridCustomColor(view, projection, matrix, gridSize, majorStep, subdivision, Col32(0x80, 0x80, 0x80), Col32(0x90, 0x90, 0x90), Col32(0x40, 0x40, 0x40));

    public static void DrawGridCustomColor(float4x4 view, float4x4 projection, float4x4 matrix, float gridSize, float majorStep, uint subdivision, ImColor8 majorCol, ImColor8 minorCol, ImColor8 centerCol) =>
        DrawGridCustomColorImpl(view, projection, matrix, gridSize, majorStep, subdivision, majorCol, minorCol, centerCol);

    public static bool Manipulate(float4x4 view, float4x4 projection, ImGuizmoOperation operation, ImGuizmoMode mode, ref float4x4 matrix, float3? snap = null, (float3 Min, float3 Max)? localBounds = null, float3? boundsSnap = null)
    {
        var delta = float4x4.Identity;
        return Manipulate(view, projection, operation, mode, ref matrix, ref delta, snap, localBounds, boundsSnap, writeDelta: false);
    }

    public static bool Manipulate(float4x4 view, float4x4 projection, ImGuizmoOperation operation, ImGuizmoMode mode, ref float4x4 matrix, ref float4x4 deltaMatrix, float3? snap = null, (float3 Min, float3 Max)? localBounds = null, float3? boundsSnap = null) =>
        Manipulate(view, projection, operation, mode, ref matrix, ref deltaMatrix, snap, localBounds, boundsSnap, writeDelta: true);

    /// <summary>
    /// Orientation cube. Autodesk holds a US patent on this interaction
    /// (https://patents.google.com/patent/US7782319B2/en). It is widely used elsewhere,
    /// but you should know that before shipping it.
    /// </summary>
    public static void ViewManipulate(ref float4x4 view, float length, float2 position, float2 size, uint backgroundColor) =>
        ViewManipulate(ref view, length, position, size, (ImColor8)backgroundColor);

    public static void ViewManipulate(ref float4x4 view, float4x4 projection, ImGuizmoOperation operation, ImGuizmoMode mode, ref float4x4 matrix, float length, float2 position, float2 size, uint backgroundColor)
    {
        ComputeContext(view, projection, matrix, Intersects(operation, ImGuizmoOperation.Scale) ? ImGuizmoMode.Local : mode);
        ViewManipulate(ref view, length, position, size, (ImColor8)backgroundColor);
    }

    public static void SetAlternativeWindow(ImGuiWindowPtr window) => gContext.AlternativeWindow = window;

    public static void PushId(string stringId) => gContext.IdStack.Add(GetId(stringId));

    public static void PushId(int intId) => gContext.IdStack.Add(GetId(intId));

    public static void PopId()
    {
        if (gContext.IdStack.Count <= 1)
            throw new InvalidOperationException("ImGuizmo.PopId called without a matching PushId.");
        gContext.IdStack.RemoveAt(gContext.IdStack.Count - 1);
    }

    public static ImGuiID GetId(string stringId) => ImGui.Internal.GetIDWithSeed(stringId, gContext.CurrentId);

    public static ImGuiID GetId(int intId) => ImGui.Internal.GetIDWithSeed(intId, gContext.CurrentId);

    public static bool IsOver(ImGuizmoOperation op)
    {
        if (IsUsing())
            return true;
        if (Intersects(op, ImGuizmoOperation.Scale | ImGuizmoOperation.ScaleU) && GetScaleType(op) != ImGuizmoMoveType.MT_NONE)
            return true;
        if (Intersects(op, ImGuizmoOperation.Rotate) && GetRotateType(op) != ImGuizmoMoveType.MT_NONE)
            return true;
        if (Intersects(op, ImGuizmoOperation.Translate) && GetMoveType(op) != ImGuizmoMoveType.MT_NONE)
            return true;
        return false;
    }

    public static void SetGizmoSizeClipSpace(float value) => gContext.GizmoSizeClipSpace = value;

    public static ImGuizmoMoveType GetActiveHandleType() =>
        !gContext.Using || gContext.CurrentId != gContext.EditingId ? ImGuizmoMoveType.MT_NONE : gContext.CurrentHandleType;

    public static ImGuizmoMoveType GetHoveredHandleType() => gContext.HoveredHandleType;

    public static void AllowAxisFlip(bool value) => gContext.AllowAxisFlip = value;

    public static void SetAxisLimit(float value) => gContext.AxisLimit = value;

    public static void SetAxisMask(bool x, bool y, bool z) => gContext.AxisMask = (x ? 1 : 0) + (y ? 2 : 0) + (z ? 4 : 0);

    public static void SetPlaneLimit(float value) => gContext.PlaneLimit = value;

    public static bool IsOver(float3 position, float pixelRadius)
    {
        var io = ImGui.GetIO();
        var projected = WorldToPos(new float4(position.X, position.Y, position.Z, 0f), gContext.ViewProjection);
        float dx = projected.X - io.MousePos.X;
        float dy = projected.Y - io.MousePos.Y;
        return MathF.Sqrt(dx * dx + dy * dy) < pixelRadius;
    }

    /// <summary>
    /// World-space mouse ray from explicit inputs. Matrices use the same row-major layout as <see cref="Manipulate"/>.
    /// </summary>
    public static void ComputeMouseRay(float4x4 view, float4x4 projection, float2 mousePosition, float2 rectPosition, float2 rectSize, out float3 rayOrigin, out float3 rayDirection)
    {
        ComputeCameraRay(view, projection, mousePosition, rectPosition, rectSize, out var origin, out var direction);
        rayOrigin = new float3(origin.X, origin.Y, origin.Z);
        rayDirection = new float3(direction.X, direction.Y, direction.Z);
    }

    public static ref ImGuizmoStyle GetStyle() => ref gContext.Style;
}
