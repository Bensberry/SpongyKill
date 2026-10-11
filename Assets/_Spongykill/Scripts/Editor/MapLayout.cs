using System.Collections.Generic;
using UnityEngine;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// A map's walkable floor plan on a 1 m grid, built from zones (rectangles and polygons with 0, 45
    /// or 90 degree edges) minus carved-out solid blocks. Walls are traced along the walkable boundary
    /// with marching squares: straight edges become single long walls and staircased 45-degree edges
    /// become clean diagonal walls. Plan coordinates are metres, X = east, Y = north (world Z).
    /// </summary>
    public sealed class MapLayout
    {
        public sealed class Zone
        {
            internal readonly List<Shape> Shapes = new List<Shape>();

            public string Name;
            public Material Wall;
            public Material Trim;
            /// <summary>Painted floor for this zone; null leaves the bare ground.</summary>
            public Material Floor;
            public float WallHeight = 5f;
            /// <summary>Height of the walkable surface (raised spawn platforms and the like).</summary>
            public float FloorHeight;
            public bool Windows = true;

            public IReadOnlyList<Shape> ShapeList => Shapes;

            public Zone AddRect(float x0, float y0, float x1, float y1)
            {
                Shapes.Add(Shape.FromRect(x0, y0, x1, y1));
                return this;
            }

            public Zone AddPolygon(params Vector2[] points)
            {
                Shapes.Add(Shape.FromPolygon(points));
                return this;
            }
        }

        public readonly struct Shape
        {
            readonly Vector2[] polygon;

            public readonly Rect Bounds;

            Shape(Rect bounds, Vector2[] polygon)
            {
                Bounds = bounds;
                this.polygon = polygon;
            }

            public static Shape FromRect(float x0, float y0, float x1, float y1) =>
                new Shape(Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Max(x0, x1), Mathf.Max(y0, y1)), null);

            public static Shape FromPolygon(Vector2[] points)
            {
                Vector2 min = points[0];
                Vector2 max = points[0];
                foreach (Vector2 point in points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                return new Shape(Rect.MinMaxRect(min.x, min.y, max.x, max.y), points);
            }

            public bool Contains(Vector2 point)
            {
                if (!Bounds.Contains(point)) return false;
                if (polygon == null) return true;

                // Even-odd ray casting.
                bool inside = false;
                for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                {
                    Vector2 a = polygon[i];
                    Vector2 b = polygon[j];
                    if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                return inside;
            }
        }

        public struct Wall
        {
            public Vector2 Start;
            public Vector2 End;
            /// <summary>Unit normal pointing away from the walkable side.</summary>
            public Vector2 Outward;
            /// <summary>Zone on the walkable side, which decides colour and height.</summary>
            public Zone Zone;

            public float Length => Vector2.Distance(Start, End);
        }

        readonly List<Zone> zones = new List<Zone>();
        readonly List<Shape> carves = new List<Shape>();
        bool[,] walkable;
        int[,] zoneIndex;
        int width;
        int depth;

        public IReadOnlyList<Zone> Zones => zones;

        public Zone AddZone(string name)
        {
            var zone = new Zone { Name = name };
            zones.Add(zone);
            walkable = null;
            return zone;
        }

        /// <summary>Removes a solid block (a building, a pillar) from whatever zones cover it.</summary>
        public void Carve(float x0, float y0, float x1, float y1)
        {
            carves.Add(Shape.FromRect(x0, y0, x1, y1));
            walkable = null;
        }

        public bool IsWalkable(Vector2 point)
        {
            EnsureRasterized();
            return Sample(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
        }

        public Zone ZoneAt(Vector2 point)
        {
            EnsureRasterized();
            int i = Mathf.FloorToInt(point.x);
            int j = Mathf.FloorToInt(point.y);
            if (i < 0 || j < 0 || i >= width || j >= depth || zoneIndex[i, j] < 0) return null;
            return zones[zoneIndex[i, j]];
        }

        /// <summary>Walls along the whole walkable boundary, merged into the longest straight runs per zone.</summary>
        public List<Wall> TraceWalls()
        {
            EnsureRasterized();

            // Marching squares over cell-centre samples. Edge midpoints land on whole or half metres, so
            // coordinates are doubled to keep everything in integers while merging.
            var runs = new Dictionary<(int kind, int line, int zone, int side), (Vector2 outward, List<(int from, int to)> pieces)>();
            for (int i = -1; i < width; i++)
            {
                for (int j = -1; j < depth; j++)
                {
                    int corners = (Sample(i, j) ? 1 : 0) | (Sample(i + 1, j) ? 2 : 0) | (Sample(i + 1, j + 1) ? 4 : 0) | (Sample(i, j + 1) ? 8 : 0);
                    var bottom = new Vector2Int(2 * i + 2, 2 * j + 1);
                    var right = new Vector2Int(2 * i + 3, 2 * j + 2);
                    var top = new Vector2Int(2 * i + 2, 2 * j + 3);
                    var left = new Vector2Int(2 * i + 1, 2 * j + 2);
                    switch (corners)
                    {
                        case 1: case 14: AddPiece(runs, left, bottom); break;
                        case 2: case 13: AddPiece(runs, bottom, right); break;
                        case 4: case 11: AddPiece(runs, right, top); break;
                        case 7: case 8: AddPiece(runs, top, left); break;
                        case 3: case 12: AddPiece(runs, left, right); break;
                        case 6: case 9: AddPiece(runs, bottom, top); break;
                        // Saddles: walkable cells that only touch diagonally stay separated.
                        case 5: AddPiece(runs, left, bottom); AddPiece(runs, right, top); break;
                        case 10: AddPiece(runs, bottom, right); AddPiece(runs, top, left); break;
                    }
                }
            }

            var walls = new List<Wall>();
            foreach (KeyValuePair<(int kind, int line, int zone, int side), (Vector2 outward, List<(int from, int to)> pieces)> run in runs)
            {
                List<(int from, int to)> pieces = run.Value.pieces;
                pieces.Sort((a, b) => a.from.CompareTo(b.from));
                int start = pieces[0].from;
                int end = pieces[0].to;
                for (int p = 1; p <= pieces.Count; p++)
                {
                    if (p < pieces.Count && pieces[p].from == end)
                    {
                        end = pieces[p].to;
                        continue;
                    }
                    walls.Add(new Wall
                    {
                        Start = FromRun(run.Key.kind, run.Key.line, start),
                        End = FromRun(run.Key.kind, run.Key.line, end),
                        Outward = run.Value.outward,
                        Zone = zones[run.Key.zone],
                    });
                    if (p < pieces.Count)
                    {
                        start = pieces[p].from;
                        end = pieces[p].to;
                    }
                }
            }
            return walls;
        }

        void AddPiece(Dictionary<(int, int, int, int), (Vector2, List<(int, int)>)> runs, Vector2Int p, Vector2Int q)
        {
            Vector2 a = (Vector2)p * 0.5f;
            Vector2 b = (Vector2)q * 0.5f;
            Vector2 mid = (a + b) * 0.5f;
            Vector2 direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x);
            Vector2 outward = IsWalkable(mid + normal * 0.35f) ? -normal : normal;
            Zone zone = ZoneAt(mid - outward * 0.35f);
            if (zone == null) return;

            int dx = q.x - p.x;
            int dy = q.y - p.y;
            int kind, line, from, to;
            if (dy == 0) { kind = 0; line = p.y; from = Mathf.Min(p.x, q.x); to = Mathf.Max(p.x, q.x); }
            else if (dx == 0) { kind = 1; line = p.x; from = Mathf.Min(p.y, q.y); to = Mathf.Max(p.y, q.y); }
            else if (dx * dy > 0) { kind = 2; line = p.y - p.x; from = Mathf.Min(p.x, q.x); to = Mathf.Max(p.x, q.x); }
            else { kind = 3; line = p.y + p.x; from = Mathf.Min(p.x, q.x); to = Mathf.Max(p.x, q.x); }

            int side = (int)Mathf.Sign(Mathf.Round(outward.x * 10f)) * 3 + (int)Mathf.Sign(Mathf.Round(outward.y * 10f));
            var key = (kind, line, zones.IndexOf(zone), side);
            if (!runs.TryGetValue(key, out (Vector2 outward, List<(int, int)> pieces) run))
            {
                run = (outward, new List<(int, int)>());
                runs[key] = run;
            }
            run.pieces.Add((from, to));
        }

        /// <summary>Converts a doubled run coordinate back to plan metres.</summary>
        static Vector2 FromRun(int kind, int line, int value)
        {
            switch (kind)
            {
                case 0: return new Vector2(value, line) * 0.5f;
                case 1: return new Vector2(line, value) * 0.5f;
                case 2: return new Vector2(value, value + line) * 0.5f;
                default: return new Vector2(value, line - value) * 0.5f;
            }
        }

        bool Sample(int i, int j) => i >= 0 && j >= 0 && i < width && j < depth && walkable[i, j];

        void EnsureRasterized()
        {
            if (walkable != null) return;

            float maxX = 1f;
            float maxY = 1f;
            foreach (Zone zone in zones)
            {
                foreach (Shape shape in zone.Shapes)
                {
                    maxX = Mathf.Max(maxX, shape.Bounds.xMax);
                    maxY = Mathf.Max(maxY, shape.Bounds.yMax);
                }
            }
            width = Mathf.CeilToInt(maxX) + 1;
            depth = Mathf.CeilToInt(maxY) + 1;
            walkable = new bool[width, depth];
            zoneIndex = new int[width, depth];

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < depth; j++)
                {
                    var point = new Vector2(i + 0.5f, j + 0.5f);
                    zoneIndex[i, j] = FindZone(point);
                    walkable[i, j] = zoneIndex[i, j] >= 0 && !IsCarved(point);
                }
            }
        }

        int FindZone(Vector2 point)
        {
            for (int z = 0; z < zones.Count; z++)
                foreach (Shape shape in zones[z].Shapes)
                    if (shape.Contains(point)) return z;
            return -1;
        }

        bool IsCarved(Vector2 point)
        {
            foreach (Shape carve in carves)
                if (carve.Contains(point)) return true;
            return false;
        }
    }
}
