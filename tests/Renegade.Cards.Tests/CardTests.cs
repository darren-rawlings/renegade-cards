using System;
using System.Linq;
using Renegade.Cards;
using Xunit;

namespace Renegade.Cards.Tests;

public class CardTests
{
	[Fact]
	public void ADeckHasEveryCardOnce()
	{
		var deck = Deck.Standard();
		Assert.Equal(52, deck.Count);
		Assert.Equal(52, deck.Distinct().Count());
		Assert.Equal(13, deck.Count(c => c.Suit == Suit.Hearts));
		Assert.All(Enumerable.Range(0, 52), i => Assert.Equal(i, Card.FromOrdinal(i).Ordinal));
	}

	[Fact]
	public void AShuffleKeepsTheCardsAndChangesTheOrder()
	{
		var shuffled = Deck.Shuffled(new Random(7));
		Assert.Equal(Deck.Standard().OrderBy(c => c.Ordinal), shuffled.OrderBy(c => c.Ordinal));
		Assert.NotEqual(Deck.Standard(), shuffled);
		Assert.Equal(shuffled, Deck.Shuffled(new Random(7)));
	}

	[Fact]
	public void TheShuffleIsFair()
	{
		// Over many shuffles, the ace of spades lands in each of the 52 places about equally often.
		var counts = new int[52];
		var random = new Random(1);
		var target = new Card(Card.Ace, Suit.Spades);
		for (int n = 0; n < 52_000; n++)
			counts[Deck.Shuffled(random).IndexOf(target)]++;
		Assert.All(counts, c => Assert.InRange(c, 800, 1200));
	}

	[Theory]
	[InlineData(1, "A", "Ace", "Aces")]
	[InlineData(6, "6", "Six", "Sixes")]
	[InlineData(10, "10", "Ten", "Tens")]
	[InlineData(12, "Q", "Queen", "Queens")]
	public void RanksHaveAnIndexAndAName(int rank, string index, string one, string many)
	{
		Assert.Equal(index, Card.RankIndex(rank));
		Assert.Equal(one, Card.RankName(rank));
		Assert.Equal(many, Card.RankName(rank, plural: true));
	}

	[Fact]
	public void HeartsAndDiamondsAreRed()
	{
		Assert.True(new Card(5, Suit.Hearts).IsRed);
		Assert.True(new Card(5, Suit.Diamonds).IsRed);
		Assert.False(new Card(5, Suit.Spades).IsRed);
		Assert.True(new Card(Card.Jack, Suit.Clubs).IsFace);
		Assert.Equal("10♥", new Card(10, Suit.Hearts).ToString());
	}
}
