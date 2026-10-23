using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Renegade.Cards;

/// <summary>The ways of shuffling the Shuffle Lab can show.</summary>
public enum ShuffleStyle { RiffleBridge, TableRiffle, Overhand, Hindu, Wash, Pile, Faro }

/// <summary>
/// Alternative shuffles, acted out with real cards (and, where hands do the work, the dealer's
/// glass hands), for choosing how the card games should shuffle. Each takes a face-down deck
/// stacked at a point on the felt (bottom first), shuffles it on screen, reorders the list to
/// match, and leaves it squared up where it began.
/// </summary>
public sealed class Shuffles
{
	private readonly Node3D _host;
	private readonly CardSounds _sounds;
	private readonly Random _random;
	private readonly Dealer _dealer;
	private readonly Vector3 _eye;

	public Shuffles(Node3D host, CardSounds sounds, Random random, Vector3 eye, GlassHand left, GlassHand right)
	{
		_host = host;
		_sounds = sounds;
		_random = random;
		_eye = eye;
		_dealer = new Dealer(host, sounds, random);
		Left = left;
		Right = right;
	}

	/// <summary>The dealer's hands (the lab moves them; they rest off to the sides between shuffles).</summary>
	public GlassHand Left { get; }
	public GlassHand Right { get; }

	public static string Name(ShuffleStyle style) => style switch
	{
		ShuffleStyle.RiffleBridge => "Riffle and bridge (now)",
		ShuffleStyle.TableRiffle => "Table riffle",
		ShuffleStyle.Overhand => "Overhand",
		ShuffleStyle.Hindu => "Hindu",
		ShuffleStyle.Wash => "Wash",
		ShuffleStyle.Pile => "Pile shuffle",
		_ => "Faro weave",
	};

	public static string About(ShuffleStyle style) => style switch
	{
		ShuffleStyle.RiffleBridge => "What the games do now: the deck is lifted towards you, split, the halves curl under the thumbs and riffle together, then the bridge bows the deck and lets it cascade flat.",
		ShuffleStyle.TableRiffle => "The casino way: the halves lie on the felt corner to corner, the inner corners lift, and the cards riffle down into each other before being pushed square.",
		ShuffleStyle.Overhand => "Held on its side in one hand while the other thumb pulls small packets off the front, again and again until the whole deck has moved across.",
		ShuffleStyle.Hindu => "Held by the ends from above while the other hand draws packets off the top, letting them fall into its palm.",
		ShuffleStyle.Wash => "Every card spread face down on the felt and swirled round under both palms, then gathered up into a pile.",
		ShuffleStyle.Pile => "Dealt one at a time into five piles, then the piles stacked together.",
		_ => "Two halves pushed end to end so the cards weave together perfectly, then bowed and released.",
	};

	private async Task Done(Tween? tween)
	{
		if (tween != null && tween.IsValid() && tween.IsRunning())
			await _host.ToSignal(tween, Tween.SignalName.Finished);
	}

	private async Task Wait(float seconds) => await _host.ToSignal(_host.GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);

	public async Task Play(ShuffleStyle style, List<Card3D> deck, Vector3 at)
	{
		switch (style)
		{
			case ShuffleStyle.RiffleBridge: await _dealer.Riffle(deck, at, times: 1, Left, Right); break;
			case ShuffleStyle.TableRiffle: await TableRiffle(deck, at); break;
			case ShuffleStyle.Overhand: await Overhand(deck, at); break;
			case ShuffleStyle.Hindu: await Hindu(deck, at); break;
			case ShuffleStyle.Wash: await Wash(deck, at); break;
			case ShuffleStyle.Pile: await Pile(deck, at); break;
			default: await Faro(deck, at); break;
		}
	}

	// ------------------------------------------------------------------ Helpers

	/// <summary>Where an in-the-air shuffle happens: lifted towards the player, as the riffle does.</summary>
	private static Vector3 Air(Vector3 at) => at + new Vector3(0, 3.2f, 3.35f);

