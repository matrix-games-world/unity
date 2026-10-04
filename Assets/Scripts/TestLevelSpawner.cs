
using System.Collections.Generic;
using UnityEngine;

public class TestLevelSpawner : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private int gridWidth = 12;
    [SerializeField] private int gridHeight = 12;
    [SerializeField] private float spacing = 0.5f;

    [Header("Arrow")]
    [SerializeField] private Sprite headSprite;
    [SerializeField] private float lineWidth = 0.14f;

    [Header("Test Level")]
    [SerializeField] private int arrowCount = 12;
    [SerializeField] private int seed = 20261003;

    [SerializeField] private Transform arrowsParent;

    private readonly List<Vector2Int> reservedCells = new();

    private readonly Color[] colors =
    {
        new Color(0.10f, 0.10f, 0.10f),
        new Color(0.10f, 0.35f, 0.90f),
        new Color(0.85f, 0.15f, 0.15f),
        new Color(0.10f, 0.65f, 0.30f),
        new Color(0.70f, 0.35f, 0.90f),
        new Color(0.95f, 0.60f, 0.10f)
    };

    private void Start()
    {
        Random.InitState(seed);

        if (arrowsParent == null)
        {
            GameObject root =
                new GameObject("TestArrows");

            arrowsParent = root.transform;
        }

        GenerateLevel();
    }

    [ContextMenu("Generate Test Level")]
    public void GenerateLevel()
    {
        if (arrowsParent == null)
        {
            GameObject root =
                new GameObject("TestArrows");

            arrowsParent = root.transform;
        }

        ClearChildren();
        reservedCells.Clear();

        int created = 0;
        int safety = 0;

        while (created < arrowCount && safety < 5000)
        {
            safety++;

            List<Vector2Int> candidate =
                TryCreateRandomPath();

            if (candidate == null)
                continue;

            if (PathOverlaps(candidate))
                continue;

            if (!HasEnoughTurns(candidate))
                continue;

            CreateArrow(candidate, created);
            Reserve(candidate);

            created++;
        }

        Debug.Log(
            $"TestLevelSpawner: Created {created} arrows."
        );
    }

    private List<Vector2Int> TryCreateRandomPath()
    {
        int startX =
            Random.Range(1, gridWidth - 2);

        int startY =
            Random.Range(1, gridHeight - 2);

        Vector2Int current =
            new Vector2Int(startX, startY);

        int length =
            Random.Range(5, 10);

        List<Vector2Int> path =
            new List<Vector2Int>();

        path.Add(current);

        Vector2Int lastDirection =
            Vector2Int.zero;

        for (int i = 1; i < length; i++)
        {
            List<Vector2Int> candidates =
                new List<Vector2Int>();

            Vector2Int[] directions =
            {
                Vector2Int.right,
                Vector2Int.left,
                Vector2Int.up,
                Vector2Int.down
            };

            for (int d = 0; d < directions.Length; d++)
            {
                Vector2Int dir = directions[d];

                // Prefer turns, but still allow straight runs.
                if (dir == -lastDirection && lastDirection != Vector2Int.zero)
                    continue;

                Vector2Int next =
                    current + dir;

                if (!IsInsideBoard(next))
                    continue;

                if (path.Contains(next))
                    continue;

                candidates.Add(next);
            }

            if (candidates.Count == 0)
                return null;

            Vector2Int chosen;

            // 65% chance to choose a direction different from the previous one.
            if (lastDirection != Vector2Int.zero &&
                Random.value < 0.65f)
            {
                List<Vector2Int> turnOptions =
                    new List<Vector2Int>();

                for (int c = 0; c < candidates.Count; c++)
                {
                    if (candidates[c] - current != lastDirection)
                        turnOptions.Add(candidates[c]);
                }

                chosen =
                    turnOptions.Count > 0
                    ? turnOptions[Random.Range(0, turnOptions.Count)]
                    : candidates[Random.Range(0, candidates.Count)];
            }
            else
            {
                chosen =
                    candidates[Random.Range(0, candidates.Count)];
            }

            lastDirection =
                chosen - current;

            current = chosen;
            path.Add(current);
        }

        return path;
    }

    private bool HasEnoughTurns(List<Vector2Int> path)
    {
        if (path.Count < 3)
            return false;

        int turns = 0;
        Vector2Int previous =
            path[1] - path[0];

        for (int i = 2; i < path.Count; i++)
        {
            Vector2Int current =
                path[i] - path[i - 1];

            if (current != previous)
                turns++;

            previous = current;
        }

        return turns >= 2;
    }

    private bool PathOverlaps(List<Vector2Int> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            if (reservedCells.Contains(path[i]))
                return true;
        }

        return false;
    }

    private void Reserve(List<Vector2Int> path)
    {
        reservedCells.AddRange(path);
    }

    private bool IsInsideBoard(Vector2Int cell)
    {
        return
            cell.x >= 0 &&
            cell.x < gridWidth &&
            cell.y >= 0 &&
            cell.y < gridHeight;
    }

    private void CreateArrow(
        List<Vector2Int> path,
        int index)
    {
        GameObject arrowObject =
            new GameObject(
                $"Arrow_{index + 1:00}"
            );

        arrowObject.transform.SetParent(
            arrowsParent,
            true
        );

        ArrowPathController controller =
            arrowObject.AddComponent<ArrowPathController>();

        Color color =
            colors[index % colors.Length];

        controller.Configure(
            path,
            headSprite,
            color,
            gridWidth,
            gridHeight,
            spacing
        );
    }

    private void ClearChildren()
    {
        for (int i = arrowsParent.childCount - 1; i >= 0; i--)
        {
            Destroy(arrowsParent.GetChild(i).gameObject);
        }
    }
}
