using System;
using System.Collections.Generic;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// Paints card fronts: paper, corner indices, the pips of 2–10 in their classic layouts, a large
/// ace, and an emblem panel for jacks, queens and kings. Each front design has its own paper,
/// inks, suit symbols and face-card emblems.
/// </summary>
internal static class FrontPainter
{
	private sealed record Palette(Color Paper, Color Edge, Color Red, Color Black, Color Gold, Color Panel);

	private static Palette For(CardFront front) => front switch
	{
		CardFront.Ocean => new(new("f6efdf"), new("2f6f7e"), new("e0503a"), new("143a5c"), new("d9a441"), new("e3f1f1")),
		_ => new(new("fbf8f1"), new("b9b2a4"), new("c41e3a"), new("1d1d1f"), new("caa24a"), new("f3ead6")),
	};

	public static void Draw(Control c, Vector2 origin, Card card, CardFront front)
	{
		var palette = For(front);
		var body = CardArt.Body(origin);
		CardArt.RoundedRect(c, body, 18, palette.Paper);
		CardArt.RoundedFrame(c, body, 18, palette.Edge, 2);
		var ink = card.IsRed ? palette.Red : palette.Black;

		// Corner indices: the rank with its suit below, top left and (upside down) bottom right.
		var font = ThemeDB.FallbackFont;
		void Corner(Vector2 at, bool flipped)
		{
			if (flipped)
				c.DrawSetTransform(at, Mathf.Pi, Vector2.One);
			else
				c.DrawSetTransform(at, 0, Vector2.One);
			string index = card.Index;
			int size = index.Length > 1 ? 38 : 46;
			var textSize = font.GetStringSize(index, HorizontalAlignment.Left, -1, size);
			c.DrawString(font, new Vector2(-textSize.X / 2, 0), index, HorizontalAlignment.Left, -1, size, ink);
			DrawSuit(c, card.Suit, new Vector2(0, 22), 13, ink, front);
			c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
		}
		Corner(origin + new Vector2(34, 58), false);
		Corner(origin + new Vector2(CardArt.CellWidth - 34, CardArt.CellHeight - 58), true);

		var centre = origin + new Vector2(CardArt.CellWidth / 2f, CardArt.CellHeight / 2f);
		if (card.Rank == Card.Ace)
		{
			DrawSuit(c, card.Suit, centre, 62, ink, front);
		}
		else if (card.IsFace)
		{
			FacePanel(c, origin, card, front, palette, ink);
		}
		else
		{
			// The pips sit in a 3-column grid inside the indices; lower-half pips are upside down.
			var area = new Rect2(origin + new Vector2(84, 76), new Vector2(CardArt.CellWidth - 168, CardArt.CellHeight - 152));
			foreach (var (x, y) in Layout(card.Rank))
			{
				var at = area.Position + new Vector2(x * area.Size.X, y * area.Size.Y);
				bool flip = y > 0.55f;
				if (flip)
					c.DrawSetTransform(at, Mathf.Pi, Vector2.One);
				else
					c.DrawSetTransform(at, 0, Vector2.One);
				DrawSuit(c, card.Suit, Vector2.Zero, card.Rank >= 9 ? 20 : 22, ink, front);
				c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
			}
		}
	}

	/// <summary>Where the pips go for 2–10, in 0..1 coordinates of the pip area.</summary>
	private static IEnumerable<(float X, float Y)> Layout(int rank)
	{
		const float l = 0f, m = 0.5f, r = 1f;
		return rank switch
		{
			2 => new[] { (m, 0f), (m, 1f) },
			3 => new[] { (m, 0f), (m, 0.5f), (m, 1f) },
			4 => new[] { (l, 0f), (r, 0f), (l, 1f), (r, 1f) },
			5 => new[] { (l, 0f), (r, 0f), (m, 0.5f), (l, 1f), (r, 1f) },
			6 => new[] { (l, 0f), (r, 0f), (l, 0.5f), (r, 0.5f), (l, 1f), (r, 1f) },
			7 => new[] { (l, 0f), (r, 0f), (m, 0.25f), (l, 0.5f), (r, 0.5f), (l, 1f), (r, 1f) },
			8 => new[] { (l, 0f), (r, 0f), (m, 0.25f), (l, 0.5f), (r, 0.5f), (m, 0.75f), (l, 1f), (r, 1f) },
			9 => new[] { (l, 0f), (r, 0f), (l, 1 / 3f), (r, 1 / 3f), (m, 0.5f), (l, 2 / 3f), (r, 2 / 3f), (l, 1f), (r, 1f) },
			10 => new[] { (l, 0f), (r, 0f), (m, 1 / 6f), (l, 1 / 3f), (r, 1 / 3f), (l, 2 / 3f), (r, 2 / 3f), (m, 5 / 6f), (l, 1f), (r, 1f) },
			_ => Array.Empty<(float, float)>(),
		};
	}

