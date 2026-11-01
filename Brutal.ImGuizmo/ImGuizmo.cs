using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

namespace Brutal.ImGuizmoAPI;

/*
 * A ImGuizmo port for the Brutal Framework.
 * The intent is to be able to 'just drop this in' and it works with Brutal.ImGui
 * without a additional third party library.
 *
 * Types are kept as close to the original as possible while adopting brutal framework dependencies
 */

public enum ImGuizmoOperation
{
    Translate, // TRANSLATE_X | TRANSLATE_Y | TRANSLATE_Z,
    Rotate, // ROTATE_X | ROTATE_Y | ROTATE_Z | ROTATE_SCREEN,
    Scale, // SCALE_X | SCALE_Y | SCALE_Z,
    ScaleU, // SCALE_XU | SCALE_YU | SCALE_ZU
    Universal, // TRANSLATE | ROTATE | SCALEU
    Default
}

public enum ImGuizmoMode
{
	Local,
	World
};

internal struct ImGuizmoStyle
{
	float TranslationLineThickness;   // Thickness of lines for translation gizmo
	float TranslationLineArrowSize;   // Size of arrow at the end of lines for translation gizmo
	float RotationLineThickness;      // Thickness of lines for rotation gizmo
	float RotationOuterLineThickness; // Thickness of line surrounding the rotation gizmo
	float ScaleLineThickness;         // Thickness of lines for scale gizmo
	float ScaleLineCircleSize;        // Size of circle at the end of lines for scale gizmo
	float HatchedAxisLineThickness;   // Thickness of hatched axis lines
	float CenterCircleSize;           // Size of circle at the center of the translate/scale gizmo

	List<float4> Colors;
};
internal class ImGuizmoContext
{

      ImDrawList? mDrawList;
      ImGuizmoStyle mStyle;

      ImGuizmoMode mMode;
      float4x4 mViewMat;
      float4x4 mProjectionMat;
      float4x4 mModel;
      float4x4 mModelLocal; // orthonormalized model
      float4x4 mModelInverse;
      float4x4 mModelSource;
      float4x4 mModelSourceInverse;
      float4x4 mMVP;
      float4x4 mMVPLocal; // MVP with full model matrix whereas mMVP's model matrix might only be translation in case of World space edition
      float4x4 mViewProjection;

      float4 mModelScaleOrigin;
      float4 mCameraEye;
      float4 mCameraRight;
      float4 mCameraDir;
      float4 mCameraUp;
      float4 mRayOrigin;
      float4 mRayVector;

      float  mRadiusSquareCenter;
      float2 mScreenSquareCenter;
      float2 mScreenSquareMin;
      float2 mScreenSquareMax;

      float mScreenFactor;
      float4 mRelativeOrigin;

      bool mbUsing = false;
      bool mbUsingViewManipulate = false;
      bool mbEnable = true;
      bool mbMouseOver;
      bool mReversed; // reversed projection matrix
      bool mIsViewManipulatorHovered = false;

      // translation
      float4 mTranslationPlan;
      float4 mTranslationPlanOrigin;
      float4 mMatrixOrigin;
      float4 mTranslationLastDelta;

      // rotation
      float4 mRotationVectorSource;
      float mRotationAngle;
      float mRotationAngleOrigin;
      //float4 mWorldToLocalAxis;

      // scale
      float4 mScale;
      float4 mScaleValueOrigin;
      float4 mScaleLast;
      float mSaveMousePosx;

      // save axis factor when using gizmo
      bool3 mBelowAxisLimit;
      int mAxisMask = 0;
      bool3 mBelowPlaneLimit;
      float3 mAxisFactor;

      float mAxisLimit=0.0025f;
      float mPlaneLimit=0.02f;

      // bounds stretching
      float4 mBoundsPivot;
      float4 mBoundsAnchor;
      float4 mBoundsPlan;
      float4 mBoundsLocalPivot;
      int mBoundsBestAxis;
      int2 mBoundsAxis;
      bool mbUsingBounds = false;
      float4x4 mBoundsMatrix;

      //
      int mCurrentOperation;

      public float mX = 0.0f;
      public float mY = 0.0f;
      public float mWidth = 0.0f;
      public float mHeight = 0.0f;
      public float mXMax = 0.0f;
      public float mYMax = 0.0f;
      public float mDisplayRatio = 1.0f;

      bool mIsOrthographic = false;
      // check to not have multiple gizmo highlighted at the same time
      bool mbOverGizmoHotspot = false;

