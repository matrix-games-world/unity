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

    [Tooltip("Empty cells required between different arrows. 1 means two different arrows cannot touch side-by-side or corner-to-corner.")]
    [Range(0, 1)] public int minimumSeparation = 1;

    [Tooltip("Minimum Chebyshev distance between different arrow heads.")]
    [Min(1)] public int headClearanceCells = 2;

    [Tooltip("Generator search effort used while creating different maze shapes.")]
    [Min(100)] public int shapeIterations = 1200;

    [Tooltip("Try to use most of the board without forcing every cell to contain an arrow.")]
    [Range(0.25f, 0.90f)] public float targetCoverage = 0.62f;

    [Tooltip("When enabled, the generator attempts to use the full available board area.")]
    public bool fillEntireBoard = true;

    [Tooltip("When enabled, generated levels are kept only when the solver finds a safe sequence.")]
    public bool requireSolvable = true;

    [Tooltip("Keep the number of moves available at the beginning intentionally small on harder levels.")]
    public bool enforceDifficulty = true;

    [Header("Default Arrow - Proof Prefab")]
    [Tooltip("Working Proof arrow prefab. Its sprite is copied, while ArrowPathController keeps its own connected body/head sizing.")]
    public ArrowPathController defaultArrowPrefab;

    [Header("Fallback Appearance")]
    public Sprite defaultHeadSprite;
    public Color defaultArrowColor = Color.black;

    [Header("Generated Arrows")]
    public List<ArrowData> arrows = new List<ArrowData>();
}
