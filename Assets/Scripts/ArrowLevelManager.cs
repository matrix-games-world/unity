using System.Collections.Generic;
using UnityEngine;

public class ArrowLevelManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PuzzleBoard board;
    [SerializeField] private ArrowPathController arrowPrefab;

    [Header("Levels")]
    [SerializeField]
    private List<LevelData> levels =
        new List<LevelData>();

    [SerializeField] private int currentLevelIndex = 0;

    private readonly List<ArrowPathController> spawnedArrows =
        new List<ArrowPathController>();

    private bool loadingLevel;

    private void Awake()
    {
        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();
    }

    private void Start()
    {
        LoadCurrentLevel();
    }

    public void LoadCurrentLevel()
    {
        if (levels == null || levels.Count == 0)
        {
            Debug.LogWarning("ArrowLevelManager: No levels assigned.");
            return;
        }

        if (currentLevelIndex < 0 || currentLevelIndex >= levels.Count)
        {
            Debug.LogError(
                "ArrowLevelManager: Current level index is out of range."
            );
            return;
        }

        LoadLevel(levels[currentLevelIndex]);
    }

    public void LoadLevel(LevelData level)
    {
        if (loadingLevel || level == null)
            return;

        loadingLevel = true;

        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

        if (board == null)
        {
            Debug.LogError("ArrowLevelManager: PuzzleBoard not found.");
            loadingLevel = false;
            return;
        }

        board.ConfigureBoard(
            level.width,
            level.height,
            level.spacing
        );

        ClearSpawnedArrows();
        board.ResetBoard();
        board.SetLivesForLevel(level.lives);
        RefreshBoardPresentation(level);

        ArrowPathController sourcePrefab = GetLevelArrowPrefab(level);

        if (sourcePrefab == null)
        {
            Debug.LogError(
                "ArrowLevelManager: Assign LevelData > Default Arrow Prefab " +
                "or ArrowLevelManager > Arrow Prefab."
            );
            loadingLevel = false;
            return;
        }

        HashSet<Vector2Int> claimedCells =
            new HashSet<Vector2Int>();

        if (level.arrows != null)
        {
            for (int i = 0; i < level.arrows.Count; i++)
            {
                ArrowData data = level.arrows[i];

                if (!IsLevelPathValid(
                        data != null ? data.path : null,
                        level.width,
                        level.height,
                        claimedCells))
                {
                    Debug.LogError(
                        "ArrowLevelManager: Skipping arrow " + i +
                        " because its path is invalid, outside the level, or overlaps another arrow."
                    );
                    continue;
                }

                SpawnArrow(sourcePrefab, level, data);
            }
        }

        BoardPanZoom zoom = FindFirstObjectByType<BoardPanZoom>();
        if (zoom != null)
            zoom.FitBoard();

        Debug.Log(
            "ArrowLevelManager: Loaded level " +
            (currentLevelIndex + 1) +
            " with " +
            spawnedArrows.Count +
            " arrows."
        );

        loadingLevel = false;
    }

    private void RefreshBoardPresentation(LevelData level)
    {
        GridRenderer[] grids =
            board.GetComponentsInChildren<GridRenderer>(true);

        for (int i = 0; i < grids.Length; i++)
        {
            if (grids[i] == null)
                continue;

            grids[i].ConfigureGrid(
                level.width,
                level.height,
                level.spacing
            );
        }
    }

    private static bool IsLevelPathValid(
        List<Vector2Int> path,
        int width,
        int height,
        HashSet<Vector2Int> claimedCells)
    {
        if (path == null || path.Count < 2)
            return false;

        HashSet<Vector2Int> localCells =
            new HashSet<Vector2Int>();

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];

            if (cell.x < 0 || cell.x >= width ||
                cell.y < 0 || cell.y >= height)
            {
                return false;
            }

            if (!localCells.Add(cell) || claimedCells.Contains(cell))
                return false;
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2Int delta = path[i + 1] - path[i];

            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                return false;
        }

        foreach (Vector2Int cell in localCells)
            claimedCells.Add(cell);

        return true;
    }

    private ArrowPathController GetLevelArrowPrefab(LevelData level)
    {
        if (level != null && level.defaultArrowPrefab != null)
            return level.defaultArrowPrefab;

        return arrowPrefab;
    }

    private void SpawnArrow(
        ArrowPathController sourcePrefab,
        LevelData level,
        ArrowData data)
    {
        Sprite headSprite = data.headSprite;
        Color color = data.arrowColor;

        SpriteRenderer sourceHead =
            FindHeadSpriteRenderer(sourcePrefab);

        if (sourceHead != null && sourceHead.sprite != null)
            headSprite = sourceHead.sprite;

        LineRenderer sourceLine =
            sourcePrefab.GetComponent<LineRenderer>();

        if (sourceLine != null)
            color = sourceLine.startColor;

        if (headSprite == null)
            headSprite = level.defaultHeadSprite;

        if (color.a <= 0.001f)
            color = level.defaultArrowColor;

        ArrowPathController arrow =
            Instantiate(sourcePrefab, board.transform);

        arrow.Configure(
            new List<Vector2Int>(data.path),
            headSprite,
            color,
            level.width,
            level.height,
            level.spacing
        );

        ArrowTouchPrecision precision =
            arrow.GetComponent<ArrowTouchPrecision>();

        if (precision == null)
            precision = arrow.gameObject.AddComponent<ArrowTouchPrecision>();

        precision.ApplyNow();

        spawnedArrows.Add(arrow);
    }

    private static SpriteRenderer FindHeadSpriteRenderer(
        ArrowPathController arrow)
    {
        if (arrow == null)
            return null;

        SpriteRenderer[] renderers =
            arrow.GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];

            if (renderer == null || renderer.sprite == null)
                continue;

            if (renderer.gameObject.name == "ArrowHead")
                return renderer;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    private void ClearSpawnedArrows()
    {
        for (int i = 0; i < spawnedArrows.Count; i++)
        {
            ArrowPathController arrow = spawnedArrows[i];

            if (arrow != null)
                Destroy(arrow.gameObject);
        }

        spawnedArrows.Clear();
    }

    public void LevelSolved()
    {
        if (levels == null || levels.Count == 0)
            return;

        if (currentLevelIndex + 1 >= levels.Count)
        {
            Debug.Log("ArrowLevelManager: ALL LEVELS COMPLETED!");
            return;
        }

        currentLevelIndex++;
        LoadCurrentLevel();
    }

    public int GetCurrentLevelIndex()
    {
        return currentLevelIndex;
    }

    public LevelData GetCurrentLevel()
    {
        if (levels == null ||
            currentLevelIndex < 0 ||
            currentLevelIndex >= levels.Count)
        {
            return null;
        }

        return levels[currentLevelIndex];
    }

    public void RestartLevel()
    {
        LoadCurrentLevel();
    }

    public void NextLevel()
    {
        if (levels == null ||
            currentLevelIndex + 1 >= levels.Count)
        {
            return;
        }

        currentLevelIndex++;
        LoadCurrentLevel();
    }
}
