#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reference-style procedural generator.
///
/// Design goals:
/// - The visual language is closer to the reference puzzle: many independent,
///   irregular bent arrows instead of a row/column comb.
/// - Rectangle mode can still use 100% board coverage.
/// - Shape modes constrain paths to a silhouette (heart, leaf, diamond, etc.)
///   and hide every unused dot by serializing the final visible-cell mask.
/// - Every arrow remains a simple non-repeating grid path.
/// - Every arrow has a straight run before the head.
/// - The head ray cannot cross its own body.
/// - The chosen orientation set is guaranteed acyclic by construction.
/// - Generation is bounded and deterministic by seed; there is no unbounded
///   brute-force search and no long solver DFS.
/// </summary>
public static class ArrowPuzzleReferenceGenerator
{
    public enum GenerationDifficulty
    {
        Easy,
        Medium,
        Hard,
        Expert,
        Mixed
    }

    public enum BoardShape
    {
        Rectangle,
        Heart,
        Leaf,
        Diamond,
        Oval,
        Ring,
        Star,
        Clover,
        RandomBlob,
        Mixed
    }

    public enum GenerationStyle
    {
        ReferenceMaze,
        DenseWeave,
        Organic,
        Mixed
    }

    [Serializable]
    public sealed class GeneratedArrow
    {
        public List<Vector2Int> path = new List<Vector2Int>();
        public ArrowPathController.Direction headDirection;
        public int blockerCount;
        public int turns;
        public string styleSignature;
    }

    [Serializable]
    public sealed class GeneratedLevel
    {
        public int seed;
        public int width;
        public int height;
        public int arrowCount;
        public float spacing;
        public GenerationDifficulty difficulty;
        public BoardShape shape;
        public GenerationStyle style;
        public float coverage;
        public float shapeFill;
        public int initialSafe;
        public int totalDependencies;
        public int totalTurns;
        public List<GeneratedArrow> arrows = new List<GeneratedArrow>();
        public List<Vector2Int> activeCells = new List<Vector2Int>();
    }

    private struct OrientationCandidate
    {
        public int index;
        public bool reversed;
        public int blockers;
        public bool selfClear;
        public int straightRun;
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private const int HeadStraightRunMinCells = 2;
    private const int RectangleBackbiteMin = 1800;
    private const int RectangleBackbiteMax = 9000;

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------

    public static List<GeneratedLevel> GenerateBatch(
        int width,
        int height,
        int minArrows,
        int maxArrows,
        GenerationDifficulty difficultyMode,
        int count,
        int baseSeed,
        float spacing)
    {
        return GenerateBatch(
            width,
            height,
            minArrows,
            maxArrows,
            difficultyMode,
            count,
            baseSeed,
            spacing,
            BoardShape.Rectangle,
            GenerationStyle.ReferenceMaze,
            true
        );
    }

    public static List<GeneratedLevel> GenerateBatch(
        int width,
        int height,
        int minArrows,
        int maxArrows,
        GenerationDifficulty difficultyMode,
        int count,
        int baseSeed,
        float spacing,
        BoardShape shape,
        GenerationStyle style,
        bool fullCoverage)
    {
        width = Mathf.Clamp(width, 4, 48);
        height = Mathf.Clamp(height, 4, 48);
        minArrows = Mathf.Clamp(minArrows, 2, 63);

        int maxPhysical = Mathf.Max(
            minArrows,
            Mathf.Min(63, (width * height) / 3)
        );

        maxArrows = Mathf.Clamp(maxArrows, minArrows, maxPhysical);
        count = Mathf.Clamp(count, 1, 200);

        List<GeneratedLevel> results =
            new List<GeneratedLevel>(count);

        HashSet<string> signatures =
            new HashSet<string>();

        int seedBase =
            baseSeed == 0
                ? Environment.TickCount
                : baseSeed;

        // Keep a bounded batch budget. We never let one bad seed lock the
        // editor window in a huge retry loop.
        int maxBatchAttempts = Mathf.Max(count * 8, 24);

        for (int attempt = 0; attempt < maxBatchAttempts && results.Count < count; attempt++)
        {
            int seed = unchecked(
                seedBase +
                (attempt + 1) * 7919 +
                (attempt % 7) * 104729
            );

            System.Random rng = new System.Random(seed);

            int arrowCount = rng.Next(minArrows, maxArrows + 1);
            GenerationDifficulty difficulty =
                difficultyMode == GenerationDifficulty.Mixed
                    ? PickMixedDifficulty(rng)
                    : difficultyMode;

            BoardShape chosenShape =
                shape == BoardShape.Mixed
                    ? PickMixedShape(rng)
                    : shape;

            GenerationStyle chosenStyle =
                style == GenerationStyle.Mixed
                    ? PickMixedStyle(rng)
                    : style;

            GeneratedLevel candidate = null;

            try
            {
                candidate = GenerateOne(
                    width,
                    height,
                    arrowCount,
                    difficulty,
                    seed,
                    spacing,
                    rng,
                    chosenShape,
                    chosenStyle,
                    fullCoverage
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "مولد الأسهم: تم تجاهل محاولة تالفة بدون إيقاف Unity. " +
                    ex.GetType().Name + ": " + ex.Message
                );
                continue;
            }

            if (candidate == null)
                continue;

            string signature = BuildSignature(candidate);
            if (!signatures.Add(signature))
                continue;

            results.Add(candidate);
        }

        return results;
    }

    public static GeneratedLevel GenerateOne(
        int width,
        int height,
        int arrowCount,
        GenerationDifficulty difficulty,
        int seed,
        float spacing,
        System.Random rng)
    {
        return GenerateOne(
            width,
            height,
            arrowCount,
            difficulty,
            seed,
            spacing,
            rng,
            BoardShape.Rectangle,
            GenerationStyle.ReferenceMaze,
            true
        );
    }

    public static GeneratedLevel GenerateOne(
        int width,
        int height,
        int arrowCount,
        GenerationDifficulty difficulty,
        int seed,
        float spacing,
        System.Random rng,
        BoardShape shape,
        GenerationStyle style,
        bool fullCoverage)
    {
        width = Mathf.Clamp(width, 4, 48);
        height = Mathf.Clamp(height, 4, 48);
        arrowCount = Mathf.Clamp(
            arrowCount,
            2,
            Mathf.Min(63, (width * height) / 3)
        );

        if (rng == null)
            rng = new System.Random(seed);

        if (arrowCount * 3 > width * height)
            return null;

        if (shape == BoardShape.Rectangle && fullCoverage)
        {
            return GenerateRectangleLevel(
                width,
                height,
                arrowCount,
                difficulty,
                seed,
                spacing,
                style,
                rng
            );
        }

        return GenerateShapeLevel(
            width,
            height,
            arrowCount,
            difficulty,
            seed,
            spacing,
            shape,
            style,
            rng
        );
    }

    // ---------------------------------------------------------------------
    // Rectangle / reference maze generation
    // ---------------------------------------------------------------------

    private static GeneratedLevel GenerateRectangleLevel(
        int width,
        int height,
        int arrowCount,
        GenerationDifficulty difficulty,
        int seed,
        float spacing,
        GenerationStyle style,
        System.Random rng)
    {
        GeneratedLevel best = null;
        float bestScore = float.NegativeInfinity;

        int candidatePaths = GetPathCandidateCount(width, height, style);
        int candidatePartitions = GetPartitionCandidateCount(arrowCount, style);
        int backbiteMoves = GetBackbiteMoves(width, height, style, rng);

        for (int pathAttempt = 0; pathAttempt < candidatePaths; pathAttempt++)
        {
            List<Vector2Int> fullPath =
                BuildRandomizedHamiltonianPath(
                    width,
                    height,
                    rng,
                    backbiteMoves
                );

            if (fullPath == null || fullPath.Count != width * height)
                continue;

            float pathScore = ScoreWholePath(fullPath, width, height, style);

            for (int partitionAttempt = 0; partitionAttempt < candidatePartitions; partitionAttempt++)
            {
                List<List<Vector2Int>> segments =
                    BuildReferenceSegments(
                        fullPath,
                        arrowCount,
                        style,
                        rng
                    );

                if (segments == null)
                    continue;

                List<GeneratedArrow> oriented =
                    FindSolvableOrientation(
                        segments,
                        width,
                        height,
                        difficulty,
                        style,
                        rng
                    );

                if (oriented == null)
                    continue;

                if (!ValidateFullCoverage(
                        oriented,
                        width,
                        height
                    ))
                {
                    continue;
                }

                if (!HasUniqueArrowFingerprints(oriented))
                    continue;

                if (!IsAcyclic(oriented, width, height))
                    continue;

                int initialSafe;
                int totalDependencies;
                int totalTurns;
                GetStats(
                    oriented,
                    width,
                    height,
                    out initialSafe,
                    out totalDependencies,
                    out totalTurns
                );

                float visualScore =
                    ScoreArrowCollection(
                        oriented,
                        width,
                        height,
                        style
                    );

                float difficultyScore =
                    ScoreDifficulty(
                        difficulty,
                        initialSafe,
                        totalDependencies,
                        oriented.Count,
                        rng
                    );

                float score =
                    pathScore * 1.7f +
                    visualScore * 8.0f +
                    difficultyScore * 2.0f;

                if (best == null || score > bestScore)
                {
                    bestScore = score;

                    best = new GeneratedLevel
                    {
                        seed = seed,
                        width = width,
                        height = height,
                        arrowCount = oriented.Count,
                        spacing = spacing,
                        difficulty = difficulty,
                        shape = BoardShape.Rectangle,
                        style = style,
                        coverage = 1f,
                        shapeFill = 1f,
                        initialSafe = initialSafe,
                        totalDependencies = totalDependencies,
                        totalTurns = totalTurns,
                        arrows = oriented,
                        activeCells = AllCells(width, height)
                    };
                }
            }
        }

        return best;
    }

    private static int GetPathCandidateCount(
        int width,
        int height,
        GenerationStyle style)
    {
        int baseCount =
            width * height >= 1600
                ? 3
                : width * height >= 576
                    ? 5
                    : 7;

        if (style == GenerationStyle.DenseWeave)
            baseCount += 2;

        if (style == GenerationStyle.Organic)
            baseCount += 1;

        return Mathf.Clamp(baseCount, 2, 5);
    }

