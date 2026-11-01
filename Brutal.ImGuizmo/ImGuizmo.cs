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

      public ImDrawList? mDrawList;
      public ImGuizmoStyle mStyle;

      public ImGuizmoMode mMode;
      public float4x4 mViewMat;
      public float4x4 mProjectionMat;
      public float4x4 mModel;
      public float4x4 mModelLocal; // orthonormalized model
      public float4x4 mModelInverse;
      public float4x4 mModelSource;
      public float4x4 mModelSourceInverse;
      public float4x4 mMVP;
      public float4x4 mMVPLocal; // MVP with full model matrix whereas mMVP's model matrix might only be translation in case of World space edition
      public float4x4 mViewProjection;

      public float4 mModelScaleOrigin;
      public float4 mCameraEye;
      public float4 mCameraRight;
      public float4 mCameraDir;
      public float4 mCameraUp;
      public float4 mRayOrigin;
      public float4 mRayVector;

      public float  mRadiusSquareCenter;
      public float2 mScreenSquareCenter;
      public float2 mScreenSquareMin;
      public float2 mScreenSquareMax;

      public float mScreenFactor;
      public float4 mRelativeOrigin;

      public bool mbUsing = false;
      public bool mbUsingViewManipulate = false;
      public bool mbEnable = true;
      public bool mbMouseOver;
      public bool mReversed; // reversed projection matrix
      public bool mIsViewManipulatorHovered = false;

      // translation
      public float4 mTranslationPlan;
      public float4 mTranslationPlanOrigin;
      public float4 mMatrixOrigin;
      public float4 mTranslationLastDelta;

      // rotation
      public float4 mRotationVectorSource;
      public float mRotationAngle;
      public float mRotationAngleOrigin;
      //float4 mWorldToLocalAxis;

      // scale
      public float4 mScale;
      public float4 mScaleValueOrigin;
      public float4 mScaleLast;
      public float mSaveMousePosx;

      // save axis factor when using gizmo
      public bool3 mBelowAxisLimit;
      public int mAxisMask = 0;
      public bool3 mBelowPlaneLimit;
      public float3 mAxisFactor;

      public float mAxisLimit=0.0025f;
      public float mPlaneLimit=0.02f;

      // bounds stretching
      public float4 mBoundsPivot;
      public float4 mBoundsAnchor;
      public float4 mBoundsPlan;
      public float4 mBoundsLocalPivot;
      public int mBoundsBestAxis;
      public int2 mBoundsAxis;
      public bool mbUsingBounds = false;
      public float4x4 mBoundsMatrix;

      //
      public int mCurrentOperation;

      public float mX = 0.0f;
      public float mY = 0.0f;
      public float mWidth = 0.0f;
      public float mHeight = 0.0f;
      public float mXMax = 0.0f;
      public float mYMax = 0.0f;
      public float mDisplayRatio = 1.0f;

      public bool mIsOrthographic = false;
      // check to not have multiple gizmo highlighted at the same time
      public bool mbOverGizmoHotspot = false;

      public ImGuiWindow? mAlternativeWindow;
      public ImVector<ImGuiID> mIDStack;
      public ImGuiID? mEditingID;
      public ImGuizmoOperation mOperation = ImGuizmoOperation.Default;

      public bool mAllowAxisFlip = true;
      public float mGizmoSizeClipSpace = 0.1f;
}

internal enum ImGuizmoMoveType
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
	MT_SCALE_XYZ
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
	  
	   bool manipulated = false;
