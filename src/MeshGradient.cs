using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum MeshGradientNodeMode { Smooth, Corner }

    [Serializable]
    public sealed class MeshGradient
    {
        public const int MaxCells = 16;
        public int columns = 1, rows = 1;
        public WhimTexGradientColorMode colorMode = WhimTexGradientColorMode.Perceptual;
        public Vector2[] positions;
        public Color[] colors;
        public MeshGradientNodeMode[] nodeModes;
        public Vector3[] patchPriorities;
        public int Width => columns * 3 + 1;
        public int Height => rows * 3 + 1;
        public int NodeCount => (columns + 1) * (rows + 1);

        public MeshGradient()
        {
            positions = new Vector2[16]; colors = new Color[16];
            nodeModes = new MeshGradientNodeMode[4];
            patchPriorities = new[] { PatchPriority(0, 0) };
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                positions[y * 4 + x] = new Vector2(x / 3f, y / 3f);
                colors[y * 4 + x] = new Color(1 - x / 3f, 0, 0, 1);
            }
        }

        public MeshGradient Clone()
        {
            var copy = (MeshGradient)MemberwiseClone();
            copy.positions = (Vector2[])positions.Clone(); copy.colors = (Color[])colors.Clone();
            copy.nodeModes = nodeModes == null || nodeModes.Length == 0 ? new MeshGradientNodeMode[NodeCount] : (MeshGradientNodeMode[])nodeModes.Clone();
            copy.patchPriorities = new Vector3[columns * rows];
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++) copy.patchPriorities[r * columns + c] = PatchPriority(c, r);
            return copy;
        }

        public static MeshGradient Create(WhimTexGradient palette, int columns = 1, int rows = 1)
        {
            var mesh = new MeshGradient();
            Color left = mesh.ToWorking(palette.EvaluateEncoded(0)), right = mesh.ToWorking(palette.EvaluateEncoded(1));
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                mesh.colors[y * 4 + x] = Color.LerpUnclamped(left, right, x / 3f);
            columns = Mathf.Clamp(columns, 1, MaxCells); rows = Mathf.Clamp(rows, 1, MaxCells);
            for (int c = 1; c < columns; c++) mesh.SplitColumn(c - 1, 1f / (columns - c + 1));
            for (int r = 1; r < rows; r++) mesh.SplitRow(r - 1, 1f / (rows - r + 1));
            mesh.patchPriorities = null;
            var priorities = new Vector3[columns * rows];
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++) priorities[r * columns + c] = mesh.PatchPriority(c, r);
            mesh.patchPriorities = priorities;
            return mesh;
        }

        public int NodeIndex(int node)
        {
            if (node < 0 || node >= NodeCount) throw new ArgumentOutOfRangeException(nameof(node));
            return node / (columns + 1) * 3 * Width + node % (columns + 1) * 3;
        }
        public Vector2 NodePosition(int node) => positions[NodeIndex(node)];
        public Color NodeColor(int node) => FromWorking(colors[NodeIndex(node)], false);
        public MeshGradientNodeMode NodeMode(int node)
        {
            NodeIndex(node);
            return nodeModes == null || nodeModes.Length == 0 ? MeshGradientNodeMode.Smooth : nodeModes[node];
        }

        public void SetNodeMode(int node, MeshGradientNodeMode mode)
        {
            NodeIndex(node);
            if (mode != MeshGradientNodeMode.Smooth && mode != MeshGradientNodeMode.Corner)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (nodeModes == null || nodeModes.Length == 0) nodeModes = new MeshGradientNodeMode[NodeCount];
            if (nodeModes[node] == mode) return;
            nodeModes[node] = mode;
            if (mode != MeshGradientNodeMode.Smooth) return;
            for (int axis = 0; axis < 4; axis += 2)
            {
                int left = HandleIndex(node, axis), right = HandleIndex(node, axis + 1);
                if (left < 0 || right < 0) continue;
                Vector2 center = NodePosition(node), a = positions[left] - center, b = positions[right] - center;
                Vector2 tangent = b.normalized - a.normalized;
                if (tangent.sqrMagnitude < 1e-12f) tangent = b.sqrMagnitude > 0 ? b : -a;
                tangent.Normalize();
                MoveHandleControls(node, axis, center - tangent * a.magnitude - positions[left]);
                MoveHandleControls(node, axis + 1, center + tangent * b.magnitude - positions[right]);
            }
        }

        public void SetNodeColor(int node, Color color)
        {
            if (!Finite(color) || color.a < 0 || color.a > 1) throw new ArgumentException("Mesh node color must be finite with alpha in 0..1.");
            int index = NodeIndex(node), x = index % Width, y = index / Width;
            Color delta = ToWorking(color) - colors[index];
            for (int j = Mathf.Max(0, y - 1); j <= Mathf.Min(Height - 1, y + 1); j++)
                for (int i = Mathf.Max(0, x - 1); i <= Mathf.Min(Width - 1, x + 1); i++) colors[j * Width + i] += delta;
        }

        public void SetColorMode(WhimTexGradientColorMode mode)
        {
            ValidateMode(mode);
            if (mode == colorMode) return;
            var encoded = new Color[colors.Length];
            for (int i = 0; i < colors.Length; i++) encoded[i] = FromWorking(colors[i], false, false);
            colorMode = mode;
            for (int i = 0; i < colors.Length; i++) colors[i] = ToWorking(encoded[i]);
        }

        public void SetNodePosition(int node, Vector2 position)
        {
            int index = NodeIndex(node), x = index % Width, y = index / Width;
            Vector2 delta = position - positions[index];
            MoveControls(x - 1, y - 1, x + 1, y + 1, delta);
        }

        public int HandleIndex(int node, int direction)
        {
            int index = NodeIndex(node), x = index % Width, y = index / Width;
            return direction switch
            {
                0 => x > 0 ? index - 1 : -1,
                1 => x < Width - 1 ? index + 1 : -1,
                2 => y > 0 ? index - Width : -1,
                3 => y < Height - 1 ? index + Width : -1,
                _ => -1
            };
        }
        public void SetHandlePosition(int node, int direction, Vector2 position)
        {
            if (!Finite(position)) throw new ArgumentException("Mesh position must be finite.");
            int index = HandleIndex(node, direction);
            if (index < 0) return;
            MoveHandleControls(node, direction, position - positions[index]);
            int opposite = HandleIndex(node, direction ^ 1);
            if (NodeMode(node) != MeshGradientNodeMode.Smooth || opposite < 0) return;
            Vector2 center = NodePosition(node), tangent = position - center;
            if (tangent.sqrMagnitude < 1e-12f) return;
            Vector2 aligned = center - tangent.normalized * (positions[opposite] - center).magnitude;
            MoveHandleControls(node, direction ^ 1, aligned - positions[opposite]);
        }
        private void MoveHandleControls(int node, int direction, Vector2 delta)
        {
            int index = HandleIndex(node, direction);
            if (index < 0) return;
            int x = index % Width, y = index / Width;
            // Carry the adjacent tensor controls with the boundary handle.
            MoveControls(direction < 2 ? x : x - 1, direction < 2 ? y - 1 : y,
                direction < 2 ? x : x + 1, direction < 2 ? y + 1 : y, delta);
        }
        public void MoveEdge(int node, int direction, Vector2 delta)
        {
            if (!Finite(delta)) throw new ArgumentException("Mesh movement must be finite.");
            int handle = HandleIndex(node, direction);
            if (handle < 0) return;
            int other = node + (direction < 2 ? (direction == 0 ? -1 : 1) : (direction == 2 ? -columns - 1 : columns + 1));
            int opposite = HandleIndex(other, direction ^ 1);
            Vector2 first = positions[handle] + delta, second = positions[opposite] + delta;
            SetHandlePosition(node, direction, first);
            SetHandlePosition(other, direction ^ 1, second);
        }
        private void MoveControls(int x0, int y0, int x1, int y1, Vector2 delta)
        {
            if (!Finite(delta)) throw new ArgumentException("Mesh position must be finite.");
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(Height - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
                    positions[y * Width + x] += delta;
        }

        public void SplitColumn(int cell, float t = .5f) => Split(true, cell, t);
        public void SplitRow(int cell, float t = .5f) => Split(false, cell, t);
        internal Vector3 PatchPriority(int column, int row)
        {
            int index = row * columns + column;
            if (patchPriorities != null && patchPriorities.Length != 0) return patchPriorities[index];
            float count = columns * rows;
            return new Vector3((index + .01f) / count, .98f / (1002 * count), .98f * 1000 / (1002 * count));
        }
        private void Split(bool horizontal, int cell, float t)
        {
            int count = horizontal ? columns : rows;
            if (cell < 0 || cell >= count || count >= MaxCells || !float.IsFinite(t) || t < .0001f || t > .9999f)
                throw new ArgumentOutOfRangeException(nameof(cell), "Mesh subdivision requires an existing cell, room for another cell and an interior split.");
            int width = Width, height = Height;
            int nextWidth = width + (horizontal ? 3 : 0), nextHeight = height + (horizontal ? 0 : 3);
            var nextPositions = new Vector2[nextWidth * nextHeight]; var nextColors = new Color[nextPositions.Length];
            int lines = horizontal ? height : width, length = horizontal ? width : height, begin = cell * 3;
            var p = new Vector2[7]; var c = new Color[7];
            for (int line = 0; line < lines; line++)
            {
                int Index(int at) => horizontal ? line * width + at : at * width + line;
                for (int k = 0; k < 4; k++) { p[k] = positions[Index(begin + k)]; c[k] = colors[Index(begin + k)]; }
                SplitCubic(p, t); SplitCubic(c, t);
                for (int at = 0; at < length + 3; at++)
                {
                    int dest = horizontal ? line * nextWidth + at : at * nextWidth + line;
                    if (at >= begin && at <= begin + 6) { nextPositions[dest] = p[at - begin]; nextColors[dest] = c[at - begin]; }
                    else { int source = Index(at < begin ? at : at - 3); nextPositions[dest] = positions[source]; nextColors[dest] = colors[source]; }
                }
            }
            int nodeWidth = columns + 1, nextNodeWidth = nodeWidth + (horizontal ? 1 : 0);
            int nextColumns = columns + (horizontal ? 1 : 0), nextRows = rows + (horizontal ? 0 : 1);
            var nextPriorities = new Vector3[nextColumns * nextRows];
            for (int y = 0; y < nextRows; y++) for (int x = 0; x < nextColumns; x++)
            {
                int at = horizontal ? x : y;
                var priority = PatchPriority(x - (horizontal && at > cell ? 1 : 0), y - (!horizontal && at > cell ? 1 : 0));
                int component = horizontal ? 1 : 2;
                if (at == cell) priority[component] *= t;
                else if (at == cell + 1) { priority.x += priority[component] * t; priority[component] *= 1 - t; }
                nextPriorities[y * nextColumns + x] = priority;
            }
            var nextModes = new MeshGradientNodeMode[(columns + (horizontal ? 2 : 1)) * (rows + (horizontal ? 1 : 2))];
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                int nx = x + (horizontal && x > cell ? 1 : 0), ny = y + (!horizontal && y > cell ? 1 : 0);
                nextModes[ny * nextNodeWidth + nx] = NodeMode(y * nodeWidth + x);
            }
            if (horizontal) columns++; else rows++;
            positions = nextPositions; colors = nextColors;
            nodeModes = nextModes;
            patchPriorities = nextPriorities;
        }
        private static void SplitCubic(Vector2[] p, float t)
        {
            Vector2 a = Vector2.LerpUnclamped(p[0], p[1], t), b = Vector2.LerpUnclamped(p[1], p[2], t), c = Vector2.LerpUnclamped(p[2], p[3], t);
            Vector2 d = Vector2.LerpUnclamped(a, b, t), e = Vector2.LerpUnclamped(b, c, t), f = Vector2.LerpUnclamped(d, e, t);
            p[6] = p[3]; p[1] = a; p[2] = d; p[3] = f; p[4] = e; p[5] = c;
        }
        private static void SplitCubic(Color[] p, float t)
        {
            Color a = Color.LerpUnclamped(p[0], p[1], t), b = Color.LerpUnclamped(p[1], p[2], t), c = Color.LerpUnclamped(p[2], p[3], t);
            Color d = Color.LerpUnclamped(a, b, t), e = Color.LerpUnclamped(b, c, t), f = Color.LerpUnclamped(d, e, t);
            p[6] = p[3]; p[1] = a; p[2] = d; p[3] = f; p[4] = e; p[5] = c;
        }
        internal static Vector4 Basis(float t)
        { float s = 1 - t; return new Vector4(s * s * s, 3 * s * s * t, 3 * s * t * t, t * t * t); }
        public Vector2 EvaluatePosition(int column, int row, float u, float v)
        {
            Vector4 bu = Basis(u), bv = Basis(v); Vector2 result = default;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) result += positions[(row * 3 + y) * Width + column * 3 + x] * (bu[x] * bv[y]);
            return result;
        }
        public Color EvaluateColor(int column, int row, float u, float v)
        {
            Vector4 bu = Basis(u), bv = Basis(v); Color result = Color.clear;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) result += colors[(row * 3 + y) * Width + column * 3 + x] * (bu[x] * bv[y]);
            return FromWorking(result, true);
        }

        private Color ToWorking(Color encoded)
        {
            var v = new GradientVector(encoded.r, encoded.g, encoded.b);
            if (colorMode == WhimTexGradientColorMode.Linear) v = GradientColorMath.Decode(v);
            else if (colorMode == WhimTexGradientColorMode.Perceptual) v = GradientColorMath.ToLab(v);
            return new Color((float)v.x, (float)v.y, (float)v.z, encoded.a);
        }
        private Color FromWorking(Color value, bool linear, bool clampAlpha = true)
        {
            var v = new GradientVector(value.r, value.g, value.b);
            if (colorMode == WhimTexGradientColorMode.Linear) v = GradientColorMath.Encode(v);
            else if (colorMode == WhimTexGradientColorMode.Perceptual) v = GradientColorMath.FromLab(v);
            if (linear) v = GradientColorMath.Decode(v);
            return new Color((float)v.x, (float)v.y, (float)v.z, clampAlpha ? Mathf.Clamp01(value.a) : value.a);
        }
        private static void ValidateMode(WhimTexGradientColorMode mode)
        {
            if (mode != WhimTexGradientColorMode.Linear && mode != WhimTexGradientColorMode.Gamma && mode != WhimTexGradientColorMode.Perceptual)
                throw new ArgumentException("Mesh supports Linear, Gamma and Perceptual color models.");
        }
        public void Validate()
        {
            ValidateMode(colorMode);
            if (columns < 1 || columns > MaxCells || rows < 1 || rows > MaxCells || positions == null || colors == null ||
                positions.Length != Width * Height || colors.Length != positions.Length) throw new ArgumentException("Invalid Mesh control lattice.");
            foreach (var color in colors) if (!Finite(color)) throw new ArgumentException("Mesh color coefficients must be finite.");
            foreach (Vector2 p in positions) if (!Finite(p)) throw new ArgumentException("Mesh controls must be finite.");
            if (patchPriorities != null && patchPriorities.Length != 0)
            {
                if (patchPriorities.Length != columns * rows) throw new ArgumentException("Invalid Mesh patch priorities.");
                foreach (Vector3 p in patchPriorities)
                    if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || p.x < 0 || p.y < 0 || p.z < 0 || (double)p.x + p.y + p.z > 1.000001)
                        throw new ArgumentException("Mesh patch priority must stay in 0..1.");
            }
            if (nodeModes != null && nodeModes.Length != 0)
            {
                if (nodeModes.Length != NodeCount) throw new ArgumentException("Invalid Mesh node modes.");
                foreach (var mode in nodeModes)
                    if (mode != MeshGradientNodeMode.Smooth && mode != MeshGradientNodeMode.Corner)
                        throw new ArgumentException("Invalid Mesh node mode.");
            }
        }
        private static bool Finite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);
        private static bool Finite(Color v) => float.IsFinite(v.r) && float.IsFinite(v.g) && float.IsFinite(v.b) && float.IsFinite(v.a);
        internal int ContentHash()
        {
            unchecked
            {
                int hash = ((columns * 397) ^ rows) * 397 ^ (int)colorMode;
                if (positions != null) foreach (var p in positions) hash = hash * 31 + p.GetHashCode();
                if (colors != null) foreach (var c in colors) hash = hash * 31 + c.GetHashCode();
                for (int i = 0; i < NodeCount; i++) hash = hash * 31 + (int)NodeMode(i);
                for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++) hash = hash * 31 + PatchPriority(c, r).GetHashCode();
                return hash;
            }
        }
    }
}
