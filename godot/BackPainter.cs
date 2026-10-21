using Godot;

namespace Renegade.Cards;

/// <summary>
/// Paints card backs: a white border, then the design's pattern and a centre medallion. Backs are
/// symmetrical top to bottom, so a face-down card gives nothing away whichever way it lies.
/// </summary>
internal static class BackPainter
{
	public static void Draw(Control c, Vector2 origin, CardBack back)
	{
		var body = CardArt.Body(origin);
		CardArt.RoundedRect(c, body, 18, new Color("fbf8f1"));
		var field = body.Grow(-12);
		var (ground, line, accent) = back switch
		{
			CardBack.Navy => (new Color("1f3a5f"), new Color("8fb3d9"), new Color("e8eef5")),
			CardBack.Emerald => (new Color("1e5a3c"), new Color("d4b25a"), new Color("f3e3b0")),
			_ => (new Color("8e1d24"), new Color("e0a96d"), new Color("f6d7a7")),
		};
		CardArt.RoundedRect(c, field, 10, ground);

		// The pattern, clipped to the field by drawing only inside it.
		var centre = field.GetCenter();
		switch (back)
		{
			case CardBack.Navy:
				// Rows of waves.
				for (float y = field.Position.Y + 14; y < field.End.Y - 6; y += 16)
				{
					var points = new System.Collections.Generic.List<Vector2>();
					for (float x = field.Position.X + 4; x <= field.End.X - 4; x += 4)
						points.Add(new Vector2(x, y + Mathf.Sin((x - field.Position.X) * 0.11f + y * 0.3f) * 4));
					c.DrawPolyline(points.ToArray(), line with { A = 0.6f }, 2, true);
				}
				break;
			case CardBack.Emerald:
				// An argyle of diamonds with fine cross lines.
				for (float y = field.Position.Y; y < field.End.Y + 30; y += 44)
					for (float x = field.Position.X + ((int)((y - field.Position.Y) / 44) % 2 == 0 ? 0 : 29); x < field.End.X + 30; x += 58)
					{
						var d = new[] { new Vector2(x, y - 20), new Vector2(x + 14, y), new Vector2(x, y + 20), new Vector2(x - 14, y) };
						if (Inside(field.Grow(-4), d))
							c.DrawColoredPolygon(d, line with { A = 0.32f });
					}
				for (float x = field.Position.X - field.Size.Y; x < field.End.X; x += 29)
					ClippedLine(c, field, new Vector2(x, field.Position.Y), new Vector2(x + field.Size.Y * 0.66f, field.End.Y), line with { A = 0.35f });
				break;
			default:
				// A lattice of fine diagonal lines.
				for (float x = field.Position.X - field.Size.Y; x < field.End.X; x += 16)
				{
					ClippedLine(c, field, new Vector2(x, field.Position.Y), new Vector2(x + field.Size.Y, field.End.Y), line with { A = 0.45f });
					ClippedLine(c, field, new Vector2(x + field.Size.Y, field.Position.Y), new Vector2(x, field.End.Y), line with { A = 0.45f });
				}
				break;
		}

		// A double frame and the medallion: a ring with a four-point star.
		CardArt.RoundedFrame(c, field.Grow(-8), 8, accent, 2);
		c.DrawCircle(centre, 46, ground);
		c.DrawArc(centre, 46, 0, Mathf.Tau, 48, accent, 3, true);
		c.DrawArc(centre, 38, 0, Mathf.Tau, 48, accent with { A = 0.6f }, 1.5f, true);
		var star = new Vector2[8];
		for (int i = 0; i < 8; i++)
		{
			float a = -Mathf.Pi / 2 + i * Mathf.Pi / 4;
			star[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 30 : 9);
		}
		c.DrawColoredPolygon(star, accent);
	}

	private static bool Inside(Rect2 rect, Vector2[] points)
	{
		foreach (var p in points)
			if (!rect.HasPoint(p))
				return false;
		return true;
	}

	/// <summary>A line cut to a rectangle (Liang–Barsky).</summary>
	private static void ClippedLine(Control c, Rect2 rect, Vector2 a, Vector2 b, Color color)
	{
		float t0 = 0, t1 = 1;
		var d = b - a;
		bool Clip(float p, float q)
		{
			if (p == 0)
				return q >= 0;
			float r = q / p;
			if (p < 0)
			{
				if (r > t1) return false;
				if (r > t0) t0 = r;
			}
			else
			{
				if (r < t0) return false;
				if (r < t1) t1 = r;
			}
			return true;
		}
		if (Clip(-d.X, a.X - rect.Position.X) && Clip(d.X, rect.End.X - a.X) && Clip(-d.Y, a.Y - rect.Position.Y) && Clip(d.Y, rect.End.Y - a.Y))
			c.DrawLine(a + d * t0, a + d * t1, color, 1.5f, true);
	}
}
