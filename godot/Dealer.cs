using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// The dealer's moves, acted out with real cards: a riffle shuffle (split, riffle together, square
/// up), dealing round the table, and spreading cards face down. The deck is a list of face-down
/// cards, bottom first, stacked at the centre of the table.
/// </summary>
public sealed class Dealer
{
	public const float CardThickness = 0.0035f;

	/// <summary>How far apart the riffled halves stay until the bridge pushes them together (along the cards' length).</summary>
	private const float Meshed = 0.09f;

	private readonly Node3D _host;
	private readonly CardSounds _sounds;
	private readonly Random _random;

	public Dealer(Node3D host, CardSounds sounds, Random random)
	{
		_host = host;
		_sounds = sounds;
		_random = random;
	}

	/// <summary>Where the card at a height in a face-down stack sits.</summary>
	public static Transform3D InStack(Vector3 at, int index, float yaw = 0) =>
		new(Card3D.FaceDown(yaw), at + Vector3.Up * (CardThickness * (index + 1)));

	/// <summary>Lays the deck out as a neat stack at once (no animation).</summary>
	public static void Stack(IReadOnlyList<Card3D> deck, Vector3 at)
	{
		for (int i = 0; i < deck.Count; i++)
			deck[i].GlobalTransform = InStack(at, i);
	}

	/// <summary>Where the shuffle happens: lifted off the felt and brought towards the near player.</summary>
	private static Vector3 Spot(Vector3 at) => at + new Vector3(0, 3.2f, 3.35f);

	/// <summary>The deck turned sideways (its long edge to the player) and tilted up to face them.</summary>
	private static Basis Sideways(float tilt = 0.55f) => new Basis(Vector3.Right, -tilt) * new Basis(Vector3.Up, Mathf.Pi / 2) * Card3D.FaceDown();

	/// <summary>
	/// Shuffles the stack in place, the way a dealer does in front of you: the deck is lifted and
	/// turned to face you, split, riffled together a few cards at a time, then bridged (arched up
	/// and let fall back in a cascade), and put back down squared up. Done a number of times.
	/// Reorders the list to match.
	/// </summary>
	public async Task Riffle(List<Card3D> deck, Vector3 at, int times = 2)
	{
		var spot = Spot(at);
		// Lift the deck and turn it towards the player.
		Tween? lift = null;
		for (int i = 0; i < deck.Count; i++)
			lift = CardMotion.Fly(deck[i], new Transform3D(Sideways(), spot - Sideways().Z * (CardThickness * i)), 0.35f, lift: 0.15f, delay: 0.004f * i);
		await _host.ToSignal(lift!, Tween.SignalName.Finished);

		for (int pass = 0; pass < times; pass++)
		{
			int cut = deck.Count / 2 + _random.Next(-3, 4);
			var left = deck.GetRange(0, cut);
			var right = deck.GetRange(cut, deck.Count - cut);

			// Split: each half moves out to its side, its inner end lifted for the riffle.
			Tween? split = null;
			for (int i = 0; i < left.Count; i++)
			{
				split = CardMotion.Slide(left[i], Half(spot, i, -1), 0.26f);
				// The thumb lifts the inner ends, bending the half up.
				left[i].BendTo(0, -0.11f, 0.26f, end: -1);
			}
			for (int i = 0; i < right.Count; i++)
			{
				split = CardMotion.Slide(right[i], Half(spot, i, +1), 0.26f);
				right[i].BendTo(0, -0.11f, 0.26f, end: +1);
			}
			await _host.ToSignal(split!, Tween.SignalName.Finished);

			// Riffle: the halves drop back into one stack, bottom up, in little runs from each side.
			var order = new List<Card3D>(deck.Count);
			int a = 0, b = 0;
			bool fromLeft = _random.Next(2) == 0;
			while (a < left.Count || b < right.Count)
			{
				int run = 1 + (_random.NextDouble() < 0.3 ? 1 : 0);
				for (int k = 0; k < run; k++)
				{
					if (fromLeft && a < left.Count)
						order.Add(left[a++]);
					else if (!fromLeft && b < right.Count)
						order.Add(right[b++]);
				}
				fromLeft = a >= left.Count ? false : b >= right.Count || !fromLeft;
			}
			var stacked = Sideways();
			Tween? riffle = null;
			for (int i = 0; i < order.Count; i++)
			{
				var card = order[i];
				// The halves mesh but stay a little apart, each overlapping the other by most of its length.
				int side = left.Contains(card) ? -1 : 1;
				var target = new Transform3D(stacked, spot - stacked.Z * (CardThickness * i) - stacked.Y * (side * Meshed));
				riffle = CardMotion.Fly(card, target, 0.1f, lift: 0.03f, delay: i * 0.02f, landed: () => _sounds.PlayFlick(card.GlobalPosition));
				// Let go: it springs straight as it drops.
				card.BendTo(0, 0, 0.09f, delay: i * 0.02f);
			}
			await _host.ToSignal(riffle!, Tween.SignalName.Finished);

			// The bridge: the meshed deck is bowed up as one block (fingers pressing down on the ends,
			// thumbs pushing up underneath the middle), then let go so the cards cascade down one after
			// another while the halves slide together into a neat stack.
			Tween? arch = null;
			var bowed = Sideways(0.68f);
			for (int i = 0; i < order.Count; i++)
			{
				int side = left.Contains(order[i]) ? -1 : 1;
				var raised = spot + Vector3.Up * 0.06f - bowed.Z * (CardThickness * i) - bowed.Y * (side * Meshed);
				arch = CardMotion.Slide(order[i], new Transform3D(bowed, raised), 0.3f);
				order[i].BendTo(-0.17f, 0, 0.3f);
			}
			await _host.ToSignal(arch!, Tween.SignalName.Finished);
			Tween? cascade = null;
			for (int i = order.Count - 1; i >= 0; i--)
			{
				var card = order[i];
				int k = order.Count - 1 - i;
				var target = new Transform3D(stacked, spot - stacked.Z * (CardThickness * i));
				cascade = CardMotion.Slide(card, target, 0.1f, delay: k * 0.014f, landed: k % 2 == 0 ? () => _sounds.PlayFlick(card.GlobalPosition) : null);
				card.BendTo(0, 0, 0.1f, delay: k * 0.014f);
			}
			await _host.ToSignal(cascade!, Tween.SignalName.Finished);
			_sounds.PlayKnock(spot);
			deck.Clear();
			deck.AddRange(order);
		}

		// Put the squared-up deck back down in the middle of the table.
		Tween? down = null;
		for (int i = 0; i < deck.Count; i++)
			down = CardMotion.Fly(deck[i], InStack(at, i), 0.32f, lift: 0.1f, delay: 0.003f * (deck.Count - i));
		await _host.ToSignal(down!, Tween.SignalName.Finished);
		_sounds.PlayKnock(at);
	}

