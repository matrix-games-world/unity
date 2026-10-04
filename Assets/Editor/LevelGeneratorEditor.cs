using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelData))]
public class LevelGeneratorEditor : Editor
{
    private sealed class GeneratedArrow
    {
        public List<Vector2Int> path = new List<Vector2Int>();
    }

    private sealed class SolverResult
    {
        public bool solvable;
        public readonly List<int> order = new List<int>();
        public int initialSafe;
        public int maximumSafe;
        public int totalDependencies;
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private const int MaxGenerationAttempts = 6000;
    private const int CandidateAttempts = 400;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelData level = (LevelData)target;

        GUILayout.Space(12);
        EditorGUILayout.LabelField(
            "Smart Arrow Puzzle Generator",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Generates separated bent arrows, builds real blocker relationships, and rejects deadlocked boards before saving.",
            MessageType.Info
        );

        if (GUILayout.Button("GENERATE LEVEL", GUILayout.Height(42)))
            Generate(level);

        if (GUILayout.Button("RANDOMIZE SEED"))
        {
            Undo.RecordObject(level, "Randomize Level Seed");
            level.seed = Random.Range(1, int.MaxValue);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
        }

        if (GUILayout.Button("CLEAR GENERATED ARROWS"))
        {
            Undo.RecordObject(level, "Clear Generated Arrows");
            level.arrows.Clear();
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
        }
    }

    private static void Generate(LevelData level)
    {
        if (level == null)
            return;

        int width = Mathf.Max(4, level.width);
        int height = Mathf.Max(4, level.height);

        int maximumArrowCount =
            Mathf.Min(
                63,
                Mathf.Max(1, (width * height) / 2)
            );

        int arrowCount = Mathf.Clamp(
            level.arrowCount,
            2,
            maximumArrowCount
        );

        int seed = level.seed;

        if (seed == 0)
        {
            seed = Random.Range(1, int.MaxValue);
            level.seed = seed;
        }

        System.Random rng = new System.Random(seed);

        GetDifficultyProfile(
            level.difficulty,
            out int minLength,
            out int maxLength,
            out int desiredMinTurns,
            out int desiredMaxSafe,
            out int desiredInitialMin,
            out int desiredInitialMax,
            out float desiredCoverage,
            out float desiredBlockedRatio
        );

        float targetCoverage =
            level.fillEntireBoard
                ? Mathf.Clamp(
                    Mathf.Max(desiredCoverage, level.targetCoverage),
                    0.45f,
                    0.92f
                )
                : Mathf.Clamp(
                    level.targetCoverage,
                    0.25f,
                    0.80f
                );

        int maxGenerationAttempts =
            Mathf.Clamp(level.shapeIterations, 100, MaxGenerationAttempts);

        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            List<GeneratedArrow> candidate =
                GenerateCandidateLevel(
                    width,
                    height,
                    arrowCount,
                    minLength,
                    maxLength,
                    desiredMinTurns,
                    level.minimumSeparation,
                    level.headClearanceCells,
                    targetCoverage,
                    rng
                );

            if (candidate == null)
                continue;

            SolverResult solved =
                SolveLevel(
                    candidate,
                    width,
                    height
                );

            if (level.requireSolvable && !solved.solvable)
                continue;

            if (level.enforceDifficulty &&
                !MatchesDifficulty(
                    solved,
                    candidate.Count,
                    level.difficulty,
                    desiredInitialMin,
                    desiredInitialMax,
                    desiredMaxSafe,
                    desiredBlockedRatio,
                    candidate
                ))
            {
                continue;
            }

            SaveLevel(
                level,
                candidate
            );

            Debug.Log(
                "LevelGenerator: VALID level created. " +
                "Arrows=" + candidate.Count +
                ", Coverage=" +
                GetCoverage(candidate, width * height).ToString("P0") +
                ", InitialSafe=" + solved.initialSafe +
                ", MaxSafe=" + solved.maximumSafe +
                ", Difficulty=" + level.difficulty +
                ", Seed=" + seed
            );

            Selection.activeObject = level;
            return;
        }

