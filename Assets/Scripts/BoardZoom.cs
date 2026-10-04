using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-2000)]
public class BoardPanZoom : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private PuzzleBoard board;

    [Header("Zoom")]
    [SerializeField] private float minOrthographicSize = 2.2f;
    [SerializeField] private float maxOrthographicSize = 9f;
    [SerializeField] private float mouseWheelZoomSpeed = 0.9f;
    [SerializeField] private float pinchZoomSpeed = 0.012f;
    [SerializeField] private float fitPadding = 0.35f;

    [Header("Pan")]
    [SerializeField] private bool enableTouchPan = true;
    [SerializeField] private bool enableMouseRightDrag = true;
    [SerializeField] private float mousePanMultiplier = 1f;
    [SerializeField] private float touchPanMultiplier = 1f;
    [SerializeField] private float touchDragThresholdPixels = 12f;

    private bool touchActive;
    private bool touchDragging;
    private Vector2 touchStartScreen;
    private Vector3 touchLastWorld;

    private bool mouseDragging;
    private Vector3 mouseLastWorld;

    private readonly List<ArrowPathController> disabledArrows =
        new List<ArrowPathController>();

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

    }

    private void Start()
    {
        FitBoardImmediately();
    }

    private void Update()
    {
        if (targetCamera == null)
            return;

        HandleMouseZoom();
        HandleMousePan();
        HandleTouch();
    }

    private void HandleMouseZoom()
    {
        float wheel = Input.mouseScrollDelta.y;

        if (Mathf.Abs(wheel) < 0.0001f)
            return;

        Vector2 screenAnchor = Input.mousePosition;
        Vector3 worldBefore = ScreenToWorld(screenAnchor);

        float newSize =
            targetCamera.orthographicSize -
            wheel * mouseWheelZoomSpeed;

        SetZoomAroundScreenPoint(
            newSize,
            screenAnchor,
            worldBefore
        );
    }

    private void HandleMousePan()
    {
        if (!enableMouseRightDrag)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            mouseDragging = true;
            mouseLastWorld = ScreenToWorld(Input.mousePosition);
        }

        if (Input.GetMouseButton(1) && mouseDragging)
        {
            Vector3 currentWorld =
                ScreenToWorld(Input.mousePosition);

            Vector3 delta =
                mouseLastWorld - currentWorld;

            Pan(delta * mousePanMultiplier);
            mouseLastWorld = ScreenToWorld(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(1))
            mouseDragging = false;
    }

    private void HandleTouch()
    {
        if (Input.touchCount >= 2)
        {
            RestoreArrows();
            HandlePinch();
            touchActive = false;
            touchDragging = false;
            return;
        }

        if (!enableTouchPan || Input.touchCount != 1)
            return;

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            touchActive = true;
            touchDragging = false;
            touchStartScreen = touch.position;
            touchLastWorld = ScreenToWorld(touch.position);

            DisableArrowsForTouch();
            return;
        }

        if (!touchActive)
            return;

        if (touch.phase == TouchPhase.Moved ||
            touch.phase == TouchPhase.Stationary)
        {
            float screenDistance =
                Vector2.Distance(
                    touch.position,
                    touchStartScreen
                );

            if (!touchDragging &&
                screenDistance >= touchDragThresholdPixels)
            {
                touchDragging = true;
            }

            if (touchDragging)
            {
                Vector3 currentWorld =
                    ScreenToWorld(touch.position);

                Vector3 delta =
                    touchLastWorld - currentWorld;

                Pan(delta * touchPanMultiplier);
                touchLastWorld = ScreenToWorld(touch.position);
            }

            return;
        }

        if (touch.phase == TouchPhase.Ended)
        {
            RestoreArrows();

            if (!touchDragging)
                TryTapArrow(touchStartScreen);

            touchActive = false;
            touchDragging = false;
            return;
        }

        if (touch.phase == TouchPhase.Canceled)
        {
            RestoreArrows();
            touchActive = false;
            touchDragging = false;
        }
    }

    private void HandlePinch()
    {
        Touch a = Input.GetTouch(0);
        Touch b = Input.GetTouch(1);

        Vector2 oldA = a.position - a.deltaPosition;
        Vector2 oldB = b.position - b.deltaPosition;

        Vector2 oldCenter = (oldA + oldB) * 0.5f;
        Vector2 newCenter = (a.position + b.position) * 0.5f;

        float oldDistance = Vector2.Distance(oldA, oldB);
        float newDistance = Vector2.Distance(a.position, b.position);

        Vector3 worldBeforeZoom = ScreenToWorld(oldCenter);

        float newSize =
            targetCamera.orthographicSize -
            (newDistance - oldDistance) * pinchZoomSpeed;

        SetZoomAroundScreenPoint(
            newSize,
            oldCenter,
            worldBeforeZoom
        );

        // Move the board with the midpoint of the two fingers.
        Vector3 beforePan = ScreenToWorld(newCenter);
        Vector3 desiredPan = worldBeforeZoom - beforePan;
        Pan(desiredPan);
    }

    private void SetZoomAroundScreenPoint(
        float requestedSize,
        Vector2 screenAnchor,
        Vector3 worldAnchorBefore)
    {
        float clampedSize = Mathf.Clamp(
            requestedSize,
            minOrthographicSize,
            maxOrthographicSize
        );

        if (Mathf.Abs(
                clampedSize - targetCamera.orthographicSize
            ) < 0.0001f)
        {
            return;
        }

        targetCamera.orthographicSize = clampedSize;

        Vector3 worldAnchorAfter =
            ScreenToWorld(screenAnchor);

        targetCamera.transform.position +=
            worldAnchorBefore - worldAnchorAfter;

        targetCamera.transform.position =
            ClampCamera(targetCamera.transform.position);
    }

    private void FitBoardImmediately()
    {
        if (board == null || targetCamera == null)
            return;

        float boardWidth =
            Mathf.Max(0, board.GetWidth() - 1) * board.GetSpacing();

        float boardHeight =
            Mathf.Max(0, board.GetHeight() - 1) * board.GetSpacing();

        float fitSize = Mathf.Max(
            boardHeight * 0.5f + fitPadding,
            (boardWidth * 0.5f + fitPadding) /
            Mathf.Max(0.01f, targetCamera.aspect)
        );

        targetCamera.orthographicSize = Mathf.Clamp(
            fitSize,
            minOrthographicSize,
            maxOrthographicSize
        );

        Vector3 bottomLeft =
            board.CellToWorld(Vector2Int.zero);

        Vector3 topRight =
            board.CellToWorld(
                new Vector2Int(
                    board.GetWidth() - 1,
                    board.GetHeight() - 1
                )
            );

        Vector3 center =
            (bottomLeft + topRight) * 0.5f;

        targetCamera.transform.position =
            new Vector3(
                center.x,
                center.y,
                targetCamera.transform.position.z
            );
    }

    private void Pan(Vector3 delta)
    {
        Vector3 position =
            targetCamera.transform.position + delta;

        targetCamera.transform.position =
            ClampCamera(position);
    }

    private Vector3 ClampCamera(Vector3 position)
    {
        if (board == null || targetCamera == null)
            return position;

        Vector3 bottomLeft =
            board.CellToWorld(Vector2Int.zero);

        Vector3 topRight =
            board.CellToWorld(
                new Vector2Int(
                    board.GetWidth() - 1,
                    board.GetHeight() - 1
                )
            );

        float centerX = (bottomLeft.x + topRight.x) * 0.5f;
        float centerY = (bottomLeft.y + topRight.y) * 0.5f;

        float halfBoardWidth =
            Mathf.Abs(topRight.x - bottomLeft.x) * 0.5f;

        float halfBoardHeight =
            Mathf.Abs(topRight.y - bottomLeft.y) * 0.5f;

        float visibleHeight = targetCamera.orthographicSize;
        float visibleWidth = visibleHeight * targetCamera.aspect;

        float xLimit = Mathf.Max(
            0f,
            halfBoardWidth - visibleWidth * 0.5f
        );

        float yLimit = Mathf.Max(
            0f,
            halfBoardHeight - visibleHeight * 0.5f
        );

        position.x = Mathf.Clamp(
            position.x,
            centerX - xLimit,
            centerX + xLimit
        );

        position.y = Mathf.Clamp(
            position.y,
            centerY - yLimit,
            centerY + yLimit
        );

        return position;
    }

    private Vector3 ScreenToWorld(Vector2 screenPosition)
    {
        if (targetCamera == null)
            return Vector3.zero;

        Vector3 point =
            targetCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    Mathf.Abs(targetCamera.transform.position.z)
                )
            );

        point.z = 0f;
        return point;
    }

    private void DisableArrowsForTouch()
    {
        disabledArrows.Clear();

        ArrowPathController[] arrows =
            FindObjectsByType<ArrowPathController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        for (int i = 0; i < arrows.Length; i++)
        {
            ArrowPathController arrow = arrows[i];

            if (arrow == null || !arrow.enabled)
                continue;

            disabledArrows.Add(arrow);
            arrow.enabled = false;
        }
    }

    private void RestoreArrows()
    {
        for (int i = 0; i < disabledArrows.Count; i++)
        {
            if (disabledArrows[i] != null)
                disabledArrows[i].enabled = true;
        }

        disabledArrows.Clear();
    }

    private void TryTapArrow(Vector2 screenPosition)
    {
        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

        if (board == null)
            return;

        Vector3 worldPoint =
            ScreenToWorld(screenPosition);

        ArrowPathController best = null;
        float bestDistance = float.MaxValue;

        ArrowPathController[] arrows =
            FindObjectsByType<ArrowPathController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        for (int i = 0; i < arrows.Length; i++)
        {
            ArrowPathController arrow = arrows[i];

            if (arrow == null || arrow.IsEscaping())
                continue;

            float distance =
                DistanceToVisibleArrow(
                    arrow,
                    worldPoint
                );

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = arrow;
            }
        }

        float bodyHalfWidth =
            board.GetSpacing() * 0.18f;

        float allowed =
            Mathf.Max(0.035f, bodyHalfWidth + 0.015f);

        if (best != null && bestDistance <= allowed)
            board.TryMoveArrow(best);
    }

    private static float DistanceToVisibleArrow(
        ArrowPathController arrow,
        Vector3 point)
    {
        float best = float.MaxValue;

        LineRenderer line =
            arrow.GetComponent<LineRenderer>();

        if (line != null && line.positionCount >= 2)
        {
            for (int i = 0; i < line.positionCount - 1; i++)
            {
                Vector3 a = line.GetPosition(i);
                Vector3 b = line.GetPosition(i + 1);
                Vector3 ab = b - a;
                float sq = ab.sqrMagnitude;

                float t =
                    sq > 0.000001f
                        ? Mathf.Clamp01(
                            Vector3.Dot(point - a, ab) / sq
                        )
                        : 0f;

                Vector3 closest = a + ab * t;

                float distance = Vector2.Distance(
                    new Vector2(point.x, point.y),
                    new Vector2(closest.x, closest.y)
                );

                if (distance < best)
                    best = distance;
            }
        }

        SpriteRenderer[] renderers =
            arrow.GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];

            if (renderer == null || renderer.sprite == null)
                continue;

            Bounds bounds = renderer.bounds;

            Vector2 closest = new Vector2(
                Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(point.y, bounds.min.y, bounds.max.y)
            );

            float distance = Vector2.Distance(
                new Vector2(point.x, point.y),
                closest
            );

            if (distance < best)
                best = distance;
        }

        return best;
    }

    public void SetZoom(float size)
    {
        if (targetCamera == null)
            return;

        targetCamera.orthographicSize =
            Mathf.Clamp(
                size,
                minOrthographicSize,
                maxOrthographicSize
            );

        targetCamera.transform.position =
            ClampCamera(targetCamera.transform.position);
    }

    public void FitBoard()
    {
        FitBoardImmediately();
    }
}
