# SignScramble Standalone Board Generator

This document explains the 4x4 board generation and two-step training process implemented in `BoardGeneratorStandalone.cs`. Use this as context for future LLM queries about how boards are created, scored, and how displayed letters are chosen to minimize alternate valid words.

**Overview**
- **Goal:** produce 4x4 Boggle-style boards that contain a set of target words while completely minimizing the number of alternate valid words (must be 0) guessable by players when only some letters of each target word are revealed.
- **Two training phases:**
  - `TrainingStep1`: find complete board layouts that contain all target words (coverage).
  - `TrainingStep2`: iteratively mutate boards and set which letters of each word are displayed to reduce the count of alternate words anchored by those displayed letters.

**Where to look in the code**
- Core implementation: [StandaloneDebug/BoardGeneratorStandalone.cs](StandaloneDebug/BoardGeneratorStandalone.cs)

Data structures and representations
- Board: a 4x4 grid represented internally as `char[4,4]` and externally as a 16-char string (row-major). The nested `Board` class includes constructors: empty, from string, and random weighted generation.
- Linear indexing: cells are frequently referenced as a single integer 0..15 (row*4 + col). `NeighborIndices` precomputes neighbors for each linear index.
- Bitmasks: 16-bit `ushort` masks represent sets of board cells. Per-word displayed-letter positions are stored as `ushort` masks where bit k corresponds to index k of the word.

Word selection and letter set
- `GetRandomSimilarWordSet(int count)` picks a small set of candidate words (default 5–6) from `Resources/fingerspellingwords.txt`.
  - Cleans words to letters only, uppercases them, removes long words (>6) and duplicates.
  - Uses a randomized search maximizing an average Jaccard similarity between words. It prefers word sets with average Jaccard within a target range (roughly 0.18–0.55), or otherwise returns the best-scored set after many attempts.
- The letter pool for board initialization is created by concatenating all target words (uppercased) with the full alphabet, then shuffling. Boards are seeded by sampling letters from this shuffled pool.

TrainingStep1 — build boards that contain the target words
- Objective: find boards for which each target word can be found (a path of adjacent cells matching the word), prioritizing boards that maximize coverage/score.
- Scoring:
  - `ScoreBoardOnWords` and `ScoreBoardOnWordsAndProtect` compute how many characters of each word can be formed (sum over words of min(foundLength, wordLength)).
  - `ScoreBoardOnWordsAndProtect` additionally marks cells used by any full-word path and can return a `protectedCells` bitmask.
- Search loop:
  - Repeatedly create `Board` candidates seeded from the shuffled letter pool.
  - Call `TrainingStep1` with a mutation loop (limit ~2000 iterations by default) that performs greedy hill-climbing with occasional acceptance of equal/less-good boards using randomness.
  - When `TryFindWordPath` finds complete paths for all words, the board is accepted.
- Mutation:
  - `Board.Mutate(ushort? protectedMask)` randomly replaces a cell with a letter sampled by frequency weights. When a `protectedMask` is supplied, mutation avoids flipping protected cells.
  - The code unions protected cells discovered from best paths to avoid destroying successful word coverage while exploring other cells.

TrainingStep2 — reduce alternate valid words given displayed letters
- Purpose: given a board that contains all target words, pick per-word displayed-letter masks (which indices of each word are shown to the player) and further mutate the board (respecting `protectedFromStep1`) so as to minimize the number of alternate valid words that match the displayed letters.
- Inputs: a board, the target words, an initial `displayedPositionsPerWord` list (often seeding with the rarest letter index per word), and `protectedFromStep1` mask.
- Scoring alternatives:
  - `ScoreBoardOnAlternatives` sums the number of alternate valid dictionary words that can be formed while matching the displayed letters for each target word. Lower is better; a score of 0 means no alternates.
  - Alternate discovery is implemented in `Board.FindAlternateWordsFromPosition`, which anchors on displayed letters and performs BFS-style path enumeration to assemble candidate words, filtering by displayed-letter positions and dictionary membership.
- Algorithm flow:
  - Keep a `currentDisplayedLetters` list of `ushort` per-word masks describing which letter indices are shown.
  - Repeatedly compute `ScoreBoardOnAlternatives`. If the score is not zero, attempt board mutations (avoiding `protectedFromStep1`) to lower the score.
  - If the search times out for the current revealed-letter counts, reveal one additional letter for the next word that still has undisplayed letters (using `Board.FindKthRarestLetter` to choose the next reveal index). This increases anchor information to reduce alternates.
  - Repeat until alternatives reach zero or all letters for all words are revealed (the code throws if it times out after revealing all letters).

Desired specifications (explicit)
- Many valid boards: be able to run the program and generate many independent valid boards that meet the constraints rather than a single filtered set.
- Per-board active word sets: each generated board may present a different subset of the candidate words as "active" (i.e., those preserved for alternate-word scoring). The generator should treat each successful board as an independent result and not compute an intersection of active words across boards.
- Minimum displayed letters: for each active word on a board, display the minimal number of letter indices needed to reach zero alternates (prefer fewer reveals; prefer revealing rare letters first).
- No alternatives: for every active word reported for a board, the alternate-word count anchored on its displayed letters should be zero.

