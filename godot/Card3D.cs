using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// The look of the deck in play: one material per front (all sampling the painted atlas) and one
/// for the back. Changing the design repaints and swaps the textures, so every card on the table
/// updates at once. The materials can bend a card (see <see cref="Card3D.Bend"/>).
/// </summary>
public sealed class CardLooks
{
	private static Shader? _shader;
	private readonly ShaderMaterial[] _fronts = new ShaderMaterial[52];
	private readonly ShaderMaterial _back = Make(+1);

	private CardLooks()
	{
		_back.SetShaderParameter("side", -1f);
		for (int i = 0; i < 52; i++)
		{
			_fronts[i] = Make(+1);
			_fronts[i].SetShaderParameter("uv_scale", new Vector2(1 / 13f, 1 / 4f));
			_fronts[i].SetShaderParameter("uv_offset", new Vector2(i % 13 / 13f, i / 13 / 4f));
		}
	}

	public CardFront Front { get; private set; }
	public CardBack Back { get; private set; }

	public static async Task<CardLooks> Create(Node host, CardFront front, CardBack back)
	{
		var looks = new CardLooks();
		await looks.SetFront(host, front);
		await looks.SetBack(host, back);
		return looks;
	}

	public async Task SetFront(Node host, CardFront front)
	{
		Front = front;
		var atlas = await CardArt.PaintFronts(host, front);
		foreach (var material in _fronts)
			material.SetShaderParameter("atlas", atlas);
	}

	public async Task SetBack(Node host, CardBack back)
	{
		Back = back;
		_back.SetShaderParameter("atlas", await CardArt.PaintBack(host, back));
	}

	public Material FrontOf(Card card) => _fronts[card.Ordinal];
	public Material BackMaterial => _back;

	private static ShaderMaterial Make(float side)
	{
		var material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = CardCode } };
		material.SetShaderParameter("side", side);
		material.SetShaderParameter("half_height", Card3D.Height / 2);
		return material;
	}

	// The painted rounded corners are transparent and are cut away cleanly (cards stay opaque, so
	// stacks need no sorting). The vertex stage bends the card along its length: "bow" lifts the
	// middle, "curl" lifts one end ("curl_end": 1 the top, -1 the bottom); positive is towards the face, as fingers do in a riffle or a bridge.
	private const string CardCode = @"
shader_type spatial;
render_mode cull_back;
uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic;
uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform float side = 1.0;
uniform float half_height = 0.44;
uniform float bow = 0.0;
uniform float curl = 0.0;
uniform float curl_end = 1.0;
void vertex() {
	float t = clamp(VERTEX.y / half_height, -1.0, 1.0);
	float e = clamp((curl_end * t + 1.0) * 0.5, 0.0, 1.0);
	float z = bow * (1.0 - t * t) + curl * e * e;
	float slope = (bow * -2.0 * t + curl * e * curl_end) / half_height;
	VERTEX.z += z * side;
	NORMAL = normalize(vec3(0.0, -slope * side, 1.0));
}
void fragment() {
	vec4 c = texture(atlas, UV * uv_scale + uv_offset);
	if (c.a < 0.5) discard;
	ALBEDO = c.rgb;
	ROUGHNESS = 0.62;
}";
}

/// <summary>
/// One playing card on the table: two back-to-back quads (front facing local +Z, back facing -Z)
/// in its local XY plane, with the card's top along +Y. Use <see cref="FaceDown"/> and
/// <see cref="FaceUp"/> for lying on the table. Cards can bend (<see cref="Bend"/>).
/// </summary>
public partial class Card3D : Node3D
{
	/// <summary>Poker size, in table units (about 6.3 × 8.8 cm at 10 units to the metre).</summary>
	public const float Width = 0.63f, Height = 0.88f;

	private static QuadMesh? _quad;
	private readonly MeshInstance3D _front;
	private readonly MeshInstance3D _back;
	private readonly CardLooks _looks;
	private Card _card;
	private ShaderMaterial? _ownFront, _ownBack;
	private float _bow, _curl;
	private int _end = 1;

