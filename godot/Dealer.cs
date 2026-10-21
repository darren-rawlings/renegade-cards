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

	/// <summary>
	/// Riffle shuffles the stack in place, a number of times: split it in two, riffle the halves
	/// back together a few cards at a time, square it up. Reorders the list to match.
	/// </summary>
	public async Task Riffle(List<Card3D> deck, Vector3 at, int times = 2)
	{
		for (int pass = 0; pass < times; pass++)
		{
			int cut = deck.Count / 2 + _random.Next(-3, 4);
			var left = deck.GetRange(0, cut);
			var right = deck.GetRange(cut, deck.Count - cut);

			// Split: each half slides out and turns in, its inner edge lifted for the riffle.
			Tween? split = null;
			for (int i = 0; i < left.Count; i++)
				split = CardMotion.Slide(left[i], Half(at, i, -1), 0.28f);
			for (int i = 0; i < right.Count; i++)
				split = CardMotion.Slide(right[i], Half(at, i, +1), 0.28f);
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
			Tween? riffle = null;
			for (int i = 0; i < order.Count; i++)
			{
				var card = order[i];
				int index = i;
				var target = InStack(at, index, yaw: ((float)_random.NextDouble() - 0.5f) * 0.1f);
				riffle = CardMotion.Fly(card, target, 0.11f, lift: 0.04f, delay: i * 0.021f, landed: () => _sounds.PlayFlick(card.GlobalPosition));
			}
			await _host.ToSignal(riffle!, Tween.SignalName.Finished);

			// Square up.
			Tween? square = null;
			for (int i = 0; i < order.Count; i++)
				square = CardMotion.Slide(order[i], InStack(at, i), 0.14f);
			await _host.ToSignal(square!, Tween.SignalName.Finished);
			_sounds.PlayKnock(at);
			deck.Clear();
			deck.AddRange(order);
		}
	}

	/// <summary>A card's place in one half of a split deck: off to one side, angled in, inner edge up.</summary>
	private static Transform3D Half(Vector3 at, int index, int side)
	{
		var basis = new Basis(Vector3.Up, -side * 0.32f) * new Basis(Vector3.Back, side * 0.12f) * Card3D.FaceDown();
		var position = at + new Vector3(side * 0.43f, CardThickness * (index + 1) + 0.03f, 0.04f);
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
