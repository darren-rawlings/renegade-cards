using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// The look of the deck in play: one material per front (all sampling the painted atlas) and one
/// for the back. Changing the design repaints and swaps the textures, so every card on the table
/// updates at once.
/// </summary>
public sealed class CardLooks
{
	private readonly StandardMaterial3D[] _fronts = new StandardMaterial3D[52];
	private readonly StandardMaterial3D _back = Make();

	private CardLooks()
	{
		for (int i = 0; i < 52; i++)
		{
			_fronts[i] = Make();
			_fronts[i].Uv1Scale = new Vector3(1 / 13f, 1 / 4f, 1);
			_fronts[i].Uv1Offset = new Vector3(i % 13 / 13f, i / 13 / 4f, 0);
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
			material.AlbedoTexture = atlas;
	}

	public async Task SetBack(Node host, CardBack back)
	{
		Back = back;
		_back.AlbedoTexture = await CardArt.PaintBack(host, back);
	}

	public Material FrontOf(Card card) => _fronts[card.Ordinal];
	public Material BackMaterial => _back;

	private static StandardMaterial3D Make() => new()
	{
		// The painted rounded corners are transparent; alpha scissor cuts them cleanly and keeps
		// cards opaque (no sorting trouble in a stack).
		Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
		AlphaScissorThreshold = 0.5f,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		Roughness = 0.62f,
		CullMode = BaseMaterial3D.CullModeEnum.Back,
	};
}

/// <summary>
/// One playing card on the table: two back-to-back quads (front facing local +Z, back facing -Z)
/// in its local XY plane, with the card's top along +Y. Use <see cref="FaceDown"/> and
/// <see cref="FaceUp"/> for lying on the table.
/// </summary>
public partial class Card3D : Node3D
{
	/// <summary>Poker size, in table units (about 6.3 × 8.8 cm at 10 units to the metre).</summary>
	public const float Width = 0.63f, Height = 0.88f;

	private readonly MeshInstance3D _front;
	private readonly MeshInstance3D _back;
	private readonly CardLooks _looks;
	private Card _card;

	public Card3D(Card card, CardLooks looks)
	{
		_looks = looks;
		var quad = new QuadMesh { Size = new Vector2(Width, Height) };
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
		}
	}

	/// <summary>The meshes, for effects such as the winner's fire.</summary>
	public MeshInstance3D[] Meshes => new[] { _front, _back };

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
