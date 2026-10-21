using System;
using System.Collections.Generic;
using Godot;

namespace Renegade.Cards;

/// <summary>How a hand is posed: how far each finger curls at each joint, and where the thumb points.</summary>
public sealed record HandPose(float[] Curl, float ThumbAcross, float ThumbCurl, float[] Splay)
{
	/// <summary>Loose and natural: the working hand, reaching and carrying.</summary>
	public static readonly HandPose Relaxed = new(new[] { 14f, 20f, 12f }, 0.15f, 18f, new[] { -9f, -1f, 6f, 13f });

	/// <summary>Holding a fan of cards: fingers behind the cards, the thumb pressed across the front.</summary>
	public static readonly HandPose Hold = new(new[] { 26f, 34f, 22f }, 0.7f, 26f, new[] { -6f, 0f, 4f, 9f });
}

/// <summary>
/// Builds a smooth, realistic hand as a single mesh. The hand is described as a signed distance
/// field in real proportions (centimetres): a palm that widens towards the knuckles, the fleshy
/// pads at the base of the thumb and little finger, a wrist, four fingers of three tapering
/// segments and a thumb, melted together where they meet (but not finger to finger). The surface
/// is then extracted with surface nets and given normals from the field, so it shades smoothly.
/// Local frame: palm centre at the origin, fingers along +Y, palm facing +Z, and (for a left
/// hand) the thumb on the +X side. Vertex colour alpha fades the wrist out.
/// </summary>
public static class HandShape
{
	/// <summary>Table units per centimetre (cards are 6.3 cm wide and 0.63 units).</summary>
	public const float UnitsPerCm = 0.085f;

	private readonly struct Bone
	{
		public readonly Vector3 A, B;
		public readonly float Ra, Rb;
		public Bone(Vector3 a, Vector3 b, float ra, float rb) { A = a; B = b; Ra = ra; Rb = rb; }
	}

	private static readonly Dictionary<(HandPose, bool), ArrayMesh> Cache = new();

	/// <summary>The mesh for a pose; a right hand is the left hand mirrored. Built once, then cached.</summary>
	public static ArrayMesh Mesh(HandPose pose, bool right)
	{
		if (Cache.TryGetValue((pose, right), out var mesh))
			return mesh;
		var left = Cache.TryGetValue((pose, false), out var l) ? l : Cache[(pose, false)] = Build(pose, mirror: false);
		if (!right)
			return left;
		return Cache[(pose, true)] = Build(pose, mirror: true);
	}

	// ------------------------------------------------------------------ The hand, as a field

	private sealed class Field
	{
		public readonly List<Bone[]> Digits = new();

		public Field(HandPose pose)
		{
			// Fingers: index (nearest the thumb), middle, ring, little.
			float[] baseX = { 2.85f, 0.95f, -0.95f, -2.8f };
			float[] baseY = { 4.4f, 4.7f, 4.45f, 3.9f };
			float[][] lengths = { new[] { 4.0f, 2.4f, 2.0f }, new[] { 4.5f, 2.8f, 2.1f }, new[] { 4.2f, 2.6f, 2.0f }, new[] { 3.3f, 1.9f, 1.8f } };
			float[] radius = { 0.93f, 0.98f, 0.9f, 0.8f };
			for (int f = 0; f < 4; f++)
			{
				var dir = new Basis(Vector3.Back, Mathf.DegToRad(pose.Splay[f])) * Vector3.Up;
				var at = new Vector3(baseX[f], baseY[f], 0.1f);
				float r = radius[f];
				var bones = new Bone[3];
				for (int s = 0; s < 3; s++)
				{
					// Each joint bends the finger towards the palm side (+Z).
					var axis = dir.Cross(Vector3.Back).Normalized();
					dir = dir.Rotated(axis, -Mathf.DegToRad(pose.Curl[s] * (f == 3 ? 1.15f : 1f)));
					float rEnd = r * (s == 2 ? 0.78f : 0.9f);
					var end = at + dir * lengths[f][s];
					bones[s] = new Bone(at, end, r, rEnd);
					at = end;
					r = rEnd;
				}
				Digits.Add(bones);
			}

			// The thumb: from the heel of the hand on the thumb side, out and up; the more
			// "across", the more it swings over the palm and forward.
			var thumbBase = new Vector3(2.9f, -2.4f, 0.7f);
			var tdir = new Vector3(0.72f - 0.55f * pose.ThumbAcross, 0.64f, 0.28f + 0.45f * pose.ThumbAcross).Normalized();
			float[] tLen = { 4.0f, 3.0f, 2.5f };
			float[] tRad = { 1.3f, 1.08f, 0.94f };
			var tAt = thumbBase;
			var thumb = new Bone[3];
			for (int s = 0; s < 3; s++)
			{
				if (s > 0)
				{
					var axis = tdir.Cross(new Vector3(0.3f, -0.2f, 1f)).Normalized();
					tdir = tdir.Rotated(axis, -Mathf.DegToRad(pose.ThumbCurl));
				}
				var end = tAt + tdir * tLen[s];
				thumb[s] = new Bone(tAt, end, tRad[s], s == 2 ? tRad[s] * 0.82f : tRad[Math.Min(s + 1, 2)]);
				tAt = end;
			}
			Digits.Add(thumb);
		}

