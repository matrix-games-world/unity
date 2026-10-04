#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Simple reference-style generator studio.
/// Pure GUI drawing is used intentionally so a failed generation can never
/// leave Unity's GUILayout stack unbalanced.
/// </summary>
public class ArrowPuzzleGeneratorStudio : EditorWindow
{
    private readonly List<ArrowPuzzleReferenceGenerator.GeneratedLevel> generated =
        new List<ArrowPuzzleReferenceGenerator.GeneratedLevel>();

    private int selectedIndex = -1;
    private Vector2 reviewScroll;

    [SerializeField] private int gridWidth = 24;
    [SerializeField] private int gridHeight = 24;
    [SerializeField] private float spacing = 0.5f;
    [SerializeField] private int minArrows = 15;
    [SerializeField] private int maxArrows = 20;
    [SerializeField] private int batchCount = 24;
    [SerializeField] private int baseSeed = 20261004;

    [SerializeField]
    private ArrowPuzzleReferenceGenerator.GenerationDifficulty difficultyMode =
        ArrowPuzzleReferenceGenerator.GenerationDifficulty.Mixed;

    [SerializeField]
    private ArrowPuzzleReferenceGenerator.BoardShape boardShape =
        ArrowPuzzleReferenceGenerator.BoardShape.Rectangle;

    [SerializeField]
    private ArrowPuzzleReferenceGenerator.GenerationStyle generationStyle =
        ArrowPuzzleReferenceGenerator.GenerationStyle.Mixed;

    [SerializeField] private bool fullRectangleCoverage = true;
    [SerializeField] private bool autoAddToManager = true;

    private bool busy;
    private string status = "جاهز. اختار الشكل واضغط إنشاء التصميمات.";

    [MenuItem("Arrow Puzzle/Generator Studio")]
    public static void ShowWindow()
    {
        ArrowPuzzleGeneratorStudio window =
            GetWindow<ArrowPuzzleGeneratorStudio>("Arrow Generator Studio");

        window.minSize = new Vector2(1080f, 700f);
        window.Show();
    }

    private void OnGUI()
    {
        try
        {
            DrawHeader();
            DrawLeftPanel();
            DrawRightPanel();
        }
        catch (ExitGUIException)
        {
            throw;
        }
        catch (Exception ex)
        {
            busy = false;
            status =
                "خطأ في الاستوديو: " +
                ex.GetType().Name +
                " — " +
                ex.Message;

            Debug.LogError(
                "مولد الأسهم: خطأ في واجهة الاستوديو: " + ex
            );

            GUIUtility.ExitGUI();
        }
    }

