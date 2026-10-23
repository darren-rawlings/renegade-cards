using System.Collections.Generic;
using Godot;
using Renegade.Play;

namespace Renegade.Cards;

/// <summary>The colours of table cloth on offer in Style.</summary>
public enum TableCloth { Green, Blue, Red, Purple, Charcoal }

/// <summary>
/// A card table for 2 to 5 players: the studio's shared stage (lights, reflections, the wooden
/// table) with a felt cloth on top, a fixed camera (card tables don't spin), and the places a game
/// uses at each seat: the fan of cards, the dealt pile, the winnings, where cards thrown to that
/// player land, and where their hand rests. Each seat has its own place setting stitched into the
/// felt. Seat 0 is you, at the bottom of the portrait screen; the others sit round the far side
/// of the table, left to right. Set <see cref="Seats"/> before adding the table, or call
/// <see cref="Arrange"/> to change it.
/// </summary>
public partial class CardTable : Node3D
{
	public const float FeltY = 0.002f;
	public const int MaxSeats = 5;

	/// <summary>The edges of the felt (not solitaire): half its width, and its far and near ends.</summary>
	public const float FeltHalfWidth = 3.0f, FeltFar = -4.3f, FeltNear = 3.1f;

	/// <summary>
	/// Where another player sits (x, z on the felt), which way they face, and how far in front of
	/// them their place setting lies (its centre).
	/// </summary>
	public readonly record struct Place(Vector2 At, Vector2 Facing, float Reach = 0.75f);

	private static Shader? _felt;
	private static Shader? _matShader;
	private ShaderMaterial _feltMaterial = null!;
	private readonly List<MeshInstance3D> _mats = new();
	private Color _cloth = ClothColour(TableCloth.Green);

	public Camera3D Camera { get; private set; } = null!;

	/// <summary>How many players sit at the table (2 to 5).</summary>
	public int Seats { get; set; } = 2;

	/// <summary>Where each player's fan is held (seat 0 is you); its +Z faces the player holding it.</summary>
	public Transform3D[] FanPivot { get; } = new Transform3D[MaxSeats];

	/// <summary>Where each player's dealt cards land before they're picked up.</summary>
	public Vector3[] Pile { get; } = new Vector3[MaxSeats];

	/// <summary>Where each player lays down what they win (books, tricks).</summary>
	public Vector3[] Winnings { get; } = new Vector3[MaxSeats];

	/// <summary>Where cards thrown to a player land, in front of them.</summary>
	public Vector3[] Landing { get; } = new Vector3[MaxSeats];

	/// <summary>Where each player's working hand lies palm down between jobs, at their place.</summary>
	public Vector3[] HandRest { get; } = new Vector3[MaxSeats];

	/// <summary>Where each player's left hand lies palm down until it has cards to hold.</summary>
	public Vector3[] HolderRest { get; } = new Vector3[MaxSeats];

	/// <summary>Where each player's speech bubble shows (kept for games that still point at a seat).</summary>
	public Vector3[] Bubble { get; } = new Vector3[MaxSeats];

	public Vector3 Centre { get; private set; } = new(0, FeltY, -0.35f);

	/// <summary>A solitaire layout: no seats, the camera looking steeply down over a wider felt.</summary>
	public bool Solitaire { get; set; }

	/// <summary>How wide the solitaire layout is, in table units (the camera frames it edge to edge).</summary>
	public float SolitaireWidth { get; set; } = 5.3f;

	public static Color ClothColour(TableCloth cloth) => cloth switch
	{
		TableCloth.Blue => new Color(0.09f, 0.22f, 0.40f),
		TableCloth.Red => new Color(0.42f, 0.08f, 0.09f),
		TableCloth.Purple => new Color(0.26f, 0.12f, 0.36f),
		TableCloth.Charcoal => new Color(0.16f, 0.17f, 0.18f),
		_ => new Color(0.10f, 0.34f, 0.22f),
	};

	public static string Name(TableCloth cloth) => cloth.ToString();

