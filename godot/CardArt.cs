using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>A design for the faces of the cards: its own suit symbols and face cards.</summary>
public enum CardFront { Classic, Ocean }

/// <summary>A design for the backs of the cards, shared by every card game.</summary>
public enum CardBack { Crimson, Navy, Emerald }

/// <summary>
/// Draws every card in code (no licensed art): a front design (paper, ink colours, suit symbols,
/// face-card emblems) and a back design. All 52 fronts are painted in one pass into an atlas
/// (13 × 4 cells, plus a fifth row with the two jokers), and each back into one cell; the 3D cards sample their cell.
/// </summary>
public static partial class CardArt
{
	public const int CellWidth = 256, CellHeight = 352;
	/// <summary>Transparent margin inside each cell, so mipmaps don't bleed between neighbours.</summary>
	public const int Inset = 8;

	/// <summary>Rows in the fronts atlas: the four suits and the jokers.</summary>
	public const int AtlasRows = 5;

	public static string Name(CardFront front) => front switch { CardFront.Ocean => "Ocean", _ => "Classic" };
	public static string Name(CardBack back) => back.ToString();

	/// <summary>Paints the 52 fronts and the two jokers into one atlas texture (columns: ace to king; rows: clubs, diamonds, hearts, spades, then the jokers).</summary>
	public static Task<ImageTexture> PaintFronts(Node host, CardFront front) =>
		Paint(host, new Vector2I(CellWidth * 13, CellHeight * AtlasRows), canvas =>
		{
			for (int i = 0; i < 54; i++)
			{
				var card = Card.FromOrdinal(i);
				var origin = new Vector2(i % 13 * CellWidth, i / 13 * CellHeight);
				FrontPainter.Draw(canvas, origin, card, front);
			}
		});

	/// <summary>Paints one back design into a single-cell texture.</summary>
	public static Task<ImageTexture> PaintBack(Node host, CardBack back) =>
		Paint(host, new Vector2I(CellWidth, CellHeight), canvas => BackPainter.Draw(canvas, Vector2.Zero, back));

	/// <summary>Draws into a transparent off-screen viewport for one frame and keeps the picture, with mipmaps.</summary>
	private static async Task<ImageTexture> Paint(Node host, Vector2I size, Action<Control> draw)
	{
		var viewport = new SubViewport
		{
			Size = size,
			TransparentBg = true,
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
		};
		var canvas = new DrawControl(draw) { Size = size };
		viewport.AddChild(canvas);
		host.AddChild(viewport);
		await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var image = viewport.GetTexture().GetImage();
		viewport.QueueFree();
		image.GenerateMipmaps();
		return ImageTexture.CreateFromImage(image);
	}

	private sealed partial class DrawControl : Control
	{
		private readonly Action<Control> _draw;
		public DrawControl(Action<Control> draw) => _draw = draw;
		public override void _Draw() => _draw(this);
	}

	// ------------------------------------------------------------------ Shared drawing helpers

	internal static void RoundedRect(Control c, Rect2 rect, float radius, Color fill)
	{
		var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
		box.SetCornerRadiusAll((int)radius);
		c.DrawStyleBox(box, rect);
	}

	internal static void RoundedFrame(Control c, Rect2 rect, float radius, Color line, int width)
	{
		var box = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = line, AntiAliasing = true, DrawCenter = false };
		box.SetCornerRadiusAll((int)radius);
		box.SetBorderWidthAll(width);
		c.DrawStyleBox(box, rect);
	}

	/// <summary>The card's outline within its cell.</summary>
	internal static Rect2 Body(Vector2 origin) =>
		new(origin + new Vector2(Inset, Inset), new Vector2(CellWidth - 2 * Inset, CellHeight - 2 * Inset));

	/// <summary>Points of a polygon given in unit coordinates (about -1..1), scaled and placed.</summary>
	internal static Vector2[] Place(IEnumerable<Vector2> unit, Vector2 centre, float size, bool flip = false)
	{
		var points = new List<Vector2>();
		foreach (var p in unit)
			points.Add(centre + (flip ? new Vector2(p.X, -p.Y) : p) * size);
		return points.ToArray();
	}

	internal static Vector2[] Ellipse(Vector2 centre, float rx, float ry, int segments = 32, float from = 0, float to = Mathf.Tau)
	{
		var points = new Vector2[segments + 1];
		for (int i = 0; i <= segments; i++)
		{
			float a = from + (to - from) * i / segments;
			points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
		}
		return points;
	}
}
