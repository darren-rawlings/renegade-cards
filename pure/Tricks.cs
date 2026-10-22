using System;
using System.Collections.Generic;
using System.Linq;

namespace Renegade.Cards;

/// <summary>One card played to a trick, and who played it.</summary>
public readonly record struct Play(int Seat, Card Card);

/// <summary>
/// The rules every trick-taking game shares (Hearts, Spades, Bridge): you must follow the suit
/// led if you can; the trick goes to the highest trump played, or else the highest card of the
/// suit led; aces are high.
/// </summary>
public static class Tricks
{
	/// <summary>A card's strength in trick-taking games: two low, ace high (2 to 14).</summary>
	public static int Value(Card card) => card.Rank == Card.Ace ? 14 : card.Rank;

	/// <summary>The cards that may be played: any card when leading, otherwise the suit led if you have it.</summary>
	public static List<Card> Following(IReadOnlyList<Card> hand, Suit? led)
	{
		if (led is { } suit && hand.Any(c => c.Suit == suit))
			return hand.Where(c => c.Suit == suit).ToList();
		return hand.ToList();
	}

	/// <summary>The play that wins a trick (the first card played sets the suit led).</summary>
	public static Play Winner(IReadOnlyList<Play> trick, Suit? trump = null)
	{
		if (trick.Count == 0)
			throw new ArgumentException("An empty trick has no winner.", nameof(trick));
		var led = trick[0].Card.Suit;
		var best = trick[0];
		foreach (var play in trick.Skip(1))
		{
			bool beats = play.Card.Suit == best.Card.Suit
				? Value(play.Card) > Value(best.Card)
				: trump is { } t && play.Card.Suit == t;
			if (beats)
				best = play;
		}
		return best;
	}

	/// <summary>Deals a deck round the table, one at a time from the given seat: hand[seat] lists each player's cards.</summary>
	public static List<Card>[] Deal(IReadOnlyList<Card> deck, int seats, int first)
	{
		var hands = Enumerable.Range(0, seats).Select(_ => new List<Card>()).ToArray();
		for (int i = 0; i < deck.Count; i++)
			hands[(first + i) % seats].Add(deck[i]);
		return hands;
	}

	/// <summary>Sorts a hand the way players hold it: by suit (alternating colours), high cards first in each.</summary>
	public static List<Card> Sorted(IEnumerable<Card> hand)
	{
		Suit[] order = { Suit.Spades, Suit.Hearts, Suit.Clubs, Suit.Diamonds };
		return hand.OrderBy(c => Array.IndexOf(order, c.Suit)).ThenByDescending(Value).ToList();
	}
}
