using UnityEngine;

enum ToolType {
    None,
    Eraser
}

[CreateAssetMenu(fileName = "Tool", menuName = "LevelBuilding/Create Tool")]
public class BuildingTool : BuildingObjectBase {
    [SerializeField] private ToolType toolType;
    public void Use(Vector3Int position) {
        switch (toolType) {
            case ToolType.Eraser:
                ToolController.GetInstance().Eraser(position);
                break;
            default:
                Debug.LogError("Tool Type not set");
                break;
        }
    }
}

