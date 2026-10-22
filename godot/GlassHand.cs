using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// A transparent glass hand: one smooth, realistically shaped mesh (see <see cref="HandShape"/>)
/// drawn with a fresnel glass shader, clear in the middle and bright at the rim, so the cards it
/// holds always show through; the wrist fades away. Local frame: palm centre at the origin,
/// fingers up (+Y), palm facing +Z. It can hold a fan, rest, and pinch a card between thumb and
/// index finger (animated).
/// </summary>
public partial class GlassHand : Node3D
{
	/// <summary>The in-between steps of a pinch, so the fingers visibly close rather than snap.</summary>
	private static readonly float[] PinchSteps = { 0.35f, 0.7f, 1f };

	private static Shader? _glass;
	private readonly MeshInstance3D _mesh;
	private readonly ShaderMaterial _material;
	private HandPose _pose = HandPose.Relaxed;
	private Color _tint;
	private Tween? _tintTween;

	public GlassHand(bool right, Color tint, HandPose? pose = null)
	{
		Right = right;
		_material = new ShaderMaterial { Shader = _glass ??= new Shader { Code = GlassCode } };
		_tint = tint;
		_material.SetShaderParameter("tint", tint);
		_mesh = new MeshInstance3D { MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_mesh);
		Pose = pose ?? HandPose.Relaxed;
	}

	public bool Right { get; }

	public HandPose Pose
	{
		get => _pose;
		set
		{
			_pose = value;
			_mesh.Mesh = HandShape.Mesh(value, Right);
		}
	}

	/// <summary>The glass colour; changing it fades over a moment (a speaker's hands turn blue, say).</summary>
	public Color Tint
	{
		get => _tint;
		set
		{
			var from = _tint;
			_tint = value;
			_tintTween?.Kill();
			if (!IsInsideTree())
			{
				_material.SetShaderParameter("tint", value);
				return;
			}
			_tintTween = CreateTween();
			_tintTween.TweenMethod(Callable.From<Color>(c => _material.SetShaderParameter("tint", c)), from, value, 0.25f);
		}
	}

	/// <summary>Where a pinched card sits, in this hand's frame.</summary>
	public Vector3 PinchPoint => HandShape.PinchPoint(Right);

	/// <summary>Builds the meshes this hand will use ahead of time, so the first pinch doesn't stall.</summary>
	public void WarmUp()
	{
		foreach (float t in PinchSteps)
			HandShape.Mesh(HandPose.Relaxed, HandPose.Pinch, t, Right);
		HandShape.Mesh(HandPose.Rest, Right);
	}

	/// <summary>Closes (or opens) thumb and index finger, over about a tenth of a second.</summary>
	public async Task Pinch(bool close)
	{
		var steps = close ? PinchSteps : new[] { 0.7f, 0.35f, 0f };
		foreach (float t in steps)
		{
			_mesh.Mesh = HandShape.Mesh(HandPose.Relaxed, HandPose.Pinch, t, Right);
			await ToSignal(GetTree().CreateTimer(0.035f), SceneTreeTimer.SignalName.Timeout);
		}
		_pose = close ? HandPose.Pinch : HandPose.Relaxed;
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
