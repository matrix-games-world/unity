using System.Collections.Generic;
using UnityEngine;

public class GridRenderer : MonoBehaviour
{
    [SerializeField] private int width = 8;
    [SerializeField] private int height = 8;
    [SerializeField] private float spacing = 0.45f;

    [SerializeField] private float dotSize = 0.07f;
    [SerializeField] private Color dotColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Header("Shape Mask")]
    [SerializeField] private bool hideUnusedDots = true;

    private Sprite dotSprite;
    private int renderedLevelId = -1;
    private int renderedWidth = -1;
    private int renderedHeight = -1;
    private float renderedSpacing = -1f;

    private void Start()
    {
        EnsureDotSprite();
        TrySyncToCurrentLevel(true);
    }

    private void LateUpdate()
    {
        TrySyncToCurrentLevel(false);
    }

    public void ConfigureGrid(int newWidth, int newHeight, float newSpacing)
    {
        width = Mathf.Max(2, newWidth);
        height = Mathf.Max(2, newHeight);
        spacing = Mathf.Max(0.01f, newSpacing);

        EnsureDotSprite();
        RebuildGrid();
        renderedLevelId = -1;
        ApplyAllDotsVisible();
    }

    public void ConfigureGrid(
        int newWidth,
        int newHeight,
        float newSpacing,
        List<Vector2Int> visibleCells,
        bool useMask)
    {
        width = Mathf.Max(2, newWidth);
        height = Mathf.Max(2, newHeight);
        spacing = Mathf.Max(0.01f, newSpacing);
        hideUnusedDots = useMask;

        EnsureDotSprite();
        RebuildGrid();
        ApplyVisibleCells(visibleCells, useMask);
    }

    public void ApplyVisibleCells(List<Vector2Int> visibleCells, bool useMask)
    {
        hideUnusedDots = useMask;

        HashSet<Vector2Int> visible = new HashSet<Vector2Int>();
        if (visibleCells != null)
        {
            for (int i = 0; i < visibleCells.Count; i++)
                visible.Add(visibleCells[i]);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Transform child = transform.Find("Dot_" + x + "_" + y);
                if (child == null)
                    continue;

                bool active = !useMask || visible.Contains(new Vector2Int(x, y));
                child.gameObject.SetActive(active);
            }
        }
    }

    private void TrySyncToCurrentLevel(bool force)
    {
        ArrowLevelManager manager =
            FindFirstObjectByType<ArrowLevelManager>();

        LevelData level =
            manager != null
                ? manager.GetCurrentLevel()
                : null;

        if (level == null)
            return;

        bool dimensionsChanged =
            renderedWidth != level.width ||
            renderedHeight != level.height ||
            Mathf.Abs(renderedSpacing - level.spacing) > 0.0001f;

        int levelId = level.GetInstanceID();

        if (!force && !dimensionsChanged && renderedLevelId == levelId)
            return;

        width = Mathf.Max(2, level.width);
        height = Mathf.Max(2, level.height);
        spacing = Mathf.Max(0.01f, level.spacing);

        EnsureDotSprite();
        RebuildGrid();

        bool useMask = hideUnusedDots && level.useGeneratedCellMask;
        ApplyVisibleCells(level.visibleCells, useMask);

        renderedLevelId = levelId;
        renderedWidth = width;
        renderedHeight = height;
        renderedSpacing = spacing;
    }

    private void EnsureDotSprite()
    {
        if (dotSprite != null)
            return;

        CreateDotSprite();
    }

    private void CreateDotSprite()
    {
        const int textureSize = 32;

        Texture2D texture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false
        );

        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[textureSize * textureSize];
        Vector2 center = new Vector2(textureSize / 2f, textureSize / 2f);
        float radius = textureSize * 0.42f;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    center
                );

                pixels[y * textureSize + x] =
                    distance <= radius
                        ? Color.white
                        : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        dotSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f
        );
    }

    private void RebuildGrid()
    {
        ClearGridDots();

        float offsetX = (width - 1) * spacing * 0.5f;
        float offsetY = (height - 1) * spacing * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                GameObject dot = new GameObject(
                    "Dot_" + x + "_" + y
                );

                dot.transform.SetParent(transform, false);
                dot.transform.localPosition = new Vector3(
                    x * spacing - offsetX,
                    y * spacing - offsetY,
                    0f
                );
                dot.transform.localScale = Vector3.one * dotSize;

                SpriteRenderer renderer =
                    dot.AddComponent<SpriteRenderer>();

                renderer.sprite = dotSprite;
                renderer.color = dotColor;
                renderer.sortingOrder = 0;
            }
        }
    }

    private void ApplyAllDotsVisible()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name.StartsWith("Dot_"))
                child.gameObject.SetActive(true);
        }
    }

    private void ClearGridDots()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith("Dot_"))
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}