    private static int GetPartitionCandidateCount(
        int arrowCount,
        GenerationStyle style)
    {
        int count = arrowCount >= 20 ? 10 : 14;

        if (style == GenerationStyle.DenseWeave)
            count += 8;

        if (style == GenerationStyle.Organic)
            count += 4;

        return Mathf.Clamp(count, 8, 18);
    }

    private static int GetBackbiteMoves(
        int width,
        int height,
        GenerationStyle style,
        System.Random rng)
    {
        int cells = width * height;
        int min = Mathf.Clamp(cells * 1, 220, 900);
        int max = Mathf.Clamp(cells * 2 + 350, min + 120, 1800);

        if (style == GenerationStyle.DenseWeave)
        {
            min = Mathf.Clamp(min + 180, 220, 1200);
            max = Mathf.Clamp(max + 300, min + 120, 2200);
        }
        else if (style == GenerationStyle.Organic)
        {
            min = Mathf.Clamp(min + 80, 220, 1100);
            max = Mathf.Clamp(max + 180, min + 120, 2000);
        }

        return rng.Next(min, max + 1);
    }

    private static List<Vector2Int> BuildRandomizedHamiltonianPath(
        int width,
        int height,
        System.Random rng,
        int randomizationMoves)
    {
        // The valid snake is merely the starting Hamiltonian path. The final
        // layout is transformed by thousands of reversible local backbite
        // moves, so the visual result is not a row/column snake.
        List<Vector2Int> path = BuildSnake(width, height, rng);

        for (int move = 0; move < randomizationMoves; move++)
            TryBackbite(path, width, height, rng);

        if (!IsValidHamiltonian(path, width, height))
            return null;

        return path;
    }

    private static bool TryBackbite(
        List<Vector2Int> path,
        int width,
        int height,
        System.Random rng)
    {
        int n = path.Count;
        if (n < 8)
            return false;

        bool reverse = rng.Next(0, 2) == 0;
        if (reverse)
            path.Reverse();

        Vector2Int endpoint = path[n - 1];
        List<Vector2Int> neighbours = new List<Vector2Int>(4);

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int cell = endpoint + Directions[i];
            if (Inside(cell, width, height))
                neighbours.Add(cell);
        }

        Shuffle(neighbours, rng);

        Dictionary<Vector2Int, int> indexOf =
            new Dictionary<Vector2Int, int>(n);

        for (int i = 0; i < n; i++)
            indexOf[path[i]] = i;

        for (int i = 0; i < neighbours.Count; i++)
        {
            Vector2Int neighbour = neighbours[i];

            if (!indexOf.TryGetValue(neighbour, out int cutIndex))
                continue;

            if (cutIndex >= n - 2)
                continue;

            List<Vector2Int> transformed =
                new List<Vector2Int>(n);

            for (int p = 0; p <= cutIndex; p++)
                transformed.Add(path[p]);

            transformed.Add(endpoint);

            for (int p = n - 2; p >= cutIndex + 1; p--)
                transformed.Add(path[p]);

            path.Clear();
            path.AddRange(transformed);

            if (reverse)
                path.Reverse();

            return true;
        }

        if (reverse)
            path.Reverse();

        return false;
    }

    // ---------------------------------------------------------------------
    // Balanced reference partitioning
    // ---------------------------------------------------------------------

    private static List<List<Vector2Int>> BuildReferenceSegments(
        List<Vector2Int> fullPath,
        int arrowCount,
        GenerationStyle style,
        System.Random rng)
    {
        int total = fullPath.Count;
        if (arrowCount < 2 || arrowCount * 3 > total)
            return null;

        int average = Mathf.Max(3, total / arrowCount);
        int minLength = Mathf.Clamp(
            Mathf.FloorToInt(
                average *
                (style == GenerationStyle.DenseWeave ? 0.48f : 0.52f)
            ),
            3,
            16
        );

        int maxLength = Mathf.Clamp(
            Mathf.CeilToInt(
                average *
                (style == GenerationStyle.Organic ? 1.70f : 1.55f)
            ),
            minLength,
            Mathf.Max(minLength, total / 2)
        );

        // The cut sequence is selected against the actual path. A segment is
        // accepted only when either its normal head has a straight run or its
        // reversed head has one. This makes the head-never-at-the-turn rule a
        // structural property instead of a late visual guess.
        List<int> cuts = new List<int>(arrowCount + 1)
        {
            0
        };

        bool[] endSafe = new bool[total];
        bool[] startSafe = new bool[total];

        for (int i = 0; i < total; i++)
        {
            if (i >= 2)
            {
                Vector2Int a = fullPath[i - 1] - fullPath[i - 2];
                Vector2Int b = fullPath[i] - fullPath[i - 1];
                endSafe[i] = a == b;
            }

            if (i + 2 < total)
            {
                Vector2Int a = fullPath[i + 1] - fullPath[i];
                Vector2Int b = fullPath[i + 2] - fullPath[i + 1];
                startSafe[i] = a == b;
            }
        }

        int branchBudget = 1200;
        if (!SelectReferenceCuts(
                fullPath,
                arrowCount,
                minLength,
                maxLength,
                style,
                rng,
                endSafe,
                startSafe,
                cuts,
                0,
                ref branchBudget))
        {
            return null;
        }

        List<List<Vector2Int>> segments = new List<List<Vector2Int>>(arrowCount);

        for (int i = 0; i < arrowCount; i++)
        {
            int startIndex = cuts[i];
            int endIndex = cuts[i + 1];
            int length = endIndex - startIndex;

            if (length < 3)
                return null;

            List<Vector2Int> segment = fullPath.GetRange(
                startIndex,
                length
            );

            segments.Add(segment);
        }

        // Shuffle segment identities only. The cells remain disjoint.
        Shuffle(segments, rng);
        return segments;
    }

    private static bool SelectReferenceCuts(
        List<Vector2Int> fullPath,
        int arrowCount,
        int minLength,
        int maxLength,
        GenerationStyle style,
        System.Random rng,
        bool[] endSafe,
        bool[] startSafe,
        List<int> cuts,
        int segmentIndex,
        ref int branchBudget)
    {
        if (branchBudget-- <= 0)
            return false;

        if (segmentIndex == arrowCount - 1)
        {
            int start = cuts[cuts.Count - 1];
            int finalLength = fullPath.Count - start;

            if (finalLength < minLength || finalLength > maxLength)
                return false;

            return endSafe[fullPath.Count - 1] || startSafe[start];
        }

        int startIndex = cuts[cuts.Count - 1];
        int arrowsRemainingAfterThis = arrowCount - segmentIndex - 1;

        int minEnd = startIndex + minLength;
        int maxEnd = Mathf.Min(
            startIndex + maxLength,
            fullPath.Count - arrowsRemainingAfterThis * minLength
        );

        if (minEnd > maxEnd)
            return false;

        List<int> candidates = new List<int>();

        for (int end = minEnd; end <= maxEnd; end++)
        {
            if (end >= fullPath.Count)
                break;

            bool normalHeadOkay = endSafe[end - 1];
            bool reversedHeadOkay = startSafe[startIndex];

            if (!normalHeadOkay && !reversedHeadOkay)
                continue;

            candidates.Add(end);
        }

        if (candidates.Count == 0)
            return false;

        float target = (fullPath.Count - startIndex) /
                       (float)(arrowCount - segmentIndex);

        if (style == GenerationStyle.DenseWeave)
            target *= 0.95f + (float)rng.NextDouble() * 0.10f;
        else if (style == GenerationStyle.Organic)
            target *= 0.75f + (float)rng.NextDouble() * 0.55f;
        else
            target *= 0.82f + (float)rng.NextDouble() * 0.40f;

        float targetEnd = startIndex + target;

        Shuffle(candidates, rng);
        candidates.Sort((a, b) =>
        {
            float da = Mathf.Abs(a - targetEnd);
            float db = Mathf.Abs(b - targetEnd);
            return da.CompareTo(db);
        });

        int limit = Mathf.Min(candidates.Count, 80);

        for (int i = 0; i < limit; i++)
        {
            cuts.Add(candidates[i]);

            if (SelectReferenceCuts(
                    fullPath,
                    arrowCount,
                    minLength,
                    maxLength,
                    style,
                    rng,
                    endSafe,
                    startSafe,
                    cuts,
                    segmentIndex + 1,
                    ref branchBudget))
            {
                return true;
            }

            cuts.RemoveAt(cuts.Count - 1);
        }

        return false;
    }

    private static List<int> BuildBalancedLengths(
        int total,
        int arrowCount,
        int minLength,
        int maxLength,
        GenerationStyle style,
        System.Random rng)
    {
        if (arrowCount <= 0)
            return null;

        minLength = Mathf.Max(3, minLength);
        maxLength = Mathf.Max(minLength, maxLength);

        if (arrowCount * minLength > total ||
            arrowCount * maxLength < total)
        {
            return null;
        }

        List<int> lengths = new List<int>(arrowCount);
        for (int i = 0; i < arrowCount; i++)
            lengths.Add(minLength);

        int remaining = total - arrowCount * minLength;

        // Distribute spare cells in randomized chunks while respecting every
        // arrow's hard upper limit. No final "remainder" arrow is allowed to
        // become a giant monster path.
        while (remaining > 0)
        {
            List<int> candidates = new List<int>();

            for (int i = 0; i < lengths.Count; i++)
                if (lengths[i] < maxLength)
                    candidates.Add(i);

            if (candidates.Count == 0)
                return null;

            int index = candidates[rng.Next(candidates.Count)];
            int room = maxLength - lengths[index];
            int amount = Mathf.Min(
                remaining,
                Mathf.Max(1, rng.Next(1, Mathf.Min(room, 5) + 1))
            );

            lengths[index] += amount;
            remaining -= amount;
        }

        // A few balancing passes create the short/medium/long distribution
        // without creating a systematic staircase.
        int passes = arrowCount * 4;
        for (int pass = 0; pass < passes; pass++)
        {
            int a = rng.Next(lengths.Count);
            int b = rng.Next(lengths.Count);

            if (a == b)
                continue;

            if (lengths[a] <= minLength || lengths[b] >= maxLength)
                continue;

            int room = maxLength - lengths[b];
            int transferable = Mathf.Min(
                lengths[a] - minLength,
                room
            );

            if (transferable <= 0)
                continue;

            int amount = rng.Next(
                1,
                Mathf.Min(transferable, 3) + 1
            );

            lengths[a] -= amount;
            lengths[b] += amount;
        }

        return lengths;
    }