	/// <summary>Turns a card to face (its back) towards the player, standing; landscape turns it on its long side.</summary>
	private Basis Standing(Vector3 where, bool landscape, float tilt = 0)
	{
		var toEye = (_eye - where).Normalized();
		var basis = Basis.LookingAt(toEye, Vector3.Up); // -Z (the back) towards the eye
		if (landscape)
			basis *= new Basis(Vector3.Back, Mathf.Pi / 2);
		return basis * new Basis(Vector3.Right, tilt);
	}

	private async Task Square(List<Card3D> deck, Vector3 at)
	{
		Tween? down = null;
		for (int i = 0; i < deck.Count; i++)
			down = CardMotion.Fly(deck[i], Dealer.InStack(at, i), 0.32f, lift: 0.1f, delay: 0.003f * (deck.Count - i));
		await Done(down);
		_sounds.PlayKnock(at);
	}

	private Tween HandTo(GlassHand hand, Transform3D to, float seconds = 0.3f) => CardMotion.Fly(hand, to, seconds, lift: 0.05f);

	/// <summary>A hand placed relative to a point, palm facing a direction, fingers along another.</summary>
	private static Transform3D Hand(Vector3 palmAt, Vector3 palm, Vector3 fingers)
	{
		var y = fingers.Normalized();
		var z = palm.Normalized();
		var x = y.Cross(z).Normalized();
		z = x.Cross(y).Normalized();
		return new Transform3D(new Basis(x, y, z), palmAt);
	}

	public void Rest(Vector3 at)
	{
		Left.Pose = HandPose.Rest;
		Right.Pose = HandPose.Rest;
		Left.GlobalTransform = Hand(at + new Vector3(-1.3f, 0.12f, 1.4f), Vector3.Down, new Vector3(0.3f, 0, -1));
		Right.GlobalTransform = Hand(at + new Vector3(1.3f, 0.12f, 1.4f), Vector3.Down, new Vector3(-0.3f, 0, -1));
	}

	private async Task ToRest(Vector3 at)
	{
		Left.Pose = HandPose.Relaxed;
		Right.Pose = HandPose.Relaxed;
		var a = HandTo(Left, Hand(at + new Vector3(-1.3f, 0.12f, 1.4f), Vector3.Down, new Vector3(0.3f, 0, -1)), 0.35f);
		var b = HandTo(Right, Hand(at + new Vector3(1.3f, 0.12f, 1.4f), Vector3.Down, new Vector3(-0.3f, 0, -1)), 0.35f);
		await Done(a);
		await Done(b);
		Left.Pose = HandPose.Rest;
		Right.Pose = HandPose.Rest;
	}

	private List<int> Packets(int count, int min, int max)
	{
		var sizes = new List<int>();
		while (count > 0)
		{
			int n = Math.Min(count, _random.Next(min, max + 1));
			sizes.Add(n);
			count -= n;
		}
		return sizes;
	}

	// ------------------------------------------------------------------ Table riffle

