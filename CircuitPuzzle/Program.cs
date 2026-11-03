using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CircuitPuzzle
{
    // ════════════════════════════════ MODEL ════════════════════════════════

    /// <summary>Kinds of components a circuit can contain.</summary>
    public enum CompKind { Battery, Switch, Not, Or, And, Xor, LedRed, LedBlue }

    /// <summary>
    /// A single circuit component: battery, switch, logic gate or LED.
    /// Components are created in topological order (inputs first), so a single
    /// forward pass of <see cref="Circuit.Simulate"/> evaluates every output.
    /// </summary>
    public class Comp
    {
        /// <summary>Creation order index; drives the visual top-to-bottom layout.</summary>
        public int Id;

        /// <summary>Type of the component.</summary>
        public CompKind Kind;

        /// <summary>Upstream components feeding this one (one entry per input pin).</summary>
        public readonly List<Comp> Inputs = new List<Comp>();

        /// <summary>Current output signal; for LEDs, whether the LED is lit.</summary>
        public bool Out;

        /// <summary>Switches only: user-controlled on/off state.</summary>
        public bool On;

        /// <summary>Logic column index (0 = battery column).</summary>
        public int Level;

        /// <summary>Box position on the canvas (top-left corner).</summary>
        public int Col, Row;

        /// <summary>Gets a value indicating whether this component is an LED.</summary>
        public bool IsLed { get { return Kind == CompKind.LedRed || Kind == CompKind.LedBlue; } }

        /// <summary>Gets a value indicating whether this component is a switch.</summary>
        public bool IsSwitch { get { return Kind == CompKind.Switch; } }
    }

    // ══════════════════════════ PURE GEOMETRY ══════════════════════════
    // Every box measure is a pure function of (Kind, input count): there is
    // no alignment state to maintain, so pin misalignment cannot recur.

    /// <summary>Derived box geometry shared by the renderer and the generator.</summary>
    public static class Geo
    {
        /// <summary>Width of every component box, in characters.</summary>
        public const int BoxW = 7;

        /// <summary>Height of a compact box (battery, switch, NOT, LED).</summary>
        public const int BoxH = 4;

        /// <summary>
        /// Returns the height in rows of the box of a component.
        /// </summary>
        /// <param name="kind">Component kind.</param>
        /// <param name="inputCount">Number of input pins.</param>
        /// <returns>Box height in rows.</returns>
        public static int BoxHeight(CompKind kind, int inputCount)
        {
            if (kind == CompKind.Or || kind == CompKind.And || kind == CompKind.Xor)
            {
                if (inputCount >= 4) return inputCount + 4;   // generic fallback, never generated
                return 6;                                     // 2 or 3 inputs
            }
            return BoxH;
        }

        /// <summary>
        /// Returns the row (relative to the box top) of the input pin with the given index.
        /// </summary>
        /// <param name="kind">Component kind.</param>
        /// <param name="index">Input pin index.</param>
        /// <param name="inputCount">Number of input pins.</param>
        /// <returns>Pin row relative to the box top.</returns>
        public static int InputPinOffset(CompKind kind, int index, int inputCount)
        {
            if (kind == CompKind.Or || kind == CompKind.And || kind == CompKind.Xor)
            {
                if (inputCount == 2) return index == 0 ? 2 : 4;   // symmetric pins
                if (inputCount >= 3) return 2 + index;
                return 3;                                         // fallback: center
            }
            return 2;                                             // compact box: single pin
        }

        /// <summary>
        /// Returns the row (relative to the box top) of the output pin.
        /// </summary>
        /// <param name="kind">Component kind.</param>
        /// <param name="inputCount">Number of input pins.</param>
        /// <returns>Output pin row, or -1 when the component has no output (LEDs).</returns>
        public static int OutputPinOffset(CompKind kind, int inputCount)
        {
            if (kind == CompKind.Or || kind == CompKind.And || kind == CompKind.Xor)
            {
                if (inputCount >= 4) return 2 + (inputCount - 1) / 2;
                return 3;                                         // center of a 6-row box
            }
            return (kind == CompKind.LedRed || kind == CompKind.LedBlue) ? -1 : 2;
        }
    }

    // ══════════════════════════ DIFFICULTY ══════════════════════════

    /// <summary>Difficulty levels; each one maps to a <see cref="DifficultySpec"/>.</summary>
    public enum Difficulty { Easy, Medium, Hard }

    /// <summary>
    /// Generation parameters of a difficulty. Component counts scale with the
    /// level, and <see cref="MinWinMoves"/> forbids solutions that are too
    /// short (in particular, one-switch solutions).
    /// </summary>
    internal sealed class DifficultySpec
    {
        /// <summary>Minimum number of switches.</summary>
        public readonly int MinSwitches;

        /// <summary>Maximum number of switches.</summary>
        public readonly int MaxSwitches;

        /// <summary>Minimum number of logic gates.</summary>
        public readonly int MinGates;

        /// <summary>Maximum number of logic gates.</summary>
        public readonly int MaxGates;

        /// <summary>Minimum number of red LEDs.</summary>
        public readonly int MinRedLeds;

        /// <summary>Maximum number of red LEDs.</summary>
        public readonly int MaxRedLeds;

        /// <summary>Minimum number of blue LEDs.</summary>
        public readonly int MinBlueLeds;

        /// <summary>Maximum number of blue LEDs.</summary>
        public readonly int MaxBlueLeds;

        /// <summary>Minimum number of switch flips required by any solution.</summary>
        public readonly int MinWinMoves;

        /// <summary>Initializes the specification with the given ranges (inclusive).</summary>
        /// <param name="minSwitches">Minimum switch count.</param>
        /// <param name="maxSwitches">Maximum switch count.</param>
        /// <param name="minGates">Minimum gate count.</param>
        /// <param name="maxGates">Maximum gate count.</param>
        /// <param name="minRedLeds">Minimum red LED count.</param>
        /// <param name="maxRedLeds">Maximum red LED count.</param>
        /// <param name="minBlueLeds">Minimum blue LED count.</param>
        /// <param name="maxBlueLeds">Maximum blue LED count.</param>
        /// <param name="minWinMoves">Minimum number of flips of any solution.</param>
        public DifficultySpec(int minSwitches, int maxSwitches, int minGates, int maxGates,
                              int minRedLeds, int maxRedLeds, int minBlueLeds, int maxBlueLeds,
                              int minWinMoves)
        {
            MinSwitches = minSwitches; MaxSwitches = maxSwitches;
            MinGates = minGates; MaxGates = maxGates;
            MinRedLeds = minRedLeds; MaxRedLeds = maxRedLeds;
            MinBlueLeds = minBlueLeds; MaxBlueLeds = maxBlueLeds;
            MinWinMoves = minWinMoves;
        }

        /// <summary>Easy: few components, few blue LEDs, solutions of 2+ moves.</summary>
        public static readonly DifficultySpec Easy = new DifficultySpec(3, 4, 2, 3, 1, 2, 0, 1, 2);

        /// <summary>Medium: more switches and gates, solutions of 3+ moves.</summary>
        public static readonly DifficultySpec Medium = new DifficultySpec(4, 5, 2, 4, 1, 3, 0, 2, 3);

        /// <summary>Hard: the largest circuits, more blue LEDs, solutions of 4+ moves.</summary>
        public static readonly DifficultySpec Hard = new DifficultySpec(4, 5, 3, 5, 2, 3, 1, 3, 4);

        /// <summary>Returns the specification of the given difficulty.</summary>
        /// <param name="difficulty">Difficulty level.</param>
        /// <returns>The matching specification.</returns>
        public static DifficultySpec For(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return Easy;
                case Difficulty.Hard: return Hard;
                default: return Medium;
            }
        }
    }

    /// <summary>
    /// A logic circuit: battery, switches, gates and LEDs, plus the generation
    /// and validation logic that guarantees fair, non-trivial puzzles.
    /// </summary>
    public class Circuit
    {
        /// <summary>All components in topological (creation) order.</summary>
        public readonly List<Comp> All = new List<Comp>();

        /// <summary>The switches, in creation order (S1, S2, ...).</summary>
        public readonly List<Comp> Switches = new List<Comp>();

        /// <summary>The logic gates.</summary>
        public readonly List<Comp> Gates = new List<Comp>();

        /// <summary>The LEDs.</summary>
        public readonly List<Comp> Leds = new List<Comp>();

        /// <summary>The battery (always the first component).</summary>
        public Comp Battery;

        /// <summary>Gets a value indicating whether every red LED is lit and no blue LED is.</summary>
        public bool Won
        {
            get
            {
                foreach (Comp l in Leds)
                {
                    if (l.Kind == CompKind.LedRed && !l.Out) return false;
                    if (l.Kind == CompKind.LedBlue && l.Out) return false;
                }
                return true;
            }
        }

        /// <summary>Gets a value indicating whether at least one blue LED is lit.</summary>
        public bool Lost
        {
            get { return Leds.Any(l => l.Kind == CompKind.LedBlue && l.Out); }
        }

        /// <summary>
        /// Recomputes every output in topological order. A switch passes the
        /// battery signal only when ON; gates evaluate their boolean kind; LEDs
        /// mirror their single input.
        /// </summary>
        public void Simulate()
        {
            foreach (Comp c in All)                       // All is in topological order
            {
                switch (c.Kind)
                {
                    case CompKind.Battery: c.Out = true; break;
                    case CompKind.Switch: c.Out = c.Inputs[0].Out && c.On; break;
                    case CompKind.Not: c.Out = !c.Inputs[0].Out; break;
                    case CompKind.Or: c.Out = c.Inputs.Any(i => i.Out); break;
                    case CompKind.And: c.Out = c.Inputs.All(i => i.Out); break;
                    case CompKind.Xor: c.Out = (c.Inputs.Count(i => i.Out) & 1) == 1; break;
                    default: c.Out = c.Inputs[0].Out; break;   // LED
                }
            }
        }

        // ─────────────── generation with solvability guarantees ───────────────

        /// <summary>
        /// Generates a medium-difficulty circuit from the given random source.
        /// </summary>
        /// <param name="rng">Random source; a fresh <c>new Random(seed)</c> always
        /// reproduces the same circuit for the same seed.</param>
        /// <returns>A validated circuit with all switches OFF.</returns>
        public static Circuit GenerateValid(Random rng)
        {
            return GenerateValid(rng, Difficulty.Medium);
        }

        /// <summary>
        /// Generates a validated circuit at the requested difficulty. Candidate
        /// circuits are rejected until one is fair: solvable without ever
        /// lighting a blue LED, neither solved nor lost at start, without
        /// solutions shorter than the difficulty minimum, and with no inert switch.
        /// </summary>
        /// <param name="rng">Random source.</param>
        /// <param name="difficulty">Requested difficulty.</param>
        /// <returns>A validated circuit with all switches OFF.</returns>
        /// <exception cref="InvalidOperationException">Thrown when generation
        /// fails after the maximum number of attempts.</exception>
        public static Circuit GenerateValid(Random rng, Difficulty difficulty)
        {
            DifficultySpec spec = DifficultySpec.For(difficulty);
            for (int attempt = 0; attempt < 100000; attempt++)
            {
                Circuit c = TryGenerate(rng, spec);
                if (c == null) continue;
                PruneNotInputs(c);    // defensive guard: a NOT gate owns exactly one input
                int minWin = Math.Min(spec.MinWinMoves, c.Switches.Count - 1);
                if (!Validate(c, minWin)) continue;
                SetSwitches(c, 0);    // every puzzle starts with all switches OFF
                c.Simulate();
                return c;
            }
            throw new InvalidOperationException("Generazione del circuito non riuscita.");
        }

        /// <summary>
        /// Defensive invariant: a NOT gate has exactly one input. Construction
        /// already guarantees it; the guard keeps the model safe even if the
        /// generation rules change in the future.
        /// </summary>
        /// <param name="c">Circuit to normalize in place.</param>
        private static void PruneNotInputs(Circuit c)
        {
            foreach (Comp g in c.Gates)
            {
                if (g.Kind != CompKind.Not) continue;
                while (g.Inputs.Count > 1) g.Inputs.RemoveAt(g.Inputs.Count - 1);
            }
        }

        /// <summary>
        /// Builds one random candidate circuit. A candidate can still be
        /// structurally rejected; semantic fairness is checked by
        /// <see cref="Validate"/>. Gates are accepted at creation time only if
        /// their truth table is non-constant and every input influences the
        /// output, so inert logic can never be born.
        /// </summary>
        /// <param name="rng">Random source.</param>
        /// <param name="spec">Difficulty parameters.</param>
        /// <returns>The candidate circuit, or null when it must be discarded.</returns>
        private static Circuit TryGenerate(Random rng, DifficultySpec spec)
        {
            int nSw = rng.Next(spec.MinSwitches, spec.MaxSwitches + 1);
            int nRed = rng.Next(spec.MinRedLeds, spec.MaxRedLeds + 1);
            int nBlue = rng.Next(spec.MinBlueLeds, spec.MaxBlueLeds + 1);
            int nGates = rng.Next(spec.MinGates, spec.MaxGates + 1);

            Circuit cir = new Circuit();
            int id = 0;
            // Every truth table has 2^nSw bits: the all-ones table is the
            // mask of all TABLE bits, not of all switches (a classic pitfall).
            int totalStates = 1 << nSw;
            ulong allTable = totalStates >= 64 ? ulong.MaxValue : (1ul << totalStates) - 1;
            Dictionary<Comp, ulong> sig = new Dictionary<Comp, ulong>();

            // 1) Battery (level 0).
            Comp batt = new Comp { Id = id++, Kind = CompKind.Battery, Level = 0 };
            cir.All.Add(batt);
            cir.Battery = batt;
            List<List<Comp>> levels = new List<List<Comp>> { new List<Comp> { batt }, new List<Comp>() };

            // 2) Switches (level 1), all fed by the battery.
            for (int i = 0; i < nSw; i++)
            {
                Comp s = new Comp { Id = id++, Kind = CompKind.Switch, Level = 1 };
                s.Inputs.Add(batt);
                cir.All.Add(s); cir.Switches.Add(s); levels[1].Add(s);
                sig[s] = SwitchSignature(i, nSw);
            }

            // 3) Gates: inputs are drawn from the level immediately above.
            //    A candidate gate is re-drawn until it passes the relevance
            //    checks, so no gate can ignore one of its own inputs.
            CompKind[] gateKinds = { CompKind.Not, CompKind.Or, CompKind.And, CompKind.Xor };
            for (int g = 0; g < nGates; g++)
            {
                int maxLvl = levels.Count - 1;
                int lvl = rng.Next(2, maxLvl + 2);        // 2..maxLvl+1
                List<Comp> pool = levels[lvl - 1];        // always exists and is never empty

                Comp gate = null;
                ulong gateSig = 0;
                for (int attempt = 0; attempt < 16 && gate == null; attempt++)
                {
                    CompKind kind = gateKinds[rng.Next(gateKinds.Length)];
                    int nIn;
                    if (kind == CompKind.Not || pool.Count < 2)
                    {
                        kind = CompKind.Not;              // a NOT has exactly one input
                        nIn = 1;
                    }
                    else
                    {
                        nIn = (pool.Count >= 3 && rng.Next(100) < 40) ? 3 : 2;
                    }

                    List<Comp> chosen = PickDistinct(pool, nIn, rng);
                    List<ulong> inSigs = chosen.Select(src => sig[src]).ToList();
                    ulong candidate = CombineSignature(kind, inSigs, allTable);

                    if (candidate == 0 || candidate == allTable) continue;           // constant output
                    if (!AllInputsInfluenceOutput(candidate, inSigs, nSw)) continue; // masked input

                    gate = new Comp { Id = id++, Kind = kind, Level = lvl };
                    gate.Inputs.AddRange(chosen);
                    gateSig = candidate;
                }
                if (gate == null) return null;            // pool exhausted: new candidate circuit

                cir.All.Add(gate); cir.Gates.Add(gate); sig[gate] = gateSig;
                while (levels.Count <= lvl) levels.Add(new List<Comp>());
                levels[lvl].Add(gate);
            }

            // 4) LEDs: fed by switches or gates; unused suppliers are preferred
            //    so that LEDs spread over the circuit instead of stacking up.
            List<CompKind> ledKinds = new List<CompKind>();
            for (int i = 0; i < nRed; i++) ledKinds.Add(CompKind.LedRed);
            for (int i = 0; i < nBlue; i++) ledKinds.Add(CompKind.LedBlue);
            for (int i = ledKinds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                CompKind t = ledKinds[i]; ledKinds[i] = ledKinds[j]; ledKinds[j] = t;
            }

            List<Comp> suppliers = cir.Switches.Cast<Comp>().Concat(cir.Gates).ToList();
            HashSet<Comp> used = new HashSet<Comp>();
            foreach (CompKind kind in ledKinds)
            {
                List<Comp> free = suppliers.Where(s => !used.Contains(s)).ToList();
                List<Comp> srcPool = free.Count > 0 ? free : suppliers;
                Comp src = srcPool[rng.Next(srcPool.Count)];

                Comp led = new Comp { Id = id++, Kind = kind, Level = src.Level + 1 };
                led.Inputs.Add(src);
                cir.All.Add(led); cir.Leds.Add(led); used.Add(src);
                while (levels.Count <= led.Level) levels.Add(new List<Comp>());
                levels[led.Level].Add(led);
            }

            // 5) Structural limits: the drawing must stay compact and readable.
            if (levels.Count > 8) return null;
            if (levels.Any(l => l.Count > 5)) return null;
            int estW = 0;
            for (int L = 0; L < levels.Count; L++)
            {
                int k = (L + 1 < levels.Count) ? levels[L + 1].Sum(n => n.Inputs.Count) : 0;
                estW += Geo.BoxW + k + 2;
            }
            if (estW > 100) return null;

            // 6) Every switch and gate must contribute to at least one LED.
            Dictionary<Comp, List<Comp>> consumers = new Dictionary<Comp, List<Comp>>();
            foreach (Comp c in cir.All) consumers[c] = new List<Comp>();
            foreach (Comp c in cir.All)
                foreach (Comp inp in c.Inputs)
                    consumers[inp].Add(c);

            foreach (Comp s in cir.Switches) if (!ReachesLed(s, consumers)) return null;
            foreach (Comp g in cir.Gates) if (!ReachesLed(g, consumers)) return null;

            return cir;
        }

        /// <summary>
        /// Draws 'count' distinct components from the pool with a Fisher-Yates
        /// shuffle on a copy.
        /// </summary>
        /// <param name="pool">Components to draw from.</param>
        /// <param name="count">How many distinct components to draw.</param>
        /// <param name="rng">Random source.</param>
        /// <returns>The drawn components.</returns>
        private static List<Comp> PickDistinct(List<Comp> pool, int count, Random rng)
        {
            List<Comp> shuffled = new List<Comp>(pool);
            for (int k = shuffled.Count - 1; k > 0; k--)
            {
                int kk = rng.Next(k + 1);
                Comp tmp = shuffled[k]; shuffled[k] = shuffled[kk]; shuffled[kk] = tmp;
            }
            return shuffled.Take(count).ToList();
        }

        /// <summary>
        /// Builds the truth table of a switch: bit 'm' is the switch value in
        /// the global combination 'm' (bit i of m = switch i).
        /// </summary>
        /// <param name="switchIndex">Index of the switch (0-based).</param>
        /// <param name="switchCount">Total number of switches.</param>
        /// <returns>A 2^switchCount-bit truth table packed in a 64-bit integer.</returns>
        private static ulong SwitchSignature(int switchIndex, int switchCount)
        {
            ulong sig = 0;
            int total = 1 << switchCount;
            for (int m = 0; m < total; m++)
                if (((m >> switchIndex) & 1) != 0) sig |= 1ul << m;
            return sig;
        }

        /// <summary>
        /// Combines the truth tables of the inputs into the truth table of a
        /// gate, applying the gate's boolean function to every combination.
        /// </summary>
        /// <param name="kind">Gate kind (Not, Or, And, Xor).</param>
        /// <param name="inputSigs">Truth tables of the inputs, one per pin.</param>
        /// <param name="allTable">Truth table with all its bits set.</param>
        /// <returns>The gate truth table.</returns>
        private static ulong CombineSignature(CompKind kind, List<ulong> inputSigs, ulong allTable)
        {
            if (kind == CompKind.Not) return allTable & ~inputSigs[0];
            ulong result = inputSigs[0];
            for (int i = 1; i < inputSigs.Count; i++)
            {
                switch (kind)
                {
                    case CompKind.Or: result |= inputSigs[i]; break;
                    case CompKind.And: result &= inputSigs[i]; break;
                    case CompKind.Xor: result ^= inputSigs[i]; break;
                }
            }
            return result;
        }

        /// <summary>
        /// Checks that every input of a gate influences its output: two
        /// combinations with identical values on the other inputs must be able
        /// to produce different outputs. This is the upstream fix that keeps
        /// dominated inputs (e.g. OR absorbed by a superset input) and twin
        /// signals (two NOTs of the same source into an XOR) out of the circuit.
        /// </summary>
        /// <param name="gateSig">Truth table of the gate.</param>
        /// <param name="inputSigs">Truth tables of the inputs, one per pin.</param>
        /// <param name="switchCount">Total number of switches.</param>
        /// <returns>True when no input is masked by the others.</returns>
        private static bool AllInputsInfluenceOutput(ulong gateSig, List<ulong> inputSigs, int switchCount)
        {
            for (int i = 0; i < inputSigs.Count; i++)
            {
                List<ulong> others = new List<ulong>(inputSigs);
                others.RemoveAt(i);
                if (!OutputVariesWithInput(gateSig, others, switchCount)) return false;
            }
            return true;
        }

        /// <summary>
        /// Checks whether the gate output takes both values while the given
        /// subset of inputs stays fixed, i.e. whether the remaining freedom
        /// (the input under test and its descendants) reaches the output.
        /// </summary>
        /// <param name="gateSig">Truth table of the gate.</param>
        /// <param name="otherSigs">Truth tables of the fixed inputs.</param>
        /// <param name="switchCount">Total number of switches.</param>
        /// <returns>True when both outputs occur for some fixed pattern.</returns>
        private static bool OutputVariesWithInput(ulong gateSig, List<ulong> otherSigs, int switchCount)
        {
            int total = 1 << switchCount;
            Dictionary<int, int> seen = new Dictionary<int, int>();   // fixed pattern -> outputs observed
            for (int m = 0; m < total; m++)
            {
                int key = 0;
                for (int j = 0; j < otherSigs.Count; j++)
                    if (((otherSigs[j] >> m) & 1ul) != 0) key |= 1 << j;
                int value = (int)((gateSig >> m) & 1ul);
                seen.TryGetValue(key, out int bits);
                seen[key] = bits | (1 << value);
            }
            foreach (int bits in seen.Values)
                if (bits == 3) return true;               // both 0 and 1 with identical other inputs
            return false;
        }

        /// <summary>
        /// Rebuilds the truth table of every switch and gate from the model by
        /// walking the components in topological order.
        /// </summary>
        /// <param name="c">Circuit to analyze.</param>
        /// <returns>A map holding the truth table of each switch and gate.</returns>
        private static Dictionary<Comp, ulong> ComputeSignatures(Circuit c)
        {
            int n = c.Switches.Count;
            int totalStates = 1 << n;
            ulong allTable = totalStates >= 64 ? ulong.MaxValue : (1ul << totalStates) - 1;
            Dictionary<Comp, ulong> sig = new Dictionary<Comp, ulong>();
            foreach (Comp comp in c.All)
            {
                if (comp.Kind == CompKind.Switch)
                    sig[comp] = SwitchSignature(c.Switches.IndexOf(comp), n);
                else if (comp.Kind != CompKind.Battery && !comp.IsLed)
                    sig[comp] = CombineSignature(comp.Kind, comp.Inputs.Select(i => sig[i]).ToList(), allTable);
                // Battery is constant and LEDs just mirror their input: no entry needed.
            }
            return sig;
        }

        /// <summary>
        /// Depth-first check that 'start' has a downstream path to some LED.
        /// </summary>
        /// <param name="start">Component to test.</param>
        /// <param name="consumers">Map of each component to its consumers.</param>
        /// <returns>True when at least one LED is reachable.</returns>
        private static bool ReachesLed(Comp start, Dictionary<Comp, List<Comp>> consumers)
        {
            HashSet<Comp> seen = new HashSet<Comp>();
            Stack<Comp> stack = new Stack<Comp>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Comp cur = stack.Pop();
                if (!seen.Add(cur)) continue;
                if (cur.IsLed) return true;
                foreach (Comp nx in consumers[cur]) stack.Push(nx);
            }
            return false;
        }

        /// <summary>Sets every switch from a bitmask (bit i = switch i).</summary>
        /// <param name="c">Circuit to modify.</param>
        /// <param name="mask">Switch states as bits.</param>
        public static void SetSwitches(Circuit c, int mask)
        {
            for (int i = 0; i < c.Switches.Count; i++)
                c.Switches[i].On = (mask & (1 << i)) != 0;
        }

        /// <summary>
        /// Semantic audit of a candidate circuit, fully driven by truth tables
        /// (no simulation loops). The circuit is accepted only when:
        /// at least one combination wins and not too many do; the rest state
        /// is neither won nor lost; all-ON is not a solution; the winning
        /// combination is reachable flipping one switch at a time without ever
        /// lighting a blue LED; it requires at least 'minWinMoves' flips; and
        /// every switch changes the game outcome somewhere.
        /// </summary>
        /// <param name="c">Candidate circuit.</param>
        /// <param name="minWinMoves">Minimum flips required for a win.</param>
        /// <returns>True when the circuit is a fair puzzle.</returns>
        private static bool Validate(Circuit c, int minWinMoves)
        {
            int n = c.Switches.Count;
            int total = 1 << n;                        // number of switch combinations = table bits
            ulong allTable = total >= 64 ? ulong.MaxValue : (1ul << total) - 1;
            Dictionary<Comp, ulong> sig = ComputeSignatures(c);

            // Win/lose bitmaps: bit 'm' tells whether combination 'm' wins or loses.
            ulong won = allTable, lost = 0;
            foreach (Comp led in c.Leds)
            {
                ulong s = sig[led.Inputs[0]];
                if (led.Kind == CompKind.LedRed) won &= s;
                else { won &= allTable & ~s; lost |= s; }
            }

            if (won == 0) return false;                            // no solution
            if (won == allTable) return false;                     // always solved
            if ((won & 1ul) != 0) return false;                    // already won at rest
            if ((lost & 1ul) != 0) return false;                   // already lost at rest
            if (((won >> (total - 1)) & 1ul) != 0) return false;   // "all ON" must not win
            if (BitOperations.PopCount(won) * 2 > total) return false;  // too many solutions

            int? reachable = ReachableWinMask(won, lost, n);
            if (reachable == null) return false;                   // no honest path to victory
            if (BitOperations.PopCount((uint)reachable.Value) < minWinMoves) return false;  // too short

            // Every switch must be able to change the outcome (win or loss):
            // a handful of bit comparisons, no simulation of 2^n states per switch.
            ulong outcome = won | lost;
            for (int s = 0; s < n; s++)
            {
                bool influential = false;
                for (int m = 0; m < total && !influential; m++)
                    if ((((outcome >> m) ^ (outcome >> (m ^ (1 << s)))) & 1ul) != 0) influential = true;
                if (!influential) return false;
            }
            return true;
        }

        /// <summary>
        /// Breadth-first search over the switch hypercube starting from
        /// all-OFF, where each move flips exactly one switch and losing states
        /// are walls. Returns the first winning mask found, i.e. the one at
        /// minimal flip distance, or null when no honest path exists.
        /// </summary>
        /// <param name="won">Bitmap of winning combinations.</param>
        /// <param name="lost">Bitmap of losing combinations.</param>
        /// <param name="switchCount">Number of switches.</param>
        /// <returns>The reachable winning mask, or null.</returns>
        private static int? ReachableWinMask(ulong won, ulong lost, int switchCount)
        {
            int total = 1 << switchCount;
            bool[] visited = new bool[total];
            Queue<int> queue = new Queue<int>();
            visited[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (((won >> cur) & 1ul) != 0) return cur;
                for (int s = 0; s < switchCount; s++)
                {
                    int next = cur ^ (1 << s);            // one move = flip one switch
                    if (!visited[next] && ((lost >> next) & 1ul) == 0)
                    {
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a winning combination reachable flipping one switch at a time
        /// without ever lighting a blue LED, at minimal flip distance. Works on
        /// any live circuit (uses simulation, so it may temporarily change the
        /// switch states).
        /// </summary>
        /// <returns>The winning switch bitmask, or null when there is none.</returns>
        public int? FindWinMask()
        {
            int n = Switches.Count;
            int total = 1 << n;
            ulong won = 0, lost = 0;
            for (int m = 0; m < total; m++)
            {
                SetSwitches(this, m);
                Simulate();
                if (Lost) lost |= 1ul << m;
                else if (Won) won |= 1ul << m;
            }

            int? reachable = ReachableWinMask(won, lost, n);
            if (reachable != null) return reachable;

            for (int m = 0; m < total; m++)               // defensive fallback, never hit after Validate
                if (((won >> m) & 1ul) != 0) return m;
            return null;
        }

        /// <summary>
        /// Describes a winning combination without altering the current game
        /// state (switch states are saved and restored).
        /// </summary>
        /// <returns>Text such as "S1=OFF  S2=ON ...", or "n/d" when unsolvable.</returns>
        public string DescribeWinCombination()
        {
            bool[] saved = new bool[Switches.Count];
            for (int i = 0; i < Switches.Count; i++) saved[i] = Switches[i].On;

            int? win = FindWinMask();

            for (int i = 0; i < Switches.Count; i++) Switches[i].On = saved[i];
            Simulate();

            if (win == null) return "n/d";
            List<string> parts = new List<string>();
            for (int i = 0; i < Switches.Count; i++)
                parts.Add("S" + (i + 1) + "=" + (((win.Value >> i) & 1) == 1 ? "ON" : "OFF"));
            return string.Join("  ", parts);
        }
    }

    // ══════════════════════════ CANVAS (character grid) ══════════════════════════

    /// <summary>
    /// A character grid with per-cell foreground colors, used to build the
    /// circuit drawing before printing it.
    /// </summary>
    public class Canvas
    {
        private const byte BUp = 1, BDown = 2, BLeft = 4, BRight = 8;

        private readonly int _w, _h;
        private readonly char[] _ch;
        private readonly ConsoleColor[] _fg;
        private readonly byte[] _mask;

        /// <summary>Initializes an empty canvas of the given size.</summary>
        /// <param name="w">Width in characters.</param>
        /// <param name="h">Height in rows.</param>
        public Canvas(int w, int h)
        {
            _w = w; _h = h;
            _ch = new char[w * h];
            _fg = new ConsoleColor[w * h];
            _mask = new byte[w * h];
            for (int i = 0; i < _ch.Length; i++) { _ch[i] = ' '; _fg[i] = ConsoleColor.Black; }
        }

        /// <summary>Writes a character with its color; out-of-range writes are ignored.</summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="c">Character to write.</param>
        /// <param name="fg">Foreground color.</param>
        public void Set(int x, int y, char c, ConsoleColor fg)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int i = y * _w + x;
            _ch[i] = c; _fg[i] = fg;
        }

        // ── wire routing: each cell accumulates directions in a bitmask ──

        /// <summary>Accumulates one wire direction into a cell and picks its glyph.</summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="bit">Direction bit to add.</param>
        /// <param name="fg">Color of the wire being drawn.</param>
        private void WireCell(int x, int y, byte bit, ConsoleColor fg)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int i = y * _w + x;
            _mask[i] |= bit;
            _ch[i] = WireChar(_mask[i]);
            // The last horizontal pass wins the color at crossings, so a wire
            // keeps its own network tint up to the destination pin.
            _fg[i] = fg;
        }

        /// <summary>Draws a horizontal wire segment.</summary>
        /// <param name="x1">Start column.</param>
        /// <param name="x2">End column.</param>
        /// <param name="y">Row.</param>
        /// <param name="fg">Wire color.</param>
        public void HSeg(int x1, int x2, int y, ConsoleColor fg)
        {
            if (x1 == x2) return;
            int a = Math.Min(x1, x2), b = Math.Max(x1, x2);
            for (int x = a; x <= b; x++)
            {
                byte bit = 0;
                if (x > a) bit |= BLeft;
                if (x < b) bit |= BRight;
                if (bit == 0) bit = BLeft | BRight;
                WireCell(x, y, bit, fg);
            }
        }

        /// <summary>Draws a vertical wire segment.</summary>
        /// <param name="y1">Start row.</param>
        /// <param name="y2">End row.</param>
        /// <param name="x">Column.</param>
        /// <param name="fg">Wire color.</param>
        public void VSeg(int y1, int y2, int x, ConsoleColor fg)
        {
            if (y1 == y2) return;
            int a = Math.Min(y1, y2), b = Math.Max(y1, y2);
            for (int y = a; y <= b; y++)
            {
                byte bit = 0;
                if (y > a) bit |= BUp;
                if (y < b) bit |= BDown;
                if (bit == 0) bit = BUp | BDown;
                WireCell(x, y, bit, fg);
            }
        }

        /// <summary>Maps the accumulated direction bits to a box-drawing glyph.</summary>
        /// <param name="m">Direction bitmask.</param>
        /// <returns>The glyph representing the wire junction.</returns>
        private static char WireChar(byte m)
        {
            bool u = (m & BUp) != 0, d = (m & BDown) != 0, l = (m & BLeft) != 0, r = (m & BRight) != 0;
            // A 4-direction cell is always a plain crossing in this game
            // (fan-outs are horizontal only): the horizontal passes over.
            if (l && r && u && d) return '─';
            if (l && r && d) return '┬';
            if (l && r && u) return '┴';
            if (u && d && l) return '┤';
            if (u && d && r) return '├';
            if (l && r) return '─';
            if (u && d) return '│';
            if (d && r) return '┌';
            if (l && d) return '┐';
            if (u && r) return '└';
            if (l && u) return '┘';
            if (u || d) return '│';
            return '─';
        }

        /// <summary>Prints the canvas to the console with per-run colors.</summary>
        public void Render()
        {
            for (int y = 0; y < _h; y++)
            {
                StringBuilder sb = new StringBuilder();
                ConsoleColor cur = (ConsoleColor)(-1);
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    if (_fg[i] != cur)
                    {
                        if (sb.Length > 0) { Console.ForegroundColor = cur; Console.Write(sb.ToString()); sb.Clear(); }
                        cur = _fg[i];
                    }
                    sb.Append(_ch[i]);
                }
                if (sb.Length > 0) { Console.ForegroundColor = cur; Console.Write(sb.ToString()); }
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        /// <summary>Renders the canvas as plain text (colors discarded).</summary>
        /// <returns>The canvas contents, one row per line.</returns>
        public string RenderToString()
        {
            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++) sb.Append(_ch[y * _w + x]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // ── test / inspection seams ──

        /// <summary>Gets the canvas width in characters.</summary>
        public int W { get { return _w; } }

        /// <summary>Gets the canvas height in rows.</summary>
        public int H { get { return _h; } }

        /// <summary>Reads the character at the given position.</summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <returns>The character, or a space outside the canvas.</returns>
        public char CharAt(int x, int y) { return _ch[y * _w + x]; }

        /// <summary>Reads the foreground color at the given position.</summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <returns>The color of the cell.</returns>
        public ConsoleColor FgAt(int x, int y) { return _fg[y * _w + x]; }

        /// <summary>
        /// Draws a connection dot, replacing whatever wire glyph was in the cell.
        /// </summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="fg">Dot color.</param>
        public void Dot(int x, int y, ConsoleColor fg)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int i = y * _w + x;
            _mask[i] = 0;
            _ch[i] = '●';
            _fg[i] = fg;
        }
    }

    // ══════════════════════════ RENDERER (pure orthogonal) ══════════════════════════

    /// <summary>
    /// Builds and prints the ASCII drawing of a circuit: per-network wire
    /// tints, orthogonal routing with parallel vertical channels, and the
    /// surrounding header/footer.
    /// </summary>
    public static class Renderer
    {
        // ════ NETWORK TINTS ════
        private static Dictionary<int, int> _netHue;   // source Id -> tint index

        /// <summary>Foreground colors used when a network is powered.</summary>
        public static readonly ConsoleColor[] NetOn =
        {
            ConsoleColor.Yellow, ConsoleColor.Cyan, ConsoleColor.Magenta,
            ConsoleColor.Green, ConsoleColor.White, ConsoleColor.Blue, ConsoleColor.Red
        };

        /// <summary>Foreground colors used when a network is off (darker variants).</summary>
        public static readonly ConsoleColor[] NetOff =
        {
            ConsoleColor.DarkYellow, ConsoleColor.DarkCyan, ConsoleColor.DarkMagenta,
            ConsoleColor.DarkGreen, ConsoleColor.Gray, ConsoleColor.DarkBlue, ConsoleColor.DarkRed
        };

        /// <summary>
        /// Maps a network tint index to its console color.
        /// </summary>
        /// <param name="hue">Tint index.</param>
        /// <param name="on">Powered state.</param>
        /// <returns>The console color for the network state.</returns>
        public static ConsoleColor HueToColor(int hue, bool on)
        {
            return on ? NetOn[hue % NetOn.Length] : NetOff[hue % NetOff.Length];
        }

        /// <summary>
        /// Assigns a distinct tint index to every switch and gate, so each
        /// output network keeps its own color along all its wires.
        /// </summary>
        /// <param name="c">Circuit to analyze.</param>
        /// <returns>A map from source Id to tint index.</returns>
        public static Dictionary<int, int> AssignNetHues(Circuit c)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            int h = 0;
            foreach (Comp s in c.Switches) map[s.Id] = h++;
            foreach (Comp g in c.Gates) map[g.Id] = h++;
            return map;
        }

        /// <summary>Seed of the current level, shown in the footer (-1 hides it).</summary>
        public static int CurrentSeed = -1;

        /// <summary>Display name of the current difficulty, shown in the footer.</summary>
        public static string CurrentDifficulty = "Media";

        /// <summary>Console color used to display the current difficulty.</summary>
        public static ConsoleColor CurrentDifficultyColor = ConsoleColor.Yellow;

        /// <summary>One wire to draw: source, destination pin and cached geometry.</summary>
        private readonly struct Wire
        {
            /// <summary>Row of the destination input pin.</summary>
            public readonly int PinRow;
            /// <summary>Row of the source output pin.</summary>
            public readonly int OutRow;
            /// <summary>Source Id (used as a stable tie-breaker).</summary>
            public readonly int SrcId;
            /// <summary>Source component.</summary>
            public readonly Comp Src;
            /// <summary>Destination component.</summary>
            public readonly Comp Dst;
            /// <summary>Input pin index on the destination.</summary>
            public readonly int Pin;
            /// <summary>Color of the wire (network tint, gray for the battery).</summary>
            public readonly ConsoleColor Color;

            /// <summary>Precomputes the geometry and color of one wire.</summary>
            /// <param name="src">Source component.</param>
            /// <param name="dst">Destination component.</param>
            /// <param name="pin">Input pin index on the destination.</param>
            public Wire(Comp src, Comp dst, int pin)
            {
                Src = src; Dst = dst; Pin = pin;
                PinRow = dst.Row + Geo.InputPinOffset(dst.Kind, pin, dst.Inputs.Count);
                OutRow = src.Row + Geo.OutputPinOffset(src.Kind, src.Inputs.Count);
                SrcId = src.Id;
                ConsoleColor tint;
                if (_netHue != null && _netHue.TryGetValue(src.Id, out int hue))
                    tint = HueToColor(hue, src.Out);
                else
                    tint = ConsoleColor.DarkGray;     // battery network: neutral gray
                Color = tint;
            }
        }

        /// <summary>Orders two wires by pin row, then out row, then source Id.</summary>
        /// <param name="a">First wire.</param>
        /// <param name="b">Second wire.</param>
        /// <returns>True when 'a' must come after 'b'.</returns>
        private static bool WireAfter(Wire a, Wire b)
        {
            if (a.PinRow != b.PinRow) return a.PinRow > b.PinRow;
            if (a.OutRow != b.OutRow) return a.OutRow > b.OutRow;
            return a.SrcId > b.SrcId;
        }

        /// <summary>Insertion sort of the wires by row (stable, tiny lists).</summary>
        /// <param name="wires">Wire list sorted in place.</param>
        private static void InsertionSortByRow(List<Wire> wires)
        {
            for (int i = 1; i < wires.Count; i++)
            {
                Wire key = wires[i];
                int j = i - 1;
                while (j >= 0 && WireAfter(wires[j], key))
                {
                    wires[j + 1] = wires[j];
                    j--;
                }
                wires[j + 1] = key;
            }
        }

        /// <summary>
        /// Builds the full drawing of a circuit. Inputs of each component are
        /// sorted by source Id so that the highest source takes the highest
        /// pin and vertical wire channels never cross each other.
        /// </summary>
        /// <param name="c">Circuit to draw.</param>
        /// <returns>The populated canvas.</returns>
        public static Canvas BuildCanvas(Circuit c)
        {
            _netHue = AssignNetHues(c);

            // Anti-crossing fix: sort the inputs of every component by source
            // Id. Sources are stacked by Id top-to-bottom, so the upper source
            // always takes the first pin from the top of its destination.
            foreach (Comp comp in c.All)
            {
                if (comp.Inputs != null && comp.Inputs.Count > 1)
                {
                    List<Comp> sortedInputs = comp.Inputs.OrderBy(src => src.Id).ToList();
                    comp.Inputs.Clear();
                    comp.Inputs.AddRange(sortedInputs);
                }
            }

            List<IGrouping<int, Comp>> groups =
                c.All.GroupBy(x => x.Level).OrderBy(g => g.Key).ToList();
            int maxCount = groups.Max(g => g.Count());

            int maxH = 0;
            foreach (Comp comp in c.All)
                maxH = Math.Max(maxH, Geo.BoxHeight(comp.Kind, comp.Inputs.Count));
            int canvasH = maxCount * (maxH + 4) + 2;

            // Fixed horizontal slots: a wide channel between levels hosts the
            // parallel vertical wires (one slot per wire, step of 2 cells).
            Dictionary<int, int> xs = new Dictionary<int, int>();
            int x = 1;
            for (int i = 0; i < groups.Count; i++)
            {
                xs[groups[i].Key] = x;
                int k = (i + 1 < groups.Count) ? groups[i + 1].Sum(n => n.Inputs.Count) * 2 : 0;
                x += Geo.BoxW + Math.Max(k, 10) + 4;
            }
            int canvasW = x;

            foreach (IGrouping<int, Comp> g in groups)
            {
                List<Comp> nodes = g.OrderBy(n => n.Id).ToList();
                int m = nodes.Count;
                for (int i = 0; i < m; i++)
                {
                    int center = (int)Math.Round(canvasH * (i + 1.0) / (m + 1.0));
                    int bh = Geo.BoxHeight(nodes[i].Kind, nodes[i].Inputs.Count);
                    nodes[i].Col = xs[g.Key];
                    nodes[i].Row = Math.Max(1, center - bh / 2);
                }
            }

            Canvas cv = new Canvas(canvasW, canvasH);

            // Wires are drawn in three passes: (1) vertical channels,
            // (2) horizontal run from the source output, (3) horizontal run
            // into the destination pin. At crossings the horizontal drawn last
            // stays continuous and keeps its own color.
            for (int i = 0; i < groups.Count - 1; i++)
            {
                List<Wire> wires = new List<Wire>();
                foreach (Comp d in groups[i + 1])
                    for (int p = 0; p < d.Inputs.Count; p++)
                        wires.Add(new Wire(d.Inputs[p], d, p));

                InsertionSortByRow(wires);

                int xOut = xs[groups[i].Key] + Geo.BoxW;

                List<Tuple<Wire, int>> slots = new List<Tuple<Wire, int>>();
                for (int j = 0; j < wires.Count; j++)
                    slots.Add(Tuple.Create(wires[j], xOut + 3 + (j * 2)));

                foreach (Tuple<Wire, int> slot in slots)
                    if (slot.Item1.OutRow != slot.Item1.PinRow)
                        cv.VSeg(slot.Item1.OutRow, slot.Item1.PinRow, slot.Item2, slot.Item1.Color);

                foreach (Tuple<Wire, int> slot in slots)
                    cv.HSeg(slot.Item1.Src.Col + Geo.BoxW,
                            slot.Item1.OutRow == slot.Item1.PinRow ? slot.Item1.Dst.Col - 1 : slot.Item2,
                            slot.Item1.OutRow, slot.Item1.Color);

                foreach (Tuple<Wire, int> slot in slots)
                    if (slot.Item1.OutRow != slot.Item1.PinRow)
                        cv.HSeg(slot.Item2, slot.Item1.Dst.Col - 1, slot.Item1.PinRow, slot.Item1.Color);

                foreach (Tuple<Wire, int> slot in slots)
                    cv.Dot(slot.Item1.Dst.Col - 1, slot.Item1.PinRow, slot.Item1.Color);
            }

            // Components on top of the wires.
            foreach (Comp comp in c.All)
                DrawComp(cv, comp, comp.IsSwitch ? c.Switches.IndexOf(comp) + 1 : 0);

            return cv;
        }

        /// <summary>
        /// Prints a complete screen: header, circuit drawing, footer and the
        /// end-of-game banner when present.
        /// </summary>
        /// <param name="c">Circuit to display.</param>
        /// <param name="moves">Moves made in the current level.</param>
        /// <param name="endMsg">End-of-game message, or null while playing.</param>
        /// <param name="endColor">Color of the end-of-game banner.</param>
        /// <param name="endDetail">Secondary line of the banner (may be empty).</param>
        public static void Draw(Circuit c, int moves, string endMsg, ConsoleColor endColor, string endDetail)
        {
            try { Console.Title = "Circuit Puzzle"; Console.Clear(); } catch { }
            Header(c);
            Canvas cv = BuildCanvas(c);
            cv.Render();
            Footer(c, moves);

            if (endMsg != null)
            {
                Console.WriteLine();
                Banner(endMsg, endColor);
                if (!string.IsNullOrEmpty(endDetail))
                    Banner(endDetail, ConsoleColor.DarkCyan);
                Console.WriteLine();
                W("Premi un tasto per un nuovo circuito, ", ConsoleColor.Gray);
                W("Q", ConsoleColor.Yellow);
                W(" per uscire.", ConsoleColor.Gray);
                Console.WriteLine();
            }
        }

        /// <summary>Draws one component box with border, pins, label and state.</summary>
        /// <param name="cv">Canvas to draw on.</param>
        /// <param name="c">Component to draw.</param>
        /// <param name="switchNum">1-based switch number, or 0 for non-switches.</param>
        private static void DrawComp(Canvas cv, Comp c, int switchNum)
        {
            string l1, l2;
            ConsoleColor border, label, state;

            switch (c.Kind)
            {
                case CompKind.Battery:
                    l1 = "BAT"; l2 = "9V";
                    border = ConsoleColor.White; label = ConsoleColor.White; state = ConsoleColor.White;
                    break;

                case CompKind.Switch:
                    l1 = "S" + switchNum;
                    l2 = c.On ? "ON" : "OFF";
                    border = c.On ? ConsoleColor.Green : ConsoleColor.Gray;
                    label = border;
                    state = c.On ? ConsoleColor.Green : ConsoleColor.DarkGray;
                    break;

                case CompKind.Not:
                case CompKind.Or:
                case CompKind.And:
                case CompKind.Xor:
                    l1 = c.Kind.ToString().ToUpperInvariant();
                    l2 = c.Out ? "●" : "○";
                    border = ConsoleColor.Cyan; label = ConsoleColor.Cyan;
                    state = c.Out ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                    break;

                case CompKind.LedRed:
                    l1 = "LED-R";
                    border = c.Out ? ConsoleColor.Red : ConsoleColor.DarkRed;
                    label = border;
                    l2 = c.Out ? "●" : "○";
                    state = c.Out ? ConsoleColor.Red : ConsoleColor.DarkGray;
                    break;

                default: // LedBlue
                    l1 = "LED-B";
                    border = c.Out ? ConsoleColor.Blue : ConsoleColor.DarkBlue;
                    label = border;
                    l2 = c.Out ? "●" : "○";
                    state = c.Out ? ConsoleColor.Blue : ConsoleColor.DarkGray;
                    break;
            }

            int bx = c.Col, by = c.Row, bw = Geo.BoxW;
            int count = c.Inputs.Count;
            int bh = Geo.BoxHeight(c.Kind, count);
            int outOff = Geo.OutputPinOffset(c.Kind, count);

            // Box border (derived height).
            for (int i = 1; i < bw - 1; i++)
            {
                cv.Set(bx + i, by, '─', border);
                cv.Set(bx + i, by + bh - 1, '─', border);
            }
            cv.Set(bx, by, '┌', border);
            cv.Set(bx + bw - 1, by, '┐', border);
            cv.Set(bx, by + bh - 1, '└', border);
            cv.Set(bx + bw - 1, by + bh - 1, '┘', border);
            for (int r = 1; r < bh - 1; r++)
            {
                cv.Set(bx, by + r, '│', border);
                cv.Set(bx + bw - 1, by + r, '│', border);
            }

            // Pins: one '├' per input on the left edge, one '┤' for the output.
            for (int p = 0; p < count; p++)
                cv.Set(bx, by + Geo.InputPinOffset(c.Kind, p, count), '├', border);
            if (outOff >= 0)
                cv.Set(bx + bw - 1, by + outOff, '┤', border);

            // Label (row 1) and state (output pin row; for LEDs, input pin row).
            string t1 = Center(l1, bw - 2);
            for (int i = 0; i < bw - 2; i++)
                cv.Set(bx + 1 + i, by + 1, t1[i], label);

            int st = c.IsLed ? Geo.InputPinOffset(c.Kind, 0, count) : outOff;
            string t2 = Center(l2, bw - 2);
            for (int i = 0; i < bw - 2; i++)
                cv.Set(bx + 1 + i, by + st, t2[i], state);
        }

        /// <summary>Centers a string inside the given width.</summary>
        /// <param name="s">Text to center.</param>
        /// <param name="width">Target width.</param>
        /// <returns>The centered, width-padded text.</returns>
        private static string Center(string s, int width)
        {
            if (s.Length > width) s = s.Substring(0, width);
            return s.PadLeft((width + s.Length) / 2).PadRight(width);
        }

        /// <summary>Prints the objective header above the drawing.</summary>
        /// <param name="c">Circuit being played.</param>
        private static void Header(Circuit c)
        {
            W("── https://github.com/LDj3SNuD/CircuitPuzzle ────────────────────", ConsoleColor.Cyan);
            Console.WriteLine();
            W("Obiettivo: accendi tutti i ", ConsoleColor.Gray);
            W("LED ROSSI", ConsoleColor.Red);
            if (c.Leds.Any(l => l.Kind == CompKind.LedBlue))
            {
                W(" tenendo SPENTI i ", ConsoleColor.Gray);
                W("LED BLU", ConsoleColor.Blue);
                W(" (un blu acceso = game over).", ConsoleColor.Gray);
            }
            else
            {
                W(". In questo circuito non ci sono LED blu.", ConsoleColor.Gray);
            }
            Console.WriteLine();
            Console.WriteLine();
        }

        /// <summary>
        /// Prints the compact footer: controls and moves, gate legend and wire
        /// tint note, current difficulty and level seed.
        /// </summary>
        /// <param name="c">Circuit being played.</param>
        /// <param name="moves">Moves made in the current level.</param>
        private static void Footer(Circuit c, int moves)
        {
            // Line 1: controls.
            W("[1-" + c.Switches.Count + "]", ConsoleColor.Yellow);
            W(" switch · ", ConsoleColor.Gray);
            W("[R]", ConsoleColor.Yellow); W(" reset · ", ConsoleColor.Gray);
            W("[N]", ConsoleColor.Yellow); W(" nuovo · ", ConsoleColor.Gray);
            W("[F1]", ConsoleColor.Yellow); W(" facile · ", ConsoleColor.Gray);
            W("[F2]", ConsoleColor.Yellow); W(" medio · ", ConsoleColor.Gray);
            W("[F3]", ConsoleColor.Yellow); W(" difficile · ", ConsoleColor.Gray);
            W("[Q]", ConsoleColor.Yellow); W(" esci · ", ConsoleColor.Gray);
            W("Mosse: ", ConsoleColor.Gray);
            W(moves.ToString(), ConsoleColor.White);
            Console.WriteLine();

            // Line 2: gate legend and wire tint note.
            W("AND", ConsoleColor.Cyan); W("=tutti · ", ConsoleColor.Gray);
            W("OR", ConsoleColor.Cyan); W("=almeno 1 · ", ConsoleColor.Gray);
            W("XOR", ConsoleColor.Cyan); W("=dispari · ", ConsoleColor.Gray);
            W("NOT", ConsoleColor.Cyan); W("=inverte · ", ConsoleColor.Gray);
            W("fili: scuro=spento, chiaro=acceso", ConsoleColor.DarkGray);
            Console.WriteLine();

            // Line 3: difficulty and level seed (the seed reproduces the level
            // with Circuit.GenerateValid(new Random(seed))).
            W(CurrentDifficulty, CurrentDifficultyColor);
            if (CurrentSeed >= 0)
            {
                W(" · seed ", ConsoleColor.Gray);
                W(CurrentSeed + " (new Random(seed))", ConsoleColor.DarkGray);
            }
            Console.WriteLine();
        }

        /// <summary>Prints a full-width colored banner line.</summary>
        /// <param name="text">Banner text.</param>
        /// <param name="bg">Background color.</param>
        private static void Banner(string text, ConsoleColor bg)
        {
            Console.BackgroundColor = bg;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(" " + text + " ");
            Console.ResetColor();
        }

        /// <summary>Writes a colored string to the console.</summary>
        /// <param name="s">Text to write.</param>
        /// <param name="col">Foreground color.</param>
        private static void W(string s, ConsoleColor col)
        {
            Console.ForegroundColor = col;
            Console.Write(s);
            Console.ResetColor();
        }

        /// <summary>Renders a circuit to plain text (test and debug seam).</summary>
        /// <param name="c">Circuit to render.</param>
        /// <returns>The ASCII drawing without colors.</returns>
        public static string DrawToString(Circuit c) { return BuildCanvas(c).RenderToString(); }
    }

    // ══════════════════════════ PROGRAM ══════════════════════════

    /// <summary>Console front-end: game loop, input handling, difficulty switching.</summary>
    public static class Program
    {
        /// <summary>
        /// Entry point: generates one level per iteration and runs it until
        /// the user quits. Keys 1-5 toggle switches, R resets, N requests a
        /// new level, F1/F2/F3 switch difficulty (implying a new level) and
        /// Q/Esc quit.
        /// </summary>
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            try { Console.CursorVisible = false; } catch { }

            int sessionSeed = Environment.TickCount & 0x7FFFFFFF;
            Random rngSeed = new Random(sessionSeed);     // produces one independent seed per level
            Difficulty difficulty = Difficulty.Medium;

            while (true)
            {
                // ★ One dedicated seed per level: the footer seed reproduces this
                // exact circuit through Circuit.GenerateValid(new Random(seed)).
                int levelSeed = rngSeed.Next();
                Circuit circuit = Circuit.GenerateValid(new Random(levelSeed), difficulty);
                Renderer.CurrentSeed = levelSeed;
                Renderer.CurrentDifficulty = DifficultyLabel(difficulty);
                Renderer.CurrentDifficultyColor = DifficultyColor(difficulty);

                int moves = 0;
                string endMsg = null, endDetail = null;
                ConsoleColor endColor = ConsoleColor.DarkGray;
                bool quit = false, redraw = true;

                while (true)
                {
                    if (redraw)
                    {
                        Renderer.Draw(circuit, moves, endMsg, endColor, endDetail);
                        redraw = false;
                    }

                    if (endMsg != null)                   // game over: any key starts a new level
                    {
                        ConsoleKey endKey = Console.ReadKey(true).Key;
                        if (endKey == ConsoleKey.Q || endKey == ConsoleKey.Escape) quit = true;
                        else if (endKey == ConsoleKey.F1) difficulty = Difficulty.Easy;
                        else if (endKey == ConsoleKey.F2) difficulty = Difficulty.Medium;
                        else if (endKey == ConsoleKey.F3) difficulty = Difficulty.Hard;
                        break;
                    }

                    ConsoleKey key = Console.ReadKey(true).Key;
                    int idx = -1;
                    if (key >= ConsoleKey.D1 && key <= ConsoleKey.D9)
                        idx = (int)key - (int)ConsoleKey.D1;
                    else if (key >= ConsoleKey.NumPad1 && key <= ConsoleKey.NumPad9)
                        idx = (int)key - (int)ConsoleKey.NumPad1;

                    if (idx >= 0 && idx < circuit.Switches.Count)
                    {
                        Comp sw = circuit.Switches[idx];
                        sw.On = !sw.On;
                        circuit.Simulate();
                        moves++;

                        if (circuit.Lost)
                        {
                            endMsg = "GAME OVER! Hai acceso un LED BLU";
                            endColor = ConsoleColor.Red;
                            endDetail = "Combinazione vincente:  " + circuit.DescribeWinCombination();
                        }
                        else if (circuit.Won)
                        {
                            endMsg = "VITTORIA! Hai chiuso il circuito giusto";
                            endColor = ConsoleColor.Green;
                            endDetail = "Risolto in " + moves + " mosse.";
                        }
                        redraw = true;
                    }
                    else if (key == ConsoleKey.R)
                    {
                        foreach (Comp s in circuit.Switches) s.On = false;
                        circuit.Simulate();
                        moves = 0;
                        redraw = true;
                    }
                    else if (key == ConsoleKey.N)
                    {
                        break;                            // new level, same difficulty
                    }
                    else if (key == ConsoleKey.F1)
                    {
                        difficulty = Difficulty.Easy; break;
                    }
                    else if (key == ConsoleKey.F2)
                    {
                        difficulty = Difficulty.Medium; break;
                    }
                    else if (key == ConsoleKey.F3)
                    {
                        difficulty = Difficulty.Hard; break;
                    }
                    else if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
                    {
                        quit = true;
                        break;
                    }
                }

                if (quit) break;
            }

            Console.Clear();
            try { Console.CursorVisible = true; } catch { }
        }

        /// <summary>Returns the Italian display name of a difficulty.</summary>
        /// <param name="difficulty">Difficulty level.</param>
        /// <returns>"Facile", "Media" or "Difficile".</returns>
        private static string DifficultyLabel(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return "Facile";
                case Difficulty.Hard: return "Difficile";
                default: return "Media";
            }
        }

        /// <summary>Returns the console color associated with a difficulty.</summary>
        /// <param name="difficulty">Difficulty level.</param>
        /// <returns>Green for easy, yellow for medium, red for hard.</returns>
        private static ConsoleColor DifficultyColor(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return ConsoleColor.Green;
                case Difficulty.Hard: return ConsoleColor.Red;
                default: return ConsoleColor.Yellow;
            }
        }

        /// <summary>
        /// Appends unhandled exceptions to a local log file instead of dying
        /// silently; the logger itself must never throw.
        /// </summary>
        /// <param name="sender">Event source (unused).</param>
        /// <param name="e">Event arguments holding the exception.</param>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                File.AppendAllText("circuito-error.log",
                    DateTime.Now + " — " + (ex == null ? "?" : ex.GetType().Name) + ": " +
                    (ex == null ? "" : ex.Message) + Environment.NewLine +
                    (ex == null ? "" : ex.StackTrace) + Environment.NewLine +
                    "Inner: " + (ex == null || ex.InnerException == null ? "—" : ex.InnerException.ToString()) +
                    Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // The logger must never throw.
            }
        }
    }
}
