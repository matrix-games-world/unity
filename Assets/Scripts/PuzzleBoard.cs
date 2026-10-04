using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PuzzleBoard : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private int width = 12;
    [SerializeField] private int height = 12;
    [SerializeField] private float spacing = 0.5f;

    [Header("Lives")]
    [SerializeField] private int startingLives = 3;

    private int lives;
    private bool resettingAfterLoss;
    private bool solvedNotified;

    private readonly Dictionary<Vector2Int, ArrowPathController> occupied =
        new Dictionary<Vector2Int, ArrowPathController>();

    private readonly List<ArrowPathController> arrows =
        new List<ArrowPathController>();

    public event Action<int> LivesChanged;
    public event Action LevelSolvedEvent;
    public event Action LevelLostEvent;

    public Vector3 CellToWorld(Vector2Int cell)
    {
        float offsetX = (width - 1) * spacing * 0.5f;
        float offsetY = (height - 1) * spacing * 0.5f;

        return transform.position +
               new Vector3(
                   cell.x * spacing - offsetX,
                   cell.y * spacing - offsetY,
                   0f
               );
    }

    public int GetWidth() => width;
    public int GetHeight() => height;
    public float GetSpacing() => spacing;
    public int GetLives() => lives;
    public int GetStartingLives() => startingLives;
    public int GetRemainingArrows() => arrows.Count;

    public bool IsSolved()
    {
        return arrows.Count == 0;
    }

    private void Awake()
    {
        lives = Mathf.Max(1, startingLives);
        LivesChanged?.Invoke(lives);
    }

    public void SetLivesForLevel(int value)
    {
        lives = Mathf.Max(1, value);
        solvedNotified = false;
        resettingAfterLoss = false;
        LivesChanged?.Invoke(lives);
    }

    public void ResetBoard()
    {
        occupied.Clear();
        arrows.Clear();
        solvedNotified = false;
        resettingAfterLoss = false;
        lives = Mathf.Max(1, startingLives);
        LivesChanged?.Invoke(lives);
    }

    public void RegisterArrow(
        ArrowPathController arrow,
        List<Vector2Int> path)
    {
        if (arrow == null || path == null || path.Count == 0)
            return;

        if (!arrows.Contains(arrow))
            arrows.Add(arrow);

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];

            if (occupied.TryGetValue(
                cell,
                out ArrowPathController existing) &&
                existing != null &&
                existing != arrow)
            {
                Debug.LogError(
                    "PuzzleBoard: Overlapping arrows at cell " + cell
                );
                continue;
            }

            occupied[cell] = arrow;
        }
    }

    public void UnregisterArrow(
        ArrowPathController arrow,
        List<Vector2Int> path)
    {
        if (arrow == null)
            return;

        if (path != null)
        {
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int cell = path[i];

                if (
                    occupied.TryGetValue(
                        cell,
                        out ArrowPathController owner) &&
                    owner == arrow)
                {
                    occupied.Remove(cell);
                }
            }
        }

        arrows.Remove(arrow);
    }

    public void TryMoveArrow(ArrowPathController arrow)
    {
        if (
            arrow == null ||
            arrow.IsEscaping() ||
            resettingAfterLoss)
        {
            return;
        }

        ArrowPathController blocker = null;

        if (CanArrowEscape(arrow, out blocker))
        {
            UnregisterArrow(arrow, arrow.GetPath());
            arrow.BeginEscape();

            if (IsSolved() && !solvedNotified)
            {
                solvedNotified = true;
                LevelSolvedEvent?.Invoke();
                StartCoroutine(NotifyManagerSolved());
            }

            return;
        }

        LoseLife();

        arrow.PlayBlockedFeedback();

        if (blocker != null)
        {
            blocker.PlayBlockedTargetFeedback();
        }

        ArrowCollisionFeedback feedback =
            arrow.GetComponent<ArrowCollisionFeedback>();

        if (feedback == null)
        {
            feedback =
                arrow.gameObject.AddComponent<ArrowCollisionFeedback>();
        }

        feedback.PlayCollision(blocker);

        if (lives <= 0)
        {
            LevelLostEvent?.Invoke();
            StartCoroutine(RestartAfterLoss());
        }
    }

    public bool CanArrowEscape(
        ArrowPathController arrow,
        out ArrowPathController blocker)
    {
        blocker = null;

        if (arrow == null)
            return false;

        List<Vector2Int> path = arrow.GetPath();

        if (path == null || path.Count < 2)
            return false;

        Vector2Int head = path[path.Count - 1];

        Vector2Int direction =
            DirectionToCellStep(arrow.GetHeadDirection());

        Vector2Int check = head + direction;

        while (IsInside(check))
        {
            if (
                occupied.TryGetValue(
                    check,
                    out ArrowPathController occupant) &&
                occupant != null &&
                occupant != arrow)
            {
                blocker = occupant;
                return false;
            }

            check += direction;
        }

        return true;
    }

    private void LoseLife()
    {
        lives = Mathf.Max(0, lives - 1);
        LivesChanged?.Invoke(lives);

        Debug.Log(
            "PuzzleBoard: Blocked move. Lives = " + lives
        );
    }

    private bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 &&
               cell.x < width &&
               cell.y >= 0 &&
               cell.y < height;
    }

    private Vector2Int DirectionToCellStep(
        ArrowPathController.Direction direction)
    {
        switch (direction)
        {
            case ArrowPathController.Direction.Right:
                return Vector2Int.right;

            case ArrowPathController.Direction.Left:
                return Vector2Int.left;

            case ArrowPathController.Direction.Up:
                return Vector2Int.up;

            case ArrowPathController.Direction.Down:
                return Vector2Int.down;
        }

        return Vector2Int.right;
    }

    private IEnumerator NotifyManagerSolved()
    {
        yield return new WaitForSeconds(0.35f);

        ArrowLevelManager manager =
            FindFirstObjectByType<ArrowLevelManager>();

        if (manager != null)
            manager.LevelSolved();
    }

    private IEnumerator RestartAfterLoss()
    {
        if (resettingAfterLoss)
            yield break;

        resettingAfterLoss = true;

        yield return new WaitForSeconds(0.35f);

        ArrowLevelManager manager =
            FindFirstObjectByType<ArrowLevelManager>();

        if (manager != null)
            manager.RestartLevel();
        else
            ResetBoard();
    }
}