	private async Task TableRiffle(List<Card3D> deck, Vector3 at)
	{
		int cut = deck.Count / 2 + _random.Next(-3, 4);
		var left = deck.GetRange(0, cut);
		var right = deck.GetRange(cut, deck.Count - cut);
		// The halves slide apart on the felt, angled so their inner corners nearly meet.
		Transform3D Half(int side, int i) =>
			new(Card3D.FaceDown(Mathf.Pi / 2 + side * 0.38f), at + new Vector3(side * 0.55f, Dealer.CardThickness * (i + 1), 0.15f));
		Tween? split = null;
		for (int i = 0; i < left.Count; i++)
			split = CardMotion.Slide(left[i], Half(-1, i), 0.3f, delay: 0.002f * i);
		for (int i = 0; i < right.Count; i++)
			split = CardMotion.Slide(right[i], Half(+1, i), 0.3f, delay: 0.002f * i);
		// Palms come down on the halves.
		var lh = HandTo(Left, Hand(at + new Vector3(-0.75f, 0.3f, 0.45f), Vector3.Down, new Vector3(0.6f, 0, -1)));
		var rh = HandTo(Right, Hand(at + new Vector3(0.75f, 0.3f, 0.45f), Vector3.Down, new Vector3(-0.6f, 0, -1)));
		await Done(split);
		await Done(rh);
		// Thumbs lift the inner corners: every card curls up at that end.
		Tween? lift = null;
		foreach (var card in left)
			lift = card.BendTo(0, -0.16f, 0.25f, end: -1);
		foreach (var card in right)
			lift = card.BendTo(0, -0.16f, 0.25f, end: +1);
		await Done(lift);
		await Wait(0.15f);
		// Riffle: cards drop from each side in turn, bottom first, interleaving where the corners overlap.
		var order = new List<Card3D>(deck.Count);
		int a = 0, b = 0;
		bool fromLeft = _random.Next(2) == 0;
		while (a < left.Count || b < right.Count)
		{
			int run = 1 + (_random.NextDouble() < 0.35 ? 1 : 0);
			for (int k = 0; k < run; k++)
			{
				if (fromLeft && a < left.Count)
					order.Add(left[a++]);
				else if (!fromLeft && b < right.Count)
					order.Add(right[b++]);
			}
			fromLeft = a >= left.Count ? false : b >= right.Count || !fromLeft;
		}
		Tween? drop = null;
		for (int i = 0; i < order.Count; i++)
		{
			var card = order[i];
			int side = left.Contains(card) ? -1 : +1;
			var target = new Transform3D(Card3D.FaceDown(Mathf.Pi / 2 + side * 0.38f), at + new Vector3(side * 0.3f, Dealer.CardThickness * (i + 1), 0.15f));
			drop = CardMotion.Slide(card, target, 0.1f, delay: i * 0.03f, landed: i % 2 == 0 ? () => _sounds.PlayFlick(card.GlobalPosition) : null);
			card.BendTo(0, 0, 0.1f, delay: i * 0.03f);
		}
		await Done(drop);
		// Push the interleaved halves together and square them.
		HandTo(Left, Hand(at + new Vector3(-0.45f, 0.3f, 0.3f), Vector3.Right, Vector3.Forward), 0.25f);
		HandTo(Right, Hand(at + new Vector3(0.45f, 0.3f, 0.3f), Vector3.Left, Vector3.Forward), 0.25f);
		deck.Clear();
		deck.AddRange(order);
		await Square(deck, at);
		await ToRest(at);
	}

	// ------------------------------------------------------------------ Cards held in the hands

	/// <summary>A deck lying face down in the air, its long edge to the player, tipped towards them.</summary>
	private static Basis Lying(float tilt = 0.6f) =>
		new Basis(Vector3.Right, -tilt) * new Basis(Vector3.Up, Mathf.Pi / 2) * Card3D.FaceDown();

	/// <summary>A hand under a card (or a pile whose bottom card this is), palm up against it, fingers away from the player.</summary>
	private static Transform3D PalmUnder(Transform3D card, float drop)
	{
		var back = -card.Basis.Z.Normalized();
		var fingers = (Vector3.Forward - back * Vector3.Forward.Dot(back)).Normalized();
		var x = fingers.Cross(back).Normalized();
		return new Transform3D(new Basis(x, fingers, back), card.Origin - back * drop);
	}

	/// <summary>Cards picked up by a hand move with it (until let go).</summary>
	private static void Attach(IEnumerable<Card3D> cards, Node3D hand)
	{
		foreach (var card in cards)
			card.Reparent(hand, keepGlobalTransform: true);
	}

	private void Detach(IEnumerable<Card3D> cards)
	{
		foreach (var card in cards)
			card.Reparent(_host, keepGlobalTransform: true);
	}

	/// <summary>Where a hand must go so that a card it holds ends up at a place.</summary>
	private static Transform3D Bring(GlassHand hand, Card3D held, Transform3D place) =>
		place * (hand.GlobalTransform.AffineInverse() * held.GlobalTransform).AffineInverse();

	// ------------------------------------------------------------------ Overhand

