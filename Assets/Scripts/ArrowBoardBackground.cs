using UnityEngine;

[DefaultExecutionOrder(-1500)]
public class ArrowBoardBackground : MonoBehaviour
{
    [SerializeField] private PuzzleBoard board;
    [SerializeField] private Color backgroundColor = new Color(0.98f, 0.98f, 0.98f, 1f);
    [SerializeField] private float paddingCells = 0.65f;
    [SerializeField] private float cornerRadiusPixels = 22f;
    [SerializeField] private int textureSize = 128;
    [SerializeField] private int borderPixels = 24;
    [SerializeField] private int sortingOrder = -20;

    private SpriteRenderer backgroundRenderer;
    private Sprite backgroundSprite;

    private void Awake()
    {
        if (board == null)
            board = GetComponent<PuzzleBoard>();
        if (board == null)
            board = FindFirstObjectByType<PuzzleBoard>();

        CreateBackground();
        FitToBoard();
    }

    private void LateUpdate()
    {
        FitToBoard();
    }

    private void CreateBackground()
    {
        GameObject go = new GameObject("ArrowBoardBackground");
        go.transform.SetParent(transform, false);

        backgroundRenderer = go.AddComponent<SpriteRenderer>();
        backgroundRenderer.sortingOrder = sortingOrder;
        backgroundRenderer.drawMode = SpriteDrawMode.Sliced;
        backgroundRenderer.color = backgroundColor;

        Texture2D tex = CreateRoundedTexture();
        int border = Mathf.Clamp(borderPixels, 1, tex.width / 3);
        backgroundSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );
        backgroundRenderer.sprite = backgroundSprite;
    }

    private Texture2D CreateRoundedTexture()
    {
        int size = Mathf.Max(64, textureSize);
        float radius = Mathf.Clamp(cornerRadiusPixels, 0f, size * 0.45f);
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = x;
                float right = size - 1 - x;
                float bottom = y;
                float top = size - 1 - y;

                bool inside;
                if (left >= radius || right >= radius || bottom >= radius || top >= radius)
                {
                    inside = true;
                }
                else
                {
                    float dx = radius - left;
                    float dy = radius - bottom;
                    inside = dx * dx + dy * dy <= radius * radius;
                }

                pixels[y * size + x] = inside ? Color.white : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private void FitToBoard()
    {
        if (board == null || backgroundRenderer == null)
            return;

        float spacing = board.GetSpacing();
        float width = Mathf.Max(1, board.GetWidth() - 1) * spacing;
        float height = Mathf.Max(1, board.GetHeight() - 1) * spacing;
        float pad = Mathf.Max(0f, paddingCells) * spacing;

        backgroundRenderer.transform.localPosition = Vector3.zero;
        backgroundRenderer.size = new Vector2(width + pad * 2f, height + pad * 2f);
    }
}