	public override void _Ready()
	{
		Stage.Build(this, 0, new Color("120c08"));
		if (Solitaire)
		{
			// Nearly straight down, framed so the layout's width fills the screen with a margin.
			float distance = (SolitaireWidth + 0.5f) / 2 / Mathf.Tan(Mathf.DegToRad(36 / 2f));
			Camera = new Camera3D { Fov = 36, KeepAspect = Camera3D.KeepAspectEnum.Width, Position = new Vector3(0, distance, 1.6f) };
			AddChild(Camera);
			Camera.LookAt(new Vector3(0, 0, 0.35f));
			Centre = new Vector3(0, FeltY, 0);
		}
		else
		{
			Camera = new Camera3D { Fov = 36, KeepAspect = Camera3D.KeepAspectEnum.Width, Position = new Vector3(0, 6.6f, 5.4f) };
			AddChild(Camera);
			Camera.LookAt(new Vector3(0, 0, -0.55f));
		}

		_feltMaterial = new ShaderMaterial { Shader = _felt ??= new Shader { Code = FeltCode } };
		_feltMaterial.SetShaderParameter("felt", _cloth);
		var felt = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = Solitaire ? new Vector2(SolitaireWidth + 1.2f, (SolitaireWidth + 1.2f) * 2.1f) : new Vector2(FeltHalfWidth * 2, FeltNear - FeltFar) },
			Position = Solitaire ? new Vector3(0, 0.001f, 0.6f) : new Vector3(0, 0.001f, (FeltNear + FeltFar) / 2),
			MaterialOverride = _feltMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(felt);
		if (Solitaire)
			Bubble[0] = Centre + new Vector3(0, 1f, 0);
		else
			Arrange(Seats);
	}

	/// <summary>Changes the colour of the cloth (and the place settings stitched into it).</summary>
	public void SetCloth(TableCloth cloth)
	{
		_cloth = ClothColour(cloth);
		_feltMaterial?.SetShaderParameter("felt", _cloth);
		foreach (var mat in _mats)
			((ShaderMaterial)mat.MaterialOverride).SetShaderParameter("felt", _cloth);
	}

	/// <summary>
	/// Where the other players sit (x, z on the felt) and which way they face, for each table size,
	/// left to right. Every seat faces straight across the table, so the layout is symmetrical and
	/// the place settings sit square: players at the far side face you, players at the sides face
	/// each other.
	/// </summary>
	private static readonly (Vector2 At, Vector2 Facing)[][] Around =
	{
		new (Vector2, Vector2)[0],
		new (Vector2, Vector2)[0],
		new[] { (new Vector2(0, -2.55f), Vector2.Down) },
		new[] { (new Vector2(-1.25f, -2.45f), Vector2.Down), (new Vector2(1.25f, -2.45f), Vector2.Down) },
		new[] { (new Vector2(-1.78f, -0.35f), Vector2.Right), (new Vector2(0, -2.55f), Vector2.Down), (new Vector2(1.78f, -0.35f), Vector2.Left) },
		new[] { (new Vector2(-1.78f, -0.3f), Vector2.Right), (new Vector2(-1.05f, -2.5f), Vector2.Down), (new Vector2(1.05f, -2.5f), Vector2.Down), (new Vector2(1.78f, -0.3f), Vector2.Left) },
	};

	/// <summary>
	/// A game's own seating for the other players (same form as the usual one, one entry per seat
	/// after yours), for when something of its own takes up part of the table. Null for the usual.
	/// </summary>
	public Place[]? Layout { get; set; }

	/// <summary>How far down the table (z) your place setting's centre lies, and your hands and piles with it.</summary>
	public float YourPlace { get; set; } = 1.15f;

	/// <summary>The size of your place setting (across, deep).</summary>
	public Vector2 YourMatSize { get; set; } = new(3.6f, 1.25f);

	public float YourHalfDepth => YourMatSize.Y / 2;

	/// <summary>How far either side of the middle your hands rest (on the ends of your place setting).</summary>
	public float YourHandsApart { get; set; } = 1.75f;

	/// <summary>Seats a number of players (2 to 5) and works out every seat's places.</summary>
	public void Arrange(int seats)
	{
		Seats = Mathf.Clamp(seats, 2, MaxSeats);
		var eye = Camera.GlobalPosition;

		// You: your fan held up close, tilted to face you, low on the screen.
		var near = new Vector3(0, 2.15f, 3.46f);
		FanPivot[0] = new Transform3D(Basis.LookingAt(near - eye, Vector3.Up), near);
		float z = YourPlace;
		Pile[0] = new Vector3(0, FeltY, z + 0.1f);
		Winnings[0] = new Vector3(1.45f, FeltY, z - 0.65f);
		Landing[0] = new Vector3(-1.2f, FeltY, z - 0.7f);
		Bubble[0] = new Vector3(0, 1.2f, 1.3f);
		HandRest[0] = new Vector3(YourHandsApart, FeltY, z + 0.3f);
		HolderRest[0] = new Vector3(-YourHandsApart, FeltY, z + 0.3f);

		foreach (var mat in _mats)
			mat.QueueFree();
		_mats.Clear();
		AddMat(new Vector3(0, FeltY, z), 0, YourMatSize, TableTalk.SeatColour(0));

		for (int s = 1; s < Seats; s++)
		{
			var (at, facing, reach) = Layout is { } layout && layout.Length == Seats - 1 ? layout[s - 1] : new Place(Around[Seats][s - 1].At, Around[Seats][s - 1].Facing);
			bool side = facing.Y == 0;
			// Facing (towards the table) and the player's own right, on the felt.
			var f = new Vector3(facing.X, 0, facing.Y);
			var r = f.Cross(Vector3.Up);
			var seat = new Vector3(at.X, FeltY, at.Y);

			var fan = seat + Vector3.Up * (side ? 0.8f : 0.62f);
			// Each fan faces its player, and leans a little away from you so you see its backs spread rather than edge-on.
			var away = (fan - eye).Normalized();
			var forward = (-f * 0.55f + away * 0.45f).Normalized();
			FanPivot[s] = new Transform3D(Basis.LookingAt(-forward, Vector3.Up), fan);

			// The place setting in front of them, square to the table, with everything on it.
			var mat = seat + f * reach;
			Pile[s] = mat;
			Winnings[s] = mat - r * 0.45f + f * 0.1f;
			Landing[s] = mat + f * 0.55f + r * 0.2f;
			HandRest[s] = mat + r * 0.72f - f * 0.12f;
			HolderRest[s] = mat - r * 0.72f - f * 0.12f;
			Bubble[s] = new Vector3(fan.X * 0.55f, fan.Y + 0.95f, fan.Z + 0.2f);
			var size = side ? new Vector2(1.75f, 1.0f) : Seats == 5 ? new Vector2(1.75f, 1.1f) : new Vector2(2.0f, 1.15f);
			AddMat(mat, Mathf.Atan2(f.X, f.Z), size, TableTalk.SeatColour(s));
		}
	}

	/// <summary>A place setting: a slightly deeper panel of the cloth with a stitched edge, turned to face its player.</summary>
	private void AddMat(Vector3 at, float yaw, Vector2 size, Color colour)
	{
		var material = new ShaderMaterial { Shader = _matShader ??= new Shader { Code = MatCode } };
		material.SetShaderParameter("felt", _cloth);
		material.SetShaderParameter("tint", colour);
		material.SetShaderParameter("size", size);
		var mat = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = size },
			Position = at + Vector3.Up * 0.0004f,
			Rotation = new Vector3(0, yaw, 0),
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(mat);
		_mats.Add(mat);
	}

	/// <summary>Projects a screen tap into the world: a ray from the camera.</summary>
	public (Vector3 Origin, Vector3 Direction) Ray(Vector2 screen) => (Camera.ProjectRayOrigin(screen), Camera.ProjectRayNormal(screen));

	private const string FeltCode = @"