	private async Task Overhand(List<Card3D> deck, Vector3 at)
	{
		var spot = Air(at);
		var from = spot + new Vector3(0.34f, 0.05f, 0);
		var into = spot + new Vector3(-0.3f, -0.05f, 0);
		var basis = Standing(spot, landscape: true, tilt: -0.3f);
		var toward = -basis.Z; // the backs, towards the player
		var up = (Vector3.Up - toward * Vector3.Up.Dot(toward)).Normalized(); // along the card, up to its top edge
		// A hand over a standing pile: on its top edge, palm down, the back of the hand to the player.
		Transform3D OnEdge(Vector3 centre, int thick) =>
			Hand(centre + up * (Card3D.Width / 2 + 0.11f) + toward * (Dealer.CardThickness * thick * 0.5f), -up, -toward);
		Tween? lift = null;
		for (int i = 0; i < deck.Count; i++)
			lift = CardMotion.Fly(deck[i], new Transform3D(basis, from + toward * (Dealer.CardThickness * i)), 0.4f, lift: 0.2f, delay: 0.003f * i);
		Right.Pose = HandPose.Hold;
		Left.Pose = HandPose.Hold;
		HandTo(Right, OnEdge(from, deck.Count), 0.4f);
		HandTo(Left, OnEdge(into, 0), 0.4f);
		await Done(lift);
		Attach(deck, Right);
		var source = deck.ToList(); // back to front: the last is nearest the player
		var pile = new List<Card3D>(); // back to front
		foreach (int size in Packets(deck.Count, 4, 8))
		{
			// The right hand brings the deck across, its front against the left hand's pile.
			var front = source[^1];
			var meet = new Transform3D(basis, into + toward * (Dealer.CardThickness * (pile.Count + size + 1)));
			await Done(CardMotion.Fly(Right, Bring(Right, front, meet), 0.2f, lift: 0.04f));
			// The left thumb holds the front packet; the right hand lifts the rest away and back.
			var packet = source.Skip(source.Count - size).ToList();
			source.RemoveRange(source.Count - size, size);
			Detach(packet);
			Attach(packet, Left);
			// The packet lands on the front of the left pile, keeping its order.
			for (int k = 0; k < packet.Count; k++)
			{
				pile.Add(packet[k]);
				CardMotion.Slide(packet[k], new Transform3D(basis, into + toward * (Dealer.CardThickness * pile.Count)), 0.12f);
			}
			_sounds.PlayFlick(into);
			if (source.Count > 0)
				await Done(CardMotion.Fly(Right, Bring(Right, source[0], new Transform3D(basis, from)), 0.22f, lift: 0.05f));
			await Wait(0.06f);
		}
		Detach(pile);
		// The pile, back to front, is the new deck bottom to top.
		deck.Clear();
		deck.AddRange(pile);
		await Square(deck, at);
		await ToRest(at);
	}

	// ------------------------------------------------------------------ Hindu

	private async Task Hindu(List<Card3D> deck, Vector3 at)
	{
		var spot = Air(at);
		var basis = Lying(0.6f);
		var n = -basis.Z; // up from the deck's back
		var hold = spot + new Vector3(0.34f, 0.12f, -0.05f);
		var palm = spot + new Vector3(-0.22f, -0.12f, 0.08f);
		Transform3D OnPile(int height) => new(basis, palm + n * (Dealer.CardThickness * height));
		// Up into the right hand, held from above by the ends; the left hand waits beneath, palm up.
		Tween? lift = null;
		for (int i = 0; i < deck.Count; i++)
			lift = CardMotion.Fly(deck[i], new Transform3D(basis, hold + n * (Dealer.CardThickness * i)), 0.4f, lift: 0.2f, delay: 0.003f * i);
		Right.Pose = HandPose.Hold;
		Left.Pose = HandPose.Relaxed;
		HandTo(Right, Dealer.PalmOn(new Transform3D(basis, hold + n * (Dealer.CardThickness * deck.Count)), 0.12f), 0.4f);
		HandTo(Left, PalmUnder(OnPile(0), 0.1f), 0.4f);
		await Done(lift);
		Attach(deck, Right);
		var source = deck.ToList(); // bottom first
		var pile = new List<Card3D>();
		foreach (int size in Packets(deck.Count, 4, 9))
		{
			// The deck dips into the left palm, its bottom just above the pile.
			var bottom = source[0];
			await Done(CardMotion.Fly(Right, Bring(Right, bottom, OnPile(pile.Count + 1)), 0.18f, lift: 0.03f));
			// The left fingers keep the top packet; the right hand draws the rest away from underneath it.
			var packet = source.Skip(source.Count - size).ToList();
			source.RemoveRange(source.Count - size, size);
			Detach(packet);
			if (source.Count > 0)
				CardMotion.Fly(Right, Bring(Right, source[0], new Transform3D(basis, hold)), 0.22f, lift: 0.05f);
			// The packet drops onto the pile.
			Tween? fall = null;
			for (int k = 0; k < packet.Count; k++)
			{
				pile.Add(packet[k]);
				fall = CardMotion.Slide(packet[k], OnPile(pile.Count), 0.12f, delay: 0.05f);
			}
			_sounds.PlayFlick(palm);
			await Done(fall);
			await Wait(0.1f);
		}
		deck.Clear();
		deck.AddRange(pile);
		await Square(deck, at);
		await ToRest(at);
	}

