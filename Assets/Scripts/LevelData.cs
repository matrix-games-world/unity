using System.Collections.Generic;
using UnityEngine;

public enum ArrowLevelDifficulty
{
    Easy,
    Medium,
    Hard,
    Expert
}

[CreateAssetMenu(
    fileName = "Level_01",
    menuName = "Arrow Puzzle/Level Data"
)]
public class LevelData : ScriptableObject
{
    [Header("Board")]
    public int width = 12;
    public int height = 12;
    public float spacing = 0.5f;
    public int lives = 3;

    [Header("Generator")]
    [Min(1)] public int arrowCount = 10;
    public ArrowLevelDifficulty difficulty = ArrowLevelDifficulty.Medium;
    public int seed = 20261003;

    [Tooltip("Empty cells required between different arrows. 0 allows the dense reference-style maze packing.")]
    [Range(0, 1)] public int minimumSeparation = 0;

    [Tooltip("Minimum Chebyshev distance between different arrow heads.")]
    [Min(1)] public int headClearanceCells = 2;

    [Tooltip("Generator effort. The reference generator uses bounded randomized construction, so this is not an unbounded search.")]
    [Min(100)] public int shapeIterations = 1200;

    [Tooltip("Target coverage used by non-full-board levels.")]
    [Range(0.25f, 0.98f)] public float targetCoverage = 0.80f;

    [Tooltip("When enabled for a rectangular level, every grid point is assigned exactly once.")]
    public bool fillEntireBoard = true;

    [Tooltip("When enabled, generated levels are kept only when a safe sequence exists.")]
    public bool requireSolvable = true;

    [Tooltip("Keep beginning availability intentionally small on harder levels.")]
    public bool enforceDifficulty = true;

    [Header("Generated Shape")]
    [Tooltip("When enabled, only visibleCells are rendered as dots. This allows heart/leaf/custom silhouettes without extra dots around them.")]
    public bool useGeneratedCellMask = false;

    [Tooltip("Cells whose dots are allowed to remain visible for this generated shape.")]
    public List<Vector2Int> visibleCells = new List<Vector2Int>();

    [Tooltip("Human-readable generated silhouette name.")]
    public string generatedShape = "Rectangle";

    [Tooltip("Human-readable generator style name.")]
    public string generatedStyle = "ReferenceMaze";

    [Tooltip("Fraction of the intended shape occupied by arrow paths at generation time.")]
    [Range(0f, 1f)] public float generatedShapeFill = 1f;

    [Header("Default Arrow - Proof Prefab")]
    public ArrowPathController defaultArrowPrefab;

    [Header("Fallback Appearance")]
    public Sprite defaultHeadSprite;
    public Color defaultArrowColor = Color.black;

    [Header("Generated Arrows")]
    public List<ArrowData> arrows = new List<ArrowData>();
}
