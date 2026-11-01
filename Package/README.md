# Brutal.ImGuizmo
A ImGuizmo port for Brutal.ImGui. 

**No Association with Original Author or Brutal framework**

```csharp
ImGuizmo.BeginFrame();
ImGuizmo.SetRect(0, 0, io.DisplaySize.X, io.DisplaySize.Y);
ImGuizmo.Manipulate(view, projection, ImGuizmoOperation.Universal, ImGuizmoMode.World, ref model);
```