	public Card3D(Card card, CardLooks looks)
	{
		_looks = looks;
		// Divided along its length so it can bend smoothly.
		var quad = _quad ??= new QuadMesh { Size = new Vector2(Width, Height), SubdivideWidth = 1, SubdivideDepth = 14 };
		_front = new MeshInstance3D { Mesh = quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_back = new MeshInstance3D
		{
			Mesh = quad,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0, 0, -0.0008f),
			Rotation = new Vector3(0, Mathf.Pi, 0),
		};
		AddChild(_front);
		AddChild(_back);
		Card = card;
		_back.MaterialOverride = looks.BackMaterial;
	}

	/// <summary>Which card this is. (In a face-down stack it can be assigned as cards are dealt.)</summary>
	public Card Card
	{
		get => _card;
		set
		{
			_card = value;
			_front.MaterialOverride = _looks.FrontOf(value);
			_ownFront = null;
			if (_bow != 0 || _curl != 0)
				Bend(_bow, _curl, _end);
		}
	}

	/// <summary>The meshes, for effects such as the winner's fire.</summary>
	public MeshInstance3D[] Meshes => new[] { _front, _back };

	private static StandardMaterial3D? _shade;

	/// <summary>Greys the face a little (a card you can't play right now).</summary>
	public bool Dimmed
	{
		get => _front.MaterialOverlay != null;
		set => _front.MaterialOverlay = value
			? _shade ??= new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.04f, 0.03f, 0.42f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
			: null;
	}

	/// <summary>
	/// Bends the card along its length: <paramref name="bow"/> lifts the middle, <paramref name="curl"/>
	/// lifts one end (<paramref name="end"/>: 1 the top, -1 the bottom), both in table units, positive
	/// towards the face (negative towards the back). Zero straightens it. While bent the card has its
	/// own copy of its materials.
	/// </summary>
	public void Bend(float bow, float curl = 0, int end = 1)
	{
		_bow = bow;
		_curl = curl;
		_end = end;
		if (bow == 0 && curl == 0)
		{
			_front.MaterialOverride = _looks.FrontOf(_card);
			_back.MaterialOverride = _looks.BackMaterial;
			_ownFront = _ownBack = null;
			return;
		}
		_ownFront ??= (ShaderMaterial)_looks.FrontOf(_card).Duplicate();
		_ownBack ??= (ShaderMaterial)_looks.BackMaterial.Duplicate();
		_front.MaterialOverride = _ownFront;
		_back.MaterialOverride = _ownBack;
		foreach (var m in new[] { _ownFront, _ownBack })
		{
			m.SetShaderParameter("bow", bow);
			m.SetShaderParameter("curl", curl);
			m.SetShaderParameter("curl_end", (float)end);
		}
	}

	public float Bow => _bow;
	public float Curl => _curl;

	/// <summary>Eases the card's bend to new values over a time (and returns the tween).</summary>
	public Tween BendTo(float bow, float curl, float seconds, float delay = 0, int? end = null)
	{
		float b0 = _bow, c0 = _curl;
		int e = end ?? _end;
		var tween = CreateTween();
		if (delay > 0)
			tween.TweenInterval(delay);
		tween.TweenMethod(Callable.From<float>(t => Bend(Mathf.Lerp(b0, bow, t), Mathf.Lerp(c0, curl, t), e)), 0f, 1f, seconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		return tween;
	}

	/// <summary>Lying on the table face down, its top pointing away from the near player.</summary>
	public static Basis FaceDown(float yaw = 0) => new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi / 2);

	/// <summary>Lying on the table face up, readable by the near player.</summary>
	public static Basis FaceUp(float yaw = 0) => new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -Mathf.Pi / 2);

	/// <summary>
	/// Where a ray meets this card, as a distance along the ray, or null if it misses. Used for
	/// taps; the nearest hit is the card on top.
	/// </summary>
	public float? Hit(Vector3 origin, Vector3 direction)
	{
		var t = GlobalTransform;
		var normal = t.Basis.Z.Normalized();
		float denom = normal.Dot(direction);
		if (Mathf.Abs(denom) < 1e-5f)
			return null;
		float distance = normal.Dot(t.Origin - origin) / denom;
		if (distance < 0)
			return null;
		var local = t.AffineInverse() * (origin + direction * distance);
		return Mathf.Abs(local.X) <= Width / 2 && Mathf.Abs(local.Y) <= Height / 2 ? distance : null;
	}
}