    private void DrawHeader()
    {
        Rect r = new Rect(0f, 0f, position.width, 38f);
        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));

        GUI.Label(
            new Rect(12f, 9f, 500f, 22f),
            "مولد الأسهم — تصميمات متاهة وأشكال",
            EditorStyles.boldLabel
        );

        Rect button = new Rect(position.width - 170f, 6f, 158f, 26f);
        if (GUI.Button(button, "فتح Level Manager"))
        {
            ArrowLevelManager manager =
                FindFirstObjectByType<ArrowLevelManager>();

            if (manager != null)
            {
                Selection.activeGameObject = manager.gameObject;
                status = "تم تحديد ArrowLevelManager.";
            }
            else
            {
                status = "لم أجد ArrowLevelManager في الـScene الحالية.";
            }
        }
    }

    private void DrawLeftPanel()
    {
        const float left = 10f;
        const float top = 48f;
        const float width = 360f;

        Rect panel = new Rect(left, top, width, position.height - top - 10f);
        EditorGUI.DrawRect(panel, new Color(0.16f, 0.16f, 0.16f));

        GUI.Label(
            new Rect(left + 12f, top + 12f, width - 24f, 22f),
            "1 — الشكل",
            EditorStyles.boldLabel
        );

        float y = top + 40f;
        DrawShapeButtons(left + 12f, ref y, width - 24f);

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 20f),
            "شكل اللوحة:",
            EditorStyles.miniBoldLabel
        );

        boardShape = (ArrowPuzzleReferenceGenerator.BoardShape)
            EditorGUI.EnumPopup(
                new Rect(left + 120f, y - 2f, width - 132f, 22f),
                boardShape
            );
        y += 34f;

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 20f),
            "2 — نمط المتاهة",
            EditorStyles.boldLabel
        );
        y += 24f;

        generationStyle = (ArrowPuzzleReferenceGenerator.GenerationStyle)
            EditorGUI.EnumPopup(
                new Rect(left + 12f, y, width - 24f, 22f),
                generationStyle
            );
        y += 36f;

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 20f),
            "3 — الصعوبة والأسهم",
            EditorStyles.boldLabel
        );
        y += 26f;

        difficultyMode = (ArrowPuzzleReferenceGenerator.GenerationDifficulty)
            EditorGUI.EnumPopup(
                new Rect(left + 12f, y, width - 24f, 22f),
                difficultyMode
            );
        y += 32f;

        GUI.Label(new Rect(left + 12f, y, 150f, 20f), "أقل أسهم");
        minArrows = EditorGUI.IntSlider(
            new Rect(left + 100f, y, width - 112f, 20f),
            minArrows,
            2,
            63
        );
        y += 28f;

        GUI.Label(new Rect(left + 12f, y, 150f, 20f), "أكثر أسهم");
        maxArrows = EditorGUI.IntSlider(
            new Rect(left + 100f, y, width - 112f, 20f),
            maxArrows,
            minArrows,
            63
        );
        y += 36f;

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 20f),
            "4 — حجم اللوحة",
            EditorStyles.boldLabel
        );
        y += 26f;

        GUI.Label(new Rect(left + 12f, y, 50f, 20f), "العرض");
        gridWidth = Mathf.Clamp(
            EditorGUI.IntField(new Rect(left + 62f, y, 85f, 22f), gridWidth),
            4,
            48
        );

        GUI.Label(new Rect(left + 158f, y, 60f, 20f), "الارتفاع");
        gridHeight = Mathf.Clamp(
            EditorGUI.IntField(new Rect(left + 222f, y, 85f, 22f), gridHeight),
            4,
            48
        );
        y += 30f;

        if (GUI.Button(new Rect(left + 12f, y, width - 24f, 28f), "⬛ اجعلها مربعة"))
        {
            int square = Mathf.Clamp(Mathf.Max(gridWidth, gridHeight), 4, 48);
            gridWidth = square;
            gridHeight = square;
            boardShape = ArrowPuzzleReferenceGenerator.BoardShape.Rectangle;
            status = "تم اختيار لوحة مربعة.";
        }
        y += 34f;

        GUI.Label(new Rect(left + 12f, y, 70f, 20f), "المسافة");
        spacing = Mathf.Max(
            0.05f,
            EditorGUI.FloatField(new Rect(left + 82f, y, 90f, 22f), spacing)
        );
        y += 30f;

        GUI.Label(new Rect(left + 12f, y, 100f, 20f), "عدد التصميمات");
        batchCount = EditorGUI.IntSlider(
            new Rect(left + 100f, y, width - 112f, 20f),
            batchCount,
            1,
            200
        );
        y += 30f;

        GUI.Label(new Rect(left + 12f, y, 60f, 20f), "Seed");
        baseSeed = EditorGUI.IntField(
            new Rect(left + 70f, y, width - 82f, 22f),
            baseSeed
        );
        y += 30f;

        autoAddToManager = GUI.Toggle(
            new Rect(left + 12f, y, width - 24f, 24f),
            autoAddToManager,
            "إضافة المستوى تلقائيًا إلى ArrowLevelManager"
        );
        y += 28f;

        bool rectMode =
            boardShape == ArrowPuzzleReferenceGenerator.BoardShape.Rectangle;
        using (new EditorGUI.DisabledScope(!rectMode))
        {
            fullRectangleCoverage = GUI.Toggle(
                new Rect(left + 12f, y, width - 24f, 24f),
                fullRectangleCoverage,
                "المربع/المستطيل: تغطية 100%"
            );
        }
        y += 34f;

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 46f),
            "نصيحتي: للقلب استخدم ❤️ قلب + Mixed.\nللمربع العشوائي استخدم ⬛ مربع + Mixed.",
            EditorStyles.wordWrappedMiniLabel
        );
        y += 52f;

        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUI.Button(
                    new Rect(left + 12f, y, width - 24f, 48f),
                    "إنشاء التصميمات"))
            {
                GenerateBatch();
            }
        }
        y += 58f;

        GUI.Label(
            new Rect(left + 12f, y, width - 24f, 38f),
            status,
            EditorStyles.wordWrappedMiniLabel
        );

        if (generated.Count > 0 &&
            selectedIndex >= 0 &&
            selectedIndex < generated.Count)
        {
            ArrowPuzzleReferenceGenerator.GeneratedLevel level =
                generated[selectedIndex];

            y += 48f;
            GUI.Label(
                new Rect(left + 12f, y, width - 24f, 20f),
                "التصميم المحدد",
                EditorStyles.boldLabel
            );
            y += 24f;

            GUI.Label(new Rect(left + 12f, y, 130f, 18f), "الشكل");
            GUI.Label(new Rect(left + 145f, y, 180f, 18f), GetArabicShape(level.shape)); y += 20f;
            GUI.Label(new Rect(left + 12f, y, 130f, 18f), "الأسهم");
            GUI.Label(new Rect(left + 145f, y, 180f, 18f), level.arrowCount.ToString()); y += 20f;
            GUI.Label(new Rect(left + 12f, y, 130f, 18f), "اللفات");
            GUI.Label(new Rect(left + 145f, y, 180f, 18f), level.totalTurns.ToString()); y += 20f;
            GUI.Label(new Rect(left + 12f, y, 130f, 18f), "العلاقات");
            GUI.Label(new Rect(left + 145f, y, 180f, 18f), level.totalDependencies.ToString()); y += 28f;

            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUI.Button(new Rect(left + 12f, y, width - 24f, 38f), "💾 حفظ وإضافة للمستوى التالي"))
                    SaveSelectedLevel(true);
                y += 42f;
                if (GUI.Button(new Rect(left + 12f, y, width - 24f, 30f), "حفظ فقط"))
                    SaveSelectedLevel(false);
                y += 34f;
                if (GUI.Button(new Rect(left + 12f, y, width - 24f, 30f), "توليد جديد لنفس التصميم"))
                    RegenerateSelected();
            }
        }
    }

    private void DrawShapeButtons(float left, ref float y, float width)
    {
        string[] names = { "❤️\nقلب", "🍃\nورقة", "◆\nمعين", "○\nبيضاوي" };
        ArrowPuzzleReferenceGenerator.BoardShape[] shapes =
        {
            ArrowPuzzleReferenceGenerator.BoardShape.Heart,
            ArrowPuzzleReferenceGenerator.BoardShape.Leaf,
            ArrowPuzzleReferenceGenerator.BoardShape.Diamond,
            ArrowPuzzleReferenceGenerator.BoardShape.Oval
        };

        for (int row = 0; row < 3; row++)
        {
            int count = row == 2 ? 3 : 4;
            for (int col = 0; col < count; col++)
            {
                float x = left + col * (width / 4f);
                float w = width / 4f - 4f;
                Rect r = new Rect(x, y, w, 42f);

                if (row < 2)
                {
                    int idx = row * 4 + col;
                    string label;
                    ArrowPuzzleReferenceGenerator.BoardShape shape;

                    if (row == 0)
                    {
                        label = names[col];
                        shape = shapes[col];
                    }
                    else
                    {
                        string[] row2 = { "☆\nنجمة", "☘\nنفل", "◎\nحلقة", "🎲\nعشوائي" };
                        ArrowPuzzleReferenceGenerator.BoardShape[] row2Shapes =
                        {
                            ArrowPuzzleReferenceGenerator.BoardShape.Star,
                            ArrowPuzzleReferenceGenerator.BoardShape.Clover,
                            ArrowPuzzleReferenceGenerator.BoardShape.Ring,
                            ArrowPuzzleReferenceGenerator.BoardShape.Mixed
                        };
                        label = row2[col];
                        shape = row2Shapes[col];
                    }

                    if (GUI.Button(r, label))
                    {
                        boardShape = shape;
                        if (shape == ArrowPuzzleReferenceGenerator.BoardShape.Heart)
                            ApplyHeartPreset();
                        else
                            status = "تم اختيار: " + GetArabicShape(shape);
                    }
                }
                else
                {
                    string[] row3 = { "⬛\nمربع", "🌫\nشكل حر", "✦\nكل الأشكال" };
                    ArrowPuzzleReferenceGenerator.BoardShape[] row3Shapes =
                    {
                        ArrowPuzzleReferenceGenerator.BoardShape.Rectangle,
                        ArrowPuzzleReferenceGenerator.BoardShape.RandomBlob,
                        ArrowPuzzleReferenceGenerator.BoardShape.Mixed
                    };

                    if (GUI.Button(r, row3[col]))
                    {
                        boardShape = row3Shapes[col];
                        if (boardShape == ArrowPuzzleReferenceGenerator.BoardShape.Rectangle)
                            gridHeight = gridWidth;
                        status = "تم اختيار: " + GetArabicShape(boardShape);
                    }
                }
            }

            y += 46f;
        }
    }

    private void ApplyHeartPreset()
    {
        gridWidth = 24;
        gridHeight = 24;
        spacing = 0.5f;
        minArrows = 15;
        maxArrows = 20;
        batchCount = 24;
        boardShape = ArrowPuzzleReferenceGenerator.BoardShape.Heart;
        generationStyle = ArrowPuzzleReferenceGenerator.GenerationStyle.Mixed;
        difficultyMode = ArrowPuzzleReferenceGenerator.GenerationDifficulty.Mixed;
        fullRectangleCoverage = false;
        baseSeed = unchecked(Mathf.Abs(baseSeed + 7919));
        status = "إعداد القلب جاهز. اضغط إنشاء التصميمات.";
    }

    private void DrawRightPanel()
    {
        float x = 382f;
        float y = 48f;
        float w = position.width - x - 10f;
        float h = position.height - y - 10f;

        GUI.Label(
            new Rect(x, y, w, 24f),
            "التصميمات الناتجة — اضغط على أي تصميم لاختياره",
            EditorStyles.boldLabel
        );

        Rect viewRect = new Rect(x, y + 28f, w, h - 28f);
        float cardW = Mathf.Max(300f, (w - 28f) * 0.5f);
        float cardH = 170f;
        int columns = 2;
        int rows = Mathf.Max(1, Mathf.CeilToInt(generated.Count / 2f));
        float contentH = Mathf.Max(h - 28f, rows * (cardH + 10f) + 20f);

        reviewScroll = GUI.BeginScrollView(
            viewRect,
            reviewScroll,
            new Rect(0f, 0f, w - 18f, contentH)
        );

        try
        {
            if (generated.Count == 0)
            {
                EditorGUI.HelpBox(
                    new Rect(10f, 10f, w - 40f, 60f),
                    "لم يتم إنشاء تصميمات بعد. اختار الشكل من اليسار ثم اضغط إنشاء التصميمات.",
                    MessageType.Info
                );
            }
            else
            {
                for (int i = 0; i < generated.Count; i++)
                {
                    int row = i / columns;
                    int col = i % columns;
                    Rect card = new Rect(
                        10f + col * (cardW + 10f),
                        10f + row * (cardH + 10f),
                        cardW,
                        cardH
                    );

                    DrawCard(i, generated[i], card);
                }
            }
        }
        finally
        {
            GUI.EndScrollView();
        }
    }

    private void DrawCard(
        int index,
        ArrowPuzzleReferenceGenerator.GeneratedLevel level,
        Rect card)
    {
        bool selected = index == selectedIndex;
        EditorGUI.DrawRect(
            card,
            selected
                ? new Color(0.12f, 0.30f, 0.50f)
                : new Color(0.12f, 0.12f, 0.12f)
        );

        Rect preview = new Rect(card.x + 8f, card.y + 8f, 150f, 150f);
        DrawMiniPreview(preview, level);

        float tx = card.x + 168f;
        GUI.Label(new Rect(tx, card.y + 10f, card.width - 178f, 20f), "التصميم #" + (index + 1), EditorStyles.boldLabel);
        GUI.Label(new Rect(tx, card.y + 34f, card.width - 178f, 18f), "الشكل: " + GetArabicShape(level.shape));
        GUI.Label(new Rect(tx, card.y + 54f, card.width - 178f, 18f), "النمط: " + GetArabicStyle(level.style));
        GUI.Label(new Rect(tx, card.y + 74f, card.width - 178f, 18f), "الأسهم: " + level.arrowCount);
        GUI.Label(new Rect(tx, card.y + 94f, card.width - 178f, 18f), "الامتلاء: " + level.shapeFill.ToString("P0"));
        GUI.Label(new Rect(tx, card.y + 114f, card.width - 178f, 18f), "اللفات: " + level.totalTurns);
        GUI.Label(new Rect(tx, card.y + 134f, card.width - 178f, 18f), "العلاقات: " + level.totalDependencies);

        if (GUI.Button(card, GUIContent.none, GUIStyle.none))
        {
            selectedIndex = index;
            status = "تم اختيار التصميم #" + (index + 1) + ".";
        }
    }

    private void DrawMiniPreview(
        Rect rect,
        ArrowPuzzleReferenceGenerator.GeneratedLevel level)
    {
        EditorGUI.DrawRect(rect, Color.white);

        int width = Mathf.Max(1, level.width);
        int height = Mathf.Max(1, level.height);
        float pad = 10f;
        float inner = Mathf.Min(rect.width, rect.height) - pad * 2f;
        float cell = inner / Mathf.Max(1f, Mathf.Max(width - 1, height - 1));

        Vector2 origin = new Vector2(
            rect.center.x - (width - 1) * cell * 0.5f,
            rect.center.y - (height - 1) * cell * 0.5f
        );

        Handles.BeginGUI();
        try
        {
            Handles.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            HashSet<Vector2Int> active =
                new HashSet<Vector2Int>(level.activeCells);

            foreach (Vector2Int p in active)
            {
                Handles.DotHandleCap(
                    0,
                    origin + new Vector2(p.x * cell, p.y * cell),
                    Quaternion.identity,
                    1.0f,
                    EventType.Repaint
                );
            }

            Color[] palette = BuildPalette(level.arrowCount);
            for (int i = 0; i < level.arrows.Count; i++)
                DrawArrowGizmo(level.arrows[i], origin, cell, palette[i]);
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    private void DrawArrowGizmo(
        ArrowPuzzleReferenceGenerator.GeneratedArrow arrow,
        Vector2 origin,
        float cell,
        Color color)
    {
        if (arrow == null || arrow.path == null || arrow.path.Count < 2)
            return;

        Vector3[] points = new Vector3[arrow.path.Count];
        for (int i = 0; i < arrow.path.Count; i++)
        {
            Vector2Int p = arrow.path[i];
            points[i] = origin + new Vector2(p.x * cell, p.y * cell);
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(2.5f, points);

        Vector3 head = points[points.Length - 1];
        Vector3 direction = points[points.Length - 1] - points[points.Length - 2];
        if (direction.sqrMagnitude < 0.00001f)
            return;

        direction.Normalize();
        Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f);
        Vector3 tip = head + direction * cell * 0.16f;
        Vector3 left = head - direction * cell * 0.08f + perpendicular * cell * 0.10f;
        Vector3 right = head - direction * cell * 0.08f - perpendicular * cell * 0.10f;

        Handles.DrawAAConvexPolygon(tip, left, right);
    }

    private void GenerateBatch()
    {
        if (busy)
            return;

        busy = true;
        generated.Clear();
        selectedIndex = -1;
        status = "جاري إنشاء التصميمات...";
        Repaint();

        try
        {
            generated.AddRange(
                ArrowPuzzleReferenceGenerator.GenerateBatch(
                    gridWidth,
                    gridHeight,
                    minArrows,
                    maxArrows,
                    difficultyMode,
                    batchCount,
                    baseSeed,
                    spacing,
                    boardShape,
                    generationStyle,
                    fullRectangleCoverage
                )
            );

            selectedIndex = generated.Count > 0 ? 0 : -1;

            if (generated.Count > 0)
            {
                status = "تم إنشاء " + generated.Count + " تصميم بنجاح.";
                Debug.Log(
                    "مولد الأسهم: تم إنشاء " + generated.Count +
                    " تصميم — الشكل: " + GetArabicShape(boardShape) +
                    " — النمط: " + GetArabicStyle(generationStyle)
                );
            }
            else
            {
                status = "لم يتم قبول أي تصميم. جرّب Seed مختلف أو أسهم أقل.";
                Debug.LogWarning(
                    "مولد الأسهم: لم يتم إنشاء أي تصميم صالح ضمن الميزانية المحددة."
                );
            }
        }
        catch (Exception ex)
        {
            generated.Clear();
            selectedIndex = -1;
            status = "فشل التوليد: " + ex.GetType().Name + " — " + ex.Message;
            Debug.LogError("مولد الأسهم: فشل التوليد: " + ex);
        }
        finally
        {
            busy = false;
            Repaint();
        }
    }

    private void RegenerateSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= generated.Count)
            return;

        ArrowPuzzleReferenceGenerator.GeneratedLevel old = generated[selectedIndex];
        int newSeed = unchecked(old.seed + 104729 + selectedIndex * 7919);

        try
        {
            ArrowPuzzleReferenceGenerator.GeneratedLevel replacement =
                ArrowPuzzleReferenceGenerator.GenerateOne(
                    gridWidth,
                    gridHeight,
                    old.arrowCount,
                    old.difficulty,
                    newSeed,
                    spacing,
                    new System.Random(newSeed),
                    old.shape,
                    generationStyle,
                    fullRectangleCoverage
                );

            if (replacement == null)
            {
                status = "لم ينجح التوليد الجديد. جرّب إنشاء Batch جديد.";
                Debug.LogWarning("مولد الأسهم: لم ينجح إعادة توليد التصميم.");
                return;
            }

            generated[selectedIndex] = replacement;
            status = "تم توليد التصميم المحدد من جديد.";
        }
        catch (Exception ex)
        {
            status = "فشل إعادة التوليد: " + ex.GetType().Name + " — " + ex.Message;
            Debug.LogError("مولد الأسهم: فشل إعادة التوليد: " + ex);
        }
    }

    private void SaveSelectedLevel(bool addToManager)
    {
        if (selectedIndex < 0 || selectedIndex >= generated.Count)
        {
            status = "اختر تصميمًا أولًا.";
            return;
        }

        try
        {
            ArrowPuzzleReferenceGenerator.GeneratedLevel source = generated[selectedIndex];
            string folder = "Assets/Levels/Generated";

            EnsureFolder("Assets", "Levels");
            EnsureFolder("Assets/Levels", "Generated");

            int nextNumber = FindNextLevelNumber(folder);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/Level_" + nextNumber.ToString("000") + ".asset"
            );

            LevelData asset = ScriptableObject.CreateInstance<LevelData>();
            asset.width = source.width;
            asset.height = source.height;
            asset.spacing = source.spacing;
            asset.lives = 3;
            asset.arrowCount = source.arrowCount;
            asset.seed = source.seed;
            asset.fillEntireBoard =
                source.shape == ArrowPuzzleReferenceGenerator.BoardShape.Rectangle &&
                fullRectangleCoverage;
            asset.requireSolvable = true;
            asset.enforceDifficulty = true;
            asset.targetCoverage = source.shapeFill;
            asset.useGeneratedCellMask =
                source.shape != ArrowPuzzleReferenceGenerator.BoardShape.Rectangle;
            asset.visibleCells = new List<Vector2Int>(source.activeCells);
            asset.generatedShape = source.shape.ToString();
            asset.generatedStyle = source.style.ToString();
            asset.generatedShapeFill = source.shapeFill;

            LevelData current =
                FindFirstObjectByType<ArrowLevelManager>()?.GetCurrentLevel();

            if (current != null)
            {
                asset.defaultArrowPrefab = current.defaultArrowPrefab;
                asset.defaultHeadSprite = current.defaultHeadSprite;
                asset.defaultArrowColor = current.defaultArrowColor;
            }

            for (int i = 0; i < source.arrows.Count; i++)
            {
                ArrowPuzzleReferenceGenerator.GeneratedArrow arrow = source.arrows[i];
                asset.arrows.Add(
                    new ArrowData
                    {
                        path = new List<Vector2Int>(arrow.path),
                        headDirection = arrow.headDirection,
                        headSprite = asset.defaultHeadSprite,
                        arrowColor = asset.defaultArrowColor
                    }
                );
            }

            AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (addToManager && autoAddToManager)
                AddLevelToActiveManager(asset);

            Selection.activeObject = asset;
            status = "تم الحفظ: " + assetPath;
            Debug.Log(
                "مولد الأسهم: تم حفظ المستوى " + assetPath +
                " — الشكل: " + GetArabicShape(source.shape)
            );
        }
        catch (Exception ex)
        {
            status = "فشل الحفظ: " + ex.GetType().Name + " — " + ex.Message;
            Debug.LogError("مولد الأسهم: فشل حفظ المستوى: " + ex);
        }
    }

    private void AddLevelToActiveManager(LevelData asset)
    {
        ArrowLevelManager manager = FindFirstObjectByType<ArrowLevelManager>();
        if (manager == null)
        {
            status = "تم الحفظ، لكن لا يوجد ArrowLevelManager في الـScene.";
            return;
        }

        SerializedObject serializedManager = new SerializedObject(manager);
        SerializedProperty levelsProperty = serializedManager.FindProperty("levels");

        if (levelsProperty == null || !levelsProperty.isArray)
        {
            status = "تم الحفظ، لكن تعذر تعديل قائمة levels.";
            return;
        }

        int newIndex = levelsProperty.arraySize;
        levelsProperty.InsertArrayElementAtIndex(newIndex);
        levelsProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = asset;
        serializedManager.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(manager.gameObject.scene);
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static int FindNextLevelNumber(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:LevelData", new[] { folder });
        int highest = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!file.StartsWith("Level_"))
                continue;

            string number = file.Substring("Level_".Length);
            if (int.TryParse(number, out int value))
                highest = Mathf.Max(highest, value);
        }

        return highest + 1;
    }

    private static Color[] BuildPalette(int count)
    {
        Color[] colors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            float h = count <= 1 ? 0f : (i / (float)count + 0.07f) % 1f;
            colors[i] = Color.HSVToRGB(h, 0.70f, 0.90f);
        }
        return colors;
    }

    private static string GetArabicShape(ArrowPuzzleReferenceGenerator.BoardShape shape)
    {
        switch (shape)
        {
            case ArrowPuzzleReferenceGenerator.BoardShape.Heart: return "قلب";
            case ArrowPuzzleReferenceGenerator.BoardShape.Leaf: return "ورقة";
            case ArrowPuzzleReferenceGenerator.BoardShape.Diamond: return "معين";
            case ArrowPuzzleReferenceGenerator.BoardShape.Oval: return "بيضاوي";
            case ArrowPuzzleReferenceGenerator.BoardShape.Ring: return "حلقة";
            case ArrowPuzzleReferenceGenerator.BoardShape.Star: return "نجمة";
            case ArrowPuzzleReferenceGenerator.BoardShape.Clover: return "نفل";
            case ArrowPuzzleReferenceGenerator.BoardShape.RandomBlob: return "شكل حر";
            case ArrowPuzzleReferenceGenerator.BoardShape.Mixed: return "متنوع";
            default: return "مربع / مستطيل";
        }
    }

    private static string GetArabicStyle(ArrowPuzzleReferenceGenerator.GenerationStyle style)
    {
        switch (style)
        {
            case ArrowPuzzleReferenceGenerator.GenerationStyle.ReferenceMaze: return "متاهة مرجعية";
            case ArrowPuzzleReferenceGenerator.GenerationStyle.DenseWeave: return "شبكة كثيفة";
            case ArrowPuzzleReferenceGenerator.GenerationStyle.Organic: return "عضوي";
            default: return "متنوع";
        }
    }
}
#endif
