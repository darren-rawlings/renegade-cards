using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// One player's two glass hands at their seat: the left hand holds their fan (palm towards them,
/// fingers behind the cards, thumb across the front), the right hand does the work. The right
/// hand picks cards up with a pinch (thumb and index finger closing on the card), carries them,
/// and puts them down or throws them. Everything is placed in the seat's own frame (the fan's
/// pivot: +X to the player's right, +Y up, +Z towards the player), so every seat works the same.
/// </summary>
public sealed class SeatHands
{
	private readonly Node3D _world;
	private readonly CardSounds _sounds;
	private readonly Transform3D _seat;
	private readonly Vector3? _rest;

	/// <summary>
	/// <paramref name="rest"/> is where the working hand lies palm down on the felt between jobs (the
	/// seat's place setting); without one it waits in the air beside the fan. Games where nobody
	/// holds a hand of cards (War) leave out the holding hand.
	/// </summary>
	public SeatHands(Node3D world, CardSounds sounds, Transform3D seat, Color tint, Vector3? rest = null, bool holds = true)
	{
		_world = world;
		_sounds = sounds;
		_seat = seat;
		_rest = rest;
		if (holds)
		{
			Holder = new GlassHand(right: false, tint, HandPose.Hold);
			world.AddChild(Holder);
			Holder.GlobalTransform = HoldingPose;
		}
		Worker = new GlassHand(right: true, tint, rest != null ? HandPose.Rest : HandPose.Relaxed);
		world.AddChild(Worker);
		Worker.WarmUp();
		Worker.GlobalTransform = RestingPose;
	}

	/// <summary>The left hand, holding the fan (none in games where no one holds cards).</summary>
	public GlassHand? Holder { get; }

	/// <summary>The right hand, which picks cards up, carries and throws them.</summary>
	public GlassHand Worker { get; }

	/// <summary>
	/// Behind the lower left of the fan, palm towards the player, fingers reaching up and right
	/// behind the cards (their tips just touching the backs), the thumb across the front at the base.
	/// </summary>
	public Transform3D HoldingPose => _seat * new Transform3D(new Basis(Vector3.Back, -0.45f) * new Basis(Vector3.Right, -0.2f), new Vector3(-0.3f, 0.05f, -0.2f));

	/// <summary>
	/// Palm down on the felt at the seat's place, fingers towards the middle of the table (or, with
	/// no place, down to the player's right of the fan, relaxed).
	/// </summary>
	public Transform3D RestingPose
	{
		get
		{
			if (_rest is not { } rest)
				return _seat * new Transform3D(new Basis(Vector3.Back, 0.35f), new Vector3(1.4f, -0.6f, 0.05f));
			var fingers = (Away - PlayersRight * 0.25f).Normalized();
			var palm = Vector3.Down;
			var basis = new Basis(fingers.Cross(palm), fingers, palm).Orthonormalized();
			return new Transform3D(basis, rest + Vector3.Up * 0.115f);
		}
	}

	/// <summary>The glass colour of both hands (a speaker's turn blue, the one asked yellow).</summary>
	public Color Tint
	{
		get => Worker.Tint;
		set
		{
			if (Holder != null)
				Holder.Tint = value;
			Worker.Tint = value;
		}
	}

	/// <summary>The player's right, along the felt.</summary>
	private Vector3 PlayersRight => Away.Cross(Vector3.Up).Normalized();

	/// <summary>The direction from this seat towards the middle of the table, along the felt.</summary>
	private Vector3 Away
	{
		get
		{
			var towardsPlayer = _seat.Basis.Z;
			var flat = new Vector3(towardsPlayer.X, 0, towardsPlayer.Z);
			return -flat.Normalized();
		}
	}

	/// <summary>
	/// Where the working hand goes to pinch a card. A card lying on the felt is taken palm down
	/// by its near edge, fingers reaching away from the player; a card standing in a fan is taken
	/// from in front, by its upper part.
	/// </summary>
	private Transform3D GrabFor(Card3D card)
	{
		var t = card.GlobalTransform;
		var face = t.Basis.Z.Normalized();
		Basis hand;
		Vector3 grip;
		if (Mathf.Abs(face.Y) > 0.7f)
		{
			// Lying flat: palm down (+Z of the hand is the palm), fingers away from the player.
			var fingers = Away;
			var palm = Vector3.Down;
			hand = new Basis(fingers.Cross(palm), fingers, palm).Orthonormalized();
			grip = t.Origin - fingers * (Card3D.Height * 0.36f) + Vector3.Up * 0.01f;
		}
		else
		{
			// Standing in a fan: palm towards the face of the card, fingers along the card's top.
			var up = t.Basis.Y.Normalized();
			var palm = -face;
			hand = new Basis(up.Cross(palm), up, palm).Orthonormalized();
			grip = t.Origin + up * (Card3D.Height * 0.3f) + face * 0.035f;
		}
		var origin = grip - hand * Worker.PinchPoint;
		return new Transform3D(hand, origin);
	}

