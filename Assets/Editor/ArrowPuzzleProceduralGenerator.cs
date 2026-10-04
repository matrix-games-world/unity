#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ArrowPuzzleProceduralGenerator
{
    public enum GenerationDifficulty
    {
        Easy,
        Medium,
        Hard,
        Expert,
        Mixed
    }

    [Serializable]
    public sealed class GeneratedArrow
    {
        public List<Vector2Int> path = new List<Vector2Int>();
        public ArrowPathController.Direction headDirection;
        public int blockerCount;
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
        public float coverage;
        public int initialSafe;
        public int totalDependencies;
        public List<GeneratedArrow> arrows =
            new List<GeneratedArrow>();
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private struct OrientationOption
    {
        public List<Vector2Int> path;
        public ArrowPathController.Direction headDirection;
        public int blockers;
        public bool selfClear;
    }

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
        width = Mathf.Clamp(width, 4, 48);
        height = Mathf.Clamp(height, 4, 48);
        minArrows = Mathf.Clamp(minArrows, 2, 63);
        int maxPhysical =
            Mathf.Max(
                minArrows,
                Mathf.Min(
                    63,
                    (width * height) / 3
                )
            );

        maxArrows =
            Mathf.Clamp(
                maxArrows,
                minArrows,
                maxPhysical
            );

        count = Mathf.Clamp(count, 1, 200);

        List<GeneratedLevel> results =
            new List<GeneratedLevel>(count);

        HashSet<string> signatures =
            new HashSet<string>();

        int seedCursor =
            baseSeed == 0
                ? Environment.TickCount
                : baseSeed;

        for (int index = 0; index < count; index++)
        {
            bool added = false;

            for (int attempt = 0; attempt < 18 && !added; attempt++)
            {
                int seed =
                    unchecked(
                        seedCursor +
                        (index + 1) * 7919 +
                        attempt * 104729
                    );

                System.Random rng =
                    new System.Random(seed);

                int arrowCount =
                    rng.Next(
                        minArrows,
                        maxArrows + 1
                    );

                GenerationDifficulty difficulty =
                    difficultyMode ==
                    GenerationDifficulty.Mixed
                        ? PickMixedDifficulty(rng)
                        : difficultyMode;

                GeneratedLevel candidate =
                    GenerateOne(
                        width,
                        height,
                        arrowCount,
                        difficulty,
                        seed,
                        spacing,
                        rng
                    );

                if (candidate == null)
                    continue;

                string signature =
                    BuildSignature(candidate);

                if (!signatures.Add(signature))
                    continue;

                results.Add(candidate);
                added = true;
            }
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
        width = Mathf.Max(4, width);
        height = Mathf.Max(4, height);
        arrowCount =
            Mathf.Clamp(
                arrowCount,
                2,
                Mathf.Min(
                    63,
                    (width * height) / 3
                )
            );

        if (arrowCount * 3 > width * height)
            return null;

        // Build several independent random Hamiltonian paths. Every accepted
        // path covers every grid cell exactly once.
        GeneratedLevel best =
            null;

        float bestScore =
            float.NegativeInfinity;

        const int pathAttempts = 8;

        for (
            int pathAttempt = 0;
            pathAttempt < pathAttempts;
            pathAttempt++
        )
        {
            List<Vector2Int> fullPath =
                BuildRandomHamiltonianPath(
                    width,
                    height,
                    rng
                );

            if (
                fullPath == null ||
                fullPath.Count != width * height
            )
            {
                continue;
            }

            // A handful of partitions/orientation attempts gives strong visual
            // variety while remaining far cheaper than nested brute-force search.
            const int partitionAttempts = 30;

            for (
                int partitionAttempt = 0;
                partitionAttempt < partitionAttempts;
                partitionAttempt++
            )
            {
                List<List<Vector2Int>> segments =
                    BuildRandomSegments(
                        fullPath,
                        arrowCount,
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

                if (!IsAcyclic(
                        oriented,
                        width,
                        height
                    ))
                {
                    continue;
                }

                GetDifficultyStats(
                    oriented,
                    width,
                    height,
                    out int initialSafe,
                    out int totalDependencies
                );

                float score =
                    ScoreDifficulty(
                        difficulty,
                        initialSafe,
                        totalDependencies,
                        oriented.Count,
                        rng
                    );

                float randomness =
                    ComputeIrregularityScore(
                        oriented
                    );

                score +=
                    randomness * 2.5f;

                if (
                    best == null ||
                    score > bestScore
                )
                {
                    bestScore = score;

                    best =
                        new GeneratedLevel
                        {
                            seed = seed,
                            width = width,
                            height = height,
                            arrowCount = oriented.Count,
                            spacing = spacing,
                            difficulty = difficulty,
                            coverage = 1f,
                            initialSafe = initialSafe,
                            totalDependencies = totalDependencies,
                            arrows = oriented
                        };
                }
            }
        }

        return best;
    }

    private static List<Vector2Int>
        BuildRandomHamiltonianPath(
            int width,
            int height,
            System.Random rng)
    {
        int total =
            width * height;

        List<Vector2Int> starts =
            new List<Vector2Int>();

        // Random starts first produce different layouts. Corner starts are
        // reliable fallbacks for the grid Hamiltonian search.
        for (int i = 0; i < 8; i++)
        {
            starts.Add(
                new Vector2Int(
                    rng.Next(width),
                    rng.Next(height)
                )
            );
        }

        starts.Add(Vector2Int.zero);
        starts.Add(
            new Vector2Int(
                width - 1,
                0
            )
        );
        starts.Add(
            new Vector2Int(
                0,
                height - 1
            )
        );
        starts.Add(
            new Vector2Int(
                width - 1,
                height - 1
            )
        );

        Shuffle(
            starts,
            rng
        );

        int nodeBudget =
            Mathf.Clamp(
                total * 1800,
                30000,
                800000
            );

        for (
            int s = 0;
            s < starts.Count;
            s++
        )
        {
            List<Vector2Int> path =
                new List<Vector2Int>(total);

            HashSet<Vector2Int> visited =
                new HashSet<Vector2Int>();

            path.Add(starts[s]);
            visited.Add(starts[s]);

            int nodesVisited = 0;

            if (SearchHamiltonian(
                    starts[s],
                    width,
                    height,
                    visited,
                    path,
                    Vector2Int.zero,
                    rng,
                    ref nodesVisited,
                    nodeBudget))
            {
                return path;
            }
        }

        // Guaranteed valid fallback. The normal path is only used if the
        // randomized DFS exhausts its bounded search budget.
        return BuildSnake(width, height, rng);
    }

    private static bool SearchHamiltonian(
        Vector2Int current,
        int width,
        int height,
        HashSet<Vector2Int> visited,
        List<Vector2Int> path,
        Vector2Int previousDirection,
        System.Random rng,
        ref int nodesVisited,
        int nodeBudget)
    {
        nodesVisited++;

        if (nodesVisited > nodeBudget)
            return false;

        if (path.Count == width * height)
            return true;

        List<Vector2Int> options =
            new List<Vector2Int>(4);

        for (
            int i = 0;
            i < Directions.Length;
            i++
        )
        {
            Vector2Int next =
                current + Directions[i];

            if (
                Inside(
                    next,
                    width,
                    height
                ) &&
                !visited.Contains(next)
            )
            {
                options.Add(next);
            }
        }

        // Low onward degree prevents isolated cells; random tie-break plus a
        // turn bonus keeps the result from degenerating into a snake.
        options.Sort(
            (a, b) =>
            {
                int degreeA =
                    CountFreeNeighbours(
                        a,
                        width,
                        height,
                        visited
                    );

                int degreeB =
                    CountFreeNeighbours(
                        b,
                        width,
                        height,
                        visited
                    );

                bool turnA =
                    previousDirection != Vector2Int.zero &&
                    a - current != previousDirection;

                bool turnB =
                    previousDirection != Vector2Int.zero &&
                    b - current != previousDirection;

                int scoreA =
                    degreeA * 20 +
                    (turnA ? -4 : 0) +
                    rng.Next(0, 5);

                int scoreB =
                    degreeB * 20 +
                    (turnB ? -4 : 0) +
                    rng.Next(0, 5);

                return scoreA.CompareTo(scoreB);
            }
        );

        for (
            int i = 0;
            i < options.Count;
            i++
        )
        {
            Vector2Int next =
                options[i];

            visited.Add(next);
            path.Add(next);

            if (SearchHamiltonian(
                    next,
                    width,
                    height,
                    visited,
                    path,
                    next - current,
                    rng,
                    ref nodesVisited,
                    nodeBudget))
            {
                return true;
            }

            path.RemoveAt(
                path.Count - 1
            );

            visited.Remove(next);
        }

        return false;
    }

    private static List<List<Vector2Int>>
        BuildRandomSegments(
            List<Vector2Int> fullPath,
            int arrowCount,
            System.Random rng)
    {
        int total =
            fullPath.Count;

        if (arrowCount * 3 > total)
            return null;

        int remainingCells =
            total -
            arrowCount * 3;

        List<int> lengths =
            new List<int>(arrowCount);

        for (
            int i = 0;
            i < arrowCount - 1;
            i++
        )
        {
            int remainingArrows =
                arrowCount - i;

            int maxExtra =
                remainingCells;

            int minKeep =
                Mathf.Max(
                    0,
                    remainingArrows - 1
                );

            int extra =
                rng.Next(
                    0,
                    maxExtra + 1
                );

            // Keep long irregular arrows common without forcing equal lengths.
            if (
                remainingCells > 12 &&
                rng.NextDouble() < 0.38
            )
            {
                int boosted =
                    Mathf.Min(
                        remainingCells,
                        rng.Next(
                            3,
                            Mathf.Max(
                                4,
                                remainingCells / 3
                            ) + 1
                        )
                    );

                extra =
                    Mathf.Max(
                        extra,
                        boosted
                    );
            }

            // Never consume the cells reserved for the remaining arrows.
            int maximumAllowed =
                Mathf.Max(
                    0,
                    remainingCells -
                    (remainingArrows - 1)
                );

            extra =
                Mathf.Min(
                    extra,
                    maximumAllowed
                );

            lengths.Add(
                3 + extra
            );

            remainingCells -= extra;
        }

        lengths.Add(
            3 + remainingCells
        );

        // If the random composition is overly lopsided, mix a few segment
        // lengths while preserving the exact total.
        for (
            int pass = 0;
            pass < 4;
            pass++
        )
        {
            int a =
                rng.Next(lengths.Count);

            int b =
                rng.Next(lengths.Count);

            if (a == b)
                continue;

            int transfer =
                Mathf.Clamp(
                    (lengths[a] - 3) / 2,
                    0,
                    6
                );

            if (transfer <= 0)
                continue;

            lengths[a] -= transfer;
            lengths[b] += transfer;
        }

        List<List<Vector2Int>> result =
            new List<List<Vector2Int>>(
                arrowCount
            );

        int cursor = 0;

        for (
            int i = 0;
            i < lengths.Count;
            i++
        )
        {
            int length =
                lengths[i];

            if (
                length < 3 ||
                cursor + length >
                fullPath.Count
            )
            {
                return null;
            }

            result.Add(
                fullPath.GetRange(
                    cursor,
                    length
                )
            );

            cursor += length;
        }

        if (cursor != fullPath.Count)
            return null;

        // Shuffle only the segment identities. Cells remain exactly once.
        Shuffle(result, rng);

        return result;
    }

    private static List<GeneratedArrow>
        FindSolvableOrientation(
            List<List<Vector2Int>> segments,
            int width,
            int height,
            GenerationDifficulty difficulty,
            System.Random rng)
    {
        int n =
            segments.Count;

        int[,] owner =
            new int[
                width,
                height
            ];

        for (
            int i = 0;
            i < n;
            i++
        )
        {
            for (
                int p = 0;
                p < segments[i].Count;
                p++
            )
            {
                Vector2Int cell =
                    segments[i][p];

                owner[
                    cell.x,
                    cell.y
                ] = i + 1;
            }
        }

        // The removal construction below guarantees a solvable orientation:
        // at each stage choose one remaining arrow orientation whose head ray
        // is clear of remaining arrows. Removing it can only make later
        // arrows easier to clear.
        List<GeneratedArrow> result =
            new List<GeneratedArrow>(n);

        Dictionary<int, GeneratedArrow> chosen =
            new Dictionary<int, GeneratedArrow>();

        HashSet<int> remaining =
            new HashSet<int>();

        for (int i = 0; i < n; i++)
            remaining.Add(i);

        while (remaining.Count > 0)
        {
            List<GeneratedArrow> safeChoices =
                new List<GeneratedArrow>();

            foreach (int index in remaining)
            {
                OrientationOption normal =
                    BuildOrientationOption(
                        segments[index],
                        false,
                        index,
                        owner,
                        remaining,
                        width,
                        height
                    );

                OrientationOption reversed =
                    BuildOrientationOption(
                        segments[index],
                        true,
                        index,
                        owner,
                        remaining,
                        width,
                        height
                    );

                AddIfSafe(
                    safeChoices,
                    normal,
                    rng
                );

                AddIfSafe(
                    safeChoices,
                    reversed,
                    rng
                );
            }

            if (safeChoices.Count == 0)
                return null;

            GeneratedArrow picked;

            // Harder difficulties prefer a later removal which keeps the
            // remaining set constrained. Easy prefers the opposite.
            if (difficulty == GenerationDifficulty.Easy)
            {
                picked =
                    PickChoice(
                        safeChoices,
                        false,
                        rng
                    );
            }
            else if (
                difficulty ==
                GenerationDifficulty.Expert
            )
            {
                picked =
                    PickChoice(
                        safeChoices,
                        true,
                        rng
                    );
            }
            else
            {
                picked =
                    safeChoices[
                        rng.Next(
                            safeChoices.Count
                        )
                    ];
            }

            int pickedIndex =
                FindSegmentIndex(
                    segments,
                    picked.path,
                    chosen,
                    remaining
                );

            if (pickedIndex < 0)
                return null;

            chosen[
                pickedIndex
            ] = picked;

            remaining.Remove(
                pickedIndex
            );

            result.Add(
                picked
            );
        }

        // Shuffle final arrow order. The level is still solvable because the
        // blocker graph is independent of the serialized list order.
        Shuffle(
            result,
            rng
        );

        return result;
    }

    private static int FindSegmentIndex(
        List<List<Vector2Int>> segments,
        List<Vector2Int> pickedPath,
        Dictionary<int, GeneratedArrow> chosen,
        HashSet<int> remaining)
    {
        for (
            int i = 0;
            i < segments.Count;
            i++
        )
        {
            if (!remaining.Contains(i))
                continue;

            List<Vector2Int> a =
                segments[i];

            if (
                a.Count !=
                pickedPath.Count
            )
            {
                continue;
            }

            bool same =
                true;

            for (
                int p = 0;
                p < a.Count;
                p++
            )
            {
                if (a[p] != pickedPath[p])
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return i;

            // Reversed pick.
            same = true;

            for (
                int p = 0;
                p < a.Count;
                p++
            )
            {
                if (
                    a[p] !=
                    pickedPath[
                        pickedPath.Count -
                        1 -
                        p
                    ]
                )
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return i;
        }

        return -1;
    }

    private static void AddIfSafe(
        List<GeneratedArrow> safeChoices,
        OrientationOption option,
        System.Random rng)
    {
        if (
            !option.selfClear ||
            option.blockers != 0
        )
        {
            return;
        }

        safeChoices.Add(
            new GeneratedArrow
            {
                path =
                    new List<Vector2Int>(
                        option.path
                    ),
                headDirection =
                    option.headDirection,
                blockerCount = 0
            }
        );
    }

    private static GeneratedArrow PickChoice(
        List<GeneratedArrow> choices,
        bool preferDifficulty,
        System.Random rng)
    {
        if (choices.Count == 1)
            return choices[0];

        // With a safe choice, randomness mainly controls which removable arrow
        // is selected; the resulting orientation set stays guaranteed solvable.
        int index =
            preferDifficulty
                ? rng.Next(
                    0,
                    choices.Count
                )
                : rng.Next(
                    0,
                    choices.Count
                );

        return choices[index];
    }

    private static OrientationOption
        BuildOrientationOption(
            List<Vector2Int> source,
            bool reverse,
            int segmentIndex,
            int[,] owner,
            HashSet<int> remaining,
            int width,
            int height)
    {
        List<Vector2Int> path =
            new List<Vector2Int>(source);

        if (reverse)
            path.Reverse();

        Vector2Int head =
            path[path.Count - 1];

        Vector2Int step =
            head -
            path[path.Count - 2];

        Vector2Int check =
            head +
            step;

        bool selfClear = true;
        int blockers = 0;
        HashSet<int> seenBlockers =
            new HashSet<int>();

        while (
            Inside(
                check,
                width,
                height
            )
        )
        {
            int ownerId =
                owner[
                    check.x,
                    check.y
                ];

            if (ownerId == segmentIndex + 1)
            {
                selfClear = false;
                break;
            }

            if (
                ownerId > 0 &&
                remaining.Contains(
                    ownerId - 1
                )
            )
            {
                seenBlockers.Add(
                    ownerId - 1
                );
            }

            check += step;
        }

        blockers =
            seenBlockers.Count;

        return new OrientationOption
        {
            path = path,
            headDirection =
                ToDirection(step),
            blockers = blockers,
            selfClear = selfClear
        };
    }

    private static bool ValidateFullCoverage(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        bool[,] seen =
            new bool[
                width,
                height
            ];

        int count = 0;

        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            List<Vector2Int> path =
                arrows[i].path;

            if (
                path == null ||
                path.Count < 3
            )
            {
                return false;
            }

            HashSet<Vector2Int> local =
                new HashSet<Vector2Int>();

            for (
                int p = 0;
                p < path.Count;
                p++
            )
            {
                Vector2Int cell =
                    path[p];

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

                if (seen[cell.x, cell.y])
                    return false;

                seen[cell.x, cell.y] = true;
                count++;
            }

            if (!SelfRayClear(
                    path,
                    width,
                    height
                ))
            {
                return false;
            }
        }

        return count ==
            width * height;
    }

    private static bool SelfRayClear(
        List<Vector2Int> path,
        int width,
        int height)
    {
        Vector2Int head =
            path[path.Count - 1];

        Vector2Int step =
            head -
            path[path.Count - 2];

        Vector2Int check =
            head +
            step;

        HashSet<Vector2Int> own =
            new HashSet<Vector2Int>(
                path
            );

        while (
            Inside(
                check,
                width,
                height
            )
        )
        {
            if (own.Contains(check))
                return false;

            check += step;
        }

        return true;
    }

    private static void GetDifficultyStats(
        List<GeneratedArrow> arrows,
        int width,
        int height,
        out int initialSafe,
        out int totalDependencies)
    {
        initialSafe = 0;
        totalDependencies = 0;

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

            Vector2Int step =
                head -
                path[path.Count - 2];

            Vector2Int check =
                head +
                step;

            HashSet<int> blockers =
                new HashSet<int>();

            while (
                Inside(
                    check,
                    width,
                    height
                )
            )
            {
                int owner =
                    FindOwner(
                        arrows,
                        check
                    );

                if (owner >= 0 && owner != i)
                    blockers.Add(owner);

                check += step;
            }

            if (blockers.Count == 0)
                initialSafe++;

            totalDependencies +=
                blockers.Count;
        }
    }

    private static bool IsAcyclic(
        List<GeneratedArrow> arrows,
        int width,
        int height)
    {
        int n = arrows.Count;
        List<HashSet<int>> graph =
            new List<HashSet<int>>(n);

        for (int i = 0; i < n; i++)
            graph.Add(new HashSet<int>());

        for (int i = 0; i < n; i++)
        {
            List<Vector2Int> path =
                arrows[i].path;

            Vector2Int head =
                path[path.Count - 1];

            Vector2Int step =
                head -
                path[path.Count - 2];

            Vector2Int check =
                head + step;

            while (
                Inside(
                    check,
                    width,
                    height
                )
            )
            {
                int owner =
                    FindOwner(
                        arrows,
                        check
                    );

                if (owner >= 0 && owner != i)
                    graph[i].Add(owner);

                check += step;
            }
        }

        int[] indegree =
            new int[n];

        for (int i = 0; i < n; i++)
        {
            foreach (int j in graph[i])
                indegree[j]++;
        }

        Queue<int> queue =
            new Queue<int>();

        for (int i = 0; i < n; i++)
        {
            if (indegree[i] == 0)
                queue.Enqueue(i);
        }

        int visited = 0;

        while (queue.Count > 0)
        {
            int current =
                queue.Dequeue();

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

    private static int FindOwner(
        List<GeneratedArrow> arrows,
        Vector2Int cell)
    {
        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            if (
                arrows[i].path.Contains(
                    cell
                )
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static float
        ComputeIrregularityScore(
            List<GeneratedArrow> arrows)
    {
        float score = 0f;

        for (
            int i = 0;
            i < arrows.Count;
            i++
        )
        {
            List<Vector2Int> path =
                arrows[i].path;

            if (path.Count < 3)
                continue;

            int turns = 0;

            for (
                int p = 1;
                p < path.Count - 1;
                p++
            )
            {
                Vector2Int d1 =
                    path[p] -
                    path[p - 1];

                Vector2Int d2 =
                    path[p + 1] -
                    path[p];

                if (d1 != d2)
                    turns++;
            }

            score +=
                turns /
                (float)Mathf.Max(
                    1,
                    path.Count - 2
                );
        }

        return
            arrows.Count > 0
                ? score / arrows.Count
                : 0f;
    }

    private static float ScoreDifficulty(
        GenerationDifficulty difficulty,
        int initialSafe,
        int totalDependencies,
        int arrowCount,
        System.Random rng)
    {
        float safe =
            initialSafe /
            (float)Mathf.Max(
                1,
                arrowCount
            );

        float deps =
            totalDependencies /
            (float)Mathf.Max(
                1,
                arrowCount * 2
            );

        float score =
            (float)(rng.NextDouble() * 3f);

        switch (difficulty)
        {
            case GenerationDifficulty.Easy:
                score +=
                    (1f - safe) * -8f;
                score +=
                    deps * -2f;
                break;

            case GenerationDifficulty.Medium:
                score +=
                    -Mathf.Abs(
                        safe - 0.30f
                    ) * 10f;
                score +=
                    -Mathf.Abs(
                        deps - 0.65f
                    ) * 4f;
                break;

            case GenerationDifficulty.Hard:
                score +=
                    (1f - safe) * 8f;
                score +=
                    deps * 3f;
                break;

            case GenerationDifficulty.Expert:
                score +=
                    (1f - safe) * 14f;
                score +=
                    deps * 5f;
                break;

            case GenerationDifficulty.Mixed:
                score +=
                    deps * 1.5f;
                break;
        }

        return score;
    }

    private static GenerationDifficulty
        PickMixedDifficulty(
            System.Random rng)
    {
        int roll =
            rng.Next(0, 100);

        if (roll < 25)
            return GenerationDifficulty.Easy;

        if (roll < 50)
            return GenerationDifficulty.Medium;

        if (roll < 78)
            return GenerationDifficulty.Hard;

        return GenerationDifficulty.Expert;
    }

    private static ArrowPathController.Direction
        ToDirection(Vector2Int delta)
    {
        if (delta == Vector2Int.right)
            return ArrowPathController.Direction.Right;

        if (delta == Vector2Int.left)
            return ArrowPathController.Direction.Left;

        if (delta == Vector2Int.up)
            return ArrowPathController.Direction.Up;

        return ArrowPathController.Direction.Down;
    }

    private static bool Inside(
        Vector2Int cell,
        int width,
        int height)
    {
        return
            cell.x >= 0 &&
            cell.x < width &&
            cell.y >= 0 &&
            cell.y < height;
    }

    private static int CountFreeNeighbours(
        Vector2Int cell,
        int width,
        int height,
        HashSet<Vector2Int> visited)
    {
        int count = 0;

        for (
            int i = 0;
            i < Directions.Length;
            i++
        )
        {
            Vector2Int next =
                cell + Directions[i];

            if (
                Inside(
                    next,
                    width,
                    height
                ) &&
                !visited.Contains(next)
            )
            {
                count++;
            }
        }

        return count;
    }

    private static List<Vector2Int>
        BuildSnake(
            int width,
            int height,
            System.Random rng)
    {
        List<Vector2Int> path =
            new List<Vector2Int>(
                width * height
            );

        bool vertical =
            rng.Next(0, 2) == 0;

        if (!vertical)
        {
            for (
                int y = 0;
                y < height;
                y++
            )
            {
                if (y % 2 == 0)
                {
                    for (
                        int x = 0;
                        x < width;
                        x++
                    )
                    {
                        path.Add(
                            new Vector2Int(
                                x,
                                y
                            )
                        );
                    }
                }
                else
                {
                    for (
                        int x = width - 1;
                        x >= 0;
                        x--
                    )
                    {
                        path.Add(
                            new Vector2Int(
                                x,
                                y
                            )
                        );
                    }
                }
            }
        }
        else
        {
            for (
                int x = 0;
                x < width;
                x++
            )
            {
                if (x % 2 == 0)
                {
                    for (
                        int y = 0;
                        y < height;
                        y++
                    )
                    {
                        path.Add(
                            new Vector2Int(
                                x,
                                y
                            )
                        );
                    }
                }
                else
                {
                    for (
                        int y = height - 1;
                        y >= 0;
                        y--
                    )
                    {
                        path.Add(
                            new Vector2Int(
                                x,
                                y
                            )
                        );
                    }
                }
            }
        }

        if (rng.Next(0, 2) == 0)
            path.Reverse();

        return path;
    }

    private static string BuildSignature(
        GeneratedLevel level)
    {
        System.Text.StringBuilder sb =
            new System.Text.StringBuilder();

        sb.Append(level.width)
          .Append('x')
          .Append(level.height)
          .Append('|');

        for (
            int i = 0;
            i < level.arrows.Count;
            i++
        )
        {
            List<Vector2Int> path =
                level.arrows[i].path;

            sb.Append(path.Count)
              .Append(':');

            for (
                int p = 0;
                p < path.Count;
                p++
            )
            {
                sb.Append(path[p].x)
                  .Append(',')
                  .Append(path[p].y)
                  .Append(';');
            }

            sb.Append('|');
        }

        return sb.ToString();
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
                rng.Next(i + 1);

            T temp =
                list[i];

            list[i] =
                list[j];

            list[j] =
                temp;
        }
    }
}
#endif
