using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArrowPathController : MonoBehaviour
{
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right
    }

    [Header("Grid")]
    [SerializeField] private int gridWidth = 12;
    [SerializeField] private int gridHeight = 12;
    [SerializeField] private float spacing = 0.5f;

    [Header("Path - Tail to Head")]
    [SerializeField] private List<Vector2Int> path = new List<Vector2Int>();

    [Header("Level Data")]
    [SerializeField] private ArrowData arrowData;

    [Header("Head")]
    [SerializeField] private Direction headDirection = Direction.Right;

    [Header("Appearance")]
    [SerializeField] private float lineWidth = 0.24f;
    [SerializeField] private Color arrowColor = Color.black;
    [SerializeField] private Sprite headSprite;
    [SerializeField] private float headScale = 0.72f;
    [SerializeField] private float headWorldWidth = 1.15f;
    [SerializeField] private float headTipOffset = 0f;
    [SerializeField] private float headBackwardOffset = 0.12f;
    [SerializeField] private float headTipStretch = 1.22f;
    [SerializeField] private float headBodyOverlap = 0.045f;

    [Header("Smoothness")]
    [SerializeField] private int samplesPerCell = 20;

    [Header("Touch")]
    [SerializeField] private float hitSpacing = 0.06f;
    [SerializeField] private float hitRadius = 0.18f;
    [SerializeField] private float headHitPadding = 0.08f;

    [Header("Escape")]
    [SerializeField] private float moveSpeed = 3.2f;
    [SerializeField] private float extraExitDistance = 3.0f;

    private readonly List<Vector3> centerline = new List<Vector3>();
    private readonly List<float> distances = new List<float>();
    private readonly List<GameObject> hitAreas = new List<GameObject>();

    private LineRenderer line;
    private GameObject headObject;
    [SerializeField] private SpriteRenderer headRenderer;

    private Camera mainCamera;
    private PuzzleBoard board;

    private float originalLength;
    private float totalLength;
    private float headDistance;

    private bool escaping;
    private bool configuredAtRuntime;

    private void Awake()
    {
        mainCamera = Camera.main;
        board = GetComponentInParent<PuzzleBoard>();

        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();
    }

    private void Start()
    {
        if (!configuredAtRuntime &&
            path != null &&
            path.Count >= 2)
        {
            Build();
        }
    }

    public void Configure(
        List<Vector2Int> newPath,
        Sprite newHeadSprite,
        Color newColor,
        float newSpacing)
    {
        path = new List<Vector2Int>(newPath);
        headSprite = newHeadSprite;
        arrowColor = newColor;
        spacing = newSpacing;

        board = GetComponentInParent<PuzzleBoard>();

        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

        if (path.Count >= 2)
        {
            headDirection = DirectionFromDelta(
                path[path.Count - 1] - path[path.Count - 2]
            );
        }

        configuredAtRuntime = true;
        Build();
    }

    public void Configure(
        ArrowData newData,
        Sprite newHeadSprite,
        Color newColor,
        int newGridWidth,
        int newGridHeight,
        float newSpacing)
    {
        arrowData = newData;

        if (arrowData == null)
        {
            Debug.LogError(
                "ArrowPathController: ArrowData is null."
            );
            return;
        }

        gridWidth = newGridWidth;
        gridHeight = newGridHeight;
        path = arrowData.path != null
            ? new List<Vector2Int>(arrowData.path)
            : new List<Vector2Int>();
        headDirection = arrowData.headDirection;
        headSprite = newHeadSprite;
        arrowColor = newColor;
        spacing = newSpacing;

        board = GetComponentInParent<PuzzleBoard>();

        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

        configuredAtRuntime = true;
        Build();
    }

    public void Configure(
        List<Vector2Int> newPath,
        Sprite newHeadSprite,
        Color newColor,
        int newGridWidth,
        int newGridHeight,
        float newSpacing)
    {
        gridWidth = newGridWidth;
        gridHeight = newGridHeight;

        Configure(newPath, newHeadSprite, newColor, newSpacing);
    }

    private void Build()
    {
        if (board != null)
        {
            gridWidth = board.GetWidth();
            gridHeight = board.GetHeight();
            spacing = board.GetSpacing();
        }

        if (!ValidatePath())
            return;

        BuildCenterline();
        BuildVisual();
        BuildHitAreas();

        headDistance = originalLength;
        DrawArrow(headDistance);

        if (board != null && !board.RegisterArrow(this, path))
        {
            Debug.LogError(
                "ArrowPathController: Arrow was not registered because its path is invalid or overlaps another arrow."
            );
            Destroy(gameObject);
        }
    }

    private bool ValidatePath()
    {
        if (path == null || path.Count < 2)
        {
            Debug.LogError("ArrowPathController: Path must contain at least 2 cells.");
            return false;
        }

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];

            if (cell.x < 0 || cell.x >= gridWidth ||
                cell.y < 0 || cell.y >= gridHeight)
            {
                Debug.LogError(
                    "ArrowPathController: Path cell is outside the board: " + cell +
                    " for " + gridWidth + "x" + gridHeight + " board."
                );
                return false;
            }
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2Int a = path[i];
            Vector2Int b = path[i + 1];
            int dx = Mathf.Abs(b.x - a.x);
            int dy = Mathf.Abs(b.y - a.y);

            if (dx + dy != 1)
            {
                Debug.LogError(
                    "ArrowPathController: Invalid path step " + a + " -> " + b +
                    ". Move exactly one cell Up/Down/Left/Right."
                );
                return false;
            }
        }

        return true;
    }

    private Direction DirectionFromDelta(Vector2Int delta)
    {
        if (delta.x > 0) return Direction.Right;
        if (delta.x < 0) return Direction.Left;
        if (delta.y > 0) return Direction.Up;
        return Direction.Down;
    }

    private void BuildCenterline()
    {
        centerline.Clear();
        distances.Clear();
        originalLength = 0f;
        totalLength = 0f;

        AddPoint(CellToWorld(path[0]), 0f);

        int samples = Mathf.Max(4, samplesPerCell);

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = CellToWorld(path[i]);
            Vector3 b = CellToWorld(path[i + 1]);

            for (int s = 1; s <= samples; s++)
            {
                float t = s / (float)samples;
                AddPointIfDifferent(Vector3.Lerp(a, b, t));
            }
        }

        originalLength = totalLength;

        Vector3 exitDirection = DirectionToVector(headDirection);
        float exitDistance = originalLength +
                             Mathf.Max(
                                 spacing * 12f,
                                 Mathf.Max(gridWidth, gridHeight) * spacing
                             ) + extraExitDistance;

        int exitSamples = Mathf.Max(100, samples * 12);
        Vector3 exitStart = centerline[centerline.Count - 1];

        for (int i = 1; i <= exitSamples; i++)
        {
            float t = i / (float)exitSamples;
            AddPointIfDifferent(exitStart + exitDirection * (exitDistance * t));
        }
    }

    private void AddPoint(Vector3 point, float distance)
    {
        centerline.Add(point);
        distances.Add(distance);
    }

    private void AddPointIfDifferent(Vector3 point)
    {
        if (centerline.Count == 0)
        {
            AddPoint(point, 0f);
            return;
        }

        float amount = Vector3.Distance(centerline[centerline.Count - 1], point);
        if (amount <= 0.00001f)
            return;

        totalLength += amount;
        centerline.Add(point);
        distances.Add(totalLength);
    }

    private void BuildVisual()
    {
        lineWidth = spacing * 0.18f;
        headWorldWidth = spacing * 1.15f;
        headTipOffset = spacing * 3.50f;

        line = GetComponent<LineRenderer>();
        if (line == null)
            line = gameObject.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.loop = false;

        float bodyWidth = spacing * 0.36f;
        line.startWidth = bodyWidth;
        line.endWidth = bodyWidth;
        line.numCapVertices = 12;
        line.numCornerVertices = 24;
        line.alignment = LineAlignment.TransformZ;
        line.sortingOrder = 20;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader != null)
            line.material = new Material(shader);

        line.startColor = arrowColor;
        line.endColor = arrowColor;

        if (headRenderer == null)
        {
            if (headObject != null)
                Destroy(headObject);

            headObject = new GameObject("ArrowHead");
            headObject.transform.SetParent(transform, true);
            headRenderer = headObject.AddComponent<SpriteRenderer>();
            headObject.transform.localScale = Vector3.one * headScale;
        }
        else
        {
            headObject = headRenderer.gameObject;
        }

        headRenderer.sprite = headSprite;
        FitHeadToWorldWidth();
        headRenderer.color = arrowColor;
        headRenderer.sortingOrder = 21;
    }

    private void FitHeadToWorldWidth()
    {
        if (headObject == null || headRenderer == null || headRenderer.sprite == null)
            return;

        float spriteWidth = Mathf.Abs(headRenderer.sprite.bounds.size.x);
        if (spriteWidth <= 0.0001f)
            return;

        headScale = headWorldWidth / spriteWidth;
        headObject.transform.localScale = Vector3.one * headScale;
    }

    private void BuildHitAreas()
    {
        ClearHitAreas();

        for (float d = 0f; d <= originalLength; d += hitSpacing)
        {
            GameObject hitObject = new GameObject("ArrowHit");
            hitObject.transform.SetParent(transform, true);
            hitObject.transform.position = SampleCenterline(d);
            hitObject.transform.position = new Vector3(
                hitObject.transform.position.x,
                hitObject.transform.position.y,
                -0.05f
            );

            CircleCollider2D collider = hitObject.AddComponent<CircleCollider2D>();
            collider.radius = hitRadius;
            collider.enabled = false;

            ArrowHitArea hit = hitObject.AddComponent<ArrowHitArea>();
            hit.Initialize(this);
            hitAreas.Add(hitObject);
        }
    }

    private void Update()
    {
        if (escaping)
            return;

        if (Input.GetMouseButtonDown(0))
            TryPick(Input.mousePosition);

        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            TryPick(Input.GetTouch(0).position);
    }

    private void TryPick(Vector2 screenPosition)
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null || centerline.Count < 2)
            return;

        float worldZ = transform.position.z - 0.15f;
        float distanceFromCamera = worldZ - mainCamera.transform.position.z;

        Vector3 worldPoint = mainCamera.ScreenToWorldPoint(
            new Vector3(screenPosition.x, screenPosition.y, distanceFromCamera)
        );

        float bodyTouch = DistanceToVisibleArrow(worldPoint);
        float headTouch = DistanceToHead(worldPoint);
        float allowedBody = Mathf.Max(hitRadius, spacing * 0.10f);

        bool touchedBody = bodyTouch <= allowedBody;
        bool touchedHead = headTouch <= headWorldWidth * 0.5f + headHitPadding;

        if (touchedBody || touchedHead)
        {
            if (board != null)
                board.TryMoveArrow(this);
            else
                BeginEscape();
        }
    }

    private float DistanceToHead(Vector3 point)
    {
        if (headObject == null || headRenderer == null || headRenderer.sprite == null)
            return float.MaxValue;

        Bounds bounds = headRenderer.bounds;
        Vector2 closest = new Vector2(
            Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
            Mathf.Clamp(point.y, bounds.min.y, bounds.max.y)
        );

        return Vector2.Distance(new Vector2(point.x, point.y), closest);
    }

    private float DistanceToVisibleArrow(Vector3 point)
    {
        float best = float.MaxValue;
        if (line == null || line.positionCount < 2)
            return best;

        for (int i = 0; i < line.positionCount - 1; i++)
        {
            Vector3 a = line.GetPosition(i);
            Vector3 b = line.GetPosition(i + 1);
            Vector3 ab = b - a;
            float abSq = ab.sqrMagnitude;

            float t = abSq > 0.000001f
                ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / abSq)
                : 0f;

            Vector3 closest = a + ab * t;
            float distance = Vector2.Distance(
                new Vector2(point.x, point.y),
                new Vector2(closest.x, closest.y)
            );

            if (distance < best)
                best = distance;
        }

        return best;
    }

    public void BeginEscape()
    {
        if (escaping)
            return;

        StartCoroutine(EscapeRoutine());
    }

    private IEnumerator EscapeRoutine()
    {
        escaping = true;

        while (headDistance < totalLength)
        {
            headDistance += moveSpeed * Time.deltaTime;
            DrawArrow(headDistance);
            yield return null;
        }

        DrawArrow(totalLength + 1f);

        if (board != null)
            board.UnregisterArrow(this, path);

        Destroy(gameObject);
    }

    public void PlayBlockedFeedback()
    {
        StartCoroutine(Flash(Color.red, 0.12f));
    }

    public void PlayBlockedTargetFeedback()
    {
        StartCoroutine(Flash(new Color(1f, 0.35f, 0.35f), 0.18f));
    }

    private IEnumerator Flash(Color flashColor, float duration)
    {
        Color normal = arrowColor;

        if (line != null)
        {
            line.startColor = flashColor;
            line.endColor = flashColor;
        }

        if (headRenderer != null)
            headRenderer.color = flashColor;

        yield return new WaitForSeconds(duration);

        if (line != null)
        {
            line.startColor = normal;
            line.endColor = normal;
        }

        if (headRenderer != null)
            headRenderer.color = normal;
    }

    private void DrawArrow(float currentHeadDistance)
    {
        float bodyLength = originalLength;
        float tailDistance = Mathf.Max(0f, currentHeadDistance - bodyLength);
        float pathBodyEnd = Mathf.Max(tailDistance + 0.001f, currentHeadDistance);
        float step = spacing / Mathf.Max(4, samplesPerCell);

        int pointCount = Mathf.Max(
            2,
            Mathf.CeilToInt(Mathf.Max(0.001f, pathBodyEnd - tailDistance) / step)
        );

        List<Vector3> bodyPoints = new List<Vector3>(pointCount + 1);

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float distance = Mathf.Lerp(tailDistance, pathBodyEnd, t);
            bodyPoints.Add(SampleCenterline(distance));
        }

        Quaternion headRotation = Quaternion.Euler(
            0f, 0f, DirectionRotation(headDirection)
        );

        Vector3 tipPosition;
        if (Mathf.Abs(currentHeadDistance - originalLength) < 0.0001f)
        {
            tipPosition = CellToWorld(path[path.Count - 1]) +
                          DirectionToVector(headDirection) *
                          (headTipOffset - headBackwardOffset);
        }
        else
        {
            tipPosition = SampleCenterline(currentHeadDistance) +
                          DirectionToVector(headDirection) *
                          (headTipOffset - headBackwardOffset);
        }

        Vector3 localTipOffset = Vector3.zero;
        Vector3 localRearOffset = Vector3.zero;

        if (headRenderer != null && headRenderer.sprite != null)
        {
            Bounds spriteBounds = headRenderer.sprite.bounds;
            float scaleX = Mathf.Abs(
                headObject != null ? headObject.transform.localScale.x : headScale
            );

            localTipOffset = new Vector3(spriteBounds.max.x * scaleX, 0f, 0f);
            localRearOffset = new Vector3(spriteBounds.min.x * scaleX, 0f, 0f);
        }

        Vector3 worldTipOffset = headRotation * localTipOffset;
        Vector3 worldRearOffset = headRotation * localRearOffset;
        Vector3 headPivot = tipPosition - worldTipOffset;
        Vector3 headRearWorld = headPivot + worldRearOffset;

        float bodyWidth = spacing * 0.36f;
        float overlap = spacing * headBodyOverlap;

        Vector3 bodyConnection = headRearWorld -
                                 DirectionToVector(headDirection) *
                                 (bodyWidth * 0.5f - overlap);

        if (bodyPoints.Count == 0 ||
            Vector3.Distance(bodyPoints[bodyPoints.Count - 1], bodyConnection) > 0.0001f)
        {
            bodyPoints.Add(bodyConnection);
        }

        line.positionCount = bodyPoints.Count;
        for (int i = 0; i < bodyPoints.Count; i++)
            line.SetPosition(i, bodyPoints[i]);

        if (headObject != null)
        {
            if (headRenderer != null && headRenderer.sprite != null)
                FitHeadToWorldWidth();

            headObject.transform.rotation = headRotation;
            headObject.transform.position = headPivot;
        }
    }

    private float GetBodyEndDistance(float currentHeadDistance)
    {
        if (headRenderer == null || headRenderer.sprite == null || headObject == null)
            return Mathf.Max(0f, currentHeadDistance - spacing * 0.75f);

        Bounds b = headRenderer.sprite.bounds;
        float headScaleX = Mathf.Abs(headObject.transform.localScale.x);
        float headLength = Mathf.Abs(b.max.x - b.min.x) * headScaleX;

        return Mathf.Max(0f, currentHeadDistance - headLength);
    }

    private float GetHeadLength()
    {
        if (headRenderer == null || headRenderer.sprite == null)
            return spacing * 0.75f;

        Bounds b = headRenderer.sprite.bounds;
        float worldWidth = Mathf.Abs(b.max.x - b.min.x) *
                           Mathf.Abs(headObject != null ? headObject.transform.localScale.x : headScale);

        return Mathf.Max(spacing * 0.40f, worldWidth);
    }

    private Vector3 SampleCenterline(float distance)
    {
        if (centerline.Count == 0)
            return transform.position;

        if (distance <= 0f)
            return centerline[0];

        if (distance >= totalLength)
            return centerline[centerline.Count - 1];

        int low = 0;
        int high = distances.Count - 1;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (distances[mid] < distance)
                low = mid + 1;
            else
                high = mid - 1;
        }

        int upper = Mathf.Clamp(low, 1, distances.Count - 1);
        int lower = upper - 1;
        float t = Mathf.InverseLerp(distances[lower], distances[upper], distance);

        return Vector3.Lerp(centerline[lower], centerline[upper], t);
    }

    private Vector3 CellToWorld(Vector2Int cell)
    {
        if (board != null)
        {
            return board.CellToWorld(cell) + new Vector3(0f, 0f, -0.15f);
        }

        float offsetX = (gridWidth - 1) * spacing * 0.5f;
        float offsetY = (gridHeight - 1) * spacing * 0.5f;

        return transform.position + new Vector3(
            cell.x * spacing - offsetX,
            cell.y * spacing - offsetY,
            -0.15f
        );
    }

    private Vector3 DirectionToVector(Direction direction)
    {
        switch (direction)
        {
            case Direction.Right: return Vector3.right;
            case Direction.Left: return Vector3.left;
            case Direction.Up: return Vector3.up;
            case Direction.Down: return Vector3.down;
        }

        return Vector3.right;
    }

    private float DirectionRotation(Direction direction)
    {
        switch (direction)
        {
            case Direction.Right: return 0f;
            case Direction.Up: return 90f;
            case Direction.Left: return 180f;
            case Direction.Down: return -90f;
        }

        return 0f;
    }

    private void ClearHitAreas()
    {
        for (int i = 0; i < hitAreas.Count; i++)
        {
            if (hitAreas[i] != null)
                Destroy(hitAreas[i]);
        }

        hitAreas.Clear();
    }

    public ArrowData GetData()
    {
        if (arrowData == null)
        {
            arrowData = new ArrowData();
        }

        arrowData.path =
            path != null
                ? new List<Vector2Int>(path)
                : new List<Vector2Int>();

        arrowData.headDirection =
            headDirection;

        return arrowData;
    }

    public List<Vector2Int> GetPath()
    {
        return path;
    }

    public Direction GetHeadDirection()
    {
        return headDirection;
    }

    public bool IsEscaping()
    {
        return escaping;
    }

    public void SetHeadSprite(Sprite sprite)
    {
        headSprite = sprite;
        if (headRenderer != null)
            headRenderer.sprite = sprite;
    }

    public void SetColor(Color color)
    {
        arrowColor = color;

        if (line != null)
        {
            line.startColor = color;
            line.endColor = color;
        }

        if (headRenderer != null)
            headRenderer.color = color;
    }
}

public class ArrowHitArea : MonoBehaviour
{
    private ArrowPathController owner;

    public void Initialize(ArrowPathController controller)
    {
        owner = controller;
    }

    public ArrowPathController GetOwner()
    {
        return owner;
    }

    private void OnMouseDown()
    {
        // Pointer input is handled by ArrowPathController directly.
    }
}
