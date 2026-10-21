using Godot;

namespace Renegade.Cards;

/// <summary>
/// A transparent glass hand: palm, jointed fingers, thumb and wrist, built from rounded shapes and
/// drawn with a fresnel glass shader (clear in the middle, bright at the rim), so the cards it
/// holds always show through. Local frame: fingers point up (+Y), the palm faces +Z.
/// <see cref="Mirrored"/> makes a right hand from the left-hand layout.
/// </summary>
public partial class GlassHand : Node3D
{
	private static Shader? _glass;
	private readonly ShaderMaterial _material;

	public GlassHand(bool mirrored, Color tint)
	{
		Mirrored = mirrored;
		_material = new ShaderMaterial { Shader = _glass ??= new Shader { Code = GlassCode } };
		_material.SetShaderParameter("tint", tint);
		Build();
	}

	public bool Mirrored { get; }

	/// <summary>Curls the fingers: 0 open, 1 gripping (wrapped round the back of a fan).</summary>
	public float Grip
	{
		get => _grip;
		set
		{
			_grip = value;
			for (int i = 0; i < _fingers.Length; i++)
			{
				_fingers[i].Rotation = new Vector3(-0.55f * value - 0.15f, 0, _fingers[i].Rotation.Z);
				_tips[i].Rotation = new Vector3(-0.9f * value, 0, 0);
			}
		}
	}

	private float _grip;
	private readonly Node3D[] _fingers = new Node3D[4];
	private readonly Node3D[] _tips = new Node3D[4];

	private MeshInstance3D Blob(Node3D parent, Mesh mesh, Vector3 position, Vector3 scale, Vector3 rotation = default)
	{
		var part = new MeshInstance3D
		{
			Mesh = mesh,
			Position = position,
			Scale = scale,
			Rotation = rotation,
			MaterialOverride = _material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(part);
		return part;
	}

	private void Build()
	{
		float side = Mirrored ? -1 : 1;
		var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 20, Rings = 12 };
		var capsule = new CapsuleMesh { Radius = 1, Height = 4, RadialSegments = 14, Rings = 6 };

		// Palm and the wrist running down out of view.
		Blob(this, sphere, new Vector3(0, 0, 0), new Vector3(0.24f, 0.27f, 0.085f));
		Blob(this, capsule, new Vector3(0.02f * side, -0.42f, -0.02f), new Vector3(0.12f, 0.16f, 0.09f));

		// Four fingers, each a knuckle joint holding a base segment and a tip segment.
		float[] lengths = { 0.15f, 0.17f, 0.16f, 0.12f };
		for (int i = 0; i < 4; i++)
		{
			float x = (-0.16f + i * 0.105f) * side;
			var knuckle = new Node3D { Position = new Vector3(x, 0.24f, 0), Rotation = new Vector3(0, 0, (0.08f - i * 0.05f) * side) };
			AddChild(knuckle);
			float r = i == 3 ? 0.038f : 0.044f;
			Blob(knuckle, capsule, new Vector3(0, lengths[i] / 2, 0), new Vector3(r, lengths[i] / 4 + r / 2, r));
			var joint = new Node3D { Position = new Vector3(0, lengths[i], 0) };
			knuckle.AddChild(joint);
			Blob(joint, capsule, new Vector3(0, lengths[i] * 0.42f, 0), new Vector3(r * 0.92f, lengths[i] * 0.21f + r / 2, r * 0.92f));
			_fingers[i] = knuckle;
			_tips[i] = joint;
		}

		// The thumb: out from the base of the palm, across the front.
		var thumb = new Node3D { Position = new Vector3(-0.2f * side, -0.08f, 0.04f), Rotation = new Vector3(0.5f, 0.3f * side, 0.95f * side) };
		AddChild(thumb);
		Blob(thumb, capsule, new Vector3(0, 0.1f, 0), new Vector3(0.05f, 0.075f, 0.05f));
		var thumbTip = new Node3D { Position = new Vector3(0, 0.2f, 0), Rotation = new Vector3(0.35f, 0, 0) };
		thumb.AddChild(thumbTip);
		Blob(thumbTip, capsule, new Vector3(0, 0.07f, 0), new Vector3(0.046f, 0.058f, 0.046f));

		Grip = 0.6f;
	}

	private const string GlassCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, specular_schlick_ggx;
uniform vec4 tint : source_color = vec4(0.78, 0.9, 1.0, 1.0);
void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - facing, 2.4);
	ALBEDO = tint.rgb * 0.55;
	ROUGHNESS = 0.06;
	SPECULAR = 0.9;
	EMISSION = tint.rgb * (0.08 + rim * 0.7);
	ALPHA = 0.16 + 0.7 * rim;
}";
}
