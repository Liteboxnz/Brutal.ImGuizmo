using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace ImGuizmo;

/*
 * A ImGuizmo port for the Brutal Framework.
 * The intent is to be able to 'just drop this in' and it works with Brutal.ImGui
 * without another third party library.
 *
 * Types are kept as close to the original as possible while adopting brutal framework dependencies
 */

public enum ImGuiOperation
{
    Translate, // TRANSLATE_X | TRANSLATE_Y | TRANSLATE_Z,
    Rotate, // ROTATE_X | ROTATE_Y | ROTATE_Z | ROTATE_SCREEN,
    Scale, // SCALE_X | SCALE_Y | SCALE_Z,
    ScaleU, // SCALE_XU | SCALE_YU | SCALE_ZU
    Universal // TRANSLATE | ROTATE | SCALEU
}

public static class ImGuizmo
{
    // call inside your own window and before Manipulate() in order to draw gizmo to that window.
   // Or pass a specific ImDrawList to draw to (e.g. ImGui::GetForegroundDrawList()).
   public static void SetDrawlist(ImDrawList? drawlist)
   {
       
   }

   // call BeginFrame right after ImGui_XXXX_NewFrame();
   public static void BeginFrame() {}

   // this is necessary because when imguizmo is compiled into a dll, and imgui into another
   // globals are not shared between them.
   // More details at https://stackoverflow.com/questions/19373061/what-happens-to-global-and-static-variables-in-a-shared-library-when-it-is-dynam
   // expose method to set imgui context
   public static void SetImGuiContext(ImGuiContext ctx) {}

   // return true if mouse cursor is over any gizmo control (axis, plan or screen component)
   public static bool IsOver()
   {
       return false;}

   // return true if mouse IsOver or if the gizmo is in moving state
   public static bool IsUsing()
   {
       return false;
   }

   // return true if the view gizmo is in moving state
   public static bool IsUsingViewManipulate()
   {
       return false;
   }
   // only check if your mouse is over the view manipulator - no matter whether it's active or not
   public static bool IsViewManipulateHovered()
   {
       return false;
   }

   // return true if any gizmo is in moving state
   public static bool IsUsingAny()
   {
       return false;
   }

   // enable/disable the gizmo. Stay in the state until next call to Enable.
   // gizmo is rendered with gray half transparent color when disabled
   public static void Enable(bool enable)
   {

   }

   // helper functions for manualy editing translation/rotation/scale with an input float
   // translation, rotation and scale float points to 3 floats each
   // Angles are in degrees (more suitable for human editing)
   // example:
   // float matrixTranslation[3], matrixRotation[3], matrixScale[3];
   // ImGuizmo::DecomposeMatrixToComponents(gizmoMatrix.m16, matrixTranslation, matrixRotation, matrixScale);
   // ImGui::InputFloat3("Tr", matrixTranslation, 3);
   // ImGui::InputFloat3("Rt", matrixRotation, 3);
   // ImGui::InputFloat3("Sc", matrixScale, 3);
   // ImGuizmo::RecomposeMatrixFromComponents(matrixTranslation, matrixRotation, matrixScale, gizmoMatrix.m16);
   //
   // These functions have some numerical stability issues for now. Use with caution.
   public static void DecomposeMatrixToComponents(float4 matrix, float4 translation, float4 rotation, float4 scale){
   }
   public static void RecomposeMatrixFromComponents(float4 translation, float4 rotation, float4 scale, float4 matrix){
   }

   public static void SetRect(float x, float y, float width, float height)
   {
   }

   // default is false
   public static void SetOrthographic(bool isOrthographic)
   {
   }

   // Render a cube with face color corresponding to face normal. Useful for debug/tests
   public static void DrawCubes(float4 view, float4 projection, float4 matrices, int matrixCount){
       
   }
   public static void DrawGrid(float4 view, float4 projection, float4 matrix, float4 gridSize){
   }

}