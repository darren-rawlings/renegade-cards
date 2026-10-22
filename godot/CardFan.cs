using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// A hand of cards held as a fan: spread in an arc from the holding point, each card a little in
/// front of the one before, so every top-left index shows. Your fan is sorted by rank; the
/// other player's is held with the backs towards you.
/// </summary>
public sealed class CardFan
{
	private readonly List<Card3D> _cards = new();
	private readonly HashSet<int> _raised = new();
	private readonly HashSet<Card3D> _raisedCards = new();

	public CardFan(Transform3D pivot, bool sorted)
	{
		Pivot = pivot;
		Sorted = sorted;
	}

	public Transform3D Pivot { get; set; }
	public bool Sorted { get; }

	/// <summary>Degrees between neighbouring cards, at most; big hands squeeze together.</summary>
	public float Spread { get; set; } = 11f;

	/// <summary>The widest the whole fan opens, in degrees.</summary>
	public float MaxArc { get; set; } = 96f;

	/// <summary>How far each card's centre sits from the holding point.</summary>
	public float Radius { get; set; } = 0.62f;

	public IReadOnlyList<Card3D> Cards => _cards;
	public int Count => _cards.Count;

	public void Add(Card3D card)
	{
		_cards.Add(card);
		Order();
	}

	public void Remove(Card3D card)
	{
		_cards.Remove(card);
		_raised.Remove(card.Card.Rank);
		_raisedCards.Remove(card);
	}

	public void Clear()
	{
		_cards.Clear();
		_raised.Clear();
	}

	public IEnumerable<Card3D> OfRank(int rank) => _cards.Where(c => c.Card.Rank == rank).ToList();

	/// <summary>Puts an unsorted fan's cards in a new order (a player shuffling their hand); call Settle after.</summary>
	public void Reorder(IEnumerable<Card3D> order)
	{
		var wanted = order.Where(_cards.Contains).ToList();
		_cards.Clear();
		_cards.AddRange(wanted);
	}

	/// <summary>Lifts every card of a rank a little out of the fan (the ones being asked about), or lowers them.</summary>
	public void Raise(int rank, bool raised)
	{
		if (raised)
			_raised.Add(rank);
		else
			_raised.Remove(rank);
	}

	public void LowerAll()
	{
		_raised.Clear();
		_raisedCards.Clear();
	}

	/// <summary>Lifts one card out of the fan (picked to pass, say), or lowers it.</summary>
	public void RaiseCard(Card3D card, bool raised)
	{
		if (raised)
			_raisedCards.Add(card);
		else
			_raisedCards.Remove(card);
	}

	public bool IsRaised(Card3D card) => _raisedCards.Contains(card);

	public IReadOnlyCollection<Card3D> RaisedCards => _raisedCards;

	private void Order()
	{
		if (Sorted)
			_cards.Sort((a, b) => a.Card.Rank != b.Card.Rank ? a.Card.Rank.CompareTo(b.Card.Rank) : a.Card.Suit.CompareTo(b.Card.Suit));
	}

	/// <summary>Where a card at an index sits, for a fan of a given size.</summary>
	public Transform3D Slot(int index, int count, bool raised = false)
	{
		float step = count > 1 ? Mathf.Min(Spread, MaxArc / (count - 1)) : 0;
		float angle = Mathf.DegToRad((index - (count - 1) / 2f) * step);
		var basis = Pivot.Basis * new Basis(Vector3.Back, -angle);
		var position = Pivot.Origin + basis * new Vector3(0, Radius + (raised ? 0.16f : 0f), 0.004f * index);
		return new Transform3D(basis, position);
	}

	/// <summary>Where a new card would go (so it can fly straight to its place).</summary>
	public Transform3D SlotFor(Card3D card)
	{
		var preview = _cards.Append(card).ToList();
		if (Sorted)
			preview.Sort((a, b) => a.Card.Rank != b.Card.Rank ? a.Card.Rank.CompareTo(b.Card.Rank) : a.Card.Suit.CompareTo(b.Card.Suit));
		return Slot(preview.IndexOf(card), preview.Count);
	}

	/// <summary>Moves every card to its place (after cards come or go). Returns the longest tween.</summary>
	public Tween? Settle(float seconds = 0.22f)
	{
		Tween? last = null;
		for (int i = 0; i < _cards.Count; i++)
			last = CardMotion.Slide(_cards[i], Slot(i, _cards.Count, _raised.Contains(_cards[i].Card.Rank) || _raisedCards.Contains(_cards[i])), seconds);
		return last;
	}

	/// <summary>The card a tap lands on (the frontmost one), if any.</summary>
	public Card3D? At(Vector3 origin, Vector3 direction)
	{
		Card3D? best = null;
		float bestDistance = float.MaxValue;
		foreach (var card in _cards)
			if (card.Hit(origin, direction) is { } d && d < bestDistance - 0.001f)
			{
				best = card;
				bestDistance = d;
			}
		return best;
	}
}