Recommended output / API change
- Current signature: `Tuple<string[], bool[][][]> GenerateBoards(int numberOfBoards)` — returns an array of board strings and a per-board-per-original-word boolean array.
- Recommended: return a per-board structure so each board carries its own active word list and displayed-mask alignment. Example structure:
  - `class GeneratedBoard { string BoardString; string[] ActiveWords; bool[][] DisplayedMasks; }`
  - `List<GeneratedBoard> GenerateBoards(int numberOfBoards)`

This avoids throwing away valid boards just because they do not share the same active-word subset.

Performance and robustness notes (short)
- `FindAlternateWordsFromPosition` is the most expensive inner operation (BFS per word + dictionary checks). Consider caching per-board+mask results or using a prefix trie to prune candidate exploration.
- Replace exception-throwing control flow in `TrainingStep2` (e.g., when all letters become displayed or no eligible reveal exists) with a clean failure return so the outer `GenerateBoards` loop can treat the word set as failed and continue trying additional sets.
- Make the random seed and iteration budgets configurable to support reproducible bulk generation and easier tuning.

Pathfinding and alternative-word search
- `FindBestWordPath`, `TryFindWordPath`: BFS-style searches over linear indices with a bitmask of used cells to locate paths matching a word; `FindBestWordPath` returns the best/longest partial path if a full path is not found.
- `FindAlternateWordsFromPosition` enumerates connected paths that contain the displayed letters at the same indices as the target word. It generates candidate words by growing a deque from the chosen seed displayed index, verifies displayed-letter constraints, consults the `DictionaryManager.IsValidWord`, and counts unique alternates.

Outputs and API
- `GenerateBoards(int numberOfBoards)` is the high-level entry used by the standalone runner:
  - Repeatedly selects candidate word sets and runs the two training steps until `numberOfBoards` completed optimized boards are produced.
  - Returns `Tuple<string[], bool[][][]>`:
    - `string[]`: array of board strings (16-char row-major).
    - `bool[][][]`: per-board per-word boolean arrays indicating which letter indices are displayed (true = shown).
  - `LastUsedWordSet` records the word set that produced the returned boards.

Reproducibility and parameters
- Randomness: a static `Random` instance is used (`Random.Next`, `Random.NextDouble`) across board generation, word-set selection, and mutations. Re-running with the same seed is not currently supported in the standalone code (no seed injection), so results vary across runs.
- Tunable limits visible in code:
  - Word-set search attempts and Jaccard thresholds in `GetRandomSimilarWordSet` (attempts = 2000, target range ~0.18–0.55).
  - `TrainingStep1` iteration limit (default 2000) and `training1Limit` in `GenerateBoards` (dynamic: Math.Max(500, 400 * words.Length)).
  - `TrainingStep2` per-display-set limit (default Math.Max(100, 150 * words.Length)).

Helpers and utilities
- `FormatMasks` functions produce human-readable mask strings for logging.
- `GetDisplayedCharactersFromMask` extracts characters shown given a word and a `ushort` mask.

Notes for LLM prompts
- If you ask an LLM to modify behavior, reference functions by name (e.g., `TrainingStep1`, `TrainingStep2`, `FindAlternateWordsFromPosition`).
- To change randomness reproducibility, request adding a `Random` seed parameter to `BoardGenerator` and making the `Random` instance non-static or injectable.
- To alter alternative-word strictness, modify `ScoreBoardOnAlternatives` or the reveal strategy (e.g., reveal multiple letters at once or change the rarity criterion in `FindKthRarestLetter`).

Suggestions for experimentations
- Add an optional `seed` argument to `BoardGenerator` so runs are deterministic for tuning.
- Expose key limits (iteration budgets, Jaccard thresholds) as constructor parameters to make automated tuning possible.
- Add detailed metrics output (per-iteration best score, alternate-count histogram) to analyze generator performance across many runs.

Contact
- See the generator implementation at [StandaloneDebug/BoardGeneratorStandalone.cs](StandaloneDebug/BoardGeneratorStandalone.cs) for code-level details.

---
Generated for use as context in future LLM queries about board generation.

**Performance update (2026-04-21)**

- The `altCache` used to memoize expensive alternate-word searches has been moved out of the per-attempt loop in `TrainingStep2` so cached `(board, word, mask)` results persist across restart attempts. This is a pure performance optimization (no logic change) and reduces repeated expensive BFS calls for the same queries.

- Optimization idea: preprocessing the dictionary into prefix/position-aware structures (trie, reversed trie, or per-length per-offset prefix sets) can greatly prune BFS expansion in `FindAlternateWordsFromPosition`.
  - A standard prefix trie or a HashSet of prefixes quickly answers "is there any word starting with this partial string?" and cuts branches early when searching from one end.
  - Because the current alternate-word BFS grows outward from an anchored pivot (two-sided expansion), you must account for substrings that start at arbitrary offsets. Practical approaches:
    - Keep two tries (forward and reversed) and validate the left-hand partial sequence against the reversed trie and the right-hand partial sequence against the forward trie; or
    - Precompute, per word-length and per start-index, a HashSet of allowed substrings/prefixes so you can check whether the currently-built contiguous substring at offset `s` is a prefix of any length-`L` word starting at `s`.
  - Trade-offs: preprocessing increases startup time and memory, but for typical moderate-sized dictionaries it dramatically reduces runtime BFS expansions and lowers the chance of hitting combinatorial expansion caps.

If you want, I can implement a simple per-length/offset prefix set in `DictionaryManager` and add the corresponding pruning checks in `FindAlternateWordsFromPosition` as a follow-up patch.