	/// <summary>A card's place in one half of a split deck: off to one side, its inner end lifted.</summary>
	private static Transform3D Half(Vector3 spot, int index, int side)
	{
		var basis = new Basis(Vector3.Back, side * 0.32f) * Sideways();
		var position = spot + new Vector3(side * 0.5f, 0.02f, 0) - basis.Z * (CardThickness * index);
		return new Transform3D(basis, position);
	}

	/// <summary>
	/// Deals a card from the top of a stack to a pile, landing with a slap. Returns the flight.
	/// <paramref name="onPile"/> is how many cards that pile already holds.
	/// </summary>
	public Tween Deal(Card3D card, Vector3 pile, int onPile, float delay)
	{
		var target = InStack(pile, onPile, yaw: ((float)_random.NextDouble() - 0.5f) * 0.16f);
		var tween = card.CreateTween();
		tween.TweenInterval(delay);
		tween.TweenCallback(Callable.From(() => _sounds.PlaySwish(card.GlobalPosition)));
		var flight = CardMotion.Fly(card, target, 0.3f, lift: 0.35f, delay: delay, landed: () => _sounds.PlaySlap(target.Origin));
		return flight;
	}

	/// <summary>A spot for a face-down card spread loosely over an oval (the Go Fish pond, say).</summary>
	public Transform3D Scatter(Vector3 centre, float radiusX, float radiusZ, int index)
	{
		float angle = (float)_random.NextDouble() * Mathf.Tau;
		float r = Mathf.Sqrt((float)_random.NextDouble());
		var at = centre + new Vector3(Mathf.Cos(angle) * radiusX * r, 0.0012f * (index + 1), Mathf.Sin(angle) * radiusZ * r);
		return new Transform3D(Card3D.FaceDown((float)_random.NextDouble() * Mathf.Tau), at);
	}
}
