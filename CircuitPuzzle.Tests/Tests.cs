using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CircuitPuzzle.Tests
{
    // ===========================================================================
    // CircuitPuzzle — definitive test suite.
    //
    //   TestVeritaInstradamento   T1–T5   drawn pins vs. model wiring (routing truth)
    //   TestColoriRete                    one identity tint per network
    //   TestCircuitRender                 renderer guards + per-switch liveness
    //   TestCircuitBase                   generator shape, rest state, Won/Lost
    //                                     semantics, solver consistency
    //   TestCircuitAdv                    anti-triviality constraints, DAG
    //                                     topology, solver/description agreement
    //   TestGateLogic                     deterministic gate truth tables
    //
    // Every random draw is seeded: any failure is reproducible from the seed
    // shown in the test-case name. This file is the ONLY test file of the
    // project — remove every other test file when adding it.
    // ===========================================================================

    /// <summary>
    /// Routing-truth guards (T1–T5): every input pin drawn on the canvas must
    /// be the glyph '●' painted with the tint of the network that actually
    /// feeds it, and toggling a switch may only repaint the wires of the
    /// sub-graph hanging below that switch. Contains 5 test methods.
    /// </summary>
    [TestClass]
    public class TestVeritaInstradamento
    {
        /// <summary>Inclusive upper bound of the seed range scanned by T1 and T2.</summary>
        private const int MaxSeed = 199;

        /// <summary>Wire and pin glyphs the renderer may paint outside component boxes.</summary>
        private const string GlifiFilo = "─│┌┐└┘┬├┤┴●";

        /// <summary>
        /// Human-readable component name used in failure messages.
        /// </summary>
        /// <param name="c">Component to name.</param>
        /// <returns>"BAT" for the battery, "S&lt;id&gt;" for a switch, otherwise "&lt;KIND&gt;&lt;id&gt;".</returns>
        private static string Nome(Comp c)
        {
            if (c.Kind == CompKind.Battery) return "BAT";
            if (c.Kind == CompKind.Switch) return "S" + c.Id;
            return c.Kind.ToString().ToUpperInvariant() + c.Id;
        }

        /// <summary>
        /// Tint the renderer would use for a wire leaving <paramref name="src"/>:
        /// a network with an assigned hue gets its light/dark variant according
        /// to <paramref name="src"/>.Out; the battery network falls back to the
        /// neutral DarkGray. Exact mirror of the Wire constructor.
        /// </summary>
        /// <param name="map">Hue map produced by Renderer.AssignNetHues (may be null).</param>
        /// <param name="src">Source component the wire leaves from.</param>
        /// <returns>The expected foreground colour of the wire's glyphs.</returns>
        private static ConsoleColor TintaAttesa(Dictionary<int, int> map, Comp src)
        {
            if (map != null && map.TryGetValue(src.Id, out int hue))
                return Renderer.HueToColor(hue, src.Out);
            return ConsoleColor.DarkGray;
        }

        /// <summary>
        /// True when the canvas cell belongs to any component box: borders and
        /// labels may change on a toggle without violating the wire-tint contract.
        /// </summary>
        /// <param name="c">Circuit under test.</param>
        /// <param name="x">Canvas column.</param>
        /// <param name="y">Canvas row.</param>
        /// <returns>True if (x, y) lies inside a component box.</returns>
        private static bool DentroUnBox(Circuit c, int x, int y)
        {
            foreach (Comp comp in c.All)
                if (x >= comp.Col && x < comp.Col + Geo.BoxW &&
                    y >= comp.Row && y < comp.Row + Geo.BoxHeight(comp.Kind, comp.Inputs.Count))
                    return true;
            return false;
        }

        /// <summary>
        /// Every tint legitimately paintable inside the sub-graph fed by
        /// <paramref name="sorgente"/>: both the light and the dark variant of
        /// each reached network, plus the neutral battery grey.
        /// </summary>
        /// <param name="c">Circuit under test.</param>
        /// <param name="map">Hue map produced by Renderer.AssignNetHues.</param>
        /// <param name="sorgente">Switch whose downstream sub-graph is collected.</param>
        /// <returns>The set of tints allowed inside that sub-graph.</returns>
        private static HashSet<ConsoleColor> TinteDiscendenti(Circuit c, Dictionary<int, int> map, Comp sorgente)
        {
            HashSet<Comp> raggiunti = new HashSet<Comp> { sorgente };
            bool grown = true;
            while (grown)
            {
                grown = false;
                foreach (Comp g in c.Gates)
                    if (!raggiunti.Contains(g) && g.Inputs.Any(i => raggiunti.Contains(i)))
                    { raggiunti.Add(g); grown = true; }
            }
            HashSet<ConsoleColor> tinte = new HashSet<ConsoleColor>();
            foreach (Comp comp in raggiunti)
            {
                if (map.TryGetValue(comp.Id, out int hue))
                {
                    tinte.Add(Renderer.HueToColor(hue, true));
                    tinte.Add(Renderer.HueToColor(hue, false));
                }
            }
            tinte.Add(ConsoleColor.DarkGray);
            return tinte;
        }

        /// <summary>
        /// T1 — for seeds 0..199, every drawn input pin must be the glyph '●'
        /// painted with the tint of the component the model says feeds it.
        /// </summary>
        [TestMethod]
        public void T1_Pallini_CorrispondonoAgliInputDelModello()
        {
            List<string> viol = new List<string>();
            for (int seed = 0; seed <= MaxSeed; seed++)
            {
                Circuit c = Circuit.GenerateValid(new Random(seed));
                Dictionary<int, int> map = Renderer.AssignNetHues(c);
                Canvas cv = Renderer.BuildCanvas(c);
                foreach (Comp dst in c.All)
                    for (int p = 0; p < dst.Inputs.Count; p++)
                    {
                        Comp src = dst.Inputs[p];
                        int px = dst.Col - 1;
                        int py = dst.Row + Geo.InputPinOffset(dst.Kind, p, dst.Inputs.Count);
                        if (cv.CharAt(px, py) != '●' || cv.FgAt(px, py) != TintaAttesa(map, src))
                            viol.Add("seed " + seed + ": pin " + p + " of " + Nome(dst) +
                                     " expected " + TintaAttesa(map, src) + " from " + Nome(src) +
                                     ", found '" + cv.CharAt(px, py) + "' " + cv.FgAt(px, py));
                    }
            }
            Assert.IsTrue(viol.Count == 0, "inconsistent pin dots:\n" + string.Join("\n", viol.Take(10)));
        }

        /// <summary>
        /// T2 — toggling one switch may only repaint wire cells belonging to
        /// that switch's downstream sub-graph (component boxes excluded): any
        /// other wire cell must keep both glyph and tint.
        /// </summary>
        [TestMethod]
        public void T2_ToggleSwitch_CambianoSoloLeRetiDelSuoSottografo()
        {
            List<string> viol = new List<string>();
            for (int seed = 0; seed <= MaxSeed; seed++)
            {
                Circuit c = Circuit.GenerateValid(new Random(seed));
                Dictionary<int, int> map = Renderer.AssignNetHues(c);
                for (int k = 0; k < c.Switches.Count; k++)
                {
                    Circuit.SetSwitches(c, 0); c.Simulate();
                    Canvas cv0 = Renderer.BuildCanvas(c);
                    Circuit.SetSwitches(c, 1 << k); c.Simulate();
                    Canvas cv1 = Renderer.BuildCanvas(c);
                    HashSet<ConsoleColor> ammesse = TinteDiscendenti(c, map, c.Switches[k]);
                    for (int y = 0; y < cv0.H; y++)
                        for (int x = 0; x < cv0.W; x++)
                        {
                            if (DentroUnBox(c, x, y)) continue;
                            char g0 = cv0.CharAt(x, y), g1 = cv1.CharAt(x, y);
                            ConsoleColor f0 = cv0.FgAt(x, y), f1 = cv1.FgAt(x, y);
                            if (g0 == g1 && f0 == f1) continue;
                            if (GlifiFilo.IndexOf(g0) < 0 && GlifiFilo.IndexOf(g1) < 0) continue;
                            if (!ammesse.Contains(f0) || !ammesse.Contains(f1))
                                viol.Add("seed " + seed + " [S" + (k + 1) + "]: (" + x + "," + y + ") '" +
                                         g0 + "' " + f0 + " -> '" + g1 + "' " + f1);
                        }
                }
            }
            Assert.IsTrue(viol.Count == 0, "cells changed with foreign tints:\n" + string.Join("\n", viol.Take(10)));
        }

        /// <summary>
        /// Hand-built reference level: BAT, S1..S3, NOT=[S1], AND=[S1,S2],
        /// LED←NOT, LED←S3, LED←AND. Mirrors the scenario of the pin-routing
        /// discussions and keeps T3 fully deterministic.
        /// </summary>
        /// <returns>The hand-wired circuit, already simulated once.</returns>
        private static Circuit LivelloClasse()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0 };
            c.All.Add(batt); c.Battery = batt;
            Comp[] sw = new Comp[3];
            for (int i = 0; i < 3; i++)
            {
                sw[i] = new Comp { Id = 1 + i, Kind = CompKind.Switch, Level = 1 };
                sw[i].Inputs.Add(batt);
                c.All.Add(sw[i]); c.Switches.Add(sw[i]);
            }
            Comp not1 = new Comp { Id = 4, Kind = CompKind.Not, Level = 2 };
            not1.Inputs.Add(sw[0]);
            Comp and1 = new Comp { Id = 5, Kind = CompKind.And, Level = 2 };
            and1.Inputs.Add(sw[0]); and1.Inputs.Add(sw[1]);
            Comp ledNot = new Comp { Id = 6, Kind = CompKind.LedRed, Level = not1.Level + 1 };
            ledNot.Inputs.Add(not1);
            Comp ledS3 = new Comp { Id = 7, Kind = CompKind.LedRed, Level = sw[2].Level + 1 };
            ledS3.Inputs.Add(sw[2]);
            Comp ledAnd = new Comp { Id = 8, Kind = CompKind.LedRed, Level = and1.Level + 1 };
            ledAnd.Inputs.Add(and1);
            c.All.AddRange(new Comp[] { not1, and1, ledNot, ledS3, ledAnd });
            c.Gates.AddRange(new Comp[] { not1, and1 });
            c.Leds.AddRange(new Comp[] { ledNot, ledS3, ledAnd });
            c.Simulate();
            return c;
        }

        /// <summary>
        /// T3 — on the hand-built level the NOT must depend on S1 only: pin dot
        /// presence and tint, tint ownership of the AND's second pin, and the
        /// set of cells each single toggle is allowed to repaint.
        /// </summary>
        [TestMethod]
        public void T3_LaNotDipendeSoloDaS1_EIlDisegnoLoDice()
        {
            Circuit c = LivelloClasse();
            Dictionary<int, int> map = Renderer.AssignNetHues(c);
            Comp not1 = c.Gates[0], and1 = c.Gates[1];

            Circuit.SetSwitches(c, 0); c.Simulate();
            Canvas riposo = Renderer.BuildCanvas(c);
            int nx = not1.Col - 1, ny = not1.Row + Geo.InputPinOffset(not1.Kind, 0, 1);
            Assert.IsTrue(not1.Out, "at rest NOT=¬S1 must be lit");
            Assert.AreEqual('●', riposo.CharAt(nx, ny), "missing pin dot at the NOT input");
            Assert.AreEqual(Renderer.HueToColor(map[1], false), riposo.FgAt(nx, ny),
                "the NOT input pin must carry S1's dark tint");
            Assert.AreEqual(Renderer.HueToColor(map[2], false), riposo.FgAt(and1.Col - 1, and1.Row + 4),
                "the CYAN pin belongs to the AND's 2nd pin (S2), not to the NOT");

            Circuit.SetSwitches(c, 0b001); c.Simulate();
            Assert.IsFalse(not1.Out, "pressing [1] turns the NOT off");
            Canvas cvS1 = Renderer.BuildCanvas(c);
            VerificaCambiConcesse(c, riposo, cvS1, TinteDiscendenti(c, map, c.Switches[0]), "S1");

            Circuit.SetSwitches(c, 0b010); c.Simulate();
            Assert.IsTrue(not1.Out, "pressing [2] from rest leaves the NOT on");
            Canvas cvS2 = Renderer.BuildCanvas(c);
            VerificaCambiConcesse(c, riposo, cvS2, TinteDiscendenti(c, map, c.Switches[1]), "S2");
            Assert.AreEqual(riposo.FgAt(nx, ny), cvS2.FgAt(nx, ny), "the NOT's pin does not change with S2");

            Circuit.SetSwitches(c, 0b011); c.Simulate();
            Assert.IsFalse(not1.Out, "with S1 on, the NOT stays off whatever S2 does");
        }

        /// <summary>
        /// Shared checker for T2/T3: compares two canvases cell by cell and
        /// fails when a wire cell changes while using a tint outside the
        /// allowed set.
        /// </summary>
        /// <param name="c">Circuit under test.</param>
        /// <param name="a">Canvas before the toggle.</param>
        /// <param name="b">Canvas after the toggle.</param>
        /// <param name="ammesse">Tints allowed on changed cells.</param>
        /// <param name="tag">Label of the toggle, used in the failure message.</param>
        private static void VerificaCambiConcesse(Circuit c, Canvas a, Canvas b, HashSet<ConsoleColor> ammesse, string tag)
        {
            List<string> viol = new List<string>();
            for (int y = 0; y < a.H; y++)
                for (int x = 0; x < a.W; x++)
                {
                    if (DentroUnBox(c, x, y)) continue;
                    char g0 = a.CharAt(x, y), g1 = b.CharAt(x, y);
                    ConsoleColor f0 = a.FgAt(x, y), f1 = b.FgAt(x, y);
                    if (g0 == g1 && f0 == f1) continue;
                    if (GlifiFilo.IndexOf(g0) < 0 && GlifiFilo.IndexOf(g1) < 0) continue;
                    if (!ammesse.Contains(f0) || !ammesse.Contains(f1))
                        viol.Add("(" + x + "," + y + ") '" + g0 + "' " + f0 + " -> '" + g1 + "' " + f1);
                }
            Assert.IsTrue(viol.Count == 0, "toggle " + tag + ": cells changed with foreign tints:\n" +
                          string.Join("\n", viol.Take(10)));
        }

        /// <summary>
        /// T4 — generation is a pure function of the seed: drawing the same
        /// seed twice must produce the identical level.
        /// </summary>
        [TestMethod]
        public void T4_StessoSeed_StessoLivello()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                string a = Renderer.DrawToString(Circuit.GenerateValid(new Random(seed)));
                string b = Renderer.DrawToString(Circuit.GenerateValid(new Random(seed)));
                Assert.AreEqual(a, b, "seed " + seed + ": the same seed must redraw the identical level");
            }
        }

        /// <summary>
        /// Seed inspected by the T5 sentinel. While everything is healthy any
        /// value works and the test stays silent; when chasing a suspect level,
        /// copy here the seed printed in the game footer and the failure report
        /// becomes a full autopsy of that level.
        /// </summary>
        private const int SeedDaAnalizzare = 0;

        /// <summary>
        /// T5 — always-on microscope sentinel for one seed: compares EVERY
        /// drawn pin against the tint declared by the model. Healthy seed →
        /// silent pass; any inconsistent pin → failure whose message is a
        /// complete autopsy (wiring, hue table, expected vs. drawn pins, full
        /// rendering).
        /// </summary>
        [TestMethod]
        public void T5_Microscopio_DiVerita()
        {
            Circuit c = Circuit.GenerateValid(new Random(SeedDaAnalizzare));
            Dictionary<int, int> map = Renderer.AssignNetHues(c);
            Canvas cv = Renderer.BuildCanvas(c);      // draws the level and places the pin dots

            // 1) pin-by-pin comparison (Inputs is already in drawing order:
            //    BuildCanvas sorts it by Id before routing the wires)
            List<string> difetti = new List<string>();
            foreach (Comp dst in c.All)
            {
                for (int p = 0; p < dst.Inputs.Count; p++)
                {
                    Comp src = dst.Inputs[p];
                    int x = dst.Col - 1;
                    int y = dst.Row + Geo.InputPinOffset(dst.Kind, p, dst.Inputs.Count);

                    ConsoleColor atteso = TintaAttesa(map, src);
                    ConsoleColor trovato = cv.FgAt(x, y);
                    char glifo = cv.CharAt(x, y);

                    if (glifo != '●' || trovato != atteso)
                        difetti.Add("seed " + SeedDaAnalizzare + ": pin " + p + " of " + Nome(dst) +
                                    " expected " + atteso + " from " + Nome(src) +
                                    ", found '" + glifo + "' " + trovato + " @(" + x + "," + y + ")");
                }
            }

            if (difetti.Count == 0) return;           // consistent: silent green

            // 2) at least one defect: the full autopsy IS the failure message
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== " + difetti.Count + " INCONSISTENT PIN(S) — SEED " + SeedDaAnalizzare + " ===");
            foreach (string d in difetti) sb.AppendLine(d);
            sb.AppendLine();
            sb.AppendLine("=== SEED " + SeedDaAnalizzare + " — ACTUAL MODEL WIRING ===");
            foreach (Comp comp in c.All)
            {
                sb.Append(Nome(comp)).Append(" (").Append(comp.Kind).Append(" L").Append(comp.Level)
                  .Append(" @").Append(comp.Col).Append(",").Append(comp.Row).Append(")");
                if (comp.Inputs.Count > 0)
                    sb.Append("  <=  ").Append(string.Join(", ", comp.Inputs.Select(Nome)));
                sb.AppendLine();
            }
            sb.AppendLine("=== TINTS (Id -> hue) ===");
            foreach (KeyValuePair<int, int> kv in map)
                sb.AppendLine("Id " + kv.Key + " -> hue " + kv.Value + " (" +
                              Renderer.HueToColor(kv.Value, true) + " / " + Renderer.HueToColor(kv.Value, false) + ")");
            sb.AppendLine("=== EXPECTED PIN DOTS vs ACTUALLY DRAWN ===");
            foreach (Comp dst in c.All)
                for (int p = 0; p < dst.Inputs.Count; p++)
                {
                    int x = dst.Col - 1;
                    int y = dst.Row + Geo.InputPinOffset(dst.Kind, p, dst.Inputs.Count);
                    sb.AppendLine(Nome(dst) + " pin" + p + " @(" + x + "," + y + ") = " +
                                  TintaAttesa(map, dst.Inputs[p]) + " (from " + Nome(dst.Inputs[p]) +
                                  ")  — drawn: '" + cv.CharAt(x, y) + "' " + cv.FgAt(x, y));
                }
            sb.AppendLine("=== RENDERING (RenderToString) ===");
            sb.Append(cv.RenderToString());
            Assert.Fail(sb.ToString());   // the failure IS the requested report
        }
    }

    /// <summary>
    /// Network-tint identity guards: every source (switch or gate) owns one
    /// hue; the OFF variant of a network never collides with the ON variant of
    /// another; at rest the distinct dark tints are actually visible on the
    /// canvas; a lit path is painted with its network's light tint.
    /// Contains 4 test methods.
    /// </summary>
    [TestClass]
    public class TestColoriRete
    {
        /// <summary>
        /// Hand-built mirror of a real level: BAT, S1..S3, XOR1=[S3,S1] (top
        /// gate), XOR2=[S1,S2], LED1←XOR1, LED2←XOR2. With only S3 on, XOR1
        /// computes 1⊕0=1 and LED1 lights up. Deterministic 5-network wiring.
        /// </summary>
        /// <returns>The hand-wired circuit, already simulated once.</returns>
        private static Circuit BuildCircuit()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0 };
            c.All.Add(batt); c.Battery = batt;

            Comp[] sw = new Comp[3];
            for (int i = 0; i < 3; i++)
            {
                sw[i] = new Comp { Id = 1 + i, Kind = CompKind.Switch, Level = 1 };
                sw[i].Inputs.Add(batt);
                c.All.Add(sw[i]); c.Switches.Add(sw[i]);
            }

            Comp xor1 = new Comp { Id = 4, Kind = CompKind.Xor, Level = 2 };
            xor1.Inputs.Add(sw[2]);                       // S3
            xor1.Inputs.Add(sw[0]);                       // S1
            Comp xor2 = new Comp { Id = 5, Kind = CompKind.Xor, Level = 2 };
            xor2.Inputs.Add(sw[0]);                       // S1
            xor2.Inputs.Add(sw[1]);                       // S2

            Comp led1 = new Comp { Id = 6, Kind = CompKind.LedRed, Level = 3 };
            led1.Inputs.Add(xor1);
            Comp led2 = new Comp { Id = 7, Kind = CompKind.LedRed, Level = 3 };
            led2.Inputs.Add(xor2);

            c.All.AddRange(new Comp[] { xor1, xor2, led1, led2 });
            c.Gates.AddRange(new Comp[] { xor1, xor2 });
            c.Leds.AddRange(new Comp[] { led1, led2 });
            c.Simulate();
            return c;
        }

        /// <summary>
        /// Collects the tints used by wire glyphs (pin dots included) on a
        /// canvas. Box borders and labels may add extra tints; the assertions
        /// rely only on tints exclusive to wires.
        /// </summary>
        /// <param name="cv">Canvas to scan.</param>
        /// <returns>The set of foreground colours used by wire glyphs.</returns>
        private static HashSet<ConsoleColor> TinteGlifiFilo(Canvas cv)
        {
            HashSet<ConsoleColor> tinte = new HashSet<ConsoleColor>();
            string glifi = "─│┌┐└┘┬├┤┴●";
            for (int y = 0; y < cv.H; y++)
                for (int x = 0; x < cv.W; x++)
                    if (glifi.IndexOf(cv.CharAt(x, y)) >= 0)
                        tinte.Add(cv.FgAt(x, y));
            return tinte;
        }

        /// <summary>
        /// Each of the 5 networks (3 switches + 2 gates) must receive a
        /// distinct hue index.
        /// </summary>
        [TestMethod]
        public void TestReti_OgniSorgente_HaTintaPropria()
        {
            Circuit c = BuildCircuit();
            HashSet<int> tinte = new HashSet<int>(Renderer.AssignNetHues(c).Values);
            Assert.AreEqual(5, tinte.Count, "every switch/gate must own a distinct tint index");
        }

        /// <summary>
        /// Within one network the off variant must differ from the on variant,
        /// and the off variant of one network must never equal the on variant
        /// of another (a dark wire must never be mistaken for a live one).
        /// </summary>
        [TestMethod]
        public void TestReti_SpentaNonEguagliaAccesaDiAltraRete()
        {
            Circuit c = BuildCircuit();
            Dictionary<int, int> map = Renderer.AssignNetHues(c);
            foreach (int h1 in map.Values)
            {
                Assert.AreNotEqual(Renderer.HueToColor(h1, false), Renderer.HueToColor(h1, true),
                    "same network: the off variant must differ from the on variant");
                foreach (int h2 in map.Values)
                    if (h1 != h2)
                        Assert.AreNotEqual(Renderer.HueToColor(h1, false), Renderer.HueToColor(h2, true),
                            "the off tint of one network must not equal the on tint of another");
            }
        }

        /// <summary>
        /// Regression guard for the identity tints: at rest the 5 networks must
        /// show 5 DIFFERENT dark tints on the canvas — wire glyphs must not
        /// collapse into a single DarkGray fallback.
        /// </summary>
        [TestMethod]
        public void TestReti_ARiposo_LeRetiHannoTinteScureDistinte()
        {
            Circuit c = BuildCircuit();
            Circuit.SetSwitches(c, 0b000);
            c.Simulate();
            Canvas cv = Renderer.BuildCanvas(c);
            HashSet<ConsoleColor> tinte = TinteGlifiFilo(cv);

            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(0, false)), "missing the off tint of network 0 (S1)");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(1, false)), "missing the off tint of network 1 (S2)");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(2, false)), "missing the off tint of network 2 (S3)");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(3, false)), "missing the off tint of network 3 (XOR1)");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(4, false)), "missing the off tint of network 4 (XOR2)");
        }

        /// <summary>
        /// With only S3 on, the lit S3 path and the lit XOR1 output must use
        /// their networks' light tints, while the off S2 stays on its dark tint.
        /// </summary>
        [TestMethod]
        public void TestReti_S3Acceso_IlSuoPercorsoUsaLaTintaChiara()
        {
            Circuit c = BuildCircuit();
            Circuit.SetSwitches(c, 0b100);                // only S3 on
            c.Simulate();
            Canvas cv = Renderer.BuildCanvas(c);
            HashSet<ConsoleColor> tinte = TinteGlifiFilo(cv);

            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(2, true)),
                "the lit S3 path must use its light tint");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(3, true)),
                "the LIT output of XOR1 must use its network's light tint");
            Assert.IsTrue(tinte.Contains(Renderer.HueToColor(1, false)),
                "S2 off must stay on its dark tint");
        }
    }

    /// <summary>
    /// Renderer guards and per-switch liveness: a wire crossing must never be
    /// drawn as a junction glyph, generated levels must stay junction-free,
    /// and every generated switch must be able to change the visible LED
    /// vector somewhere. Contains 3 test methods (21 executed cases: the two
    /// parameterized ones run on seeds 0–9).
    /// </summary>
    [TestClass]
    public class TestCircuitRender
    {
        /// <summary>
        /// The horizontal stub of S3 must be CROSSED (not joined) by the wire
        /// running from S4 up to the OR gate: on wrong renderers this wiring
        /// produces a '┼' that looks like a connection.
        /// </summary>
        [TestMethod]
        public void TestRender_Crossing_IsNeverDrawnAsJunction()
        {
            string s = Renderer.DrawToString(BuildCrossingCircuit());
            Assert.IsTrue(s.Contains("NOT"), "sanity: the NOT must appear in the drawing");
            Assert.IsFalse(s.Contains('┼'),
                "RENDER BUG: a crossing was drawn as the junction '┼' (it looks like a connection)");
        }

        /// <summary>
        /// Junction-free rendering on generated levels: no '┼' anywhere in the
        /// drawing of the given seed.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestRender_GeneratedCircuits_NoFalseJunctionGlyphs(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));
            Assert.IsFalse(Renderer.DrawToString(circuit).Contains('┼'));
        }

        /// <summary>
        /// Stronger than a plain Won/Lost probe: every switch must be able to
        /// change the FULL LED vector in at least one context of the other
        /// switches — i.e. no dead switches from the player's point of view.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestLogic_EverySwitch_ChangesLedVector_Somewhere(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));
            int n = circuit.Switches.Count;
            for (int s = 0; s < n; s++)
            {
                bool changes = false;
                for (int mask = 0; mask < (1 << n) && !changes; mask++)
                {
                    Circuit.SetSwitches(circuit, mask & ~(1 << s)); circuit.Simulate();
                    bool[] off = circuit.Leds.Select(l => l.Out).ToArray();
                    Circuit.SetSwitches(circuit, mask | (1 << s)); circuit.Simulate();
                    changes = !off.SequenceEqual(circuit.Leds.Select(l => l.Out).ToArray());
                }
                Assert.IsTrue(changes, $"Switch S{s + 1} is dead: it never changes the LED vector.");
            }
        }

        /// <summary>
        /// Hand-built crossing scenario: OR=[S1,S2,S4] and NOT=[S3] at the same
        /// level, so the wire from S4 must climb towards the OR while crossing
        /// the horizontal stub of S3. Plus LED←S2 (direct), LED←OR, LED←NOT.
        /// </summary>
        /// <returns>The hand-wired crossing circuit, already simulated once.</returns>
        private static Circuit BuildCrossingCircuit()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0 };
            c.All.Add(batt); c.Battery = batt;
            Comp[] sw = new Comp[4];
            for (int i = 0; i < 4; i++)
            {
                sw[i] = new Comp { Id = 1 + i, Kind = CompKind.Switch, Level = 1 };
                sw[i].Inputs.Add(batt);
                c.All.Add(sw[i]); c.Switches.Add(sw[i]);
            }
            Comp or = new Comp { Id = 5, Kind = CompKind.Or, Level = 2 };
            or.Inputs.AddRange(new Comp[] { sw[0], sw[1], sw[3] });
            Comp not = new Comp { Id = 6, Kind = CompKind.Not, Level = 2 };
            not.Inputs.Add(sw[2]);
            Comp ledC = new Comp { Id = 7, Kind = CompKind.LedRed, Level = 2 };
            ledC.Inputs.Add(sw[2]);
            Comp ledA = new Comp { Id = 8, Kind = CompKind.LedRed, Level = 3 };
            ledA.Inputs.Add(or);
            Comp ledB = new Comp { Id = 9, Kind = CompKind.LedRed, Level = 3 };
            ledB.Inputs.Add(not);
            c.All.AddRange(new Comp[] { or, not, ledC, ledA, ledB });
            c.Gates.AddRange(new Comp[] { or, not });
            c.Leds.AddRange(new Comp[] { ledC, ledA, ledB });
            c.Simulate();
            return c;
        }
    }

    /// <summary>
    /// Generator contract, verified on seeds 0–9: a generated circuit has the
    /// expected shape, its rest state is neither won nor lost, the Won/Lost
    /// properties follow the red/blue LED semantics, and the built-in solver
    /// returns a mask that really wins. Contains 5 test methods.
    /// </summary>
    [TestClass]
    public class TestCircuitBase
    {
        /// <summary>
        /// Shape contract: exactly one battery, at least one switch, at least
        /// one gate, at least one red LED (the goal), and every component of
        /// the expected kind.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestCircuitGenerateValid(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));

            Assert.IsNotNull(circuit.Battery, "a circuit always has a battery");
            Assert.AreEqual(1, circuit.All.Count(k => k.Kind == CompKind.Battery), "exactly one battery");
            Assert.IsTrue(circuit.Switches.Count >= 1, "at least one switch is required to play");
            Assert.IsTrue(circuit.Gates.Count >= 1, "at least one gate is required for a non-trivial puzzle");
            Assert.IsTrue(circuit.Leds.Any(l => l.Kind == CompKind.LedRed),
                "at least one red LED (the goal) is required");
            Assert.IsTrue(circuit.Leds.All(l => l.Kind == CompKind.LedRed || l.Kind == CompKind.LedBlue),
                "LEDs must be of kind LedRed or LedBlue");
            Assert.IsTrue(circuit.Gates.All(g =>
                    g.Kind == CompKind.Or || g.Kind == CompKind.And ||
                    g.Kind == CompKind.Not || g.Kind == CompKind.Xor),
                "gates must be OR, AND, NOT or XOR");
        }

        /// <summary>
        /// Rest state (all switches OFF): the battery is live, no blue LED is
        /// lit, and the level is neither an instant win nor an instant loss.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestCircuitSimulate(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));
            Circuit.SetSwitches(circuit, 0);
            circuit.Simulate();

            Assert.IsTrue(circuit.Battery.Out, "the battery must be live");
            Assert.IsFalse(circuit.Won, "the rest state must not be an instant win");
            Assert.IsFalse(circuit.Lost, "the rest state must not be an instant loss");
            foreach (Comp led in circuit.Leds.Where(l => l.Kind == CompKind.LedBlue))
                Assert.IsFalse(led.Out, "no blue LED may be lit at rest");
        }

        /// <summary>
        /// Minimal circuit used to check the Won/Lost semantics directly:
        /// a live battery plus one red and one blue LED. No simulation is run,
        /// so the LED outputs can be driven by hand.
        /// </summary>
        /// <returns>The minimal two-LED circuit.</returns>
        private static Circuit MinimalLedCircuit()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0, Out = true };
            Comp red = new Comp { Id = 1, Kind = CompKind.LedRed, Level = 1 };
            Comp blue = new Comp { Id = 2, Kind = CompKind.LedBlue, Level = 1 };
            c.All.AddRange(new Comp[] { batt, red, blue });
            c.Battery = batt;
            c.Leds.AddRange(new Comp[] { red, blue });
            return c;
        }

        /// <summary>
        /// Won semantics: the level is won exactly when every red LED is lit
        /// and no blue LED is lit; a single lit blue LED cancels the win even
        /// with all red LEDs on.
        /// </summary>
        [TestMethod]
        public void TestCircuitWon()
        {
            Circuit c = MinimalLedCircuit();
            Comp red = c.Leds[0], blue = c.Leds[1];

            red.Out = true; blue.Out = false;
            Assert.IsTrue(c.Won, "all red LEDs on and no blue LED on: won");

            red.Out = true; blue.Out = true;
            Assert.IsFalse(c.Won, "a lit blue LED cancels the win");
        }

        /// <summary>
        /// Lost semantics: the level is lost as soon as one blue LED is lit,
        /// regardless of the red LEDs; with no blue LED lit the game continues.
        /// </summary>
        [TestMethod]
        public void TestCircuitLost()
        {
            Circuit c = MinimalLedCircuit();
            Comp red = c.Leds[0], blue = c.Leds[1];

            red.Out = false; blue.Out = true;
            Assert.IsTrue(c.Lost, "one lit blue LED is an immediate loss");

            red.Out = false; blue.Out = false;
            Assert.IsFalse(c.Lost, "no blue LED lit: the game continues");
        }

        /// <summary>
        /// Solver contract: FindWinMask returns a mask which, applied through
        /// SetSwitches, really wins the level.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestCircuitFindWinMask(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));

            int? winMask = circuit.FindWinMask();
            Assert.IsNotNull(winMask, "a validated circuit always has a winning mask");

            Circuit.SetSwitches(circuit, winMask.Value);
            circuit.Simulate();

            Assert.IsTrue(circuit.Won, "the mask returned by the solver must actually win");
        }
    }

    /// <summary>
    /// Anti-triviality constraints enforced by the generator (the rest state is
    /// inert, all-ON is not a solution, a solution exists), feed-forward DAG
    /// topology, and bit-level agreement between the solver and the
    /// human-readable win description. Contains 3 test methods.
    /// </summary>
    [TestClass]
    public class TestCircuitAdv
    {
        /// <summary>
        /// Constraints the generator must strictly enforce on every circuit:
        /// 1) the rest state (all OFF) is neither won nor lost;
        /// 2) switching every switch ON is not a win either;
        /// 3) at least one winning combination exists (the puzzle is solvable).
        /// Difficulty-gradient bounds (e.g. a maximum win ratio) are deliberately
        /// NOT asserted here: Validate does not enforce them, and a test must
        /// encode only guaranteed invariants.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
        public void TestCircuit_GenerationConstraints_AreStrictlyEnforced(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));
            int totalCombinations = 1 << circuit.Switches.Count;

            // Constraint 1 — the rest state (all switches OFF) is inert.
            Circuit.SetSwitches(circuit, 0);
            circuit.Simulate();
            Assert.IsFalse(circuit.Won, "the rest state (all OFF) must not be an instant win");
            Assert.IsFalse(circuit.Lost, "the rest state (all OFF) must not be an instant loss");

            // Constraint 2 — switching everything ON is not a trivial solution.
            Circuit.SetSwitches(circuit, totalCombinations - 1);
            circuit.Simulate();
            Assert.IsFalse(circuit.Won, "all-ON must not be a trivial win");

            // Constraint 3 — the puzzle is solvable.
            int totalWins = 0;
            for (int mask = 0; mask < totalCombinations; mask++)
            {
                Circuit.SetSwitches(circuit, mask);
                circuit.Simulate();
                if (circuit.Won) totalWins++;
            }
            Assert.IsTrue(totalWins >= 1, "a generated circuit must be solvable");
        }

        /// <summary>
        /// Topology: the circuit is a strictly feed-forward DAG — every input
        /// comes from a strictly lower level, the battery sits at level 0 and
        /// every other component sits above it.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        public void TestCircuit_GraphTopology_IsStrictlyFeedForward(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));

            foreach (Comp comp in circuit.All)
            {
                foreach (Comp inputComp in comp.Inputs)
                    Assert.IsTrue(inputComp.Level < comp.Level,
                        $"Topological violation on component ID {comp.Id} ({comp.Kind}): its input " +
                        $"ID {inputComp.Id} ({inputComp.Kind}) is at level {inputComp.Level}, " +
                        $"which is not lower than {comp.Level}.");

                if (comp.Kind == CompKind.Battery)
                    Assert.AreEqual(0, comp.Level, "the battery is not at level 0");
                else
                    Assert.IsTrue(comp.Level > 0, $"non-battery component {comp.Kind} has an invalid level ({comp.Level})");
            }
        }

        /// <summary>
        /// DescribeWinCombination must agree bit-by-bit with the mask returned
        /// by FindWinMask: for every switch S&lt;n&gt; the description contains
        /// "S&lt;n&gt;=ON" or "S&lt;n&gt;=OFF" accordingly.
        /// </summary>
        /// <param name="seed">Deterministic seed passed to the generator.</param>
        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        public void TestCircuit_DescribeWinCombination_MatchesActualWinMask(int seed)
        {
            Circuit circuit = Circuit.GenerateValid(new Random(seed));

            int? winMask = circuit.FindWinMask();
            Assert.IsNotNull(winMask, "a validated circuit always has a winning mask");

            string description = circuit.DescribeWinCombination();

            for (int i = 0; i < circuit.Switches.Count; i++)
            {
                bool isOn = (winMask.Value & (1 << i)) != 0;
                string expectedSubstring = "S" + (i + 1) + "=" + (isOn ? "ON" : "OFF");

                Assert.IsTrue(description.Contains(expectedSubstring),
                    $"the description '{description}' does not reflect switch S{i + 1}; expected {expectedSubstring}");
            }
        }
    }

    /// <summary>
    /// Deterministic gate semantics, verified on hand-built mini-circuits via
    /// the public SetSwitches/Simulate API: XOR computes the odd parity of its
    /// inputs, AND/OR/NOT follow their truth tables. Contains 2 test methods.
    /// </summary>
    [TestClass]
    public class TestGateLogic
    {
        /// <summary>
        /// Hand-built circuit: BAT, S1..S3, XOR=[S1,S2,S3] (3-input odd parity).
        /// </summary>
        /// <returns>The hand-wired XOR circuit (not simulated).</returns>
        private static Circuit BuildXor3Circuit()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0 };
            c.All.Add(batt); c.Battery = batt;

            Comp[] sw = new Comp[3];
            for (int i = 0; i < 3; i++)
            {
                sw[i] = new Comp { Id = 1 + i, Kind = CompKind.Switch, Level = 1 };
                sw[i].Inputs.Add(batt);
                c.All.Add(sw[i]); c.Switches.Add(sw[i]);
            }

            Comp xor = new Comp { Id = 4, Kind = CompKind.Xor, Level = 2 };
            xor.Inputs.AddRange(sw);
            c.All.Add(xor); c.Gates.Add(xor);
            return c;
        }

        /// <summary>
        /// Hand-built circuit: BAT, S1, S2, AND=[S1,S2], OR=[S1,S2], NOT=[S1],
        /// all at the same level below their switches.
        /// </summary>
        /// <returns>The hand-wired AND/OR/NOT circuit (not simulated).</returns>
        private static Circuit BuildAndOrNotCircuit()
        {
            Circuit c = new Circuit();
            Comp batt = new Comp { Id = 0, Kind = CompKind.Battery, Level = 0 };
            c.All.Add(batt); c.Battery = batt;

            Comp s1 = new Comp { Id = 1, Kind = CompKind.Switch, Level = 1 };
            s1.Inputs.Add(batt);
            Comp s2 = new Comp { Id = 2, Kind = CompKind.Switch, Level = 1 };
            s2.Inputs.Add(batt);
            c.All.AddRange(new Comp[] { s1, s2 });
            c.Switches.AddRange(new Comp[] { s1, s2 });

            Comp and = new Comp { Id = 3, Kind = CompKind.And, Level = 2 };
            and.Inputs.AddRange(new Comp[] { s1, s2 });
            Comp or = new Comp { Id = 4, Kind = CompKind.Or, Level = 2 };
            or.Inputs.AddRange(new Comp[] { s1, s2 });
            Comp not = new Comp { Id = 5, Kind = CompKind.Not, Level = 2 };
            not.Inputs.Add(s1);
            c.All.AddRange(new Comp[] { and, or, not });
            c.Gates.AddRange(new Comp[] { and, or, not });
            return c;
        }

        /// <summary>
        /// XOR with three inputs must output the odd parity of its inputs.
        /// Truth table (bit 0 = S1, bit 1 = S2, bit 2 = S3): 0 active inputs →
        /// false, 1 → true, 2 → false, 3 → true.
        /// </summary>
        [TestMethod]
        public void TestGate_Xor_CorrectlyCalculatesOddParity()
        {
            Circuit c = BuildXor3Circuit();
            Comp xor = c.Gates[0];

            var cases = new (int mask, bool expected)[]
            {
                (0b000, false),   // 0 active inputs (even)
                (0b001, true),    // 1 active input  (odd)
                (0b010, true),    // 1 active input  (odd)
                (0b011, false),   // 2 active inputs (even)
                (0b111, true)     // 3 active inputs (odd)
            };

            foreach (var tc in cases)
            {
                Circuit.SetSwitches(c, tc.mask);
                c.Simulate();
                Assert.AreEqual(tc.expected, xor.Out,
                    "XOR wrong with switch mask " + tc.mask + ": expected " + tc.expected);
            }
        }

        /// <summary>
        /// AND, OR and NOT must follow their truth tables for all four
        /// combinations of S1 and S2.
        /// </summary>
        [TestMethod]
        public void TestGate_AndOrNot_TruthTables()
        {
            Circuit c = BuildAndOrNotCircuit();
            Comp and = c.Gates[0], or = c.Gates[1], not = c.Gates[2];

            for (int mask = 0; mask <= 0b11; mask++)
            {
                bool s1 = (mask & 1) != 0, s2 = (mask & 2) != 0;
                Circuit.SetSwitches(c, mask);
                c.Simulate();
                Assert.AreEqual(s1 && s2, and.Out, "AND wrong with switch mask " + mask);
                Assert.AreEqual(s1 || s2, or.Out, "OR wrong with switch mask " + mask);
                Assert.AreEqual(!s1, not.Out, "NOT wrong with switch mask " + mask);
            }
        }
    }
}
