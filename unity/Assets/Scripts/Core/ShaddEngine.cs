// Shadd — pure game logic (no UnityEngine dependency, unit-testable).
// Port of the Engine module in game/shadd.html. Keep the two in sync.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shadd.Core
{
    public readonly struct Peg { public readonly int Q, R; public Peg(int q, int r) { Q = q; R = r; } }

    public sealed class Edge { public int A, B; public readonly List<int> Tris = new List<int>(2); }
    public sealed class Tri  { public int[] V; public int[] E; }
    public sealed class Move { public int A, B; public int[] Pegs; public int[] E; }

    /// Static board topology: hex of radius R (side = R+1 pegs), bands span L unit edges.
    public sealed class Board
    {
        static readonly (int dq, int dr)[] Dirs = { (1, 0), (0, 1), (-1, 1) };
        public readonly int R, L;
        public readonly List<Peg> Pegs = new List<Peg>();
        public readonly List<Edge> Edges = new List<Edge>();
        public readonly List<Tri> Tris = new List<Tri>();
        public readonly List<Move> Moves = new List<Move>();
        readonly Dictionary<(int, int), int> _peg = new Dictionary<(int, int), int>();
        readonly Dictionary<long, int> _edge = new Dictionary<long, int>();
        readonly Dictionary<long, int> _move = new Dictionary<long, int>();

        static long Key(int a, int b) => a < b ? ((long)a << 20) | (uint)b : ((long)b << 20) | (uint)a;
        public int PegId(int q, int r) => _peg.TryGetValue((q, r), out var i) ? i : -1;
        public int MoveBetween(int a, int b) => _move.TryGetValue(Key(a, b), out var m) ? m : -1;

        public Board(int radius = 4, int bandLength = 3)
        {
            R = radius; L = bandLength;
            for (int q = -R; q <= R; q++)
                for (int r = -R; r <= R; r++)
                    if (Math.Abs(q + r) <= R) { _peg[(q, r)] = Pegs.Count; Pegs.Add(new Peg(q, r)); }

            for (int i = 0; i < Pegs.Count; i++)
                foreach (var (dq, dr) in Dirs)
                {
                    int j = PegId(Pegs[i].Q + dq, Pegs[i].R + dr);
                    if (j >= 0) EdgeId(i, j);
                }

            // Walk every lattice cell so triangles whose "anchor" peg is off-board are included.
            for (int q = -R - 1; q <= R; q++)
                for (int r = -R - 1; r <= R; r++)
                {
                    int i = PegId(q, r), a = PegId(q + 1, r), b = PegId(q, r + 1), c = PegId(q + 1, r + 1);
                    if (i >= 0 && a >= 0 && b >= 0) AddTri(i, a, b);
                    if (a >= 0 && b >= 0 && c >= 0) AddTri(a, b, c);
                }

            for (int i = 0; i < Pegs.Count; i++)
                foreach (var (dq, dr) in Dirs)
                {
                    var chain = new int[L + 1]; chain[0] = i; bool ok = true;
                    for (int k = 1; k <= L && ok; k++) { chain[k] = PegId(Pegs[i].Q + dq * k, Pegs[i].R + dr * k); ok = chain[k] >= 0; }
                    if (!ok) continue;
                    var e = new int[L];
                    for (int k = 0; k < L; k++) e[k] = _edge[Key(chain[k], chain[k + 1])];
                    _move[Key(chain[0], chain[L])] = Moves.Count;
                    Moves.Add(new Move { A = chain[0], B = chain[L], Pegs = chain, E = e });
                }
        }

        int EdgeId(int a, int b)
        {
            long k = Key(a, b);
            if (_edge.TryGetValue(k, out var e)) return e;
            _edge[k] = Edges.Count; Edges.Add(new Edge { A = Math.Min(a, b), B = Math.Max(a, b) });
            return Edges.Count - 1;
        }

        void AddTri(int x, int y, int z)
        {
            int ti = Tris.Count;
            var t = new Tri { V = new[] { x, y, z }, E = new[] { EdgeId(x, y), EdgeId(y, z), EdgeId(x, z) } };
            foreach (var e in t.E) Edges[e].Tris.Add(ti);
            Tris.Add(t);
        }
    }

    public readonly struct MoveResult { public readonly int Player; public readonly List<int> Claimed; public MoveResult(int p, List<int> c) { Player = p; Claimed = c; } }

    /// Mutable match state.
    public sealed class GameState
    {
        public readonly Board Board; public readonly int Players; public readonly bool ExtraTurn;
        public readonly byte[] Cover; public readonly sbyte[] Owner; public readonly int[] Scores;
        public readonly List<int> History = new List<int>();
        public int Current { get; private set; }
        public bool Over { get; private set; }

        public GameState(Board board, int players, bool extraTurn)
        {
            Board = board; Players = players; ExtraTurn = extraTurn;
            Cover = new byte[board.Edges.Count];
            Owner = Enumerable.Repeat((sbyte)-1, board.Tris.Count).ToArray();
            Scores = new int[players];
        }

        public bool IsLegal(int m) { foreach (var e in Board.Moves[m].E) if (Cover[e] == 0) return true; return false; }
        public List<int> LegalMoves() { var o = new List<int>(); for (int m = 0; m < Board.Moves.Count; m++) if (IsLegal(m)) o.Add(m); return o; }

        /// Triangles that playing m would complete right now.
        public List<int> Captures(int m)
        {
            var mv = Board.Moves[m]; var o = new List<int>();
            foreach (var e in mv.E)
            {
                if (Cover[e] != 0) continue;
                foreach (var t in Board.Edges[e].Tris)
                {
                    if (Owner[t] >= 0 || o.Contains(t)) continue;
                    if (Board.Tris[t].E.All(x => Cover[x] != 0 || Array.IndexOf(mv.E, x) >= 0)) o.Add(t);
                }
            }
            return o;
        }

        public MoveResult Apply(int m)
        {
            int who = Current; var got = Captures(m);
            foreach (var e in Board.Moves[m].E) Cover[e]++;
            foreach (var t in got) Owner[t] = (sbyte)who;
            Scores[who] += got.Count; History.Add(m);
            if (!(ExtraTurn && got.Count > 0)) Current = (Current + 1) % Players;
            if (LegalMoves().Count == 0) Over = true;
            return new MoveResult(who, got);
        }

        // Used by the AI for cheap look-ahead.
        internal void Push(int m, List<int> got) { foreach (var e in Board.Moves[m].E) Cover[e]++; foreach (var t in got) Owner[t] = 9; }
        internal void Pop(int m, List<int> got) { foreach (var t in got) Owner[t] = -1; foreach (var e in Board.Moves[m].E) Cover[e]--; }
    }

    public enum AILevel { Easy, Medium, Hard }

    public static class ShaddAI
    {
        public static int Think(GameState s, AILevel level, Random rng)
        {
            var ms = s.LegalMoves(); if (ms.Count == 0) return -1;
            if (level == AILevel.Easy)
            {
                var caps = ms.Select(m => s.Captures(m).Count).ToList(); int best = caps.Max();
                if (best > 0 && rng.NextDouble() < 0.65) { var c = ms.Where((m, i) => caps[i] == best).ToList(); return c[rng.Next(c.Count)]; }
                return ms[rng.Next(ms.Count)];
            }
            double bestScore = double.MinValue; var bestMoves = new List<int>();
            foreach (var m in ms)
            {
                var got = s.Captures(m); s.Push(m, got);
                // open triangles left with two covered sides = gifts to the next player
                var seen = new HashSet<int>(); int twos = 0;
                foreach (var e in s.Board.Moves[m].E)
                    foreach (var t in s.Board.Edges[e].Tris)
                    {
                        if (!seen.Add(t) || s.Owner[t] >= 0) continue;
                        if (s.Board.Tris[t].E.Count(x => s.Cover[x] != 0) == 2) twos++;
                    }
                double score;
                if (level == AILevel.Medium) score = got.Count * 10 - twos * 4 + rng.NextDouble() * 3;
                else
                {
                    bool keeps = s.ExtraTurn && got.Count > 0; int opp = 0;
                    if (!keeps) foreach (var m2 in ms) { if (m2 == m || !s.IsLegal(m2)) continue; opp = Math.Max(opp, s.Captures(m2).Count); }
                    score = got.Count * 10 - (keeps ? 0 : opp * 9) - twos * 1.5 + (keeps ? 3 : 0) + rng.NextDouble() * 0.6;
                }
                s.Pop(m, got);
                if (score > bestScore + 1e-9) { bestScore = score; bestMoves.Clear(); bestMoves.Add(m); }
                else if (Math.Abs(score - bestScore) < 1e-9) bestMoves.Add(m);
            }
            return bestMoves[rng.Next(bestMoves.Count)];
        }
    }
}