    private static List<List<Vector2Int>> SlicePath(
        List<Vector2Int> fullPath,
        List<int> lengths)
    {
        List<List<Vector2Int>> result =
            new List<List<Vector2Int>>(lengths.Count);

        int cursor = 0;

        for (int i = 0; i < lengths.Count; i++)
        {
            int length = lengths[i];

            if (length < 3 || cursor + length > fullPath.Count)
                return null;

            result.Add(
                fullPath.GetRange(
                    cursor,
                    length
                )
            );

            cursor += length;
        }

        return cursor == fullPath.Count ? result : null;
    }

    private static bool AllSegmentsHaveSomeStraightOrientation(
        List<List<Vector2Int>> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (!HasStraightHeadRun(segments[i], false) &&
                !HasStraightHeadRun(segments[i], true))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------------
    // Shape mode: independent irregular paths inside a silhouette
    // ---------------------------------------------------------------------

    private static GeneratedLevel GenerateShapeLevel(
        int width,
        int height,
        int arrowCount,
        GenerationDifficulty difficulty,
        int seed,
        float spacing,
        BoardShape shape,
        GenerationStyle style,
        System.Random rng)
    {
        bool[,] mask = BuildShapeMask(width, height, shape, rng);
        List<Vector2Int> maskCells = ExtractCells(mask, width, height);

        if (maskCells.Count < arrowCount * 4)
            return null;

        GeneratedLevel best = null;
        float bestScore = float.NegativeInfinity;

        int attempts = Mathf.Min(
            8,
            GetShapeAttemptCount(shape, style, width, height)
        );

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int targetCells = ComputeShapeTargetCells(
                maskCells.Count,
                arrowCount,
                difficulty,
                style,
                rng
            );

            List<GeneratedArrow> arrows =
                BuildIndependentShapeArrowsFast(
                    mask,
                    maskCells,
                    width,
                    height,
                    arrowCount,
                    targetCells,
                    difficulty,
                    style,
                    shape,
                    rng
                );

            if (arrows == null)
                continue;

            List<GeneratedArrow> oriented =
                OrientIndependentArrows(
                    arrows,
                    width,
                    height,
                    difficulty,
                    style,
                    rng
                );

            if (oriented == null)
                continue;

            if (!ValidateArrowSet(
                    oriented,
                    width,
                    height
                ))
            {
                continue;
            }

            if (!IsAcyclic(oriented, width, height))
                continue;

            int usedCells = CountUniqueCells(oriented);
            float fill = usedCells / (float)Mathf.Max(1, maskCells.Count);

            if (fill < GetMinimumShapeFill(style))
                continue;

            int initialSafe;
            int totalDependencies;
            int totalTurns;
            GetStats(
                oriented,
                width,
                height,
                out initialSafe,
                out totalDependencies,
                out totalTurns
            );

            List<Vector2Int> activeCells = GetUniqueCells(oriented);

            float silhouette =
                ScoreSilhouette(activeCells, maskCells, width, height);

            float visual =
                ScoreArrowCollection(
                    oriented,
                    width,
                    height,
                    style
                );

            float difficultyScore =
                ScoreDifficulty(
                    difficulty,
                    initialSafe,
                    totalDependencies,
                    oriented.Count,
                    rng
                );

            float score =
                fill * 10f +
                silhouette * 14f +
                visual * 7f +
                difficultyScore * 2f;

            if (best == null || score > bestScore)
            {
                bestScore = score;

                best = new GeneratedLevel
                {
                    seed = seed,
                    width = width,
                    height = height,
                    arrowCount = oriented.Count,
                    spacing = spacing,
                    difficulty = difficulty,
                    shape = shape,
                    style = style,
                    coverage = 1f,
                    shapeFill = fill,
                    initialSafe = initialSafe,
                    totalDependencies = totalDependencies,
                    totalTurns = totalTurns,
                    arrows = oriented,
                    activeCells = activeCells
                };
            }
        }

        return best;
    }

    private static int GetShapeAttemptCount(
        BoardShape shape,
        GenerationStyle style,
        int width,
        int height)
    {
        int cells = width * height;
        int attempts = cells >= 1600 ? 30 : cells >= 576 ? 50 : 70;

        if (style == GenerationStyle.DenseWeave)
            attempts += 15;

        if (shape == BoardShape.RandomBlob)
            attempts += 10;

        return Mathf.Clamp(attempts, 24, 90);
    }

    private static int ComputeShapeTargetCells(
        int maskCount,
        int arrowCount,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        float minFill = 0.78f;
        float maxFill = 0.90f;

        switch (difficulty)
        {
            case GenerationDifficulty.Easy:
                minFill = 0.70f;
                maxFill = 0.82f;
                break;
            case GenerationDifficulty.Medium:
                minFill = 0.76f;
                maxFill = 0.86f;
                break;
            case GenerationDifficulty.Hard:
                minFill = 0.80f;
                maxFill = 0.90f;
                break;
            case GenerationDifficulty.Expert:
                minFill = 0.82f;
                maxFill = 0.92f;
                break;
        }

        if (style == GenerationStyle.DenseWeave)
        {
            minFill += 0.02f;
            maxFill += 0.02f;
        }

        int target = Mathf.RoundToInt(
            maskCount * Mathf.Lerp(
                minFill,
                maxFill,
                (float)rng.NextDouble()
            )
        );

        target = Mathf.Max(target, arrowCount * 7);
        target = Mathf.Min(target, maskCount);
        return target;
    }

    private static float GetMinimumShapeFill(GenerationStyle style)
    {
        switch (style)
        {
            case GenerationStyle.DenseWeave:
                return 0.67f;
            case GenerationStyle.Organic:
                return 0.74f;
            default:
                return 0.70f;
        }
    }

    // ---------------------------------------------------------------------
    // FAST SHAPE GENERATION
    // ---------------------------------------------------------------------

    private static List<GeneratedArrow> BuildIndependentShapeArrowsFast(
        bool[,] mask,
        List<Vector2Int> maskCells,
        int width,
        int height,
        int arrowCount,
        int targetCells,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        BoardShape shape,
        System.Random rng)
    {
        HashSet<Vector2Int> occupied =
            new HashSet<Vector2Int>();

        List<GeneratedArrow> result =
            new List<GeneratedArrow>(arrowCount);

        List<Vector2Int> boundary =
            CollectBoundaryCells(mask, width, height);

        if (boundary.Count == 0)
            boundary.AddRange(maskCells);

        Shuffle(boundary, rng);

        int remaining = targetCells;

        for (int index = 0; index < arrowCount; index++)
        {
            int arrowsLeft = arrowCount - index;
            int minRemaining = (arrowsLeft - 1) * 7;
            int preferred = Mathf.Clamp(
                Mathf.RoundToInt(
                    remaining / (float)Mathf.Max(1, arrowsLeft)
                ),
                7,
                28
            );

            int low = Mathf.Max(7, preferred - 5);
            int high = Mathf.Max(
                low,
                Mathf.Min(
                    30,
                    remaining - minRemaining
                )
            );

            GeneratedArrow arrow = null;

            // Most heads live on the silhouette boundary. This gives shape
            // modes a visible contour instead of making every arrow start in
            // the center of the shape.
            for (int trial = 0; trial < 32 && arrow == null; trial++)
            {
                Vector2Int head = boundary[
                    rng.Next(boundary.Count)
                ];

                if (occupied.Contains(head))
                    continue;

                int desired =
                    index == arrowCount - 1
                        ? Mathf.Clamp(remaining, low, high)
                        : rng.Next(low, high + 1);

                arrow = TryBuildBoundaryHeadArrow(
                    mask,
                    occupied,
                    head,
                    width,
                    height,
                    desired,
                    difficulty,
                    style,
                    rng
                );
            }

            // Fallback to any free mask cell. Still bounded and fast.
            if (arrow == null)
            {
                Vector2Int start = PickFreeMaskCellFast(
                    mask,
                    occupied,
                    width,
                    height,
                    rng
                );

                if (start != InvalidCell)
                {
                    int desired = index == arrowCount - 1
                        ? Mathf.Clamp(remaining, low, high)
                        : rng.Next(low, high + 1);

                    arrow = TryBuildFreeShapeArrow(
                        mask,
                        occupied,
                        start,
                        width,
                        height,
                        desired,
                        difficulty,
                        style,
                        rng
                    );
                }
            }

            if (arrow == null)
                return null;

            result.Add(arrow);
            for (int p = 0; p < arrow.path.Count; p++)
                occupied.Add(arrow.path[p]);

            remaining -= arrow.path.Count;
        }

        return result.Count == arrowCount ? result : null;
    }

