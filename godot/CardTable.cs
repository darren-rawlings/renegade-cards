using Godot;
using Renegade.Play;

namespace Renegade.Cards;

/// <summary>
/// A card table for two: the studio's shared stage (lights, reflections, the wooden table) with a
/// felt mat on top, a fixed camera (card tables don't spin), and the places a two-player game
/// uses: each player's fan of cards, their dealt pile and their winnings, and the middle of the
/// table. The near player (you) sits at the bottom of the portrait screen.
/// </summary>
public partial class CardTable : Node3D
{
	public const float FeltY = 0.002f;

	private static Shader? _felt;

	public Camera3D Camera { get; private set; } = null!;

	/// <summary>Where each player's fan is held: index 0 is you, 1 the player across the table.</summary>
	public Transform3D[] FanPivot { get; } = new Transform3D[2];

	/// <summary>Where each player's dealt cards land before they're picked up.</summary>
	public Vector3[] Pile { get; } = new Vector3[2];

	/// <summary>Where each player lays down what they win (books, tricks).</summary>
	public Vector3[] Winnings { get; } = new Vector3[2];

	public Vector3 Centre { get; } = new(0, FeltY, -0.35f);

	public override void _Ready()
	{
		Stage.Build(this, 0, new Color("120c08"));
		Camera = new Camera3D { Fov = 36, KeepAspect = Camera3D.KeepAspectEnum.Width, Position = new Vector3(0, 6.6f, 5.4f) };
		AddChild(Camera);
		Camera.LookAt(new Vector3(0, 0, -0.55f));

		var mat = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(4.6f, 6.4f) },
			Position = new Vector3(0, 0.001f, -0.4f),
			MaterialOverride = new ShaderMaterial { Shader = _felt ??= new Shader { Code = FeltCode } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(mat);

		var eye = Camera.GlobalPosition;
		// Your fan: held up close, tilted to face you, low on the screen.
		var near = new Vector3(0, 2.15f, 3.46f);
		FanPivot[0] = new Transform3D(Basis.LookingAt(near - eye, Vector3.Up), near);
		// Theirs: across the table, facing them, so you see the backs.
		var far = new Vector3(0, 1.05f, -2.75f);
		FanPivot[1] = new Transform3D(Basis.LookingAt(eye - far, Vector3.Up), far);
		Pile[0] = new Vector3(0, FeltY, 1.25f);
		Pile[1] = new Vector3(0, FeltY, -2.05f);
		Winnings[0] = new Vector3(1.45f, FeltY, 0.5f);
		Winnings[1] = new Vector3(-1.25f, FeltY, -2.15f);
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
}
