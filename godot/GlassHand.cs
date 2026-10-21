using Godot;

namespace Renegade.Cards;

/// <summary>
/// A transparent glass hand: one smooth, realistically shaped mesh (see <see cref="HandShape"/>)
/// drawn with a fresnel glass shader, clear in the middle and bright at the rim, so the cards it
/// holds always show through; the wrist fades away. Local frame: palm centre at the origin,
/// fingers up (+Y), palm facing +Z. <see cref="Mirrored"/> makes a right hand.
/// </summary>
public partial class GlassHand : Node3D
{
	private static Shader? _glass;
	private readonly MeshInstance3D _mesh;
	private float _grip;

	public GlassHand(bool mirrored, Color tint)
	{
		Mirrored = mirrored;
		var material = new ShaderMaterial { Shader = _glass ??= new Shader { Code = GlassCode } };
		material.SetShaderParameter("tint", tint);
		_mesh = new MeshInstance3D { MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_mesh);
		Grip = 0;
	}

	public bool Mirrored { get; }

	/// <summary>How closed the hand is: below one half it's relaxed, above it's holding a fan.</summary>
	public float Grip
	{
		get => _grip;
		set
		{
			_grip = value;
			_mesh.Mesh = HandShape.Mesh(value >= 0.5f ? HandPose.Hold : HandPose.Relaxed, Mirrored);
		}
	}

	private const string GlassCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, specular_schlick_ggx;
uniform vec4 tint : source_color = vec4(0.78, 0.9, 1.0, 1.0);
void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - facing, 2.2);
	ALBEDO = tint.rgb * 0.55;
	ROUGHNESS = 0.05;
	SPECULAR = 0.95;
	EMISSION = tint.rgb * (0.07 + rim * 0.65);
	// COLOR.a fades the wrist out, so the arm melts away instead of ending.
	ALPHA = (0.14 + 0.66 * rim) * COLOR.a;
}";
}