		public float Distance(Vector3 p)
		{
			float palm = Palm(p);
			float d = float.MaxValue;
			foreach (var digit in Digits)
			{
				float finger = float.MaxValue;
				foreach (var bone in digit)
					finger = SmoothMin(finger, Cone(p, bone), 0.35f);
				d = Mathf.Min(d, SmoothMin(palm, finger, 1.1f));
			}
			return d;
		}

		private static float Palm(Vector3 p)
		{
			// A rounded slab, narrower at the heel than at the knuckles, a little thicker in the middle.
			float t = Mathf.Clamp((p.Y + 4.6f) / 9.2f, 0, 1);
			float halfWidth = 3.55f + 0.6f * t;
			var q = new Vector3(p.X * 4.15f / halfWidth, p.Y, p.Z);
			float slab = RoundBox(q, new Vector3(4.15f, 4.6f, 1.15f), 1.05f);
			float thenar = Ellipsoid(p - new Vector3(2.3f, -2.1f, 0.55f), new Vector3(2.1f, 2.7f, 1.35f));
			float hypothenar = Ellipsoid(p - new Vector3(-2.55f, -1.9f, 0.35f), new Vector3(1.45f, 2.6f, 1.05f));
			// The wrist: a flattened tapering cone down out of the hand.
			var w = new Vector3(p.X, p.Y, p.Z / 0.66f);
			float wrist = Cone(w, new Bone(new Vector3(0, -4.2f, 0), new Vector3(0, -12.5f, 0), 2.75f, 2.35f)) * 0.66f;
			float d = SmoothMin(slab, thenar, 1.2f);
			d = SmoothMin(d, hypothenar, 1.0f);
			return SmoothMin(d, wrist, 1.6f);
		}
	}

	private static float SmoothMin(float a, float b, float k)
	{
		float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k;
		return Mathf.Min(a, b) - h * h * k * 0.25f;
	}

	/// <summary>Distance to a capsule whose radius tapers from one end to the other.</summary>
	private static float Cone(Vector3 p, Bone b)
	{
		var ab = b.B - b.A;
		float t = Mathf.Clamp((p - b.A).Dot(ab) / ab.LengthSquared(), 0, 1);
		return (p - (b.A + ab * t)).Length() - Mathf.Lerp(b.Ra, b.Rb, t);
	}

