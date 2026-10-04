using UnityEngine;

public class GridRenderer : MonoBehaviour
{
    [SerializeField] private int width = 8;
    [SerializeField] private int height = 8;
    [SerializeField] private float spacing = 0.45f;

    [SerializeField] private float dotSize = 0.07f;
    [SerializeField] private Color dotColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    private Sprite dotSprite;

    private void Start()
    {
        if (dotSprite == null)
            CreateDotSprite();

        RebuildGrid();
    }

    public void ConfigureGrid(int newWidth, int newHeight, float newSpacing)
    {
        width = Mathf.Max(2, newWidth);
        height = Mathf.Max(2, newHeight);
        spacing = Mathf.Max(0.01f, newSpacing);

        if (dotSprite == null)
            CreateDotSprite();

        RebuildGrid();
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

        Vector2 center = new Vector2(
            textureSize / 2f,
            textureSize / 2f
        );

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
            new Rect(0, 0, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f
        );
    }

    private void RebuildGrid()
    {
        ClearGrid();

        float offsetX = (width - 1) * spacing * 0.5f;
        float offsetY = (height - 1) * spacing * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                GameObject dot = new GameObject($"Dot_{x}_{y}");
                dot.transform.SetParent(transform, false);

                dot.transform.localPosition = new Vector3(
                    x * spacing - offsetX,
                    y * spacing - offsetY,
                    0f
                );

                dot.transform.localScale = Vector3.one * dotSize;

                SpriteRenderer renderer = dot.AddComponent<SpriteRenderer>();
                renderer.sprite = dotSprite;
                renderer.color = dotColor;
                renderer.sortingOrder = 0;
            }
        }
    }

    private void ClearGrid()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }
}