	private async Task Await(Tween tween) => await _world.ToSignal(tween, Tween.SignalName.Finished);

	/// <summary>
	/// Picks up cards (the first is the one pinched; the rest come with it, as a stack would) and
	/// carries them so the first lands exactly at its target; each card then settles into its own
	/// place. Returns once they're down and the hand is on its way back to rest.
	/// </summary>
	public async Task Carry(IReadOnlyList<Card3D> cards, IReadOnlyList<Transform3D> targets, float seconds = 0.42f, float lift = 0.3f)
	{
		if (cards.Count == 0)
			return;
		await PickUp(cards);
		// Fly the hand so that the first card arrives at its target.
		var relative = Worker.GlobalTransform.AffineInverse() * cards[0].GlobalTransform;
		var handTarget = targets[0] * relative.AffineInverse();
		_sounds.PlaySwish(cards[0].GlobalPosition);
		await Await(CardMotion.Fly(Worker, handTarget, seconds, lift));
		Release(cards);
		Tween? last = null;
		for (int i = 0; i < cards.Count; i++)
		{
			var card = cards[i];
			last = CardMotion.Slide(card, targets[Math.Min(i, targets.Count - 1)], 0.12f, landed: i == 0 ? () => _sounds.PlaySlap(card.GlobalPosition) : null);
		}
		// Start listening before letting go: the cards may land before the fingers open.
		var down = last != null ? Await(last) : Task.CompletedTask;
		await Worker.Pinch(false);
		GoRest();
		await down;
	}

	/// <summary>
	/// Picks up cards and throws them: the hand swings towards the landing spot and lets go, and
	/// the cards fly the rest of the way, spinning a little, landing with a slap.
	/// </summary>
	public async Task Throw(IReadOnlyList<Card3D> cards, IReadOnlyList<Transform3D> landings)
	{
		if (cards.Count == 0)
			return;
		await PickUp(cards);
		// Swing a third of the way there, raised, then let go.
		var start = Worker.GlobalTransform;
		var toward = landings[0].Origin - cards[0].GlobalPosition;
		var swing = new Transform3D(start.Basis, start.Origin + toward * 0.33f + Vector3.Up * 0.25f);
		_sounds.PlaySwish(cards[0].GlobalPosition);
		await Await(CardMotion.Fly(Worker, swing, 0.2f, lift: 0.05f));
		Release(cards);
		Tween? last = null;
		for (int i = 0; i < cards.Count; i++)
		{
			var card = cards[i];
			var land = landings[Math.Min(i, landings.Count - 1)];
			last = CardMotion.Fly(card, land, 0.32f + i * 0.03f, lift: 0.3f, landed: () => _sounds.PlaySlap(land.Origin));
		}
		// Start listening before letting go: the cards may land before the fingers open.
		var down = last != null ? Await(last) : Task.CompletedTask;
		await Worker.Pinch(false);
		GoRest();
		await down;
	}

	/// <summary>Back to rest: the hand flies home and, at a place on the felt, lays itself flat.</summary>
	private void GoRest()
	{
		var flight = CardMotion.Fly(Worker, RestingPose, 0.32f, lift: 0.1f);
		if (_rest != null)
			flight.TweenCallback(Callable.From(() => Worker.Pose = HandPose.Rest));
	}

	/// <summary>Reaches for the first card, pinches it, and takes the others along (fixed to the hand).</summary>
	private async Task PickUp(IReadOnlyList<Card3D> cards)
	{
		Worker.Pose = HandPose.Relaxed;
		await Await(CardMotion.Fly(Worker, GrabFor(cards[0]), 0.24f, lift: 0.12f));
		await Worker.Pinch(true);
		foreach (var card in cards)
			card.Reparent(Worker, keepGlobalTransform: true);
	}

	private void Release(IReadOnlyList<Card3D> cards)
	{
		foreach (var card in cards)
			card.Reparent(_world, keepGlobalTransform: true);
	}

	/// <summary>Puts the working hand back at rest at once (a new deal).</summary>
	public void Reset()
	{
		Worker.Pose = _rest != null ? HandPose.Rest : HandPose.Relaxed;
		Worker.GlobalTransform = RestingPose;
		foreach (var child in Worker.GetChildren())
			if (child is Card3D card)
				card.Reparent(_world, keepGlobalTransform: true);
	}
}