shader_type spatial;
render_mode specular_disabled;
uniform vec3 felt : source_color = vec3(0.10, 0.34, 0.22);
uniform vec3 stitch : source_color = vec3(0.78, 0.64, 0.36);
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
	vec2 uv = UV;
	// Fine fibres and a soft mottle, a little darker towards the edges.
	float fibre = hash(floor(uv * vec2(700.0, 980.0)));
	float mottle = hash(floor(uv * 9.0)) * 0.5 + hash(floor(uv * 23.0)) * 0.5;
	vec2 d = abs(uv - 0.5) * 2.0;
	float edge = smoothstep(0.7, 1.0, max(d.x, d.y));
	vec3 colour = felt * (0.9 + 0.1 * fibre + 0.06 * mottle) * (1.0 - 0.4 * edge);
	// A dashed stitch line just inside the edge.
	float inset = 1.0 - 0.045;
	float line = (abs(max(d.x, d.y) - inset) < 0.004) ? 1.0 : 0.0;
	float dash = step(0.5, fract((d.x > d.y ? uv.y : uv.x) * 90.0));
	colour = mix(colour, stitch, line * dash * 0.7);
	ALBEDO = colour;
	ROUGHNESS = 0.97;
}";

	private const string MatCode = @"
shader_type spatial;
render_mode specular_disabled;
uniform vec3 felt : source_color = vec3(0.10, 0.34, 0.22);
uniform vec2 size = vec2(2.0, 1.2);
uniform vec3 tint : source_color = vec3(0.9, 0.75, 0.3);
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
	// Distance inside a rounded rectangle, in table units.
	vec2 p = (UV - 0.5) * size;
	vec2 q = abs(p) - (size * 0.5 - vec2(0.16));
	float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.16;
	if (outside > 0.0) discard;
	float fibre = hash(floor(UV * size * 160.0));
	// The mat is its player's colour (their name's colour in the log), soft like the felt.
	vec3 colour = mix(felt * 0.75, tint * 0.78, 0.86) * (0.9 + 0.1 * fibre);
	// A dashed stitch a little inside the edge.
	float line = 1.0 - smoothstep(0.006, 0.014, abs(outside + 0.07));
	float along = abs(q.x) > abs(q.y) ? p.y : p.x;
	float dash = step(0.45, fract(along * 9.0));
	colour = mix(colour, vec3(0.86, 0.76, 0.52), line * dash * 0.55);
	ALBEDO = colour;
	ROUGHNESS = 0.97;
}";
}
