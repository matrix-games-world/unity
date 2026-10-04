using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArrowCollisionFeedback : MonoBehaviour
{
    [Header("Collision Feel")]
    [SerializeField] private float forwardDuration = 0.10f;
    [SerializeField] private float returnDuration = 0.13f;

    [SerializeField]
    [Range(0.02f, 0.25f)]
    private float safetyGap = 0.06f;

    private Coroutine routine;

    public void PlayCollision(
        ArrowPathController blocker)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }

        routine =
            StartCoroutine(
                CollisionRoutine(
                    blocker
                )
            );
    }

    private IEnumerator CollisionRoutine(
        ArrowPathController blocker)
    {
        ArrowPathController arrow =
            GetComponent<ArrowPathController>();

        if (arrow == null)
        {
            routine = null;
            yield break;
        }

        PuzzleBoard board =
            GetComponentInParent<PuzzleBoard>();

        float spacing =
            board != null
                ? board.GetSpacing()
                : 0.5f;

        float travelDistance =
            CalculateHitTravelDistance(
                arrow,
                blocker,
                spacing
            );

        Color normalLine = Color.white;
        Color normalHead = Color.white;

        LineRenderer line =
            arrow.GetComponent<LineRenderer>();

        SpriteRenderer head =
            FindHead(arrow);

        if (line != null)
            normalLine = line.startColor;

        if (head != null)
            normalHead = head.color;

        if (line != null)
        {
            line.startColor = Color.red;
            line.endColor = Color.red;
        }

        if (head != null)
            head.color = Color.red;

        arrow.PlayBlockedMotion(
            travelDistance,
            forwardDuration,
            returnDuration
        );

        yield return new WaitForSeconds(
            forwardDuration +
            returnDuration
        );

        if (line != null)
        {
            line.startColor = normalLine;
            line.endColor = normalLine;
        }

        if (head != null)
            head.color = normalHead;

        routine = null;
    }

    private float CalculateHitTravelDistance(
        ArrowPathController arrow,
        ArrowPathController blocker,
        float spacing)
    {
        if (
            arrow == null ||
            blocker == null
        )
        {
            return spacing * 0.18f;
        }

        List<Vector2Int> arrowPath =
            arrow.GetPath();

        List<Vector2Int> blockerPath =
            blocker.GetPath();

        if (
            arrowPath == null ||
            blockerPath == null ||
            arrowPath.Count < 2 ||
            blockerPath.Count == 0
        )
        {
            return spacing * 0.18f;
        }

        Vector2Int head =
            arrowPath[
                arrowPath.Count - 1
            ];

        Vector2Int direction =
            head -
            arrowPath[
                arrowPath.Count - 2
            ];

        int bestCells =
            int.MaxValue;

        for (
            int i = 0;
            i < blockerPath.Count;
            i++
        )
        {
            Vector2Int delta =
                blockerPath[i] -
                head;

            bool sameRay =
                false;

            if (
                direction ==
                Vector2Int.right
            )
            {
                sameRay =
                    delta.y == 0 &&
                    delta.x > 0;
            }
            else if (
                direction ==
                Vector2Int.left
            )
            {
                sameRay =
                    delta.y == 0 &&
                    delta.x < 0;
            }
            else if (
                direction ==
                Vector2Int.up
            )
            {
                sameRay =
                    delta.x == 0 &&
                    delta.y > 0;
            }
            else if (
                direction ==
                Vector2Int.down
            )
            {
                sameRay =
                    delta.x == 0 &&
                    delta.y < 0;
            }

            if (!sameRay)
                continue;

            int cells =
                Mathf.Abs(delta.x) +
                Mathf.Abs(delta.y);

            if (cells < bestCells)
                bestCells = cells;
        }

        if (
            bestCells ==
            int.MaxValue
        )
        {
            return spacing * 0.18f;
        }

        float blockerHalfBody =
            spacing * 0.18f;

        float travel =
            bestCells *
            spacing -
            blockerHalfBody -
            safetyGap;

        return Mathf.Clamp(
            travel,
            spacing * 0.05f,
            bestCells *
            spacing
        );
    }

    private static SpriteRenderer FindHead(
        ArrowPathController arrow)
    {
        if (arrow == null)
            return null;

        SpriteRenderer[] renderers =
            arrow.GetComponentsInChildren<
                SpriteRenderer
            >(true);

        for (
            int i = 0;
            i < renderers.Length;
            i++
        )
        {
            if (
                renderers[i] != null &&
                renderers[i].sprite != null &&
                renderers[i].gameObject.name ==
                "ArrowHead"
            )
            {
                return renderers[i];
            }
        }

        for (
            int i = 0;
            i < renderers.Length;
            i++
        )
        {
            if (
                renderers[i] != null &&
                renderers[i].sprite != null
            )
            {
                return renderers[i];
            }
        }

        return null;
    }
}
