
using System.Collections.Generic;
using UnityEngine;

public class InterlockedTestLevel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PuzzleBoard board;
    [SerializeField] private Sprite headSprite;

    [Header("Test")]
    [SerializeField] private bool generateOnStart = true;

    private readonly Color[] colors =
    {
        new Color(0.10f, 0.10f, 0.10f),
        new Color(0.12f, 0.36f, 0.95f),
        new Color(0.90f, 0.16f, 0.14f),
        new Color(0.12f, 0.68f, 0.30f),
        new Color(0.66f, 0.30f, 0.90f),
        new Color(0.95f, 0.58f, 0.08f)
    };

    private void Start()
    {
        if (board == null)
            board = GetComponent<PuzzleBoard>();

        if (generateOnStart)
            Generate();
    }

    [ContextMenu("Generate Interlocked Level")]
    public void Generate()
    {
        ClearOldArrows();

        // C is immediately free.
        CreateArrow(
            "Arrow_C",
            new[]
            {
                new Vector2Int(8, 9),
                new Vector2Int(9, 9),
                new Vector2Int(10, 9)
            },
            0
        );

        // B is blocked by C until C leaves.
        CreateArrow(
            "Arrow_B",
            new[]
            {
                new Vector2Int(8, 3),
                new Vector2Int(8, 4),
                new Vector2Int(8, 5),
                new Vector2Int(8, 6)
            },
            1
        );

        // A is blocked by B until B leaves.
        CreateArrow(
            "Arrow_A",
            new[]
            {
                new Vector2Int(1, 6),
                new Vector2Int(2, 6),
                new Vector2Int(3, 6),
                new Vector2Int(4, 6)
            },
            2
        );

        // D is blocked by A.
        CreateArrow(
            "Arrow_D",
            new[]
            {
                new Vector2Int(2, 2),
                new Vector2Int(2, 3),
                new Vector2Int(2, 4),
                new Vector2Int(2, 5)
            },
            3
        );

        // E is blocked by B.
        CreateArrow(
            "Arrow_E",
            new[]
            {
                new Vector2Int(4, 3),
                new Vector2Int(5, 3),
                new Vector2Int(6, 3),
                new Vector2Int(7, 3)
            },
            4
        );

        // F is blocked by E.
        CreateArrow(
            "Arrow_F",
            new[]
            {
                new Vector2Int(6, 0),
                new Vector2Int(6, 1),
                new Vector2Int(6, 2)
            },
            5
        );

        // K is free.
        CreateArrow(
            "Arrow_K",
            new[]
            {
                new Vector2Int(11, 10),
                new Vector2Int(11, 9),
                new Vector2Int(11, 8),
                new Vector2Int(11, 7),
                new Vector2Int(11, 6)
            },
            0
        );

        // L is blocked by K.
        CreateArrow(
            "Arrow_L",
            new[]
            {
                new Vector2Int(5, 7),
                new Vector2Int(6, 7),
                new Vector2Int(7, 7),
                new Vector2Int(8, 7),
                new Vector2Int(9, 7),
                new Vector2Int(10, 7)
            },
            1
        );

        // N is blocked by B.
        CreateArrow(
            "Arrow_N",
            new[]
            {
                new Vector2Int(5, 5),
                new Vector2Int(6, 5),
                new Vector2Int(7, 5)
            },
            2
        );

        // O is blocked by A.
        CreateArrow(
            "Arrow_O",
            new[]
            {
                new Vector2Int(1, 10),
                new Vector2Int(1, 9),
                new Vector2Int(1, 8),
                new Vector2Int(1, 7)
            },
            3
        );

        // P is free.
        CreateArrow(
            "Arrow_P",
            new[]
            {
                new Vector2Int(6, 11),
                new Vector2Int(5, 11),
                new Vector2Int(4, 11),
                new Vector2Int(3, 11),
                new Vector2Int(2, 11)
            },
            4
        );

        // Q is blocked by E.
        CreateArrow(
            "Arrow_Q",
            new[]
            {
                new Vector2Int(4, 0),
                new Vector2Int(4, 1),
                new Vector2Int(4, 2)
            },
            5
        );

        Debug.Log(
            "InterlockedTestLevel: Generated 12 arrows."
        );
    }

    private void CreateArrow(
        string arrowName,
        Vector2Int[] cells,
        int colorIndex)
    {
        GameObject obj =
            new GameObject(arrowName);

        obj.transform.SetParent(
            transform,
            true
        );

        ArrowPathController controller =
            obj.AddComponent<ArrowPathController>();

        controller.Configure(
            new List<Vector2Int>(cells),
            headSprite,
            colors[colorIndex],
            board != null
                ? board.GetSpacing()
                : 0.5f
        );
    }

    private void ClearOldArrows()
    {
        List<GameObject> old =
            new();

        for (
            int i = 0;
            i < transform.childCount;
            i++
        )
        {
            Transform child =
                transform.GetChild(i);

            if (
                child.GetComponent<
                    ArrowPathController
                >() != null
            )
            {
                old.Add(
                    child.gameObject
                );
            }
        }

        for (int i = 0; i < old.Count; i++)
            Destroy(old[i]);
    }
}
