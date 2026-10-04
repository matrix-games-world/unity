using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArrowCollisionFeedback : MonoBehaviour
{
    [SerializeField] private float duration = 0.20f;
    private const float HitDistanceMultiplier = 0.28f;

    private Coroutine routine;

    public void PlayCollision(ArrowPathController blocker)
    {
        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(CollisionRoutine(blocker));
    }

    private IEnumerator CollisionRoutine(ArrowPathController blocker)
    {
        ArrowPathController arrow =
            GetComponent<ArrowPathController>();

        if (arrow == null)
            yield break;

        LineRenderer line =
            arrow.GetComponent<LineRenderer>();

        SpriteRenderer head =
            FindHead(arrow);

        Vector3[] originalLine = null;

        if (line != null && line.positionCount > 0)
        {
            originalLine = new Vector3[line.positionCount];

            for (int i = 0; i < line.positionCount; i++)
                originalLine[i] = line.GetPosition(i);
        }

        Vector3 originalHeadPosition =
            head != null ? head.transform.position : Vector3.zero;

        Vector3 direction =
            DirectionToVector(arrow.GetHeadDirection());

        float spacing = 0.5f;
        PuzzleBoard board =
            GetComponentInParent<PuzzleBoard>();

        if (board != null)
            spacing = board.GetSpacing();

        float hitDistance =
            CalculateHitDistance(
                arrow,
                blocker,
                spacing
            );

        Color normalLine =
            line != null ? line.startColor : Color.white;

        Color normalHead =
            head != null ? head.color : Color.white;

        if (line != null)
        {
            line.startColor = Color.red;
            line.endColor = Color.red;
        }

        if (head != null)
            head.color = Color.red;

        float half = duration * 0.5f;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;

            float offsetAmount;

            if (time <= half)
            {
                float t = Mathf.Clamp01(time / half);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                offsetAmount = eased * hitDistance;
            }
            else
            {
                float t = Mathf.Clamp01((time - half) / half);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                offsetAmount = Mathf.Lerp(hitDistance, 0f, eased);
            }

            Vector3 offset = direction * offsetAmount;

            if (line != null && originalLine != null)
            {
                for (int i = 0; i < originalLine.Length; i++)
                    line.SetPosition(i, originalLine[i] + offset);
            }

            if (head != null)
                head.transform.position = originalHeadPosition + offset;

            yield return null;
        }

        if (line != null && originalLine != null)
        {
            for (int i = 0; i < originalLine.Length; i++)
                line.SetPosition(i, originalLine[i]);

            line.startColor = normalLine;
            line.endColor = normalLine;
        }

        if (head != null)
        {
            head.transform.position = originalHeadPosition;
            head.color = normalHead;
        }

        routine = null;
    }

    private static float CalculateHitDistance(
        ArrowPathController arrow,
        ArrowPathController blocker,
        float spacing)
    {
        float fallback = spacing * HitDistanceMultiplier;

        if (arrow == null || blocker == null)
            return fallback;

        List<Vector2Int> arrowPath = arrow.GetPath();
        List<Vector2Int> blockerPath = blocker.GetPath();

        if (arrowPath == null ||
            blockerPath == null ||
            arrowPath.Count < 2 ||
            blockerPath.Count == 0)
        {
            return fallback;
        }

        Vector2Int head =
            arrowPath[arrowPath.Count - 1];

        Vector2Int direction =
            head - arrowPath[arrowPath.Count - 2];

        int bestCells = int.MaxValue;

        for (int i = 0; i < blockerPath.Count; i++)
        {
            Vector2Int delta = blockerPath[i] - head;

            bool sameRay = false;

            if (direction == Vector2Int.right)
                sameRay = delta.y == 0 && delta.x > 0;
            else if (direction == Vector2Int.left)
                sameRay = delta.y == 0 && delta.x < 0;
            else if (direction == Vector2Int.up)
                sameRay = delta.x == 0 && delta.y > 0;
            else if (direction == Vector2Int.down)
                sameRay = delta.x == 0 && delta.y < 0;

            if (!sameRay)
                continue;

            int cells =
                Mathf.Abs(delta.x) +
                Mathf.Abs(delta.y);

            if (cells < bestCells)
                bestCells = cells;
        }

        if (bestCells == int.MaxValue)
            return fallback;

        // Leave a very small visual gap so the two outlines do not overlap.
        return Mathf.Max(
            spacing * 0.08f,
            (bestCells - 0.70f) * spacing
        );
    }

    private static SpriteRenderer FindHead(
        ArrowPathController arrow)
    {
        SpriteRenderer[] renderers =
            arrow.GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null &&
                renderers[i].sprite != null &&
                renderers[i].gameObject.name == "ArrowHead")
            {
                return renderers[i];
            }
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    private static Vector3 DirectionToVector(
        ArrowPathController.Direction direction)
    {
        switch (direction)
        {
            case ArrowPathController.Direction.Up:
                return Vector3.up;
            case ArrowPathController.Direction.Down:
                return Vector3.down;
            case ArrowPathController.Direction.Left:
                return Vector3.left;
            case ArrowPathController.Direction.Right:
                return Vector3.right;
        }

        return Vector3.right;
    }
}
