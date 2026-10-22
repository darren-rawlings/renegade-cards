using System.Linq;
using Renegade.Cards;
using Xunit;

namespace Renegade.Cards.Tests;

public class TrickTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	[Fact]
	public void YouMustFollowSuitIfYouCan()
	{
		var hand = new[] { C(5, Suit.Hearts), C(9, Suit.Spades), C(2, Suit.Hearts) };
		Assert.Equal(2, Tricks.Following(hand, Suit.Hearts).Count);
		Assert.Equal(3, Tricks.Following(hand, Suit.Clubs).Count);
		Assert.Equal(3, Tricks.Following(hand, null).Count);
	}

	[Fact]
	public void TheHighestOfTheSuitLedWinsAndAcesAreHigh()
	{
		var trick = new[] { new Play(0, C(10, Suit.Clubs)), new Play(1, C(Card.Ace, Suit.Clubs)), new Play(2, C(Card.King, Suit.Hearts)), new Play(3, C(3, Suit.Clubs)) };
		Assert.Equal(1, Tricks.Winner(trick).Seat);
	}

	[Fact]
	public void AnyTrumpBeatsTheSuitLed()
	{
		var trick = new[] { new Play(0, C(Card.Ace, Suit.Clubs)), new Play(1, C(2, Suit.Spades)), new Play(2, C(3, Suit.Spades)), new Play(3, C(Card.King, Suit.Clubs)) };
		Assert.Equal(2, Tricks.Winner(trick, Suit.Spades).Seat);
		Assert.Equal(0, Tricks.Winner(trick).Seat);
	}

	[Fact]
	public void DealingRoundTheTableGivesThirteenEach()
	{
		var hands = Tricks.Deal(Deck.Standard(), 4, first: 1);
		Assert.All(hands, h => Assert.Equal(13, h.Count));
		Assert.Equal(Deck.Standard()[0], hands[1][0]);
		var sorted = Tricks.Sorted(hands[0]);
		Assert.Equal(13, sorted.Distinct().Count());
	}
}