/*
 * gContext.mDrawList->PushClipRect (ImVec2 (gContext.mX, gContext.mY), ImVec2 (gContext.mX + gContext.mWidth, gContext.mY + gContext.mHeight), false);
 */
	     // Scale is always local or matrix will be skewed when applying world scale or oriented matrix
	     ImGuizmoMode mMode = (operation == ImGuizmoOperation.Scale) ? ImGuizmoMode.Local : mode;
	     ComputeContext(view, projection, matrix, mMode);

	     // set delta to identity
	     if (deltaMatrix != null)
	     {
		     deltaMatrix = float4x4.Identity;
	     }

	     // behind camera
	     float4 camSpacePosition = TransformPoint(float4.Zero, gContext.mMVP);
	     if (!gContext.mIsOrthographic && camSpacePosition.Z < 0.001f && !gContext.mbUsing)
	     {
	        return false;
	     }

	     ImGuizmoMoveType type = ImGuizmoMoveType.MT_NONE;
	     if (gContext.mbEnable)
	     {
	        if (!gContext.mbUsingBounds)
	        {
	           manipulated = (HandleTranslation(matrix, deltaMatrix, operation, type, snap) ||
	                         HandleScale(matrix, deltaMatrix, operation, type, snap) ||
	                         HandleRotation(matrix, deltaMatrix, operation, type, snap));
	        }
	     }

	      //if (localBounds != null && !gContext.mbUsing)
	      //{
	     //    manipulated |= HandleAndDrawLocalBounds(localBounds, (matrix_t*)matrix, boundsSnap, operation);
	      //}

	     gContext.mOperation = operation;
	     if (!gContext.mbUsingBounds)
	     {
	        DrawRotationGizmo(operation, type);
	        DrawTranslationGizmo(operation, type);
	        DrawScaleGizmo(operation, type);
	        DrawScaleUniveralGizmo(operation, type);
	     }

	     // gContext.mDrawList?.PopClipRect ();

	   return manipulated;
   }

   private static float4 TransformPoint(float4 src, float4x4 matrix)
   {
	   float4 result = float4.Zero;

	   result.X = src.X * matrix.M11 + src.Y * matrix.M21 + src.Z * matrix.M31 + matrix.M41;
	   result.Y = src.X * matrix.M12 + src.Y * matrix.M22 + src.Z * matrix.M32 + matrix.M42;
	   result.Z = src.X * matrix.M13 + src.Y * matrix.M23 + src.Z * matrix.M33 + matrix.M43;
	   result.W = src.X * matrix.M14 + src.Y * matrix.M24 + src.Z * matrix.M34 + matrix.M44;
	   
	   return result;
   }
   
   private static void DrawScaleUniveralGizmo(ImGuizmoOperation operation, ImGuizmoMoveType type)
   {
	   throw new NotImplementedException();
   }

   private static void DrawScaleGizmo(ImGuizmoOperation operation, ImGuizmoMoveType type)
   {
	   throw new NotImplementedException();
   }

   private static void DrawTranslationGizmo(ImGuizmoOperation operation, ImGuizmoMoveType type)
   {
	   throw new NotImplementedException();
   }

   private static void DrawRotationGizmo(ImGuizmoOperation operation, ImGuizmoMoveType type)
   {
	   throw new NotImplementedException();
   }

   private static bool HandleRotation(float4x4 matrix, float4x4? deltaMatrix, ImGuizmoOperation operation, ImGuizmoMoveType type, float3? snap)
   {
	   throw new NotImplementedException();
   }

   private static bool HandleScale(float4x4 matrix, float4x4? deltaMatrix, ImGuizmoOperation operation, ImGuizmoMoveType type, float3? snap)
   {
	   throw new NotImplementedException();
   }

   private static bool HandleTranslation(float4x4 matrix, float4x4? deltaMatrix, ImGuizmoOperation operation, ImGuizmoMoveType type, float3? snap)
   {
	   throw new NotImplementedException();
   }

   private static void ComputeContext(float4x4 view, float4x4 projection, float4x4 matrix, ImGuizmoMode mode)
   {
	   gContext.mMode = mode;
	   gContext.mViewMat = view;
	   gContext.mProjectionMat = projection;
	   gContext.mbMouseOver = IsHoveringWindow();
	   gContext.mModelLocal = matrix;
	   // gContext.mModelLocal.OrthoNormalize();

	   if (mode == ImGuizmoMode.Local)
	   {
		   gContext.mModel = gContext.mModelLocal;
	   }
	   else
	   {
		   // gContext.mModel.Translation(matrix.Translation);
	   }

	   gContext.mModelSource = matrix;
	   //gContext.mModelScaleOrigin.Set(gContext.mModelSource.v.right.Length(), gContext.mModelSource.v.up.Length(), gContext.mModelSource.v.dir.Length());
	   
	   /*

	      gContext.mModelInverse.Inverse(gContext.mModel);
	      gContext.mModelSourceInverse.Inverse(gContext.mModelSource);
	      */
	   
	   
	      gContext.mViewProjection = gContext.mViewMat * gContext.mProjectionMat;
	      gContext.mMVP = gContext.mModel * gContext.mViewProjection;
	      gContext.mMVPLocal = gContext.mModelLocal * gContext.mViewProjection;

	      float4x4.Invert(gContext.mViewMat, out var viewInverse);
	      // gContext.mCameraDir = viewInverse.v.dir;
	      // gContext.mCameraEye = viewInverse.v.position;
	      // gContext.mCameraRight = viewInverse.v.right;
	      // gContext.mCameraUp = viewInverse.v.up;

	      // projection reverse
	      float4 nearPos = new float4(0.0f, 0.0f, 1.0f, 1.0f) * gContext.mProjectionMat;
	      float4 farPos = new float4(0.0f, 0.0f, 2.0f, 1.0f) * gContext.mProjectionMat;

	       gContext.mReversed = (nearPos.Z/nearPos.W) > (farPos.Z / farPos.W);

	      // compute scale from the size of camera right vector projected on screen at the matrix position
	      // float4 pointRight = viewInverse.v.right;
	      // TransformPoint(pointRight, gContext.mViewProjection);

	      // float4 rightViewInverse = viewInverse.v.right;
	      // rightViewInverse.TransformVector(gContext.mModelInverse);
	      // float rightLength = GetSegmentLengthClipSpace(new float2(0.0f, 0.0f), rightViewInverse);
	      // gContext.mScreenFactor = gContext.mGizmoSizeClipSpace / rightLength;

	      float2 centerSSpace = worldToPos(new float2(0.0f, 0.0f), gContext.mMVP);
	      gContext.mScreenSquareCenter = centerSSpace;
	      gContext.mScreenSquareMin = new float2(centerSSpace.X - 10.0f, centerSSpace.Y - 10.0f);
	      gContext.mScreenSquareMax = new float2(centerSSpace.X + 10.0f, centerSSpace.Y + 10.0f);

	      ComputeCameraRay(gContext.mRayOrigin, gContext.mRayVector);
	    
   }

   private static float2 worldToPos(float2 vec2, float4x4 gContextMMvp)
   {
	   return float2.Zero;
   }

   private static void ComputeCameraRay(float4 gContextMRayOrigin, float4 gContextMRayVector)
   {
	   
   }

   private static bool IsHoveringWindow()
   {
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

