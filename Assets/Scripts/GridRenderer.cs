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
        CreateDotSprite();
        CreateGrid();
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

    private void CreateGrid()
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                GameObject dot = new GameObject(
                    $"Dot_{x}_{y}"
                );

                dot.transform.SetParent(transform);

                float offsetX = (width - 1) * spacing * 0.5f;
                float offsetY = (height - 1) * spacing * 0.5f;

                dot.transform.localPosition =
                    new Vector3(
                        x * spacing - offsetX,
                        y * spacing - offsetY,
                        0f
                    );

                dot.transform.localScale =
                    Vector3.one * dotSize;

                SpriteRenderer renderer =
                    dot.AddComponent<SpriteRenderer>();

                renderer.sprite = dotSprite;
                renderer.color = dotColor;

                renderer.sortingOrder = 0;
            }
        }
    }
}