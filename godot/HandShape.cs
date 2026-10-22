using System;
using System.Collections.Generic;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// How a hand is posed: how far each finger (index, middle, ring, little) curls at each of its
/// three joints, how the fingers spread, and where the thumb goes. A pinch brings the thumb tip
/// to the index fingertip.
/// </summary>
public sealed record HandPose(string Name, float[][] Curl, float ThumbAcross, float ThumbCurl, float[] Splay, bool ThumbToIndex = false)
{
	private static float[] C(float a, float b, float c) => new[] { a, b, c };

	/// <summary>Loose and natural: the working hand at rest and reaching.</summary>
	public static readonly HandPose Relaxed = new("relaxed", new[] { C(14, 20, 12), C(14, 20, 12), C(15, 21, 12), C(17, 24, 14) }, 0.15f, 18f, new[] { -9f, -1f, 6f, 13f });

	/// <summary>Lying palm down on the table, fingers nearly flat: a hand at rest at its place.</summary>
	public static readonly HandPose Rest = new("rest", new[] { C(6, 9, 6), C(6, 9, 6), C(7, 10, 6), C(8, 11, 7) }, 0.1f, 6f, new[] { -8f, -1f, 6f, 12f });

	/// <summary>Holding a fan of cards: fingers behind the cards, the thumb pressed across the front.</summary>
	public static readonly HandPose Hold = new("hold", new[] { C(10, 14, 9), C(10, 14, 9), C(11, 15, 9), C(13, 17, 10) }, 0.75f, 24f, new[] { -6f, 0f, 4f, 9f });

	/// <summary>Picking up a card: thumb and index finger together, the other fingers tucked out of the way.</summary>
	public static readonly HandPose Pinch = new("pinch", new[] { C(40, 44, 18), C(46, 62, 38), C(50, 66, 40), C(54, 70, 42) }, 0.6f, 12f, new[] { -3f, 1f, 6f, 11f }, ThumbToIndex: true);
}

/// <summary>
/// Builds a smooth, realistic hand as a single mesh. The hand is described as a signed distance
/// field in real proportions (centimetres): a palm that widens towards the knuckles, the fleshy
/// pads at the base of the thumb and little finger, a wrist, four fingers of three tapering
/// segments and a thumb, melted together where they meet (but not finger to finger). The surface
/// is then extracted with surface nets and given normals from the field, so it shades smoothly.
/// Local frame: palm centre at the origin, fingers along +Y, palm facing +Z. The model is a
/// right hand: seen palm-on, its thumb is on the viewer's right (+X); left hands are mirrored.
/// Vertex colour alpha fades the wrist out.
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

		public static Bone Lerp(Bone x, Bone y, float t) => new(x.A.Lerp(y.A, t), x.B.Lerp(y.B, t), Mathf.Lerp(x.Ra, y.Ra, t), Mathf.Lerp(x.Rb, y.Rb, t));
	}

	private static readonly Dictionary<(string, string, int, bool), ArrayMesh> Cache = new();

	/// <summary>The mesh for a pose. Built once, then cached.</summary>
	public static ArrayMesh Mesh(HandPose pose, bool right) => Mesh(pose, pose, 0, right);

	/// <summary>The mesh part of the way from one pose to another (t from 0 to 1), for animating between them.</summary>
	public static ArrayMesh Mesh(HandPose from, HandPose to, float t, bool right)
	{
		var key = (from.Name, to.Name, (int)Mathf.Round(t * 100), right);
		if (Cache.TryGetValue(key, out var mesh))
			return mesh;
		var a = Digits(from);
		var b = Digits(to);
		var blend = new List<Bone[]>();
		for (int d = 0; d < a.Count; d++)
		{
			var bones = new Bone[a[d].Length];
			for (int s = 0; s < bones.Length; s++)
				bones[s] = Bone.Lerp(a[d][s], b[d][s], t);
			blend.Add(bones);
		}
		return Cache[key] = Build(new Field(blend), mirror: !right);
	}

	/// <summary>Where a pinched card sits, between the thumb and index fingertips, in the hand's frame (table units).</summary>
	public static Vector3 PinchPoint(bool right)
	{
		var digits = Digits(HandPose.Pinch);
		var point = (digits[0][2].B + digits[4][2].B) / 2 * UnitsPerCm;
		return right ? point : new Vector3(-point.X, point.Y, point.Z);
	}

	// ------------------------------------------------------------------ The hand, as a field

	/// <summary>The bones of a pose: index, middle, ring and little fingers, then the thumb (three each).</summary>
	private static List<Bone[]> Digits(HandPose pose)
	{
		var digits = new List<Bone[]>();
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
				// Each joint bends the finger towards the palm (+Z).
				var axis = dir.Cross(Vector3.Back).Normalized();
				dir = dir.Rotated(axis, Mathf.DegToRad(pose.Curl[f][s]));
				float rEnd = r * (s == 2 ? 0.78f : 0.9f);
				var end = at + dir * lengths[f][s];
				bones[s] = new Bone(at, end, r, rEnd);
				at = end;
				r = rEnd;
			}
			digits.Add(bones);
		}

		var thumbBase = new Vector3(2.9f, -2.4f, 0.7f);
		float[] tLen = { 4.0f, 3.0f, 2.5f };
		float[] tRad = { 1.3f, 1.08f, 0.94f };
		var thumb = new Bone[3];
		if (pose.ThumbToIndex)
		{
			// A pinch: the thumb arcs out in front of the palm to meet the index fingertip.
			var index = digits[0][2];
			var towards = (thumbBase - index.B).Normalized();
			var tip = index.B + towards * (index.Rb + tRad[2] * 0.82f) * 0.85f;
			var bulge = (thumbBase + tip) / 2 + new Vector3(0.9f, 0, 2.2f);
			Vector3 At(float t) => thumbBase * (1 - t) * (1 - t) + bulge * 2 * t * (1 - t) + tip * t * t;
			float[] ts = { 0, 0.42f, 0.74f, 1 };
			for (int s = 0; s < 3; s++)
				thumb[s] = new Bone(At(ts[s]), At(ts[s + 1]), tRad[s], s == 2 ? tRad[s] * 0.82f : tRad[s + 1]);
		}
		else
		{
			// From the heel of the hand on the thumb side, out and up; the more "across", the more
			// it swings over the palm and forward.
			var tdir = new Vector3(0.72f - 0.55f * pose.ThumbAcross, 0.64f, 0.28f + 0.45f * pose.ThumbAcross).Normalized();
			var tAt = thumbBase;
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
		}
		digits.Add(thumb);
		return digits;
	}

	private sealed class Field
	{
		public readonly List<Bone[]> Digits;

		public Field(List<Bone[]> digits) => Digits = digits;

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

	private static ArrayMesh Build(Field field, bool mirror)
	{
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