	// ------------------------------------------------------------------ Wash

	private async Task Wash(List<Card3D> deck, Vector3 at)
	{
		// Spread every card loosely over an oval, face down, at random angles.
		var spots = deck.Select((_, i) => _dealer.Scatter(at + new Vector3(0, 0, 0.3f), 1.35f, 0.85f, i)).ToList();
		Tween? spread = null;
		for (int i = 0; i < deck.Count; i++)
			spread = CardMotion.Slide(deck[i], spots[i], 0.35f, delay: 0.004f * i);
		Left.Pose = HandPose.Rest;
		Right.Pose = HandPose.Rest;
		await Done(spread);
		// Both palms swirl the cards round: each card drifts on its own little circle, turning.
		var phase = deck.Select(_ => (float)_random.NextDouble() * Mathf.Tau).ToList();
		var origin = deck.Select(c => c.GlobalPosition).ToList();
		var spin = deck.Select(c => c.GlobalTransform.Basis.GetEuler().Y).ToList();
		const float seconds = 3.6f;
		var tween = _host.CreateTween();
		tween.TweenMethod(Callable.From<float>(t =>
		{
			float a = t * Mathf.Tau * 1.4f;
			for (int i = 0; i < deck.Count; i++)
			{
				var drift = new Vector3(Mathf.Cos(a + phase[i]), 0, Mathf.Sin(a + phase[i])) * 0.28f;
				var p = origin[i] + drift;
				deck[i].GlobalTransform = new Transform3D(Card3D.FaceDown(spin[i] + a * 0.35f * (i % 2 == 0 ? 1 : -1)), p);
			}
			var l = at + new Vector3(-0.6f + Mathf.Cos(a) * 0.45f, 0.14f, 0.3f + Mathf.Sin(a) * 0.35f);
			var r = at + new Vector3(0.6f + Mathf.Cos(a + Mathf.Pi) * 0.45f, 0.14f, 0.3f + Mathf.Sin(a + Mathf.Pi) * 0.35f);
			Left.GlobalTransform = Hand(l, Vector3.Down, new Vector3(Mathf.Sin(a), 0, -1));
			Right.GlobalTransform = Hand(r, Vector3.Down, new Vector3(-Mathf.Sin(a), 0, -1));
		}), 0f, 1f, seconds);
		for (int k = 0; k < 6; k++)
			_host.GetTree().CreateTimer(k * 0.4f).Timeout += () => _sounds.PlaySwish(at);
		await Done(tween);
		// Gathered up into a pile, in a fresh random order.
		var order = deck.OrderBy(_ => _random.Next()).ToList();
		deck.Clear();
		deck.AddRange(order);
		await Square(deck, at);
		await ToRest(at);
	}

	// ------------------------------------------------------------------ Pile shuffle

	private async Task Pile(List<Card3D> deck, Vector3 at)
	{
		const int piles = 5;
		var place = Enumerable.Range(0, piles).Select(p => at + new Vector3(-1.2f + p * 0.6f, 0, 0.55f)).ToList();
		var stacks = Enumerable.Range(0, piles).Select(_ => new List<Card3D>()).ToList();
		var top = deck.AsEnumerable().Reverse().ToList();
		Tween? last = null;
		for (int i = 0; i < top.Count; i++)
		{
			int p = i % piles;
			last = _dealer.Deal(top[i], place[p], stacks[p].Count, delay: i * 0.07f);
			stacks[p].Add(top[i]);
		}
		await Done(last);
		await Wait(0.3f);
		// The piles are picked up one on another, left to right.
		var order = new List<Card3D>();
		foreach (var stack in stacks)
			order.AddRange(stack);
		deck.Clear();
		deck.AddRange(order);
		await Square(deck, at);
	}