    private static List<Vector2Int> CollectBoundaryCells(
        bool[,] mask,
        int width,
        int height)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[x, y])
                    continue;

                Vector2Int cell = new Vector2Int(x, y);
                bool boundary = false;

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next = cell + Directions[i];
                    if (!Inside(next, width, height) || !mask[next.x, next.y])
                    {
                        boundary = true;
                        break;
                    }
                }

                if (boundary)
                    result.Add(cell);
            }
        }

        return result;
    }

    private static GeneratedArrow TryBuildBoundaryHeadArrow(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        Vector2Int head,
        int width,
        int height,
        int desiredLength,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        // Find a direction that immediately leaves the silhouette and has
        // two cells available behind the head. This guarantees the required
        // straight run and a clean escape ray without any solver search.
        List<Vector2Int> headDirections = new List<Vector2Int>(4);

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int outward = Directions[i];
            Vector2Int previous = head - outward;
            Vector2Int previous2 = head - outward * 2;

            if (!Inside(previous, width, height) ||
                !Inside(previous2, width, height))
                continue;

            if (!mask[previous.x, previous.y] ||
                !mask[previous2.x, previous2.y])
                continue;

            if (occupied.Contains(previous) ||
                occupied.Contains(previous2))
                continue;

            // The ray in this direction must remain outside the silhouette.
            bool rayClear = true;
            Vector2Int ray = head + outward;
            while (Inside(ray, width, height))
            {
                if (mask[ray.x, ray.y])
                {
                    rayClear = false;
                    break;
                }
                ray += outward;
            }

            if (rayClear)
                headDirections.Add(outward);
        }

        if (headDirections.Count == 0)
            return null;

        Vector2Int headOutward =
            headDirections[rng.Next(headDirections.Count)];

        List<Vector2Int> reversePath =
            new List<Vector2Int>(desiredLength);

        HashSet<Vector2Int> local =
            new HashSet<Vector2Int>();

        reversePath.Add(head);
        local.Add(head);

        // First two cells behind the head are forced to be collinear.
        Vector2Int first = head - headOutward;
        Vector2Int second = head - headOutward * 2;

        reversePath.Add(first);
        reversePath.Add(second);
        local.Add(first);
        local.Add(second);

        int guard = desiredLength * 5;

        while (reversePath.Count < desiredLength && guard-- > 0)
        {
            Vector2Int current = reversePath[reversePath.Count - 1];
            List<Vector2Int> options = new List<Vector2Int>(4);

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];

                if (!Inside(next, width, height) ||
                    !mask[next.x, next.y] ||
                    occupied.Contains(next) ||
                    local.Contains(next))
                    continue;

                options.Add(next);
            }

            if (options.Count == 0)
                break;

            Shuffle(options, rng);

            // Prefer cells with more free neighbors so the random walk does
            // not seal itself off early, but keep a random choice among the
            // best few candidates.
            options.Sort((a, b) =>
            {
                int sa = CountAvailableNeighbours(
                    a, mask, occupied, width, height
                );
                int sb = CountAvailableNeighbours(
                    b, mask, occupied, width, height
                );
                return sb.CompareTo(sa);
            });

            int top = Mathf.Min(options.Count, 3);
            Vector2Int chosen = options[rng.Next(top)];
            reversePath.Add(chosen);
            local.Add(chosen);
        }

        if (reversePath.Count < desiredLength)
            return null;

        reversePath.Reverse();

        if (!HasStraightHeadRun(reversePath, false))
            return null;

        int turns = CountPathTurns(reversePath);
        if (!MeetsTurnProfile(
                reversePath.Count,
                turns,
                difficulty,
                style))
            return null;

        return new GeneratedArrow
        {
            path = reversePath,
            headDirection = ToDirection(
                reversePath[reversePath.Count - 1] -
                reversePath[reversePath.Count - 2]
            ),
            blockerCount = 0,
            turns = turns,
            styleSignature = BuildShapeSignature(reversePath)
        };
    }

    private static float BoundaryWalkScore(
        Vector2Int cell,
        Vector2 center,
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        System.Random rng)
    {
        int free = 0;
        int occupiedNeighbours = 0;

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int next = cell + Directions[i];
            if (!Inside(next, width, height) || !mask[next.x, next.y])
                continue;

            if (occupied.Contains(next))
                occupiedNeighbours++;
            else
                free++;
        }

        float edgeBias =
            Vector2.Distance(
                new Vector2(cell.x, cell.y),
                center
            ) * 0.16f;

        return free * 2f -
               occupiedNeighbours * 3f +
               edgeBias +
               (float)rng.NextDouble() * 2f;
    }

    private static Vector2Int PickFreeMaskCellFast(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        System.Random rng)
    {
        for (int i = 0; i < 80; i++)
        {
            Vector2Int cell = new Vector2Int(
                rng.Next(width),
                rng.Next(height)
            );

            if (mask[cell.x, cell.y] && !occupied.Contains(cell))
                return cell;
        }

        return InvalidCell;
    }

    private static GeneratedArrow TryBuildFreeShapeArrow(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        Vector2Int start,
        int width,
        int height,
        int desiredLength,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        List<Vector2Int> path = new List<Vector2Int>(desiredLength);
        HashSet<Vector2Int> local = new HashSet<Vector2Int>();

        path.Add(start);
        local.Add(start);

        int guard = desiredLength * 5;
        while (path.Count < desiredLength && guard-- > 0)
        {
            List<Vector2Int> options = new List<Vector2Int>(4);
            Vector2Int current = path[path.Count - 1];

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (!Inside(next, width, height) ||
                    !mask[next.x, next.y] ||
                    occupied.Contains(next) ||
                    local.Contains(next))
                    continue;

                options.Add(next);
            }

            if (options.Count == 0)
                break;

            Shuffle(options, rng);
            Vector2Int chosen = options[0];

            if (options.Count > 1 && rng.NextDouble() < 0.55)
                chosen = options[rng.Next(Mathf.Min(options.Count, 3))];

            path.Add(chosen);
            local.Add(chosen);
        }

        if (path.Count < desiredLength)
            return null;

        int turns = CountPathTurns(path);
        if (!MeetsTurnProfile(path.Count, turns, difficulty, style))
            return null;

        return new GeneratedArrow
        {
            path = path,
            headDirection = ToDirection(path[path.Count - 1] - path[path.Count - 2]),
            blockerCount = 0,
            turns = turns,
            styleSignature = BuildShapeSignature(path)
        };
    }

    private static List<GeneratedArrow> BuildIndependentShapeArrows(
        bool[,] mask,
        List<Vector2Int> maskCells,
        int width,
        int height,
        int arrowCount,
        int targetCells,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        HashSet<Vector2Int> occupied =
            new HashSet<Vector2Int>();

        List<GeneratedArrow> arrows =
            new List<GeneratedArrow>(arrowCount);

        int remainingTarget = targetCells;

        for (int arrowIndex = 0; arrowIndex < arrowCount; arrowIndex++)
        {
            int arrowsLeft = arrowCount - arrowIndex;

            int minLength = 6;
            int averageLeft = Mathf.Max(
                minLength,
                remainingTarget / Mathf.Max(1, arrowsLeft)
            );

            int low = Mathf.Max(
                minLength,
                Mathf.FloorToInt(averageLeft * 0.62f)
            );

            int high = Mathf.Max(
                low,
                Mathf.CeilToInt(averageLeft *
                    (style == GenerationStyle.Organic ? 1.55f : 1.40f))
            );

            int reserveForOthers =
                (arrowsLeft - 1) * minLength;

            high = Mathf.Min(
                high,
                remainingTarget - reserveForOthers
            );

            if (high < low)
                return null;

            int desired = arrowIndex == arrowCount - 1
                ? remainingTarget
                : rng.Next(low, high + 1);

            desired = Mathf.Clamp(desired, minLength, high);

            GeneratedArrow arrow =
                TryGrowShapeArrow(
                    mask,
                    occupied,
                    width,
                    height,
                    desired,
                    difficulty,
                    style,
                    arrowIndex,
                    rng
                );

            if (arrow == null)
                return null;

            if (!HasMeaningfulShapeVariation(arrows, arrow))
            {
                // A second attempt prevents a batch of near-identical arrows.
                arrow = TryGrowShapeArrow(
                    mask,
                    occupied,
                    width,
                    height,
                    desired,
                    difficulty,
                    style,
                    arrowIndex,
                    rng
                );
            }

            if (arrow == null)
                return null;

            arrows.Add(arrow);
            for (int i = 0; i < arrow.path.Count; i++)
                occupied.Add(arrow.path[i]);

            remainingTarget -= arrow.path.Count;
        }

        return arrows.Count == arrowCount ? arrows : null;
    }

    private static GeneratedArrow TryGrowShapeArrow(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        int desiredLength,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        int arrowIndex,
        System.Random rng)
    {
        int startTrials = Mathf.Clamp(width * height / 3, 80, 260);
        int walkTrials = style == GenerationStyle.DenseWeave ? 90 : 60;

        for (int startAttempt = 0; startAttempt < startTrials; startAttempt++)
        {
            Vector2Int start =
                PickWeightedFreeMaskCell(
                    mask,
                    occupied,
                    width,
                    height,
                    style,
                    rng
                );

            if (start == InvalidCell)
                return null;

            for (int walkAttempt = 0; walkAttempt < walkTrials; walkAttempt++)
            {
                List<Vector2Int> path =
                    GrowRandomSelfAvoidingPath(
                        mask,
                        occupied,
                        start,
                        width,
                        height,
                        desiredLength,
                        style,
                        rng
                    );

                if (path == null || path.Count < 6)
                    continue;

                if (!HasStraightHeadRun(path, false) &&
                    !HasStraightHeadRun(path, true))
                {
                    continue;
                }

                int turns = CountPathTurns(path);
                if (!MeetsTurnProfile(path.Count, turns, difficulty, style))
                    continue;

                return new GeneratedArrow
                {
                    path = path,
                    headDirection = ToDirection(
                        path[path.Count - 1] - path[path.Count - 2]
                    ),
                    blockerCount = 0,
                    turns = turns,
                    styleSignature = BuildShapeSignature(path)
                };
            }
        }

        return null;
    }

    private static List<Vector2Int> GrowRandomSelfAvoidingPath(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        Vector2Int start,
        int width,
        int height,
        int desiredLength,
        GenerationStyle style,
        System.Random rng)
    {
        List<Vector2Int> path =
            new List<Vector2Int>(desiredLength);

        HashSet<Vector2Int> local =
            new HashSet<Vector2Int>();

        path.Add(start);
        local.Add(start);

        int guard = desiredLength * 10;

        while (path.Count < desiredLength && guard-- > 0)
        {
            List<Vector2Int> options =
                new List<Vector2Int>(4);

            Vector2Int current = path[path.Count - 1];

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];

                if (!Inside(next, width, height))
                    continue;

                if (!mask[next.x, next.y])
                    continue;

                if (occupied.Contains(next) || local.Contains(next))
                    continue;

                options.Add(next);
            }

            if (options.Count == 0)
                break;

            options.Sort((a, b) =>
            {
                float scoreA = ScoreWalkOption(
                    path,
                    a,
                    mask,
                    occupied,
                    width,
                    height,
                    style,
                    rng
                );

                float scoreB = ScoreWalkOption(
                    path,
                    b,
                    mask,
                    occupied,
                    width,
                    height,
                    style,
                    rng
                );

                // Randomized ordering among close scores prevents deterministic
                // worms while still avoiding dead-end traps.
                return scoreA.CompareTo(scoreB);
            });

            Vector2Int selected;

            if (path.Count >= desiredLength - 2)
            {
                selected = PickFinalStraightOption(
                    path,
                    options,
                    mask,
                    occupied,
                    width,
                    height,
                    rng
                );

                if (selected == InvalidCell)
                    break;
            }
            else
            {
                int take = ChooseWeightedIndex(
                    options.Count,
                    style,
                    rng
                );

                selected = options[take];
            }

            path.Add(selected);
            local.Add(selected);
        }

        if (path.Count < desiredLength)
            return null;

        return path;
    }

    private static Vector2Int PickFinalStraightOption(
        List<Vector2Int> path,
        List<Vector2Int> options,
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        System.Random rng)
    {
        if (path.Count < 2)
            return InvalidCell;

        Vector2Int prevStep =
            path[path.Count - 1] - path[path.Count - 2];

        // Prefer continuing the existing direction first; this creates the
        // mandatory two-cell straight section before the head.
        for (int i = 0; i < options.Count; i++)
        {
            Vector2Int d = options[i] - path[path.Count - 1];
            if (d != prevStep)
                continue;

            Vector2Int second = options[i] + d;

            if (!Inside(second, width, height))
                continue;

            if (!mask[second.x, second.y])
                continue;

            if (occupied.Contains(second))
                continue;

            bool used = false;
            for (int p = 0; p < path.Count; p++)
            {
                if (path[p] == second)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
                return options[i];
        }

        // Fallback: pick any option with a possible same-direction continuation.
        for (int i = 0; i < options.Count; i++)
        {
            Vector2Int d = options[i] - path[path.Count - 1];
            Vector2Int second = options[i] + d;

            if (!Inside(second, width, height) ||
                !mask[second.x, second.y] ||
                occupied.Contains(second))
            {
                continue;
            }

            bool used = false;
            for (int p = 0; p < path.Count; p++)
            {
                if (path[p] == second)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
                return options[i];
        }

        return InvalidCell;
    }

    private static float ScoreWalkOption(
        List<Vector2Int> path,
        Vector2Int next,
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        GenerationStyle style,
        System.Random rng)
    {
        int freeNeighbours = 0;
        int occupiedNeighbours = 0;

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int n = next + Directions[i];
            if (!Inside(n, width, height) || !mask[n.x, n.y])
                continue;

            if (occupied.Contains(n))
                occupiedNeighbours++;
            else
                freeNeighbours++;
        }

        float score = freeNeighbours * 5f - occupiedNeighbours * 3f;

        if (path.Count >= 2)
        {
            Vector2Int previousDirection =
                path[path.Count - 1] - path[path.Count - 2];

            Vector2Int nextDirection =
                next - path[path.Count - 1];

            bool turn = nextDirection != previousDirection;

            float turnBias;
            switch (style)
            {
                case GenerationStyle.DenseWeave:
                    turnBias = turn ? -7f : 1.5f;
                    break;
                case GenerationStyle.Organic:
                    turnBias = turn ? -2.0f : 0.25f;
                    break;
                default:
                    turnBias = turn ? -4.5f : 0.5f;
                    break;
            }

            score += turnBias;
        }

        score += (float)rng.NextDouble() * 6f;
        return score;
    }

    private static Vector2Int PickWeightedFreeMaskCell(
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height,
        GenerationStyle style,
        System.Random rng)
    {
        Vector2 center = new Vector2(
            (width - 1) * 0.5f,
            (height - 1) * 0.5f
        );

        Vector2Int best = InvalidCell;
        float bestScore = float.NegativeInfinity;

        int tries = Mathf.Clamp(width * height / 2, 60, 260);

        for (int i = 0; i < tries; i++)
        {
            Vector2Int cell = new Vector2Int(
                rng.Next(width),
                rng.Next(height)
            );

            if (!mask[cell.x, cell.y] || occupied.Contains(cell))
                continue;

            float distanceFromCenter =
                Vector2.Distance(
                    new Vector2(cell.x, cell.y),
                    center
                );

            float score =
                (float)rng.NextDouble() * 8f;

            if (style == GenerationStyle.DenseWeave)
                score -= distanceFromCenter * 0.20f;
            else if (style == GenerationStyle.Organic)
                score += distanceFromCenter * 0.08f;

            score += CountAvailableNeighbours(
                cell,
                mask,
                occupied,
                width,
                height
            ) * 2f;

            if (score > bestScore)
            {
                bestScore = score;
                best = cell;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------------
    // Orientation + solver
    // ---------------------------------------------------------------------

    private static List<GeneratedArrow> FindSolvableOrientation(
        List<List<Vector2Int>> segments,
        int width,
        int height,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        int n = segments.Count;

        int[,] owner = BuildOwnerMap(segments, width, height);
        HashSet<int> remaining = new HashSet<int>();
        List<GeneratedArrow> result = new List<GeneratedArrow>(n);

        for (int i = 0; i < n; i++)
            remaining.Add(i);

        while (remaining.Count > 0)
        {
            List<OrientationCandidate> choices =
                new List<OrientationCandidate>();

            foreach (int index in remaining)
            {
                OrientationCandidate normal = EvaluateOrientation(
                    segments[index],
                    index,
                    false,
                    owner,
                    remaining,
                    width,
                    height
                );

                OrientationCandidate reversed = EvaluateOrientation(
                    segments[index],
                    index,
                    true,
                    owner,
                    remaining,
                    width,
                    height
                );

                if (normal.selfClear && normal.blockers == 0)
                    choices.Add(normal);

                if (reversed.selfClear && reversed.blockers == 0)
                    choices.Add(reversed);
            }

            if (choices.Count == 0)
                return null;

            OrientationCandidate picked =
                PickOrientationChoice(
                    choices,
                    difficulty,
                    style,
                    rng
                );

            List<Vector2Int> pickedPath =
                new List<Vector2Int>(segments[picked.index]);

            if (picked.reversed)
                pickedPath.Reverse();

            result.Add(new GeneratedArrow
            {
                path = pickedPath,
                headDirection = ToDirection(
                    pickedPath[pickedPath.Count - 1] -
                    pickedPath[pickedPath.Count - 2]
                ),
                blockerCount = 0,
                turns = CountPathTurns(pickedPath),
                styleSignature = BuildShapeSignature(pickedPath)
            });

            remaining.Remove(picked.index);
        }

        Shuffle(result, rng);
        return result;
    }

    private static List<GeneratedArrow> OrientIndependentArrows(
        List<GeneratedArrow> source,
        int width,
        int height,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        int n = source.Count;
        int[,] owner = BuildOwnerMapFromArrows(source, width, height);
        HashSet<int> remaining = new HashSet<int>();
        List<GeneratedArrow> result = new List<GeneratedArrow>(n);

        for (int i = 0; i < n; i++)
            remaining.Add(i);

        while (remaining.Count > 0)
        {
            List<OrientationCandidate> safeChoices =
                new List<OrientationCandidate>();

            foreach (int index in remaining)
            {
                OrientationCandidate normal = EvaluateOrientation(
                    source[index].path,
                    index,
                    false,
                    owner,
                    remaining,
                    width,
                    height
                );

                OrientationCandidate reversed = EvaluateOrientation(
                    source[index].path,
                    index,
                    true,
                    owner,
                    remaining,
                    width,
                    height
                );

                if (normal.selfClear && normal.blockers == 0)
                    safeChoices.Add(normal);

                if (reversed.selfClear && reversed.blockers == 0)
                    safeChoices.Add(reversed);
            }

            if (safeChoices.Count == 0)
                return null;

            OrientationCandidate picked =
                PickOrientationChoice(
                    safeChoices,
                    difficulty,
                    style,
                    rng
                );

            List<Vector2Int> pickedPath =
                new List<Vector2Int>(source[picked.index].path);

            if (picked.reversed)
                pickedPath.Reverse();

            result.Add(new GeneratedArrow
            {
                path = pickedPath,
                headDirection = ToDirection(
                    pickedPath[pickedPath.Count - 1] -
                    pickedPath[pickedPath.Count - 2]
                ),
                blockerCount = 0,
                turns = CountPathTurns(pickedPath),
                styleSignature = BuildShapeSignature(pickedPath)
            });

            remaining.Remove(picked.index);
        }

        Shuffle(result, rng);
        return result;
    }

    private static OrientationCandidate EvaluateOrientation(
        List<Vector2Int> source,
        int index,
        bool reversed,
        int[,] owner,
        HashSet<int> remaining,
        int width,
        int height)
    {
        List<Vector2Int> path = new List<Vector2Int>(source);
        if (reversed)
            path.Reverse();

        bool straight = HasStraightHeadRun(path, false);
        int straightRun = CountHeadStraightRun(path);
        bool selfClear = straight && SelfRayClear(path, width, height);
        HashSet<int> blockers = new HashSet<int>();

        if (selfClear)
        {
            Vector2Int head = path[path.Count - 1];
            Vector2Int step = head - path[path.Count - 2];
            Vector2Int check = head + step;

            while (Inside(check, width, height))
            {
                int ownerId = owner[check.x, check.y];
                if (ownerId >= 0 && ownerId != index && remaining.Contains(ownerId))
                    blockers.Add(ownerId);

                check += step;
            }
        }

        return new OrientationCandidate
        {
            index = index,
            reversed = reversed,
            blockers = blockers.Count,
            selfClear = selfClear,
            straightRun = straightRun
        };
    }

    private static OrientationCandidate PickOrientationChoice(
        List<OrientationCandidate> choices,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        if (choices.Count == 1)
            return choices[0];

        choices.Sort((a, b) =>
        {
            float scoreA = OrientationScore(a, difficulty, style, rng);
            float scoreB = OrientationScore(b, difficulty, style, rng);
            return scoreA.CompareTo(scoreB);
        });

        // Keep a randomized top slice instead of always taking the same item.
        int top = Mathf.Min(
            choices.Count,
            difficulty == GenerationDifficulty.Expert ? 3 : 6
        );

        return choices[rng.Next(top)];
    }

    private static float OrientationScore(
        OrientationCandidate candidate,
        GenerationDifficulty difficulty,
        GenerationStyle style,
        System.Random rng)
    {
        float score = 0f;

        score += candidate.straightRun * 0.35f;
        score += (float)rng.NextDouble() * 2f;

        if (style == GenerationStyle.DenseWeave)
            score -= candidate.straightRun * 0.10f;

        switch (difficulty)
        {
            case GenerationDifficulty.Easy:
                score -= candidate.straightRun * 0.12f;
                break;
            case GenerationDifficulty.Expert:
                score += candidate.straightRun * 0.18f;
                break;
        }

        return score;
    }

    // ---------------------------------------------------------------------
    // Shape masks
    // ---------------------------------------------------------------------

    private static readonly Vector2Int InvalidCell =
        new Vector2Int(int.MinValue, int.MinValue);

    private static bool[,] BuildShapeMask(
        int width,
        int height,
        BoardShape shape,
        System.Random rng)
    {
        bool[,] mask = new bool[width, height];

        float cx = (width - 1) * 0.5f;
        float cy = (height - 1) * 0.5f;
        float sx = Mathf.Max(1f, width * 0.46f);
        float sy = Mathf.Max(1f, height * 0.46f);
        float blobPhase1 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float blobPhase2 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float blobPhase3 = (float)rng.NextDouble() * Mathf.PI * 2f;
        float blobBias = Mathf.Lerp(-0.035f, 0.035f, (float)rng.NextDouble());

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x - cx) / sx;
                float ny = (y - cy) / sy;

                bool inside;

                switch (shape)
                {
                    case BoardShape.Heart:
                        inside = HeartContains(nx, ny);
                        break;

                    case BoardShape.Leaf:
                        inside = LeafContains(nx, ny);
                        break;

                    case BoardShape.Diamond:
                        inside = Mathf.Abs(nx) + Mathf.Abs(ny) <= 0.92f;
                        break;

                    case BoardShape.Oval:
                        inside = nx * nx + ny * ny <= 0.90f;
                        break;

                    case BoardShape.Ring:
                        {
                            float r = Mathf.Sqrt(nx * nx + ny * ny);
                            inside = r <= 0.92f && r >= 0.38f;
                            break;
                        }

                    case BoardShape.Star:
                        inside = StarContains(nx, ny);
                        break;

                    case BoardShape.Clover:
                        inside = CloverContains(nx, ny);
                        break;

                    case BoardShape.RandomBlob:
                        inside = RandomBlobContains(
                            nx, ny, blobPhase1, blobPhase2, blobPhase3, blobBias);
                        break;

                    default:
                        inside = true;
                        break;
                }

                mask[x, y] = inside;
            }
        }

        // Remove tiny isolated islands. The generator only needs a silhouette;
        // isolated pixels are useless for an arrow and would make path growth fail.
        RemoveTinyIslands(mask, width, height, 3);
        EnsureMinimumMask(mask, width, height, rng);

        return mask;
    }

    private static bool HeartContains(float x, float y)
    {
        // Classic filled heart, tuned for low-resolution grids.
        float sx = x * 1.02f;
        float sy = y * 1.04f;
        float a = sx * sx + sy * sy - 1f;
        float value = a * a * a - sx * sx * sy * sy * sy;
        return value <= 0.02f;
    }

    private static bool LeafContains(float x, float y)
    {
        float main =
            ((x * 0.90f) * (x * 0.90f)) +
            ((y * 0.72f) * (y * 0.72f));

        float diagonal =
            Mathf.Abs(x * 0.75f + y * 0.52f);

        return main <= 0.93f &&
               diagonal <= 0.98f &&
               y < 0.95f;
    }

    private static bool StarContains(float x, float y)
    {
        float angle = Mathf.Atan2(y, x);
        float r = Mathf.Sqrt(x * x + y * y);

        float sector = Mathf.Cos(angle * 5f);
        float limit = Mathf.Lerp(0.52f, 0.94f, (sector + 1f) * 0.5f);

        return r <= limit;
    }

    private static bool CloverContains(float x, float y)
    {
        float d1 = DistanceSquared(x - 0.35f, y - 0.28f);
        float d2 = DistanceSquared(x + 0.35f, y - 0.28f);
        float d3 = DistanceSquared(x - 0.35f, y + 0.28f);
        float d4 = DistanceSquared(x + 0.35f, y + 0.28f);

        return d1 <= 0.45f ||
               d2 <= 0.45f ||
               d3 <= 0.45f ||
               d4 <= 0.45f;
    }

    private static bool RandomBlobContains(
        float x,
        float y,
        float phase1,
        float phase2,
        float phase3,
        float seedBias)
    {
        // Smooth, deterministic radial noise. The phases are generated once
        // per board, so every cell sees the same coherent blob function.
        float angle = Mathf.Atan2(y, x);
        float radius = Mathf.Sqrt(x * x + y * y);

        float wave =
            0.10f * Mathf.Sin(angle * 3f + phase1) +
            0.07f * Mathf.Sin(angle * 7f + phase2) +
            0.05f * Mathf.Cos(angle * 11f + phase3);

        return radius <= 0.86f + wave + seedBias;
    }

    private static float DistanceSquared(float x, float y)
    {
        return x * x + y * y;
    }

    private static void RemoveTinyIslands(
        bool[,] mask,
        int width,
        int height,
        int minimumIslandSize)
    {
        bool[,] seen = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[x, y] || seen[x, y])
                    continue;

                List<Vector2Int> island =
                    new List<Vector2Int>();

                Queue<Vector2Int> queue =
                    new Queue<Vector2Int>();

                Vector2Int start = new Vector2Int(x, y);
                queue.Enqueue(start);
                seen[x, y] = true;

                while (queue.Count > 0)
                {
                    Vector2Int current = queue.Dequeue();
                    island.Add(current);

                    for (int i = 0; i < Directions.Length; i++)
                    {
                        Vector2Int next = current + Directions[i];

                        if (!Inside(next, width, height))
                            continue;

                        if (!mask[next.x, next.y] || seen[next.x, next.y])
                            continue;

                        seen[next.x, next.y] = true;
                        queue.Enqueue(next);
                    }
                }

                if (island.Count < minimumIslandSize)
                {
                    for (int i = 0; i < island.Count; i++)
                        mask[island[i].x, island[i].y] = false;
                }
            }
        }
    }

    private static void EnsureMinimumMask(
        bool[,] mask,
        int width,
        int height,
        System.Random rng)
    {
        int count = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (mask[x, y]) count++;

        int minimum = Mathf.Max(32, width * height / 5);
        if (count >= minimum)
            return;

        // Grow the largest central region if a narrow shape became too small.
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        Vector2Int center = new Vector2Int(width / 2, height / 2);
        queue.Enqueue(center);
        visited.Add(center);

        while (queue.Count > 0 && count < minimum)
        {
            Vector2Int current = queue.Dequeue();

            if (Inside(current, width, height) && !mask[current.x, current.y])
            {
                mask[current.x, current.y] = true;
                count++;
            }

            List<Vector2Int> options = new List<Vector2Int>(4);
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (!Inside(next, width, height) || visited.Contains(next))
                    continue;

                visited.Add(next);
                options.Add(next);
            }

            Shuffle(options, rng);
            for (int i = 0; i < options.Count; i++)
                queue.Enqueue(options[i]);
        }
    }

    // ---------------------------------------------------------------------
    // Validation and scoring
    // ---------------------------------------------------------------------

    private static bool ValidateFullCoverage(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        if (arrows == null || arrows.Count == 0)
            return false;

        bool[,] seen = new bool[width, height];
        int count = 0;

        for (int i = 0; i < arrows.Count; i++)
        {
            GeneratedArrow arrow = arrows[i];
            if (!ValidatePath(arrow.path, width, height))
                return false;

            if (!SelfRayClear(arrow.path, width, height))
                return false;

            if (!HasStraightHeadRun(arrow.path, false))
                return false;

            for (int p = 0; p < arrow.path.Count; p++)
            {
                Vector2Int cell = arrow.path[p];
                if (seen[cell.x, cell.y])
                    return false;

                seen[cell.x, cell.y] = true;
                count++;
            }
        }

        return count == width * height;
    }

    private static bool ValidateArrowSet(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        if (arrows == null || arrows.Count == 0)
            return false;

        HashSet<Vector2Int> used =
            new HashSet<Vector2Int>();

        for (int i = 0; i < arrows.Count; i++)
        {
            GeneratedArrow arrow = arrows[i];

            if (!ValidatePath(arrow.path, width, height))
                return false;

            if (!HasStraightHeadRun(arrow.path, false))
                return false;

            if (!SelfRayClear(arrow.path, width, height))
                return false;

            for (int p = 0; p < arrow.path.Count; p++)
            {
                if (!used.Add(arrow.path[p]))
                    return false;
            }
        }

        return true;
    }

    private static bool ValidatePath(
        List<Vector2Int> path,
        int width,
        int height)
    {
        if (path == null || path.Count < 3)
            return false;

        HashSet<Vector2Int> local =
            new HashSet<Vector2Int>();

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];

            if (!Inside(cell, width, height))
                return false;

            if (!local.Add(cell))
                return false;

            if (i == 0)
                continue;

            Vector2Int delta = cell - path[i - 1];
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                return false;
        }

        return true;
    }

    private static bool SelfRayClear(
        List<Vector2Int> path,
        int width,
        int height)
    {
        Vector2Int head = path[path.Count - 1];
        Vector2Int step = head - path[path.Count - 2];
        Vector2Int check = head + step;

        HashSet<Vector2Int> own = new HashSet<Vector2Int>(path);

        while (Inside(check, width, height))
        {
            if (own.Contains(check))
                return false;

            check += step;
        }

        return true;
    }

    private static bool HasStraightHeadRun(
        List<Vector2Int> path,
        bool reverse)
    {
        if (path == null || path.Count < HeadStraightRunMinCells + 1)
            return false;

        List<Vector2Int> p = path;
        int n = p.Count;

        if (!reverse)
        {
            Vector2Int step = p[n - 1] - p[n - 2];

            for (int i = 2; i <= HeadStraightRunMinCells; i++)
            {
                if (p[n - i] - p[n - i - 1] != step)
                    return false;
            }

            return true;
        }

        Vector2Int reverseStep = p[0] - p[1];

        for (int i = 1; i < HeadStraightRunMinCells; i++)
        {
            if (p[i] - p[i + 1] != reverseStep)
                return false;
        }

        return true;
    }

    private static int CountHeadStraightRun(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
            return 0;

        Vector2Int step =
            path[path.Count - 1] - path[path.Count - 2];

        int run = 1;

        for (int i = path.Count - 2; i > 0; i--)
        {
            Vector2Int previous = path[i] - path[i - 1];
            if (previous != step)
                break;

            run++;
        }

        return run;
    }

    private static bool IsAcyclic(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        int n = arrows.Count;
        int[,] owner = BuildOwnerMapFromArrows(arrows, width, height);
        List<HashSet<int>> graph = new List<HashSet<int>>(n);

        for (int i = 0; i < n; i++)
            graph.Add(new HashSet<int>());

        for (int i = 0; i < n; i++)
        {
            Vector2Int head = arrows[i].path[^1];
            Vector2Int step = head - arrows[i].path[^2];
            Vector2Int check = head + step;

            while (Inside(check, width, height))
            {
                int other = owner[check.x, check.y];
                if (other >= 0 && other != i)
                    graph[i].Add(other);

                check += step;
            }
        }

        int[] indegree = new int[n];

        for (int i = 0; i < n; i++)
            foreach (int j in graph[i])
                indegree[j]++;

        Queue<int> queue = new Queue<int>();
        for (int i = 0; i < n; i++)
            if (indegree[i] == 0)
                queue.Enqueue(i);

        int visited = 0;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            visited++;

            foreach (int next in graph[current])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        return visited == n;
    }

    private static int[,] BuildOwnerMap(
        List<List<Vector2Int>> segments,
        int width,
        int height)
    {
        int[,] owner = new int[width, height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                owner[x, y] = -1;

        for (int i = 0; i < segments.Count; i++)
            for (int p = 0; p < segments[i].Count; p++)
            {
                Vector2Int cell = segments[i][p];
                owner[cell.x, cell.y] = i;
            }

        return owner;
    }

    private static int[,] BuildOwnerMapFromArrows(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        int[,] owner = new int[width, height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                owner[x, y] = -1;

        for (int i = 0; i < arrows.Count; i++)
            for (int p = 0; p < arrows[i].path.Count; p++)
            {
                Vector2Int cell = arrows[i].path[p];
                owner[cell.x, cell.y] = i;
            }

        return owner;
    }

    private static void GetStats(
        List<GeneratedArrow> arrows,
        int width,
        int height,
        out int initialSafe,
        out int totalDependencies,
        out int totalTurns)
    {
        initialSafe = 0;
        totalDependencies = 0;
        totalTurns = 0;

        int[,] owner = BuildOwnerMapFromArrows(arrows, width, height);

        for (int i = 0; i < arrows.Count; i++)
        {
            GeneratedArrow arrow = arrows[i];
            totalTurns += CountPathTurns(arrow.path);

            Vector2Int head = arrow.path[^1];
            Vector2Int step = head - arrow.path[^2];
            Vector2Int check = head + step;
            HashSet<int> blockers = new HashSet<int>();

            while (Inside(check, width, height))
            {
                int other = owner[check.x, check.y];
                if (other >= 0 && other != i)
                    blockers.Add(other);

                check += step;
            }

            if (blockers.Count == 0)
                initialSafe++;

            arrow.blockerCount = blockers.Count;
            totalDependencies += blockers.Count;
        }
    }

    private static bool HasUniqueArrowFingerprints(
        List<GeneratedArrow> arrows)
    {
        HashSet<string> signatures =
            new HashSet<string>();

        for (int i = 0; i < arrows.Count; i++)
        {
            List<Vector2Int> path = arrows[i].path;
            if (path == null || path.Count < 3)
                return false;

            string signature =
                path.Count.ToString() + ":" +
                BuildShapeSignature(path);

            if (!signatures.Add(signature))
                return false;
        }

        return true;
    }

    private static float ScoreWholePath(
        List<Vector2Int> path,
        int width,
        int height,
        GenerationStyle style)
    {
        if (path == null || path.Count < 2)
            return float.NegativeInfinity;

        int turns = CountPathTurns(path);
        int longRuns = CountLongRuns(path, 6);
        float turnRatio = turns / (float)Mathf.Max(1, path.Count - 2);

        float edgeBalance = ScoreEdgeUse(path, width, height);
        float localVariety = ScoreLocalDirectionVariety(path);
        float railPenalty = ScoreRailPenalty(path, width, height);

        float score =
            turnRatio * 25f +
            edgeBalance * 4f +
            localVariety * 20f -
            longRuns * 0.55f -
            railPenalty * 1.8f;

        if (style == GenerationStyle.DenseWeave)
            score += turnRatio * 10f;

        if (style == GenerationStyle.Organic)
            score += edgeBalance * 6f;

        return score;
    }

    private static float ScoreArrowCollection(
        List<GeneratedArrow> arrows,
        int width,
        int height,
        GenerationStyle style)
    {
        if (arrows == null || arrows.Count == 0)
            return float.NegativeInfinity;

        float turn = 0f;
        float lengths = 0f;
        float boxes = 0f;
        float signatures = 0f;
        float directionMix = 0f;

        Dictionary<string, int> signatureCounts =
            new Dictionary<string, int>();

        for (int i = 0; i < arrows.Count; i++)
        {
            GeneratedArrow arrow = arrows[i];
            turn += CountPathTurns(arrow.path) /
                    (float)Mathf.Max(1, arrow.path.Count - 2);
            lengths += arrow.path.Count;

            BoundsInt bounds = GetPathBounds(arrow.path);
            boxes += bounds.size.x * bounds.size.y;

            string signature = BuildShapeSignature(arrow.path);
            if (!signatureCounts.ContainsKey(signature))
                signatureCounts.Add(signature, 0);

            signatureCounts[signature]++;
        }

        foreach (KeyValuePair<string, int> kv in signatureCounts)
        {
            if (kv.Value == 1)
                signatures += 1f;
            else
                signatures -= (kv.Value - 1) * 0.7f;
        }

        directionMix = CountUsedHeadDirections(arrows) / 4f;

        float averageLength = lengths / arrows.Count;
        float lengthVariation = ScoreLengthVariation(arrows, averageLength);
        float boxCompactness = boxes / Mathf.Max(1f, width * height * arrows.Count);

        float score =
            turn / arrows.Count * 30f +
            signatures * 2.5f +
            directionMix * 8f +
            lengthVariation * 12f -
            boxCompactness * 3f;

        if (style == GenerationStyle.DenseWeave)
            score += directionMix * 3f;

        return score;
    }

    private static float ScoreLengthVariation(
        List<GeneratedArrow> arrows,
        float average)
    {
        if (arrows.Count <= 1)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < arrows.Count; i++)
            sum += Mathf.Abs(arrows[i].path.Count - average);

        return Mathf.Clamp01(
            sum / Mathf.Max(1f, average * arrows.Count)
        );
    }

    private static float ScoreSilhouette(
        List<Vector2Int> active,
        List<Vector2Int> mask,
        int width,
        int height)
    {
        if (active == null || active.Count == 0 || mask == null || mask.Count == 0)
            return 0f;

        HashSet<Vector2Int> target = new HashSet<Vector2Int>(mask);
        int inside = 0;

        for (int i = 0; i < active.Count; i++)
            if (target.Contains(active[i]))
                inside++;

        float fit = inside / (float)active.Count;
        BoundsInt bounds = GetPathBounds(active);
        float bboxRatio =
            (bounds.size.x * bounds.size.y) /
            (float)Mathf.Max(1, width * height);

        return Mathf.Clamp01(
            fit * 0.75f + Mathf.Clamp01(bboxRatio * 3f) * 0.25f
        );
    }

    private static float ScoreEdgeUse(
        List<Vector2Int> path,
        int width,
        int height)
    {
        int edgeCount = 0;

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int p = path[i];
            if (p.x == 0 || p.x == width - 1 ||
                p.y == 0 || p.y == height - 1)
            {
                edgeCount++;
            }
        }

        return edgeCount / (float)Mathf.Max(1, path.Count);
    }

    private static float ScoreLocalDirectionVariety(
        List<Vector2Int> path)
    {
        if (path.Count < 4)
            return 0f;

        int turns = 0;
        int reversals = 0;

        for (int i = 2; i < path.Count; i++)
        {
            Vector2Int a = path[i - 1] - path[i - 2];
            Vector2Int b = path[i] - path[i - 1];

            if (a != b)
                turns++;

            if (a == -b)
                reversals++;
        }

        return turns / (float)Mathf.Max(1, path.Count - 2) +
               reversals / (float)Mathf.Max(1, path.Count - 2);
    }

    private static float ScoreRailPenalty(
        List<Vector2Int> path,
        int width,
        int height)
    {
        int horizontalRows = 0;
        int verticalCols = 0;
        int run = 0;

        Vector2Int last = InvalidCell;
        Vector2Int step = Vector2Int.zero;

        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int currentStep = path[i] - path[i - 1];

            if (i == 1 || currentStep == step)
            {
                run++;
            }
            else
            {
                if (run >= Mathf.Max(width / 4, 4))
                {
                    if (step.x != 0) horizontalRows++;
                    else verticalCols++;
                }
                run = 1;
            }

            step = currentStep;
            last = path[i];
        }

        if (run >= Mathf.Max(width / 4, 4))
        {
            if (step.x != 0) horizontalRows++;
            else verticalCols++;
        }

        return horizontalRows + verticalCols;
    }

    private static int CountLongRuns(List<Vector2Int> path, int threshold)
    {
        int count = 0;
        if (path.Count < 2)
            return count;

        int run = 1;
        Vector2Int direction = path[1] - path[0];

        for (int i = 2; i < path.Count; i++)
        {
            Vector2Int next = path[i] - path[i - 1];

            if (next == direction)
            {
                run++;
            }
            else
            {
                if (run >= threshold)
                    count++;

                run = 1;
                direction = next;
            }
        }

        if (run >= threshold)
            count++;

        return count;
    }

    private static BoundsInt GetPathBounds(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0)
            return new BoundsInt();

        int minX = path[0].x;
        int maxX = path[0].x;
        int minY = path[0].y;
        int maxY = path[0].y;

        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int p = path[i];
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        return new BoundsInt(
            minX,
            minY,
            0,
            maxX - minX + 1,
            maxY - minY + 1,
            1
        );
    }

    private static int CountUsedHeadDirections(List<GeneratedArrow> arrows)
    {
        bool up = false;
        bool down = false;
        bool left = false;
        bool right = false;

        for (int i = 0; i < arrows.Count; i++)
        {
            switch (arrows[i].headDirection)
            {
                case ArrowPathController.Direction.Up: up = true; break;
                case ArrowPathController.Direction.Down: down = true; break;
                case ArrowPathController.Direction.Left: left = true; break;
                case ArrowPathController.Direction.Right: right = true; break;
            }
        }

        int count = 0;
        if (up) count++;
        if (down) count++;
        if (left) count++;
        if (right) count++;
        return count;
    }

    private static int CountPathTurns(List<Vector2Int> path)
    {
        int turns = 0;

        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2Int a = path[i] - path[i - 1];
            Vector2Int b = path[i + 1] - path[i];
            if (a != b)
                turns++;
        }

        return turns;
    }

    private static bool MeetsTurnProfile(
        int length,
        int turns,
        GenerationDifficulty difficulty,
        GenerationStyle style)
    {
        float ratio = turns / (float)Mathf.Max(1, length - 2);

        float minimum;

        switch (difficulty)
        {
            case GenerationDifficulty.Easy:
                minimum = 0.10f;
                break;
            case GenerationDifficulty.Medium:
                minimum = 0.16f;
                break;
            case GenerationDifficulty.Hard:
                minimum = 0.22f;
                break;
            case GenerationDifficulty.Expert:
                minimum = 0.28f;
                break;
            default:
                minimum = 0.16f;
                break;
        }

        if (style == GenerationStyle.DenseWeave)
            minimum += 0.04f;

        return ratio >= minimum;
    }

    private static float ScoreDifficulty(
        GenerationDifficulty difficulty,
        int initialSafe,
        int totalDependencies,
        int arrowCount,
        System.Random rng)
    {
        float safe = initialSafe / (float)Mathf.Max(1, arrowCount);
        float deps = totalDependencies /
                     (float)Mathf.Max(1, arrowCount * 2);

        float score = (float)rng.NextDouble();

        switch (difficulty)
        {
            case GenerationDifficulty.Easy:
                score += (1f - Mathf.Abs(safe - 0.48f)) * 4f;
                score -= deps * 2f;
                break;

            case GenerationDifficulty.Medium:
                score += (1f - Mathf.Abs(safe - 0.30f)) * 5f;
                score += (1f - Mathf.Abs(deps - 0.55f)) * 3f;
                break;

            case GenerationDifficulty.Hard:
                score += (1f - safe) * 5f;
                score += deps * 5f;
                break;

            case GenerationDifficulty.Expert:
                score += (1f - safe) * 8f;
                score += deps * 7f;
                break;

            default:
                score += deps * 3f;
                break;
        }

        return score;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static bool IsValidHamiltonian(
        List<Vector2Int> path,
        int width,
        int height)
    {
        if (path == null || path.Count != width * height)
            return false;

        HashSet<Vector2Int> seen =
            new HashSet<Vector2Int>();

        for (int i = 0; i < path.Count; i++)
        {
            if (!Inside(path[i], width, height))
                return false;

            if (!seen.Add(path[i]))
                return false;

            if (i > 0)
            {
                Vector2Int d = path[i] - path[i - 1];
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1)
                    return false;
            }
        }

        return true;
    }

    private static List<Vector2Int> BuildSnake(
        int width,
        int height,
        System.Random rng)
    {
        List<Vector2Int> path =
            new List<Vector2Int>(width * height);

        bool vertical = rng.Next(0, 2) == 0;

        if (!vertical)
        {
            for (int y = 0; y < height; y++)
            {
                if ((y & 1) == 0)
                {
                    for (int x = 0; x < width; x++)
                        path.Add(new Vector2Int(x, y));
                }
                else
                {
                    for (int x = width - 1; x >= 0; x--)
                        path.Add(new Vector2Int(x, y));
                }
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                if ((x & 1) == 0)
                {
                    for (int y = 0; y < height; y++)
                        path.Add(new Vector2Int(x, y));
                }
                else
                {
                    for (int y = height - 1; y >= 0; y--)
                        path.Add(new Vector2Int(x, y));
                }
            }
        }

        if (rng.Next(0, 2) == 0)
            path.Reverse();

        return path;
    }

    private static List<Vector2Int> AllCells(int width, int height)
    {
        List<Vector2Int> cells = new List<Vector2Int>(width * height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private static List<Vector2Int> ExtractCells(
        bool[,] mask,
        int width,
        int height)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (mask[x, y])
                    cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private static int CountUniqueCells(List<GeneratedArrow> arrows)
    {
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        for (int i = 0; i < arrows.Count; i++)
            for (int p = 0; p < arrows[i].path.Count; p++)
                used.Add(arrows[i].path[p]);
        return used.Count;
    }

    private static List<Vector2Int> GetUniqueCells(List<GeneratedArrow> arrows)
    {
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        for (int i = 0; i < arrows.Count; i++)
            for (int p = 0; p < arrows[i].path.Count; p++)
                used.Add(arrows[i].path[p]);

        List<Vector2Int> result = new List<Vector2Int>(used);
        result.Sort((a, b) =>
        {
            int byY = a.y.CompareTo(b.y);
            return byY != 0 ? byY : a.x.CompareTo(b.x);
        });
        return result;
    }

    private static bool HasMeaningfulShapeVariation(
        List<GeneratedArrow> arrows,
        GeneratedArrow candidate)
    {
        if (arrows == null || arrows.Count == 0)
            return true;

        for (int i = 0; i < arrows.Count; i++)
        {
            if (arrows[i].styleSignature == candidate.styleSignature)
                return false;

            if (Mathf.Abs(
                    arrows[i].path.Count - candidate.path.Count
                ) <= 1 &&
                Mathf.Abs(
                    arrows[i].turns - candidate.turns
                ) <= 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int CountAvailableNeighbours(
        Vector2Int cell,
        bool[,] mask,
        HashSet<Vector2Int> occupied,
        int width,
        int height)
    {
        int count = 0;
        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int n = cell + Directions[i];
            if (!Inside(n, width, height) || !mask[n.x, n.y])
                continue;

            if (!occupied.Contains(n))
                count++;
        }
        return count;
    }

    private static float ScoreLengthVariation(
        List<GeneratedArrow> arrows,
        int averageLength)
    {
        if (arrows == null || arrows.Count <= 1)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < arrows.Count; i++)
            sum += Mathf.Abs(arrows[i].path.Count - averageLength);

        return Mathf.Clamp01(
            sum / Mathf.Max(1f, averageLength * arrows.Count)
        );
    }

    private static string BuildShapeSignature(
        List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
            return string.Empty;

        System.Text.StringBuilder sb =
            new System.Text.StringBuilder();

        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int d = path[i] - path[i - 1];

            if (d == Vector2Int.right) sb.Append('R');
            else if (d == Vector2Int.left) sb.Append('L');
            else if (d == Vector2Int.up) sb.Append('U');
            else sb.Append('D');
        }

        return sb.ToString();
    }

    private static float OrientationSpreadScore(
        List<GeneratedArrow> arrows)
    {
        return CountUsedHeadDirections(arrows) / 4f;
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

    private static ArrowPathController.Direction ToDirection(Vector2Int delta)
    {
        if (delta == Vector2Int.left)
            return ArrowPathController.Direction.Left;
        if (delta == Vector2Int.up)
            return ArrowPathController.Direction.Up;
        if (delta == Vector2Int.down)
            return ArrowPathController.Direction.Down;
        return ArrowPathController.Direction.Right;
    }

    private static int ChooseWeightedIndex(
        int count,
        GenerationStyle style,
        System.Random rng)
    {
        if (count <= 1)
            return 0;

        float r = (float)rng.NextDouble();

        if (style == GenerationStyle.DenseWeave)
        {
            int index = Mathf.FloorToInt(
                Mathf.Pow(r, 1.7f) * count
            );
            return Mathf.Clamp(index, 0, count - 1);
        }

        if (style == GenerationStyle.Organic)
        {
            int index = Mathf.FloorToInt(
                Mathf.Pow(r, 0.75f) * count
            );
            return Mathf.Clamp(index, 0, count - 1);
        }

        return rng.Next(count);
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    private static GenerationDifficulty PickMixedDifficulty(System.Random rng)
    {
        int roll = rng.Next(0, 100);
        if (roll < 22) return GenerationDifficulty.Easy;
        if (roll < 50) return GenerationDifficulty.Medium;
        if (roll < 80) return GenerationDifficulty.Hard;
        return GenerationDifficulty.Expert;
    }

    private static BoardShape PickMixedShape(System.Random rng)
    {
        int roll = rng.Next(0, 100);
        if (roll < 32) return BoardShape.Rectangle;
        if (roll < 48) return BoardShape.Heart;
        if (roll < 62) return BoardShape.Leaf;
        if (roll < 72) return BoardShape.Oval;
        if (roll < 82) return BoardShape.Diamond;
        if (roll < 89) return BoardShape.Ring;
        if (roll < 95) return BoardShape.Clover;
        return BoardShape.RandomBlob;
    }

    private static GenerationStyle PickMixedStyle(System.Random rng)
    {
        int roll = rng.Next(0, 100);
        if (roll < 35) return GenerationStyle.ReferenceMaze;
        if (roll < 60) return GenerationStyle.DenseWeave;
        if (roll < 82) return GenerationStyle.Organic;
        return GenerationStyle.ReferenceMaze;
    }

    private static string BuildSignature(GeneratedLevel level)
    {
        System.Text.StringBuilder sb =
            new System.Text.StringBuilder();

        sb.Append(level.width)
          .Append('x')
          .Append(level.height)
          .Append('|')
          .Append(level.shape)
          .Append('|')
          .Append(level.style)
          .Append('|');

        for (int i = 0; i < level.arrows.Count; i++)
        {
            List<Vector2Int> path = level.arrows[i].path;
            sb.Append(path.Count).Append(':');

            for (int p = 0; p < path.Count; p++)
                sb.Append(path[p].x)
                  .Append(',')
                  .Append(path[p].y)
                  .Append(';');

            sb.Append('|');
        }

        return sb.ToString();
    }
}
#endif