	// ------------------------------------------------------------------ Suit symbols

	/// <summary>Draws a suit symbol centred at a point, about 2 × size across, in the design's own shape.</summary>
	public static void DrawSuit(Control c, Cards.Suit suit, Vector2 at, float size, Color ink, CardFront front)
	{
		if (front == CardFront.Ocean)
		{
			OceanSuit(c, suit, at, size, ink);
			return;
		}
		switch (suit)
		{
			case Cards.Suit.Hearts:
				c.DrawColoredPolygon(Heart(at, size, false), ink);
				break;
			case Cards.Suit.Diamonds:
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(0, -1), new Vector2(0.72f, 0), new Vector2(0, 1), new Vector2(-0.72f, 0) }, at, size), ink);
				break;
			case Cards.Suit.Spades:
				c.DrawColoredPolygon(Heart(at + new Vector2(0, -0.08f * size), size * 0.92f, true), ink);
				Stem(c, at, size, ink);
				break;
			default:
				c.DrawCircle(at + new Vector2(0, -0.45f * size), 0.4f * size, ink);
				c.DrawCircle(at + new Vector2(-0.44f * size, 0.12f * size), 0.4f * size, ink);
				c.DrawCircle(at + new Vector2(0.44f * size, 0.12f * size), 0.4f * size, ink);
				Stem(c, at, size, ink);
				break;
		}
	}

	private static void Stem(Control c, Vector2 at, float size, Color ink) =>
		c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(-0.12f, 0.1f), new Vector2(0.12f, 0.1f), new Vector2(0.38f, 1f), new Vector2(-0.38f, 1f) }, at, size), ink);

	/// <summary>The classic heart curve (point down); flipped, the body of a spade.</summary>
	private static Vector2[] Heart(Vector2 at, float size, bool flip)
	{
		var points = new Vector2[48];
		for (int i = 0; i < points.Length; i++)
		{
			float t = Mathf.Tau * i / points.Length;
			float x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
			float y = -(13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t));
			points[i] = new Vector2(x, (y + 2.5f)) / 16.5f;
		}
		return CardArt.Place(points, at, size, flip);
	}

	/// <summary>The Ocean deck: starfish (hearts), scallop shells (diamonds), anchors (clubs), whale tails (spades).</summary>
	private static void OceanSuit(Control c, Cards.Suit suit, Vector2 at, float size, Color ink)
	{
		switch (suit)
		{
			case Cards.Suit.Hearts:
			{
				// A plump five-armed starfish.
				var points = new Vector2[10];
				for (int i = 0; i < 10; i++)
				{
					float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
					float r = i % 2 == 0 ? 1f : 0.46f;
					points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
				}
				c.DrawColoredPolygon(CardArt.Place(points, at, size * 1.05f), ink);
				c.DrawCircle(at, size * 0.16f, ink.Lightened(0.35f));
				break;
			}
			case Cards.Suit.Diamonds:
			{
				// A scallop shell: a fan with ribs and a little hinge.
				var fan = new List<Vector2> { new(0, 0.75f) };
				for (int i = 0; i <= 16; i++)
				{
					float a = Mathf.Pi * (1.08f + 0.84f * i / 16);
					fan.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.95f) * 0.98f + new Vector2(0, 0.2f));
				}
				c.DrawColoredPolygon(CardArt.Place(fan, at, size), ink);
				var rib = ink.Lightened(0.4f);
				for (int i = 1; i < 6; i++)
				{
					float a = Mathf.Pi * (1.12f + 0.76f * i / 6);
					var tip = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.95f) * 0.9f + new Vector2(0, 0.2f);
					c.DrawLine(at + new Vector2(0, 0.7f) * size, at + tip * size, rib, Mathf.Max(1.5f, size * 0.07f), true);
				}
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(-0.3f, 0.62f), new Vector2(0.3f, 0.62f), new Vector2(0.2f, 0.95f), new Vector2(-0.2f, 0.95f) }, at, size), ink);
				break;
			}
			case Cards.Suit.Clubs:
			{
				// An anchor: ring, stock, shank and curved arms with flukes.
				float w = Mathf.Max(2f, size * 0.17f);
				c.DrawArc(at + new Vector2(0, -0.78f * size), size * 0.2f, 0, Mathf.Tau, 20, ink, w * 0.8f, true);
				c.DrawLine(at + new Vector2(-0.42f, -0.46f) * size, at + new Vector2(0.42f, -0.46f) * size, ink, w, true);
				c.DrawLine(at + new Vector2(0, -0.6f) * size, at + new Vector2(0, 0.86f) * size, ink, w, true);
				c.DrawArc(at + new Vector2(0, 0.18f * size), size * 0.7f, Mathf.Pi * 0.12f, Mathf.Pi * 0.88f, 20, ink, w, true);
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(-0.86f, 0.2f), new Vector2(-0.56f, 0.38f), new Vector2(-0.8f, 0.52f) }, at, size), ink);
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(0.86f, 0.2f), new Vector2(0.56f, 0.38f), new Vector2(0.8f, 0.52f) }, at, size), ink);
				break;
			}
			default:
			{
				// A whale's tail rising from the water: two flukes over a stem, traced as one simple outline.
				var tail = new List<Vector2>();
				for (int i = 0; i <= 10; i++) // left fluke, top edge: tip to the notch
				{
					float t = i / 10f;
					tail.Add(new Vector2(-1f + t, -0.5f + 0.28f * t - 0.22f * Mathf.Sin(t * Mathf.Pi)));
				}
				for (int i = 1; i <= 10; i++) // right fluke, top edge: notch to the tip
				{
					float t = i / 10f;
					tail.Add(new Vector2(t, -0.22f - 0.28f * t - 0.22f * Mathf.Sin(t * Mathf.Pi)));
				}
				for (int i = 1; i <= 8; i++) // right fluke, underside: tip back to the stem
				{
					float t = i / 8f;
					tail.Add(new Vector2(1f - 0.84f * t, -0.5f + 0.62f * t + 0.12f * Mathf.Sin(t * Mathf.Pi)));
				}
				tail.Add(new Vector2(0.24f, 0.92f));
				tail.Add(new Vector2(-0.24f, 0.92f));
				for (int i = 0; i < 8; i++) // left fluke, underside: stem back towards the tip
				{
					float t = i / 8f;
					tail.Add(new Vector2(-0.16f - 0.84f * t, 0.12f - 0.62f * t + 0.12f * Mathf.Sin(t * Mathf.Pi)));
				}
				c.DrawColoredPolygon(CardArt.Place(tail, at, size), ink);
				break;
			}
		}
	}

	// ------------------------------------------------------------------ Face cards

	/// <summary>Jacks, queens and kings: a framed panel with the design's emblem and the suit.</summary>
	private static void FacePanel(Control c, Vector2 origin, Card card, CardFront front, Palette palette, Color ink)
	{
		var panel = new Rect2(origin + new Vector2(62, 52), new Vector2(CardArt.CellWidth - 124, CardArt.CellHeight - 104));
		CardArt.RoundedRect(c, panel, 10, palette.Panel);
		CardArt.RoundedFrame(c, panel, 10, palette.Gold, 3);
		CardArt.RoundedFrame(c, panel.Grow(-8), 6, ink with { A = 0.5f }, 1);
		var centre = panel.GetCenter();
		var emblem = centre + new Vector2(0, -26);
		if (front == CardFront.Ocean)
			OceanEmblem(c, card.Rank, emblem, palette, ink);
		else
			ClassicEmblem(c, card.Rank, emblem, palette, ink);
		DrawSuit(c, card.Suit, centre + new Vector2(0, 74), 22, ink, front);
		// The rank's name along the foot of the panel.
		var font = ThemeDB.FallbackFont;
		string name = Card.RankName(card.Rank).ToUpperInvariant();
		var size = font.GetStringSize(name, HorizontalAlignment.Left, -1, 18);
		c.DrawString(font, new Vector2(centre.X - size.X / 2, panel.End.Y - 14), name, HorizontalAlignment.Left, -1, 18, ink with { A = 0.75f });
	}

	/// <summary>Classic: a king's crown, a queen's tiara, a jack's shield.</summary>
	private static void ClassicEmblem(Control c, int rank, Vector2 at, Palette palette, Color ink)
	{
		var gold = palette.Gold;
		switch (rank)
		{
			case Card.King:
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(-1, 0.55f), new Vector2(-1, -0.5f), new Vector2(-0.5f, 0), new Vector2(0, -0.85f), new Vector2(0.5f, 0), new Vector2(1, -0.5f), new Vector2(1, 0.55f) }, at, 44), gold);
				c.DrawRect(new Rect2(at + new Vector2(-44, 22), new Vector2(88, 14)), gold.Darkened(0.2f));
				foreach (float x in new[] { -1f, 0f, 1f })
					c.DrawCircle(at + new Vector2(x * 44, x == 0 ? -38 : -22), 7, ink);
				c.DrawCircle(at + new Vector2(0, 29), 5, ink);
				break;
			case Card.Queen:
				c.DrawColoredPolygon(CardArt.Ellipse(at + new Vector2(0, 14), 46, 26, 32, Mathf.Pi, Mathf.Tau), gold);
				c.DrawRect(new Rect2(at + new Vector2(-46, 12), new Vector2(92, 12)), gold.Darkened(0.2f));
				for (int i = -2; i <= 2; i++)
					c.DrawCircle(at + new Vector2(i * 20, -10 - (2 - Mathf.Abs(i)) * 8), 6, i == 0 ? ink : gold.Lightened(0.3f));
				c.DrawCircle(at + new Vector2(0, -38), 9, ink);
				break;
			default:
				// The jack's shield with a chevron.
				var shield = new[] { new Vector2(-0.8f, -0.8f), new Vector2(0.8f, -0.8f), new Vector2(0.8f, 0.05f), new Vector2(0, 0.85f), new Vector2(-0.8f, 0.05f) };
				c.DrawColoredPolygon(CardArt.Place(shield, at, 44), gold);
				c.DrawColoredPolygon(CardArt.Place(new[] { new Vector2(-0.8f, -0.1f), new Vector2(0, 0.4f), new Vector2(0.8f, -0.1f), new Vector2(0.8f, 0.15f), new Vector2(0, 0.65f), new Vector2(-0.8f, 0.15f) }, at, 44), ink);
				break;
		}
	}

	/// <summary>Ocean: a king's trident, a queen's pearl in an open shell, a jack's ship's wheel.</summary>
	private static void OceanEmblem(Control c, int rank, Vector2 at, Palette palette, Color ink)
	{
		var gold = palette.Gold;
		switch (rank)
		{
			case Card.King:
				c.DrawLine(at + new Vector2(0, 50), at + new Vector2(0, -28), gold, 8, true);
				c.DrawLine(at + new Vector2(-30, -14), at + new Vector2(30, -14), gold, 7, true);
				foreach (float x in new[] { -30f, 0f, 30f })
				{
					c.DrawLine(at + new Vector2(x, -14), at + new Vector2(x, -42), gold, 7, true);
					c.DrawColoredPolygon(new[] { at + new Vector2(x - 9, -40), at + new Vector2(x, -58), at + new Vector2(x + 9, -40) }, gold);
				}
				c.DrawCircle(at + new Vector2(0, 6), 7, ink);
				break;
			case Card.Queen:
				c.DrawColoredPolygon(CardArt.Ellipse(at + new Vector2(0, 18), 48, 26, 24, 0, Mathf.Pi), gold.Darkened(0.15f));
				c.DrawColoredPolygon(CardArt.Ellipse(at + new Vector2(0, 0), 48, 40, 24, Mathf.Pi, Mathf.Tau), gold);
				for (int i = 1; i < 6; i++)
				{
					float a = Mathf.Pi + Mathf.Pi * i / 6;
					c.DrawLine(at, at + new Vector2(Mathf.Cos(a) * 46, Mathf.Sin(a) * 38), gold.Darkened(0.25f), 2, true);
				}
				c.DrawCircle(at + new Vector2(0, 10), 13, new Color("fdfbf6"));
				c.DrawCircle(at + new Vector2(-4, 6), 4, Colors.White);
				break;
			default:
				// A ship's wheel: rim, hub and eight handled spokes.
				c.DrawArc(at, 34, 0, Mathf.Tau, 40, gold, 7, true);
				for (int i = 0; i < 8; i++)
				{
					float a = Mathf.Tau * i / 8;
					var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
					c.DrawLine(at, at + dir * 50, gold, 5, true);
					c.DrawCircle(at + dir * 52, 6, gold.Darkened(0.2f));
				}
				c.DrawCircle(at, 12, ink);
				break;
		}
	}
}
