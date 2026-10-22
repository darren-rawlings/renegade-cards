using System.Collections.Generic;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// The old Windows solitaire win: cards leave the foundations one after another and bounce down
/// the screen under gravity, each leaving a trail of copies of itself behind, until the table is
/// papered with cards. Played on the felt as seen from above: "down the screen" is towards the
/// player. The trails are drawn in one batch per card, so even a full deck stays light.
/// </summary>
public partial class WinCascade : Node3D
{
	private const float Gravity = 13f, Bounce = 0.78f, StampEvery = 0.032f, Epsilon = 0.00006f;
	private const int MaxStamps = 160;

	private sealed class Flyer
	{
		public Card3D Node = null!;
		public Vector2 Position, Velocity;
		public MultiMesh Trail = null!;
		public float SinceStamp;
	}

	private readonly List<Flyer> _flying = new();
	private readonly Queue<Card3D> _waiting = new();
	private readonly Rect2 _area;
	private readonly float _y;
	private readonly float _gap;
	private readonly RandomNumberGenerator _random = new();
	private CardLooks _looks = null!;
	private float _untilNext;
	private int _stamps;

	private WinCascade(Rect2 area, float y, float gap)
	{
		_area = area;
		_y = y;
		_gap = gap;
	}

	/// <summary>True while cards are still bouncing (or waiting their turn).</summary>
	public bool Running => _flying.Count > 0 || _waiting.Count > 0;

	/// <summary>
	/// Starts the cascade: <paramref name="cards"/> go in the order given, a launch every
	/// <paramref name="gap"/> seconds, across <paramref name="area"/> (x and z on the felt; cards bounce
	/// off its near edge and leave by its sides) at height <paramref name="y"/>.
	/// </summary>
	public static WinCascade Start(Node3D host, IEnumerable<Card3D> cards, CardLooks looks, Rect2 area, float y, float gap = 0.2f)
	{
		var cascade = new WinCascade(area, y, gap) { _looks = looks };
		host.AddChild(cascade);
		foreach (var card in cards)
			cascade._waiting.Enqueue(card);
		return cascade;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_untilNext -= dt;
		if (_untilNext <= 0 && _waiting.Count > 0)
		{
			Launch(_waiting.Dequeue());
			_untilNext = _gap;
		}
		float floor = _area.End.Y - Card3D.Height / 2;
		for (int i = _flying.Count - 1; i >= 0; i--)
		{
			var f = _flying[i];
			f.Velocity.Y += Gravity * dt;
			f.Position += f.Velocity * dt;
			if (f.Position.Y > floor)
			{
				f.Position.Y = floor;
				f.Velocity.Y = -Mathf.Abs(f.Velocity.Y) * Bounce;
			}
			f.Node.GlobalPosition = new Vector3(f.Position.X, _y + (_stamps + 1) * Epsilon, f.Position.Y);
			f.SinceStamp += dt;
			if (f.SinceStamp >= StampEvery && f.Trail.VisibleInstanceCount < MaxStamps)
			{
				f.SinceStamp = 0;
				int k = f.Trail.VisibleInstanceCount;
				f.Trail.SetInstanceTransform(k, new Transform3D(Card3D.FaceUp(), new Vector3(f.Position.X, _y + _stamps * Epsilon, f.Position.Y)));
				f.Trail.VisibleInstanceCount = k + 1;
				_stamps++;
			}
			// Gone off the side: the card's done.
			if (f.Position.X < _area.Position.X - Card3D.Width || f.Position.X > _area.End.X + Card3D.Width || f.Trail.VisibleInstanceCount >= MaxStamps)
			{
				f.Node.Visible = false;
				_flying.RemoveAt(i);
			}
		}
	}

	private void Launch(Card3D card)
	{
		var trail = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = new QuadMesh { Size = new Vector2(Card3D.Width, Card3D.Height) },
			InstanceCount = MaxStamps,
			VisibleInstanceCount = 0,
		};
		AddChild(new MultiMeshInstance3D
		{
			Multimesh = trail,
			MaterialOverride = _looks.FrontOf(card.Card),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		card.Bend(0);
		card.GlobalTransform = new Transform3D(Card3D.FaceUp(), card.GlobalPosition);
		float side = _random.Randf() < 0.5f ? -1 : 1;
		_flying.Add(new Flyer
		{
			Node = card,
			Position = new Vector2(card.GlobalPosition.X, card.GlobalPosition.Z),
			Velocity = new Vector2(side * _random.RandfRange(1.6f, 3.6f), -_random.RandfRange(0.5f, 4f)),
			Trail = trail,
		});
	}
}
