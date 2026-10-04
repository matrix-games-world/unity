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

        ClearSpawnedArrows();
        board.ResetBoard();
        board.SetLivesForLevel(level.lives);

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

        if (level.arrows != null)
        {
            for (int i = 0; i < level.arrows.Count; i++)
            {
                ArrowData data = level.arrows[i];

                if (data == null || data.path == null || data.path.Count < 2)
                    continue;

                SpawnArrow(sourcePrefab, level, data);
            }
        }

        Debug.Log(
            "ArrowLevelManager: Loaded level " +
            (currentLevelIndex + 1) +
            " with " +
            spawnedArrows.Count +
            " arrows."
        );

        loadingLevel = false;
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
