using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelData))]
public class LevelGeneratorEditor : Editor
{
    private sealed class GeneratedArrow
    {
        public List<Vector2Int> path =
            new List<Vector2Int>();
    }

    private sealed class SolverResult
    {
        public bool solvable;
        public readonly List<int> order =
            new List<int>();

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
    private const int CandidateAttempts = 800;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelData level =
            (LevelData)target;

        GUILayout.Space(12);

        EditorGUILayout.LabelField(
            "Smart Arrow Puzzle Generator",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Creates randomized maze-like, grid-locked arrows. Full-board mode assigns every dot once, prevents self-crossing and checks that every arrow can eventually escape.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "GENERATE LEVEL",
                GUILayout.Height(42)
            )
        )
        {
            Generate(level);
        }

        if (
            GUILayout.Button(
                "VALIDATE SAVED LEVEL"
            )
        )
        {
            ValidateSavedLevel(level);
        }

        if (GUILayout.Button("RANDOM SET + GENERATE", GUILayout.Height(36)))
        {
            Undo.RecordObject(level, "Random Set And Generate Level");
            level.seed = Random.Range(1, int.MaxValue);
            EditorUtility.SetDirty(level);
            Generate(level);
        }

        if (
            GUILayout.Button(
                "CLEAR GENERATED ARROWS"
            )
        )
        {
            Undo.RecordObject(
                level,
                "Clear Generated Arrows"
            );

            level.arrows.Clear();

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
        }
    }

    private static void Generate(
        LevelData level)
    {
        if (level == null)
            return;

        int width =
            Mathf.Max(
                4,
                level.width
            );

        int height =
            Mathf.Max(
                4,
                level.height
            );

        // The solver uses a 64-bit mask, and every arrow needs at least
        // three cells so its final body segment can stay straight.
        int maximumArrowCount =
            Mathf.Min(
                63,
                Mathf.Max(
                    2,
                    level.fillEntireBoard
                        ? (width * height) / 3
                        : (width * height) / 2
                )
            );

        int arrowCount =
            Mathf.Clamp(
                level.arrowCount,
                2,
                maximumArrowCount
            );

        int seed =
            level.seed;

        if (seed == 0)
        {
            seed =
                Random.Range(
                    1,
                    int.MaxValue
                );

            level.seed =
                seed;
        }

        System.Random rng =
            new System.Random(
                seed
            );

        // Full-board mode uses a bounded construction instead of an
        // expensive random search. It assigns every grid dot exactly once
        // and creates a solvable dependency chain from the final arrow back.
        if (level.fillEntireBoard)
        {
            List<GeneratedArrow> fullCoverage =
                GenerateFullCoverageLevel(
                    width,
                    height,
                    arrowCount,
                    rng
                );

            if (
                fullCoverage != null &&
                ValidateCandidateGeometry(
                    fullCoverage,
                    width,
                    height,
                    0
                )
            )
            {
                SolverResult fullCoverageSolution =
                    SolveLevel(
                        fullCoverage,
                        width,
                        height
                    );

                if (fullCoverageSolution.solvable)
                {
                    SaveLevel(
                        level,
                        fullCoverage
                    );

                    Debug.Log(
                        "LevelGenerator: FULL COVERAGE LEVEL CREATED. " +
                        "Arrows=" + fullCoverage.Count +
                        ", Dots=" + (width * height) +
                        ", Coverage=100%" +
                        ", InitialSafe=" + fullCoverageSolution.initialSafe +
                        ", Dependencies=" + fullCoverageSolution.totalDependencies +
                        ", Seed=" + seed
                    );

                    Selection.activeObject = level;
                    return;
                }
            }

            Debug.LogError(
                "LevelGenerator: No solvable full-coverage layout was found in this search. " +
                "Current settings: " + width + "x" + height + ", arrows=" + arrowCount + ". " +
                "The generator now backtracks through randomized route partitions; try reducing the arrow count slightly if this persists."
            );
            return;
        }

        GetDifficultyProfile(
            level.difficulty,
            out int baseMinLength,
            out int baseMaxLength,
            out int minimumTurns,
            out int desiredMaxSafe,
            out int desiredInitialMin,
            out int desiredInitialMax,
            out float difficultyCoverage,
            out float desiredBlockedRatio
        );

        // Full-board mode requires every dot to belong to exactly one path.
        float targetCoverage =
            level.fillEntireBoard
                ? 1.0f
                : Mathf.Clamp(
                    level.targetCoverage,
                    0.35f,
                    0.80f
                );

        int targetCells =
            Mathf.RoundToInt(
                width *
                height *
                targetCoverage
            );

        /*
         * On a 12x12 board, 10 arrows at 62% coverage need about
         * 9 cells per arrow. The old generator capped Easy at 4 cells,
         * making the configured coverage impossible.
         *
         * We therefore raise the practical maximum as needed to satisfy
         * the board's requested density.
         */
        int densityDrivenMax =
            Mathf.CeilToInt(
                targetCells /
                Mathf.Max(
                    1f,
                    arrowCount
                )
            ) + 2;

        int minLength =
            Mathf.Clamp(
                Mathf.Min(
                    baseMinLength,
                    Mathf.Max(
                        3,
                        targetCells /
                        Mathf.Max(
                            1,
                            arrowCount * 2
                        )
                    )
                ),
                3,
                Mathf.Max(
                    3,
                    Mathf.Min(
                        width,
                        height
                    ) + 2
                )
            );

        int maxLength =
            Mathf.Clamp(
                Mathf.Max(
                    baseMaxLength,
                    densityDrivenMax
                ),
                minLength,
                Mathf.Max(
                    3,
                    width * height
                )
            );

        /*
         * We intentionally create several chains rather than one random
         * dependency cloud. The board starts with 1..3 safe roots, and
         * every later arrow is constructed to point at an existing arrow.
         */
        int chainRoots =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    arrowCount / 5f
                ),
                1,
                3
            );

        int maxGenerationAttempts =
            Mathf.Clamp(
                level.shapeIterations,
                100,
                MaxGenerationAttempts
            );

        for (
            int attempt = 0;
            attempt < maxGenerationAttempts;
            attempt++
        )
        {
            List<GeneratedArrow> candidate =
                GenerateDependencyLevel(
                    width,
                    height,
                    arrowCount,
                    minLength,
                    maxLength,
                    minimumTurns,
                    chainRoots,
                    targetCells,
                    level.headClearanceCells,
                    rng
                );

            if (candidate == null)
                continue;

            if (
                !ValidateCandidateGeometry(
                    candidate,
                    width,
                    height,
                    level.headClearanceCells
                )
            )
            {
                continue;
            }

            SolverResult solved =
                SolveLevel(
                    candidate,
                    width,
                    height
                );

            if (!solved.solvable)
                continue;

            if (
                solved.initialSafe <
                desiredInitialMin ||
                solved.initialSafe >
                desiredInitialMax
            )
            {
                continue;
            }

            if (
                solved.maximumSafe >
                desiredMaxSafe
            )
            {
                continue;
            }

            float blockedRatio =
                CountBlockedArrows(
                    candidate,
                    width,
                    height
                ) /
                (float)candidate.Count;

            if (
                blockedRatio <
                desiredBlockedRatio
            )
            {
                continue;
            }

            if (
                candidate.Count > 1 &&
                solved.totalDependencies <
                candidate.Count -
                chainRoots
            )
            {
                continue;
            }

            SaveLevel(
                level,
                candidate
            );

            Debug.Log(
                "LevelGenerator: VALID CHAIN LEVEL. " +
                "Arrows=" +
                candidate.Count +
                ", Coverage=" +
                GetCoverage(
                    candidate,
                    width * height
                ).ToString("P0") +
                ", InitialSafe=" +
                solved.initialSafe +
                ", MaxSafe=" +
                solved.maximumSafe +
                ", Dependencies=" +
                solved.totalDependencies +
                ", Blocked=" +
                blockedRatio.ToString("P0") +
                ", Seed=" +
                seed
            );

            Selection.activeObject =
                level;

            return;
        }

        Debug.LogError(
            "LevelGenerator: Could not find a valid dependency level. " +
            "For a dense 12x12 board, try 8-12 arrows and Minimum Separation 0 or regenerate with another seed."
        );
    }

    private static List<GeneratedArrow> GenerateFullCoverageLevel(
        int width,
        int height,
        int arrowCount,
        System.Random rng)
    {
        int totalCells = width * height;
        if (arrowCount < 1 || arrowCount > 63 || arrowCount * 3 > totalCells)
            return null;

        // Try several randomized Hamiltonian paths and several partitions of each.
        // Unlike a plain row/column snake, backbite moves change the route topology
        // while preserving adjacency and visiting every grid cell exactly once.
        // Full-coverage puzzles are constrained: each arrow needs a straight
        // launch lane, no part of its own body may lie on that lane, and the
        // blocker graph must be acyclic. Use more route topologies, then use a
        // backtracking partitioner instead of greedily committing to one cut.
        int pathAttempts = totalCells >= 1600 ? 8 : (totalCells >= 576 ? 16 : 24);
        int partitionsPerPath = totalCells >= 1600 ? 80 : 100;

        for (int pathAttempt = 0; pathAttempt < pathAttempts; pathAttempt++)
        {
            List<Vector2Int> route = BuildRandomizedHamiltonianPath(width, height, rng);
            if (route == null || route.Count != totalCells)
                continue;

            for (int partitionAttempt = 0; partitionAttempt < partitionsPerPath; partitionAttempt++)
            {
                List<GeneratedArrow> arrows = PartitionHamiltonianPath(route, arrowCount, rng);
                if (arrows == null)
                    continue;

                if (!ValidateCandidateGeometry(arrows, width, height, 0))
                    continue;

                SolverResult solution = SolveLevel(arrows, width, height);
                if (solution.solvable)
                    return arrows;
            }
        }

        return null;
    }

    private static List<GeneratedArrow> PartitionHamiltonianPath(
        List<Vector2Int> route,
        int arrowCount,
        System.Random rng)
    {
        int totalCells = route.Count;
        if (arrowCount < 1 || arrowCount > 63 || arrowCount * 3 > totalCells)
            return null;

        List<GeneratedArrow> arrows = new List<GeneratedArrow>(arrowCount);
        // Bound the recursive search so an awkward route never stalls the Editor.
        int searchBudget = 180;
        if (!TryPartitionHamiltonianPath(route, arrowCount, 0, arrows, rng, ref searchBudget))
            return null;

        return arrows.Count == arrowCount ? arrows : null;
    }

    private static bool TryPartitionHamiltonianPath(
        List<Vector2Int> route,
        int arrowCount,
        int start,
        List<GeneratedArrow> result,
        System.Random rng,
        ref int searchBudget)
    {
        if (searchBudget-- <= 0)
            return false;

        int totalCells = route.Count;
        int arrowsRemaining = arrowCount - result.Count;
        int cellsRemaining = totalCells - start;

        if (arrowsRemaining <= 0 || cellsRemaining < arrowsRemaining * 3)
            return false;

        // The final arrow ends at the end of the Hamiltonian route. Its head
        // must have a straight approach segment; geometry validation below also
        // checks that its escape ray never runs back into its own body.
        if (arrowsRemaining == 1)
        {
            if (cellsRemaining < 3)
                return false;

            Vector2Int lastDirection = route[totalCells - 1] - route[totalCells - 2];
            Vector2Int previousDirection = route[totalCells - 2] - route[totalCells - 3];
            if (lastDirection != previousDirection)
                return false;

            GeneratedArrow last = new GeneratedArrow();
            for (int i = start; i < totalCells; i++)
                last.path.Add(route[i]);

            result.Add(last);
            return true;
        }

        int minimumCut = start + 3;
        int maximumCut = totalCells - ((arrowsRemaining - 1) * 3);
        if (minimumCut > maximumCut)
            return false;

        List<int> validCuts = new List<int>();
        for (int cut = minimumCut; cut <= maximumCut; cut++)
        {
            // Keep the arrowhead's last two body steps straight, and continue
            // straight into the next arrow so the head does not point back into
            // its own body or start at a sharp kink.
            Vector2Int a = route[cut - 2] - route[cut - 3];
            Vector2Int b = route[cut - 1] - route[cut - 2];
            Vector2Int c = route[cut] - route[cut - 1];

            // A valid head ray continues into a later arrow, and must not pass
            // through any cell owned by this arrow or an earlier arrow. This
            // makes dependencies point only toward later path segments, so the
            // puzzle has a guaranteed reverse-order solution when the final
            // arrow exits the board.
            if (a == b && b == c && !HeadRayHitsRoutePrefix(route, cut))
                validCuts.Add(cut);
        }

        if (validCuts.Count == 0)
            return false;

        // Randomize the preferred length each recursion. Unlike the old greedy
        // cut, failed choices backtrack and try other boundaries, creating more
        // varied arrow lengths and greatly reducing seed-dependent failures.
        int averageLength = Mathf.Max(3, cellsRemaining / arrowsRemaining);
        int variation = Mathf.Max(2, averageLength / 3);
        int desiredLength = averageLength + rng.Next(-variation, variation + 1);
        int maxAllowedLength = cellsRemaining - ((arrowsRemaining - 1) * 3);
        desiredLength = Mathf.Clamp(desiredLength, 3, maxAllowedLength);

        Shuffle(validCuts, rng);
        validCuts.Sort((left, right) =>
            Mathf.Abs((left - start) - desiredLength).CompareTo(
                Mathf.Abs((right - start) - desiredLength)));

        for (int cutIndex = 0; cutIndex < validCuts.Count; cutIndex++)
        {
            if (searchBudget <= 0)
                return false;

            int cut = validCuts[cutIndex];
            GeneratedArrow arrow = new GeneratedArrow();
            for (int i = start; i < cut; i++)
                arrow.path.Add(route[i]);

            result.Add(arrow);
            if (TryPartitionHamiltonianPath(route, arrowCount, cut, result, rng, ref searchBudget))
                return true;

            result.RemoveAt(result.Count - 1);
        }

        return false;
    }

    private static List<Vector2Int> BuildRandomizedHamiltonianPath(
        int width,
        int height,
        System.Random rng)
    {
        if (width < 2 || height < 2)
            return null;

        List<Vector2Int> cells = BuildFullBoardSnake(width, height, rng);
        if (cells == null || cells.Count != width * height)
            return null;

        // Backbite transformations: connect one endpoint to a non-adjacent
        // neighbour already on the path, then reverse the intervening segment.
        // This preserves a Hamiltonian path but produces loops and varied turns.
        // Keep an index table so endpoint-neighbour lookups are O(1), even on
        // large boards such as 48x48. The table is updated only for each
        // reversed segment rather than scanning the full route every move.
        Dictionary<Vector2Int, int> routeIndices = new Dictionary<Vector2Int, int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
            routeIndices[cells[i]] = i;

        int moves = Mathf.Max(100, cells.Count * 2);
        for (int step = 0; step < moves; step++)
        {
            bool useFront = rng.Next(0, 2) == 0;
            int endpointIndex = useFront ? 0 : cells.Count - 1;
            Vector2Int endpoint = cells[endpointIndex];
            List<Vector2Int> candidates = new List<Vector2Int>(4);

            for (int d = 0; d < Directions.Length; d++)
            {
                Vector2Int next = endpoint + Directions[d];
                if (Inside(next, width, height))
                    candidates.Add(next);
            }
            Shuffle(candidates, rng);

            for (int c = 0; c < candidates.Count; c++)
            {
                if (!routeIndices.TryGetValue(candidates[c], out int neighborIndex))
                    continue;

                if (useFront)
                {
                    if (neighborIndex <= 1)
                        continue;
                    ReverseRangeAndUpdateIndices(cells, 0, neighborIndex - 1, routeIndices);
                    if (!HasStraightStart(cells))
                    {
                        // Undo this backbite: both route endpoints must retain
                        // a straight lead-in so they can become arrowheads.
                        ReverseRangeAndUpdateIndices(cells, 0, neighborIndex - 1, routeIndices);
                        continue;
                    }
                    break;
                }
                else
                {
                    if (neighborIndex >= cells.Count - 2)
                        continue;
                    ReverseRangeAndUpdateIndices(cells, neighborIndex + 1, cells.Count - 1, routeIndices);
                    if (!HasStraightEnd(cells) || !HasOutwardEnd(cells, width, height))
                    {
                        ReverseRangeAndUpdateIndices(cells, neighborIndex + 1, cells.Count - 1, routeIndices);
                        continue;
                    }
                    break;
                }
            }
        }

        if (rng.Next(0, 2) == 0)
        {
            cells.Reverse();
            if (!HasOutwardEnd(cells, width, height))
                cells.Reverse();
        }

        if (!HasStraightEnd(cells) || !HasOutwardEnd(cells, width, height))
            return null;

        return cells;
    }

    private static bool HasStraightStart(List<Vector2Int> path)
    {
        if (path == null || path.Count < 3)
            return false;
        return path[1] - path[0] == path[2] - path[1];
    }

    private static bool HasStraightEnd(List<Vector2Int> path)
    {
        if (path == null || path.Count < 3)
            return false;
        int last = path.Count - 1;
        return path[last] - path[last - 1] == path[last - 1] - path[last - 2];
    }

    private static bool HasOutwardEnd(List<Vector2Int> path, int width, int height)
    {
        if (path == null || path.Count < 2)
            return false;

        int last = path.Count - 1;
        Vector2Int head = path[last];
        Vector2Int direction = path[last] - path[last - 1];
        return (head.x == 0 && direction == Vector2Int.left) ||
               (head.x == width - 1 && direction == Vector2Int.right) ||
               (head.y == 0 && direction == Vector2Int.down) ||
               (head.y == height - 1 && direction == Vector2Int.up);
    }

    private static bool HeadRayHitsRoutePrefix(List<Vector2Int> route, int cut)
    {
        // Head is route[cut - 1]; the route prefix includes this arrow and all
        // earlier arrows. Future cells at route[cut] and beyond are allowed.
        Vector2Int head = route[cut - 1];
        Vector2Int direction = route[cut - 1] - route[cut - 2];

        for (int i = 0; i < cut - 1; i++)
        {
            Vector2Int cell = route[i];
            if (direction.x > 0 && cell.y == head.y && cell.x > head.x)
                return true;
            if (direction.x < 0 && cell.y == head.y && cell.x < head.x)
                return true;
            if (direction.y > 0 && cell.x == head.x && cell.y > head.y)
                return true;
            if (direction.y < 0 && cell.x == head.x && cell.y < head.y)
                return true;
        }

        return false;
    }

    private static void ReverseRangeAndUpdateIndices(
        List<Vector2Int> list,
        int left,
        int right,
        Dictionary<Vector2Int, int> indices)
    {
        ReverseRange(list, left, right);
        for (int i = left; i <= right; i++)
            indices[list[i]] = i;
    }

    private static void ReverseRange(List<Vector2Int> list, int left, int right)
    {
        while (left < right)
        {
            Vector2Int temp = list[left];
            list[left] = list[right];
            list[right] = temp;
            left++;
            right--;
        }
    }

    private static List<Vector2Int> BuildFullBoardSnake(
        int width,
        int height,
        System.Random rng)
    {
        List<Vector2Int> cells = new List<Vector2Int>(width * height);
        bool verticalSnake = rng.Next(0, 2) == 0;

        if (!verticalSnake)
        {
            List<int> rowOrder = new List<int>();
            for (int y = 0; y < height; y++) rowOrder.Add(y);
            // Keep adjacent rows ordered so the base route remains Hamiltonian.
            for (int y = 0; y < height; y++)
            {
                if ((y & 1) == 0)
                    for (int x = 0; x < width; x++) cells.Add(new Vector2Int(x, y));
                else
                    for (int x = width - 1; x >= 0; x--) cells.Add(new Vector2Int(x, y));
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                if ((x & 1) == 0)
                    for (int y = 0; y < height; y++) cells.Add(new Vector2Int(x, y));
                else
                    for (int y = height - 1; y >= 0; y--) cells.Add(new Vector2Int(x, y));
            }
        }

        if (rng.Next(0, 2) == 0)
            cells.Reverse();
        return cells;
    }

    private static List<GeneratedArrow> GenerateDependencyLevel(
        int width,
        int height,
        int arrowCount,
        int minLength,
        int maxLength,
        int minTurns,
        int chainRoots,
        int targetCells,
        int headClearance,
        System.Random rng)
    {
        List<GeneratedArrow> result =
            new List<GeneratedArrow>();

        HashSet<Vector2Int> occupied =
            new HashSet<Vector2Int>();

        int[] chainSizes =
            BuildChainSizes(
                arrowCount,
                chainRoots,
                rng
            );

        int currentTargetCells = 0;

        for (
            int chainIndex = 0;
            chainIndex < chainSizes.Length;
            chainIndex++
        )
        {
            int chainLength =
                chainSizes[chainIndex];

            GeneratedArrow root =
                TryCreateRootArrow(
                    width,
                    height,
                    occupied,
                    minLength,
                    maxLength,
                    minTurns,
                    headClearance,
                    result,
                    rng
                );

            if (root == null)
                return null;

            AddArrow(
                result,
                occupied,
                root
            );

            currentTargetCells +=
                root.path.Count;

            for (
                int position = 1;
                position < chainLength;
                position++
            )
            {
                int remainingArrows =
                    arrowCount -
                    result.Count;

                int remainingCells =
                    Mathf.Max(
                        0,
                        targetCells -
                        currentTargetCells
                    );

                int desiredLength =
                    CalculateDesiredLength(
                        remainingCells,
                        remainingArrows,
                        minLength,
                        maxLength,
                        rng
                    );

                GeneratedArrow blocked =
                    TryCreateBlockedArrow(
                        width,
                        height,
                        occupied,
                        result,
                        result[result.Count - 1],
                        desiredLength,
                        minLength,
                        maxLength,
                        minTurns,
                        headClearance,
                        rng
                    );

                if (blocked == null)
                    return null;

                AddArrow(
                    result,
                    occupied,
                    blocked
                );

                currentTargetCells +=
                    blocked.path.Count;
            }
        }

        if (
            result.Count !=
            arrowCount
        )
        {
            return null;
        }

        if (targetCells >= width * height)
        {
            // Full-board mode is all-or-nothing: no unassigned dots.
            if (currentTargetCells != width * height)
                return null;
        }
        else if (
            currentTargetCells <
            Mathf.RoundToInt(
                targetCells * 0.85f
            )
        )
        {
            return null;
        }

        return result;
    }

    private static int[] BuildChainSizes(
        int arrowCount,
        int chainRoots,
        System.Random rng)
    {
        int[] sizes =
            new int[chainRoots];

        for (
            int i = 0;
            i < chainRoots;
            i++
        )
        {
            sizes[i] = 1;
        }

        int remaining =
            arrowCount -
            chainRoots;

        while (remaining > 0)
        {
            List<int> possible =
                new List<int>();

            for (
                int i = 0;
                i < sizes.Length;
                i++
            )
            {
                possible.Add(i);
            }

            Shuffle(
                possible,
                rng
            );

            int index =
                possible[0];

            sizes[index]++;
            remaining--;
        }

        return sizes;
    }

    private static int CalculateDesiredLength(
        int remainingCells,
        int remainingArrows,
        int minLength,
        int maxLength,
        System.Random rng)
    {
        if (remainingArrows <= 0)
            return minLength;

        if (remainingArrows == 1)
        {
            // Use every remaining cell on the final arrow in full-board mode.
            if (remainingCells >= minLength && remainingCells <= maxLength)
                return remainingCells;
        }

        int average =
            Mathf.CeilToInt(
                remainingCells /
                (float)remainingArrows
            );

        int desiredMin =
            Mathf.Max(
                minLength,
                average - 2
            );

        int desiredMax =
            Mathf.Min(
                maxLength,
                average + 2
            );

        if (desiredMax < desiredMin)
            desiredMax =
                desiredMin;

        return rng.Next(
            desiredMin,
            desiredMax + 1
        );
    }

    private static GeneratedArrow TryCreateRootArrow(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        int minLength,
        int maxLength,
        int minTurns,
        int headClearance,
        List<GeneratedArrow> existing,
        System.Random rng)
    {
        for (
            int attempt = 0;
            attempt < CandidateAttempts;
            attempt++
        )
        {
            int desiredLength =
                rng.Next(
                    minLength,
                    maxLength + 1
                );

            GeneratedArrow arrow =
                TryBuildFreePath(
                    width,
                    height,
                    occupied,
                    desiredLength,
                    minTurns,
                    rng
                );

            if (arrow == null)
                continue;

            if (
                !HeadIsClear(
                    arrow,
                    existing,
                    headClearance
                )
            )
            {
                continue;
            }

            // Root must have a genuinely clear exit path at creation time.
            return arrow;
        }

        return null;
    }

    private static GeneratedArrow TryCreateBlockedArrow(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        List<GeneratedArrow> existing,
        GeneratedArrow blocker,
        int desiredLength,
        int minLength,
        int maxLength,
        int minTurns,
        int headClearance,
        System.Random rng)
    {
        if (blocker == null)
            return null;

        for (
            int attempt = 0;
            attempt < CandidateAttempts;
            attempt++
        )
        {
            List<Vector2Int> blockerCells =
                new List<Vector2Int>(
                    blocker.path
                );

            Shuffle(
                blockerCells,
                rng
            );

            for (
                int targetIndex = 0;
                targetIndex < blockerCells.Count;
                targetIndex++
            )
            {
                Vector2Int target =
                    blockerCells[targetIndex];

                List<Vector2Int> directions =
                    new List<Vector2Int>(
                        Directions
                    );

                Shuffle(
                    directions,
                    rng
                );

                for (
                    int directionIndex = 0;
                    directionIndex < directions.Count;
                    directionIndex++
                )
                {
                    Vector2Int direction =
                        directions[directionIndex];

                    int gap =
                        rng.Next(
                            1,
                            3
                        );

                    Vector2Int head =
                        target -
                        direction *
                        gap;

                    if (
                        !Inside(
                            head,
                            width,
                            height
                        ) ||
                        occupied.Contains(head)
                    )
                    {
                        continue;
                    }

                    Vector2Int predecessor =
                        head -
                        direction;

                    if (
                        !Inside(
                            predecessor,
                            width,
                            height
                        ) ||
                        occupied.Contains(
                            predecessor
                        )
                    )
                    {
                        continue;
                    }

                    GeneratedArrow arrow =
                        TryBuildPathEndingAtHead(
                            width,
                            height,
                            occupied,
                            head,
                            direction,
                            desiredLength,
                            minLength,
                            minTurns,
                            rng
                        );

                    if (arrow == null)
                        continue;

                    if (
                        !HeadIsClear(
                            arrow,
                            existing,
                            headClearance
                        )
                    )
                    {
                        continue;
                    }

                    /*
                     * Reject an accidental cycle or dependency inversion.
                     * The whole set is small (<=63), so solving this candidate
                     * is cheap and guarantees the newly created dependency
                     * doesn't make the level deadlocked.
                     */
                    List<GeneratedArrow> test =
                        new List<GeneratedArrow>(
                            existing
                        );

                    test.Add(
                        arrow
                    );

                    SolverResult check =
                        SolveLevel(
                            test,
                            width,
                            height
                        );

                    if (
                        test.Count > 1 &&
                        !check.solvable
                    )
                    {
                        continue;
                    }

                    return arrow;
                }
            }
        }

        return null;
    }

    private static GeneratedArrow TryBuildFreePath(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        int desiredLength,
        int minTurns,
        System.Random rng)
    {
        for (
            int attempt = 0;
            attempt < CandidateAttempts;
            attempt++
        )
        {
            Vector2Int start =
                new Vector2Int(
                    rng.Next(width),
                    rng.Next(height)
                );

            if (occupied.Contains(start))
                continue;

            List<Vector2Int> path =
                BuildRandomPath(
                    width,
                    height,
                    occupied,
                    start,
                    desiredLength,
                    minTurns,
                    rng
                );

            if (
                path != null &&
                path.Count >= desiredLength
            )
            {
                return new GeneratedArrow
                {
                    path = path
                };
            }
        }

        return null;
    }

    private static GeneratedArrow TryBuildPathEndingAtHead(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        Vector2Int head,
        Vector2Int headDirection,
        int desiredLength,
        int minLength,
        int minTurns,
        System.Random rng)
    {
        Vector2Int predecessor =
            head -
            headDirection;

        if (
            !Inside(
                predecessor,
                width,
                height
            ) ||
            occupied.Contains(predecessor)
        )
        {
            return null;
        }

        for (
            int attempt = 0;
            attempt < CandidateAttempts;
            attempt++
        )
        {
            List<Vector2Int> reversed =
                new List<Vector2Int>
                {
                    head,
                    predecessor
                };

            HashSet<Vector2Int> local =
                new HashSet<Vector2Int>
                {
                    head,
                    predecessor
                };

            Vector2Int previousDirection =
                -headDirection;

            int turns = 0;

            while (
                reversed.Count <
                desiredLength
            )
            {
                Vector2Int current =
                    reversed[
                        reversed.Count - 1
                    ];

                List<Vector2Int> choices =
                    new List<Vector2Int>(
                        Directions
                    );

                Shuffle(
                    choices,
                    rng
                );

                if (
                    previousDirection !=
                    Vector2Int.zero &&
                    rng.NextDouble() <
                    0.55
                )
                {
                    choices.Remove(
                        previousDirection
                    );

                    choices.Insert(
                        0,
                        previousDirection
                    );
                }

                bool added = false;

                for (
                    int i = 0;
                    i < choices.Count;
                    i++
                )
                {
                    Vector2Int next =
                        current -
                        choices[i];

                    if (
                        !Inside(
                            next,
                            width,
                            height
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        occupied.Contains(
                            next
                        ) ||
                        local.Contains(
                            next
                        )
                    )
                    {
                        continue;
                    }

                    Vector2Int move =
                        next -
                        current;

                    if (
                        previousDirection !=
                        Vector2Int.zero &&
                        move !=
                        previousDirection
                    )
                    {
                        turns++;
                    }

                    local.Add(
                        next
                    );

                    reversed.Add(
                        next
                    );

                    previousDirection =
                        move;

                    added = true;
                    break;
                }

                if (!added)
                    break;
            }

            if (
                reversed.Count <
                minLength
            )
            {
                continue;
            }

            if (
                turns <
                minTurns
            )
            {
                continue;
            }

            List<Vector2Int> path =
                new List<Vector2Int>(
                    reversed
                );

            path.Reverse();

            Vector2Int actualHeadDelta =
                path[path.Count - 1] -
                path[path.Count - 2];

            if (
                actualHeadDelta !=
                headDirection
            )
            {
                continue;
            }

            return new GeneratedArrow
            {
                path = path
            };
        }

        return null;
    }

    private static List<Vector2Int> BuildRandomPath(
        int width,
        int height,
        HashSet<Vector2Int> occupied,
        Vector2Int start,
        int desiredLength,
        int minTurns,
        System.Random rng)
    {
        List<Vector2Int> path =
            new List<Vector2Int>
            {
                start
            };

        HashSet<Vector2Int> local =
            new HashSet<Vector2Int>
            {
                start
            };

        Vector2Int previousDirection =
            Vector2Int.zero;

        int turns = 0;

        while (
            path.Count <
            desiredLength
        )
        {
            Vector2Int current =
                path[path.Count - 1];

            List<Vector2Int> choices =
                new List<Vector2Int>(
                    Directions
                );

            Shuffle(
                choices,
                rng
            );

            if (
                previousDirection !=
                Vector2Int.zero &&
                rng.NextDouble() <
                0.55
            )
            {
                choices.Remove(
                    previousDirection
                );

                choices.Insert(
                    0,
                    previousDirection
                );
            }

            bool added = false;

            for (
                int i = 0;
                i < choices.Count;
                i++
            )
            {
                Vector2Int next =
                    current +
                    choices[i];

                if (
                    !Inside(
                        next,
                        width,
                        height
                    )
                )
                {
                    continue;
                }

                if (
                    occupied.Contains(
                        next
                    ) ||
                    local.Contains(
                        next
                    )
                )
                {
                    continue;
                }

                Vector2Int move =
                    next -
                    current;

                if (
                    previousDirection !=
                    Vector2Int.zero &&
                    move !=
                    previousDirection
                )
                {
                    turns++;
                }

                local.Add(
                    next
                );

                path.Add(
                    next
                );

                previousDirection =
                    move;

                added = true;
                break;
            }

            if (!added)
                break;
        }

        if (
            path.Count <
            desiredLength
        )
        {
            return null;
        }

        if (turns < minTurns)
            return null;

        return path;
    }

    private static void AddArrow(
        List<GeneratedArrow> result,
        HashSet<Vector2Int> occupied,
        GeneratedArrow arrow)
    {
        result.Add(
            arrow
        );

        for (
            int i = 0;
            i < arrow.path.Count;
            i++
        )
        {
            occupied.Add(
                arrow.path[i]
            );
        }
    }

    private static bool HeadIsClear(
        GeneratedArrow arrow,
        List<GeneratedArrow> existing,
        int clearance)
    {
        if (clearance <= 0)
            return true;

        Vector2Int head =
            arrow.path[
                arrow.path.Count - 1
            ];

        for (
            int i = 0;
            i < existing.Count;
            i++
        )
        {
            Vector2Int otherHead =
                existing[i].path[
                    existing[i].path.Count - 1
                ];

            int distance =
                Mathf.Max(
                    Mathf.Abs(
                        head.x -
                        otherHead.x
                    ),
                    Mathf.Abs(
                        head.y -
                        otherHead.y
                    )
                );

            if (distance < clearance)
                return false;
        }

        return true;
    }

    private static bool ValidateCandidateGeometry(
        List<GeneratedArrow> arrows,
        int width,
        int height,
        int headClearance)
    {
        if (
            arrows == null ||
            arrows.Count == 0
        )
        {
            return false;
        }

        Dictionary<Vector2Int, int> owners =
            new Dictionary<Vector2Int, int>();

        for (
            int arrowIndex = 0;
            arrowIndex < arrows.Count;
            arrowIndex++
        )
        {
            List<Vector2Int> path =
                arrows[arrowIndex].path;

            if (
                path == null ||
                path.Count < 3
            )
            {
                return false;
            }

            // Keep the last two body segments straight into the arrowhead.
            Vector2Int finalDirection =
                path[path.Count - 1] - path[path.Count - 2];
            Vector2Int previousFinalDirection =
                path[path.Count - 2] - path[path.Count - 3];

            if (finalDirection != previousFinalDirection)
                return false;

            HashSet<Vector2Int> local =
                new HashSet<Vector2Int>();

            for (
                int i = 0;
                i < path.Count;
                i++
            )
            {
                Vector2Int cell =
                    path[i];

                if (
                    !Inside(
                        cell,
                        width,
                        height
                    )
                )
                {
                    return false;
                }

                if (!local.Add(cell))
                    return false;

                if (
                    owners.ContainsKey(
                        cell
                    )
                )
                {
                    Debug.LogError(
                        "LevelGenerator: Shared grid cell " +
                        cell +
                        " detected."
                    );

                    return false;
                }

                owners.Add(
                    cell,
                    arrowIndex
                );
            }

            for (
                int i = 0;
                i < path.Count - 1;
                i++
            )
            {
                Vector2Int delta =
                    path[i + 1] -
                    path[i];

                if (
                    Mathf.Abs(delta.x) +
                    Mathf.Abs(delta.y) != 1
                )
                {
                    return false;
                }
            }

            // The head must never escape through its own body, even if a long
            // winding path later returns to the same row or column.
            Vector2Int head = path[path.Count - 1];
            Vector2Int headDirection = finalDirection;
            Vector2Int rayCell = head + headDirection;
            while (Inside(rayCell, width, height))
            {
                if (local.Contains(rayCell))
                    return false;
                rayCell += headDirection;
            }
        }

        // Heads themselves must remain separated enough to be unambiguous.
        if (headClearance > 0)
        {
            for (
                int a = 0;
                a < arrows.Count;
                a++
            )
            {
                Vector2Int headA =
                    arrows[a].path[
                        arrows[a].path.Count - 1
                    ];

                for (
                    int b = a + 1;
                    b < arrows.Count;
                    b++
                )
                {
                    Vector2Int headB =
                        arrows[b].path[
                            arrows[b].path.Count - 1
                        ];

                    int distance =
                        Mathf.Max(
                            Mathf.Abs(
                                headA.x -
                                headB.x
                            ),
                            Mathf.Abs(
                                headA.y -
                                headB.y
                            )
                        );

                    if (
                        distance <
                        headClearance
                    )
                    {
                        return false;
                    }
                }
            }
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

        int count =
            arrows.Count;

        if (
            count <= 0 ||
            count > 63
        )
        {
            return result;
        }

        ulong[] blockers =
            BuildBlockMasks(
                arrows,
                width,
                height
            );

        ulong fullMask =
            (1UL << count) -
            1UL;

        HashSet<ulong> visited =
            new HashSet<ulong>();

        List<int> path =
            new List<int>();

        result.initialSafe =
            CountSafe(
                blockers,
                fullMask
            );

        int maxSafe =
            result.initialSafe;

        bool found =
            SearchSolution(
                blockers,
                fullMask,
                visited,
                path,
                ref maxSafe
            );

        result.solvable =
            found;

        result.maximumSafe =
            maxSafe;

        for (
            int i = 0;
            i < blockers.Length;
            i++
        )
        {
            result.totalDependencies +=
                CountBits(
                    blockers[i]
                );
        }

        if (found)
        {
            result.order.AddRange(
                path
            );
        }

        return result;
    }

    private static ulong[] BuildBlockMasks(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        ulong[] masks =
            new ulong[
                arrows.Count
            ];

        Dictionary<Vector2Int, ulong> owners =
            new Dictionary<Vector2Int, ulong>();

        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            ulong bit =
                1UL << i;

            for (
                int p = 0;
                p < arrows[i].path.Count;
                p++
            )
            {
                Vector2Int cell =
                    arrows[i].path[p];

                if (!owners.ContainsKey(cell))
                {
                    owners.Add(
                        cell,
                        bit
                    );
                }
            }
        }

        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            List<Vector2Int> path =
                arrows[i].path;

            Vector2Int head =
                path[path.Count - 1];

            Vector2Int direction =
                head -
                path[path.Count - 2];

            Vector2Int check =
                head +
                direction;

            ulong mask =
                0UL;

            while (
                Inside(
                    check,
                    width,
                    height
                )
            )
            {
                if (
                    owners.TryGetValue(
                        check,
                        out ulong ownerBit
                    )
                )
                {
                    mask |=
                        ownerBit;
                }

                check +=
                    direction;
            }

            mask &=
                ~(1UL << i);

            masks[i] =
                mask;
        }

        return masks;
    }

    private static int CountBlockedArrows(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        ulong[] masks =
            BuildBlockMasks(
                arrows,
                width,
                height
            );

        int count = 0;

        for (
            int i = 0;
            i < masks.Length;
            i++
        )
        {
            if (masks[i] != 0UL)
                count++;
        }

        return count;
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
            CountSafe(
                blockers,
                remaining
            );

        maximumSafe =
            Mathf.Max(
                maximumSafe,
                safeCount
            );

        List<int> safe =
            new List<int>();

        for (
            int i = 0;
            i < blockers.Length;
            i++
        )
        {
            ulong bit =
                1UL << i;

            if (
                (remaining & bit) ==
                0UL
            )
            {
                continue;
            }

            if (
                (blockers[i] &
                remaining) ==
                0UL
            )
            {
                safe.Add(
                    i
                );
            }
        }

        safe.Sort(
            (a, b) =>
                CountInteractions(
                    blockers[b],
                    remaining
                ) -
                CountInteractions(
                    blockers[a],
                    remaining
                )
        );

        for (
            int i = 0;
            i < safe.Count;
            i++
        )
        {
            int arrow =
                safe[i];

            ulong next =
                remaining &
                ~(1UL << arrow);

            path.Add(
                arrow
            );

            if (
                SearchSolution(
                    blockers,
                    next,
                    visited,
                    path,
                    ref maximumSafe
                )
            )
            {
                return true;
            }

            path.RemoveAt(
                path.Count - 1
            );
        }

        return false;
    }

    private static int CountSafe(
        ulong[] blockers,
        ulong remaining)
    {
        int count = 0;

        for (
            int i = 0;
            i < blockers.Length;
            i++
        )
        {
            ulong bit =
                1UL << i;

            if (
                (remaining & bit) ==
                0UL
            )
            {
                continue;
            }

            if (
                (blockers[i] &
                remaining) ==
                0UL
            )
            {
                count++;
            }
        }

        return count;
    }

    private static int CountInteractions(
        ulong mask,
        ulong remaining)
    {
        return CountBits(
            mask &
            remaining
        );
    }

    private static int CountBits(
        ulong value)
    {
        int count = 0;

        while (value != 0UL)
        {
            value &=
                value - 1UL;

            count++;
        }

        return count;
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

        for (
            int i = 0;
            i < generated.Count;
            i++
        )
        {
            List<Vector2Int> path =
                new List<Vector2Int>(
                    generated[i].path
                );

            Vector2Int delta =
                path[path.Count - 1] -
                path[path.Count - 2];

            level.arrows.Add(
                new ArrowData
                {
                    path = path,
                    headDirection =
                        DirectionFromDelta(
                            delta
                        ),
                    headSprite =
                        level.defaultHeadSprite,
                    arrowColor =
                        level.defaultArrowColor
                }
            );
        }

        EditorUtility.SetDirty(
            level
        );

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

    private static float GetCoverage(
        List<GeneratedArrow> arrows,
        int totalCells)
    {
        int count = 0;

        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            count +=
                arrows[i].path.Count;
        }

        return totalCells > 0
            ? count /
              (float)totalCells
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
        for (
            int i = list.Count - 1;
            i > 0;
            i--
        )
        {
            int j =
                rng.Next(
                    i + 1
                );

            T temp =
                list[i];

            list[i] =
                list[j];

            list[j] =
                temp;
        }
    }

    private static void ValidateSavedLevel(
        LevelData level)
    {
        if (level == null)
            return;

        List<GeneratedArrow> saved =
            new List<GeneratedArrow>();

        if (level.arrows != null)
        {
            for (
                int i = 0;
                i < level.arrows.Count;
                i++
            )
            {
                ArrowData data =
                    level.arrows[i];

                if (
                    data == null ||
                    data.path == null
                )
                {
                    Debug.LogError(
                        "LevelGenerator: Saved arrow " +
                        i +
                        " is invalid."
                    );

                    return;
                }

                saved.Add(
                    new GeneratedArrow
                    {
                        path =
                            new List<Vector2Int>(
                                data.path
                            )
                    }
                );
            }
        }

        if (
            !ValidateCandidateGeometry(
                saved,
                Mathf.Max(
                    4,
                    level.width
                ),
                Mathf.Max(
                    4,
                    level.height
                ),
                level.headClearanceCells
            )
        )
        {
            Debug.LogError(
                "LevelGenerator: SAVED LEVEL IS INVALID. " +
                "At least one path is outside the board, repeated, shared, or has bad steps."
            );

            return;
        }

        SolverResult result =
            SolveLevel(
                saved,
                Mathf.Max(
                    4,
                    level.width
                ),
                Mathf.Max(
                    4,
                    level.height
                )
            );

        Debug.Log(
            "LevelGenerator: SAVED LEVEL IS VALID. " +
            "Arrows=" +
            saved.Count +
            ", Solvable=" +
            result.solvable +
            ", InitialSafe=" +
            result.initialSafe +
            ", Dependencies=" +
            result.totalDependencies
        );
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
                minLength = 3;
                maxLength = 6;
                minTurns = 0;
                maxSafe = 3;
                initialMin = 1;
                initialMax = 3;
                coverage = 0.55f;
                blockedRatio = 0.70f;
                break;

            case ArrowLevelDifficulty.Hard:
                minLength = 4;
                maxLength = 8;
                minTurns = 1;
                maxSafe = 2;
                initialMin = 1;
                initialMax = 2;
                coverage = 0.68f;
                blockedRatio = 0.80f;
                break;

            case ArrowLevelDifficulty.Expert:
                minLength = 4;
                maxLength = 9;
                minTurns = 1;
                maxSafe = 2;
                initialMin = 1;
                initialMax = 2;
                coverage = 0.72f;
                blockedRatio = 0.85f;
                break;

            default:
                minLength = 3;
                maxLength = 7;
                minTurns = 1;
                maxSafe = 3;
                initialMin = 1;
                initialMax = 3;
                coverage = 0.62f;
                blockedRatio = 0.75f;
                break;
        }
    }
}