      ImGuiWindow? mAlternativeWindow;
      ImVector<ImGuiID> mIDStack;
      private ImGuiID? mEditingID;
      private ImGuizmoOperation mOperation = ImGuizmoOperation.Default;

      bool mAllowAxisFlip = true;
      float mGizmoSizeClipSpace = 0.1f;
}

public static class ImGuizmo
{
	static ImGuizmoContext gContext = new ImGuizmoContext();
	
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
   public static void DecomposeMatrixToComponents(float4x4 matrix, out float3 translation, out float3 rotation, out float3 scale){
	   translation = matrix.Translation;
	   rotation = float3.Zero;
	   scale = float3.One;
   }
   
   public static void RecomposeMatrixFromComponents(float3 translation, float3 rotation, float3 scale, float4x4 matrix){
   }
   
   public static void SetRect(float x, float y, float width, float height)
   { 
	   gContext.mX = x;
	   gContext.mY = y;
	   gContext.mWidth = width; 
	   gContext.mHeight = height;
	   gContext.mXMax = gContext.mX + gContext.mWidth;
	   gContext.mYMax = gContext.mY + gContext.mXMax;
	   gContext.mDisplayRatio = width / height;
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

   public static bool Manipulate(float4x4 view, float4x4 projection, ImGuizmoOperation operation, ImGuizmoMode mode, ref float4x4 matrix, float4x4? deltaMatrix, float3? snap, float3? localBounds, float3? boundsSnap){
	  return false;
   }
	   
   //
   // Please note that this cubeview is patented by Autodesk : https://patents.google.com/patent/US7782319B2/en
   // It seems to be a defensive patent in the US. I don't think it will bring troubles using it as
   // other software are using the same mechanics. But just in case, you are now warned!
   //
   public static void ViewManipulate(float4 view, float length, float2 position, float2 size, int backgroundColor)
   {
	   
   }

   // use this version if you did not call Manipulate before and you are just using ViewManipulate
   public static void ViewManipulate(float3 view, float3 projection, ImGuizmoOperation operation, ImGuizmoMode mode, float4x4 matrix, float length, float2 position, float2 size, int backgroundColor){
	   
   }

   public static void SetAlternativeWindow(ImGuiWindow window)
   {
	   
   }

	// ID stack/scopes
	// Read the FAQ (docs/FAQ.md or http://dearimgui.org/faq) for more details about how ID are handled in dear imgui.
	// - Those questions are answered and impacted by understanding of the ID stack system:
	//   - "Q: Why is my widget not reacting when I click on it?"
	//   - "Q: How can I have widgets with an empty label?"
	//   - "Q: How can I have multiple widgets with the same label?"
	// - Short version: ID are hashes of the entire ID stack. If you are creating widgets in a loop you most likely
	//   want to push a unique identifier (e.g. object pointer, loop index) to uniquely differentiate them.
	// - You can also use the "Label##foobar" syntax within widget label to distinguish them from each others.
	// - In this header file we use the "label"/"name" terminology to denote a string that will be displayed + used as an ID,
	//   whereas "str_id" denote a string that is only used as an ID and not normally displayed.
	public static void PushId(string stringId)
	{

	}

	public static void PushId(int intId)
	{

	}

	public static void PopId()
	{
		
	}                                                       
	
	public static ImGuiID GetId(string stringId)
	{
		return new ImGuiID();
	}
	
   // return true if the cursor is over the operation's gizmo
   public static bool IsOver(ImGuizmoOperation op)
   {
	   return false;
   }

   public static void SetGizmoSizeClipSpace(float value)
   {
	   
   }

   // Allow axis to flip
   // When true (default), the guizmo axis flip for better visibility
   // When false, they always stay along the positive world/local axis
   public static void AllowAxisFlip(bool value)
   {
	   
   }

   // Configure the limit where axis are hidden
   public static void SetAxisLimit(float value)
   {
	   
   }
   // Set an axis mask to permanently hide a given axis (true -> hidden, false -> shown)
   public static void SetAxisMask(bool x, bool y, bool z)
   {
	   
   }
   // Configure the limit where planes are hiden
   public static void SetPlaneLimit(float value)
   {
	   
   }
   // from a x,y,z point in space and using Manipulation view/projection matrix, check if mouse is in pixel radius distance of that projected point
   public static bool IsOver(float3 position, float pixelRadius)
   {
	   return false;
   }

}

