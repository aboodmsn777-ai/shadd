// Shadd — Unity presentation layer. Attach to an empty GameObject at the board centre.
// Prefabs come from blender/build_shadd_assets.py (FBX) — see docs/GDD.md §8.
using System.Collections;
using System.Collections.Generic;
using Shadd.Core;
using UnityEngine;

namespace Shadd.View
{
    public class BoardView : MonoBehaviour
    {
        [Header("Rules")]
        [Range(3, 5)] public int radius = 4;
        [Range(2, 4)] public int players = 2;
        public bool extraTurn = false;
        public AILevel[] aiLevels = { AILevel.Medium, AILevel.Medium, AILevel.Medium }; // seats 2..4; seat 1 is human
        public bool[] seatIsAI = { false, true, false, false };

        [Header("Prefabs (from Blender)")]
        public GameObject pegPrefab, piecePrefab;
        public LineRenderer bandPrefab;
        public Color[] playerColors = { new Color(.25f, .75f, .42f), new Color(.94f, .48f, .16f), new Color(.24f, .56f, .94f), new Color(.95f, .76f, .19f) };
        public float spacing = 1f, pegPickRadius = .45f;

        Board _board; GameState _state; Vector3[] _pegPos; int _sel = -1; bool _busy; readonly System.Random _rng = new System.Random();
        readonly List<GameObject> _spawned = new List<GameObject>();
        public event System.Action<GameState> Changed;

        void Start() => NewMatch();

        public void NewMatch()
        {
            foreach (var g in _spawned) Destroy(g); _spawned.Clear();
            _board = new Board(radius, 3); _state = new GameState(_board, players, extraTurn);
            _pegPos = new Vector3[_board.Pegs.Count];
            for (int i = 0; i < _board.Pegs.Count; i++)
            {
                var p = _board.Pegs[i];
                _pegPos[i] = transform.TransformPoint(new Vector3((p.Q + p.R * .5f) * spacing, 0, p.R * spacing * Mathf.Sqrt(3) / 2));
                _spawned.Add(Instantiate(pegPrefab, _pegPos[i], Quaternion.identity, transform));
            }
            Changed?.Invoke(_state); StartCoroutine(TurnLoop());
        }

        IEnumerator TurnLoop()
        {
            while (!_state.Over)
            {
                if (seatIsAI[_state.Current])
                {
                    _busy = true; yield return new WaitForSeconds(.6f);
                    yield return Play(ShaddAI.Think(_state, aiLevels[Mathf.Max(0, _state.Current - 1)], _rng));
                }
                else { _busy = false; yield return null; }
            }
        }

        void Update()
        {
            if (_busy || _state == null || _state.Over || seatIsAI[_state.Current]) return;
            if (!Input.GetMouseButtonDown(0)) return;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!new Plane(transform.up, transform.position).Raycast(ray, out float d)) return;
            int peg = NearestPeg(ray.GetPoint(d));
            if (peg < 0) { _sel = -1; return; }
            if (_sel >= 0 && peg != _sel)
            {
                int m = _board.MoveBetween(_sel, peg);
                if (m >= 0 && _state.IsLegal(m)) { _sel = -1; StartCoroutine(Play(m)); return; }
            }
            _sel = peg;
        }

        int NearestPeg(Vector3 p)
        {
            int best = -1; float bd = pegPickRadius * pegPickRadius;
            for (int i = 0; i < _pegPos.Length; i++) { float dd = (p - _pegPos[i]).sqrMagnitude; if (dd < bd) { bd = dd; best = i; } }
            return best;
        }

        IEnumerator Play(int m)
        {
            if (m < 0) yield break;
            _busy = true;
            var res = _state.Apply(m); var mv = _board.Moves[m];
            var band = Instantiate(bandPrefab, transform); _spawned.Add(band.gameObject);
            band.startColor = band.endColor = Color.Lerp(playerColors[res.Player], Color.white, .6f);
            Vector3 a = _pegPos[mv.A] + Vector3.up * .08f, b = _pegPos[mv.B] + Vector3.up * .08f;
            band.positionCount = 2;
            for (float t = 0; t < 1; t += Time.deltaTime / .35f) { band.SetPosition(0, a); band.SetPosition(1, Vector3.LerpUnclamped(a, b, ElasticOut(t))); yield return null; }
            band.SetPosition(1, b);
            foreach (var tri in res.Claimed)
            {
                var T = _board.Tris[tri]; var c = (_pegPos[T.V[0]] + _pegPos[T.V[1]] + _pegPos[T.V[2]]) / 3f;
                var v = _pegPos[T.V[0]] - c;
                var piece = Instantiate(piecePrefab, c + Vector3.up * 2f, Quaternion.LookRotation(new Vector3(v.x, 0, v.z)), transform);
                piece.GetComponentInChildren<Renderer>().material.color = playerColors[res.Player];
                _spawned.Add(piece); StartCoroutine(Drop(piece.transform, c)); yield return new WaitForSeconds(.12f);
            }
            Changed?.Invoke(_state);
            yield return new WaitForSeconds(.3f);
            _busy = false;
        }

        static IEnumerator Drop(Transform t, Vector3 target)
        {
            Vector3 from = t.position;
            for (float k = 0; k < 1; k += Time.deltaTime / .5f) { t.position = Vector3.LerpUnclamped(from, target, BounceOut(k)); yield return null; }
            t.position = target;
        }

        static float ElasticOut(float t) => t >= 1 ? 1 : 1 - Mathf.Pow(2, -9 * t) * Mathf.Cos(t * Mathf.PI * 2.6f);
        static float BounceOut(float t)
        {
            const float n = 7.5625f, d = 2.75f;
            if (t < 1 / d) return n * t * t;
            if (t < 2 / d) return n * (t -= 1.5f / d) * t + .75f;
            if (t < 2.5 / d) return n * (t -= 2.25f / d) * t + .9375f;
            return n * (t -= 2.625f / d) * t + .984375f;
        }
    }
}