        Debug.LogError(
            "LevelGenerator: Could not find a valid level. " +
            "Try fewer arrows, a lower separation, or a different seed."
        );
    }

    private static List<GeneratedArrow> GenerateCandidateLevel(
        int width,
        int height,
        int arrowCount,
        int minLength,
        int maxLength,
        int minTurns,
        int minimumSeparation,
        int headClearance,
        float targetCoverage,
        System.Random rng)
    {
        List<GeneratedArrow> result =
            new List<GeneratedArrow>();

        HashSet<Vector2Int> occupied =
            new HashSet<Vector2Int>();

        int targetCells = Mathf.Clamp(
            Mathf.RoundToInt(width * height * targetCoverage),
            arrowCount * minLength,
            arrowCount * maxLength
        );

        int currentCells = 0;

        for (int index = 0; index < arrowCount; index++)
        {
            int remainingArrows = arrowCount - index;
            int remainingTarget =
                targetCells - currentCells;

            int dynamicMin = minLength;
            int dynamicMax = maxLength;

            if (remainingArrows > 0)
            {
                dynamicMax = Mathf.Min(
                    dynamicMax,
                    Mathf.Max(
                        dynamicMin,
                        remainingTarget -
                        (remainingArrows - 1) * dynamicMin
                    )
                );
            }

            GeneratedArrow arrow =
                TryCreateSeparatedArrow(
                    width,
                    height,
                    occupied,
                    dynamicMin,
                    dynamicMax,
                    minTurns,
                    minimumSeparation,
                    rng
                );

            if (arrow == null)
                return null;

            if (!HeadIsClearFromOtherHeads(
                    arrow,
                    result,
                    headClearance))
            {
                return null;
            }

            result.Add(arrow);

            for (int i = 0; i < arrow.path.Count; i++)
                occupied.Add(arrow.path[i]);

            currentCells += arrow.path.Count;
        }

        if (currentCells < arrowCount * minLength)
            return null;

        if (currentCells < targetCells * 0.80f)
            return null;

        return result;
    }

    private static GeneratedArrow TryCreateSeparatedArrow(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        int minLength,
        int maxLength,
        int minTurns,
        int minimumSeparation,
        System.Random rng)
    {
        if (maxLength < minLength)
            return null;

        for (int attempt = 0; attempt < CandidateAttempts; attempt++)
        {
            Vector2Int start =
                new Vector2Int(
                    rng.Next(width),
                    rng.Next(height)
                );

            if (!CanPlaceAgainstOtherArrows(
                    start,
                    occupied,
                    minimumSeparation))
            {
                continue;
            }

            int desiredLength =
                rng.Next(minLength, maxLength + 1);

            List<Vector2Int> path =
                new List<Vector2Int> { start };

            HashSet<Vector2Int> local =
                new HashSet<Vector2Int> { start };

            Vector2Int previousDirection =
                Vector2Int.zero;

            int turns = 0;

            for (int stepIndex = 1;
                 stepIndex < desiredLength;
                 stepIndex++)
            {
                List<Vector2Int> options =
                    GetOptions(
                        path[path.Count - 1],
                        width,
                        height,
                        occupied,
                        local,
                        previousDirection,
                        minimumSeparation,
                        rng
                    );

                if (options.Count == 0)
                    break;

                Vector2Int chosen =
                    options[0];

                Vector2Int step =
                    chosen - path[path.Count - 1];

                if (previousDirection != Vector2Int.zero &&
                    step != previousDirection)
                {
                    turns++;
                }

                path.Add(chosen);
                local.Add(chosen);
                previousDirection = step;
            }

            if (path.Count < minLength)
                continue;

            if (turns < minTurns)
                continue;

            // Avoid ending a path with a head that points immediately outside
            // only because the last step was forced against the boundary.
            if (path.Count >= 2)
            {
                Vector2Int head = path[path.Count - 1];
                Vector2Int direction =
                    head - path[path.Count - 2];

                if (head.x == 0 && direction == Vector2Int.left)
                    continue;

                if (head.x == width - 1 && direction == Vector2Int.right)
                    continue;

                if (head.y == 0 && direction == Vector2Int.down)
                    continue;

                if (head.y == height - 1 && direction == Vector2Int.up)
                    continue;
            }

            return new GeneratedArrow
            {
                path = path
            };
        }

        return null;
    }

    private static List<Vector2Int> GetOptions(
        Vector2Int current,
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        HashSet<Vector2Int> local,
        Vector2Int previousDirection,
        int minimumSeparation,
        System.Random rng)
    {
        List<Vector2Int> options =
            new List<Vector2Int>();

        List<Vector2Int> shuffled =
            new List<Vector2Int>(Directions);

        Shuffle(shuffled, rng);

        // Bias toward a straight continuation, but do not force straight lines.
        if (previousDirection != Vector2Int.zero &&
            rng.NextDouble() < 0.45)
        {
            shuffled.Remove(previousDirection);
            shuffled.Insert(0, previousDirection);
        }

        for (int i = 0; i < shuffled.Count; i++)
        {
            Vector2Int next =
                current + shuffled[i];

            if (!Inside(next, width, height))
                continue;

            if (local.Contains(next))
                continue;

            if (!CanPlaceAgainstOtherArrows(
                    next,
                    occupied,
                    minimumSeparation))
            {
                continue;
            }

            options.Add(next);
        }

        return options;
    }

    private static bool CanPlaceAgainstOtherArrows(
        Vector2Int cell,
        HashSet<Vector2Int> occupied,
        int minimumSeparation)
    {
        if (occupied.Contains(cell))
            return false;

        if (minimumSeparation <= 0)
            return true;

        for (int dx = -minimumSeparation;
             dx <= minimumSeparation;
             dx++)
        {
            for (int dy = -minimumSeparation;
                 dy <= minimumSeparation;
                 dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                if (occupied.Contains(
                        cell + new Vector2Int(dx, dy)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HeadIsClearFromOtherHeads(
        GeneratedArrow arrow,
        List<GeneratedArrow> existing,
        int clearance)
    {
        if (clearance <= 0)
            return true;

        Vector2Int head =
            arrow.path[arrow.path.Count - 1];

        for (int i = 0; i < existing.Count; i++)
        {
            Vector2Int otherHead =
                existing[i].path[
                    existing[i].path.Count - 1
                ];

            int dx = Mathf.Abs(head.x - otherHead.x);
            int dy = Mathf.Abs(head.y - otherHead.y);

            if (Mathf.Max(dx, dy) < clearance)
                return false;
        }

        return true;
    }

    private static SolverResult SolveLevel(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        SolverResult result =
            new SolverResult();

        int count = arrows.Count;

        if (count <= 0 || count > 63)
            return result;

        ulong[] blockers =
            BuildBlockMasks(
                arrows,
                width,
                height
            );

        ulong fullMask =
            count == 64
                ? ulong.MaxValue
                : ((1UL << count) - 1UL);

        HashSet<ulong> visited =
            new HashSet<ulong>();

        List<int> path =
            new List<int>();

        int initialSafe =
            CountSafe(blockers, fullMask);

        result.initialSafe = initialSafe;

        int maxSafe = initialSafe;

        bool found =
            SearchSolution(
                blockers,
                fullMask,
                visited,
                path,
                ref maxSafe
            );

        result.solvable = found;
        result.maximumSafe = maxSafe;

        if (found)
        {
            result.order.AddRange(path);

            int dependencyTotal = 0;

            for (int i = 0; i < blockers.Length; i++)
            {
                dependencyTotal +=
                    CountBits(blockers[i]);
            }

            result.totalDependencies = dependencyTotal;
        }

        return result;
    }

    private static ulong[] BuildBlockMasks(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        ulong[] masks =
            new ulong[arrows.Count];

        Dictionary<Vector2Int, ulong> cellOwners =
            new Dictionary<Vector2Int, ulong>();

        for (int i = 0; i < arrows.Count; i++)
        {
            ulong bit = 1UL << i;

            for (int p = 0; p < arrows[i].path.Count; p++)
                cellOwners[arrows[i].path[p]] = bit;
        }

        for (int i = 0; i < arrows.Count; i++)
        {
            List<Vector2Int> path = arrows[i].path;
            Vector2Int head = path[path.Count - 1];
            Vector2Int direction =
                head - path[path.Count - 2];

            Vector2Int check = head + direction;
            ulong mask = 0UL;

            while (Inside(check, width, height))
            {
                if (cellOwners.TryGetValue(
                        check,
                        out ulong ownerBit))
                {
                    mask |= ownerBit;
                }

                check += direction;
            }

            mask &= ~(1UL << i);
            masks[i] = mask;
        }

        return masks;
    }

    private static bool SearchSolution(
        ulong[] blockers,
        ulong remaining,
        HashSet<ulong> visited,
        List<int> path,
        ref int maximumSafe)
    {
        if (remaining == 0UL)
            return true;

        if (!visited.Add(remaining))
            return false;

        int safeCount =
            CountSafe(blockers, remaining);

        maximumSafe = Mathf.Max(
            maximumSafe,
            safeCount
        );

        List<int> safe =
            new List<int>();

        for (int i = 0; i < blockers.Length; i++)
        {
            ulong bit = 1UL << i;

            if ((remaining & bit) == 0UL)
                continue;

            if ((blockers[i] & remaining) == 0UL)
                safe.Add(i);
        }

        // Prefer the move that unlocks the largest number of other arrows.
        safe.Sort(
            (a, b) =>
                CountInteractions(blockers[b], remaining) -
                CountInteractions(blockers[a], remaining)
        );

        for (int i = 0; i < safe.Count; i++)
        {
            int arrow = safe[i];
            ulong next =
                remaining & ~(1UL << arrow);

            path.Add(arrow);

            if (SearchSolution(
                    blockers,
                    next,
                    visited,
                    path,
                    ref maximumSafe))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static int CountSafe(
        ulong[] blockers,
        ulong remaining)
    {
        int count = 0;

        for (int i = 0; i < blockers.Length; i++)
        {
            ulong bit = 1UL << i;

            if ((remaining & bit) == 0UL)
                continue;

            if ((blockers[i] & remaining) == 0UL)
                count++;
        }

        return count;
    }

    private static int CountInteractions(
        ulong mask,
        ulong remaining)
    {
        return CountBits(mask & remaining);
    }

    private static int CountBits(ulong value)
    {
        int count = 0;

        while (value != 0UL)
        {
            value &= value - 1UL;
            count++;
        }

        return count;
    }

    private static bool MatchesDifficulty(
        SolverResult solved,
        int count,
        ArrowLevelDifficulty difficulty,
        int desiredInitialMin,
        int desiredInitialMax,
        int desiredMaxSafe,
        float desiredBlockedRatio,
        List<GeneratedArrow> arrows)
    {
        if (!solved.solvable)
            return false;

        if (solved.initialSafe < desiredInitialMin ||
            solved.initialSafe > desiredInitialMax)
        {
            return false;
        }

        if (solved.maximumSafe > desiredMaxSafe)
            return false;

        int blocked = 0;
        int total = arrows.Count;

        ulong[] masks =
            BuildBlockMasks(
                arrows,
                100000,
                100000
            );

        for (int i = 0; i < masks.Length; i++)
        {
            if (masks[i] != 0UL)
                blocked++;
        }

        float ratio =
            total > 0
                ? blocked / (float)total
                : 0f;

        return ratio >= desiredBlockedRatio;
    }

    private static void SaveLevel(
        LevelData level,
        List<GeneratedArrow> generated)
    {
        Undo.RecordObject(
            level,
            "Generate Smart Arrow Puzzle"
        );

        level.arrows.Clear();

        for (int i = 0; i < generated.Count; i++)
        {
            List<Vector2Int> path =
                new List<Vector2Int>(generated[i].path);

            Vector2Int delta =
                path[path.Count - 1] - path[path.Count - 2];

            level.arrows.Add(
                new ArrowData
                {
                    path = path,
                    headDirection =
                        DirectionFromDelta(delta),
                    headSprite =
                        level.defaultHeadSprite,
                    arrowColor =
                        level.defaultArrowColor
                }
            );
        }

        EditorUtility.SetDirty(level);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static ArrowPathController.Direction DirectionFromDelta(
        Vector2Int delta)
    {
        if (delta.x > 0)
            return ArrowPathController.Direction.Right;

        if (delta.x < 0)
            return ArrowPathController.Direction.Left;

        if (delta.y > 0)
            return ArrowPathController.Direction.Up;

        return ArrowPathController.Direction.Down;
    }

    private static void GetDifficultyProfile(
        ArrowLevelDifficulty difficulty,
        out int minLength,
        out int maxLength,
        out int minTurns,
        out int maxSafe,
        out int initialMin,
        out int initialMax,
        out float coverage,
        out float blockedRatio)
    {
        switch (difficulty)
        {
            case ArrowLevelDifficulty.Easy:
                minLength = 2;
                maxLength = 4;
                minTurns = 0;
                maxSafe = 5;
                initialMin = 2;
                initialMax = 5;
                coverage = 0.48f;
                blockedRatio = 0.45f;
                break;

            case ArrowLevelDifficulty.Hard:
                minLength = 2;
                maxLength = 5;
                minTurns = 1;
                maxSafe = 3;
                initialMin = 1;
                initialMax = 2;
                coverage = 0.68f;
                blockedRatio = 0.70f;
                break;

            case ArrowLevelDifficulty.Expert:
                minLength = 2;
                maxLength = 6;
                minTurns = 1;
                maxSafe = 2;
                initialMin = 1;
                initialMax = 2;
                coverage = 0.74f;
                blockedRatio = 0.80f;
                break;

            default:
                minLength = 2;
                maxLength = 5;
                minTurns = 1;
                maxSafe = 4;
                initialMin = 1;
                initialMax = 3;
                coverage = 0.58f;
                blockedRatio = 0.60f;
                break;
        }
    }

    private static float GetCoverage(
        List<GeneratedArrow> arrows,
        int totalCells)
    {
        int count = 0;

        for (int i = 0; i < arrows.Count; i++)
            count += arrows[i].path.Count;

        return totalCells > 0
            ? count / (float)totalCells
            : 0f;
    }

    private static bool Inside(
        Vector2Int cell,
        int width,
        int height)
    {
        return cell.x >= 0 &&
               cell.x < width &&
               cell.y >= 0 &&
               cell.y < height;
    }

    private static void Shuffle<T>(
        List<T> list,
        System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