	// ------------------------------------------------------------------ Faro

	private async Task Faro(List<Card3D> deck, Vector3 at)
	{
		var spot = Air(at);
		var basis = Lying(0.6f);
		var n = -basis.Z;
		var along = basis.Y; // the cards' length (towards the left): the halves meet end to end along it
		int half = deck.Count / 2;
		var low = deck.GetRange(0, half);
		var high = deck.GetRange(half, deck.Count - half);
		Transform3D Place(int side, float apart, int height) => new(basis, spot + along * (side * -apart) + n * (Dealer.CardThickness * height));
		// The halves lie end to end, a hand on each from above, holding it by its outer end.
		Tween? lift = null;
		for (int i = 0; i < low.Count; i++)
			lift = CardMotion.Fly(low[i], Place(-1, 0.6f, i), 0.4f, lift: 0.2f, delay: 0.003f * i);
		for (int i = 0; i < high.Count; i++)
			lift = CardMotion.Fly(high[i], Place(1, 0.6f, i), 0.4f, lift: 0.2f, delay: 0.003f * i);
		Left.Pose = HandPose.Hold;
		Right.Pose = HandPose.Hold;
		HandTo(Left, Dealer.PalmOn(Place(-1, 0.78f, low.Count), 0.12f), 0.4f);
		HandTo(Right, Dealer.PalmOn(Place(1, 0.78f, high.Count), 0.12f), 0.4f);
		await Done(lift);
		// Pushed end to end, the halves weave: one card from each in turn.
		var order = new List<Card3D>();
		for (int i = 0; i < Math.Max(low.Count, high.Count); i++)
		{
			if (i < low.Count)
				order.Add(low[i]);
			if (i < high.Count)
				order.Add(high[i]);
		}
		Tween? weave = null;
		for (int i = 0; i < order.Count; i++)
			weave = CardMotion.Slide(order[i], Place(low.Contains(order[i]) ? -1 : 1, 0.16f, i), 0.9f, delay: i * 0.004f);
		HandTo(Left, Dealer.PalmOn(Place(-1, 0.34f, order.Count), 0.12f), 0.9f);
		HandTo(Right, Dealer.PalmOn(Place(1, 0.34f, order.Count), 0.12f), 0.9f);
		await Done(weave);
		_sounds.PlayFlick(spot);
		// Squeezed the rest of the way together before the bow.
		Tween? squeeze = null;
		for (int i = 0; i < order.Count; i++)
			squeeze = CardMotion.Slide(order[i], Place(low.Contains(order[i]) ? -1 : 1, 0.035f, i), 0.25f);
		HandTo(Left, Dealer.PalmOn(Place(-1, 0.3f, order.Count), 0.12f), 0.25f);
		HandTo(Right, Dealer.PalmOn(Place(1, 0.3f, order.Count), 0.12f), 0.25f);
		await Done(squeeze);
		// Bowed up as one block, the hands pressing down on the ends, then released to cascade together.
		Tween? bow = null;
		foreach (var card in order)
			bow = card.BendTo(-0.15f, 0, 0.28f);
		await Done(bow);
		Tween? cascade = null;
		for (int i = order.Count - 1; i >= 0; i--)
		{
			int k = order.Count - 1 - i;
			var card = order[i];
			cascade = CardMotion.Slide(card, Place(0, 0, i), 0.1f, delay: k * 0.014f,
				landed: k % 2 == 0 ? () => _sounds.PlayFlick(card.GlobalPosition) : null);
			card.BendTo(0, 0, 0.1f, delay: k * 0.014f);
		}
		await Done(cascade);
		deck.Clear();
		deck.AddRange(order);
		await Square(deck, at);
		await ToRest(at);
	}
}
