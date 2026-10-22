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
			Mesh = new PlaneMesh { Size = Solitaire ? new Vector2(SolitaireWidth + 1.2f, (SolitaireWidth + 1.2f) * 2.1f) : new Vector2(6.0f, 7.4f) },
			Position = Solitaire ? new Vector3(0, 0.001f, 0.6f) : new Vector3(0, 0.001f, -0.6f),
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

	/// <summary>Where the other players sit (x, z on the felt), for each table size, left to right.</summary>
	private static readonly Vector2[][] Around =
	{
		new Vector2[0],
		new Vector2[0],
		new[] { new Vector2(0, -2.75f) },
		new[] { new Vector2(-1.2f, -2.6f), new Vector2(1.2f, -2.6f) },
		new[] { new Vector2(-1.85f, -0.95f), new Vector2(0, -2.75f), new Vector2(1.85f, -0.95f) },
		new[] { new Vector2(-1.85f, -0.7f), new Vector2(-1.0f, -2.7f), new Vector2(1.0f, -2.7f), new Vector2(1.85f, -0.7f) },
	};

	/// <summary>Seats a number of players (2 to 5) and works out every seat's places.</summary>
	public void Arrange(int seats)
	{
		Seats = Mathf.Clamp(seats, 2, MaxSeats);
		var eye = Camera.GlobalPosition;
		var centre = new Vector2(Centre.X, Centre.Z);

		// You: your fan held up close, tilted to face you, low on the screen.
		var near = new Vector3(0, 2.15f, 3.46f);
		FanPivot[0] = new Transform3D(Basis.LookingAt(near - eye, Vector3.Up), near);
		Pile[0] = new Vector3(0, FeltY, 1.25f);
		Winnings[0] = new Vector3(1.45f, FeltY, 0.5f);
		Landing[0] = new Vector3(-1.2f, FeltY, 0.45f);
		Bubble[0] = new Vector3(0, 1.2f, 1.3f);
		HandRest[0] = new Vector3(1.75f, FeltY, 1.45f);

		foreach (var mat in _mats)
			mat.QueueFree();
		_mats.Clear();
		AddMat(new Vector3(0.15f, FeltY, 1.15f), 0, new Vector2(3.6f, 1.25f));

		for (int s = 1; s < Seats; s++)
		{
			var at = Around[Seats][s - 1];
			bool side = Mathf.Abs(at.X) > 1.5f;
			var fan = new Vector3(at.X, side ? 0.85f : 1.05f, at.Y);
			// Each fan faces its player: outward from the middle, and a little away from you so
			// you see its backs spread rather than edge-on.
			var outward = (at - centre).Normalized();
			var away = (fan - eye).Normalized();
			var forward = (new Vector3(outward.X, 0, outward.Y) * 0.55f + away * 0.45f).Normalized();
			FanPivot[s] = new Transform3D(Basis.LookingAt(-forward, Vector3.Up), fan);

			var inward = centre + (at - centre) * 0.62f;
			var right = new Vector2(-outward.Y, outward.X); // the player's right, seen from the middle
			Pile[s] = new Vector3(inward.X, FeltY, inward.Y);
			var win = inward + right * 0.8f;
			Winnings[s] = new Vector3(win.X, FeltY, win.Y);
			var land = centre + (at - centre) * 0.4f - right * 0.45f;
			Landing[s] = new Vector3(land.X, FeltY, land.Y);
			Bubble[s] = new Vector3(fan.X * 0.55f, fan.Y + 0.95f, fan.Z + 0.2f);
			var rest = inward + right * 0.75f + outward * 0.55f;
			HandRest[s] = new Vector3(rest.X, FeltY, rest.Y);

			// The place setting: under the pile, the winnings and the resting hand, facing the player.
			var mid = inward + right * 0.35f + outward * 0.2f;
			AddMat(new Vector3(mid.X, FeltY, mid.Y), Mathf.Atan2(outward.X, outward.Y), new Vector2(side ? 1.7f : 2.0f, 1.25f));
		}
	}

	/// <summary>A place setting: a slightly deeper panel of the cloth with a stitched edge, turned to face its player.</summary>
	private void AddMat(Vector3 at, float yaw, Vector2 size)
	{
		var material = new ShaderMaterial { Shader = _matShader ??= new Shader { Code = MatCode } };
		material.SetShaderParameter("felt", _cloth);
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
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
	// Distance inside a rounded rectangle, in table units.
	vec2 p = (UV - 0.5) * size;
	vec2 q = abs(p) - (size * 0.5 - vec2(0.16));
	float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.16;
	if (outside > 0.0) discard;
	float fibre = hash(floor(UV * size * 160.0));
	vec3 colour = felt * (0.8 + 0.08 * fibre);
	// A dashed stitch a little inside the edge.
	float line = 1.0 - smoothstep(0.006, 0.014, abs(outside + 0.07));
	float along = abs(q.x) > abs(q.y) ? p.y : p.x;
	float dash = step(0.45, fract(along * 9.0));
	colour = mix(colour, vec3(0.86, 0.76, 0.52), line * dash * 0.55);
	ALBEDO = colour;
	ROUGHNESS = 0.97;
}";
}