	private static float RoundBox(Vector3 p, Vector3 half, float r)
	{
		var q = new Vector3(Mathf.Abs(p.X), Mathf.Abs(p.Y), Mathf.Abs(p.Z)) - half + new Vector3(r, r, r);
		var outside = new Vector3(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0), Mathf.Max(q.Z, 0));
		return outside.Length() + Mathf.Min(Mathf.Max(q.X, Mathf.Max(q.Y, q.Z)), 0) - r;
	}

	private static float Ellipsoid(Vector3 p, Vector3 r)
	{
		float k0 = new Vector3(p.X / r.X, p.Y / r.Y, p.Z / r.Z).Length();
		float k1 = new Vector3(p.X / (r.X * r.X), p.Y / (r.Y * r.Y), p.Z / (r.Z * r.Z)).Length();
		return k0 * (k0 - 1) / Mathf.Max(k1, 1e-5f);
	}

	// ------------------------------------------------------------------ Surface nets

	private static ArrayMesh Build(HandPose pose, bool mirror)
	{
		var field = new Field(pose);
		const float step = 0.28f;
		var min = new Vector3(-6.5f, -13.5f, -4f);
		var max = new Vector3(12f, 15.5f, 11.5f);
		int nx = (int)((max.X - min.X) / step) + 1, ny = (int)((max.Y - min.Y) / step) + 1, nz = (int)((max.Z - min.Z) / step) + 1;
		var values = new float[nx * ny * nz];
		int Index(int x, int y, int z) => (z * ny + y) * nx + x;
		Vector3 Point(int x, int y, int z) => min + new Vector3(x, y, z) * step;
		for (int z = 0; z < nz; z++)
			for (int y = 0; y < ny; y++)
				for (int x = 0; x < nx; x++)
					values[Index(x, y, z)] = field.Distance(Point(x, y, z));

		// One vertex per cell the surface passes through, at the average of its edge crossings.
		var cellVertex = new int[nx * ny * nz];
		Array.Fill(cellVertex, -1);
		var positions = new List<Vector3>();
		int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
		var corner = new float[8];
		for (int z = 0; z < nz - 1; z++)
			for (int y = 0; y < ny - 1; y++)
				for (int x = 0; x < nx - 1; x++)
				{
					int inside = 0;
					for (int c = 0; c < 8; c++)
					{
						corner[c] = values[Index(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))];
						if (corner[c] < 0)
							inside++;
					}
					if (inside == 0 || inside == 8)
						continue;
					var sum = Vector3.Zero;
					int crossings = 0;
					for (int e = 0; e < 12; e++)
					{
						int a = edges[e, 0], b = edges[e, 1];
						if ((corner[a] < 0) == (corner[b] < 0))
							continue;
						float t = corner[a] / (corner[a] - corner[b]);
						var pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
						var pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
						sum += pa.Lerp(pb, t);
						crossings++;
					}
					cellVertex[Index(x, y, z)] = positions.Count;
					positions.Add(Point(x, y, z) + sum / crossings * step);
				}

		// A quad for every grid edge the surface crosses, joining the four cells around it.
		var indices = new List<int>();
		void Quad(int a, int b, int c, int d, bool flip)
		{
			if (a < 0 || b < 0 || c < 0 || d < 0)
				return;
			if (flip)
				indices.AddRange(new[] { a, c, b, a, d, c });
			else
				indices.AddRange(new[] { a, b, c, a, c, d });
		}
		for (int z = 1; z < nz - 1; z++)
			for (int y = 1; y < ny - 1; y++)
				for (int x = 0; x < nx - 1; x++)
				{
					bool a = values[Index(x, y, z)] < 0, b = values[Index(x + 1, y, z)] < 0;
					if (a != b)
						Quad(cellVertex[Index(x, y - 1, z - 1)], cellVertex[Index(x, y, z - 1)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y - 1, z)], b);
				}
		for (int z = 1; z < nz - 1; z++)
			for (int y = 0; y < ny - 1; y++)
				for (int x = 1; x < nx - 1; x++)
				{
					bool a = values[Index(x, y, z)] < 0, b = values[Index(x, y + 1, z)] < 0;
					if (a != b)
						Quad(cellVertex[Index(x - 1, y, z - 1)], cellVertex[Index(x - 1, y, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y, z - 1)], b);
				}
		for (int z = 0; z < nz - 1; z++)
			for (int y = 1; y < ny - 1; y++)
				for (int x = 1; x < nx - 1; x++)
				{
					bool a = values[Index(x, y, z)] < 0, b = values[Index(x, y, z + 1)] < 0;
					if (a != b)
						Quad(cellVertex[Index(x - 1, y - 1, z)], cellVertex[Index(x, y - 1, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x - 1, y, z)], b);
				}

		// Smooth normals from the field, the wrist fading out, scaled to the table and mirrored if asked.
		var verts = new Vector3[positions.Count];
		var normals = new Vector3[positions.Count];
		var colours = new Color[positions.Count];
		const float h = 0.12f;
		for (int i = 0; i < positions.Count; i++)
		{
			var p = positions[i];
			var n = new Vector3(
				field.Distance(p + new Vector3(h, 0, 0)) - field.Distance(p - new Vector3(h, 0, 0)),
				field.Distance(p + new Vector3(0, h, 0)) - field.Distance(p - new Vector3(0, h, 0)),
				field.Distance(p + new Vector3(0, 0, h)) - field.Distance(p - new Vector3(0, 0, h))).Normalized();
			float fade = Mathf.SmoothStep(-12f, -6.5f, p.Y);
			if (mirror)
			{
				p.X = -p.X;
				n.X = -n.X;
			}
			verts[i] = p * UnitsPerCm;
			normals[i] = n;
			colours[i] = new Color(1, 1, 1, fade);
		}
		var tris = indices.ToArray();
		// Godot draws the side whose corners run clockwise; make sure that's the outside (checked
		// against the field's normals) whichever way the quads came out.
		int outward = 0;
		for (int i = 0; i < tris.Length; i += 3)
		{
			var face = (verts[tris[i + 1]] - verts[tris[i]]).Cross(verts[tris[i + 2]] - verts[tris[i]]);
			outward += face.Dot(normals[tris[i]]) < 0 ? 1 : -1;
		}
		// (Checked on the final, possibly mirrored, data, so mirroring needs nothing extra.)
		if (outward < 0)
			for (int i = 0; i < tris.Length; i += 3)
				(tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Godot.Mesh.ArrayType.Max);
		arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
		arrays[(int)Godot.Mesh.ArrayType.Color] = colours;
		arrays[(int)Godot.Mesh.ArrayType.Index] = tris;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
